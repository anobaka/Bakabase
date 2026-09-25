using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Refs;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Components.DataSync;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DomainProperty = Bakabase.Abstractions.Models.Domain.Property;

namespace Bakabase.Tests.DataSync.CustomProperties;

/// <summary>
/// v3.1 H4 #3, L8, §3.4 here: data sync's model of the Property module's option identity is the module's own. The
/// codec's <see cref="OptionFolding"/> gives what <see cref="ReferencePropertyOptionsNormalizer"/> stores, its
/// <see cref="OptionMatcher"/> finds the tag <c>TagsPropertyDescriptor</c> matches, and
/// <see cref="DataSyncLabelKey"/> agrees with <c>GetLabelComparer()</c>. Contents become options through
/// <see cref="CustomPropertyContentMapper"/>, the adapter's own mapping. A change to either side fails here.
/// </summary>
[TestClass]
public class OptionEquivalenceCrossCheckTests
{
    private static readonly string[] Labels =
    [
        "Action", "action", "Drama", "ACTION", "drama", "Ä", "ä", "ß", "SS", "ı", "I", "µ", "Μ", "μ", "ſ", "s", "S",
        "Ǆ", "ǅ", "ǆ", "𐐀", "𐐨", "京都", "",
    ];

    // ---- folding ------------------------------------------------------------------------------

    [TestMethod]
    public void ChoicesFoldLikeTheNormalizer()
    {
        foreach (var ignoreCase in new[] { true, false })
        {
            var choices = Labels.Select((label, i) => new CustomPropertyChoiceV1($"c{i}", label, i % 3 == 0 ? $"#{i}" : null))
                .ToArray();
            foreach (var preserved in PreservedSets(choices.Select(c => c.Uuid!).ToArray()))
            {
                foreach (var type in new[] { PropertyType.MultipleChoice, PropertyType.SingleChoice })
                {
                    var content = new CustomPropertyContentV1
                    {
                        Name = "G", Type = type, IgnoreCase = ignoreCase, Choices = choices,
                        DefaultValue = type == PropertyType.SingleChoice
                            ? [OptionRef.Choice("c1", "action")]
                            : [OptionRef.Choice("c1", "action"), OptionRef.Choice("c3", "ACTION"), OptionRef.Choice("c0", "Action")],
                    };
                    AssertSameAsNormalizer(content, preserved);
                }
            }
        }
    }

    [TestMethod]
    public void TagsFoldLikeTheNormalizerWithAnEmptyGroupAsNone()
    {
        (string? Group, string Name)[] shapes =
        [
            (null, "A"), ("", "a"), ("G", "x"), ("g", "X"), (null, "a"), ("", "A"), ("G", "y"), ("Studio", "Kyoto"),
            ("STUDIO", "kyoto"), (null, "Kyoto"), ("ſ", "s"), ("S", "S"),
        ];
        foreach (var ignoreCase in new[] { true, false })
        {
            var tags = shapes.Select((s, i) => new CustomPropertyTagV1($"t{i}", s.Group, s.Name, null)).ToArray();
            foreach (var preserved in PreservedSets(tags.Select(t => t.Uuid!).ToArray()))
            {
                AssertSameAsNormalizer(
                    new CustomPropertyContentV1 { Name = "T", Type = PropertyType.Tags, IgnoreCase = ignoreCase, Tags = tags },
                    preserved);
            }
        }
    }

    [TestMethod]
    public void MultilevelFoldsLikeTheNormalizerAtEveryLevel()
    {
        var tree = new[]
        {
            Node("n1", "Asia", Node("j1", "Japan", Node("k1", "Kyoto")), Node("c1", "China")),
            Node("n2", "ASIA", Node("j2", "japan", Node("k2", "KYOTO"), Node("o2", "Osaka")), Node("kr2", "Korea")),
            Node("e1", "Europe"),
            Node("n3", "asia", Node("j3", "JAPAN"), Node("c3", "china")),
            Node("e2", "EUROPE", Node("f2", "France")),
        };
        var ids = new[] { "n1", "j1", "k1", "c1", "n2", "j2", "k2", "o2", "kr2", "e1", "n3", "j3", "c3", "e2", "f2" };
        foreach (var ignoreCase in new[] { true, false })
        {
            foreach (var preserved in PreservedSets(ids).Append(["n2", "j3"]).Append(["j2", "e2", "k1"]))
            {
                AssertSameAsNormalizer(new CustomPropertyContentV1
                {
                    Name = "R", Type = PropertyType.Multilevel, IgnoreCase = ignoreCase, Nodes = tree,
                    Settings = new CustomPropertySettingsV1 { ValueIsSingleton = false },
                    DefaultValue = [OptionRef.Node("k2", ["ASIA", "japan", "KYOTO"]), OptionRef.Node("c3", ["asia", "china"]),
                        OptionRef.Node("k1", ["Asia", "Japan", "Kyoto"])],
                }, preserved);
            }
        }
    }

    /// <summary>A few preserved-id sets per list: none (AddRange), all (Put of stored options), and slices.</summary>
    private static IEnumerable<string[]> PreservedSets(string[] ids)
    {
        yield return [];
        yield return ids;
        yield return ids.Where((_, i) => i % 2 == 1).ToArray();
        yield return ids.Where((_, i) => i % 3 == 2).ToArray();
        yield return ids.Skip(ids.Length / 2).ToArray();
    }

    private static void AssertSameAsNormalizer(CustomPropertyContentV1 content, string[] preserved)
    {
        var options = ToOptions(content);
        var previous = preserved.Length == 0 ? null : ToOptions(Only(content, preserved.ToHashSet()));
        ReferencePropertyOptionsNormalizer.Normalize(options, previous);

        var folded = OptionFolding.Fold(content, preserved.ToHashSet(StringComparer.Ordinal)).Content;
        Assert.AreEqual(Describe(options), Describe(ToOptions(folded)),
            $"{content.Type}, IgnoreCase {content.IgnoreCase}, preserved [{string.Join(",", preserved)}]");
    }

    // ---- matching -----------------------------------------------------------------------------

    [TestMethod]
    public void OptionMatcherFindsTheTagTheDescriptorMatches()
    {
        (string? Group, string Name)[] stored =
            [("", "A"), (null, "A"), (null, "a"), ("Studio", "Kyoto"), ("studio", "KYOTO"), ("ſ", "x"), ("S", "x")];
        (string? Group, string Name)[] probes =
        [
            (null, "A"), ("", "A"), (null, "a"), ("", "a"), ("STUDIO", "kyoto"), ("Studio", "Kyoto"), ("s", "X"),
            ("ſ", "X"), (null, "missing"), ("", "kyoto"),
        ];
        foreach (var ignoreCase in new[] { true, false })
        {
            var tags = stored.Select((s, i) => new CustomPropertyTagV1($"t{i}", s.Group, s.Name, null)).ToArray();
            var property = new DomainProperty(PropertyPool.Custom, 1, PropertyType.Tags, "T",
                ToOptions(new CustomPropertyContentV1 { Name = "T", Type = PropertyType.Tags, IgnoreCase = ignoreCase, Tags = tags }));
            foreach (var probe in probes)
            {
                var (dbValue, changed) = PropertySystem.Property.ToDbValue(property,
                    new List<TagValue> { new(probe.Group, probe.Name) }, PropertyValueMatchPolicy.MatchOnly);
                Assert.IsFalse(changed);
                var byDescriptor = (dbValue as List<string>)?.Single();
                var byMatcher = OptionMatcher.FindTag(tags, probe.Group, probe.Name, ignoreCase)?.Uuid;
                Assert.AreEqual(byDescriptor, byMatcher, $"IgnoreCase {ignoreCase}, probe ({probe.Group ?? "null"}, {probe.Name})");
            }
        }
    }

    [TestMethod]
    public void TheLabelKeyAgreesWithGetLabelComparer()
    {
        foreach (var ignoreCase in new[] { true, false })
        {
            var comparer = new TagsPropertyOptions { IgnoreCase = ignoreCase }.GetLabelComparer();
            Assert.AreSame(comparer, DataSyncLabelKey.ComparerFor(ignoreCase));
            foreach (var a in Labels)
            {
                foreach (var b in Labels)
                {
                    Assert.AreEqual(comparer.Equals(a, b),
                        DataSyncLabelKey.Fold(a, ignoreCase) == DataSyncLabelKey.Fold(b, ignoreCase), $"{a} / {b}");
                }
            }
        }
    }

    // ---- conversion to the Property module's options ---------------------------------------------

    private static CustomPropertyNodeV1 Node(string uuid, string label, params CustomPropertyNodeV1[] children) =>
        new(uuid, label, null) { Children = children };

    private static CustomPropertyContentV1 Only(CustomPropertyContentV1 content, HashSet<string> ids) => content with
    {
        Choices = content.Choices.Where(c => ids.Contains(c.Uuid!)).ToArray(),
        Tags = content.Tags.Where(t => ids.Contains(t.Uuid!)).ToArray(),
        // The normalizer collects every node id of the previous tree; a flat list of the preserved nodes is enough.
        Nodes = Flatten(content.Nodes).Where(n => ids.Contains(n.Uuid!)).Select(n => n with { Children = [] }).ToArray(),
        DefaultValue = [],
    };

    private static IEnumerable<CustomPropertyNodeV1> Flatten(IEnumerable<CustomPropertyNodeV1> nodes) =>
        nodes.SelectMany(n => Flatten(n.Children).Prepend(n));

    /// <summary>The adapter's own mapping, so the check also covers what the adapter hands the service.</summary>
    private static object ToOptions(CustomPropertyContentV1 content) =>
        CustomPropertyContentMapper.ToOptions(content) ??
        throw new ArgumentOutOfRangeException(nameof(content), content.Type, "A reference type has options.");

    private static string Describe(object options) => options switch
    {
        SingleChoicePropertyOptions s => $"{Describe(s.Choices)} default={s.DefaultValue}",
        MultipleChoicePropertyOptions m => $"{Describe(m.Choices)} default={string.Join(",", m.DefaultValue ?? [])}",
        TagsPropertyOptions t => string.Join(";", (t.Tags ?? []).Select(x => $"{x.Value}:{x.Group ?? "<null>"}/{x.Name}:{x.Color}")),
        MultilevelPropertyOptions ml => $"{Describe(ml.Data)} default={string.Join(",", ml.DefaultValue ?? [])}",
        _ => throw new ArgumentOutOfRangeException(nameof(options)),
    };

    private static string Describe(List<ChoiceOptions>? choices) =>
        string.Join(";", (choices ?? []).Select(c => $"{c.Value}:{c.Label}:{c.Color}"));

    private static string Describe(List<MultilevelDataOptions>? nodes) =>
        nodes is null || nodes.Count == 0
            ? ""
            : "[" + string.Join(";", nodes.Select(n => $"{n.Value}:{n.Label}{Describe(n.Children)}")) + "]";
}
