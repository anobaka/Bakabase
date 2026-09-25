using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>A snapshot read to its last page, or why it could not be used.</summary>
/// <param name="Problem">A <see cref="DataSyncPeerErrorCode"/> name; null when every requested kind was read.</param>
public sealed record DataSyncFetchedSnapshot(DataSyncFeedManifest? Manifest, IReadOnlyList<DataSyncStagedKind> Kinds,
    long Bytes, DataSyncPeerErrorCode? Problem, string? ProblemDetail);

/// <summary>
/// The fetch half of a cycle (§8.10.2), run by the <c>DataSync</c> task outside the gate and outside any transaction.
/// Per due link: the head poll with the contract checks and breakers B1/B1b, the peer facts and the actor evidence it
/// carries, the approver's wait for its peer's review, then — only when the link's kinds changed, a fallback or a full
/// reconciliation is due, or a first contact needs a review — one manifest and its pages. A first-link review is
/// staged once (§8.3); every other pull is staged for <c>DataSyncApply</c>, and no pull is fetched again while an
/// equal one still waits for the apply.
/// </summary>
public sealed class DataSyncFetcher
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DataSyncLinkService _links;
    private readonly IDataSyncStagedPullStore _stagedPulls;
    private readonly DataSyncTaskLauncher _launcher;
    private readonly DataSyncRuntimeState _state;
    private readonly DataSyncPeerFetchLock _fetchLock;
    private readonly IDataSyncRuntimeObserver _observer;
    private readonly IDataSyncClock _clock;
    private readonly DataSyncLimits _limits;
    private readonly ILogger<DataSyncFetcher> _logger;

    public DataSyncFetcher(IServiceScopeFactory scopes, DataSyncLinkService links, IDataSyncStagedPullStore stagedPulls,
        DataSyncTaskLauncher launcher, DataSyncRuntimeState state, DataSyncPeerFetchLock fetchLock,
        IDataSyncRuntimeObserver observer, IDataSyncClock clock, DataSyncLimits limits,
        ILogger<DataSyncFetcher> logger)
    {
        _scopes = scopes;
        _links = links;
        _stagedPulls = stagedPulls;
        _launcher = launcher;
        _state = state;
        _fetchLock = fetchLock;
        _observer = observer;
        _clock = clock;
        _limits = limits;
        _logger = logger;
    }

    /// <summary>
    /// One run of the <c>DataSync</c> task: every due link in turn, then the actor verification (§5.6) and, once a
    /// day, retention (§4.6). A link's failure is recorded on the link and never stops the others.
    /// </summary>
    public async Task RunCycleAsync(BTaskArgs args)
    {
        var ct = args.CancellationToken;
        var now = _clock.UtcNow;
        var fallback = _state.TakeFallbackDue(now);

        DataSyncLocalStateDbModel? local;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            local = await scope.ServiceProvider.GetRequiredService<IDataSyncStore>().GetLocalStateAsync(ct);
        }

        if (local?.AllPaused != true)
        {
            var due = (await _links.GetLinksAsync(ct))
                .Where(l => l.IsFetchable() && (l.NextAttemptAtUtc is not { } next || next <= now ||
                                                (fallback && l.GetCursors().Count > 0)))
                .OrderBy(l => l.Id)
                .ToList();
            for (var i = 0; i < due.Count; i++)
            {
                await args.YieldAsync();
                var index = i;
                await args.UpdateTask(t =>
                {
                    t.Percentage = index * 100 / due.Count;
                    t.Process = $"{index}/{due.Count}";
                });
                await FetchLinkAsync(due[i], fallback, local, ct);
            }
        }

        await VerifyActorIfDueAsync(ct);

        await using (var scope = _scopes.CreateAsyncScope())
        {
            var retention = scope.ServiceProvider.GetService<IDataSyncRetention>();
            if (retention is not null && _state.TakeRetentionDue(_clock.UtcNow))
            {
                await args.YieldAsync();
                await retention.RunAsync(_clock.UtcNow, ct);
            }
        }
    }

    /// <summary>
    /// Marks the actor verified once every Active link's peer answered one head since the start, or the start is
    /// two minutes ago, or there is no Active link (§5.6).
    /// </summary>
    public async Task VerifyActorIfDueAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var guard = scope.ServiceProvider.GetService<IDataSyncActorGuard>();
        if (guard is null || guard.IsVerified) return;
        if (_state.CanVerify(await _links.GetLinksAsync(ct), _clock.UtcNow)) guard.MarkVerified();
    }

    /// <summary>The fetch half for one link. Failures are recorded on the link; only cancellation escapes.</summary>
    public async Task FetchLinkAsync(DataSyncLinkDbModel link, bool fallback, DataSyncLocalStateDbModel? local,
        CancellationToken ct)
    {
        try
        {
            if (link.State == DataSyncLinkState.AwaitingAccess)
            {
                await CheckAccessAsync(link, ct);
                return;
            }

            using (await _fetchLock.AcquireAsync(link.PeerNodeId, ct))
            {
                await FetchLockedAsync(link, fallback, local, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (DataSyncPeerException e)
        {
            await HandlePeerErrorAsync(link.Id, e.Code, e.Message, e.RetryAfterSeconds, ct);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Data sync could not fetch from {Peer}", link.PeerNodeId);
            await _links.RecordFailureAsync(link.Id, DataSyncLinkService.FetchFailed, e.Message, null, ct);
        }
    }

    private async Task FetchLockedAsync(DataSyncLinkDbModel link, bool fallback, DataSyncLocalStateDbModel? local,
        CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var peer = sp.GetRequiredService<IDataSyncPeerClient>();
        var reader = sp.GetRequiredService<IDataSyncKindPageReader>();
        var store = sp.GetRequiredService<IDataSyncStore>();

        var kinds = link.GetKinds().Where(reader.Supports).ToList();
        var cursors = link.GetCursors();
        var openItems = (await store.GetOpenItemsAsync(link.Id, ct)).Count;
        var query = new DataSyncFeedQuery(link.GetDeclaredMode(),
            kinds.ToDictionary(k => k, k => cursors.GetValueOrDefault(k), StringComparer.Ordinal),
            ActorOf(local), link.GetDeclaredState(openItems));

        // 1. Head.
        var head = await peer.GetHeadAsync(link.PeerNodeId, query, ct);
        var now = _clock.UtcNow;
        _state.RecordHead(link.Id, head, now);

        // Version skew (§8.12): no pulls; checked again every 6 h.
        if (head.ContractVersion < DataSyncContract.MinimumPeerVersion)
        {
            await SetPeerErrorStateAsync(link.Id, DataSyncLinkState.PeerTooOld, DataSyncPeerErrorCode.PeerTooOld,
                DataSyncSchedule.VersionRetry, ct, head);
            return;
        }

        if (DataSyncContract.Version < head.MinimumPeerContract)
        {
            await SetPeerErrorStateAsync(link.Id, DataSyncLinkState.ThisTooOld, DataSyncPeerErrorCode.ThisTooOld,
                DataSyncSchedule.VersionRetry, ct, head);
            return;
        }

        if (BreakerOf(link, head.NodeId, head.LibraryEpoch) is { } reset)
        {
            await _links.PauseAsync(link.Id, DataSyncPauseReason.PeerReset, reset, ct);
            return;
        }

        var headKinds = head.Kinds.GroupBy(k => k.Kind, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        if (kinds.Any(k => cursors.TryGetValue(k, out var c) && c > 0 && headKinds.TryGetValue(k, out var hk) &&
                           hk.MaxSeq < c))
        {
            // B1b: the same node and epoch, but a kind went back below the cursor (§5.6 "Peer restored").
            await _links.PauseAsync(link.Id, DataSyncPauseReason.PeerReset, DataSyncLinkService.RestoredDetail, ct);
            return;
        }

        if (head.SeenCounter is { } seen && ActorOf(local) is { } selfActor &&
            sp.GetService<IDataSyncActorGuard>() is { } guard)
        {
            await guard.ReportPeerEvidenceAsync(link.PeerNodeId, selfActor, seen, ct);
        }

        var identityChanged = link.PeerLibraryEpoch is null || link.PeerActorId != head.ActorId ||
                              link.PeerContractVersion != head.ContractVersion;
        var peerFactsChanged = link.PeerAttentionJson != Serialize(head.Attention) ||
                               link.CounterpartJson != Serialize(head.Counterpart);
        var updated = await RecordAnswerAsync(link.Id, head.LibraryEpoch, head.ActorId, head.AppVersion,
            head.ContractVersion, head.Attention, head.Counterpart, ct);
        if (updated is null || !updated.IsFetchable()) return;
        if (peerFactsChanged) await ObserveChangedAsync(updated, ct);
        link = updated;

        // The approver waits for the initiator's review (§8.3): the head's counterpart says when it is done, so a
        // waiting link never builds a snapshot.
        if (link.State == DataSyncLinkState.WaitingForPeerReview)
        {
            if (head.Counterpart?.FirstContactCompleted != true)
            {
                await RescheduleAsync(link.Id, DataSyncSchedule.PollInterval, ct);
                return;
            }

            var active = await _links.MutateAsync(link.Id, row =>
            {
                if (row.State != DataSyncLinkState.WaitingForPeerReview) return DataSyncLinkWrite.None;
                row.State = DataSyncLinkState.Active;
                return DataSyncLinkWrite.Transition;
            }, ct);
            if (active is not { State: DataSyncLinkState.Active }) return;
            link = active;
        }

        // 2. What this cycle needs.
        var reviews = sp.GetRequiredService<IDataSyncReviewStore>();
        var awaitingFirstContact = link.GetKindsAwaitingFirstContact().Where(kinds.Contains).ToList();
        var reviewKinds = link.State switch
        {
            DataSyncLinkState.AwaitingReview => kinds,
            DataSyncLinkState.Active when link.Initiator == DataSyncLinkInitiator.ThisDevice => awaitingFirstContact,
            _ => [],
        };
        var needReview = reviewKinds.Count > 0 && reviews.GetForLink(link.Id) is null;
        var mergeKinds = link.State == DataSyncLinkState.Active
            ? kinds.Where(k => !reviewKinds.Contains(k) && headKinds.ContainsKey(k)).ToList()
            : [];
        var fullReconciliation = mergeKinds.Count > 0 && IsFullReconciliationDue(link, now);

        // Only the link's kinds with something new are pulled, unless the whole link is: a full reconciliation, the
        // fallback pull, or a peer whose node, epoch, actor or contract changed (§8.2).
        var pullKinds = fullReconciliation || identityChanged || (fallback && cursors.Count > 0)
            ? mergeKinds
            : mergeKinds.Where(k => !cursors.TryGetValue(k, out var c) || headKinds[k].MaxSeq > c ||
                                    headKinds[k].CursorSuperseded).ToList();
        if (pullKinds.Count > 0 && _stagedPulls.Peek(link.Id) is { } waiting)
        {
            if (pullKinds.All(k => waiting.Kinds.Any(s => s.Kind == k && s.MaxSeq == headKinds[k].MaxSeq)))
            {
                // An equal pull still waits for DataSyncApply (e.g. behind Enhancement): never fetch it again.
                pullKinds = [];
            }
            else
            {
                // The new pull replaces the waiting one, so it carries the waiting one's kinds too.
                pullKinds = mergeKinds.Where(k => pullKinds.Contains(k) || waiting.Kinds.Any(s => s.Kind == k))
                    .ToList();
            }
        }

        var mergeWanted = pullKinds.Count > 0;

        if (!needReview && !mergeWanted)
        {
            await RescheduleAsync(link.Id, DataSyncSchedule.PollInterval, ct);
            return;
        }

        // 3. Manifest and pages.
        var since = new Dictionary<string, long>(StringComparer.Ordinal);
        if (needReview)
        {
            foreach (var kind in reviewKinds) since[kind] = 0;
        }

        if (mergeWanted)
        {
            foreach (var kind in pullKinds) since[kind] = fullReconciliation ? 0 : cursors.GetValueOrDefault(kind);
        }

        var snapshot = await FetchSnapshotAsync(peer, reader, link, query with { Since = since }, ct);
        if (snapshot.Problem is { } problem)
        {
            await HandlePeerErrorAsync(link.Id, problem, snapshot.ProblemDetail, null, ct);
            return;
        }

        var manifest = snapshot.Manifest!;
        var fetchedAt = _clock.UtcNow;
        var afterManifest = await RecordAnswerAsync(link.Id, manifest.LibraryEpoch, manifest.ActorId,
            manifest.AppVersion, manifest.ContractVersion, manifest.Attention, manifest.Counterpart, ct);
        if (afterManifest is null || !afterManifest.IsFetchable()) return;
        link = afterManifest;

        // 4. A first contact on the initiator: stage the review once, and say so once (§8.3).
        if (needReview)
        {
            var reviewPull = new DataSyncStagedPull(link.PeerNodeId, link.PeerName, manifest,
                snapshot.Kinds.Where(k => reviewKinds.Contains(k.Kind)).ToList(), fetchedAt);
            var entry = reviews.Stage(link.Id, link.Mode == DataSyncLinkMode.Off, reviewPull);
            var staged = await _links.MutateAsync(link.Id, row =>
            {
                row.ReviewId = entry.ReviewId;
                return DataSyncLinkWrite.Bookkeeping;
            }, ct);
            if (staged is not null) await ObserveAsync(o => o.ReviewReadyAsync(staged, entry, ct));
        }

        // 5. Every other pull waits for DataSyncApply.
        if (mergeWanted)
        {
            var mergePull = new DataSyncStagedPull(link.PeerNodeId, link.PeerName, manifest,
                snapshot.Kinds.Where(k => pullKinds.Contains(k.Kind)).ToList(), fetchedAt);
            if (mergePull.Kinds.Count > 0)
            {
                if (!_stagedPulls.PutSized(link.Id, mergePull, snapshot.Bytes))
                {
                    await HandlePeerErrorAsync(link.Id, DataSyncPeerErrorCode.TooLarge, "stagedPull", null, ct);
                    return;
                }

                await _launcher.EnqueueApplyAsync();
            }
        }

        await RescheduleAsync(link.Id, DataSyncSchedule.PollInterval, ct);
    }

    /// <summary>
    /// A manifest and every page of the kinds it names (§8.10.2 steps 2–3), under the caller's per-peer fetch lock. A
    /// snapshot that expired or a cursor superseded mid-read restarts once with a new manifest; any other problem
    /// discards the whole pull, so nothing is ever planned from an incomplete snapshot.
    /// </summary>
    public async Task<DataSyncFetchedSnapshot> FetchSnapshotAsync(IDataSyncPeerClient peer,
        IDataSyncKindPageReader reader, DataSyncLinkDbModel link, DataSyncFeedQuery query, CancellationToken ct)
    {
        for (var attempt = 0;; attempt++)
        {
            try
            {
                return await ReadSnapshotAsync(peer, reader, link, query, ct);
            }
            catch (DataSyncPeerException e) when (attempt == 0 &&
                                                  e.Code is DataSyncPeerErrorCode.SnapshotExpired
                                                      or DataSyncPeerErrorCode.CursorSuperseded)
            {
                _logger.LogInformation("Data sync restarts the fetch from {Peer}: {Code}", link.PeerNodeId, e.Code);
            }
        }
    }

    private async Task<DataSyncFetchedSnapshot> ReadSnapshotAsync(IDataSyncPeerClient peer,
        IDataSyncKindPageReader reader, DataSyncLinkDbModel link, DataSyncFeedQuery query, CancellationToken ct)
    {
        var manifest = await peer.GetManifestAsync(link.PeerNodeId, query, ct);
        if (BreakerOf(link, manifest.NodeId, manifest.LibraryEpoch) is not null)
            return new DataSyncFetchedSnapshot(null, [], 0, DataSyncPeerErrorCode.PeerReset, "manifest");

        var kinds = new List<DataSyncStagedKind>();
        long bytes = 0;
        foreach (var kind in DataSyncKindIds.All.Where(query.Since.ContainsKey))
        {
            var manifestKind = manifest.Kinds.FirstOrDefault(k => k.Kind == kind);
            if (manifestKind is null) continue;
            var full = query.Since[kind] == 0 || manifestKind.CursorSuperseded;
            var assembly = reader.Begin(kind, manifest.SnapshotId, manifestKind, full);
            string? cursor = null;
            var cursorsSeen = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var page = await peer.GetPageAsync(link.PeerNodeId, manifest.SnapshotId, kind, manifestKind.SinceSeq,
                    cursor, ct);
                bytes += page.Length;
                if (bytes > _limits.MaxStagedPullBytes)
                    return new DataSyncFetchedSnapshot(null, [], bytes, DataSyncPeerErrorCode.TooLarge, "stagedPull");
                var step = assembly.Add(page);
                if (!step.Ok) return Discarded(assembly.Problem, bytes);
                if (step.Complete) break;
                if (step.NextCursor is null || !cursorsSeen.Add(step.NextCursor))
                    return Discarded(DataSyncKindPageReader.Corrupted, bytes);
                cursor = step.NextCursor;
            }

            var staged = assembly.Complete();
            if (staged is null) return Discarded(assembly.Problem, bytes);
            kinds.Add(staged);
        }

        return new DataSyncFetchedSnapshot(manifest, kinds, bytes, null, null);
    }

    private static DataSyncFetchedSnapshot Discarded(string? problem, long bytes) =>
        new(null, [], bytes, problem == "tooLarge" ? DataSyncPeerErrorCode.TooLarge : DataSyncPeerErrorCode.InvalidResponse,
            problem ?? DataSyncKindPageReader.Corrupted);

    /// <summary>
    /// A link waiting for access (§8.1): the grant arrived (normally raised by the claim loop within 5 s), or the
    /// request this device filed ended — the link then stops and stays on the map with Dismiss.
    /// </summary>
    private async Task CheckAccessAsync(DataSyncLinkDbModel link, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IDataSyncGrantService>();
        if (await grants.HasOutboundGrantAsync(link.PeerNodeId, ct))
        {
            await _links.OnOutboundGrantedAsync(link.PeerNodeId, ct);
            return;
        }

        if (link.PendingRequestId is { } requestId)
        {
            var request = (await grants.GetRequestsAsync(ct)).FirstOrDefault(r =>
                r.Direction == DataSyncRequestDirection.Outgoing &&
                string.Equals(r.RequestId, requestId, StringComparison.Ordinal));
            var ended = request?.Status?.ToLowerInvariant() switch
            {
                "rejected" => DataSyncLinkService.AccessRejected,
                "expired" => DataSyncLinkService.AccessExpired,
                "cancelled" or "canceled" => DataSyncLinkService.AccessCancelled,
                _ => null,
            };
            if (request is not null && ended is null && request.ExpiresAt <= _clock.UtcNow &&
                request.Status?.ToLowerInvariant() is "pending" or "awaitingapproval")
            {
                ended = DataSyncLinkService.AccessExpired;
            }

            if (ended is not null)
            {
                await _links.OnRequestEndedAsync(link.Id, ended, ct);
                return;
            }
        }

        // The approver's failed read-back has no request to wait for: "Try again" sends one (§7.2.4).
        await RescheduleAsync(link.Id,
            link.PendingRequestId is null ? DataSyncSchedule.AccessRetry : DataSyncSchedule.PollInterval, ct);
    }

    /// <summary>How a failed peer call changes the link (§7.6, §8.1, §8.2).</summary>
    private async Task HandlePeerErrorAsync(int linkId, DataSyncPeerErrorCode code, string? detail,
        int? retryAfterSeconds, CancellationToken ct)
    {
        switch (code)
        {
            case DataSyncPeerErrorCode.PeerReset:
            case DataSyncPeerErrorCode.IdentityConflict:
                await _links.PauseAsync(linkId, DataSyncPauseReason.PeerReset,
                    System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(code.ToString()), ct);
                return;
            case DataSyncPeerErrorCode.AccessMissing:
            case DataSyncPeerErrorCode.AccessRevoked:
                await SetPeerErrorStateAsync(linkId, DataSyncLinkState.AccessRevoked, code,
                    DataSyncSchedule.AccessRetry, ct);
                return;
            case DataSyncPeerErrorCode.PeerSharingOff:
                await SetPeerErrorStateAsync(linkId, DataSyncLinkState.PeerSharingOff, code,
                    DataSyncSchedule.AccessRetry, ct);
                return;
            case DataSyncPeerErrorCode.PeerRemoteAccessOff:
                await SetPeerErrorStateAsync(linkId, DataSyncLinkState.PeerRemoteAccessOff, code,
                    DataSyncSchedule.AccessRetry, ct);
                return;
            case DataSyncPeerErrorCode.PeerTooOld:
                await SetPeerErrorStateAsync(linkId, DataSyncLinkState.PeerTooOld, code,
                    DataSyncSchedule.VersionRetry, ct);
                return;
            case DataSyncPeerErrorCode.ThisTooOld:
                await SetPeerErrorStateAsync(linkId, DataSyncLinkState.ThisTooOld, code,
                    DataSyncSchedule.VersionRetry, ct);
                return;
            case DataSyncPeerErrorCode.PeerRestorePending:
            {
                // The peer waits for a restore choice: attention, never a failure (§7.5.2).
                var now = _clock.UtcNow;
                await _links.MutateAsync(linkId, row =>
                {
                    row.LastErrorCode = code.ToCode();
                    row.LastErrorDetail = null;
                    row.LastAttemptAtUtc = now;
                    row.NextAttemptAtUtc = now + DataSyncSchedule.AccessRetry;
                    return DataSyncLinkWrite.Bookkeeping;
                }, ct);
                return;
            }
            case DataSyncPeerErrorCode.TooLarge:
            {
                // Not retryable as such: the peer offers more than one sync can carry (§7.5.2).
                var now = _clock.UtcNow;
                await _links.MutateAsync(linkId, row =>
                {
                    row.ConsecutiveFailures++;
                    row.LastErrorCode = code.ToCode();
                    row.LastErrorDetail = DataSyncLinkService.Truncate(detail);
                    row.LastAttemptAtUtc = now;
                    row.NextAttemptAtUtc = now + DataSyncSchedule.AccessRetry;
                    return DataSyncLinkWrite.Bookkeeping;
                }, ct);
                return;
            }
            default:
                await _links.RecordFailureAsync(linkId, code.ToCode(), detail, retryAfterSeconds, ct);
                return;
        }
    }

    /// <summary>
    /// Sets one of the peer error states of §8.1 (retried hourly or every 6 h; back when the peer answers). A link a
    /// person or a breaker paused or stopped meanwhile keeps its state.
    /// </summary>
    /// <param name="head">The head that answered, when the peer answered: its versions are shown with the state.</param>
    private async Task SetPeerErrorStateAsync(int linkId, DataSyncLinkState state, DataSyncPeerErrorCode code,
        TimeSpan retry, CancellationToken ct, DataSyncFeedHead? head = null)
    {
        var now = _clock.UtcNow;
        await _links.MutateAsync(linkId, row =>
        {
            if (head is not null)
            {
                row.PeerContractVersion = head.ContractVersion;
                row.PeerAppVersion = head.AppVersion;
            }

            row.LastErrorCode = code.ToCode();
            row.LastErrorDetail = null;
            row.LastAttemptAtUtc = now;
            row.NextAttemptAtUtc = now + retry;
            if (row.State is DataSyncLinkState.Paused or DataSyncLinkState.Stopped || row.State == state)
                return DataSyncLinkWrite.Bookkeeping;
            row.State = state;
            return DataSyncLinkWrite.Transition;
        }, ct);
    }

    /// <summary>
    /// What a head or manifest says about the peer: its epoch (kept from the first answer), actor, version,
    /// attention and counterpart. A peer that answers brings the link out of a peer error state.
    /// </summary>
    private Task<DataSyncLinkDbModel?> RecordAnswerAsync(int linkId, string libraryEpoch, string actorId,
        string appVersion, int contractVersion, DataSyncSourceAttention attention, DataSyncFeedCounterpart? counterpart,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        return _links.MutateAsync(linkId, row =>
        {
            var write = DataSyncLinkWrite.Bookkeeping;
            row.PeerLibraryEpoch ??= libraryEpoch;
            row.PeerActorId = actorId;
            row.PeerAppVersion = appVersion;
            row.PeerContractVersion = contractVersion;
            row.SetPeerAttention(attention);
            row.SetCounterpart(counterpart);
            row.LastAttemptAtUtc = now;
            if (IsFetchError(row.LastErrorCode))
            {
                row.LastErrorCode = null;
                row.LastErrorDetail = null;
                row.ConsecutiveFailures = 0;
            }

            if (row.State.IsPeerErrorState())
            {
                row.State = row.GetResumeState();
                write = DataSyncLinkWrite.Transition;
            }

            return write;
        }, ct);
    }

    private Task<DataSyncLinkDbModel?> RescheduleAsync(int linkId, TimeSpan delay, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        return _links.MutateAsync(linkId, row =>
        {
            row.LastAttemptAtUtc = now;
            row.NextAttemptAtUtc = now + delay;
            if (IsFetchError(row.LastErrorCode))
            {
                row.LastErrorCode = null;
                row.LastErrorDetail = null;
                row.ConsecutiveFailures = 0;
            }

            return DataSyncLinkWrite.Bookkeeping;
        }, ct);
    }

    /// <summary>
    /// B1 (§8.7): the answer comes from another node, or the peer's library epoch is not the one this link knows.
    /// Returns the pause detail, or null.
    /// </summary>
    private static string? BreakerOf(DataSyncLinkDbModel link, string nodeId, string libraryEpoch)
    {
        if (!string.Equals(nodeId, link.PeerNodeId, StringComparison.Ordinal)) return "nodeChanged";
        if (link.PeerLibraryEpoch is { } epoch && !string.Equals(epoch, libraryEpoch, StringComparison.Ordinal))
            return "epochChanged";
        return null;
    }

    /// <summary>A full reconciliation is due when the last one (else the first contact) is more than 24 h old (§8.8).</summary>
    private static bool IsFullReconciliationDue(DataSyncLinkDbModel link, DateTime nowUtc)
    {
        var last = link.LastFullReconciliationAtUtc ?? link.FirstContactCompletedAtUtc ?? link.CreatedAtUtc;
        return nowUtc - last >= DataSyncSchedule.FullReconciliationInterval;
    }

    private static bool IsFetchError(string? code) =>
        code is DataSyncLinkService.FetchFailed || Enum.TryParse<DataSyncPeerErrorCode>(code, out _);

    private static string? ActorOf(DataSyncLocalStateDbModel? local) =>
        DataSyncActorId.IsValid(local?.ActorId) ? local!.ActorId : null;

    private static string? Serialize<T>(T? value) where T : class =>
        value is null ? null : System.Text.Json.JsonSerializer.Serialize(value, Bakabase.Modules.DataSync.Canonical.DataSyncJson.Options);

    private Task ObserveChangedAsync(DataSyncLinkDbModel link, CancellationToken ct) =>
        ObserveAsync(o => o.LinkChangedAsync(link, ct));

    private async Task ObserveAsync(Func<IDataSyncRuntimeObserver, Task> call)
    {
        try
        {
            await call(_observer);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "A data sync observer failed");
        }
    }
}
