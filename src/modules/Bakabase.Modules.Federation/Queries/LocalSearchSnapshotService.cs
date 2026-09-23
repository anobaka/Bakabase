using Bakabase.Modules.Federation.Contracts;

namespace Bakabase.Modules.Federation.Queries;

/// <summary>Stores short-lived immutable, grant-bound LOCAL arrays. It has no peer/coordinator dependency.</summary>
public sealed class LocalSearchSnapshotService : IDisposable
{
    private sealed class Snapshot
    {
        public required string Id { get; init; }
        public required QueryAccess Access { get; init; }
        public required string QueryHash { get; init; }
        public required FederatedResourceSummary[] Items { get; init; }
        public required int BlockSize { get; init; }
        public required long Created { get; init; }
        public required long Bytes { get; init; }
        public required DateTimeOffset CaptureStarted { get; init; }
        public required DateTimeOffset CaptureCompleted { get; init; }
        public CancellationTokenRegistration Revocation;
    }

    private readonly ILocalLibraryReader _reader;
    private readonly IFederationQueryAccess _access;
    private readonly FederationQueryLimits _limits;
    private readonly TimeProvider _time;
    private readonly QueryTokenCodec _tokens = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, Snapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _captures = new(StringComparer.Ordinal);
    private readonly ITimer _timer;
    private long _snapshotBytes;
    private bool _disposed;

    public LocalSearchSnapshotService(ILocalLibraryReader reader, IFederationQueryAccess access,
        FederationQueryLimits? limits = null, TimeProvider? timeProvider = null)
    {
        _reader = reader;
        _access = access;
        _limits = limits ?? new();
        _time = timeProvider ?? TimeProvider.System;
        _timer = _time.CreateTimer(_ => Prune(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public async Task<NodeQueryBlock> CreateAsync(string grantId, NodeExportQuery input,
        CancellationToken cancellationToken = default)
    {
        QueryProtocol.RejectUnknown(input, "request");
        var query = QueryProtocol.Normalize(input.Query, _limits);
        if (string.IsNullOrWhiteSpace(input.ExpectedLibraryEpoch)) throw QueryProtocol.Unsupported("expectedLibraryEpoch");
        if (input.BlockSize < 1 || input.BlockSize > _limits.MaxBlockSize) throw QueryProtocol.Unsupported("blockSize");
        var access = await _access.ValidateAsync(grantId, input.ExpectedLibraryEpoch, cancellationToken);
        ReserveCapture(grantId);
        using var timeout = new CancellationTokenSource(_limits.CaptureTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            timeout.Token, _access.GetCancellationToken(grantId));
        string? createdId = null;
        try
        {
            var capture = await _reader.CaptureAsync(new CaptureBudget(_limits.MaxCaptureBytes,
                _limits.MaxStringLength), linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (capture.NodeId != access.NodeId || capture.LibraryEpoch != access.LibraryEpoch)
                throw new FederationQueryException("LibraryEpochChanged", 409);
            var items = new List<FederatedResourceSummary>();
            var ids = new HashSet<int>();
            long bytes = 256 + 2L * (capture.NodeId.Length + capture.LibraryEpoch.Length + capture.OwnerLabel.Length);
            foreach (var resource in capture.Resources)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (resource.ResourceId <= 0 || !ids.Add(resource.ResourceId))
                    throw new FederationQueryException("InvalidLocalProjection", 503,
                        "A captured local projection contains invalid or duplicate resource identities.");
                if (!QueryProtocol.Matches(resource, query)) continue;
                var summary = QueryProtocol.Project(capture, resource);
                if (QueryProtocol.EstimateBytes(summary) > _limits.MaxBlockBytes)
                    throw new FederationQueryException("ResultSnapshotTooLarge", 413, "One resource cannot fit in a response block.");
                bytes += QueryProtocol.EstimateRetainedBytes(summary);
                if (bytes > _limits.MaxSnapshotBytes)
                    throw new FederationQueryException("ResultSnapshotTooLarge", 413);
                items.Add(summary);
            }
            // Sorting is bounded by the workspace limit; comparison checks also stop a cancelled build.
            var comparer = QueryProtocol.Comparer(query.Sort);
            items.Sort(System.Collections.Generic.Comparer<FederatedResourceSummary>.Create((a, b) =>
            {
                linked.Token.ThrowIfCancellationRequested();
                return comparer.Compare(a, b);
            }));
            var current = await _access.ValidateAsync(grantId, input.ExpectedLibraryEpoch, linked.Token);
            if (current != access) throw new FederationQueryException("GrantRevoked", 401);
            var snapshot = new Snapshot
            {
                Id = Guid.NewGuid().ToString("N"), Access = access, QueryHash = QueryProtocol.Hash(query),
                Items = items.ToArray(), BlockSize = input.BlockSize, Bytes = bytes,
                Created = _time.GetTimestamp(), CaptureStarted = capture.StartedAt,
                CaptureCompleted = capture.CompletedAt
            };
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                PruneLocked();
                if (_snapshotBytes + bytes > _limits.MaxTotalSnapshotBytes)
                    throw new FederationQueryException("Busy", 429, retryable: true);
                _snapshots.Add(snapshot.Id, snapshot);
                _snapshotBytes += bytes;
                createdId = snapshot.Id;
            }
            snapshot.Revocation = _access.GetCancellationToken(grantId).Register(() => Remove(snapshot.Id));
            linked.Token.ThrowIfCancellationRequested();
            return BuildBlock(snapshot, 0);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (createdId != null) Remove(createdId);
            throw new FederationQueryException(timeout.IsCancellationRequested ? "ScanBudgetExceeded" : "GrantRevoked",
                timeout.IsCancellationRequested ? 503 : 401);
        }
        catch (InvalidOperationException exception) when (exception.InnerException is OperationCanceledException)
        {
            if (createdId != null) Remove(createdId);
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw new FederationQueryException(timeout.IsCancellationRequested ? "ScanBudgetExceeded" : "GrantRevoked",
                timeout.IsCancellationRequested ? 503 : 401);
        }
        catch
        {
            if (createdId != null) Remove(createdId);
            throw;
        }
        finally
        {
            lock (_gate)
            {
                if (--_captures[grantId] == 0) _captures.Remove(grantId);
            }
        }
    }

    public async Task<NodeQueryBlock> ReadAsync(string grantId, string snapshotId, string cursor,
        CancellationToken cancellationToken = default)
    {
        var snapshot = Get(grantId, snapshotId);
        var position = _tokens.Decode(snapshotId, cursor);
        await Authorize(snapshot, cancellationToken);
        if (position >= snapshot.Items.Length)
            throw new FederationQueryException("InvalidCursor", 400);
        return BuildBlock(snapshot, position);
    }

    public async Task ValidateAsync(string grantId, string snapshotId, CancellationToken cancellationToken = default) =>
        await Authorize(Get(grantId, snapshotId), cancellationToken);

    public Task ReleaseAsync(string grantId, string snapshotId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_snapshots.TryGetValue(snapshotId, out var snapshot) && snapshot.Access.GrantId == grantId)
                RemoveLocked(snapshotId);
        }
        return Task.CompletedTask;
    }

    public void RevokeGrant(string grantId)
    {
        lock (_gate)
            foreach (var id in _snapshots.Values.Where(s => s.Access.GrantId == grantId).Select(s => s.Id).ToArray())
                RemoveLocked(id);
    }

    private async Task Authorize(Snapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            var current = await _access.ValidateAsync(snapshot.Access.GrantId, snapshot.Access.LibraryEpoch, cancellationToken);
            if (current != snapshot.Access) throw new FederationQueryException("GrantRevoked", 401);
            if (Remaining(snapshot) <= 0) throw new FederationQueryException("QuerySessionExpired", 410);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            // A caller cancellation is not a revocation and must not destroy another in-flight retry.
            if (!cancellationToken.IsCancellationRequested) Remove(snapshot.Id);
            throw;
        }
    }

    private NodeQueryBlock BuildBlock(Snapshot snapshot, int offset)
    {
        var count = 0;
        long bytes = 0;
        while (count < snapshot.BlockSize && offset + count < snapshot.Items.Length)
        {
            var rowBytes = QueryProtocol.EstimateBytes(snapshot.Items[offset + count]);
            if (bytes + rowBytes > _limits.MaxBlockBytes) break;
            bytes += rowBytes;
            count++;
        }
        return new NodeQueryBlock
        {
            SnapshotId = snapshot.Id, NodeId = snapshot.Access.NodeId, LibraryEpoch = snapshot.Access.LibraryEpoch,
            QueryHash = snapshot.QueryHash, TotalCount = snapshot.Items.Length, Offset = offset,
            Items = snapshot.Items.Skip(offset).Take(count).Select(QueryProtocol.Copy).ToArray(),
            NextCursor = offset + count < snapshot.Items.Length ? _tokens.Encode(snapshot.Id, offset + count) : null,
            ExpiresInMs = Remaining(snapshot), CaptureStartedAt = snapshot.CaptureStarted,
            CaptureCompletedAt = snapshot.CaptureCompleted
        };
    }

    private long Remaining(Snapshot snapshot) => Math.Max(0,
        (long)(_limits.SnapshotTtl - _time.GetElapsedTime(snapshot.Created)).TotalMilliseconds);

    private Snapshot Get(string grantId, string id)
    {
        lock (_gate)
        {
            PruneLocked();
            if (!_snapshots.TryGetValue(id, out var snapshot)) throw new FederationQueryException("QuerySessionExpired", 410);
            if (snapshot.Access.GrantId != grantId) throw new FederationQueryException("CapabilityDenied", 403);
            return snapshot;
        }
    }

    private void ReserveCapture(string grantId)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            PruneLocked();
            var running = _captures.Values.Sum();
            if (_snapshots.Count + running >= _limits.MaxSnapshotCount ||
                _snapshots.Values.Count(s => s.Access.GrantId == grantId) + _captures.GetValueOrDefault(grantId) >= _limits.MaxSnapshotsPerGrant ||
                (running + 1L) * _limits.MaxCaptureBytes > _limits.MaxTotalCaptureBytes)
                throw new FederationQueryException("Busy", 429, retryable: true);
            _captures[grantId] = _captures.GetValueOrDefault(grantId) + 1;
        }
    }

    private void Prune() { lock (_gate) PruneLocked(); }
    private void PruneLocked()
    {
        foreach (var id in _snapshots.Values.Where(s => Remaining(s) <= 0).Select(s => s.Id).ToArray()) RemoveLocked(id);
    }
    private void Remove(string id) { lock (_gate) RemoveLocked(id); }
    private void RemoveLocked(string id)
    {
        if (!_snapshots.Remove(id, out var snapshot)) return;
        _snapshotBytes -= snapshot.Bytes;
        snapshot.Revocation.Unregister();
    }
    public void Dispose()
    {
        _timer.Dispose();
        lock (_gate)
        {
            _disposed = true;
            foreach (var id in _snapshots.Keys.ToArray()) RemoveLocked(id);
        }
    }
}

/// <summary>Uses the same snapshot implementation for the coordinator's local participant, without HTTP.</summary>
public sealed class LocalPeerSearchClient(LocalSearchSnapshotService snapshots, string grantId) : IPeerSearchClient
{
    public Task<NodeQueryBlock> CreateAsync(NodeExportQuery query, CancellationToken cancellationToken) =>
        snapshots.CreateAsync(grantId, query, cancellationToken);
    public Task<NodeQueryBlock> ReadAsync(string snapshotId, string cursor, CancellationToken cancellationToken) =>
        snapshots.ReadAsync(grantId, snapshotId, cursor, cancellationToken);
    public Task ValidateAsync(string snapshotId, CancellationToken cancellationToken) =>
        snapshots.ValidateAsync(grantId, snapshotId, cancellationToken);
    public Task ReleaseAsync(string snapshotId, CancellationToken cancellationToken) =>
        snapshots.ReleaseAsync(grantId, snapshotId, cancellationToken);
}
