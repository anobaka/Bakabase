using Bakabase.Modules.DataSync.Identity;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// The version vector each kind of revision gives a row (§2.8, §5.6, §8.4). The only place vectors are made:
/// never write <c>VvJson</c> by hand.
/// </summary>
/// <remarks>
/// Monotonicity (invariant I7) holds by construction: every rule returns a vector ≥ <c>local</c>; <c>Revive</c>
/// and <c>Retire</c> return one ≥ the tombstone; <c>Resolution</c> one ≥ every resolved record. Inputs that would
/// break it (a Create or FastForward whose remote does not include local) are refused.
/// </remarks>
public static class DataSyncRevisionRules
{
    /// <summary>
    /// The version vector a row gets for one revision (§5.6, §8.4). <paramref name="nextCounter"/> issues this
    /// actor's next counter; it is called only by the rules that add one. <paramref name="remote"/>: for
    /// Resolution, the Max of every resolved item's record vector; for RestoreWins, <see cref="RestoreWinsRemote"/>.
    /// <paramref name="tombstone"/>: Revive and Retire only.
    /// </summary>
    /// <exception cref="ArgumentException">A required vector is missing, or the result would not include local.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The issued counter does not exceed what the vector already holds for <paramref name="self"/> (a regression).
    /// </exception>
    public static DataSyncVersionVector Next(DataSyncRevisionKind kind, DataSyncVersionVector local,
        DataSyncVersionVector? remote, bool resultEqualsRemote, bool resultEqualsLocal,
        DataSyncActorId self, Func<long> nextCounter, DataSyncVersionVector? tombstone = null)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(nextCounter);
        if (self.Value is null) throw new ArgumentException("An uninitialized actor id cannot issue revisions.", nameof(self));

        DataSyncVersionVector Bump(DataSyncVersionVector v) => v.With(self, nextCounter());
        DataSyncVersionVector Remote() => remote ?? throw new ArgumentException($"{kind} needs the remote vector.", nameof(remote));
        DataSyncVersionVector Tombstone() =>
            tombstone ?? throw new ArgumentException($"{kind} needs the tombstone's vector.", nameof(tombstone));
        static DataSyncVersionVector Max(DataSyncVersionVector a, DataSyncVersionVector b) => DataSyncVersionVector.Max(a, b);

        var result = kind switch
        {
            DataSyncRevisionKind.LocalEdit or DataSyncRevisionKind.LocalDelete or DataSyncRevisionKind.Undo =>
                Bump(local),
            DataSyncRevisionKind.Create or DataSyncRevisionKind.FastForward =>
                resultEqualsRemote ? Remote() : Bump(Remote()),
            DataSyncRevisionKind.Revive =>
                resultEqualsRemote && IsAtLeast(Remote(), Tombstone()) ? Remote() : Bump(Max(Tombstone(), Remote())),
            DataSyncRevisionKind.MergedNoConflict =>
                resultEqualsRemote ? Max(local, Remote()) : Bump(Max(local, Remote())),
            // Yielding a field to a peer makes a state that has seen both sides, never the peer's revision: two
            // devices that yield to each other must not end with equal vectors and swapped contents (§2.8).
            DataSyncRevisionKind.FollowMerged => Bump(Max(local, Remote())),
            // Never absorbs the peer's counters: the conflict is still open.
            DataSyncRevisionKind.MergedWithConflicts => resultEqualsLocal ? local : Bump(local),
            // Nothing to absorb (no resolved record, no base) reads as the empty vector.
            DataSyncRevisionKind.Resolution or DataSyncRevisionKind.KeepDeleted or DataSyncRevisionKind.RestoreWins =>
                Bump(Max(local, remote ?? DataSyncVersionVector.Empty)),
            // The tombstone already dominates.
            DataSyncRevisionKind.AcceptRemoteDelete => Max(local, Remote()),
            DataSyncRevisionKind.Retire => Max(local, Tombstone()),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown revision kind."),
        };

        return IsAtLeast(result, local)
            ? result
            : throw new ArgumentException(
                $"{kind}: the result {result} does not include local {local}; the remote vector must dominate it.",
                nameof(remote));
    }

    /// <summary>
    /// The <c>remote</c> of a <see cref="DataSyncRevisionKind.RestoreWins"/> revision (§5.6, §9.5; v5.1 gate fix
    /// B1): the Max of every base, pending and item vector of the entity, and {a: recorded(a)} for every retired
    /// own actor a, so the winning revision dominates every counter this device issued before the restore — the
    /// ones it lost included. A recorded counter below 1 (an actor that never issued one) adds nothing.
    /// </summary>
    public static DataSyncVersionVector RestoreWinsRemote(IEnumerable<DataSyncVersionVector> entityVectors,
        IReadOnlyDictionary<string, long> retiredActorCounters)
    {
        ArgumentNullException.ThrowIfNull(entityVectors);
        ArgumentNullException.ThrowIfNull(retiredActorCounters);
        var result = entityVectors.Aggregate(DataSyncVersionVector.Empty, DataSyncVersionVector.Max);
        foreach (var (actorId, recorded) in retiredActorCounters.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (recorded < 1) continue;
            var actor = new DataSyncActorId(actorId);
            if (recorded > result[actor]) result = result.With(actor, recorded);
        }

        return result;
    }

    private static bool IsAtLeast(DataSyncVersionVector a, DataSyncVersionVector b) =>
        a.CompareTo(b) is DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates;
}
