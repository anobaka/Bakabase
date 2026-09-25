// Merging/: entity state, bases, revisions and field merges. The enums live in the module's root namespace
// so every DataSync namespace sees them (§2.1).
namespace Bakabase.Modules.DataSync;

public enum DataSyncEntitySyncState { Synced = 1, LocalOnly = 2, Detached = 3 }

/// <summary>What a tombstone records (§8.11).</summary>
public enum DataSyncTombstoneKind { Deleted = 1, UndoneCreate = 2 }

public enum DataSyncBaseState { Normal = 1, Excluded = 2, MissingAtPeer = 3, Held = 4, Unbound = 5 }

public enum DataSyncExclusionReason { Skipped = 1, Undone = 2, NotSyncedHere = 3, DroppedIdentity = 4 }

public enum DataSyncPendingReason
{
    Conflict = 1, TypeChange = 2, MassChildDeletion = 3, LargeChange = 4, Retry = 5, Held = 6,
    AwaitingDecision = 7, OverBudget = 8, PublishHeld = 9, IdentityConflict = 10,
}

/// <summary>How one revision of an entity came about; decides its version vector (§2.8).</summary>
public enum DataSyncRevisionKind
{
    LocalEdit = 1, Create = 2, FastForward = 3, MergedNoConflict = 4, MergedWithConflicts = 5, Resolution = 6,
    LocalDelete = 7, AcceptRemoteDelete = 8, KeepDeleted = 9, Undo = 10, RestoreWins = 11,
    FollowMerged = 12, Revive = 13, Retire = 14,
}

public enum DataSyncFieldResolution
{
    Unchanged = 1, TookRemote = 2, KeptLocal = 3, Combined = 4, Conflict = 5, AppearanceTookRemote = 6,
    AppearanceKeptLocal = 7, FollowTookRemote = 8, DeletionHeldInUse = 9, EditWinsRestored = 10, TypeChangeHeld = 11,
}

public enum DataSyncMergeSide { Local = 1, Remote = 2 }

/// <summary>How <c>Merge3</c> runs (§8.5); <see cref="Convert"/> is phase two of a type change (§8.5.6).</summary>
public enum DataSyncMerge3Mode { ThreeWay = 1, FastForward = 2, NoBase = 3, Convert = 4 }
