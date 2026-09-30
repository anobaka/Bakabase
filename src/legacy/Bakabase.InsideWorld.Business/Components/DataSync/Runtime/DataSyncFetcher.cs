using System;
using System.Collections.Concurrent;
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
    DataSyncPeerErrorCode? Problem, string? ProblemDetail);

/// <summary>
/// The fetch half of a cycle (§8.10.2), run by the <c>DataSync</c> task outside the gate and outside any transaction.
/// Per due link: the head poll with the contract checks and breakers B1/B1b, the peer facts and the actor evidence it
/// carries, the approver's wait for its peer's first sync, then — only when the link's kinds changed, a fallback or a
/// full reconciliation is due, or a first sync needs its snapshot — one manifest and its pages. A first sync's snapshot
/// is staged once, for the person's Start (§8.3); every other pull is staged for <c>DataSyncApply</c>, and no pull is
/// fetched again while an equal one still waits for the apply. With nothing new, pending records that wait for a
/// re-merge anyway (§8.4) are handed to <c>DataSyncApply</c> without a pull. The apply is enqueued only once the actor is verified (§5.6): the
/// scheduler enqueues it on the tick that verifies. A head counts for that verification only once the evidence it
/// carried was reported, and "Pause all" (§8.7) is read again before each link and before a pull is staged.
/// </summary>
public sealed class DataSyncFetcher
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DataSyncLinkService _links;
    private readonly DataSyncTaskLauncher _launcher;
    private readonly DataSyncRuntimeState _state;
    private readonly IDataSyncRuntimeObserver _observer;
    private readonly IDataSyncClock _clock;
    private readonly DataSyncLimits _limits;
    private readonly ILogger<DataSyncFetcher> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _peerLocks = new(StringComparer.Ordinal);

    /// <summary>How long a fetch waits for another fetch of the same peer before it answers Busy (§7.6).</summary>
    internal TimeSpan FetchWait { get; set; } = TimeSpan.FromSeconds(30);

    public DataSyncFetcher(IServiceScopeFactory scopes, DataSyncLinkService links, DataSyncTaskLauncher launcher,
        DataSyncRuntimeState state, IDataSyncRuntimeObserver observer, IDataSyncClock clock, DataSyncLimits limits,
        ILogger<DataSyncFetcher> logger)
    {
        _scopes = scopes;
        _links = links;
        _launcher = launcher;
        _state = state;
        _observer = observer;
        _clock = clock;
        _limits = limits;
        _logger = logger;
    }

    /// <summary>
    /// One run of the <c>DataSync</c> task: every due link in turn, then the actor verification when that was every
    /// Active link (§5.6) and, once a day, retention (§4.6). A link's failure is recorded on the link and never stops
    /// the others. "Pause all" (§8.7 "Other pauses") takes effect between links: the local state row is read again
    /// before each one.
    /// </summary>
    public async Task RunCycleAsync(BTaskArgs args)
    {
        var ct = args.CancellationToken;
        var now = _clock.UtcNow;
        var fallback = _state.TakeFallbackDue(now);

        var local = await ReadLocalStateAsync(ct);
        var askedEveryActive = false;
        if (local?.AllPaused != true)
        {
            var links = await _links.GetLinksAsync(ct);
            var due = links
                .Where(l =>
                {
                    // A peer discovery or a session saw (§8.2) is due now; the mark is taken whether or not it was
                    // due anyway.
                    var woken = _state.TakeWoken(l.Id);
                    return l.IsFetchable() && (woken || _state.IsDue(l.Id, now) ||
                                               (fallback && l.GetCursors().Count > 0));
                })
                .OrderBy(l => l.Id)
                .ToList();
            for (var i = 0; i < due.Count; i++)
            {
                await args.YieldAsync();
                if (i > 0) local = await ReadLocalStateAsync(ct);
                if (local?.AllPaused == true) break;
                var index = i;
                await args.UpdateTask(t =>
                {
                    t.Percentage = index * 100 / due.Count;
                    t.Process = $"{index}/{due.Count}";
                });
                await FetchLinkAsync(due[i], fallback, local, ct, args);
                askedEveryActive = i == due.Count - 1 &&
                                   links.All(l => !l.IsRunning() || due.Any(d => d.Id == l.Id));
            }
        }

        // §5.6: one cycle asked every Active link's peer in turn: it answered a head, whose evidence was reported before
        // its fetch went on, or could not be reached. Otherwise the guard verifies two minutes after the start.
        if (askedEveryActive)
        {
            await using var scope = _scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IDataSyncActorGuard>().MarkVerified();
        }

        if (_state.TakeRetentionDue(_clock.UtcNow))
        {
            await args.YieldAsync();
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<Persistence.DataSyncRetention>().RunAsync(_clock.UtcNow, ct);
        }
    }

    /// <summary>The fetch half for one link. Failures are recorded on the link; only cancellation escapes.</summary>
    /// <param name="args">The <c>DataSync</c> task's arguments, when it runs there: a pause then takes effect between
    /// the pages of a snapshot, not only between links.</param>
    public async Task FetchLinkAsync(DataSyncLinkDbModel link, bool fallback, DataSyncLocalStateDbModel? local,
        CancellationToken ct, BTaskArgs? args = null)
    {
        try
        {
            if (link.State == DataSyncLinkState.AwaitingAccess)
            {
                await CheckAccessAsync(link, ct);
                return;
            }

            // One fetch per peer at a time (§7.6), from the head to the last page: a second manifest would discard at
            // the source the snapshot being read. Every fetch of a peer comes through here, and nothing else calls
            // the peer's feed.
            var peerLock = _peerLocks.GetOrAdd(link.PeerNodeId, _ => new SemaphoreSlim(1, 1));
            if (!await peerLock.WaitAsync(FetchWait, ct))
                throw new DataSyncPeerException(DataSyncPeerErrorCode.Busy, "fetchInProgress");
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var peer = scope.ServiceProvider.GetRequiredService<IDataSyncPeerClient>();
                await FetchLockedAsync(scope.ServiceProvider, peer, link, fallback, local, ct, args);
            }
            finally
            {
                peerLock.Release();
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

    private async Task FetchLockedAsync(IServiceProvider sp, IDataSyncPeerClient peer, DataSyncLinkDbModel link,
        bool fallback, DataSyncLocalStateDbModel? local, CancellationToken ct, BTaskArgs? args)
    {
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

        // Version skew (§8.12): no pulls; checked again every 6 h.
        if (head.ContractVersion < DataSyncContract.MinimumPeerVersion ||
            DataSyncContract.Version < head.MinimumPeerContract)
        {
            await HandlePeerErrorAsync(link.Id, head.ContractVersion < DataSyncContract.MinimumPeerVersion
                ? DataSyncPeerErrorCode.PeerTooOld
                : DataSyncPeerErrorCode.ThisTooOld, null, null, ct, head);
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
                           hk.MaxSeq < c && !hk.CursorSuperseded))
        {
            // B1b: the same node and epoch, but a kind went back below the cursor (§5.6 "Peer restored"). Not when the
            // peer says the cursor is superseded: it found this device's cursor ahead of what it issued, detected its
            // own restore (§7.5.1 step 1) and serves the kind from 0 in the next snapshot, which lowers the cursor.
            await _links.PauseAsync(link.Id, DataSyncPauseReason.PeerReset, DataSyncLinkService.RestoredDetail, ct);
            return;
        }

        if (head.SeenCounter is { } seen && ActorOf(local) is { } selfActor)
        {
            await sp.GetRequiredService<IDataSyncActorGuard>().ReportPeerEvidenceAsync(link.PeerNodeId, selfActor, seen,
                ct);
        }

        var identityChanged = link.PeerLibraryEpoch is null || link.PeerActorId != head.ActorId ||
                              link.PeerContractVersion != head.ContractVersion;
        var peerFactsChanged = link.PeerAttentionJson != Serialize(head.Attention) ||
                               link.CounterpartJson != Serialize(head.Counterpart);
        var updated = await RecordAnswerAsync(link.Id, head.LibraryEpoch, head.ActorId, head.AppVersion,
            head.ContractVersion, head.Attention, head.Counterpart, ct);
        if (updated is null || !updated.IsFetchable()) return;
        if (peerFactsChanged) await ObserveChangedAsync(updated, ct);

        // From here on, the row as that write read it: an apply may have committed cursors, a first contact or kinds
        // since this link was read for the head (§8.10.2), and a pull from the older cursors would be wasted.
        link = updated;
        kinds = link.GetKinds().Where(reader.Supports).ToList();
        cursors = link.GetCursors();
        query = new DataSyncFeedQuery(link.GetDeclaredMode(),
            kinds.ToDictionary(k => k, k => cursors.GetValueOrDefault(k), StringComparer.Ordinal),
            ActorOf(local), link.GetDeclaredState(openItems));

        // The approver waits for the initiator's first sync (§8.3): the head's counterpart says when it is done, so a
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

        // 2. What this cycle needs: a first sync's snapshot once (§8.3), or what the link merges.
        var needPreview = link.State == DataSyncLinkState.AwaitingReview && _state.PeekPreview(link.Id) is null;
        var mergeKinds = link.State == DataSyncLinkState.Active ? kinds.Where(headKinds.ContainsKey).ToList() : [];
        var fullReconciliation = mergeKinds.Count > 0 && IsFullReconciliationDue(link, now);

        // Only the link's kinds with something new are pulled, unless the whole link is: a full reconciliation, the
        // fallback pull, or a peer whose node, epoch, actor or contract changed (§8.2).
        var pullKinds = fullReconciliation || identityChanged || (fallback && cursors.Count > 0)
            ? mergeKinds
            : mergeKinds.Where(k => !cursors.TryGetValue(k, out var c) || headKinds[k].MaxSeq > c ||
                                    headKinds[k].CursorSuperseded).ToList();
        if (pullKinds.Count > 0 && _state.PeekPull(link.Id) is { } waiting)
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

        if (!needPreview && !mergeWanted)
        {
            // Nothing new. Every apply merges the link's pending records again (§8.4), and one runs without a pull only
            // when that may change something: a record waits to be retried, or this device changed its definitions
            // since the link's last apply. The scheduler enqueues it on its next tick, under its rules.
            if (mergeKinds.Count > 0 && _state.PeekPull(link.Id) is null &&
                (await store.GetPendingAsync(link.Id, ct)).Where(p => mergeKinds.Contains(p.Kind)).ToList() is
                { Count: > 0 } pending &&
                (pending.Any(p => p.Reason == DataSyncPendingReason.Retry) ||
                 _state.ChangedSinceMerge(link.Id, local?.LastSeq ?? 0)))
            {
                _state.RequestReMerge(link.Id);
            }

            await RescheduleAsync(link.Id, DataSyncSchedule.PollInterval, ct);
            return;
        }

        // 3. Manifest and pages.
        var since = needPreview
            ? kinds.ToDictionary(k => k, _ => 0L, StringComparer.Ordinal)
            : pullKinds.ToDictionary(k => k, k => fullReconciliation ? 0 : cursors.GetValueOrDefault(k),
                StringComparer.Ordinal);

        // A merge pull with a kind from 0 is a full reconciliation of that kind (§8.8): the link view says so while it
        // is read (§11.6), and once staged, the pull says so until it is applied. A first sync's snapshot is not one.
        using var reconciling = mergeWanted && pullKinds.Any(k => since[k] == 0)
            ? _state.BeginFullReconciliation(link.Id)
            : null;
        var snapshot = await FetchSnapshotAsync(peer, reader, link, query with { Since = since }, ct, args);
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

        // 4. A first sync on the initiator: stage its snapshot for the person's Start, and say so once (§8.3). The
        // preview reads the local state as committed, so it is refreshed first: a device nobody read has none yet, and
        // the preview would miss the name matches among its own definitions.
        if (needPreview)
        {
            try
            {
                await sp.GetRequiredService<Persistence.DataSyncRefreshCoordinator>().EnsureRecentAsync(kinds, ct);
            }
            catch (Exception e) when (e is Persistence.DataSyncGateTimeoutException or DataSyncActorChangedException)
            {
                throw new DataSyncPeerException(DataSyncPeerErrorCode.Busy, "refresh");
            }

            _state.StagePreview(link.Id, new DataSyncStagedPull(link.PeerNodeId, link.PeerName, manifest,
                snapshot.Kinds, fetchedAt, true));
            await ObserveAsync(o => o.ReviewReadyAsync(link, ct));
        }

        // 5. Every other pull waits for DataSyncApply, unless "Pause all" was pressed while this link was fetched
        // (§8.7 "Other pauses"): nothing is staged then, and the unpause makes every link due again.
        if (mergeWanted)
        {
            if ((await store.GetLocalStateAsync(ct))?.AllPaused == true) return;
            var pulled = snapshot.Kinds.Where(k => pullKinds.Contains(k.Kind)).ToList();
            var mergePull = new DataSyncStagedPull(link.PeerNodeId, link.PeerName, manifest, pulled, fetchedAt,
                mergeKinds.All(k => pulled.Any(s => s.Kind == k && s.FullReconciliation)));
            if (mergePull.Kinds.Count > 0)
            {
                _state.StagePull(link.Id, mergePull);
                // New work ends a person's hold on the apply (§8.10.1), whether or not it can start yet.
                _state.ReleaseApply();
                await EnqueueApplyIfVerifiedAsync(sp);
            }
        }

        await RescheduleAsync(link.Id, DataSyncSchedule.PollInterval, ct);
    }

    /// <summary>
    /// A manifest and every page of the kinds it names (§8.10.2 steps 2–3), under the peer's fetch lock. A
    /// snapshot that expired or a cursor superseded mid-read restarts once with a new manifest; any other problem
    /// discards the whole pull, so nothing is ever planned from an incomplete snapshot. A pull still unfinished after
    /// <see cref="DataSyncSchedule.SnapshotDeadline"/> is discarded as <c>Unreachable</c> (<c>timeout</c>).
    /// </summary>
    private async Task<DataSyncFetchedSnapshot> FetchSnapshotAsync(IDataSyncPeerClient peer,
        IDataSyncKindPageReader reader, DataSyncLinkDbModel link, DataSyncFeedQuery query, CancellationToken ct,
        BTaskArgs? args = null)
    {
        var deadline = _clock.UtcNow + DataSyncSchedule.SnapshotDeadline;
        for (var attempt = 0;; attempt++)
        {
            try
            {
                return await ReadSnapshotAsync(peer, reader, link, query, deadline, ct, args);
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
        IDataSyncKindPageReader reader, DataSyncLinkDbModel link, DataSyncFeedQuery query, DateTime deadline,
        CancellationToken ct, BTaskArgs? args)
    {
        if (_clock.UtcNow >= deadline) return TimedOut();
        var manifest = await peer.GetManifestAsync(link.PeerNodeId, query, ct);
        if (BreakerOf(link, manifest.NodeId, manifest.LibraryEpoch) is not null)
            return new DataSyncFetchedSnapshot(null, [], DataSyncPeerErrorCode.PeerReset, "manifest");

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
                // A cooperative checkpoint per page: a large snapshot must not keep a pause waiting (btask.md).
                if (args is not null) await args.YieldAsync();
                else ct.ThrowIfCancellationRequested();
                // The pages' own budget (the reader's count, the staged bytes) bounds what a source sends; this bounds
                // how long it may take to send it, so one link never holds the cycle's other links waiting.
                if (_clock.UtcNow >= deadline) return TimedOut();
                var page = await peer.GetPageAsync(link.PeerNodeId, manifest.SnapshotId, kind, manifestKind.SinceSeq,
                    cursor, ct);
                bytes += page.Length;
                if (bytes > _limits.MaxStagedPullBytes)
                    return new DataSyncFetchedSnapshot(null, [], DataSyncPeerErrorCode.TooLarge, "stagedPull");
                var step = assembly.Add(page);
                if (!step.Ok) return Discarded(assembly.Problem);
                if (step.Complete) break;
                if (step.NextCursor is null || !cursorsSeen.Add(step.NextCursor))
                    return Discarded(DataSyncKindPageReader.Corrupted);
                cursor = step.NextCursor;
            }

            var staged = assembly.Complete();
            if (staged is null) return Discarded(assembly.Problem);
            kinds.Add(staged);
        }

        return new DataSyncFetchedSnapshot(manifest, kinds, null, null);
    }

    /// <summary>
    /// Enqueues <c>DataSyncApply</c> once the actor is verified (§5.6, §8.2). Before that, a started apply could only
    /// sit on the write tasks' conflict keys; the scheduler enqueues it on the tick that verifies.
    /// </summary>
    private async Task EnqueueApplyIfVerifiedAsync(IServiceProvider sp)
    {
        if (!sp.GetRequiredService<IDataSyncActorGuard>().IsVerified) return;
        await _launcher.EnqueueApplyAsync();
    }

    private async Task<DataSyncLocalStateDbModel?> ReadLocalStateAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDataSyncStore>().GetLocalStateAsync(ct);
    }

    private static DataSyncFetchedSnapshot Discarded(string? problem) =>
        new(null, [], problem == "tooLarge" ? DataSyncPeerErrorCode.TooLarge : DataSyncPeerErrorCode.InvalidResponse,
            problem ?? DataSyncKindPageReader.Corrupted);

    /// <summary>A pull past <see cref="DataSyncSchedule.SnapshotDeadline"/>: retried like a peer that did not answer.</summary>
    private static DataSyncFetchedSnapshot TimedOut() => new(null, [], DataSyncPeerErrorCode.Unreachable, "timeout");

    /// <summary>
    /// A link waiting for access (§8.1): the grant arrived (normally raised by the claim loop within 5 s), or the
    /// request this device filed ended — the link then stops and stays on the map with Dismiss. A request this device
    /// no longer lists ended too: the listing drops a request once it expired, and "Done — stop reading" or removing
    /// the device deletes it, so it can never be answered any more; it ends as expired once no grant has arrived.
    /// </summary>
    private async Task CheckAccessAsync(DataSyncLinkDbModel link, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IDataSyncGrantService>();
        var request = link.PendingRequestId is { } requestId
            ? (await grants.GetRequestsAsync(ct)).FirstOrDefault(r =>
                r.Direction == DataSyncRequestDirection.Outgoing &&
                string.Equals(r.RequestId, requestId, StringComparison.Ordinal))
            : null;
        var status = request?.Status?.ToLowerInvariant();
        var granted = status == "granted";
        var waiting = status is "pending" or "awaitingapproval";
        var ended = status switch
        {
            "rejected" => DataSyncLinkService.AccessRejected,
            "expired" => DataSyncLinkService.AccessExpired,
            "cancelled" or "canceled" => DataSyncLinkService.AccessCancelled,
            _ when waiting && request!.ExpiresAt <= _clock.UtcNow => DataSyncLinkService.AccessExpired,
            // Filed, but no longer listed: expired (the listing leaves expired requests out) or deleted with the
            // device's datasync state. Nobody can answer it now.
            _ when link.PendingRequestId is not null && request is null => DataSyncLinkService.AccessExpired,
            _ => null,
        };
        if (granted || await grants.HasOutboundGrantAsync(link.PeerNodeId, ct))
        {
            await _links.OnOutboundGrantedAsync(link.PeerNodeId, ct);
            return;
        }

        if (ended is not null)
        {
            await _links.OnRequestEndedAsync(link.Id, ended, ct);
            return;
        }

        // The approver's failed read-back has no request to wait for: "Try again" sends one (§7.2.4).
        await RescheduleAsync(link.Id,
            link.PendingRequestId is null ? DataSyncSchedule.AccessRetry : DataSyncSchedule.PollInterval, ct);
    }

    /// <summary>
    /// How a failed peer call changes the link (§7.6, §8.1, §8.2). A reset peer pauses it. A peer that refuses it
    /// (<see cref="DataSyncLinkColumns.HasPeerError"/>), waits for a restore choice (attention, never a failure) or
    /// offers more than one sync can carry leaves its code on the link, retried hourly — every 6 h for a version skew —
    /// and cleared when the peer answers again; a new code is a transition, so the status says it. Anything else is a
    /// failure with a backoff.
    /// </summary>
    /// <param name="head">The head that answered, when the peer answered: its versions are shown with the code.</param>
    private async Task HandlePeerErrorAsync(int linkId, DataSyncPeerErrorCode code, string? detail,
        int? retryAfterSeconds, CancellationToken ct, DataSyncFeedHead? head = null)
    {
        if (code is DataSyncPeerErrorCode.PeerReset or DataSyncPeerErrorCode.IdentityConflict)
        {
            await _links.PauseAsync(linkId, DataSyncPauseReason.PeerReset,
                System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(code.ToString()), ct);
            return;
        }

        if (code is not (DataSyncPeerErrorCode.AccessMissing or DataSyncPeerErrorCode.AccessRevoked
            or DataSyncPeerErrorCode.PeerSharingOff or DataSyncPeerErrorCode.PeerRemoteAccessOff
            or DataSyncPeerErrorCode.PeerTooOld or DataSyncPeerErrorCode.ThisTooOld
            or DataSyncPeerErrorCode.PeerRestorePending or DataSyncPeerErrorCode.TooLarge))
        {
            await _links.RecordFailureAsync(linkId, code.ToCode(), detail, retryAfterSeconds, ct);
            return;
        }

        var stored = (code == DataSyncPeerErrorCode.AccessMissing ? DataSyncPeerErrorCode.AccessRevoked : code).ToCode();
        var now = _clock.UtcNow;
        await _links.MutateAsync(linkId, row =>
        {
            if (head is not null)
            {
                row.PeerContractVersion = head.ContractVersion;
                row.PeerAppVersion = head.AppVersion;
            }

            var changed = row.LastErrorCode != stored;
            if (code == DataSyncPeerErrorCode.TooLarge) row.ConsecutiveFailures++;
            row.LastErrorCode = stored;
            row.LastErrorDetail = code == DataSyncPeerErrorCode.TooLarge ? DataSyncLinkService.Truncate(detail) : null;
            return changed ? DataSyncLinkWrite.Transition : DataSyncLinkWrite.Bookkeeping;
        }, ct);
        _state.RecordAttempt(linkId, now, now + (code is DataSyncPeerErrorCode.PeerTooOld
            or DataSyncPeerErrorCode.ThisTooOld ? DataSyncSchedule.VersionRetry : DataSyncSchedule.AccessRetry));
    }

    /// <summary>
    /// What a head or manifest says about the peer: its epoch (kept from the first answer), actor, version,
    /// attention and counterpart. A peer that answers clears a fetch or peer error (a peer error's end is a transition).
    /// Read fresh inside the write, so nothing another writer committed is put back; written only when something
    /// changed.
    /// </summary>
    private Task<DataSyncLinkDbModel?> RecordAnswerAsync(int linkId, string libraryEpoch, string actorId,
        string appVersion, int contractVersion, DataSyncSourceAttention attention, DataSyncFeedCounterpart? counterpart,
        CancellationToken ct) => _links.MutateAsync(linkId, row =>
    {
        var before = (row.PeerLibraryEpoch, row.PeerActorId, row.PeerAppVersion, row.PeerContractVersion,
            row.PeerAttentionJson, row.CounterpartJson);
        var refused = row.HasPeerError();
        row.PeerLibraryEpoch ??= libraryEpoch;
        row.PeerActorId = actorId;
        row.PeerAppVersion = appVersion;
        row.PeerContractVersion = contractVersion;
        row.SetPeerAttention(attention);
        row.SetCounterpart(counterpart);
        var write = ClearFetchError(row) || before != (row.PeerLibraryEpoch, row.PeerActorId, row.PeerAppVersion,
            row.PeerContractVersion, row.PeerAttentionJson, row.CounterpartJson)
            ? DataSyncLinkWrite.Bookkeeping
            : DataSyncLinkWrite.None;
        return refused ? DataSyncLinkWrite.Transition : write;
    }, ct);

    /// <summary>
    /// The fetch half is over for now: a fetch error is cleared, and the link is due again after
    /// <paramref name="delay"/>.
    /// </summary>
    private async Task<DataSyncLinkDbModel?> RescheduleAsync(int linkId, TimeSpan delay, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var link = await _links.MutateAsync(linkId,
            row => ClearFetchError(row) ? DataSyncLinkWrite.Bookkeeping : DataSyncLinkWrite.None, ct);
        _state.RecordAttempt(linkId, now, now + delay);
        return link;
    }

    private static bool ClearFetchError(DataSyncLinkDbModel row)
    {
        if (!IsFetchError(row.LastErrorCode)) return false;
        row.LastErrorCode = null;
        row.LastErrorDetail = null;
        row.ConsecutiveFailures = 0;
        return true;
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
    /// <remarks>
    /// §8.8's other triggers come from elsewhere: a superseded cursor is served from 0 in the same snapshot, a kind
    /// added to a link and a cursor reset (B1, B1b, a restore choice) pull from 0 by their empty cursors. An upgrade of
    /// this build pulls nothing again: the records held for a newer schema are pending records, merged again by the
    /// link's first apply after the start (every record this device did not apply is one, §7.5.5).
    /// </remarks>
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
