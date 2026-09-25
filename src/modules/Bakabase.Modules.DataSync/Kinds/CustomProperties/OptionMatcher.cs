namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

/// <summary>
/// The Property module's own option identity (v3.1 §3.3.1), which the service applies when it stores options: the
/// comparer is <c>IgnoreCase ? OrdinalIgnoreCase : Ordinal</c> (F5); a choice and a multilevel node are identified by
/// their label among their siblings; a tag by <c>(Group, Name)</c> compared part by part; duplicates are allowed, and
/// a lookup takes the first in list order.
/// </summary>
/// <remarks>
/// <para>
/// A tag group <c>""</c> is the same as none: the module's <c>TagValue</c>, which every stored tag and every tag value
/// is, turns an empty group into null when it is constructed, so the normalizer and the descriptor never see
/// <c>""</c>. (v3.1's F26 read the comparer alone and took the two for different groups.)
/// </para>
/// <para>
/// This is the identity <see cref="OptionFolding"/> mirrors, so data sync predicts what the service stores. Comparing
/// and merging across devices uses label classes (<see cref="ChildClasses"/>, §3.4), which differ from it in one way:
/// exact duplicates are one class even with IgnoreCase off, where the service keeps both.
/// </para>
/// </remarks>
public static class OptionMatcher
{
    public static StringComparer ComparerFor(bool ignoreCase) => DataSyncLabelKey.ComparerFor(ignoreCase);

    /// <summary>A tag group as the module stores it: <c>""</c> is none.</summary>
    public static string? GroupOf(string? group) => string.IsNullOrEmpty(group) ? null : group;

    public static bool SameChoice(CustomPropertyChoiceV1 a, CustomPropertyChoiceV1 b, bool ignoreCase) =>
        ComparerFor(ignoreCase).Equals(a.Label, b.Label);

    public static bool SameTag(CustomPropertyTagV1 a, CustomPropertyTagV1 b, bool ignoreCase) =>
        TagKeyComparer(ignoreCase).Equals((a.Group, a.Name), (b.Group, b.Name));

    public static bool SameNode(CustomPropertyNodeV1 a, CustomPropertyNodeV1 b, bool ignoreCase) =>
        ComparerFor(ignoreCase).Equals(a.Label, b.Label);

    /// <summary>The first choice whose label equals <paramref name="label"/> under the comparer, or null.</summary>
    public static CustomPropertyChoiceV1? FindChoice(IEnumerable<CustomPropertyChoiceV1> choices, string label,
        bool ignoreCase)
    {
        var comparer = ComparerFor(ignoreCase);
        return choices.FirstOrDefault(c => comparer.Equals(c.Label, label));
    }

    /// <summary>The first tag with an equal group (<c>""</c> = none) and name under the comparer, or null.</summary>
    public static CustomPropertyTagV1? FindTag(IEnumerable<CustomPropertyTagV1> tags, string? group, string name,
        bool ignoreCase)
    {
        var comparer = TagKeyComparer(ignoreCase);
        return tags.FirstOrDefault(t => comparer.Equals((t.Group, t.Name), (group, name)));
    }

    /// <summary>The first of <paramref name="siblings"/> whose label equals <paramref name="label"/>, or null.</summary>
    public static CustomPropertyNodeV1? FindNode(IEnumerable<CustomPropertyNodeV1> siblings, string label,
        bool ignoreCase)
    {
        var comparer = ComparerFor(ignoreCase);
        return siblings.FirstOrDefault(n => comparer.Equals(n.Label, label));
    }

    /// <summary>Tag identity as an equality comparer (the normalizer's <c>TagComparer</c> over <c>TagValue</c>s).</summary>
    public static IEqualityComparer<(string? Group, string Name)> TagKeyComparer(bool ignoreCase) =>
        new TagComparer(ComparerFor(ignoreCase));

    private sealed class TagComparer(StringComparer comparer) : IEqualityComparer<(string? Group, string Name)>
    {
        public bool Equals((string? Group, string Name) x, (string? Group, string Name) y) =>
            comparer.Equals(GroupOf(x.Group), GroupOf(y.Group)) && comparer.Equals(x.Name, y.Name);

        public int GetHashCode((string? Group, string Name) tag) => HashCode.Combine(
            GroupOf(tag.Group) is { } group ? comparer.GetHashCode(group) : 0, comparer.GetHashCode(tag.Name));
    }
}
