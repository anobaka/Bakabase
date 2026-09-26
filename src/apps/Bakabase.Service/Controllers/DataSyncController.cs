using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.Input.DataSync;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>
/// Data sync as this device's own UI (and the headless CLI) manages it (spec §10.1). Every action is a thin call into
/// <see cref="IDataSyncService"/>; expected failures come back as a <see cref="DataSyncProblem"/> with HTTP 200.
/// </summary>
/// <remarks>
/// <para>
/// Management-level, so no action is <c>[RemoteAccessible]</c>: this device's own window, a paired device (the desktop
/// app's relay managing this server included) and, only in Unrestricted mode, an unpaired LAN browser reach it; any
/// other caller gets 403 <c>HostOnly</c>. Nothing here runs on the user's machine, and the routes sit outside
/// <c>/federation</c>, so a node signature never reaches them.
/// </para>
/// <para>
/// The remote-access gate admits an unpaired Unrestricted browser to everything, which is too wide for granting
/// access: such a browser must not mint a node credential that outlives a later switch to RequirePairing. So every
/// action that creates or widens definitions access also requires this device's own window or a paired device
/// (§7.1.5). Where the action always does (sharing on, approving, a code, asking for access again), it otherwise
/// answers <see cref="DataSyncProblemCode.NotAllowedOnThisDevice"/> without calling the service. Where only the
/// service knows whether it will send a request or mint a reciprocal code (links, copy once), the controller passes
/// who is asking and the service refuses exactly those calls. Reducing access (reject, revoke, cancel, sharing off,
/// pause) stays open to every caller the gate admits, so access can always be shut off from wherever a person is.
/// </para>
/// <para>
/// The rule is only as strong as the way to a device key. An unpaired caller of an Enabled server has none: remote
/// access's pairing management is for the host and paired devices only. An unpaired browser of an Unrestricted server
/// can pair itself — it is that server's operator, and where a headless server's pairing requests are answered — so
/// there the rule makes it pair first (a device the server lists and can revoke), not stay out.
/// </para>
/// </remarks>
[Route("~/data-sync")]
public class DataSyncController(IDataSyncService service) : Controller
{
    private const int MaxChangePageSize = 500;

    private static readonly DataSyncProblem NotAllowed = new(DataSyncProblemCode.NotAllowedOnThisDevice, null);
    private static readonly DataSyncLinkResult RefusedLink = new(null, null, null, NotAllowed);

    /// <summary>
    /// This device's own window and the CLI (loopback), or a paired device. A missing context means the remote-access
    /// middleware did not run, which is not a reason to trust the caller.
    /// </summary>
    private bool MayCreateAccess => HttpContext.GetRemoteAccessContext() is {IsLoopback: true} or {IsPaired: true};

    /// <summary>Never gated. <see cref="DataSyncOverview.CanManageSharing"/> is decided here, for this caller.</summary>
    [HttpGet("overview")]
    [SwaggerOperation(OperationId = "GetDataSyncOverview")]
    public async Task<SingletonResponse<DataSyncOverview>> GetOverview(CancellationToken ct)
    {
        var overview = await service.GetOverviewAsync(ct);
        return new SingletonResponse<DataSyncOverview>(overview with {CanManageSharing = MayCreateAccess});
    }

    [HttpGet("map")]
    [SwaggerOperation(OperationId = "GetDataSyncMap")]
    public async Task<SingletonResponse<DataSyncMapView>> GetMap(CancellationToken ct) =>
        new(await service.GetMapAsync(ct));

    /// <summary>Turning sharing on creates access; turning it off never needs more than the gate.</summary>
    [HttpPut("sharing")]
    [SwaggerOperation(OperationId = "SetDataSyncSharing")]
    public async Task<SingletonResponse<DataSyncProblem>> SetSharing([FromBody] DataSyncSharingInput input,
        CancellationToken ct) =>
        new(input.Enabled && !MayCreateAccess ? NotAllowed : await service.SetSharingAsync(input, ct));

    [HttpGet("peers")]
    [SwaggerOperation(OperationId = "GetDataSyncPeers")]
    public async Task<ListResponse<DataSyncPeerCandidate>> GetPeers([FromQuery] bool discover, CancellationToken ct) =>
        new(await service.GetPeersAsync(discover, ct));

    [HttpGet("links")]
    [SwaggerOperation(OperationId = "GetDataSyncLinks")]
    public async Task<ListResponse<DataSyncLinkView>> GetLinks(CancellationToken ct) =>
        new(await service.GetLinksAsync(ct));

    /// <summary>
    /// Creates access when it sends a request or mints a reciprocal code (a peer this device cannot read yet, or a
    /// two-way link the peer cannot read back); which one only the service knows, so it decides for this caller.
    /// </summary>
    [HttpPost("links")]
    [SwaggerOperation(OperationId = "CreateDataSyncLink")]
    public async Task<SingletonResponse<DataSyncLinkResult>> CreateLink([FromBody] DataSyncLinkCreateInput input,
        CancellationToken ct) =>
        new(await service.CreateLinkAsync(input, MayCreateAccess, ct));

    /// <summary>
    /// Turning a link two-way creates access when it mints a reciprocal code; turning a stopped link back on or
    /// changing its kinds never does. The service decides for this caller.
    /// </summary>
    [HttpPut("links/{id:int}")]
    [SwaggerOperation(OperationId = "UpdateDataSyncLink")]
    public async Task<SingletonResponse<DataSyncLinkResult>> UpdateLink(int id,
        [FromBody] DataSyncLinkUpdateInput input, CancellationToken ct) =>
        new(await service.UpdateLinkAsync(id, input, MayCreateAccess, ct));

    [HttpPost("links/{id:int}/pause")]
    [SwaggerOperation(OperationId = "PauseDataSyncLink")]
    public async Task<SingletonResponse<DataSyncLinkResult>> PauseLink(int id, CancellationToken ct) =>
        new(await service.PauseLinkAsync(id, ct));

    /// <summary>
    /// Asking for access again sends a new request, so it counts as creating access. It is B1's "Ask X for access
    /// again" on a peer that looks reset, "Try again" on a link that waits for access (N14, §7.2.4), and "[Ask X to
    /// keep in step]" on a two-way link the peer does not read back (§7.2.3). Start anyway answers
    /// <c>DecisionsInvalid</c> (<c>tooEarly</c>) before <see cref="DataSyncLinkView.StartAnywayAt"/>.
    /// </summary>
    [HttpPost("links/{id:int}/resume")]
    [SwaggerOperation(OperationId = "ResumeDataSyncLink")]
    public async Task<SingletonResponse<DataSyncLinkResult>> ResumeLink(int id,
        [FromBody] DataSyncLinkResumeInputModel model, CancellationToken ct) =>
        new(model.Action == DataSyncResumeAction.AskAccessAgain && !MayCreateAccess
            ? RefusedLink
            : await service.ResumeLinkAsync(id, model.Action, ct));

    /// <summary>Forgets the link's state and keeps every definition; also "Dismiss" on an ended request.</summary>
    [HttpDelete("links/{id:int}")]
    [SwaggerOperation(OperationId = "ResetDataSyncLink")]
    public async Task<SingletonResponse<DataSyncProblem>> ResetLink(int id, CancellationToken ct) =>
        new(await service.ResetLinkAsync(id, ct));

    [HttpPost("sync-now")]
    [SwaggerOperation(OperationId = "SyncDataSyncNow")]
    public async Task<SingletonResponse<DataSyncTaskStart>> SyncNow([FromBody] DataSyncSyncNowInputModel model,
        CancellationToken ct) =>
        new(await service.SyncNowAsync(model.LinkId, ct));

    [HttpPut("paused")]
    [SwaggerOperation(OperationId = "SetDataSyncAllPaused")]
    public async Task<SingletonResponse<DataSyncProblem>> SetAllPaused([FromBody] DataSyncPausedInputModel model,
        CancellationToken ct) =>
        new(await service.SetAllPausedAsync(model.Paused, ct));

    /// <summary>"Stop reading X": drops this device's own credentials for that peer.</summary>
    [HttpDelete("access/{nodeId}")]
    [SwaggerOperation(OperationId = "ForgetDataSyncAccess")]
    public async Task<SingletonResponse<DataSyncProblem>> ForgetAccess(string nodeId, CancellationToken ct) =>
        new(await service.ForgetAccessAsync(nodeId, ct));

    /// <summary>
    /// Creates access when it sends a request. Whether it will depends on access this device already holds, which only
    /// the service knows, so it decides for this caller.
    /// </summary>
    [HttpPost("copy-once")]
    [SwaggerOperation(OperationId = "CreateDataSyncCopyOnce")]
    public async Task<SingletonResponse<DataSyncReviewResult>> CreateCopyOnce([FromBody] DataSyncCopyOnceInput input,
        CancellationToken ct) =>
        new(await service.CreateCopyOnceAsync(input, MayCreateAccess, ct));

    /// <summary>A read-only re-plan; never gated.</summary>
    [HttpGet("reviews/{reviewId}")]
    [SwaggerOperation(OperationId = "GetDataSyncReview")]
    public async Task<SingletonResponse<DataSyncReviewResult>> GetReview(string reviewId, CancellationToken ct) =>
        new(await service.GetReviewAsync(reviewId, ct));

    /// <summary>"Fetch again".</summary>
    [HttpPost("reviews/{reviewId}/refetch")]
    [SwaggerOperation(OperationId = "RefetchDataSyncReview")]
    public async Task<SingletonResponse<DataSyncReviewResult>> RefetchReview(string reviewId, CancellationToken ct) =>
        new(await service.RefetchReviewAsync(reviewId, ct));

    /// <summary>
    /// One plan item's changes beyond what the plan carries inline. <paramref name="itemId"/> travels in the query
    /// because it contains <c>/</c>.
    /// </summary>
    /// <param name="candidate">A candidate's local key, for a Link or NeedsDecision item.</param>
    /// <param name="take">At most 500.</param>
    [HttpGet("reviews/{reviewId}/changes")]
    [SwaggerOperation(OperationId = "GetDataSyncReviewChanges")]
    public async Task<SingletonResponse<DataSyncChangePage>> GetReviewChanges(string reviewId,
        [FromQuery] string planId, [FromQuery] string itemId, [FromQuery] string? candidate, [FromQuery] int skip = 0,
        [FromQuery] int take = MaxChangePageSize, CancellationToken ct = default) =>
        new(await service.GetReviewChangesAsync(reviewId, planId, itemId, candidate, Math.Max(0, skip),
            Math.Clamp(take, 1, MaxChangePageSize), ct));

    /// <summary><c>DecisionErrors</c> and a fresh <c>Plan</c> come back with <c>DecisionsInvalid</c>.</summary>
    [HttpPost("reviews/{reviewId}/apply")]
    [SwaggerOperation(OperationId = "ApplyDataSyncReview")]
    public async Task<SingletonResponse<DataSyncApplyStart>> ApplyReview(string reviewId,
        [FromBody] DataSyncReviewApplyInput input, CancellationToken ct) =>
        new(await service.ApplyReviewAsync(reviewId, input, ct));

    /// <summary>Never gated, so a waiting apply can always be called off.</summary>
    [HttpDelete("reviews/{reviewId}/apply")]
    [SwaggerOperation(OperationId = "CancelDataSyncReviewApply")]
    public async Task<SingletonResponse<DataSyncReviewCancelResult>> CancelReviewApply(string reviewId,
        CancellationToken ct) =>
        new(await service.CancelReviewApplyAsync(reviewId, ct));

    [HttpDelete("reviews/{reviewId}")]
    [SwaggerOperation(OperationId = "DiscardDataSyncReview")]
    public async Task<BaseResponse> DiscardReview(string reviewId, CancellationToken ct)
    {
        await service.DiscardReviewAsync(reviewId, ct);
        return new BaseResponse();
    }

    [HttpGet("requests")]
    [SwaggerOperation(OperationId = "GetDataSyncRequests")]
    public async Task<ListResponse<DataSyncAccessRequestView>> GetRequests(CancellationToken ct) =>
        new(await service.GetRequestsAsync(ct));

    /// <summary>Creates access: issues a datasync grant, and redeems a reciprocal offer when asked to receive back.</summary>
    [HttpPost("requests/{id}/approve")]
    [SwaggerOperation(OperationId = "ApproveDataSyncRequest")]
    public async Task<SingletonResponse<DataSyncRequestResult>> ApproveRequest(string id,
        [FromBody] DataSyncApproveInput input, CancellationToken ct) =>
        new(MayCreateAccess
            ? await service.ApproveRequestAsync(id, input, ct)
            : new DataSyncRequestResult(null, false, NotAllowed));

    [HttpPost("requests/{id}/reject")]
    [SwaggerOperation(OperationId = "RejectDataSyncRequest")]
    public async Task<SingletonResponse<DataSyncProblem>> RejectRequest(string id, CancellationToken ct) =>
        new(await service.RejectRequestAsync(id, ct));

    /// <summary>Withdraws a request this device filed.</summary>
    [HttpDelete("requests/{id}")]
    [SwaggerOperation(OperationId = "CancelDataSyncRequest")]
    public async Task<SingletonResponse<DataSyncProblem>> CancelRequest(string id, CancellationToken ct) =>
        new(await service.CancelRequestAsync(id, ct));

    [HttpGet("readers")]
    [SwaggerOperation(OperationId = "GetDataSyncReaders")]
    public async Task<ListResponse<DataSyncReaderView>> GetReaders(CancellationToken ct) =>
        new(await service.GetReadersAsync(ct));

    /// <summary>Stops a device from reading this device's definitions.</summary>
    [HttpDelete("readers/{nodeId}")]
    [SwaggerOperation(OperationId = "RevokeDataSyncReader")]
    public async Task<SingletonResponse<DataSyncProblem>> RevokeReader(string nodeId, CancellationToken ct) =>
        new(await service.RevokeReaderAsync(nodeId, ct));

    /// <summary>Creates access: a one-time code another device can redeem.</summary>
    [HttpPost("invitations")]
    [SwaggerOperation(OperationId = "CreateDataSyncInvitation")]
    public async Task<SingletonResponse<DataSyncInvitationResult>> CreateInvitation(
        [FromBody] DataSyncInvitationInput input, CancellationToken ct) =>
        new(MayCreateAccess
            ? await service.CreateInvitationAsync(input, ct)
            : new DataSyncInvitationResult(null, NotAllowed));

    /// <summary>
    /// "Needs you"; never gated. Open items first, then closed ones, newest first within each group: with
    /// <paramref name="openOnly"/> false, the closed items start at <c>skip = openTotal</c>.
    /// </summary>
    /// <param name="take">At most 500.</param>
    /// <param name="localKey">
    /// With <paramref name="kind"/>: only that definition's items, so every open conflict of it, which must be
    /// resolved together, comes in one page.
    /// </param>
    [HttpGet("inbox")]
    [SwaggerOperation(OperationId = "GetDataSyncInbox")]
    public async Task<SingletonResponse<DataSyncInboxPage>> GetInbox([FromQuery] bool openOnly = true,
        [FromQuery] string? peerNodeId = null, [FromQuery] string? kind = null, [FromQuery] int skip = 0,
        [FromQuery] int take = 100, [FromQuery] string? localKey = null, CancellationToken ct = default) =>
        new(await service.GetInboxAsync(new DataSyncInboxQuery(openOnly, peerNodeId, kind, skip, take, localKey), ct));

    [HttpGet("inbox/{id:long}")]
    [SwaggerOperation(OperationId = "GetDataSyncInboxItem")]
    public async Task<SingletonResponse<DataSyncInboxItemView>> GetInboxItem(long id, CancellationToken ct) =>
        new(await service.GetInboxItemAsync(id, ct));

    /// <summary>What converting would do here; null data unless the item is a type change.</summary>
    [HttpGet("inbox/{id:long}/preview")]
    [SwaggerOperation(OperationId = "PreviewDataSyncInboxItem")]
    public async Task<SingletonResponse<DataSyncTypeChangePreview>> PreviewInboxItem(long id, CancellationToken ct) =>
        new(await service.PreviewInboxItemAsync(id, ct));

    /// <summary>Every open conflict of one entity must be in the same batch, else <c>ResolveTogether</c>.</summary>
    [HttpPost("inbox/resolve")]
    [SwaggerOperation(OperationId = "ResolveDataSyncInbox")]
    public async Task<SingletonResponse<DataSyncTaskStart>> Resolve([FromBody] DataSyncResolveBatchInput input,
        CancellationToken ct) =>
        new(await service.ResolveAsync(input, ct));

    [HttpGet("entities")]
    [SwaggerOperation(OperationId = "GetDataSyncEntities")]
    public async Task<ListResponse<DataSyncEntityStatusView>> GetEntities([FromQuery] string kind,
        CancellationToken ct) =>
        new(await service.GetEntitiesAsync(kind, ct));

    /// <summary>A side-table change only: the answer's <c>TaskId</c> is null.</summary>
    [HttpPut("entities/{kind}/{localKey}")]
    [SwaggerOperation(OperationId = "SetDataSyncEntitySync")]
    public async Task<SingletonResponse<DataSyncTaskStart>> SetEntitySync(string kind, string localKey,
        [FromBody] DataSyncEntitySyncInput input, CancellationToken ct) =>
        new(await service.SetEntitySyncAsync(kind, localKey, input, ct));

    [HttpGet("history")]
    [SwaggerOperation(OperationId = "GetDataSyncHistory")]
    public async Task<ListResponse<DataSyncHistoryEntry>> GetHistory(CancellationToken ct) =>
        new(await service.GetHistoryAsync(ct));

    [HttpGet("history/{id:int}")]
    [SwaggerOperation(OperationId = "GetDataSyncHistoryEntry")]
    public async Task<SingletonResponse<DataSyncHistoryDetail>> GetHistoryEntry(int id, CancellationToken ct) =>
        new(await service.GetHistoryEntryAsync(id, ct));

    [HttpGet("history/{id:int}/undo-preview")]
    [SwaggerOperation(OperationId = "PreviewDataSyncUndo")]
    public async Task<SingletonResponse<DataSyncUndoPreview>> PreviewUndo(int id, CancellationToken ct) =>
        new(await service.PreviewUndoAsync(id, ct));

    [HttpPost("history/{id:int}/undo")]
    [SwaggerOperation(OperationId = "UndoDataSync")]
    public async Task<SingletonResponse<DataSyncTaskStart>> Undo(int id, CancellationToken ct) =>
        new(await service.StartUndoAsync(id, ct));

    [HttpGet("restore")]
    [SwaggerOperation(OperationId = "GetDataSyncRestore")]
    public async Task<SingletonResponse<DataSyncRestoreView>> GetRestore(CancellationToken ct) =>
        new(await service.GetRestoreAsync(ct));

    /// <summary>"My configuration wins" or "the others' win", after a restore was detected.</summary>
    [HttpPost("restore")]
    [SwaggerOperation(OperationId = "ChooseDataSyncRestore")]
    public async Task<SingletonResponse<DataSyncTaskStart>> ChooseRestore(
        [FromBody] DataSyncRestoreChoiceInputModel model, CancellationToken ct) =>
        new(await service.ChooseRestoreAsync(model.Choice, model.LinkId, ct));

    /// <summary>Never gated.</summary>
    [HttpDelete("tasks/{taskId}")]
    [SwaggerOperation(OperationId = "CancelDataSyncTask")]
    public async Task<SingletonResponse<DataSyncProblem>> CancelTask(string taskId, CancellationToken ct) =>
        new(await service.CancelTaskAsync(taskId, ct));
}
