using Bakabase.Modules.DataSync.Abstractions;

namespace Bakabase.Modules.DataSync.Planning;

/// <summary>
/// The one apply order of kinds, shared by the review planner (v3.1 §7.2) and the merger (§8.4): topological by
/// <c>DependsOn</c>, ties as <see cref="DataSyncKindIds.All"/>, then ordinal.
/// </summary>
internal static class DataSyncKindOrder
{
    /// <summary>
    /// Orders <paramref name="kinds"/> (duplicates removed). A kind without a codec depends on nothing, a dependency
    /// outside <paramref name="kinds"/> is ignored, and a cycle falls back to the first kind left in tie order.
    /// </summary>
    public static IReadOnlyList<string> Of(IEnumerable<string> kinds,
        IReadOnlyDictionary<string, IDataSyncKindCodec> codecs)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(codecs);

        var remaining = kinds.Distinct(StringComparer.Ordinal).OrderBy(Rank).ThenBy(k => k, StringComparer.Ordinal)
            .ToList();
        var done = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>(remaining.Count);
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(k => !codecs.TryGetValue(k, out var codec) ||
                                                     codec.Descriptor.DependsOn.All(d =>
                                                         done.Contains(d) || !remaining.Contains(d)))
                       ?? remaining[0];
            remaining.Remove(next);
            done.Add(next);
            order.Add(next);
        }

        return order;
    }

    private static int Rank(string kind)
    {
        for (var i = 0; i < DataSyncKindIds.All.Count; i++)
        {
            if (DataSyncKindIds.All[i] == kind) return i;
        }

        return int.MaxValue;
    }
}
