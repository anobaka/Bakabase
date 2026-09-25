using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.TestKinds;

/// <summary>The test kind is itself a codec the engine relies on in later tests, so it is held to the same rules.</summary>
[TestClass]
public class TestItemCodecTests
{
    private static readonly IDataSyncKindCodec Codec = TestItemCodec.Instance;

    private static TestItemContent T(string name, string? color, params (string Id, string Label)[] children) =>
        new(name, color, children.Select(c => new TestChild(c.Id, c.Label)));

    private static DataSyncMerge3Input Input(TestItemContent? b, TestItemContent l, TestItemContent r,
        DataSyncMerge3Mode mode3 = DataSyncMerge3Mode.ThreeWay, IReadOnlyDictionary<string, int>? usage = null,
        DataSyncChildDeletionMode deletions = DataSyncChildDeletionMode.Normal,
        DataSyncMergeSide winner = DataSyncMergeSide.Remote, DataSyncOverlay? overlay = null) =>
        new(b, l, overlay ?? DataSyncOverlay.None, r, mode3, new Dictionary<string, string>(), false, false,
            DataSyncLinkMode.TwoWay, true, winner,
            usage ?? l.Children.ToDictionary(c => c.Id, _ => 0), deletions);

    private static TestItemContent Merged(DataSyncMerge3Result result) => (TestItemContent)result.Merged;

    private static string Form(object content) => CanonicalJson.Serialize(Codec.ComparisonForm(content, null, false));

    [TestMethod]
    public void WriteOfReadLocalIsByteIdentical()
    {
        var content = T("Genre", "#fff", ("1", "Action"), ("2", "Action"), ("3", "a\u0000b"));
        var json = CanonicalJson.Serialize(Codec.Write(content));
        Assert.AreEqual(json, CanonicalJson.Serialize(Codec.Write(Codec.ReadLocal(JsonNode.Parse(json)!.AsObject()))));
        Assert.AreEqual("{\"name\":\"Plain\"}", CanonicalJson.Serialize(Codec.Write(T("Plain", ""))));
    }

    [TestMethod]
    public void ReadDropsInvalidChildrenAndPublishWithholdsThem()
    {
        var local = T("Genre", null, ("1", "Action"), ("1", "Again"), ("2", ""), ("*", "Star"), ("4", "Kept"));
        var published = Codec.Publish(local, new DataSyncOverlay(["4"], []), false);
        Assert.AreEqual(T("Genre", null, ("1", "Action")), published.Content);
        Assert.AreEqual(4, published.ChildrenWithheld);
    }

    [TestMethod]
    public void PeerDeletionsGoByUsage()
    {
        var b = T("G", null, ("1", "A"), ("2", "B"), ("3", "C"));
        var r = T("G", null, ("1", "A"));
        var result = Codec.Merge3(Input(b, b, r, usage: new Dictionary<string, int> { ["1"] = 0, ["2"] = 0, ["3"] = 4 }));
        CollectionAssert.AreEqual(new[] { "2" }, result.RemovedChildIds.ToArray());
        CollectionAssert.AreEqual(new[] { "3" }, result.HeldChildIds.ToArray());
        Assert.AreEqual(T("G", null, ("1", "A"), ("3", "C")), Merged(result));
        Assert.AreEqual(DataSyncFieldResolution.DeletionHeldInUse,
            result.Fields.Single(f => f.Path == "child:3").Resolution);

        // A missing usage entry counts as in use.
        var unknownUsage = Codec.Merge3(Input(b, b, r, usage: new Dictionary<string, int>()));
        CollectionAssert.AreEquivalent(new[] { "2", "3" }, unknownUsage.HeldChildIds.ToArray());

        CollectionAssert.AreEquivalent(new[] { "2", "3" },
            Codec.ChildDeletionCandidates(new DataSyncChildCandidatesInput(b, b, DataSyncOverlay.None, r,
                DataSyncMerge3Mode.ThreeWay, new Dictionary<string, string>(), false)).ToArray());
    }

    [TestMethod]
    public void AChildChangedHereOrThereWinsOverADeletion()
    {
        var b = T("G", null, ("1", "A"), ("2", "B"));
        // Renamed here, deleted there: kept.
        var keptHere = Codec.Merge3(Input(b, T("G", null, ("1", "A"), ("2", "B2")), T("G", null, ("1", "A"))));
        Assert.AreEqual(T("G", null, ("1", "A"), ("2", "B2")), Merged(keptHere));

        // Deleted here, renamed there: restored.
        var restored = Codec.Merge3(Input(b, T("G", null, ("1", "A")), T("G", null, ("1", "A"), ("2", "B2"))));
        Assert.AreEqual(T("G", null, ("1", "A"), ("2", "B2")), Merged(restored));
        Assert.IsTrue(restored.Warnings.Any(w => w.Code == DataSyncWarningCode.ChildRestored));

        // Deleted here, unchanged there: stays deleted.
        var stays = Codec.Merge3(Input(b, T("G", null, ("1", "A")), b));
        Assert.AreEqual(T("G", null, ("1", "A")), Merged(stays));
    }

    [TestMethod]
    public void MassDeletionTripsB4UnlessTheItemSaysOtherwise()
    {
        var children = Enumerable.Range(0, 20).Select(i => (i.ToString(), "L" + i)).ToArray();
        var b = T("G", null, children);
        var r = T("G", null, children.Take(10).ToArray());

        var tripped = Codec.Merge3(Input(b, b, r));
        Assert.AreEqual(10, tripped.MassDeletionCandidates.Count);
        Assert.AreEqual(b, Merged(tripped));

        Assert.AreEqual(10, Codec.Merge3(Input(b, b, r, deletions: DataSyncChildDeletionMode.Apply)).RemovedChildIds.Count);
        Assert.AreEqual(10, Codec.Merge3(Input(b, b, r, deletions: DataSyncChildDeletionMode.ReviewEach)).HeldChildIds.Count);
        var restore = Codec.Merge3(Input(b, b, r, deletions: DataSyncChildDeletionMode.Restore));
        Assert.AreEqual(b, Merged(restore));
        Assert.AreEqual(0, restore.HeldChildIds.Count + restore.RemovedChildIds.Count);
    }

    [TestMethod]
    public void ColoursFollowTheAppearanceRules()
    {
        var b = T("G", "#b");
        Assert.AreEqual("#r", Merged(Codec.Merge3(Input(b, b, T("G", "#r")))).Color);
        Assert.AreEqual(null, Merged(Codec.Merge3(Input(b, b, T("G", null)))).Color, "a cleared colour syncs");
        Assert.AreEqual("#l", Merged(Codec.Merge3(Input(b, T("G", "#l"), T("G", "#r"), winner: DataSyncMergeSide.Local))).Color);
        Assert.AreEqual("#r", Merged(Codec.Merge3(Input(b, T("G", "#l"), T("G", "#r")))).Color);
        Assert.AreEqual("#l", Merged(Codec.Merge3(Input(null, T("G", "#l"), T("G", null), DataSyncMerge3Mode.NoBase))).Color,
            "without a base a colour is never cleared");
    }

    [TestMethod]
    public void OverlayChildrenStayAndAHoldIsReleasedWhenThePeerReAddsIt()
    {
        var overlay = new DataSyncOverlay(["mine"], [new DataSyncHeldChild("held", 2)]);
        var local = T("G", null, ("1", "A"), ("held", "H"), ("mine", "M"));
        var result = Codec.Merge3(Input(T("G", null, ("1", "A")), local, T("G", null, ("1", "A"), ("held", "H")),
            overlay: overlay));
        Assert.AreEqual(local, Merged(result));
        CollectionAssert.AreEqual(new[] { "held" }, result.ReleasedChildIds.ToArray());
    }

    [TestMethod]
    public void FastForwardClosure()
    {
        var random = new Random(11);
        for (var run = 0; run < 2_000; run++)
        {
            var l = RandomItem(random);
            var r = random.Next(2) == 0 ? Edit(l, random) : RandomItem(random);
            var result = Codec.Merge3(Input(null, l, r, DataSyncMerge3Mode.FastForward,
                deletions: DataSyncChildDeletionMode.Apply));
            Assert.AreEqual(Form(r), Form(result.Merged), $"L={l} R={r}");
        }
    }

    [TestMethod]
    public void SymmetricMerge()
    {
        var random = new Random(12);
        var compared = 0;
        for (var run = 0; run < 2_000; run++)
        {
            var b = RandomItem(random);
            var l = Edit(b, random);
            var r = Edit(b, random);
            var usage = l.Children.Concat(r.Children).Select(c => c.Id).Distinct().ToDictionary(id => id, _ => 0);
            DataSyncMerge3Input Directed(TestItemContent local, TestItemContent remote, DataSyncMergeSide winner) =>
                Input(b, local, remote, usage: usage, deletions: DataSyncChildDeletionMode.Apply, winner: winner);

            var forward = Codec.Merge3(Directed(l, r, DataSyncMergeSide.Remote));
            var backward = Codec.Merge3(Directed(r, l, DataSyncMergeSide.Local));
            if (forward.Fields.Concat(backward.Fields).Any(f => f.Resolution == DataSyncFieldResolution.Conflict)) continue;
            compared++;
            Assert.AreEqual(Form(forward.Merged), Form(backward.Merged), $"B={b} L={l} R={r}");
        }

        Assert.IsTrue(compared > 1_000, compared.ToString());
    }

    [TestMethod]
    public void RoundTripsThroughTheWire()
    {
        var content = Codec.Write(T("Genre", "#fff", ("1", "Action"), ("2", "Drama")));
        var pages = DataSyncWireWriter.WritePages("s", TestItemCodec.Kind, 0,
            [new DataSyncWireRecord([new string('a', 32)], "node", 1, DataSyncVersionVector.Empty, null, false, 1,
                "a0", content, ContentHash.Of(content), null, 0)], DataSyncLimits.Default);
        var assembler = new DataSyncRecordAssembler(Codec, DataSyncLimits.Default);
        assembler.Add(DataSyncWireReader.ReadPage(pages[0], "s", TestItemCodec.Kind, DataSyncLimits.Default));
        var entity = assembler.Complete(1, false).Entities.Single();
        Assert.AreEqual(T("Genre", "#fff", ("1", "Action"), ("2", "Drama")), entity.Content);
        Assert.AreEqual("Genre", entity.DisplayName);
    }

    private static readonly string[] Labels = ["A", "B", "C", "D", "E", "F"];

    private static TestItemContent RandomItem(Random random)
    {
        var count = random.Next(0, 6);
        return T(random.Next(2) == 0 ? "Genre" : "Tags", random.Next(3) == 0 ? null : "#" + random.Next(3),
            Enumerable.Range(0, count).Select(i => ("r" + random.Next(1000) + "-" + i, Labels[random.Next(Labels.Length)]))
                .ToArray());
    }

    private static int _fresh;

    private static TestItemContent Edit(TestItemContent content, Random random)
    {
        var children = content.Children.ToList();
        for (var i = random.Next(0, 3); i > 0; i--)
        {
            switch (random.Next(3))
            {
                case 0 when children.Count > 0:
                    children.RemoveAt(random.Next(children.Count));
                    break;
                case 1 when children.Count > 0:
                    var at = random.Next(children.Count);
                    children[at] = children[at] with { Label = Labels[random.Next(Labels.Length)] };
                    break;
                default:
                    children.Add(new TestChild("n" + Interlocked.Increment(ref _fresh), Labels[random.Next(Labels.Length)]));
                    break;
            }
        }

        var color = random.Next(4) switch { 0 => null, 1 => "#" + random.Next(3), _ => content.Color };
        return new TestItemContent(random.Next(5) == 0 ? content.Name + "!" : content.Name, color, children);
    }
}
