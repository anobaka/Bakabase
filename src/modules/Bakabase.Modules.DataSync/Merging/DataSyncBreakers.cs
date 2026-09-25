using System.Globalization;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// The pause protection of §8.7, as pure checks. The merger applies B2, B3, B5 and B8 to a pull before anything
/// is applied, and the codecs apply B4 per entity (<see cref="IsMassChildDeletion"/>); B1 and B1b are read from
/// the peer's head by the fetch half (§8.10.2); B6 and B7 come from <see cref="DataSyncAnomalies"/>.
/// </summary>
public static class DataSyncBreakers
{
    /// <summary>
    /// B1: the peer's node id or library epoch differs from the ones this link recorded. Null when the link has
    /// recorded none yet (its first contact), or when both match.
    /// </summary>
    public static DataSyncBreakerTrip? PeerReset(string linkPeerNodeId, string? recordedEpoch, string headNodeId,
        string headEpoch)
    {
        ArgumentNullException.ThrowIfNull(linkPeerNodeId);
        ArgumentNullException.ThrowIfNull(headNodeId);
        ArgumentNullException.ThrowIfNull(headEpoch);
        if (!string.Equals(linkPeerNodeId, headNodeId, StringComparison.Ordinal))
            return new DataSyncBreakerTrip(DataSyncPauseReason.PeerReset, "node");
        return recordedEpoch is not null && !string.Equals(recordedEpoch, headEpoch, StringComparison.Ordinal)
            ? new DataSyncBreakerTrip(DataSyncPauseReason.PeerReset, "epoch")
            : null;
    }

    /// <summary>
    /// B1b: the same node and epoch, but a kind of the link has <c>MaxSeq</c> below this link's cursor for it — the
    /// peer's sequence went backwards, so it looks restored from a backup (§5.6). Detail <c>restored;kind=…</c>
    /// names the first such kind in ordinal order.
    /// </summary>
    public static DataSyncBreakerTrip? PeerRestored(IReadOnlyDictionary<string, long> cursors,
        IEnumerable<DataSyncFeedKindHead> headKinds, IReadOnlyCollection<string> linkKinds)
    {
        ArgumentNullException.ThrowIfNull(cursors);
        ArgumentNullException.ThrowIfNull(headKinds);
        ArgumentNullException.ThrowIfNull(linkKinds);
        var behind = headKinds
            .Where(k => linkKinds.Contains(k.Kind) && cursors.TryGetValue(k.Kind, out var cursor) && k.MaxSeq < cursor)
            .Select(k => k.Kind).OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
        return behind is null ? null : new DataSyncBreakerTrip(DataSyncPauseReason.PeerReset, $"restored;kind={behind}");
    }

    /// <summary>
    /// B2: more new entity deletions of one kind than <see cref="DataSyncAutoApplyPolicy.MaxEntityDeletionsPerPull"/>,
    /// or more than <see cref="DataSyncAutoApplyPolicy.MaxEntityDeletionRatio"/> of the kind's based entities when it
    /// has at least <see cref="DataSyncAutoApplyPolicy.MinEntitiesForRatio"/>. "New" is the caller's count: not an
    /// open <c>DeletedThere</c> item or an <c>AwaitingDecision</c> pending record already.
    /// </summary>
    public static DataSyncBreakerTrip? MassDeletion(string kind, int newDeletions, int basedEntities,
        DataSyncAutoApplyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var tripped = newDeletions > policy.MaxEntityDeletionsPerPull ||
                      (basedEntities >= policy.MinEntitiesForRatio &&
                       newDeletions > policy.MaxEntityDeletionRatio * basedEntities);
        return tripped
            ? new DataSyncBreakerTrip(DataSyncPauseReason.MassDeletion,
                $"deletions={newDeletions.ToString(CultureInfo.InvariantCulture)};kind={kind}")
            : null;
    }

    /// <summary>
    /// B3: the peer offers no live entity of a kind (the manifest's total <c>LiveCount</c> is 0) while this link has
    /// at least <see cref="DataSyncAutoApplyPolicy.MinBasesForKindEmptied"/> agreed bases for it.
    /// </summary>
    public static DataSyncBreakerTrip? KindEmptied(string kind, int peerLiveCount, int agreedBases,
        DataSyncAutoApplyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return peerLiveCount == 0 && agreedBases >= policy.MinBasesForKindEmptied
            ? new DataSyncBreakerTrip(DataSyncPauseReason.KindEmptied,
                $"kind={kind};bases={agreedBases.ToString(CultureInfo.InvariantCulture)}")
            : null;
    }

    /// <summary>
    /// B4 (§8.5.4 step 7), for codecs: more classes to remove or hold than
    /// <see cref="DataSyncAutoApplyPolicy.MaxChildDeletionsPerEntity"/>, or more than
    /// <see cref="DataSyncAutoApplyPolicy.MaxChildDeletionRatio"/> of an entity with at least
    /// <see cref="DataSyncAutoApplyPolicy.MinChildrenForRatio"/> classes.
    /// </summary>
    public static bool IsMassChildDeletion(int deletionCandidates, int childClasses, DataSyncAutoApplyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return deletionCandidates > policy.MaxChildDeletionsPerEntity ||
               (childClasses >= policy.MinChildrenForRatio &&
                deletionCandidates > policy.MaxChildDeletionRatio * childClasses);
    }

    /// <summary>B5: which side of a pull waits as <c>LargeChange</c> (§8.7). Both may.</summary>
    public static (bool Updates, bool Creates) LargeChange(int updatedEntities, int createdEntities,
        DataSyncAutoApplyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return (updatedEntities > policy.MaxUpdatedEntitiesPerPull, createdEntities > policy.MaxCreatedEntitiesPerPull);
    }

    /// <summary>B8: the link's open items would exceed <see cref="DataSyncLimits.MaxOpenInboxItemsPerLink"/>.</summary>
    public static DataSyncBreakerTrip? TooManyDecisions(int openItemsAfterPull, DataSyncLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return openItemsAfterPull > limits.MaxOpenInboxItemsPerLink
            ? new DataSyncBreakerTrip(DataSyncPauseReason.TooManyDecisions,
                $"openItems={openItemsAfterPull.ToString(CultureInfo.InvariantCulture)}")
            : null;
    }

    /// <summary>B8's resume rule: resuming is allowed once the link is below the limit (§8.7).</summary>
    public static bool MayResumeTooManyDecisions(int openItems, DataSyncLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return openItems < limits.MaxOpenInboxItemsPerLink;
    }
}

/// <summary>
/// A breaker that tripped: the pause it causes, and <see cref="Detail"/>, a short machine string for
/// <c>PausedDetail</c> such as <c>deletions=182;kind=customProperty</c> (§8.7). Never an error.
/// </summary>
public sealed record DataSyncBreakerTrip(DataSyncPauseReason Reason, string Detail);
