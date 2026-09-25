using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync;

/// <summary>
/// The data sync facade the <c>/data-sync</c> controller and the CLI call (spec §2.10, §10.1). Expected failures come
/// back as a <see cref="DataSyncProblem"/>; unexpected exceptions take the normal error path.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gate.</b> Actions marked "gate" in §10.1 wait for the DataSyncGate at most 30 s and otherwise answer
/// <see cref="DataSyncProblemCode.Busy"/> before changing anything. Reads never wait for it, so they answer while an
/// apply runs. A call that asks a peer for access releases the gate around that network call (§10.1) and writes what
/// the peer answered once it holds it again.
/// </para>
/// <para>
/// <b>GETs never write</b> (F78): every read here only reads; the one read that learns something for scheduling
/// (discovered peers) keeps it in memory.
/// </para>
/// <para>
/// <b>Access</b> (§7.1.5): the controller decides whether the caller may create or widen definitions access; where only
/// this facade knows whether a call will send a request or mint a reciprocal code (links, copy once) it passes that
/// word on, and the link service refuses exactly those calls.
/// </para>
/// </remarks>
public sealed class DataSyncService : IDataSyncService
{
    /// <summary>History older than this cannot be undone (§4.6, §8.11).</summary>
    public static readonly TimeSpan UndoRetention = TimeSpan.FromDays(30);

    private static readonly DataSyncProblem Busy = new(DataSyncProblemCode.Busy, null);

    private readonly IServiceProvider _services;

    /// <param name="services">The request's scope.</param>
    public DataSyncService(IServiceProvider services)
    {
        _services = services;
    }

    private IDataSyncStore Store => _services.GetRequiredService<IDataSyncStore>();
    private IDataSyncGrantService Grants => _services.GetRequiredService<IDataSyncGrantService>();
    private DataSyncLinkService Links => _services.GetRequiredService<DataSyncLinkService>();
    private DataSyncTaskLauncher Launcher => _services.GetRequiredService<DataSyncTaskLauncher>();
    private IDataSyncRuntimeObserver Observer => _services.GetRequiredService<IDataSyncRuntimeObserver>();
    private DataSyncViews Views => new(_services);
    private DateTime Now => _services.GetService<IDataSyncClock>()?.UtcNow ?? DateTime.UtcNow;

    // ---- this device -------------------------------------------------------------------------------------------

    public async Task<DataSyncOverview> GetOverviewAsync(CancellationToken ct)
    {
        var device = await _services.GetRequiredService<IDataSyncDeviceIdentity>().GetAsync(ct);
        var views = Views;
        var snapshot = await views.ReadAsync(ct);
        var kinds = new List<DataSyncKindCount>();
        foreach (var kind in DataSyncKindIds.All)
            kinds.Add(new DataSyncKindCount(kind, (await Store.CountPublishedAsync(kind, ct)).Live));
        var now = Now;
        var requests = (await Grants.GetRequestsAsync(ct))
            .Count(r => r.Direction == DataSyncRequestDirection.Incoming && DataSyncViews.IsPending(r, now));
        var local = snapshot.Local;
        return new DataSyncOverview(device.Name, device.NodeId,
            _services.GetService<IDataSyncHostKind>()?.IsHeadless ?? false, await Grants.IsSharingEnabledAsync(ct),
            await Grants.GetRemoteAccessModeAsync(ct), false, local?.NewDefinitionsStayLocal ?? false,
            local?.AllPaused ?? false, kinds, views.GetStatus(snapshot, views.IsSyncing()), ActiveTaskId(),
            local?.RestoreReason is not null, snapshot.OpenItems.Count, requests, DatabaseBytes(),
            _services.GetService<IDataSyncHostAddresses>()?.GetReachableAddresses() ?? []);
    }

    public Task<DataSyncMapView> GetMapAsync(CancellationToken ct) => Views.GetMapAsync(ct);

    /// <summary>
    /// The switch (§7.1.3), never gated (it is federation state, §10.1), so turning sharing off always works, even
    /// while an apply runs (§7.1.5). With <c>EnablePairedRemoteAccess</c>, remote access is turned on with pairing
    /// required only when it is Disabled. "Share new definitions automatically" is a local state write, so only that
    /// part takes the gate, after the switch: while the gate is busy the switch still applies and the answer is
    /// <c>Busy</c> with the detail <c>newDefinitionsStayLocal</c>; a switch that failed changes nothing else.
    /// </summary>
    public async Task<DataSyncProblem?> SetSharingAsync(DataSyncSharingInput input, CancellationToken ct)
    {
        try
        {
            await Grants.SetSharingEnabledAsync(input.Enabled, input.EnablePairedRemoteAccess, ct);
        }
        catch (DataSyncProblemException e)
        {
            return e.Problem;
        }

        DataSyncProblem? problem = null;
        if (input.NewDefinitionsStayLocal is { } stayLocal)
        {
            await using var gate = await EnterGateAsync(ct);
            problem = gate is null
                ? new DataSyncProblem(DataSyncProblemCode.Busy, "newDefinitionsStayLocal")
                : await Links.SetNewDefinitionsStayLocalAsync(stayLocal, ct);
        }

        await Observer.StateChangedAsync(ct);
        return problem;
    }

    /// <summary>
    /// Devices this one knows, and with <paramref name="discover"/> the ones answering nearby. A discovered peer with
    /// a link makes that link due now (§8.2), in memory only: this is a GET.
    /// </summary>
    public async Task<IReadOnlyList<DataSyncPeerCandidate>> GetPeersAsync(bool discover, CancellationToken ct)
    {
        var peers = await Grants.GetPeersAsync(discover, ct);
        var links = await Store.GetLinksAsync(ct);
        var state = _services.GetService<DataSyncRuntimeState>();
        var result = new List<DataSyncPeerCandidate>();
        foreach (var peer in peers)
        {
            var link = links.FirstOrDefault(l => string.Equals(l.PeerNodeId, peer.NodeId, StringComparison.Ordinal));
            if (discover && peer.Discovered && link is not null) state?.Wake(link.Id);
            result.Add(peer with { LinkId = link?.Id });
        }

        return result;
    }

    // ---- links -------------------------------------------------------------------------------------------------

    public Task<IReadOnlyList<DataSyncLinkView>> GetLinksAsync(CancellationToken ct) => Views.GetLinksAsync(ct);

    /// <summary>
    /// A link this device starts (§8.1). It takes the gate for the row: a request to the peer goes out with the gate
    /// released.
    /// </summary>
    public async Task<DataSyncLinkResult> CreateLinkAsync(DataSyncLinkCreateInput input, bool callerMayCreateAccess,
        CancellationToken ct)
    {
        if (input.Mode is not (DataSyncLinkMode.Follow or DataSyncLinkMode.TwoWay))
            return LinkProblem(DataSyncProblemCode.DecisionsInvalid, "mode");
        DataSyncLinkChange change;
        await using (var gate = await EnterGateAsync(ct))
        {
            if (gate is null) return new DataSyncLinkResult(null, null, null, Busy);
            change = await Links.CreateAsync(new DataSyncLinkCreate(input.PeerNodeId, input.Address, input.Code,
                input.Mode, input.Kinds), callerMayCreateAccess, ct, gate);
        }

        return await ToLinkResultAsync(change, ct);
    }

    public Task<DataSyncLinkResult> UpdateLinkAsync(int linkId, DataSyncLinkUpdateInput input,
        bool callerMayCreateAccess, CancellationToken ct) =>
        GatedLinkAsync(gate => Links.UpdateAsync(linkId, input.Mode, input.Kinds, callerMayCreateAccess, ct, gate), ct);

    public Task<DataSyncLinkResult> PauseLinkAsync(int linkId, CancellationToken ct) =>
        GatedLinkAsync(_ => Links.PauseByUserAsync(linkId, ct), ct);

    /// <summary>
    /// The resume actions (§8.7). Asking for access again creates access — after a reset (B1), as "Try again" for a
    /// link waiting for access (§7.2.4), or as "[Ask {name} to keep in step]" (§7.2.3); the controller already refused
    /// it to a caller that may not, so the facade lets it through.
    /// </summary>
    public Task<DataSyncLinkResult> ResumeLinkAsync(int linkId, DataSyncResumeAction action, CancellationToken ct) =>
        GatedLinkAsync(gate => Links.ResumeAsync(linkId, action, true, ct, gate), ct);

    /// <summary>"Sync now" (§8.2): the link, or every link, is due now and the fetch task starts. Never gated.</summary>
    public async Task<DataSyncTaskStart> SyncNowAsync(int? linkId, CancellationToken ct)
    {
        if (linkId is { } id && await Store.GetLinkAsync(id, ct) is null)
            return new DataSyncTaskStart(null, new DataSyncProblem(DataSyncProblemCode.LinkNotFound, null));
        var taskId = await _services.GetRequiredService<DataSyncScheduler>().SyncNowAsync(linkId, ct);
        return taskId is null
            ? new DataSyncTaskStart(null, new DataSyncProblem(DataSyncProblemCode.Busy, "notRunning"))
            : new DataSyncTaskStart(taskId, null);
    }

    /// <summary>Reset, also "Dismiss" (§8.1): forgets the link's state and keeps every definition.</summary>
    public async Task<DataSyncProblem?> ResetLinkAsync(int linkId, CancellationToken ct)
    {
        await using var gate = await EnterGateAsync(ct);
        return gate is null ? Busy : await Links.ResetAsync(linkId, ct);
    }

    public async Task<DataSyncProblem?> SetAllPausedAsync(bool paused, CancellationToken ct)
    {
        DataSyncProblem? problem;
        await using (var gate = await EnterGateAsync(ct))
        {
            if (gate is null) return Busy;
            problem = await Links.SetAllPausedAsync(paused, ct);
        }

        if (problem is null) await Observer.StateChangedAsync(ct);
        return problem;
    }

    /// <summary>"Done — stop reading X" (§8.1): this device's own credentials only; never library access.</summary>
    public async Task<DataSyncProblem?> ForgetAccessAsync(string peerNodeId, CancellationToken ct)
    {
        try
        {
            await Grants.ForgetOutboundAsync(peerNodeId, ct);
        }
        catch (DataSyncProblemException e)
        {
            return e.Problem;
        }

        await Observer.StateChangedAsync(ct);
        return null;
    }

    /// <summary>
    /// Copy once (§8.1): a link row whose mode stays Off, reviewed like a first link. The fetch is asynchronous, so
    /// the answer names the link and the review follows when staged (not gated).
    /// </summary>
    public async Task<DataSyncReviewResult> CreateCopyOnceAsync(DataSyncCopyOnceInput input, bool callerMayCreateAccess,
        CancellationToken ct)
    {
        var change = await Links.CreateAsync(new DataSyncLinkCreate(input.PeerNodeId, input.Address, input.Code,
            DataSyncLinkMode.Off, input.Kinds, CopyOnce: true), callerMayCreateAccess, ct);
        var link = change.Link;
        if (change.Problem is not null || link is null)
        {
            return new DataSyncReviewResult(null, link?.Id, true, DataSyncLinkMode.Off, null, null, null, null, null,
                null, change.Problem ?? new DataSyncProblem(DataSyncProblemCode.LinkNotFound, null));
        }

        // Fetch now rather than on the next tick; a link waiting for access fetches once it is granted. The answer's
        // TaskId stays the review's apply task, which does not exist yet.
        if (link.State == DataSyncLinkState.AwaitingReview)
            await _services.GetRequiredService<DataSyncScheduler>().SyncNowAsync(link.Id, ct);
        return new DataSyncReviewResult(Views.CurrentReviewId(link), link.Id, true, DataSyncLinkMode.Off, null, null,
            null, null, null, null, null);
    }

    // ---- reviews -----------------------------------------------------------------------------------------------

    public Task<DataSyncReviewResult> GetReviewAsync(string reviewId, CancellationToken ct) =>
        Reviews.GetAsync(reviewId, ct);

    public Task<DataSyncReviewResult> RefetchReviewAsync(string reviewId, CancellationToken ct) =>
        Reviews.RefetchAsync(reviewId, ct);

    public Task<DataSyncChangePage> GetReviewChangesAsync(string reviewId, string planId, string itemId,
        string? candidateLocalKey, int skip, int take, CancellationToken ct) =>
        Reviews.GetChangesAsync(reviewId, planId, itemId, candidateLocalKey, Math.Max(0, skip),
            Math.Clamp(take, 1, 500), ct);

    public async Task<DataSyncApplyStart> ApplyReviewAsync(string reviewId, DataSyncReviewApplyInput input,
        CancellationToken ct)
    {
        await using var gate = await EnterGateAsync(ct);
        return gate is null
            ? new DataSyncApplyStart(null, Busy, [], null)
            : await Reviews.ApplyAsync(reviewId, input, ct);
    }

    public Task<DataSyncReviewCancelResult> CancelReviewApplyAsync(string reviewId, CancellationToken ct) =>
        Reviews.CancelApplyAsync(reviewId, ct);

    public Task DiscardReviewAsync(string reviewId, CancellationToken ct) => Reviews.DiscardAsync(reviewId, ct);

    private DataSyncReviewService Reviews => new(_services);

    // ---- requests, readers, invitations -----------------------------------------------------------------------

    public async Task<IReadOnlyList<DataSyncAccessRequestView>> GetRequestsAsync(CancellationToken ct) =>
        (await Grants.GetRequestsAsync(ct)).Select(r => r with { ExpiresAt = DataSyncViews.Utc(r.ExpiresAt) })
        .ToList();

    /// <summary>
    /// Approves a datasync request (§7.2.4 step 4): only a request this device holds for definitions (a library
    /// request is not visible here, G29), and only with remote access on (G36). A two-way request approved to receive
    /// back makes the approver's link — even when the read-back failed (N14). The link row needs no gate: the link
    /// service orders its writes itself, and the queued grant event that writes the same row takes none either. So once
    /// the grant is issued the answer never waits behind an apply.
    /// </summary>
    public async Task<DataSyncRequestResult> ApproveRequestAsync(string requestId, DataSyncApproveInput input,
        CancellationToken ct)
    {
        var request = (await Grants.GetRequestsAsync(ct)).FirstOrDefault(r =>
            r.Direction == DataSyncRequestDirection.Incoming &&
            string.Equals(r.RequestId, requestId, StringComparison.Ordinal));
        if (request is null) return RequestProblem(DataSyncProblemCode.RequestNotFound, null);
        if (await Grants.GetRemoteAccessModeAsync(ct) == RemoteAccessMode.Disabled)
            return RequestProblem(DataSyncProblemCode.RemoteAccessOff, null);
        if (!await Grants.IsSharingEnabledAsync(ct)) return RequestProblem(DataSyncProblemCode.SharingOff, null);
        if (input.Kinds?.FirstOrDefault(k => !DataSyncKindIds.All.Contains(k)) is { } unknown)
            return RequestProblem(DataSyncProblemCode.UnknownKind, unknown);

        DataSyncApprovalOutcome outcome;
        try
        {
            outcome = await Grants.ApproveAsync(requestId, input.ReceiveBack, ct);
        }
        catch (DataSyncProblemException e)
        {
            return RequestProblem(e.Problem.Code, e.Problem.Detail);
        }
        catch (DataSyncPeerException e)
        {
            return RequestProblem(DataSyncLinkService.ProblemOf(e.Code), e.Code.ToCode());
        }

        _services.GetService<DataSyncNotifier>()?.NoteApproved(outcome.PeerNodeId);
        DataSyncLinkView? view = null;
        if (outcome.Intent == DataSyncRequestIntent.TwoWay && input.ReceiveBack)
        {
            var link = await Links.OnInboundGrantedAsync(outcome.PeerNodeId, outcome.Intent, true,
                outcome.ReadBackGranted, outcome.ReadBackError, outcome.PeerName, input.Kinds, ct);
            if (link is not null && input.Kinds is { Count: > 0 } kinds && link.FirstContactCompletedAtUtc is null)
            {
                // The pairing flow's own grant event may have made the link a moment earlier, with every kind.
                link = await Links.MutateAsync(link.Id, row =>
                {
                    if (row.GetKinds().SequenceEqual(DataSyncKindIds.All.Where(kinds.Contains)))
                        return DataSyncLinkWrite.None;
                    row.SetKinds(kinds);
                    return DataSyncLinkWrite.Transition;
                }, ct);
            }

            if (link is not null) view = await Views.GetLinkAsync(link.Id, ct);
        }

        await Observer.StateChangedAsync(ct);
        return new DataSyncRequestResult(view, outcome.ReadBackGranted, null);
    }

    public async Task<DataSyncProblem?> RejectRequestAsync(string requestId, CancellationToken ct)
    {
        if (!await HasRequestAsync(requestId, DataSyncRequestDirection.Incoming, ct))
            return new DataSyncProblem(DataSyncProblemCode.RequestNotFound, null);
        try
        {
            await Grants.RejectAsync(requestId, ct);
        }
        catch (DataSyncProblemException e)
        {
            return e.Problem;
        }

        await Observer.StateChangedAsync(ct);
        return null;
    }

    /// <summary>
    /// Withdraws a request this device filed. A link made for that request has nothing yet and goes with it; a link
    /// that already had sync state (turned back on after a stop, or a copy once onto a stopped link) stops again with
    /// its bases, pending records and last mode (§8.1), as when the peer's answer ends a request. Not gated: the link
    /// service orders its writes itself.
    /// </summary>
    public async Task<DataSyncProblem?> CancelRequestAsync(string requestId, CancellationToken ct)
    {
        if (!await HasRequestAsync(requestId, DataSyncRequestDirection.Outgoing, ct))
            return new DataSyncProblem(DataSyncProblemCode.RequestNotFound, null);
        try
        {
            await Grants.CancelOutgoingAsync(requestId, ct);
        }
        catch (DataSyncProblemException e)
        {
            return e.Problem;
        }

        var waiting = (await Store.GetLinksAsync(ct)).FirstOrDefault(l =>
            l.State == DataSyncLinkState.AwaitingAccess &&
            string.Equals(l.PendingRequestId, requestId, StringComparison.Ordinal));
        if (waiting is not null) await Links.OnRequestCancelledAsync(waiting.Id, ct);

        await Observer.StateChangedAsync(ct);
        return null;
    }

    public Task<IReadOnlyList<DataSyncReaderView>> GetReadersAsync(CancellationToken ct) => Views.GetReadersAsync(ct);

    public async Task<DataSyncProblem?> RevokeReaderAsync(string peerNodeId, CancellationToken ct)
    {
        try
        {
            await Grants.RevokeAsync(peerNodeId, ct);
        }
        catch (DataSyncProblemException e)
        {
            return e.Problem;
        }

        await Observer.StateChangedAsync(ct);
        return null;
    }

    /// <summary>A code needs both switches, so a code that cannot work is never shown (§7.2.3, G36).</summary>
    public async Task<DataSyncInvitationResult> CreateInvitationAsync(DataSyncInvitationInput input,
        CancellationToken ct)
    {
        if (!await Grants.IsSharingEnabledAsync(ct))
            return new DataSyncInvitationResult(null, new DataSyncProblem(DataSyncProblemCode.SharingOff, null));
        if (await Grants.GetRemoteAccessModeAsync(ct) == RemoteAccessMode.Disabled)
            return new DataSyncInvitationResult(null, new DataSyncProblem(DataSyncProblemCode.RemoteAccessOff, null));
        try
        {
            var invitation = await Grants.CreateInvitationAsync(input, ct);
            return new DataSyncInvitationResult(invitation with { ExpiresAt = DataSyncViews.Utc(invitation.ExpiresAt) },
                null);
        }
        catch (DataSyncProblemException e)
        {
            return new DataSyncInvitationResult(null, e.Problem);
        }
    }

    // ---- the inbox ---------------------------------------------------------------------------------------------

    public Task<DataSyncInboxPage> GetInboxAsync(DataSyncInboxQuery query, CancellationToken ct) =>
        Inbox.GetPageAsync(query, ct);

    public Task<DataSyncInboxItemView?> GetInboxItemAsync(long id, CancellationToken ct) => Inbox.GetItemAsync(id, ct);

    public Task<DataSyncTypeChangePreview?> PreviewInboxItemAsync(long id, CancellationToken ct) =>
        Inbox.PreviewAsync(id, ct);

    public async Task<DataSyncTaskStart> ResolveAsync(DataSyncResolveBatchInput input, CancellationToken ct)
    {
        await using var gate = await EnterGateAsync(ct);
        return gate is null ? new DataSyncTaskStart(null, Busy) : await Inbox.ResolveAsync(input, ct);
    }

    private DataSyncInboxService Inbox => new(_services);

    // ---- entities ----------------------------------------------------------------------------------------------

    public Task<IReadOnlyList<DataSyncEntityStatusView>> GetEntitiesAsync(string kind, CancellationToken ct) =>
        Views.GetEntitiesAsync(kind, ct);

    public async Task<DataSyncTaskStart> SetEntitySyncAsync(string kind, string localKey, DataSyncEntitySyncInput input,
        CancellationToken ct)
    {
        await using var gate = await EnterGateAsync(ct);
        return gate is null
            ? new DataSyncTaskStart(null, Busy)
            : await new DataSyncEntitySettings(_services).SetAsync(kind, localKey, input, gate, ct);
    }

    // ---- history and undo --------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<DataSyncHistoryEntry>> GetHistoryAsync(CancellationToken ct)
    {
        var now = Now;
        return (await Store.GetHistoryAsync(ct)).OrderByDescending(l => l.AppliedAtUtc).ThenByDescending(l => l.Id)
            .Select(l => ToEntry(l, now)).ToList();
    }

    public async Task<DataSyncHistoryDetail?> GetHistoryEntryAsync(int id, CancellationToken ct)
    {
        var log = await Store.GetHistoryEntryAsync(id, ct);
        return log is null ? null : new DataSyncHistoryDetail(ToEntry(log, Now), DataSyncHistoryJson.ReadItems(log.ResultJson));
    }

    /// <summary>
    /// What undoing an entry would do (§8.11); never gated. An undo, an entry already undone and one past retention
    /// cannot be undone; everything else is previewed read-only by the undo planner.
    /// </summary>
    public async Task<DataSyncUndoPreview> PreviewUndoAsync(int id, CancellationToken ct)
    {
        var log = await Store.GetHistoryEntryAsync(id, ct);
        if (UndoRefusal(log) is { } refusal) return new DataSyncUndoPreview(false, [], refusal);
        var previewer = _services.GetService<IDataSyncUndoPreviewer>();
        return previewer is null
            ? new DataSyncUndoPreview(false, [], new DataSyncProblem(DataSyncProblemCode.UndoNotAvailable, null))
            : await previewer.PreviewAsync(log!, ct);
    }

    /// <summary>The check and the enqueue, under the gate: <c>DataSyncUndo:{logId}</c>, retried after an Error.</summary>
    public async Task<DataSyncTaskStart> StartUndoAsync(int id, CancellationToken ct)
    {
        await using var gate = await EnterGateAsync(ct);
        if (gate is null) return new DataSyncTaskStart(null, Busy);
        var log = await Store.GetHistoryEntryAsync(id, ct);
        if (UndoRefusal(log) is { } refusal) return new DataSyncTaskStart(null, refusal);
        if (_services.GetService<IDataSyncUndoPreviewer>() is { } previewer &&
            await previewer.PreviewAsync(log!, ct) is { CanUndo: false } preview)
        {
            return new DataSyncTaskStart(null,
                preview.Problem ?? new DataSyncProblem(DataSyncProblemCode.UndoNotAvailable, null));
        }

        var attempt = await Launcher.EnqueueUndoAsync(id);
        return attempt is null
            ? new DataSyncTaskStart(null, new DataSyncProblem(DataSyncProblemCode.ApplyInProgress, null))
            : new DataSyncTaskStart(attempt.TaskId, null);
    }

    private DataSyncProblem? UndoRefusal(DataSyncApplyLogDbModel? log) => log switch
    {
        null => new DataSyncProblem(DataSyncProblemCode.UndoNotAvailable, "notFound"),
        { UndoneAtUtc: not null } => new DataSyncProblem(DataSyncProblemCode.UndoNotAvailable, "undone"),
        { Kind: DataSyncHistoryKind.Undo } => new DataSyncProblem(DataSyncProblemCode.UndoNotAvailable, "undo"),
        _ when Now - DataSyncViews.Utc(log.AppliedAtUtc) > UndoRetention =>
            new DataSyncProblem(DataSyncProblemCode.UndoNotAvailable, "expired"),
        _ => null,
    };

    private static DataSyncHistoryEntry ToEntry(DataSyncApplyLogDbModel log, DateTime now)
    {
        var state = log.UndoneAtUtc is not null ? DataSyncUndoState.Undone
            : log.Kind == DataSyncHistoryKind.Undo || now - DataSyncViews.Utc(log.AppliedAtUtc) > UndoRetention
                ? DataSyncUndoState.Expired
                : DataSyncUndoState.Available;
        return new DataSyncHistoryEntry(log.Id, DataSyncViews.Utc(log.AppliedAtUtc), log.Kind, log.LinkId,
            log.PeerNodeId, log.PeerName, DataSyncHistoryJson.ReadCounts(log.SummaryJson, log.ResultJson), state,
            DataSyncViews.Utc(log.UndoneAtUtc));
    }

    // ---- restore -----------------------------------------------------------------------------------------------

    /// <summary>
    /// The restore panel (§9.5): detected (every link paused) or suspected through one link, when, and whose evidence —
    /// this device's own records only, or a named peer that has seen newer changes from this device.
    /// </summary>
    public async Task<DataSyncRestoreView> GetRestoreAsync(CancellationToken ct)
    {
        var local = await Store.GetLocalStateAsync(ct);
        var links = await Store.GetLinksAsync(ct);
        var paused = links.Count(l => l is
        {
            State: DataSyncLinkState.Paused,
            PausedReason: DataSyncPauseReason.LocalRestoreDetected or DataSyncPauseReason.LocalRestoreSuspected,
        });
        return new DataSyncRestoreView(local?.RestoreReason is not null, local?.RestoreReason,
            DataSyncViews.Utc(local?.RestoreDetectedAtUtc), paused, local?.RestoreDetail, local?.RestoreLinkId,
            EvidenceFromName(local?.RestoreEvidenceJson, links));
    }

    /// <summary>
    /// "My configuration wins" or "take the others'" (§9.5), both scopes: a restore suspected through one link is
    /// chosen for that link only, a detected one for every link. Enqueued as <c>DataSyncRestore</c> under the gate.
    /// </summary>
    public async Task<DataSyncTaskStart> ChooseRestoreAsync(DataSyncRestoreChoice choice, int? linkId,
        CancellationToken ct)
    {
        if (!Enum.IsDefined(choice)) return TaskProblem(DataSyncProblemCode.DecisionsInvalid, "choice");
        await using var gate = await EnterGateAsync(ct);
        if (gate is null) return new DataSyncTaskStart(null, Busy);
        var local = await Store.GetLocalStateAsync(ct);
        if (local?.RestoreReason is not { } reason) return TaskProblem(DataSyncProblemCode.DecisionsInvalid, "noRestore");
        var scope = reason == DataSyncPauseReason.LocalRestoreSuspected ? local.RestoreLinkId : null;
        if (linkId is { } asked && asked != scope) return TaskProblem(DataSyncProblemCode.DecisionsInvalid, "linkId");
        var attempt = await Launcher.EnqueueRestoreAsync(choice, scope);
        return attempt is null
            ? TaskProblem(DataSyncProblemCode.ApplyInProgress, null)
            : new DataSyncTaskStart(attempt.TaskId, null);
    }

    /// <summary>
    /// The name a restore's evidence came from: the first peer or reader that saw newer changes from this device; null
    /// when only this device's own records say so.
    /// </summary>
    private static string? EvidenceFromName(string? evidenceJson, IReadOnlyList<DataSyncLinkDbModel> links)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson)) return null;
        try
        {
            if (JsonNode.Parse(evidenceJson) is not JsonArray entries) return null;
            foreach (var entry in entries.OfType<JsonObject>())
            {
                if (Text(entry["source"]) is null or "watermark") continue;
                if (Text(entry["name"]) is { Length: > 0 } name) return name;
                if (Text(entry["nodeId"]) is { } nodeId)
                    return links.FirstOrDefault(l => l.PeerNodeId == nodeId)?.PeerName ?? nodeId;
            }
        }
        catch (JsonException)
        {
        }

        return null;

        static string? Text(JsonNode? node) =>
            node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    // ---- tasks -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Cancel (§8.10.1), never gated: the attempt's flag is set before the task's status is read, then a waiting task
    /// is removed or a running one stopped. Only data sync's own tasks.
    /// </summary>
    public async Task<DataSyncProblem?> CancelTaskAsync(string taskId, CancellationToken ct)
    {
        if (!DataSyncTaskIds.IsDataSyncTask(taskId)) return new DataSyncProblem(DataSyncProblemCode.UnknownItem, "task");
        return await Launcher.CancelAsync(taskId) == DataSyncTaskCancelOutcome.NotFound
            ? new DataSyncProblem(DataSyncProblemCode.UnknownItem, "task")
            : null;
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    /// <summary>The gate for a request: at most 30 s, else null (the caller answers Busy).</summary>
    private Task<DataSyncGateHold?> EnterGateAsync(CancellationToken ct) =>
        DataSyncGateHold.TryEnterAsync(_services.GetRequiredService<IDataSyncGateEntry>(),
            DataSyncGateHold.RequestTimeout, ct);

    private async Task<DataSyncLinkResult> GatedLinkAsync(Func<DataSyncGateHold, Task<DataSyncLinkChange>> action,
        CancellationToken ct)
    {
        DataSyncLinkChange change;
        await using (var gate = await EnterGateAsync(ct))
        {
            if (gate is null) return new DataSyncLinkResult(null, null, null, Busy);
            change = await action(gate);
        }

        return await ToLinkResultAsync(change, ct);
    }

    private async Task<DataSyncLinkResult> ToLinkResultAsync(DataSyncLinkChange change, CancellationToken ct)
    {
        var view = change.Link is null ? null : await Views.GetLinkAsync(change.Link.Id, ct);
        return new DataSyncLinkResult(view, change.RequestId, view?.ReviewId, change.Problem);
    }

    private async Task<bool> HasRequestAsync(string requestId, DataSyncRequestDirection direction, CancellationToken ct) =>
        (await Grants.GetRequestsAsync(ct)).Any(r =>
            r.Direction == direction && string.Equals(r.RequestId, requestId, StringComparison.Ordinal));

    private string? ActiveTaskId()
    {
        if (Launcher.GetActiveWriteTaskId() is { } write) return write;
        var fetch = _services.GetService<BTaskManager>()?.GetTaskViewModel(DataSyncTaskIds.Fetch);
        return fetch?.Status.IsActive() == true ? DataSyncTaskIds.Fetch : null;
    }

    /// <summary>The database file's size: what a backup before a destructive decision costs (§8.10.4).</summary>
    private long DatabaseBytes()
    {
        try
        {
            var connection = _services.GetService<BakabaseDbContext>()?.Database.GetConnectionString();
            if (string.IsNullOrEmpty(connection)) return 0;
            var path = new SqliteConnectionStringBuilder(connection).DataSource;
            return !string.IsNullOrEmpty(path) && File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
                                      or InvalidOperationException)
        {
            return 0;
        }
    }

    private static DataSyncLinkResult LinkProblem(DataSyncProblemCode code, string? detail) =>
        new(null, null, null, new DataSyncProblem(code, detail));

    private static DataSyncRequestResult RequestProblem(DataSyncProblemCode code, string? detail) =>
        new(null, false, new DataSyncProblem(code, detail));

    private static DataSyncTaskStart TaskProblem(DataSyncProblemCode code, string? detail) =>
        new(null, new DataSyncProblem(code, detail));
}
