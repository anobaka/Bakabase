using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>
/// One synced, live, published entity of a kind with order, in local order, as Refresh hands it to move detection
/// (§3.7 step 1).
/// </summary>
/// <param name="LocalKey">The entity's local key.</param>
/// <param name="OrderKey">Its shared order key; null for a definition that has none yet (a local create).</param>
/// <param name="TieKey">
/// The ordinally smallest of all its sync keys (§3.7); null for a definition that has no key yet: it is created by
/// this Refresh and always gets a new order key.
/// </param>
public sealed record DataSyncOrderMoveEntry(string LocalKey, string? OrderKey, string? TieKey);

/// <summary>
/// Local moves (§3.7, Refresh): which entities need a new order key so that the shared order reproduces the local
/// one. The algorithm (the longest increasing run of <c>(orderKey, tieKey)</c>, fractional keys, tie runs,
/// rebalancing) is the pure engine's <c>DataSyncOrderPlanner.DetectMoves</c> (package A); this is the seam Refresh
/// calls it through, so persistence never carries a second copy of it. A kind with order cannot be refreshed while
/// nothing is registered.
/// </summary>
public interface IDataSyncOrderMoveDetector
{
    /// <returns>Local key → new order key, only for the entities whose key changes.</returns>
    IReadOnlyDictionary<string, string> DetectMoves(IReadOnlyList<DataSyncOrderMoveEntry> localOrder,
        int maxOrderKeyLength);
}
