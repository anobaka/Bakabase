using System.Globalization;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Tests.CustomProperties;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Merging;

/// <summary>
/// Random content for the Merge3 property tests (§13.1), per kind this package ships a codec for: the test kind
/// (duplicate labels, renames onto a label another child has, colours set and cleared, children added, removed and
/// moved) and extension groups (stored raw extensions in any case, with or without their dot). Custom properties take
/// their contents from B's own generator (<see cref="Merge3Generator"/>: IgnoreCase, tag groups, multilevel moves).
/// </summary>
internal sealed class MergeContentGenerator(Random random)
{
    private static readonly string[] Names = ["Genre", "Mood", "Artist", "Studio"];
    private static readonly string[] Labels = ["Action", "Drama", "Comedy", "action"];
    private static readonly string?[] Colors = [null, "#e5484d", "#30a46c", "#0090ff"];
    private static readonly string?[] Types = [null, "Tags", "Choice"];
    private static readonly string[] RawExtensions = [".mp4", ".MKV", "mkv", ".avi", " .Jpg", ".png", "zip", ".mp3"];

    /// <summary>Stored extensions a reader would drop (never published, §3.5): only ever on this device's side.</summary>
    private static readonly string[] Unpublishable = ["a b", ".x,y", ".abcdefghijklmnopqrstuvwxyz0123456789"];
    private int _next;

    public Random Random { get; } = random;

    public string FreshId(string prefix) => prefix + (++_next).ToString(CultureInfo.InvariantCulture);

    private T Pick<T>(IReadOnlyList<T> values) => values[Random.Next(values.Count)];

    public TestItemContent Item(string idPrefix) =>
        new(Pick(Names), Pick(Colors),
            Enumerable.Range(0, Random.Next(0, 7)).Select(_ => new TestChild(FreshId(idPrefix), Pick(Labels))).ToList(),
            Pick(Types));

    /// <summary>One to four edits that keep the subtype (a type change never reaches Merge3, §8.5.6).</summary>
    public TestItemContent Edit(TestItemContent content, string idPrefix)
    {
        for (var i = Random.Next(1, 5); i > 0; i--)
        {
            var children = content.Children.ToList();
            switch (Random.Next(8))
            {
                case 0:
                    content = content.With(name: Pick(Names) + (Random.Next(2) == 0 ? "" : "!"));
                    break;
                case 1:
                    var color = Pick(Colors);
                    content = color is null ? content.With(clearColor: true) : content.With(color: color);
                    break;
                case 2 or 3:
                    children.Insert(Random.Next(children.Count + 1), new TestChild(FreshId(idPrefix), Pick(Labels)));
                    content = content.With(children: children);
                    break;
                case 4 when children.Count > 0:
                    children.RemoveAt(Random.Next(children.Count));
                    content = content.With(children: children);
                    break;
                case 5 when children.Count > 0:
                    // Onto another child's label about half the time.
                    var at = Random.Next(children.Count);
                    var label = Random.Next(2) == 0 ? Pick(children).Label : Pick(Labels) + "'";
                    children[at] = children[at] with { Label = label };
                    content = content.With(children: children);
                    break;
                case 6 when children.Count > 1:
                    var moved = children[Random.Next(children.Count)];
                    children.Remove(moved);
                    children.Insert(Random.Next(children.Count + 1), moved);
                    content = content.With(children: children);
                    break;
                case 7 when children.Count > 0:
                    children.Add(new TestChild(FreshId(idPrefix), Pick(children).Label));   // a duplicate label
                    content = content.With(children: children);
                    break;
            }
        }

        return content;
    }

    /// <summary>
    /// A local extension group as stored: raw extensions, any case, with or without a dot, and now and then one a
    /// reader would drop.
    /// </summary>
    public ExtensionGroupContentV1 Group() =>
        ExtensionGroupContentV1.FromLocal(Pick(Names), RawExtensions.Where(_ => Random.Next(3) == 0)
            .Concat(Unpublishable.Where(_ => Random.Next(4) == 0)));

    public ExtensionGroupContentV1 Edit(ExtensionGroupContentV1 content)
    {
        for (var i = Random.Next(1, 4); i > 0; i--)
        {
            var extensions = content.Extensions.ToList();
            switch (Random.Next(3))
            {
                case 0:
                    content = new ExtensionGroupContentV1(Pick(Names) + (Random.Next(2) == 0 ? "" : "!"), extensions);
                    break;
                case 1:
                    content = ExtensionGroupContentV1.FromLocal(content.Name, [.. extensions, Pick(RawExtensions)]);
                    break;
                default:
                    if (extensions.Count == 0) break;
                    extensions.RemoveAt(Random.Next(extensions.Count));
                    content = new ExtensionGroupContentV1(content.Name, extensions);
                    break;
            }
        }

        return content;
    }

    /// <summary>Content as a peer publishes it: validated through the codec's own Publish.</summary>
    public static object Published(IDataSyncKindCodec codec, object content) =>
        codec.Publish(content, DataSyncOverlay.None, false).Content!;

    public static string Form(IDataSyncKindCodec codec, object content) =>
        CanonicalJson.Serialize(codec.ComparisonForm(Published(codec, content), null, false));

    public static IReadOnlyDictionary<string, int> Unused(IDataSyncKindCodec codec, object local) =>
        codec.ChildrenOf(local).ToDictionary(c => c.Id, _ => 0);

    public static DataSyncMerge3Input Input(IDataSyncKindCodec codec, object? baseContent, object local, object remote,
        DataSyncMerge3Mode mode3, IReadOnlyDictionary<string, string> childMap, DataSyncMergeSide appearanceWinner) =>
        new(baseContent, local, DataSyncOverlay.None, remote, mode3, childMap, false, false, DataSyncLinkMode.TwoWay, true,
            appearanceWinner, Unused(codec, local), DataSyncChildDeletionMode.Normal);

    public static string Describe(IDataSyncKindCodec codec, object? content) =>
        content is null ? "∅" : CanonicalJson.Serialize(codec.Write(content));
}

/// <summary>
/// §8.5: for random <c>(L, R)</c> pairs where R descends from L — no overlays, the same subtype, no
/// <c>childrenLocal</c> — fast-forwarding L to R reaches R's comparison form, whatever the peer calls L's children
/// (the same ids, its own ids through the base's child map, or its own ids with no map at all).
/// </summary>
[TestClass]
public class FastForwardClosureTests
{
    private const int Pairs = 2_000;

    [TestMethod]
    public void FastForwardReachesTheRecordsFormForTheTestKind()
    {
        var codec = TestItemCodec.Instance;
        var g = new MergeContentGenerator(new Random(8_5_1));
        var failures = new List<string>();
        for (var i = 0; i < Pairs && failures.Count < 5; i++)
        {
            var local = g.Item("L");
            var scheme = g.Random.Next(3);
            // The peer's names for this device's children: the same ids, or its own ("p…") with or without a map.
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var asPeerHasIt = scheme == 0
                ? local
                : local.With(children: local.Children.Select(c => c with { Id = "p" + c.Id }).ToList());
            if (scheme == 1) foreach (var c in local.Children) map["p" + c.Id] = c.Id;
            var remote = (TestItemContent)MergeContentGenerator.Published(codec, g.Edit(asPeerHasIt, "R"));

            var result = codec.Merge3(MergeContentGenerator.Input(codec, null, local, remote,
                DataSyncMerge3Mode.FastForward, map, DataSyncMergeSide.Remote));
            var expected = MergeContentGenerator.Form(codec, remote);
            var actual = MergeContentGenerator.Form(codec, result.Merged);
            if (expected != actual)
            {
                failures.Add($"pair {i} (ids scheme {scheme}):\n  L = {MergeContentGenerator.Describe(codec, local)}\n" +
                             $"  R = {MergeContentGenerator.Describe(codec, remote)}\n  merged = {actual}\n  R's form = {expected}");
            }
        }

        Assert.AreEqual(0, failures.Count, "\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// Custom properties (package B's codec) with B's generator: duplicates, IgnoreCase, <c>null</c>/<c>""</c> tag
    /// groups, colours set and cleared, node moves and renames onto existing keys, and on the local side options this
    /// device never publishes. The peer's names for this device's options are its own, through the child map, a third of
    /// the time. B4 is out of the way (deletions apply): a mass deletion keeps the local content by design.
    /// </summary>
    [TestMethod]
    public void FastForwardReachesTheRecordsFormForCustomProperties()
    {
        IDataSyncKindCodec codec = CustomPropertyCodec.Instance;
        var failures = new List<string>();
        for (var i = 0; i < Pairs && failures.Count < 5; i++)
        {
            var g = new Merge3Generator(8_5_5_000 + i);
            var local = g.Content(g.Type(), "l");
            var remoteRaw = g.Mutate(local, "r", g.Edits());
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (i % 3 == 1) remoteRaw = Merge3Generator.Reprefix(remoteRaw, "p", map);
            var remote = MergeContentGenerator.Published(codec, remoteRaw);
            local = g.Unpublishable(local, "x");

            var result = codec.Merge3(new DataSyncMerge3Input(null, local, DataSyncOverlay.None, remote,
                DataSyncMerge3Mode.FastForward, map, false, false, DataSyncLinkMode.TwoWay, true, DataSyncMergeSide.Remote,
                MergeContentGenerator.Unused(codec, local), DataSyncChildDeletionMode.Apply));
            // What this device publishes after the merge: a child the merge holds (a subtree holding an option that
            // cannot be removed, such as one without a uuid, counts as in use) is withheld like any held child.
            var held = new DataSyncOverlay([], result.HeldChildIds.Select(id => new DataSyncHeldChild(id, 1)).ToArray());
            var expected = MergeContentGenerator.Form(codec, remote);
            var actual = CanonicalJson.Serialize(codec.ComparisonForm(codec.Publish(result.Merged, held, false).Content!, null,
                false));
            if (result.TypeChanged || expected != actual)
            {
                failures.Add($"pair {i}:\n  L = {MergeContentGenerator.Describe(codec, local)}\n" +
                             $"  R = {MergeContentGenerator.Describe(codec, remote)}\n  merged = {actual}\n  R's form = {expected}");
            }
        }

        Assert.AreEqual(0, failures.Count, "\n" + string.Join("\n", failures));
    }

    [TestMethod]
    public void FastForwardReachesTheRecordsFormForExtensionGroups()
    {
        IDataSyncKindCodec codec = ExtensionGroupCodec.Instance;
        var g = new MergeContentGenerator(new Random(8_5_2));
        var failures = new List<string>();
        for (var i = 0; i < Pairs && failures.Count < 5; i++)
        {
            var local = g.Group();
            var remote = MergeContentGenerator.Published(codec, g.Edit(
                (ExtensionGroupContentV1)MergeContentGenerator.Published(codec, local)));
            var result = codec.Merge3(MergeContentGenerator.Input(codec, null, local, remote,
                DataSyncMerge3Mode.FastForward, new Dictionary<string, string>(), DataSyncMergeSide.Remote));
            var expected = MergeContentGenerator.Form(codec, remote);
            var actual = MergeContentGenerator.Form(codec, result.Merged);
            if (expected != actual)
            {
                failures.Add($"pair {i}:\n  L = {MergeContentGenerator.Describe(codec, local)}\n" +
                             $"  R = {MergeContentGenerator.Describe(codec, remote)}\n  merged = {actual}\n  R's form = {expected}");
            }
        }

        Assert.AreEqual(0, failures.Count, "\n" + string.Join("\n", failures));
    }
}

/// <summary>
/// §8.5: <c>Merge3(L, R, B)</c> and <c>Merge3(R, L, B)</c> give equal forms when neither reports a conflict — the
/// two devices of a link reach one content from the same three versions. The appearance winner is the same device
/// both times (§8.5.5), as it is between two devices. Triples that stop at a question (a conflict, a child held or
/// a mass deletion) are left out; each run asserts enough triples remain.
/// </summary>
[TestClass]
public class SymmetricMergeTests
{
    private const int Triples = 2_000;

    private static bool Settles(DataSyncMerge3Result r) =>
        !r.TypeChanged && r.MassDeletionCandidates.Count == 0 && r.HeldChildIds.Count == 0 &&
        r.Fields.All(f => f.Resolution != DataSyncFieldResolution.Conflict);

    private static void AssertSymmetric(IDataSyncKindCodec codec, int seed,
        Func<MergeContentGenerator, (object Base, object Local, object Remote)> triple,
        DataSyncChildDeletionMode deletions = DataSyncChildDeletionMode.Normal)
    {
        var g = new MergeContentGenerator(new Random(seed));
        var failures = new List<string>();
        var settled = 0;
        for (var i = 0; i < Triples && failures.Count < 5; i++)
        {
            var (b, l, r) = triple(g);
            var here = codec.Merge3(MergeContentGenerator.Input(codec, b, l, MergeContentGenerator.Published(codec, r),
                DataSyncMerge3Mode.ThreeWay, new Dictionary<string, string>(), DataSyncMergeSide.Remote) with
            {
                ChildDeletions = deletions,
            });
            var there = codec.Merge3(MergeContentGenerator.Input(codec, b, r, MergeContentGenerator.Published(codec, l),
                DataSyncMerge3Mode.ThreeWay, new Dictionary<string, string>(), DataSyncMergeSide.Local) with
            {
                ChildDeletions = deletions,
            });
            if (!Settles(here) || !Settles(there)) continue;
            settled++;
            var formHere = MergeContentGenerator.Form(codec, here.Merged);
            var formThere = MergeContentGenerator.Form(codec, there.Merged);
            if (formHere != formThere)
            {
                failures.Add($"triple {i}:\n  B = {MergeContentGenerator.Describe(codec, b)}\n" +
                             $"  L = {MergeContentGenerator.Describe(codec, l)}\n  R = {MergeContentGenerator.Describe(codec, r)}\n" +
                             $"  Merge3(L,R,B) = {formHere}\n  Merge3(R,L,B) = {formThere}");
            }
        }

        Assert.AreEqual(0, failures.Count, "\n" + string.Join("\n", failures));
        Assert.IsTrue(settled >= Triples / 4, $"only {settled} of {Triples} triples merged without a question");
    }

    [TestMethod]
    public void BothOrdersGiveOneFormForTheTestKind()
    {
        var codec = TestItemCodec.Instance;
        AssertSymmetric(codec, 8_5_3, g =>
        {
            // One id space: both devices keep the base's child ids (the child map is the identity).
            var b = (TestItemContent)MergeContentGenerator.Published(codec, g.Item("B"));
            return (b, g.Edit(b, "L"), g.Edit(b, "R"));
        });
    }

    /// <summary>
    /// Custom properties (package B's codec) from one base with B's generator, each side with options of its own that
    /// it never publishes. The known failures of this property are pinned in <c>Merge3PropertyTests</c>.
    /// </summary>
    [TestMethod]
    public void BothOrdersGiveOneFormForCustomProperties()
    {
        IDataSyncKindCodec codec = CustomPropertyCodec.Instance;
        var index = 0;
        AssertSymmetric(codec, 8_5_6, _ =>
        {
            var g = new Merge3Generator(8_5_6_000 + index++);
            var b = (CustomPropertyContentV1)MergeContentGenerator.Published(codec, g.Content(g.Type(), "b"));
            return (b, g.Unpublishable(g.Mutate(b, "l", g.Edits()), "xl"), g.Unpublishable(g.Mutate(b, "r", g.Edits()), "xr"));
        }, DataSyncChildDeletionMode.Apply);
    }

    [TestMethod]
    public void BothOrdersGiveOneFormForExtensionGroups()
    {
        IDataSyncKindCodec codec = ExtensionGroupCodec.Instance;
        AssertSymmetric(codec, 8_5_4, g =>
        {
            var b = (ExtensionGroupContentV1)MergeContentGenerator.Published(codec, g.Group());
            return (b, g.Edit(b), g.Edit(b));
        });
    }
}
