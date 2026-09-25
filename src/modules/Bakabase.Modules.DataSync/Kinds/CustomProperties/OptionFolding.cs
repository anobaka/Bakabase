using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

/// <summary>
/// Mirrors the Property module's <c>ReferencePropertyOptionsNormalizer.Normalize(options, previous)</c> over content
/// (v3.1 §3.3.2, F25, F72), so data sync folds as the service does: a new property's <c>AddRange</c> passes no
/// previous options (<c>preservedIds = ∅</c>), an edit's <c>Put</c> the stored ones (every stored id is preserved).
/// A merge result is folded here the second way and stored with <c>PutVerbatim</c>.
/// </summary>
/// <remarks>
/// <para>
/// Nothing happens unless the (final) IgnoreCase is true. Identity is <see cref="OptionMatcher"/>'s: an empty tag
/// group is none. Per list — and per sibling level for multilevel — the preserved options seed a
/// first-by-label map; then every option that is not preserved and whose label is already present is dropped, its id
/// aliased to the first; a dropped node's children are appended to the survivor's, and each level recurses after
/// its own merges. Default values are mapped through the aliases and deduplicated.
/// </para>
/// <para>
/// One deliberate difference: an option without a uuid takes no part. It is never folded, so a merge can never drop a
/// local option (v3.1 B3, §3.3), and nothing is folded into it: it is never published, so an option folded into it
/// would leave its class out of what this device publishes, and no id could be mapped to it. The service differs: it
/// reads a stored choice without a <c>Value</c> with a fresh random id on each side of a <c>Put</c>
/// (<c>ChoiceOptions.Value</c> defaults to a new guid), takes it for an option the edit introduced and folds it into an
/// earlier option of its class, and it fails on a duplicate tag or node without an id (it aliases by id). Data sync
/// therefore never lets the service fold content that holds one: a merge result is stored with <c>PutVerbatim</c>.
/// </para>
/// <para><c>OptionEquivalenceCrossCheckTests</c> runs this and the normalizer over the same inputs.</para>
/// </remarks>
public static class OptionFolding
{
    /// <param name="content">The options as they would be handed to the service.</param>
    /// <param name="preservedIds">Ids the service already stores for this property; never folded.</param>
    /// <param name="ignoreCase">The IgnoreCase the service will store; null = the content's own.</param>
    /// <param name="mayAbsorb">
    /// Whether an option of <paramref name="content"/> (a choice, tag or node record, by instance) may take another in;
    /// null: every option with a uuid. A merge passes the options this device publishes (§3.5): one folded into an
    /// option it does not publish would leave its class out of what it publishes.
    /// </param>
    public static OptionFoldResult Fold(CustomPropertyContentV1 content, IReadOnlySet<string>? preservedIds = null,
        bool? ignoreCase = null, Func<object, bool>? mayAbsorb = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        if ((ignoreCase ?? content.IgnoreCase) != true || !CustomPropertyTypes.IsReference(content.Type))
            return new OptionFoldResult(content, new Dictionary<string, string>(), []);

        var preserved = preservedIds ?? new HashSet<string>();
        var folder = new Folder(preserved, OptionMatcher.ComparerFor(true), mayAbsorb ?? (static _ => true));
        var folded = content with
        {
            Choices = content.Type is PropertyType.SingleChoice or PropertyType.MultipleChoice
                ? folder.FoldChoices(content.Choices)
                : content.Choices,
            Tags = content.Type is PropertyType.Tags ? folder.FoldTags(content.Tags) : content.Tags,
            Nodes = content.Type is PropertyType.Multilevel ? folder.FoldNodes(content.Nodes) : content.Nodes,
        };
        if (folder.Aliases.Count > 0 && content.DefaultValue.Count > 0)
            folded = folded with { DefaultValue = CustomPropertyRefs.Remap(folded, content.DefaultValue, folder.Aliases) };
        return new OptionFoldResult(folded, folder.Aliases, folder.Folds);
    }

    private sealed class Folder(IReadOnlySet<string> preserved, StringComparer comparer, Func<object, bool> mayAbsorb)
    {
        public Dictionary<string, string> Aliases { get; } = new(StringComparer.Ordinal);
        public List<OptionFold> Folds { get; } = [];

        private bool IsPreserved(string? uuid) => uuid is not null && preserved.Contains(uuid);

        public IReadOnlyList<CustomPropertyChoiceV1> FoldChoices(IReadOnlyList<CustomPropertyChoiceV1> choices) =>
            FoldList(choices, c => c.Label, c => c.Uuid, c => c.Label, c => c, comparer, null);

        public IReadOnlyList<CustomPropertyTagV1> FoldTags(IReadOnlyList<CustomPropertyTagV1> tags) =>
            FoldList(tags, t => (t.Group, t.Name), t => t.Uuid, t => t.Name, t => t, OptionMatcher.TagKeyComparer(true),
                null);

        public IReadOnlyList<CustomPropertyNodeV1> FoldNodes(IReadOnlyList<CustomPropertyNodeV1> nodes) =>
            FoldWorkNodes(nodes.Select(WorkNode.From).ToList()).Select(n => n.ToNode()).ToArray();

        private List<WorkNode> FoldWorkNodes(List<WorkNode> nodes)
        {
            var result = FoldList(nodes, n => n.Label, n => n.Uuid, n => n.Label, n => n.Source, comparer,
                (survivor, duplicate) => survivor.Children.AddRange(duplicate.Children));
            foreach (var node in result) node.Children = FoldWorkNodes(node.Children);
            return result;
        }

        private List<T> FoldList<T, TKey>(IReadOnlyList<T> values, Func<T, TKey> key, Func<T, string?> id,
            Func<T, string> label, Func<T, object> source, IEqualityComparer<TKey> keyComparer, Action<T, T>? merge)
            where TKey : notnull
        {
            var firstByLabel = new Dictionary<TKey, T>(keyComparer);
            foreach (var value in values.Where(v => IsPreserved(id(v)) && mayAbsorb(source(v))))
                firstByLabel.TryAdd(key(value), value);

            var result = new List<T>();
            foreach (var value in values)
            {
                // Without an id: kept, and never a survivor (see the remarks).
                if (id(value) is not { } uuid)
                {
                    result.Add(value);
                    continue;
                }

                if (!IsPreserved(uuid) && firstByLabel.TryGetValue(key(value), out var first))
                {
                    var into = id(first)!;
                    if (uuid != into)
                    {
                        Aliases[uuid] = into;
                        Folds.Add(new OptionFold(uuid, into, label(first)));
                    }

                    merge?.Invoke(first, value);
                    continue;
                }

                if (mayAbsorb(source(value))) firstByLabel.TryAdd(key(value), value);
                result.Add(value);
            }

            return result;
        }
    }

    private sealed class WorkNode
    {
        public required CustomPropertyNodeV1 Source { get; init; }
        public required List<WorkNode> Children { get; set; }
        public string? Uuid => Source.Uuid;
        public string Label => Source.Label;

        public static WorkNode From(CustomPropertyNodeV1 node) =>
            new() { Source = node, Children = node.Children.Select(From).ToList() };

        public CustomPropertyNodeV1 ToNode() => Source with { Children = Children.Select(c => c.ToNode()).ToArray() };
    }
}

/// <param name="Aliases">Folded uuid → the uuid it was folded into.</param>
/// <param name="Folds">One entry per folded option, in the order the service meets them.</param>
public sealed record OptionFoldResult(CustomPropertyContentV1 Content, IReadOnlyDictionary<string, string> Aliases,
    IReadOnlyList<OptionFold> Folds);

/// <summary>One option the service will fold (<c>OptionLabelConflict</c>): <paramref name="Uuid"/> into <paramref name="Into"/>.</summary>
public sealed record OptionFold(string Uuid, string Into, string IntoLabel);
