namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

/// <summary>
/// Label classes (§3.4): the sibling children whose class keys are equal, compared and merged as one. The class key
/// is <c>Fold(label)</c> for a choice or a multilevel node, and <c>(Fold(group ?? ""), Fold(name))</c> for a tag, with
/// <see cref="DataSyncLabelKey.Fold"/> under the property's IgnoreCase. A class's representative is its first member
/// in that side's order — the member a fresh <c>AddRange</c> keeps (F72) — and supplies the class's colour. A
/// multilevel class's children are all its members' children, concatenated in member order and classed again.
/// </summary>
/// <remarks>Classes are listed in the order of their first member; the comparison form sorts them by key.</remarks>
public static class ChildClasses
{
    public static string KeyOf(CustomPropertyChoiceV1 choice, bool ignoreCase) =>
        DataSyncLabelKey.Fold(choice.Label, ignoreCase);

    public static DataSyncTagClassKey KeyOf(CustomPropertyTagV1 tag, bool ignoreCase) =>
        new(DataSyncLabelKey.Fold(tag.Group ?? "", ignoreCase), DataSyncLabelKey.Fold(tag.Name, ignoreCase));

    public static string KeyOf(CustomPropertyNodeV1 node, bool ignoreCase) => DataSyncLabelKey.Fold(node.Label, ignoreCase);

    public static IReadOnlyList<ChoiceClass> OfChoices(IReadOnlyList<CustomPropertyChoiceV1> choices, bool ignoreCase) =>
        Group(choices, c => KeyOf(c, ignoreCase), StringComparer.Ordinal)
            .Select(g => new ChoiceClass(g.Key, g.Members)).ToArray();

    public static IReadOnlyList<TagClass> OfTags(IReadOnlyList<CustomPropertyTagV1> tags, bool ignoreCase) =>
        Group(tags, t => KeyOf(t, ignoreCase), EqualityComparer<DataSyncTagClassKey>.Default)
            .Select(g => new TagClass(g.Key, g.Members)).ToArray();

    public static IReadOnlyList<NodeClass> OfNodes(IReadOnlyList<CustomPropertyNodeV1> siblings, bool ignoreCase) =>
        Group(siblings, n => KeyOf(n, ignoreCase), StringComparer.Ordinal)
            .Select(g => new NodeClass(g.Key, g.Members,
                OfNodes(g.Members.SelectMany(m => m.Children).ToArray(), ignoreCase)))
            .ToArray();

    private static IEnumerable<(TKey Key, IReadOnlyList<T> Members)> Group<T, TKey>(IReadOnlyList<T> items,
        Func<T, TKey> keyOf, IEqualityComparer<TKey> comparer) where TKey : notnull
    {
        var order = new List<TKey>();
        var members = new Dictionary<TKey, List<T>>(comparer);
        foreach (var item in items)
        {
            var key = keyOf(item);
            if (!members.TryGetValue(key, out var list))
            {
                members[key] = list = [];
                order.Add(key);
            }

            list.Add(item);
        }

        return order.Select(k => (k, (IReadOnlyList<T>)members[k]));
    }
}

/// <summary>A tag's class key: the folded group (<c>""</c> for none, so null and <c>""</c> are one) and name.</summary>
public readonly record struct DataSyncTagClassKey(string Group, string Name) : IComparable<DataSyncTagClassKey>
{
    /// <summary>Ordinal by group, then name: the comparison form's order.</summary>
    public int CompareTo(DataSyncTagClassKey other)
    {
        var byGroup = string.CompareOrdinal(Group, other.Group);
        return byGroup != 0 ? byGroup : string.CompareOrdinal(Name, other.Name);
    }
}

public sealed record ChoiceClass(string Key, IReadOnlyList<CustomPropertyChoiceV1> Members)
{
    public CustomPropertyChoiceV1 Representative => Members[0];
    public string? Color => Representative.Color;
}

public sealed record TagClass(DataSyncTagClassKey Key, IReadOnlyList<CustomPropertyTagV1> Members)
{
    public CustomPropertyTagV1 Representative => Members[0];
    public string? Color => Representative.Color;
}

/// <param name="Children">The classes of every member's children, concatenated in member order.</param>
public sealed record NodeClass(string Key, IReadOnlyList<CustomPropertyNodeV1> Members, IReadOnlyList<NodeClass> Children)
{
    public CustomPropertyNodeV1 Representative => Members[0];
    public string? Color => Representative.Color;
}
