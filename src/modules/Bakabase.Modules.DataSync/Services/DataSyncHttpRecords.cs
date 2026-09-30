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

/// <param name="PendingRequests">Requests from other devices to read this one that wait for an answer here.</param>
/// <param name="Readers">Devices that may read this device's definitions.</param>
/// <param name="LinksToReview">Links whose first sync waits for a review on this device (AwaitingReview).</param>
/// <param name="LinksWaiting">
/// Links that wait for the other device: to let this one read it (AwaitingAccess), or to review its first sync
/// (WaitingForPeerReview). A link whose read-back failed waits for this device's own "Try again" instead: it is a
/// failure, not counted here.
/// </param>
/// <param name="LastErrorCode">The error of the link that set the level.</param>
/// <param name="LastErrorDetail">
/// What that error says beyond its code: for <c>ReadBackFailed</c>, the peer error code that says why (§7.2.4).
/// </param>
public sealed record DataSyncStatusView(DataSyncStatusLevel Level, int OpenItems, int Links, int LinksInStep,
    int PeersNeedingDecisions /* sources whose attention shows open decisions, §7.5.1 */,
    DateTime? LastSyncedAt, string? LastErrorCode, int PendingRequests = 0, int Readers = 0, int LinksToReview = 0,
    int LinksWaiting = 0, string? LastErrorDetail = null);

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
    IReadOnlyList<string> Kinds, IReadOnlyList<string>? PeerKinds, DateTime? LastSyncedAt, string? LastErrorCode,
    string? LastErrorDetail, int OpenItems, int PendingCount, string? PeerAppVersion, int? PeerContractVersion,
    bool PeerMayReadUs, bool ReadBackDeclined,
    string? PeerModeTowardsUs, DateTime? PeerLastReadAt, DataSyncSourceAttention? PeerAttention,
    int ExcludedCount, int HeldCount, int MissingAtPeerCount,
    DateTime? StartAnywayAt = null, bool FullReconciliationRunning = false);

/// <param name="Mode">Follow or TwoWay.</param>
public sealed record DataSyncLinkCreateInput(string? PeerNodeId, string? Address, string? Code, DataSyncLinkMode Mode,
    IReadOnlyList<string> Kinds);

public sealed record DataSyncLinkUpdateInput(DataSyncLinkMode? Mode, IReadOnlyList<string>? Kinds);

public sealed record DataSyncLinkResult(DataSyncLinkView? Link, string? RequestId, DataSyncProblem? Problem);

/// <summary>Copy once to a known peer, or to a device reached by address and code (the link row starts AwaitingAccess, Mode Off).</summary>
public sealed record DataSyncCopyOnceInput(string? PeerNodeId, string? Address, string? Code, IReadOnlyList<string> Kinds);

public sealed record DataSyncTaskStart(string? TaskId, DataSyncProblem? Problem);

public sealed record DataSyncPeerCandidate(string NodeId, string Name, string? Address, bool Known, bool Discovered,
    int? ContractVersion, bool? SharesDefinitions, bool WeMayRead, bool TheyMayRead, int? LinkId);

/// <param name="ClaimsKnownDevice">
/// The request names a device this one knows, and came from another address (both IP literals, see
/// <c>FederationDataSyncGrants.IsElsewhere</c>).
/// </param>
/// <param name="ReplacesExistingAccess">
/// Incoming and waiting: the NodeId it claims already holds a live grant to read this device's definitions, which
/// approving replaces with the request's; a two-way request approved to receive back also replaces how this device
/// reads that NodeId. Said wherever a request is approved, whatever its address says.
/// </param>
public sealed record DataSyncAccessRequestView(string RequestId, DataSyncRequestDirection Direction, string NodeId,
    string NodeName, DataSyncRequestIntent Intent, string Status, DateTime ExpiresAt, string? RemoteAddress,
    bool ClaimsKnownDevice, string? KnownAddress, bool ReplacesExistingAccess);

public sealed record DataSyncApproveInput(bool ReceiveBack, IReadOnlyList<string>? Kinds);

public sealed record DataSyncRequestResult(DataSyncLinkView? CreatedLink, bool ReadBackGranted, DataSyncProblem? Problem);

public sealed record DataSyncGrantView(string NodeId, string Name);

public sealed record DataSyncReaderView(string NodeId, string Name, DateTime? LastReadAt,
    string? Mode, string? State, bool UpToDate);


public sealed record DataSyncInvitationView(string Code, DateTime ExpiresAt, IReadOnlyList<string> Addresses);

public sealed record DataSyncInvitationResult(DataSyncInvitationView? Invitation, DataSyncProblem? Problem);

// The first sync (§8.3): a preview of the ordinary merge over the staged snapshot, then Start.

public sealed record DataSyncReviewSource(string NodeId, string Name, string AppVersion, DateTime FetchedAt,
    IReadOnlyList<DataSyncKindCount> Kinds);

/// <summary>What the first sync would do with one of the peer's definitions (§8.3).</summary>
public enum DataSyncPreviewOutcome
{
    Create = 1, Update = 2, Unchanged = 3, Delete = 4,

    /// <summary>A definition here has its name: a person links it or keeps both (a copy once asks in the preview).</summary>
    NameMatch = 5,

    /// <summary>Anything else a person decides under "Needs you" after Start: a conflict, a type change, …</summary>
    Question = 6,

    Held = 7,

    /// <summary>Kept out of sync: skipped before, or kept on this device only.</summary>
    NotSynced = 8,
}

/// <param name="Key">The peer record's primary key, which a choice names.</param>
/// <param name="Candidates">A name match: the definitions here it may be.</param>
public sealed record DataSyncPreviewEntry(string Kind, string Key, string Name, string? Subtype,
    DataSyncPreviewOutcome Outcome, IReadOnlyList<DataSyncInboxCandidate>? Candidates);

/// <param name="Source">Null while the snapshot is still being fetched.</param>
/// <param name="TaskId">The Start's task, while it waits or runs.</param>
public sealed record DataSyncFirstSyncPreview(int LinkId, bool CopyOnce, DataSyncLinkMode Mode,
    DataSyncLinkState State, DataSyncReviewSource? Source, IReadOnlyList<DataSyncPreviewEntry> Entries,
    string? TaskId, DataSyncProblem? Problem);

public enum DataSyncFirstSyncAction { Skip = 1, Link = 2, KeepBoth = 3 }

/// <param name="LocalKey">Link: the definition here the record is linked to.</param>
public sealed record DataSyncFirstSyncChoice(string Kind, string Key, DataSyncFirstSyncAction Action,
    string? LocalKey = null);

/// <param name="Choices">Skips, and a copy once's answers to its name matches.</param>
public sealed record DataSyncFirstSyncStartInput(IReadOnlyList<DataSyncFirstSyncChoice> Choices);

/// <summary>
/// A page of "Needs you" (§9). Items come open ones first, by definition (kind, then local key, the latest created
/// first within one), then closed ones, newest first; a page never ends inside a definition, whose open conflicts a
/// resolution carries together (§9.2). So with <see cref="OpenOnly"/> false, the closed items start at
/// <c>Skip = OpenTotal</c>.
/// </summary>
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

/// <param name="Peers">Every device data sync has anything to do with, once each.</param>
/// <param name="Requests">Requests other devices filed that wait for an answer here: claims (M5).</param>
public sealed record DataSyncMapView(bool SharingEnabled, RemoteAccessMode RemoteAccessMode,
    IReadOnlyList<DataSyncMapPeer> Peers, IReadOnlyList<DataSyncMapRequest> Requests);

/// <summary>
/// One device as data sync sees it (§11.1, §11.2), the one record the /data-sync page and the device map read: this
/// device's link to it, its grant to read this device with what it declared when it last read (§7.5.6), and this
/// device's own request to it.
/// </summary>
public sealed record DataSyncMapPeer(string NodeId, string Name, DataSyncLinkView? Link, DataSyncReaderView? Reader,
    DataSyncOwnRequest? Request);

/// <summary>
/// This device's own request to read a device's definitions (M5: never vanishes): waiting, with the id [Cancel]
/// withdraws (null for a link whose request is no longer listed), or ended — rejected or expired — until the link is
/// dismissed. Also a request no link carries: "Ask X to keep in step", "Try again", one left after a Reset.
/// </summary>
public sealed record DataSyncOwnRequest(string? RequestId, string Outcome /* "awaitingApproval"|"rejected"|"expired" */,
    DateTime? ExpiresAt, string? Address);

/// <summary>An incoming datasync request: a claim, drawn on its own unverified node (M5).</summary>
/// <param name="ReplacesExistingAccess">
/// A device known under the NodeId the request claims can already read this device's definitions, and approving
/// replaces that access with the request's (as on <see cref="DataSyncAccessRequestView"/>).
/// </param>
public sealed record DataSyncMapRequest(string RequestId, string NodeId, string NodeName, string? RemoteAddress,
    DataSyncRequestIntent Intent, DateTime ExpiresAt, bool ClaimsKnownDevice, string? KnownAddress,
    bool ReplacesExistingAccess = false);

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
