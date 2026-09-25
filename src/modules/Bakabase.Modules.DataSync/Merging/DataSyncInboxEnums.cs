// Merging/: the inbox ("Needs you") and history. The enums live in the module's root namespace so every
// DataSync namespace sees them (§2.1).
namespace Bakabase.Modules.DataSync;

public enum DataSyncInboxItemType
{
    FieldConflict = 1, ChildRenameConflict = 2, TypeChange = 3, DeletedThere = 4, ChildDeletedInUse = 5,
    DeletedHereEditedThere = 6, LinkSuggestion = 7, IdentityConflict = 8, MassChildDeletion = 9,
    SuspectedLostUpdate = 10, LargeChange = 11,
}

/// <summary>
/// Merger-derived items are re-derived on every evaluation; state-derived items close only when their state
/// disappears (§9.3).
/// </summary>
public enum DataSyncInboxItemOrigin { Merger = 1, State = 2 }

public enum DataSyncInboxAction
{
    KeepLocal = 1, UseRemote = 2, UseCustom = 3, Detach = 4, DeleteHere = 5, KeepHereOnly = 6,
    RestoreEverywhere = 7, RestoreHere = 8, KeepDeleted = 9, Link = 10, KeepBoth = 11, Convert = 12,
    ApplyAll = 13, ReviewEach = 14, KeepWithEntity = 15, Publish = 16, Reapply = 17, Skip = 18,
    KeepRecordLinked = 19,
}

public enum DataSyncInboxClosure
{
    ResolvedHere = 1, ResolvedElsewhere = 2, Superseded = 3, LinkRemoved = 4, LinkStopped = 5,
}

public enum DataSyncHistoryKind
{
    FirstLink = 1, CopyOnce = 2, AutoSync = 3, Resolution = 4, Undo = 5, Restore = 6, EntitySetting = 7,
}
