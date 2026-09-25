using System.Globalization;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Refs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.Cp;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.M3;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>
/// The comparison-form rule for this codec (§3.4, §8.5): the form is invariant under exactly what <c>Merge3</c> does
/// not transfer, and <c>Merge3</c> transfers every difference the form keeps. Checked as the closure property
/// (<c>FastForward</c> ends at the peer's form) and the symmetry property (merging either way without conflicts ends
/// at one form), over seeded random contents with duplicates, IgnoreCase, <c>null</c>/<c>""</c> tag groups, colours set
/// and cleared, node moves and renames onto existing keys.
/// </summary>
[TestClass]
public class Merge3PropertyTests
{
    /// <summary>2,000 seeded runs in CI; <c>DATASYNC_MERGE3_RUNS</c> raises it for a local hunt.</summary>
    private static readonly int Runs =
        int.TryParse(Environment.GetEnvironmentVariable("DATASYNC_MERGE3_RUNS"), out var runs) && runs > 0 ? runs : 2_000;

    [TestMethod]
    public void FastForwardClosure()
    {
        for (var seed = 0; seed < Runs; seed++)
        {
            var gen = new Merge3Generator(seed);
            var type = gen.Type();
            var local = gen.Content(type, "l");
            CustomPropertyContentV1 remoteRaw;
            var childMap = new Dictionary<string, string>(StringComparer.Ordinal);
            switch (seed % 8)
            {
                case 0:
                    remoteRaw = gen.Content(type, "r");
                    break;
                case 1:
                    // The peer holds the same options under other ids: the base row's child map relates them.
                    remoteRaw = gen.Mutate(local, "r", gen.Edits());
                    remoteRaw = Merge3Generator.Reprefix(remoteRaw, "p", childMap);
                    break;
                case 2:
                    remoteRaw = gen.Mutate(local, "r", gen.Edits());
                    foreach (var id in Ids(local)) childMap[id] = id;
                    break;
                default:
                    remoteRaw = gen.Mutate(local, "r", gen.Edits());
                    break;
            }

            var remote = Peer(remoteRaw);
            var usage = Ids(local).Distinct().ToDictionary(id => id, _ => gen.Usage(), StringComparer.Ordinal);
            var input = new DataSyncMerge3Input(null, local, DataSyncOverlay.None, remote, DataSyncMerge3Mode.FastForward,
                childMap, false, false, DataSyncLinkMode.TwoWay, true, gen.Winner(), usage,
                DataSyncChildDeletionMode.Apply);
            var result = Untyped.Merge3(input);
            var trace = $"seed {seed}\nlocal  {Canon(local)}\nremote {Canon(remote)}\nmerged {Canon(Merged(result))}";
            Assert.IsFalse(result.TypeChanged, trace);
            Assert.AreEqual(Form(remote), PublishedForm(result), trace);
            AssertNothingLost(local, result, trace);
            AssertCandidatesCoverRemovals(input, result, trace);
        }
    }

    [TestMethod]
    public void SymmetricMerge()
    {
        var compared = 0;
        for (var seed = 0; seed < Runs; seed++)
        {
            var gen = new Merge3Generator(1_000_000 + seed);
            var type = gen.Type();
            var @base = Peer(gen.Content(type, "b"));
            var l = gen.Mutate(@base, "l", gen.Edits());
            var r = gen.Mutate(@base, "r", gen.Edits());
            var winner = gen.Winner();
            var other = winner == DataSyncMergeSide.Local ? DataSyncMergeSide.Remote : DataSyncMergeSide.Local;

            var lr = Merge(@base, l, r, winner: winner, deletions: DataSyncChildDeletionMode.Apply);
            var rl = Merge(@base, r, l, winner: other, deletions: DataSyncChildDeletionMode.Apply);
            if (lr.TypeChanged || rl.TypeChanged || HasConflict(lr) || HasConflict(rl)) continue;
            compared++;
            var trace = $"seed {seed}\nbase {Canon(@base)}\nl    {Canon(l)}\nr    {Canon(r)}\n" +
                        $"lr   {Canon(Merged(lr))}\nrl   {Canon(Merged(rl))}";
            Assert.AreEqual(PublishedForm(lr), PublishedForm(rl), trace);
            AssertNothingLost(l, lr, trace);
            AssertNothingLost(r, rl, trace);
        }

        Assert.IsTrue(compared > Runs / 2, $"only {compared} of {Runs} runs merged without conflicts");
    }

    [TestMethod]
    public void MergingTheSameInputTwiceGivesTheSameResult()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var gen = new Merge3Generator(2_000_000 + seed);
            var type = gen.Type();
            var @base = Peer(gen.Content(type, "b"));
            var l = gen.Mutate(@base, "l", gen.Edits());
            var r = gen.Mutate(@base, "r", gen.Edits());
            var first = Merge(@base, l, r);
            var second = Merge(@base, l, r);
            Assert.AreEqual(Canon(Merged(first)), Canon(Merged(second)));
            Assert.AreEqual(JsonSerializer.Serialize(first.Fields), JsonSerializer.Serialize(second.Fields));
            Assert.AreEqual(JsonSerializer.Serialize(first.Warnings), JsonSerializer.Serialize(second.Warnings));
            CollectionAssert.AreEqual(first.ChildMap.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(),
                second.ChildMap.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray());
        }
    }

    private static bool HasConflict(DataSyncMerge3Result result) =>
        result.Fields.Any(f => f.Resolution is DataSyncFieldResolution.Conflict);

    /// <summary>Every local option stays unless this merge removed it (v3.1 B3, §8.6).</summary>
    private static void AssertNothingLost(CustomPropertyContentV1 local, DataSyncMerge3Result result, string trace)
    {
        var merged = Ids(Merged(result)).ToHashSet(StringComparer.Ordinal);
        foreach (var id in Ids(local))
        {
            Assert.IsTrue(merged.Contains(id) || result.RemovedChildIds.Contains(id), $"{id} lost\n{trace}");
        }
    }

    private static void AssertCandidatesCoverRemovals(DataSyncMerge3Input input, DataSyncMerge3Result result, string trace)
    {
        var candidates = Untyped.ChildDeletionCandidates(new DataSyncChildCandidatesInput(input.Base, input.Local,
            input.LocalOverlay, input.Remote, input.Mode3, input.ChildMap, false)).ToHashSet(StringComparer.Ordinal);
        foreach (var id in result.RemovedChildIds.Concat(result.HeldChildIds))
            Assert.IsTrue(candidates.Contains(id), $"{id} removed or held without its usage asked\n{trace}");
    }
}

/// <summary>
/// Seeded random custom properties and peer edits for the property tests: few labels, so classes collide (with and
/// without IgnoreCase), few colours, tag groups among <c>null</c>, <c>""</c> and two casings.
/// </summary>
internal sealed class Merge3Generator(int seed)
{
    private static readonly string[] Labels = ["a", "A", "b", "B", "ab", "aB", "é", "É"];
    private static readonly string?[] Colors = [null, null, "#1", "#2"];
    private static readonly string?[] Groups = [null, "", "g", "G"];

    private static readonly PropertyType[] Types =
        [PropertyType.MultipleChoice, PropertyType.SingleChoice, PropertyType.Tags, PropertyType.Multilevel];

    private readonly Random _rng = new(seed);
    private int _next;

    public PropertyType Type() => Pick(Types);
    public int Edits() => _rng.Next(1, 5);
    public int Usage() => _rng.Next(3) == 0 ? 1 : 0;
    public DataSyncMergeSide Winner() => _rng.Next(2) == 0 ? DataSyncMergeSide.Local : DataSyncMergeSide.Remote;

    public CustomPropertyContentV1 Content(PropertyType type, string prefix)
    {
        var content = new CustomPropertyContentV1
        {
            Name = "P", Type = type, IgnoreCase = _rng.Next(2) == 0, Settings = CustomPropertyTypes.DefaultSettings(type),
        };
        var roots = Enumerable.Range(0, _rng.Next(7)).Select(_ => NewNode(prefix, type, 1)).ToList();
        content = FromNodes(content, roots);
        return content with { DefaultValue = Defaults(content) };
    }

    /// <summary>Random edits a peer could make, new options under ids with <paramref name="prefix"/>.</summary>
    public CustomPropertyContentV1 Mutate(CustomPropertyContentV1 content, string prefix, int edits)
    {
        var roots = ToNodes(content);
        for (var i = 0; i < edits; i++)
        {
            var all = All(roots).ToList();
            switch (_rng.Next(10))
            {
                case 0 or 1 when all.Count > 0:
                    // Rename, often onto a label another option has.
                    var renamed = Pick(all);
                    renamed.Label = Pick(Labels);
                    if (content.Type == PropertyType.Tags && _rng.Next(2) == 0) renamed.Group = Pick(Groups);
                    break;
                case 2 when all.Count > 0:
                    Pick(all).Color = Pick(Colors);
                    break;
                case 3:
                    var added = NewNode(prefix, content.Type, 3);
                    if (content.Type == PropertyType.Multilevel && all.Count > 0 && _rng.Next(2) == 0)
                        Pick(all).Children.Add(added);
                    else roots.Add(added);
                    break;
                case 4 when all.Count > 0:
                    var deleted = Pick(all);
                    Remove(roots, deleted);
                    break;
                case 5 when content.Type == PropertyType.Multilevel && all.Count > 1:
                    var moved = Pick(all);
                    Remove(roots, moved);
                    var targets = All(roots).ToList();
                    if (targets.Count > 0 && _rng.Next(3) != 0) Pick(targets).Children.Add(moved);
                    else roots.Add(moved);
                    break;
                case 6:
                    content = content with { IgnoreCase = !(content.IgnoreCase ?? false) };
                    break;
                case 7:
                    content = content with { Name = "P" + _rng.Next(3).ToString(CultureInfo.InvariantCulture) };
                    break;
                case 8 when content.Type == PropertyType.Multilevel:
                    content = content with { Settings = new CustomPropertySettingsV1 { ValueIsSingleton = _rng.Next(2) == 0 } };
                    break;
                case 9:
                    content = FromNodes(content, roots) with { DefaultValue = [] };
                    content = content with { DefaultValue = Defaults(content) };
                    break;
            }
        }

        return FromNodes(content, roots);
    }

    /// <summary>Every option id prefixed; <paramref name="childMap"/> gets the peer → local entries.</summary>
    public static CustomPropertyContentV1 Reprefix(CustomPropertyContentV1 content, string prefix,
        Dictionary<string, string> childMap)
    {
        var roots = ToNodes(content);
        foreach (var node in All(roots))
        {
            childMap[prefix + node.Id] = node.Id;
            node.Id = prefix + node.Id;
        }

        var result = FromNodes(content, roots);
        return result with
        {
            DefaultValue = content.DefaultValue.Select(r => CustomPropertyRefs.Resolve(result with { DefaultValue = [] },
                    RefWithId(r, prefix + r.Uuid)) is { } resolved ? CustomPropertyRefs.RefFor(result, resolved.Uuid) : null)
                .OfType<OptionRef>().ToArray(),
        };
    }

    private static OptionRef RefWithId(OptionRef r, string uuid) =>
        r.Path is { } path ? OptionRef.Node(uuid, path) : r.Name is { } name ? OptionRef.Tag(uuid, r.Group, name)
        : OptionRef.Choice(uuid, r.Label!);

    private IReadOnlyList<OptionRef> Defaults(CustomPropertyContentV1 content)
    {
        if (!CustomPropertyTypes.HasDefaultValue(content.Type) || _rng.Next(3) == 0) return [];
        var ids = Cp.Codec.ChildrenOf(content).Select(c => c.Id).ToList();
        if (ids.Count == 0) return [];
        var count = content.Type == PropertyType.SingleChoice ? 1 : _rng.Next(1, 3);
        return Enumerable.Range(0, count).Select(_ => Pick(ids)).Distinct()
            .Select(id => CustomPropertyRefs.RefFor(content, id)!).ToArray();
    }

    private MNode NewNode(string prefix, PropertyType type, int depth)
    {
        var node = new MNode
        {
            Id = prefix + (_next++).ToString(CultureInfo.InvariantCulture), Label = Pick(Labels), Color = Pick(Colors),
            Group = type == PropertyType.Tags ? Pick(Groups) : null,
        };
        if (type == PropertyType.Multilevel && depth < 3)
        {
            for (var i = _rng.Next(depth == 1 ? 3 : 2); i > 0; i--) node.Children.Add(NewNode(prefix, type, depth + 1));
        }

        return node;
    }

    private T Pick<T>(IReadOnlyList<T> items) => items[_rng.Next(items.Count)];

    private static void Remove(List<MNode> roots, MNode target)
    {
        if (roots.Remove(target)) return;
        foreach (var node in All(roots))
        {
            if (node.Children.Remove(target)) return;
        }
    }

    private static IEnumerable<MNode> All(IEnumerable<MNode> roots) =>
        roots.SelectMany(r => new[] { r }.Concat(All(r.Children)));

    private static List<MNode> ToNodes(CustomPropertyContentV1 content) => content.Type switch
    {
        PropertyType.Tags => content.Tags.Select(t => new MNode { Id = t.Uuid!, Label = t.Name, Group = t.Group, Color = t.Color }).ToList(),
        PropertyType.Multilevel => FromTree(content.Nodes),
        _ => content.Choices.Select(c => new MNode { Id = c.Uuid!, Label = c.Label, Color = c.Color }).ToList(),
    };

    private static List<MNode> FromTree(IReadOnlyList<CustomPropertyNodeV1> nodes) =>
        nodes.Select(n =>
        {
            var node = new MNode { Id = n.Uuid!, Label = n.Label, Color = n.Color };
            node.Children.AddRange(FromTree(n.Children));
            return node;
        }).ToList();

    private static CustomPropertyContentV1 FromNodes(CustomPropertyContentV1 content, List<MNode> roots) => content.Type switch
    {
        PropertyType.Tags => content with { Tags = roots.Select(n => new CustomPropertyTagV1(n.Id, n.Group, n.Label, n.Color)).ToArray() },
        PropertyType.Multilevel => content with { Nodes = ToTree(roots) },
        _ => content with { Choices = roots.Select(n => new CustomPropertyChoiceV1(n.Id, n.Label, n.Color)).ToArray() },
    };

    private static IReadOnlyList<CustomPropertyNodeV1> ToTree(IEnumerable<MNode> nodes) =>
        nodes.Select(n => new CustomPropertyNodeV1(n.Id, n.Label, n.Color) { Children = ToTree(n.Children) }).ToArray();

    private sealed class MNode
    {
        public required string Id { get; set; }
        public required string Label { get; set; }
        public string? Group { get; set; }
        public string? Color { get; set; }
        public List<MNode> Children { get; } = [];
    }
}
