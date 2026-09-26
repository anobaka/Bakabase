using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Services;

// §2.10 HTTP records. Every DateTime is UTC (see IDataSyncService.cs).

public sealed record DataSyncKindCount(string Kind, int Count);

/// <param name="DatabaseBytes">The size estimate a backup dialog shows (§8.10.4).</param>
/// <remarks>
/// No <c>/data-sync</c> record carries a path on this device's disk, so the secret canary (§12) holds with no
/// exception: the backup folder a dialog names is <c>AppInfo.BackupPath</c> (F77), read from <c>/app/info</c>.
/// </remarks>
public sealed record DataSyncOverview(string DeviceName, string NodeId, bool IsHeadless, bool SharingEnabled,
    RemoteAccessMode RemoteAccessMode, bool CanManageSharing /* this caller may create access, §7.1.5 */,
    bool NewDefinitionsStayLocal, bool AllPaused, IReadOnlyList<DataSyncKindCount> Kinds, DataSyncStatusView Status,
    string? ActiveTaskId, bool RestorePending, int OpenInboxItems, int PendingRequests,
    long DatabaseBytes, IReadOnlyList<string> ReachableAddresses);

public sealed record DataSyncStatusView(DataSyncStatusLevel Level, int OpenItems, int Links, int LinksInStep,
    int PeersNeedingDecisions /* sources whose attention shows open decisions, §7.5.1 */,
    DateTime? LastSyncedAt, string? LastErrorCode);

/// <param name="Enabled">
/// Definitions sharing on or off; null leaves it as it is, so a change of <paramref name="NewDefinitionsStayLocal"/>
/// alone never sends back a sharing value read before sharing changed elsewhere.
/// </param>
/// <param name="EnablePairedRemoteAccess">With <c>Enabled: true</c> only: remote access, pairing required, if off.</param>
public sealed record DataSyncSharingInput(bool? Enabled = null, bool EnablePairedRemoteAccess = false,
    bool? NewDefinitionsStayLocal = null);

/// <param name="StartAnywayAt">WaitingForPeerReview only: from when [Start anyway] is offered (§8.3); UTC.</param>
/// <param name="FullReconciliationRunning">
/// A full reconciliation of the link (§8.8) is being fetched, waits to be applied, or is being applied: "Comparing
/// everything with {{name}}…" (§11.6). Known since this process started; false after a restart until the next one.
/// </param>
public sealed record DataSyncLinkView(int Id, string PeerNodeId, string PeerName, string? PeerAddress, DataSyncLinkMode Mode,
    DataSyncLinkMode LastMode /* the mode the receive arrow turns back on, §11.1 */,
    DataSyncLinkState State, DataSyncPauseReason? PausedReason, string? PausedDetail, DataSyncLinkInitiator Initiator,
    IReadOnlyList<string> Kinds, IReadOnlyList<string>? PeerKinds, DateTime? LastSyncedAt, DateTime? NextAttemptAt,
    string? LastErrorCode, string? LastErrorDetail, int OpenItems, int PendingCount, string? ReviewId,
    string? PeerAppVersion, int? PeerContractVersion, bool PeerMayReadUs, bool ReadBackDeclined,
    string? PeerModeTowardsUs, DateTime? PeerLastReadAt, DataSyncSourceAttention? PeerAttention,
    int ExcludedCount, int HeldCount, int MissingAtPeerCount, bool PeerOnline,
    DateTime? StartAnywayAt = null, bool FullReconciliationRunning = false);

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

/// <summary>
/// A page of "Needs you" (§9). Items come open ones first, then closed ones; newest first (the latest created) within
/// each group. So with <see cref="OpenOnly"/> false, the closed items start at <c>Skip = OpenTotal</c>.
/// </summary>
/// <param name="LocalKey">
/// Only the items of the definition with this local key, together with <see cref="Kind"/> (a local key is unique per
/// kind only): every open conflict of one definition at once, which a resolution must carry together (§9.2), however
/// many other items are open.
/// </param>
public sealed record DataSyncInboxQuery(bool OpenOnly = true, string? PeerNodeId = null, string? Kind = null,
    int Skip = 0, int Take = 100, string? LocalKey = null);

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

/// <summary>
/// One device on the map's sync lines (§11.1). The trailing members say what the link's details on the /data-sync
/// page say, as <see cref="DataSyncLinkView"/> does: the definitions skipped, withheld and no longer offered by the
/// peer, who started the link (null without one), when [Start anyway] is offered, whether a full reconciliation
/// runs, and the error's detail (for <c>ReadBackFailed</c>, the peer error code that says why).
/// </summary>
public sealed record DataSyncMapPeer(string NodeId, string Name, int? LinkId, DataSyncLinkMode Mode, DataSyncLinkMode LastMode,
    DataSyncLinkState? State, DataSyncPauseReason? PausedReason, bool Receiving, bool ReceivingPending,
    bool PeerMayRead, string? PeerMode, IReadOnlyList<string>? PeerKinds, DateTime? PeerLastReadAt,
    DateTime? LastSyncedAt, int OpenItems, DataSyncSourceAttention? Attention, bool ReadBackDeclined,
    string? LastErrorCode, IReadOnlyList<string> Kinds, int ExcludedCount = 0, int HeldCount = 0,
    int MissingAtPeerCount = 0, DataSyncLinkInitiator? Initiator = null, DateTime? StartAnywayAt = null,
    bool FullReconciliationRunning = false, string? LastErrorDetail = null);

/// <summary>An incoming datasync request: a claim, drawn on its own unverified node (M5).</summary>
public sealed record DataSyncMapRequest(string RequestId, string NodeId, string NodeName, string? RemoteAddress,
    DataSyncRequestIntent Intent, DateTime ExpiresAt, bool ClaimsKnownDevice, string? KnownAddress);

/// <summary>This device's own link to a device it has no datasync access to yet, or whose request ended (M5: never vanishes).</summary>
public sealed record DataSyncMapOutgoing(int LinkId, string NodeId, string NodeName, string? Address,
    DataSyncLinkState State, string? Outcome /* "awaitingApproval"|"rejected"|"expired" */, DateTime? ExpiresAt);

public sealed record DataSyncRestoreView(bool Pending, DataSyncPauseReason? Reason, DateTime? DetectedAt,
    int PausedLinks, string? Detail, int? LinkId /* suspected through one link only */, string? EvidenceFromName);

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

/// <remarks>The dialog names the backup folder from <c>AppInfo.BackupPath</c> (see <see cref="DataSyncOverview"/>).</remarks>
public sealed record DataSyncUndoPreview(bool CanUndo, IReadOnlyList<DataSyncUndoPreviewItem> Items,
    DataSyncProblem? Problem);
