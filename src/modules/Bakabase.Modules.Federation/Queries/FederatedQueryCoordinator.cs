using Bakabase.Modules.Federation.Contracts;

namespace Bakabase.Modules.Federation.Queries;

/// <summary>Freezes the participant set, then merges immutable local sequences. No resource data is persisted.</summary>
public sealed class FederatedQueryCoordinator : IDisposable
{
    private sealed class StreamState
    {
        public required PeerSearchTarget Target { get; init; }
        public required string SnapshotId { get; init; }
        public required long Total { get; init; }
        public FederatedResourceSummary[] Block { get; set; } = [];
        public int Position { get; set; }
        public int Consumed { get; set; }
        public string? NextCursor { get; set; }
        public FederatedResourceSummary? LastBlockItem { get; set; }
        public StreamState Clone() => (StreamState)MemberwiseClone();
    }

    private sealed class Session
    {
        public required string Id { get; init; }
        public required string Owner { get; init; }
        public required CommonLibraryQuery Query { get; init; }
        public required int PageSize { get; init; }
        public required long Created { get; init; }
        public required TimeSpan Lifetime { get; init; }
        public required QueryOmittedNode[] Omitted { get; init; }
        public required StreamState[] Streams { get; set; }
        public readonly SemaphoreSlim Gate = new(1, 1);
        public readonly CancellationTokenSource Closed = new();
        public int Sequence;
        public string? LastInput;
        public FederatedQueryPage? LastPage;
        public long Bytes;
    }

    private sealed record Prepared(PeerSearchTarget? Target, NodeQueryBlock? Block,
        QueryOmittedNode? Failure, long Started, long Received, WorkspaceReservation? Reservation = null);

    private sealed class WorkspaceReservation(FederatedQueryCoordinator owner) : IDisposable
    {
        public long Bytes { get; private set; }
        public void Resize(long bytes)
        {
            lock (owner._gate)
            {
                if (bytes < 0 || owner._bytes + owner._workspaceBytes - Bytes + bytes > owner._limits.MaxCoordinatorBytes)
                    throw new FederationQueryException("Busy", 429, retryable: true);
                owner._workspaceBytes += bytes - Bytes;
                Bytes = bytes;
            }
        }
        public void Dispose()
        {
            lock (owner._gate) { owner._workspaceBytes -= Bytes; Bytes = 0; }
        }
    }

    private readonly IPeerSearchTargetResolver _targets;
    private readonly FederationQueryLimits _limits;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _preparations;
    private readonly QueryTokenCodec _tokens = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _creating = new(StringComparer.Ordinal);
    private readonly ITimer _timer;
    private long _bytes;
    private long _workspaceBytes;
    private bool _disposed;

    public FederatedQueryCoordinator(IPeerSearchTargetResolver targets, FederationQueryLimits? limits = null,
        TimeProvider? timeProvider = null)
    {
        _targets = targets;
        _limits = limits ?? new();
        _time = timeProvider ?? TimeProvider.System;
        _preparations = new(_limits.MaxConcurrentPreparations);
        _timer = _time.CreateTimer(_ => Prune(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public async Task<FederatedQueryPage> CreateAsync(string owner, LocalFederatedQuery input,
        CancellationToken cancellationToken = default)
    {
        QueryProtocol.RejectUnknown(input, "request");
        var query = QueryProtocol.Normalize(input.Query, _limits);
        if (input.NodeIds is not { Length: > 0 } || input.NodeIds.Length > _limits.MaxNodes ||
            input.NodeIds.Any(n => string.IsNullOrWhiteSpace(n) || n.Length > 128) ||
            input.NodeIds.Distinct(StringComparer.Ordinal).Count() != input.NodeIds.Length)
            throw QueryProtocol.Unsupported("nodeIds");
        if (input.PageSize < 1 || input.PageSize > _limits.MaxPageSize) throw QueryProtocol.Unsupported("pageSize");
        Reserve(owner);
        var accepted = new List<Prepared>();
        Session? session = null;
        var admitted = false;
        var preparationStarted = _time.GetTimestamp();
        using var deadline = new CancellationTokenSource(_limits.PreparationTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var jobs = input.NodeIds.Select(n => Prepare(n, query, linked.Token)).ToArray();
        try
        {
            try { await Task.WhenAll(jobs).WaitAsync(linked.Token); }
            catch (OperationCanceledException) { /* Seal completed participants; late success is released below. */ }
            var omitted = new List<QueryOmittedNode>();
            for (var i = 0; i < jobs.Length; i++)
            {
                if (!jobs[i].IsCompletedSuccessfully)
                {
                    omitted.Add(new(input.NodeIds[i], "QueryDeadlineExceeded", true));
                    _ = ReleaseLate(jobs[i]);
                    continue;
                }
                var result = jobs[i].Result;
                // Timer callbacks and the continuation can be scheduled late. Compare the actual
                // completion timestamp so a success after the deadline never joins the frozen set.
                if (_time.GetElapsedTime(preparationStarted, result.Received) > _limits.PreparationTimeout)
                {
                    omitted.Add(new(input.NodeIds[i], "QueryDeadlineExceeded", true));
                    _ = ReleasePrepared(result);
                }
                else if (result.Failure != null) omitted.Add(result.Failure);
                else accepted.Add(result);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (accepted.Count == 0)
                throw new FederationQueryException("PeerUnavailable", 503,
                    "No requested node could create a search snapshot.", retryable: true, omittedNodes: omitted);
            var created = _time.GetTimestamp();
            var lifetime = _limits.SessionTtl;
            foreach (var result in accepted)
            {
                // Charge the full request duration, conservatively allowing for response transit without synchronized clocks.
                var remaining = TimeSpan.FromMilliseconds(result.Block!.ExpiresInMs) - _time.GetElapsedTime(result.Started, created);
                if (remaining < lifetime) lifetime = remaining;
            }
            if (lifetime <= TimeSpan.Zero) throw new FederationQueryException("QuerySessionExpired", 410);
            session = new Session
            {
                Id = Guid.NewGuid().ToString("N"), Owner = owner, Query = query, PageSize = input.PageSize,
                Created = created, Lifetime = lifetime, Omitted = omitted.ToArray(),
                Streams = accepted.Select(p => new StreamState
                {
                    Target = p.Target!, SnapshotId = p.Block!.SnapshotId, Total = p.Block.TotalCount,
                    Block = p.Block.Items.Select(QueryProtocol.Copy).ToArray(), NextCursor = p.Block.NextCursor,
                    LastBlockItem = p.Block.Items.LastOrDefault()
                }).ToArray()
            };
            session.Bytes = Estimate(session.Streams, null);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                var preparedBytes = accepted.Sum(p => p.Reservation?.Bytes ?? 0L);
                if (_bytes + _workspaceBytes - preparedBytes + session.Bytes > _limits.MaxCoordinatorBytes)
                    throw new FederationQueryException("Busy", 429, retryable: true);
                foreach (var prepared in accepted) prepared.Reservation?.Dispose();
                _sessions.Add(session.Id, session);
                _bytes += session.Bytes;
                admitted = true;
            }
            return await ReadAsync(owner, session.Id, _tokens.Encode(session.Id, 0), cancellationToken);
        }
        catch
        {
            if (admitted) await ReleaseAsync(owner, session!.Id, CancellationToken.None);
            else await Task.WhenAll(accepted.Select(ReleasePrepared));
            throw;
        }
        finally
        {
            // Also cancel queued work when creation was cancelled before the shared deadline.
            linked.Cancel();
            lock (_gate)
                if (--_creating[owner] == 0) _creating.Remove(owner);
        }
    }

    public async Task<FederatedQueryPage> ReadAsync(string owner, string sessionId, string cursor,
        CancellationToken cancellationToken = default)
    {
        var session = Get(owner, sessionId);
        var sequence = _tokens.Decode(sessionId, cursor);
        using var timeout = new CancellationTokenSource(_limits.PageTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token, session.Closed.Token);
        await session.Gate.WaitAsync(linked.Token);
        try
        {
            EnsureLive(session);
            if (session.LastInput == cursor && session.LastPage != null)
            {
                // Cached responses are not a way around revocation, epoch changes or TTL.
                await ValidatePageSources(session, session.LastPage.Items, linked.Token);
                EnsureLive(session);
                return CopyPage(session.LastPage, Remaining(session));
            }
            if (sequence != session.Sequence)
                throw new FederationQueryException("CursorSuperseded", 409);
            using var workspace = new WorkspaceReservation(this);
            var states = session.Streams.Select(s => s.Clone()).ToArray();
            var comparer = QueryProtocol.Comparer(session.Query.Sort);
            var heap = new PriorityQueue<int, FederatedResourceSummary>(comparer);
            for (var i = 0; i < states.Length; i++)
            {
                await EnsureHead(states[i], session.Query, workspace, linked.Token);
                if (states[i].Consumed < states[i].Total) heap.Enqueue(i, states[i].Block[states[i].Position]);
            }
            var page = new List<FederatedResourceSummary>(session.PageSize);
            long pageBytes = 0;
            while (page.Count < session.PageSize && heap.TryDequeue(out var index, out var item))
            {
                linked.Token.ThrowIfCancellationRequested();
                var itemBytes = QueryProtocol.EstimateBytes(item);
                if (page.Count > 0 && pageBytes + itemBytes > _limits.MaxPageBytes) break;
                pageBytes += itemBytes;
                page.Add(item);
                var state = states[index];
                state.Position++;
                state.Consumed++;
                if (state.Consumed == state.Total)
                {
                    state.Block = [];
                    state.Position = 0;
                    state.LastBlockItem = null;
                }
                if (page.Count == session.PageSize) break;
                await EnsureHead(state, session.Query, workspace, linked.Token);
                if (state.Consumed < state.Total) heap.Enqueue(index, state.Block[state.Position]);
            }
            await ValidatePageSources(session, page, linked.Token);
            EnsureLive(session);
            linked.Token.ThrowIfCancellationRequested();
            var result = new FederatedQueryPage
            {
                SessionId = session.Id, Items = page.Select(QueryProtocol.Copy).ToArray(),
                NextCursor = states.Any(s => s.Consumed < s.Total) ? _tokens.Encode(session.Id, session.Sequence + 1) : null,
                ExpiresInMs = Remaining(session),
                Participants = states.Select(s => new QueryParticipant(s.Target.NodeId, s.Target.LibraryEpoch, s.Total)).ToArray(),
                OmittedNodes = session.Omitted.ToArray(), TotalWithinParticipants = states.Sum(s => s.Total),
                CoverageComplete = session.Omitted.Length == 0
            };
            var nextBytes = Estimate(states, result);
            lock (_gate)
            {
                EnsureLive(session);
                if (_bytes + _workspaceBytes - workspace.Bytes - session.Bytes + nextBytes > _limits.MaxCoordinatorBytes)
                    throw new FederationQueryException("Busy", 429, retryable: true);
                workspace.Dispose();
                _bytes += nextBytes - session.Bytes;
                session.Bytes = nextBytes;
                session.Streams = states;
                session.Sequence++;
                session.LastInput = cursor;
                session.LastPage = result;
            }
            return CopyPage(result, result.ExpiresInMs);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FederationQueryException(session.Closed.IsCancellationRequested ? "QuerySessionExpired" : "QuerySessionInterrupted",
                session.Closed.IsCancellationRequested ? 410 : 409, retryable: !session.Closed.IsCancellationRequested);
        }
        finally { session.Gate.Release(); }
    }

    public async Task ReleaseAsync(string owner, string sessionId, CancellationToken cancellationToken = default)
    {
        Session? session;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out session)) return;
            if (session.Owner != owner) throw new FederationQueryException("CapabilityDenied", 403);
            RemoveLocked(session);
        }
        await ReleaseStreams(session.Streams);
    }

    private async Task<Prepared> Prepare(string nodeId, CommonLibraryQuery query, CancellationToken cancellationToken)
    {
        var started = _time.GetTimestamp();
        PeerSearchTarget? target = null;
        NodeQueryBlock? block = null;
        var entered = false;
        var reservation = new WorkspaceReservation(this);
        try
        {
            await _preparations.WaitAsync(cancellationToken);
            entered = true;
            target = await _targets.ResolveAsync(nodeId, cancellationToken);
            if (target.NodeId != nodeId) throw new FederationQueryException("IdentityConflict", 409, nodeId: nodeId);
            reservation.Resize(8 * 1024 * 1024 + _limits.MaxBlockBytes);
            block = await target.Client.CreateAsync(new NodeExportQuery
            {
                ExpectedLibraryEpoch = target.LibraryEpoch, Query = query, BlockSize = _limits.BlockSize
            }, cancellationToken);
            ValidateBlock(target, block, QueryProtocol.Hash(query), 0, null, null, query.Sort);
            reservation.Resize(512 + block.Items.Sum(QueryProtocol.EstimateBytes) * 2);
            return new(target, block, null, started, _time.GetTimestamp(), reservation);
        }
        catch (Exception exception)
        {
            reservation.Dispose();
            if (target != null && block != null) await BestEffortRelease(target.Client, block.SnapshotId);
            var error = exception as FederationQueryException;
            return new(null, null, new(nodeId, error?.Code ??
                (exception is OperationCanceledException ? "QueryDeadlineExceeded" : "PeerUnavailable"), error?.Retryable ?? true),
                started, _time.GetTimestamp());
        }
        finally { if (entered) _preparations.Release(); }
    }

    private async Task EnsureHead(StreamState state, CommonLibraryQuery query, WorkspaceReservation workspace,
        CancellationToken cancellationToken)
    {
        if (state.Consumed >= state.Total || state.Position < state.Block.Length) return;
        if (state.NextCursor == null) throw new FederationQueryException("InvalidPeerResponse", 502, nodeId: state.Target.NodeId);
        NodeQueryBlock block;
        var previousWorkspace = workspace.Bytes;
        workspace.Resize(previousWorkspace + 8 * 1024 * 1024 + _limits.MaxBlockBytes);
        try { block = await state.Target.Client.ReadAsync(state.SnapshotId, state.NextCursor, cancellationToken); }
        catch (FederationQueryException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            throw new FederationQueryException("QuerySessionInterrupted", 409, retryable: true,
                nodeId: state.Target.NodeId, innerException: exception);
        }
        ValidateBlock(state.Target, block, QueryProtocol.Hash(query), state.Consumed,
            state.SnapshotId, state.Total, query.Sort, state.LastBlockItem);
        workspace.Resize(previousWorkspace + 512 + block.Items.Sum(QueryProtocol.EstimateBytes) * 2);
        state.Block = block.Items.Select(QueryProtocol.Copy).ToArray();
        state.Position = 0;
        state.NextCursor = block.NextCursor;
        state.LastBlockItem = block.Items.LastOrDefault() ?? state.LastBlockItem;
    }

    private void ValidateBlock(PeerSearchTarget target, NodeQueryBlock block, string queryHash,
        int offset, string? snapshotId, long? total, string sort, FederatedResourceSummary? previous = null)
    {
        void Invalid() => throw new FederationQueryException("InvalidPeerResponse", 502, nodeId: target.NodeId);
        if (block.NodeId != target.NodeId || block.LibraryEpoch != target.LibraryEpoch)
            throw new FederationQueryException("LibraryEpochChanged", 409, nodeId: target.NodeId);
        if (string.IsNullOrWhiteSpace(block.SnapshotId) || block.SnapshotId.Length > 128 ||
            snapshotId != null && block.SnapshotId != snapshotId || block.QueryHash != queryHash ||
            block.Offset != offset || block.TotalCount < 0 || block.TotalCount > int.MaxValue ||
            block.Consistency != "frozen-observation-v1" || block.NextCursor is "" || block.NextCursor?.Length > 512 ||
            total.HasValue && block.TotalCount != total || block.Items == null || block.Items.Length > _limits.MaxBlockSize ||
            block.ExpiresInMs <= 0 || block.ExpiresInMs > TimeSpan.FromDays(1).TotalMilliseconds ||
            block.Items.Length > block.TotalCount - offset || offset < block.TotalCount && block.Items.Length == 0 ||
            (offset + block.Items.Length < block.TotalCount) != (block.NextCursor != null)) Invalid();
        long bytes = 0;
        var comparer = QueryProtocol.Comparer(sort);
        foreach (var item in block.Items!)
        {
            if (item?.Ref == null || item.Ref.NodeId != target.NodeId || item.Ref.LibraryEpoch != target.LibraryEpoch ||
                item.Ref.ResourceId <= 0 || item.OwnerLabel == null || item.DisplayName == null || item.SourceKinds == null ||
                item.PlaybackCapabilities == null) Invalid();
            if (new[] { item!.OwnerLabel, item.Title, item.DisplayName, item.FileName, item.NormalizedSortKey, item.CoverAsset }
                .Any(s => s?.Length > _limits.MaxStringLength)) Invalid();
            if (item.SourceKinds!.Length > QueryProtocol.SupportedSourceKinds.Count ||
                item.SourceKinds.Distinct().Count() != item.SourceKinds.Length ||
                item.SourceKinds.Any(s => !QueryProtocol.SupportedSourceKinds.Contains(s)) ||
                item.PlaybackCapabilities!.Length > 16 ||
                item.FileAvailability is not ("HasFile" or "MetadataOnly") ||
                item.PlaybackCapabilities!.Any(p => p == null || p.Length > _limits.MaxStringLength) ||
                item.NormalizedSortKey != QueryProtocol.NormalizeName(item.Title) ||
                previous != null && comparer.Compare(previous, item) >= 0) Invalid();
            bytes += QueryProtocol.EstimateBytes(item);
            if (bytes > _limits.MaxBlockBytes || bytes > _limits.MaxCoordinatorBytes) Invalid();
            previous = item;
        }
    }

    private async Task ValidatePageSources(Session session, IEnumerable<FederatedResourceSummary> items,
        CancellationToken cancellationToken)
    {
        // Counts and coverage still describe every original participant, including exhausted streams.
        // Revalidate all of them for new pages AND cached retries; none may silently leave the session.
        foreach (var state in session.Streams)
        {
            try { await state.Target.Client.ValidateAsync(state.SnapshotId, cancellationToken); }
            catch (FederationQueryException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                throw new FederationQueryException("QuerySessionInterrupted", 409, retryable: true,
                    nodeId: state.Target.NodeId, innerException: exception);
            }
        }
    }

    private void Reserve(string owner)
    {
        Prune();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sessions.Count + _creating.Values.Sum() >= _limits.MaxCoordinatorSessions ||
                _sessions.Values.Count(s => s.Owner == owner) + _creating.GetValueOrDefault(owner) >= _limits.MaxSessionsPerOwner)
                throw new FederationQueryException("Busy", 429, retryable: true);
            _creating[owner] = _creating.GetValueOrDefault(owner) + 1;
        }
    }
    private Session Get(string owner, string id)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(id, out var session)) throw new FederationQueryException("QuerySessionExpired", 410);
            if (session.Owner != owner) throw new FederationQueryException("CapabilityDenied", 403);
            EnsureLive(session);
            return session;
        }
    }
    private long Remaining(Session session) => Math.Max(0, (long)(session.Lifetime - _time.GetElapsedTime(session.Created)).TotalMilliseconds);
    private void EnsureLive(Session session)
    {
        if (session.Closed.IsCancellationRequested || Remaining(session) <= 0)
            throw new FederationQueryException("QuerySessionExpired", 410);
    }
    private static long Estimate(IEnumerable<StreamState> states, FederatedQueryPage? page) =>
        1024L + states.Sum(s => 256L + s.Block.Sum(QueryProtocol.EstimateBytes)) +
        (page?.Items.Sum(QueryProtocol.EstimateBytes) ?? 0L);
    private static FederatedQueryPage CopyPage(FederatedQueryPage page, long remaining) => page with
    {
        ExpiresInMs = remaining, Items = page.Items.Select(QueryProtocol.Copy).ToArray(),
        Participants = page.Participants.ToArray(), OmittedNodes = page.OmittedNodes.ToArray()
    };
    private void RemoveLocked(Session session)
    {
        if (!_sessions.Remove(session.Id)) return;
        _bytes -= session.Bytes;
        session.Closed.Cancel();
    }
    private void Prune()
    {
        Session[] expired;
        lock (_gate)
        {
            expired = _sessions.Values.Where(s => Remaining(s) <= 0).ToArray();
            foreach (var session in expired) RemoveLocked(session);
        }
        foreach (var session in expired) _ = ReleaseStreams(session.Streams);
    }
    private async Task ReleaseLate(Task<Prepared> task)
    {
        try { await ReleasePrepared(await task); }
        catch { /* Creation failures are already represented in omittedNodes. */ }
    }
    private Task ReleasePrepared(Prepared value)
    {
        value.Reservation?.Dispose();
        return value.Target != null && value.Block != null
            ? BestEffortRelease(value.Target.Client, value.Block.SnapshotId) : Task.CompletedTask;
    }
    private Task ReleaseStreams(IEnumerable<StreamState> streams) =>
        Task.WhenAll(streams.Select(s => BestEffortRelease(s.Target.Client, s.SnapshotId)));
    private async Task BestEffortRelease(IPeerSearchClient client, string snapshotId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2), _time);
        try { await client.ReleaseAsync(snapshotId, timeout.Token).WaitAsync(timeout.Token); }
        catch { /* Owner-side absolute TTL bounds orphan retention. */ }
    }
    public Task ReleaseOwnerAsync(string owner)
    {
        Session[] sessions;
        lock (_gate)
        {
            sessions = _sessions.Values.Where(s => s.Owner == owner).ToArray();
            foreach (var session in sessions) RemoveLocked(session);
        }
        return Task.WhenAll(sessions.Select(session => ReleaseStreams(session.Streams)));
    }

    public void Dispose()
    {
        _timer.Dispose();
        Session[] sessions;
        lock (_gate)
        {
            _disposed = true;
            sessions = _sessions.Values.ToArray();
            foreach (var session in sessions) RemoveLocked(session);
        }
        foreach (var session in sessions) _ = ReleaseStreams(session.Streams);
    }
}
