namespace Bakabase.Modules.DataSync.Planning;

// The item vocabulary history entries record (v3.1 §2.4), and the reasons a record is held.

public enum DataSyncPlanItemType { Create = 1, Update = 2, Unchanged = 3, Link = 4, NeedsDecision = 5, Held = 6 }

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

public enum DataSyncWarningCode
{
    UnknownFieldsIgnored = 1, OptionLabelConflict = 2, DefaultValueRefDropped = 3, SettingsIgnoredForType = 5,
    OptionUuidRemapped = 7, OptionDropped = 9, ChildRestored = 10, ChildrenLocalTurnedOff = 14,
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
