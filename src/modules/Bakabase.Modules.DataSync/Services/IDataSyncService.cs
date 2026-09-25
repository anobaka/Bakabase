using Bakabase.Modules.DataSync.Abstractions;

namespace Bakabase.Modules.DataSync.Services;

// §2.10. Every DateTime in these records is UTC, built from the *Utc columns with DateTimeKind.Utc; MVC writes it as
// naked UTC digits and the frontend reads it through parseServerTime. Records are Newtonsoft-safe: no JsonNode, no
// object, no enum-keyed dictionaries.

public enum DataSyncProblemCode
{
    Busy = 1, ApplyInProgress = 2, DecisionsInvalid = 3, PlanChanged = 4, UnknownItem = 5, UnknownKind = 6,
    ReviewExpired = 7, NothingToReview = 8, UndoNotAvailable = 9, PeerUnreachable = 10, AccessMissing = 11,
    AccessRevoked = 12, PeerSharingOff = 13, PeerTooOld = 14, ThisTooOld = 15, PeerReset = 16, LinkNotFound = 17,
    LinkExists = 18, SharingOff = 19, RequestNotFound = 20, InvitationInvalid = 21, InboxItemChanged = 22,
    InboxItemClosed = 23, BackupFailed = 24, NothingSelected = 25, NotAllowedOnThisDevice = 26, ResolveTogether = 27,
    RemoteAccessOff = 28,
}

public sealed record DataSyncProblem(DataSyncProblemCode Code, string? Detail);

public enum DataSyncResumeAction
{
    Resume = 1, ReviewDeletions = 2, ApplyAsUsual = 3, AskAccessAgain = 4, ThisDeviceWins = 5, TakeTheirs = 6,
    StartAnyway = 7,
}

/// <summary>The facade the controller calls [E]. "never gated" methods never wait for the DataSyncGate.</summary>
public interface IDataSyncService
{
    Task<DataSyncOverview> GetOverviewAsync(CancellationToken ct);                                        // never gated
    Task<DataSyncMapView> GetMapAsync(CancellationToken ct);                                              // never gated
    Task<DataSyncProblem?> SetSharingAsync(DataSyncSharingInput input, CancellationToken ct);
    Task<IReadOnlyList<DataSyncPeerCandidate>> GetPeersAsync(bool discover, CancellationToken ct);
    Task<IReadOnlyList<DataSyncLinkView>> GetLinksAsync(CancellationToken ct);                            // never gated
    Task<DataSyncLinkResult> CreateLinkAsync(DataSyncLinkCreateInput input, CancellationToken ct);
    Task<DataSyncLinkResult> UpdateLinkAsync(int linkId, DataSyncLinkUpdateInput input, CancellationToken ct);
    Task<DataSyncLinkResult> PauseLinkAsync(int linkId, CancellationToken ct);
    Task<DataSyncLinkResult> ResumeLinkAsync(int linkId, DataSyncResumeAction action, CancellationToken ct);
    Task<DataSyncTaskStart> SyncNowAsync(int? linkId, CancellationToken ct);
    Task<DataSyncProblem?> ResetLinkAsync(int linkId, CancellationToken ct);      // forgets state, keeps definitions; also "Dismiss"
    Task<DataSyncProblem?> SetAllPausedAsync(bool paused, CancellationToken ct);
    Task<DataSyncProblem?> ForgetAccessAsync(string peerNodeId, CancellationToken ct);                    // "stop reading X"
    Task<DataSyncReviewResult> CreateCopyOnceAsync(DataSyncCopyOnceInput input, CancellationToken ct);
    Task<DataSyncReviewResult> GetReviewAsync(string reviewId, CancellationToken ct);                     // read-only re-plan
    Task<DataSyncReviewResult> RefetchReviewAsync(string reviewId, CancellationToken ct);                 // "Fetch again"

    Task<DataSyncChangePage> GetReviewChangesAsync(string reviewId, string planId, string itemId,
        string? candidateLocalKey, int skip, int take, CancellationToken ct);                             // never gated

    Task<DataSyncApplyStart> ApplyReviewAsync(string reviewId, DataSyncReviewApplyInput input, CancellationToken ct);
    Task<DataSyncReviewCancelResult> CancelReviewApplyAsync(string reviewId, CancellationToken ct);       // never gated
    Task DiscardReviewAsync(string reviewId, CancellationToken ct);
    Task<IReadOnlyList<DataSyncAccessRequestView>> GetRequestsAsync(CancellationToken ct);
    Task<DataSyncRequestResult> ApproveRequestAsync(string requestId, DataSyncApproveInput input, CancellationToken ct);
    Task<DataSyncProblem?> RejectRequestAsync(string requestId, CancellationToken ct);
    Task<DataSyncProblem?> CancelRequestAsync(string requestId, CancellationToken ct);
    Task<IReadOnlyList<DataSyncReaderView>> GetReadersAsync(CancellationToken ct);
    Task<DataSyncProblem?> RevokeReaderAsync(string peerNodeId, CancellationToken ct);
    Task<DataSyncInvitationResult> CreateInvitationAsync(DataSyncInvitationInput input, CancellationToken ct);
    Task<DataSyncInboxPage> GetInboxAsync(DataSyncInboxQuery query, CancellationToken ct);                // never gated
    Task<DataSyncInboxItemView?> GetInboxItemAsync(long id, CancellationToken ct);                        // never gated
    Task<DataSyncTypeChangePreview?> PreviewInboxItemAsync(long id, CancellationToken ct);   // never gated; null unless TypeChange
    Task<DataSyncTaskStart> ResolveAsync(DataSyncResolveBatchInput input, CancellationToken ct);
    Task<IReadOnlyList<DataSyncEntityStatusView>> GetEntitiesAsync(string kind, CancellationToken ct);    // never gated

    Task<DataSyncTaskStart> SetEntitySyncAsync(string kind, string localKey, DataSyncEntitySyncInput input,
        CancellationToken ct);

    Task<IReadOnlyList<DataSyncHistoryEntry>> GetHistoryAsync(CancellationToken ct);                      // never gated
    Task<DataSyncHistoryDetail?> GetHistoryEntryAsync(int id, CancellationToken ct);                      // never gated
    Task<DataSyncUndoPreview> PreviewUndoAsync(int id, CancellationToken ct);                             // never gated
    Task<DataSyncTaskStart> StartUndoAsync(int id, CancellationToken ct);
    Task<DataSyncRestoreView> GetRestoreAsync(CancellationToken ct);
    Task<DataSyncTaskStart> ChooseRestoreAsync(DataSyncRestoreChoice choice, int? linkId, CancellationToken ct);
    Task<DataSyncProblem?> CancelTaskAsync(string taskId, CancellationToken ct);                          // never gated
}
