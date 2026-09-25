// Runtime/: links, requests, peers and status. The enums live in the module's root namespace so every
// DataSync namespace sees them (§2.1).
namespace Bakabase.Modules.DataSync;

public enum DataSyncLinkMode { Off = 0, Follow = 1, TwoWay = 2 }

public enum DataSyncLinkState
{
    Active = 1, AwaitingAccess = 2, AwaitingReview = 3, WaitingForPeerReview = 4, Paused = 5, Stopped = 6,
    PeerTooOld = 7, ThisTooOld = 8, AccessRevoked = 9, PeerSharingOff = 10, PeerRemoteAccessOff = 11,
}

public enum DataSyncPauseReason
{
    ByUser = 1, AllPaused = 2, PeerReset = 3, PeerIdentityDuplicated = 4, MassDeletion = 5, KindEmptied = 6,
    LocalRestoreDetected = 7, TooManyDecisions = 8, LocalRestoreSuspected = 9,
}

public enum DataSyncLinkInitiator { ThisDevice = 1, Peer = 2 }

public enum DataSyncRequestIntent { Follow = 1, TwoWay = 2 }

public enum DataSyncRequestDirection { Incoming = 1, Outgoing = 2 }

public enum DataSyncPeerErrorCode
{
    Unreachable = 1, AccessMissing = 2, AccessRevoked = 3, PeerSharingOff = 4, PeerReset = 5, PeerTooOld = 6,
    ThisTooOld = 7, CursorSuperseded = 8, SnapshotExpired = 9, Busy = 10, InvalidResponse = 11, TooLarge = 12,
    IdentityConflict = 13, PeerRemoteAccessOff = 14, PeerRestorePending = 15,
}

public enum DataSyncStatusLevel
{
    Off = 0, InStep = 1, Syncing = 2, NeedsYou = 3, Paused = 4, Offline = 5, Failed = 6, UpdateNeeded = 7,
}

public enum DataSyncReviewState { Staged = 1, Applying = 2, Applied = 3, Failed = 4 }

public enum DataSyncRestoreChoice { ThisDeviceWins = 1, OthersWin = 2 }
