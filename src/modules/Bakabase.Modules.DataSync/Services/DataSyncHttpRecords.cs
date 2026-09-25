using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Services;

// §2.10 HTTP records. Every DateTime is UTC (see IDataSyncService.cs).

public sealed record DataSyncKindCount(string Kind, int Count);

public sealed record DataSyncOverview(string DeviceName, string NodeId, bool IsHeadless, bool SharingEnabled,
    RemoteAccessMode RemoteAccessMode, bool CanManageSharing /* this caller may create access, §7.1.5 */,
    bool NewDefinitionsStayLocal, bool AllPaused, IReadOnlyList<DataSyncKindCount> Kinds, DataSyncStatusView Status,
    string? ActiveTaskId, bool RestorePending, int OpenInboxItems, int PendingRequests,
    long DatabaseBytes, string BackupPath, IReadOnlyList<string> ReachableAddresses);

public sealed record DataSyncStatusView(DataSyncStatusLevel Level, int OpenItems, int Links, int LinksInStep,
    int PeersNeedingDecisions /* sources whose attention shows open decisions, §7.5.1 */,
    DateTime? LastSyncedAt, string? LastErrorCode);

public sealed record DataSyncSharingInput(bool Enabled, bool EnablePairedRemoteAccess = false,
    bool? NewDefinitionsStayLocal = null);

public sealed record DataSyncLinkView(int Id, string PeerNodeId, string PeerName, string? PeerAddress, DataSyncLinkMode Mode,
    DataSyncLinkMode LastMode /* the mode the receive arrow turns back on, §11.1 */,
    DataSyncLinkState State, DataSyncPauseReason? PausedReason, string? PausedDetail, DataSyncLinkInitiator Initiator,
    IReadOnlyList<string> Kinds, IReadOnlyList<string>? PeerKinds, DateTime? LastSyncedAt, DateTime? NextAttemptAt,
    string? LastErrorCode, string? LastErrorDetail, int OpenItems, int PendingCount, string? ReviewId,
    string? PeerAppVersion, int? PeerContractVersion, bool PeerMayReadUs, bool ReadBackDeclined,
    string? PeerModeTowardsUs, DateTime? PeerLastReadAt, DataSyncSourceAttention? PeerAttention,
    int ExcludedCount, int HeldCount, int MissingAtPeerCount, bool PeerOnline);

/// <param name="Mode">Follow or TwoWay.</param>
public sealed record DataSyncLinkCreateInput(string? PeerNodeId, string? Address, string? Code, DataSyncLinkMode Mode,
    IReadOnlyList<string> Kinds);

public sealed record DataSyncLinkUpdateInput(DataSyncLinkMode? Mode, IReadOnlyList<string>? Kinds);

public sealed record DataSyncLinkResult(DataSyncLinkView? Link, string? RequestId, string? ReviewId, DataSyncProblem? Problem);

/// <summary>Copy once to a known peer, or to a device reached by address and code (the link row starts AwaitingAccess, Mode Off).</summary>
public sealed record DataSyncCopyOnceInput(string? PeerNodeId, string? Address, string? Code, IReadOnlyList<string> Kinds);

public sealed record DataSyncTaskStart(string? TaskId, DataSyncProblem? Problem);

public sealed record DataSyncPeerCandidate(string NodeId, string Name, string? Address, bool Known, bool Discovered,
    int? ContractVersion, bool? SharesDefinitions, bool WeMayRead, bool TheyMayRead, int? LinkId, string? ConnectionState);

public sealed record DataSyncAccessRequestView(string RequestId, DataSyncRequestDirection Direction, string NodeId,
    string NodeName, DataSyncRequestIntent Intent, string Status, DateTime ExpiresAt, string? RemoteAddress,
    bool ClaimsKnownDevice, string? KnownAddress, bool ReplacesExistingAccess);

public sealed record DataSyncApproveInput(bool ReceiveBack, IReadOnlyList<string>? Kinds);

public sealed record DataSyncRequestResult(DataSyncLinkView? CreatedLink, bool ReadBackGranted, DataSyncProblem? Problem);

public sealed record DataSyncGrantView(string NodeId, string Name, DateTime GrantedAt);

public sealed record DataSyncReaderView(string NodeId, string Name, DateTime? GrantedAt, DateTime? LastReadAt,
    string? Mode, string? State, bool UpToDate);

public sealed record DataSyncInvitationInput(bool AllowTwoWay);

public sealed record DataSyncInvitationView(string Code, DateTime ExpiresAt, IReadOnlyList<string> Addresses, bool AllowTwoWay);

public sealed record DataSyncInvitationResult(DataSyncInvitationView? Invitation, DataSyncProblem? Problem);

// Review = v3.1's import screen on a staged pull.

public sealed record DataSyncReviewSource(string NodeId, string Name, string AppVersion, DateTime FetchedAt,
    IReadOnlyList<DataSyncKindCount> Kinds);

public sealed record DataSyncReviewResult(string? ReviewId, int? LinkId, bool CopyOnce, DataSyncLinkMode LinkMode,
    DataSyncReviewState? State, DataSyncReviewSource? Source, DataSyncPlan? Plan /* truncated, v3.1 §7.8 */,
    int? ApplyLogId, string? TaskId, string? LastError, DataSyncProblem? Problem);

public sealed record DataSyncReviewApplyInput(IReadOnlyList<DataSyncPlanDecision> Decisions, bool BackupBeforeDestructive);

/// <summary>v3.1's record, kept: DecisionErrors and a fresh Plan come back on DecisionsInvalid (§10.1, §11.3).</summary>
public sealed record DataSyncApplyStart(string? TaskId, DataSyncProblem? Problem,
    IReadOnlyList<DataSyncDecisionError> DecisionErrors, DataSyncPlan? Plan);

public sealed record DataSyncReviewCancelResult(DataSyncReviewState? State, DataSyncProblem? Problem);

/// <summary>v3.1 §9.3: a page of one plan item's (or candidate's) changes beyond the inline cap.</summary>
public sealed record DataSyncChangePage(string PlanId, IReadOnlyList<DataSyncFieldChange> Changes,
    IReadOnlyList<DataSyncPlanWarning> Warnings, int Total, DataSyncProblem? Problem);

public sealed record DataSyncInboxQuery(bool OpenOnly = true, string? PeerNodeId = null, string? Kind = null,
    int Skip = 0, int Take = 100);

public sealed record DataSyncInboxItemView(long Id, int? LinkId, string? PeerNodeId, string? PeerName, string Kind,
    string? LocalKey, DataSyncInboxItemType Type, DataSyncInboxItemOrigin Origin, string SubjectPath,
    DataSyncInboxPayload Payload, IReadOnlyList<DataSyncInboxAction> AllowedActions, DataSyncInboxAction? DefaultAction,
    string Token, DateTime CreatedAt, DateTime UpdatedAt, DateTime? ClosedAt, DataSyncInboxClosure? Closure,
    DataSyncInboxAction? Action, string? ClosedByName);

public sealed record DataSyncInboxPage(IReadOnlyList<DataSyncInboxItemView> Items, int Total, int OpenTotal);

public sealed record DataSyncResolveInput(long ItemId, DataSyncInboxAction Action, string Token,
    string? CustomValue /* UseCustom: a name, or a child label */,
    string? TargetLocalKey /* Link / KeepWithEntity */, string? TargetRecordKey /* KeepRecordLinked */,
    string? NewName /* KeepBoth */);

public sealed record DataSyncResolveBatchInput(IReadOnlyList<DataSyncResolveInput> Items, bool BackupBeforeDestructive);

public sealed record DataSyncEntityStatusView(string LocalKey, string SyncKey, DataSyncEntitySyncState State,
    bool ChildrenLocal, int LocalOnlyChildren, int HeldChildren, string? OriginNodeId, string? OriginName,
    string? LastEditorName, DateTime? LastSyncedAt, int OpenItems, bool DiffersFromSource /* Follow links */,
    DataSyncHeldReason? HeldAtSource);

/// <summary>ChildrenLocal is a SHARED change (§3.6): it becomes a revision and reaches every linked device.</summary>
public sealed record DataSyncEntitySyncInput(DataSyncEntitySyncState? State, bool? ChildrenLocal,
    IReadOnlyList<string>? AddLocalOnlyChildren, IReadOnlyList<string>? RemoveLocalOnlyChildren);

public sealed record DataSyncMapView(bool SharingEnabled, RemoteAccessMode RemoteAccessMode,
    IReadOnlyList<DataSyncMapPeer> Peers, IReadOnlyList<DataSyncMapRequest> Requests,
    IReadOnlyList<DataSyncMapOutgoing> Outgoing);

public sealed record DataSyncMapPeer(string NodeId, string Name, int? LinkId, DataSyncLinkMode Mode, DataSyncLinkMode LastMode,
    DataSyncLinkState? State, DataSyncPauseReason? PausedReason, bool Receiving, bool ReceivingPending,
    bool PeerMayRead, string? PeerMode, IReadOnlyList<string>? PeerKinds, DateTime? PeerLastReadAt,
    DateTime? LastSyncedAt, int OpenItems, DataSyncSourceAttention? Attention, bool ReadBackDeclined,
    string? LastErrorCode, IReadOnlyList<string> Kinds);

/// <summary>An incoming datasync request: a claim, drawn on its own unverified node (M5).</summary>
public sealed record DataSyncMapRequest(string RequestId, string NodeId, string NodeName, string? RemoteAddress,
    DataSyncRequestIntent Intent, DateTime ExpiresAt, bool ClaimsKnownDevice, string? KnownAddress);

/// <summary>This device's own link to a device it has no datasync access to yet, or whose request ended (M5: never vanishes).</summary>
public sealed record DataSyncMapOutgoing(int LinkId, string NodeId, string NodeName, string? Address,
    DataSyncLinkState State, string? Outcome /* "awaitingApproval"|"rejected"|"expired" */, DateTime? ExpiresAt);

public sealed record DataSyncRestoreView(bool Pending, DataSyncPauseReason? Reason, DateTime? DetectedAt,
    int PausedLinks, string? Detail, int? LinkId /* suspected through one link only */, string? EvidenceFromName,
    string BackupPath);

// History (v3.1 §9.3, adapted).

public enum DataSyncUndoState { Available = 1, Undone = 2, Expired = 3 }

public enum DataSyncUndoAction { Remove = 1, Revert = 2, RemoveAliases = 3, Recreate = 4, Exclude = 5 }

public enum DataSyncUndoBlock { ChangedSinceImport = 1, InUse = 2, AddedOptionsInUse = 3, Missing = 4 }

public sealed record DataSyncHistoryCounts(int Created, int Updated, int Linked, int Unchanged, int Skipped,
    int ChangedSinceReview, int ChangedDuringApply, int Held, int Deleted, int TypeChanged, int Reordered, int Resolved);

public sealed record DataSyncHistoryEntry(int Id, DateTime AppliedAt, DataSyncHistoryKind Kind, int? LinkId,
    string? PeerNodeId, string? PeerName, DataSyncHistoryCounts Counts, DataSyncUndoState UndoState, DateTime? UndoneAt);

public sealed record DataSyncHistoryItem(string ItemId, string Kind, string Name, DataSyncItemOutcome Outcome,
    DataSyncItemAction Action, string? LocalKey, DataSyncPlanItemType Type);

public sealed record DataSyncHistoryDetail(DataSyncHistoryEntry Entry, IReadOnlyList<DataSyncHistoryItem> Items);

/// <param name="RecreatedGetsNewId">A deleted definition this undo re-creates gets a new local id (§8.11).</param>
public sealed record DataSyncUndoPreviewItem(string Kind, string LocalKey, string Name, DataSyncUndoAction Action,
    DataSyncUndoBlock? Blocked, int? ValueCount, bool SettingsMayReferenceIt, bool RecreatedGetsNewId);

/// <param name="BackupPath">The backup folder named in the dialog (§8.10.4, F77).</param>
public sealed record DataSyncUndoPreview(bool CanUndo, IReadOnlyList<DataSyncUndoPreviewItem> Items,
    DataSyncProblem? Problem, string BackupPath);
