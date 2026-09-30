using Bakabase.Modules.DataSync.Identity;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// The pure halves of §8.4 rows A1/A2, which the merger uses: <see cref="FindRegression"/> and
/// <see cref="JudgeEqualVectors"/>. What restore evidence means (§5.6) is the actor guard's (Business).
/// </summary>
public static class DataSyncAnomalies
{
    /// <summary><see cref="DataSyncAnomaly.Code"/> of row A1.</summary>
    public const string Regression = "regression";

    /// <summary><see cref="DataSyncAnomaly.Code"/> of row A2.</summary>
    public const string DuplicateActor = "duplicateActor";

    /// <summary>Row A2's verdict for equal vectors whose forms differ, when it is not a duplicate actor.</summary>
    public const string Drift = "drift";

    /// <summary>
    /// Row A2's verdict for equal vectors whose forms differ when the revision was produced by one of this device's
    /// RETIRED actors: this device issued that counter twice (the residual restore window of §5.6, after the actor
    /// rotated). The two contents are merged as concurrent versions — conflicts become items, the rest merges — so
    /// both sides reach a revision that dominates the reissued one.
    /// </summary>
    public const string Collision = "collision";

    /// <summary>
    /// Row A1 over every valid record of a merge: a record carries <c>Vv[a] &gt; recorded(a)</c> for an own actor
    /// <c>a</c> (the current one: <c>ActorCounter</c>; a retired one: its recorded counter). The anomaly names the
    /// current actor when it regressed, else the retired actor with the highest excess (ties by actor id), and
    /// carries the highest counter of that actor in the merge and the first record (in the order given) that
    /// shows it. Null when nothing regressed.
    /// </summary>
    public static DataSyncAnomaly? FindRegression(IEnumerable<(string Kind, SyncKey Key, DataSyncVersionVector Vv)> records,
        DataSyncActorId currentActor, IReadOnlyDictionary<string, long> ownActorCounters)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(ownActorCounters);
        var highest = new Dictionary<string, (long Counter, string Kind, SyncKey Key)>(StringComparer.Ordinal);
        foreach (var (kind, key, vv) in records)
        {
            foreach (var (actor, recorded) in ownActorCounters)
            {
                if (!vv.Counters.TryGetValue(actor, out var seen) || seen <= recorded) continue;
                if (!highest.TryGetValue(actor, out var best) || seen > best.Counter) highest[actor] = (seen, kind, key);
            }
        }

        if (highest.Count == 0) return null;
        var chosen = highest.ContainsKey(currentActor.Value)
            ? currentActor.Value
            : highest.OrderByDescending(h => h.Value.Counter - ownActorCounters[h.Key])
                .ThenBy(h => h.Key, StringComparer.Ordinal).First().Key;
        var (counter, recordKind, recordKey) = highest[chosen];
        return new DataSyncAnomaly(Regression, chosen, counter, recordKind, recordKey);
    }

    /// <summary>
    /// Row A2 (§8.4), for a record whose vector equals the local one (or the base's): null when the recomputed
    /// comparison forms are equal (the ordinary rows follow); <see cref="DuplicateActor"/> when they differ and the
    /// revision was produced by this device's current actor or by the source's own current actor;
    /// <see cref="Drift"/> otherwise — a third device's revision, or one naming no editor: no pause, no revision, the
    /// base takes the record. Both devices are on one contract (§8.12), so their forms never differ by version.
    /// </summary>
    /// <param name="retiredOwnActors">
    /// This device's retired actors (<c>RetiredActorsJson</c>). A revision one of them produced, met with the same
    /// vector and another form, is a <see cref="Collision"/>: this device reissued the counter before it knew it had
    /// been restored. Without it, once the actor rotated the reissued revision read as drift on both sides and the
    /// two contents kept one vector for good (found by the convergence simulator).
    /// </param>
    public static string? JudgeEqualVectors(bool formsEqual, string? editedByActorId, DataSyncActorId selfActor,
        string? peerActorId, IReadOnlyCollection<string>? retiredOwnActors = null)
    {
        if (formsEqual) return null;
        if (editedByActorId is not null && editedByActorId != selfActor.Value && retiredOwnActors?.Contains(editedByActorId) == true)
            return Collision;
        var producedByOwnerOfActor = editedByActorId is not null &&
                                     (editedByActorId == selfActor.Value || editedByActorId == peerActorId);
        return producedByOwnerOfActor ? DuplicateActor : Drift;
    }

    /// <summary>
    /// Whether a duplicated actor is this device's own (§5.6): then the guidance recommends resetting this device's
    /// identity; otherwise it says to open Data sync on the other device.
    /// </summary>
    public static bool IsOwnActor(string actorId, IReadOnlyDictionary<string, long> ownActorCounters)
    {
        ArgumentNullException.ThrowIfNull(ownActorCounters);
        return ownActorCounters.ContainsKey(actorId);
    }
}
