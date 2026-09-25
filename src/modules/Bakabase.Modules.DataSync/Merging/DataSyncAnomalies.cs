using Bakabase.Modules.DataSync.Identity;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// The pure halves of §5.6 and §8.4 rows A1/A2: which evidence means what. The merger uses
/// <see cref="FindRegression"/> and <see cref="JudgeEqualVectors"/>; the actor guard [C] uses
/// <see cref="IsNewEvidence"/> and <see cref="RestorePause"/>.
/// </summary>
/// <remarks>
/// Gate fix B1(a): a retired actor keeps a recorded counter. Evidence at or below it does nothing; evidence above
/// it only raises it (and is appended to the evidence log). Evidence about a retired actor never rotates or
/// pauses: the counters this device lost in one restore are history after the first detection.
/// </remarks>
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
    /// comparison forms are equal (the ordinary rows follow); <see cref="DuplicateActor"/> when they differ, the
    /// revision was produced by this device's current actor or by the source's own current actor, and the source's
    /// codec has this build's <c>ComparisonFormVersion</c> for the kind; <see cref="Drift"/> otherwise — a third
    /// device's revision relayed by a hub on another build, or a peer whose form differs by version (N5): no
    /// pause, no revision, the base takes the record.
    /// </summary>
    /// <param name="peerComparisonFormVersion">The head's version for the kind; null when the head did not say.</param>
    /// <param name="retiredOwnActors">
    /// This device's retired actors (<c>RetiredActorsJson</c>). A revision one of them produced, met with the same
    /// vector and another form, is a <see cref="Collision"/>: this device reissued the counter before it knew it had
    /// been restored. Without it, once the actor rotated the reissued revision read as drift on both sides and the
    /// two contents kept one vector for good (found by the convergence simulator). Like a duplicate actor, a
    /// collision needs the head's <c>ComparisonFormVersion</c> for the kind to be this build's: a form-version
    /// mismatch is always drift (§8.4 row A2, §8.12), whoever produced the revision.
    /// </param>
    public static string? JudgeEqualVectors(bool formsEqual, string? editedByActorId, DataSyncActorId selfActor,
        string? peerActorId, int? peerComparisonFormVersion, int codecComparisonFormVersion,
        IReadOnlyCollection<string>? retiredOwnActors = null)
    {
        if (formsEqual) return null;
        if (peerComparisonFormVersion != codecComparisonFormVersion) return Drift;
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

    /// <summary>
    /// Whether a counter a peer has seen for one of this device's actors is new evidence (§5.6): above the recorded
    /// counter (for the current actor, <c>ActorCounter</c>). Evidence at or below it does nothing; that includes a
    /// head's <c>SeenCounter</c>.
    /// </summary>
    public static bool IsNewEvidence(long? seenCounter, long recordedCounter) =>
        seenCounter is { } seen && seen > recordedCounter;

    /// <summary>
    /// The counter a rotation records for the actor it retires (§5.6): the max of <c>ActorCounter</c>, the counter
    /// <c>actor.json</c> names for it, and the counter of the evidence that caused the rotation.
    /// </summary>
    public static long RetiredCounter(long actorCounter, long? watermarkCounter, long? evidenceCounter) =>
        Math.Max(actorCounter, Math.Max(watermarkCounter ?? 0, evidenceCounter ?? 0));

    /// <summary>
    /// The pause a set of restore evidence causes (§5.6 table, B6): this device's own records (the watermark) or a
    /// reader running ahead → every link pauses <c>LocalRestoreDetected</c>; one peer's counters alone → only that
    /// link pauses <c>LocalRestoreSuspected</c>; two or more peers, or one peer plus a watermark or reader signal
    /// → <c>LocalRestoreDetected</c>. Evidence about an already-retired actor is ignored (gate fix B1(a)). Null when
    /// nothing counts.
    /// </summary>
    public static DataSyncPauseReason? RestorePause(IEnumerable<DataSyncRestoreEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var counted = evidence.Where(e => !e.AboutRetiredActor).ToList();
        if (counted.Count == 0) return null;
        var local = counted.Any(e => e.Source is DataSyncRestoreEvidence.Watermark or DataSyncRestoreEvidence.Reader);
        var peers = counted.Where(e => e.Source == DataSyncRestoreEvidence.Peer)
            .Select(e => e.NodeId ?? "").Distinct(StringComparer.Ordinal).Count();
        if (local || peers >= 2) return DataSyncPauseReason.LocalRestoreDetected;
        return peers == 1 ? DataSyncPauseReason.LocalRestoreSuspected : null;
    }
}

/// <summary>
/// One piece of restore evidence (§5.6, <c>RestoreEvidenceJson</c>): <see cref="Source"/> is
/// <see cref="Watermark"/> ("this device's own records"), <see cref="Reader"/> (a reader's cursor ran ahead) or
/// <see cref="Peer"/> (a peer has seen newer counters); <see cref="NodeId"/> names the reader or peer.
/// </summary>
/// <param name="AboutRetiredActor">The evidence concerns an actor already retired: it only raises its recorded counter.</param>
public sealed record DataSyncRestoreEvidence(string Source, string? NodeId, bool AboutRetiredActor = false)
{
    public const string Watermark = "watermark";
    public const string Reader = "reader";
    public const string Peer = "peer";
}
