namespace Bakabase.Modules.DataSync.Planning;

// v3.1 §2.4, with the members v5 §2.1 adds.

public enum DataSyncPlanItemType { Create = 1, Update = 2, Unchanged = 3, Link = 4, NeedsDecision = 5, Held = 6 }

public enum DataSyncPlanItemReason
{
    AmbiguousNameMatch = 1, TypeMismatch = 2, NameClashDifferentType = 3, IdentityConflict = 4,
    DuplicateInPackage = 5,

    /// <summary>Only on an Unchanged item whose local version dominates the incoming one (§8.3).</summary>
    LocalIsNewer = 6,
}

public enum DataSyncHeldReason
{
    NewerSchema = 1, UnknownKind = 2, UnknownEnumValue = 3, Invalid = 4,

    /// <summary>The peer refused to publish it.</summary>
    AtSource = 5,

    /// <summary>This device's stored options do not parse (§3.3).</summary>
    LocalUnreadable = 6,

    /// <summary>Withheld by the lost-update guard (§6.5).</summary>
    PendingDecision = 7,

    /// <summary>Over a per-pull budget (§7.5.4).</summary>
    TooLarge = 8,
}

public enum DataSyncPlanResolution { Create = 1, Update = 2, Link = 3, CreateSeparate = 4, Skip = 5 }

/// <summary><see cref="RemoveChild"/>, <see cref="SetType"/> and <see cref="MoveChild"/> are for history only; a
/// review never removes.</summary>
public enum DataSyncFieldChangeKind
{
    Set = 1, AddChild = 2, RenameChild = 3, RecolorChild = 4, AddMember = 5, RemoveChild = 6, SetType = 7,
    MoveChild = 8,
}

public enum DataSyncWarningCode
{
    UnknownFieldsIgnored = 1, OptionLabelConflict = 2, DefaultValueRefDropped = 3, PreviouslyDeletedHere = 4,
    SettingsIgnoredForType = 5,

    /// <summary>Reviews only: continuous merges move nodes (§8.5.4).</summary>
    NodeMoveIgnored = 6,

    OptionUuidRemapped = 7, FromThisDevice = 8, OptionDropped = 9, ChildRestored = 10,

    /// <summary>Reserved and unused.</summary>
    ChildrenOmittedByPeer = 11,

    NameUsedEverywhere = 12, NormalizationChanged = 13, ChildrenLocalTurnedOff = 14,
}

public enum DataSyncDecisionErrorCode
{
    DecisionMissing = 1, ResolutionNotAllowed = 2, TargetNotAllowed = 3, TargetUsedTwice = 4,
    ChangedSinceReview = 5, UnknownChange = 6, InvalidName = 7, UnknownItem = 8, DuplicateDecision = 9,
}

public enum DataSyncItemOutcome
{
    Applied = 1, SkippedByUser = 2, ChangedSinceReview = 3, Held = 4,

    /// <summary>Hash no longer held at write time, or the identity pre-flight refused a key.</summary>
    ChangedDuringApply = 5,

    NoChange = 6,
}

public enum DataSyncItemAction
{
    None = 0, Created = 1, Updated = 2, Linked = 3, KeysRecorded = 4, Deleted = 5, TypeChanged = 6, Reordered = 7,
}
