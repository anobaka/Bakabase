using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;

namespace Bakabase.Service.Components.DataSync;

/// <summary>
/// Stands in for the data sync facade until the sync runtime registers the real one. Reads answer an empty, switched-off
/// data sync; everything else answers <see cref="Problem"/>, so a caller gets a clear answer instead of a failed
/// request. Registered with <c>TryAdd</c>, so it disappears as soon as the runtime registers a facade.
/// </summary>
/// <remarks>
/// The problem is <see cref="DataSyncProblemCode.ThisTooOld"/> ("update this device"), the one code that says this
/// build cannot do it. <see cref="DataSyncProblemCode.NotAllowedOnThisDevice"/> would be read as the access rule
/// refusing the caller, which is about who asks, not about the build.
/// </remarks>
public sealed class UnavailableDataSyncService(IRemoteAccessService remoteAccess) : IDataSyncService
{
    /// <summary>The status code the placeholder reports (<see cref="DataSyncStatusView.LastErrorCode"/>).</summary>
    public const string NotAvailableCode = "notAvailable";

    public static readonly DataSyncProblem Problem =
        new(DataSyncProblemCode.ThisTooOld, "Data sync is not available in this build yet.");

    private static readonly DataSyncLinkResult LinkProblem = new(null, null, null, Problem);
    private static readonly DataSyncTaskStart TaskProblem = new(null, Problem);

    private static readonly DataSyncReviewResult ReviewProblem =
        new(null, null, false, DataSyncLinkMode.Off, null, null, null, null, null, null, Problem);

    public Task<DataSyncOverview> GetOverviewAsync(CancellationToken ct) =>
        Task.FromResult(new DataSyncOverview(string.Empty, string.Empty, false, false, remoteAccess.GetEffectiveMode(),
            false, false, false, [], new DataSyncStatusView(DataSyncStatusLevel.Off, 0, 0, 0, 0, null, NotAvailableCode),
            null, false, 0, 0, 0, string.Empty, []));

    public Task<DataSyncMapView> GetMapAsync(CancellationToken ct) =>
        Task.FromResult(new DataSyncMapView(false, remoteAccess.GetEffectiveMode(), [], [], []));

    public Task<DataSyncProblem?> SetSharingAsync(DataSyncSharingInput input, CancellationToken ct) => Refuse();

    public Task<IReadOnlyList<DataSyncPeerCandidate>> GetPeersAsync(bool discover, CancellationToken ct) =>
        Empty<DataSyncPeerCandidate>();

    public Task<IReadOnlyList<DataSyncLinkView>> GetLinksAsync(CancellationToken ct) => Empty<DataSyncLinkView>();

    public Task<DataSyncLinkResult> CreateLinkAsync(DataSyncLinkCreateInput input, CancellationToken ct) =>
        Task.FromResult(LinkProblem);

    public Task<DataSyncLinkResult> UpdateLinkAsync(int linkId, DataSyncLinkUpdateInput input, CancellationToken ct) =>
        Task.FromResult(LinkProblem);

    public Task<DataSyncLinkResult> PauseLinkAsync(int linkId, CancellationToken ct) => Task.FromResult(LinkProblem);

    public Task<DataSyncLinkResult> ResumeLinkAsync(int linkId, DataSyncResumeAction action, CancellationToken ct) =>
        Task.FromResult(LinkProblem);

    public Task<DataSyncTaskStart> SyncNowAsync(int? linkId, CancellationToken ct) => Task.FromResult(TaskProblem);

    public Task<DataSyncProblem?> ResetLinkAsync(int linkId, CancellationToken ct) => Refuse();

    public Task<DataSyncProblem?> SetAllPausedAsync(bool paused, CancellationToken ct) => Refuse();

    public Task<DataSyncProblem?> ForgetAccessAsync(string peerNodeId, CancellationToken ct) => Refuse();

    public Task<DataSyncReviewResult> CreateCopyOnceAsync(DataSyncCopyOnceInput input, CancellationToken ct) =>
        Task.FromResult(ReviewProblem);

    public Task<DataSyncReviewResult> GetReviewAsync(string reviewId, CancellationToken ct) =>
        Task.FromResult(ReviewProblem);

    public Task<DataSyncReviewResult> RefetchReviewAsync(string reviewId, CancellationToken ct) =>
        Task.FromResult(ReviewProblem);

    public Task<DataSyncChangePage> GetReviewChangesAsync(string reviewId, string planId, string itemId,
        string? candidateLocalKey, int skip, int take, CancellationToken ct) =>
        Task.FromResult(new DataSyncChangePage(planId, [], [], 0, Problem));

    public Task<DataSyncApplyStart> ApplyReviewAsync(string reviewId, DataSyncReviewApplyInput input,
        CancellationToken ct) =>
        Task.FromResult(new DataSyncApplyStart(null, Problem, [], null));

    public Task<DataSyncReviewCancelResult> CancelReviewApplyAsync(string reviewId, CancellationToken ct) =>
        Task.FromResult(new DataSyncReviewCancelResult(null, Problem));

    public Task DiscardReviewAsync(string reviewId, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<DataSyncAccessRequestView>> GetRequestsAsync(CancellationToken ct) =>
        Empty<DataSyncAccessRequestView>();

    public Task<DataSyncRequestResult> ApproveRequestAsync(string requestId, DataSyncApproveInput input,
        CancellationToken ct) =>
        Task.FromResult(new DataSyncRequestResult(null, false, Problem));

    public Task<DataSyncProblem?> RejectRequestAsync(string requestId, CancellationToken ct) => Refuse();

    public Task<DataSyncProblem?> CancelRequestAsync(string requestId, CancellationToken ct) => Refuse();

    public Task<IReadOnlyList<DataSyncReaderView>> GetReadersAsync(CancellationToken ct) => Empty<DataSyncReaderView>();

    public Task<DataSyncProblem?> RevokeReaderAsync(string peerNodeId, CancellationToken ct) => Refuse();

    public Task<DataSyncInvitationResult> CreateInvitationAsync(DataSyncInvitationInput input, CancellationToken ct) =>
        Task.FromResult(new DataSyncInvitationResult(null, Problem));

    public Task<DataSyncInboxPage> GetInboxAsync(DataSyncInboxQuery query, CancellationToken ct) =>
        Task.FromResult(new DataSyncInboxPage([], 0, 0));

    public Task<DataSyncInboxItemView?> GetInboxItemAsync(long id, CancellationToken ct) =>
        Task.FromResult<DataSyncInboxItemView?>(null);

    public Task<DataSyncTypeChangePreview?> PreviewInboxItemAsync(long id, CancellationToken ct) =>
        Task.FromResult<DataSyncTypeChangePreview?>(null);

    public Task<DataSyncTaskStart> ResolveAsync(DataSyncResolveBatchInput input, CancellationToken ct) =>
        Task.FromResult(TaskProblem);

    public Task<IReadOnlyList<DataSyncEntityStatusView>> GetEntitiesAsync(string kind, CancellationToken ct) =>
        Empty<DataSyncEntityStatusView>();

    public Task<DataSyncTaskStart> SetEntitySyncAsync(string kind, string localKey, DataSyncEntitySyncInput input,
        CancellationToken ct) =>
        Task.FromResult(TaskProblem);

    public Task<IReadOnlyList<DataSyncHistoryEntry>> GetHistoryAsync(CancellationToken ct) =>
        Empty<DataSyncHistoryEntry>();

    public Task<DataSyncHistoryDetail?> GetHistoryEntryAsync(int id, CancellationToken ct) =>
        Task.FromResult<DataSyncHistoryDetail?>(null);

    public Task<DataSyncUndoPreview> PreviewUndoAsync(int id, CancellationToken ct) =>
        Task.FromResult(new DataSyncUndoPreview(false, [], Problem, string.Empty));

    public Task<DataSyncTaskStart> StartUndoAsync(int id, CancellationToken ct) => Task.FromResult(TaskProblem);

    public Task<DataSyncRestoreView> GetRestoreAsync(CancellationToken ct) =>
        Task.FromResult(new DataSyncRestoreView(false, null, null, 0, null, null, null, string.Empty));

    public Task<DataSyncTaskStart> ChooseRestoreAsync(DataSyncRestoreChoice choice, int? linkId, CancellationToken ct) =>
        Task.FromResult(TaskProblem);

    public Task<DataSyncProblem?> CancelTaskAsync(string taskId, CancellationToken ct) => Refuse();

    private static Task<DataSyncProblem?> Refuse() => Task.FromResult<DataSyncProblem?>(Problem);

    private static Task<IReadOnlyList<T>> Empty<T>() => Task.FromResult<IReadOnlyList<T>>([]);
}
