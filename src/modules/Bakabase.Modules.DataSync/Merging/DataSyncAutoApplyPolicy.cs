namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// What applies without asking (§8.6) and the breaker thresholds (§8.7). Everything the table of §8.6 lists as
/// safe is applied by the merger and the codecs directly: creates that match nothing here, fast-forward and merged
/// changes not in conflict, child adds, renames and moves, appearance, extension adds and removals, keys, and
/// removing a child the peer deleted when nothing here uses it and it was not changed here (the codec's step 3).
/// The one decision that needs facts from outside the merge — deleting a whole entity — is
/// <see cref="DecideEntityDeletion"/>.
/// </summary>
public sealed record DataSyncAutoApplyPolicy
{
    public int MaxEntityDeletionsPerPull { get; init; } = 10;       // B2, new deletions only (§8.7)
    public double MaxEntityDeletionRatio { get; init; } = 0.2;      // B2, of the kind's based entities…
    public int MinEntitiesForRatio { get; init; } = 20;             // …only when it has at least this many
    public int MinBasesForKindEmptied { get; init; } = 3;           // B3
    public int MaxChildDeletionsPerEntity { get; init; } = 50;      // B4
    public double MaxChildDeletionRatio { get; init; } = 0.2;       // B4, only when the entity has ≥ MinChildrenForRatio children
    public int MinChildrenForRatio { get; init; } = 10;
    public int MaxUpdatedEntitiesPerPull { get; init; } = 50;       // B5
    public int MaxCreatedEntitiesPerPull { get; init; } = 50;       // B5
    public static DataSyncAutoApplyPolicy Default { get; } = new();

    /// <summary>
    /// §8.6: a peer's deletion of a live synced entity applies by itself only when ALL of these hold — the
    /// tombstone dominates the local version (nothing changed here that the deleting device had not seen), sync
    /// created the definition on this device (the Recyclarr rule), it has no values (a known count of 0; an
    /// unknown count asks), and it has no open inbox item, no pending record and no held child. The once flag
    /// <c>DeletionsAsItems</c> (B2's "Review deletions") makes every deletion a question. Everything else asks
    /// (deviation 7).
    /// </summary>
    public DataSyncAutoDeleteVerdict DecideEntityDeletion(DataSyncEntityDeletionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.DeletionsAsItems) return DataSyncAutoDeleteVerdict.Ask(DataSyncAutoDeleteVerdict.DeletionsAsItems);
        if (facts.LocalToTombstone != DataSyncVvRelation.DominatedBy)
            return DataSyncAutoDeleteVerdict.Ask(DataSyncAutoDeleteVerdict.NotDominated);
        if (!facts.CreatedBySync) return DataSyncAutoDeleteVerdict.Ask(DataSyncAutoDeleteVerdict.NotCreatedBySync);
        if (facts.ValueCount is not { } values) return DataSyncAutoDeleteVerdict.Ask(DataSyncAutoDeleteVerdict.ValueCountUnknown);
        if (values > 0) return DataSyncAutoDeleteVerdict.Ask(DataSyncAutoDeleteVerdict.HasValues);
        if (facts.HasOpenItem) return DataSyncAutoDeleteVerdict.Ask(DataSyncAutoDeleteVerdict.OpenItem);
        if (facts.HasPendingRecord) return DataSyncAutoDeleteVerdict.Ask(DataSyncAutoDeleteVerdict.PendingRecord);
        if (facts.HasHeldChildren) return DataSyncAutoDeleteVerdict.Ask(DataSyncAutoDeleteVerdict.HeldChildren);
        return DataSyncAutoDeleteVerdict.Automatic;
    }
}

/// <summary>What <see cref="DataSyncAutoApplyPolicy.DecideEntityDeletion"/> needs to know about one entity.</summary>
/// <param name="LocalToTombstone">The relation of the local version to the peer's tombstone (<c>L.Vv.CompareTo(R.Vv)</c>).</param>
/// <param name="ValueCount">Stored values of the entity; null when unknown (asks). Extension groups have none (0).</param>
/// <param name="HasOpenItem">An open inbox item of the entity (any link).</param>
/// <param name="HasPendingRecord">A pending record of the entity on this link (a record not agreed to).</param>
/// <param name="HasHeldChildren">A child of the entity is held for any link (a state-derived item waits).</param>
/// <param name="DeletionsAsItems">The once flag of B2's "Review deletions" (§8.7), stored with the record.</param>
public sealed record DataSyncEntityDeletionFacts(DataSyncVvRelation LocalToTombstone, bool CreatedBySync, int? ValueCount,
    bool HasOpenItem, bool HasPendingRecord, bool HasHeldChildren, bool DeletionsAsItems);

/// <summary>The §8.6 verdict for one entity deletion; <see cref="Reason"/> says why it asks (never localized).</summary>
public sealed record DataSyncAutoDeleteVerdict(bool IsAutomatic, string? Reason)
{
    public const string DeletionsAsItems = "deletionsAsItems";
    public const string NotDominated = "notDominated";
    public const string NotCreatedBySync = "notCreatedBySync";
    public const string ValueCountUnknown = "valueCountUnknown";
    public const string HasValues = "hasValues";
    public const string OpenItem = "openItem";
    public const string PendingRecord = "pendingRecord";
    public const string HeldChildren = "heldChildren";

    public static DataSyncAutoDeleteVerdict Automatic { get; } = new(true, null);

    public static DataSyncAutoDeleteVerdict Ask(string reason) => new(false, reason);
}
