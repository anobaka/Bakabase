using Bakabase.Modules.DataSync.Identity;

namespace Bakabase.Modules.DataSync.Ordering;

/// <summary>One synced, live, published entity as ordering sees it (§3.7).</summary>
/// <param name="LocalKey">The entity's local key.</param>
/// <param name="OrderKey">Its shared order key; null when it never had one (a local create).</param>
/// <param name="TieKey">The ordinally smallest of all its sync keys (<see cref="DataSyncOrderPlanner.TieKeyOf(EntityKeys)"/>).</param>
public sealed record DataSyncOrderEntry(string LocalKey, string? OrderKey, string TieKey);

/// <summary>
/// Shared order (§3.7): which local moves become new order keys (<see cref="DetectMoves"/>), and where synced
/// entities go when a peer's order is applied (<see cref="Place(IReadOnlyList{string}, IReadOnlyList{DataSyncOrderEntry})"/>).
/// Both rank by <see cref="Compare"/>, so placing and then detecting finds nothing to move. Pure and deterministic.
/// </summary>
public static class DataSyncOrderPlanner
{
    /// <summary>
    /// The share of <c>MaxOrderKeyLength</c> that neighbours of an over-long key must have in common with it to be
    /// re-keyed with it (<see cref="Rebalance"/>).
    /// </summary>
    private const int RunPrefixDivisor = 2;

    /// <summary>The tie key of an entity: the ordinally smallest of all its keys, primary and aliases.</summary>
    public static string TieKeyOf(EntityKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return TieKeyOf(keys.All.Select(k => k.Value));
    }

    /// <inheritdoc cref="TieKeyOf(EntityKeys)"/>
    public static string TieKeyOf(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        string? smallest = null;
        foreach (var key in keys)
        {
            if (smallest is null || string.CompareOrdinal(key, smallest) < 0) smallest = key;
        }

        return smallest ?? throw new ArgumentException("An entity has at least one key.", nameof(keys));
    }

    /// <summary>The shared ranking: <c>(OrderKey, TieKey)</c>, both ordinal. An entry without an order key sorts last.</summary>
    public static int Compare(DataSyncOrderEntry a, DataSyncOrderEntry b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.OrderKey is null || b.OrderKey is null)
        {
            if (a.OrderKey is not null) return -1;
            if (b.OrderKey is not null) return 1;
        }
        else
        {
            var byKey = string.CompareOrdinal(a.OrderKey, b.OrderKey);
            if (byKey != 0) return byKey;
        }

        var byTie = string.CompareOrdinal(a.TieKey, b.TieKey);
        return byTie != 0 ? byTie : string.CompareOrdinal(a.LocalKey, b.LocalKey);
    }

    /// <summary>The entries in shared order (<see cref="Compare"/>).</summary>
    public static IReadOnlyList<DataSyncOrderEntry> Sort(IEnumerable<DataSyncOrderEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var sorted = entries.ToList();
        sorted.Sort(Compare);
        return sorted;
    }

    /// <summary>
    /// Local moves (§3.7, Refresh): given the synced, live, published entities in LOCAL order, the new order key
    /// of every entity whose key must change so that the shared order reproduces the local one. Entities on the
    /// longest increasing run of <c>(OrderKey, TieKey)</c> keep their keys; every other entity, and every entity
    /// without a key, gets one after its left neighbour. A key that would fall inside a run of equal keys (two
    /// devices appended at once) re-keys that whole run instead, so <c>Between(k, k)</c> never happens, and a key
    /// longer than <paramref name="maxOrderKeyLength"/> re-keys its run (<see cref="Rebalance"/>).
    /// </summary>
    /// <returns>Local key → new order key, only for keys that change.</returns>
    public static IReadOnlyDictionary<string, string> DetectMoves(IReadOnlyList<DataSyncOrderEntry> localOrder,
        int maxOrderKeyLength)
    {
        ArgumentNullException.ThrowIfNull(localOrder);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxOrderKeyLength, 2);
        RequireDistinctLocalKeys(localOrder);

        var n = localOrder.Count;
        var keys = localOrder.Select(e => e.OrderKey).ToArray();
        var kept = KeptByLongestRun(localOrder);

        var i = 0;
        while (i < n)
        {
            if (kept[i])
            {
                i++;
                continue;
            }

            var left = i > 0 ? keys[i - 1] : null;
            var nearestKept = i + 1;
            while (nearestKept < n && !kept[nearestKept]) nearestKept++;
            var right = nearestKept < n ? keys[nearestKept] : null;

            if (left is null || right is null || !string.Equals(left, right, StringComparison.Ordinal))
            {
                keys[i] = FractionalIndex.Between(left, right);
                i++;
                continue;
            }

            // The new key would land inside a run of equal keys: re-key the whole tied run in its local order,
            // between the nearest distinct keys on either side.
            var tied = left;
            var start = i - 1;
            while (start > 0 && string.Equals(keys[start - 1], tied, StringComparison.Ordinal)) start--;
            var end = i;
            while (end < n && (!kept[end] || string.Equals(keys[end], tied, StringComparison.Ordinal))) end++;
            var runKeys = FractionalIndex.NBetween(start > 0 ? keys[start - 1] : null, end < n ? keys[end] : null,
                end - start);
            for (var k = start; k < end; k++) keys[k] = runKeys[k - start];
            i = end;
        }

        var assigned = localOrder.Select((e, index) => e with { OrderKey = keys[index] }).ToList();
        var rebalanced = Rebalance(assigned, maxOrderKeyLength);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < n; index++)
        {
            var entry = localOrder[index];
            var key = rebalanced.TryGetValue(entry.LocalKey, out var rekeyed) ? rekeyed : keys[index]!;
            if (!string.Equals(key, entry.OrderKey, StringComparison.Ordinal)) result[entry.LocalKey] = key;
        }

        return result;
    }

    /// <summary>
    /// Long keys (§3.7): for every key longer than <paramref name="maxOrderKeyLength"/> in
    /// <paramref name="order"/> (keys in ascending order, as <see cref="DetectMoves"/> leaves them), re-keys only
    /// its run — the maximal run of neighbours sharing a long prefix with it (half the maximum length) — between
    /// the run's distinct outer neighbours. When the neighbours themselves leave too little room, the run grows
    /// by one on each side until the keys fit. Never re-keys every entity unless nothing smaller fits.
    /// </summary>
    /// <returns>Local key → new order key, only for keys that change.</returns>
    public static IReadOnlyDictionary<string, string> Rebalance(IReadOnlyList<DataSyncOrderEntry> order,
        int maxOrderKeyLength)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxOrderKeyLength, 2);
        var n = order.Count;
        var keys = order.Select(e => e.OrderKey ??
            throw new ArgumentException("Rebalancing needs every entry to have an order key.", nameof(order))).ToArray();
        for (var i = 1; i < n; i++)
        {
            if (string.CompareOrdinal(keys[i - 1], keys[i]) > 0)
                throw new ArgumentException("Order keys must be in ascending order.", nameof(order));
        }

        var prefixLength = Math.Max(1, maxOrderKeyLength / RunPrefixDivisor);
        for (var p = 0; p < n; p++)
        {
            if (keys[p].Length <= maxOrderKeyLength) continue;

            var prefix = keys[p][..prefixLength];
            var start = p;
            while (start > 0 && keys[start - 1].StartsWith(prefix, StringComparison.Ordinal)) start--;
            var end = p + 1;
            while (end < n && keys[end].StartsWith(prefix, StringComparison.Ordinal)) end++;

            while (true)
            {
                // Equal neighbours leave no room: take them into the run.
                while (start > 0 && end < n && string.Equals(keys[start - 1], keys[end], StringComparison.Ordinal))
                {
                    start--;
                    end++;
                }

                var runKeys = FractionalIndex.NBetween(start > 0 ? keys[start - 1] : null, end < n ? keys[end] : null,
                    end - start);
                if (runKeys.All(k => k.Length <= maxOrderKeyLength) || (start == 0 && end == n))
                {
                    for (var k = start; k < end; k++) keys[k] = runKeys[k - start];
                    break;
                }

                if (start > 0) start--;
                if (end < n) end++;
            }
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < n; i++)
        {
            if (!string.Equals(keys[i], order[i].OrderKey, StringComparison.Ordinal)) result[order[i].LocalKey] = keys[i];
        }

        return result;
    }

    /// <summary>
    /// Applying peer order (§3.7): the slots of <paramref name="synced"/> entities in <paramref name="localOrder"/>
    /// are filled with them in shared order (<see cref="Compare"/>). Every other entity (local-only, detached, held
    /// at source) and every synced entry without an order key keeps its slot.
    /// </summary>
    /// <returns>The new local order (every local key of <paramref name="localOrder"/>).</returns>
    public static IReadOnlyList<string> Place(IReadOnlyList<string> localOrder, IReadOnlyList<DataSyncOrderEntry> synced)
    {
        ArgumentNullException.ThrowIfNull(synced);
        RequireDistinctLocalKeys(synced);
        return Place(localOrder, Sort(synced.Where(e => e.OrderKey is not null)).Select(e => e.LocalKey).ToList());
    }

    /// <summary>
    /// <see cref="Place(IReadOnlyList{string}, IReadOnlyList{DataSyncOrderEntry})"/> for entities already in shared
    /// order (what the merger hands the adapter, <c>ApplyOrderAsync</c>). Keys absent from
    /// <paramref name="localOrder"/> are ignored.
    /// </summary>
    public static IReadOnlyList<string> Place(IReadOnlyList<string> localOrder,
        IReadOnlyList<string> syncedLocalKeysInSharedOrder)
    {
        ArgumentNullException.ThrowIfNull(localOrder);
        ArgumentNullException.ThrowIfNull(syncedLocalKeysInSharedOrder);
        var present = new HashSet<string>(localOrder, StringComparer.Ordinal);
        if (present.Count != localOrder.Count)
            throw new ArgumentException("A local key appears twice in the local order.", nameof(localOrder));

        var ordered = syncedLocalKeysInSharedOrder.Where(present.Contains).ToList();
        var synced = new HashSet<string>(ordered, StringComparer.Ordinal);
        if (synced.Count != ordered.Count)
            throw new ArgumentException("A synced local key appears twice.", nameof(syncedLocalKeysInSharedOrder));

        var result = new string[localOrder.Count];
        var next = 0;
        for (var i = 0; i < localOrder.Count; i++)
        {
            result[i] = synced.Contains(localOrder[i]) ? ordered[next++] : localOrder[i];
        }

        return result;
    }

    /// <summary>
    /// Marks the entries on the longest strictly increasing run of <see cref="Compare"/> ranks (entries without
    /// a key take no part). Among runs of equal length it keeps the one the classic patience method reconstructs
    /// from the smallest final element, which is deterministic.
    /// </summary>
    private static bool[] KeptByLongestRun(IReadOnlyList<DataSyncOrderEntry> localOrder)
    {
        var n = localOrder.Count;
        var keyed = Enumerable.Range(0, n).Where(i => localOrder[i].OrderKey is not null).ToList();
        var rankOf = new int[n];
        var byRank = keyed.OrderBy(i => localOrder[i], Comparer<DataSyncOrderEntry>.Create(Compare)).ThenBy(i => i)
            .ToList();
        for (var r = 0; r < byRank.Count; r++) rankOf[byRank[r]] = r;

        // tails[len - 1] = index (into keyed) of the smallest final rank of an increasing run of length len.
        var tails = new List<int>();
        var previous = new int[keyed.Count];
        for (var k = 0; k < keyed.Count; k++)
        {
            var rank = rankOf[keyed[k]];
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (rankOf[keyed[tails[mid]]] < rank) lo = mid + 1;
                else hi = mid;
            }

            previous[k] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count) tails.Add(k);
            else tails[lo] = k;
        }

        var kept = new bool[n];
        for (var k = tails.Count > 0 ? tails[^1] : -1; k >= 0; k = previous[k]) kept[keyed[k]] = true;
        return kept;
    }

    private static void RequireDistinctLocalKeys(IReadOnlyList<DataSyncOrderEntry> entries)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!seen.Add(entry.LocalKey))
                throw new ArgumentException($"Local key '{entry.LocalKey}' appears twice.", nameof(entries));
        }
    }
}
