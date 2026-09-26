namespace Bakabase.Modules.DataSync.Abstractions;

/// <summary>
/// <see cref="IDataSyncKindCodec.MapChildrenByClass"/> over the children's display values: a child's class key is its
/// folded group and its folded label path (a node's labels from the root, any other child's own label), so a choice,
/// a tag without a group and a root node of the same label are one class — which is what a conversion between them
/// keeps.
/// </summary>
public static class DataSyncChildClassMap
{
    /// <param name="fold">A label's fold key under the property's comparer (§3.4).</param>
    public static IReadOnlyDictionary<string, string> Map(IReadOnlyList<DataSyncChildInfo> from,
        IReadOnlyList<DataSyncChildInfo> to, Func<string, string> fold)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(fold);
        var toIds = to.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var firstOfClass = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var child in to) firstOfClass.TryAdd(KeyOf(child, fold), child.Id);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var child in from)
        {
            if (toIds.Contains(child.Id)) continue;
            if (firstOfClass.TryGetValue(KeyOf(child, fold), out var target)) map.TryAdd(child.Id, target);
        }

        return map;
    }

    private static string KeyOf(DataSyncChildInfo child, Func<string, string> fold)
    {
        var path = child.Display.Path is { Count: > 0 } labels ? labels : [child.Display.Text ?? ""];
        // Control characters separate the group from the path and the path's labels.
        return fold(child.Display.Group ?? "") + "\u001f" + string.Join("\u001e", path.Select(fold));
    }
}
