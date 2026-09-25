using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Kinds.ExtensionGroups;

[TestClass]
public class ExtensionGroupCodecTests
{
    private static readonly ExtensionGroupCodec Codec = ExtensionGroupCodec.Instance;
    private static readonly IDataSyncKindCodec Untyped = Codec;

    private static ExtensionGroupContentV1 G(string name, params string[] extensions) => new(name, extensions);

    private static DataSyncMerge3Input Input(ExtensionGroupContentV1? b, ExtensionGroupContentV1 l,
        ExtensionGroupContentV1 r, DataSyncMerge3Mode mode3 = DataSyncMerge3Mode.ThreeWay,
        DataSyncOverlay? overlay = null, DataSyncLinkMode mode = DataSyncLinkMode.TwoWay, bool lastEditorIsSelf = true) =>
        new(b, l, overlay ?? DataSyncOverlay.None, r, mode3, new Dictionary<string, string>(), false, false, mode,
            lastEditorIsSelf, DataSyncMergeSide.Remote, new Dictionary<string, int>(), DataSyncChildDeletionMode.Normal);

    private static ExtensionGroupContentV1 Merged(DataSyncMerge3Result result) => (ExtensionGroupContentV1)result.Merged;

    // ---- descriptor and canonical form --------------------------------------------------------

    [TestMethod]
    public void DescriptorMatchesTheSpec()
    {
        var d = Codec.Descriptor;
        Assert.AreEqual("extensionGroup", d.Kind);
        Assert.AreEqual(1, d.SchemaVersion);
        Assert.AreEqual(typeof(ExtensionGroupContentV1), d.ContentType);
        Assert.IsTrue(d.AutoLinkIdentical);
        Assert.IsFalse(d.HasOrder);
        Assert.IsTrue(d.HasChildren);
        Assert.IsFalse(d.SupportsChildrenLocal);
        Assert.AreEqual("extension", d.ChildNoun);
        Assert.AreEqual(0, d.DependsOn.Count);
    }

    [TestMethod]
    public void LocalExtensionsAreCanonicalized()
    {
        var content = ExtensionGroupContentV1.FromLocal("Video", [" MKV", ".Mp4", "..avi", "mkv", "", "  ", null]);
        CollectionAssert.AreEqual(new[] { ".avi", ".mkv", ".mp4" }, content.Extensions.ToArray());
        Assert.AreEqual("{\"extensions\":[\".avi\",\".mkv\",\".mp4\"],\"name\":\"Video\"}",
            CanonicalJson.Serialize(Codec.Write(content)));
        Assert.AreEqual("{\"extensions\":[],\"name\":\"Empty\"}",
            CanonicalJson.Serialize(Codec.Write(ExtensionGroupContentV1.FromLocal("Empty", null))));
    }

    [TestMethod]
    public void WriteOfReadLocalIsByteIdenticalAndKeepsInvalidLocalExtensions()
    {
        // ".a b" and ".*" fail the reader's pattern; a 40-character extension is too long. Local content keeps them.
        var local = ExtensionGroupContentV1.FromLocal("Odd", ["a b", "*", new string('x', 40), "mkv"]);
        var json = Codec.Write(local);
        var bytes = CanonicalJson.SerializeToUtf8Bytes(json);
        var again = CanonicalJson.SerializeToUtf8Bytes(Codec.Write(Codec.ReadLocal(JsonNode.Parse(bytes)!.AsObject())));
        CollectionAssert.AreEqual(bytes, again);
        Assert.AreEqual(4, Codec.ReadLocal(json).Extensions.Count);
    }

    // ---- peer read ----------------------------------------------------------------------------

    [TestMethod]
    public void ReadCanonicalizesAndDropsInvalidExtensions()
    {
        var content = new JsonObject
        {
            ["extensions"] = new JsonArray(".MKV", "mp4", ".a b", ".*", 5, ".mkv", "." + new string('x', 40), " "),
            ["name"] = "Video",
        };
        var result = Codec.Read(content, DataSyncLimits.Default);
        Assert.IsNull(result.Held);
        CollectionAssert.AreEqual(new[] { ".mkv", ".mp4" }, ((ExtensionGroupContentV1)result.Content!).Extensions.ToArray());
        var dropped = result.Warnings.Where(w => w.Code == DataSyncWarningCode.OptionDropped).ToList();
        Assert.AreEqual(5, dropped.Count);
        Assert.IsTrue(dropped.All(w => w.Args!["reason"] == "extension"));
        Assert.IsNull(result.Unknown);
    }

    [TestMethod]
    public void ReadKeepsUnknownMembersVerbatim()
    {
        var content = new JsonObject
        {
            ["extensions"] = new JsonArray(".mkv"), ["name"] = "Video", ["x-icon"] = new JsonObject { ["a"] = 1 },
        };
        var result = Codec.Read(content, DataSyncLimits.Default);
        Assert.AreEqual("{\"x-icon\":{\"a\":1}}", CanonicalJson.Serialize(result.Unknown));
        var warning = result.Warnings.Single(w => w.Code == DataSyncWarningCode.UnknownFieldsIgnored);
        Assert.AreEqual("1", warning.Args!["count"]);
    }

    [TestMethod]
    public void ReadHoldsInvalidEntities()
    {
        var limits = DataSyncLimits.Default with { MaxOptionsPerProperty = 3 };
        var cases = new[]
        {
            new JsonObject { ["extensions"] = new JsonArray() },
            new JsonObject { ["extensions"] = new JsonArray(), ["name"] = "" },
            new JsonObject { ["extensions"] = new JsonArray(), ["name"] = new string('n', 257) },
            new JsonObject { ["extensions"] = new JsonArray(), ["name"] = "a\u0000" },
            new JsonObject { ["extensions"] = new JsonArray(), ["name"] = "\ud800x" },
            new JsonObject { ["name"] = "No list" },
            new JsonObject { ["extensions"] = ".mkv", ["name"] = "Not a list" },
            new JsonObject { ["extensions"] = new JsonArray(".a", ".b", ".c", ".d"), ["name"] = "Too many" },
            new JsonObject { ["extensions"] = new JsonArray(), ["name"] = 5 },
        };
        foreach (var content in cases)
        {
            var result = Codec.Read(content, limits);
            Assert.AreEqual(DataSyncHeldReason.Invalid, result.Held, content.ToJsonString());
            Assert.IsNull(result.Content);
        }

        Assert.IsNull(Codec.Read(new JsonObject { ["extensions"] = new JsonArray(), ["name"] = "Tab\tand\nline" },
            limits).Held, "tabs and line feeds are allowed in names");
    }

    // ---- review members -----------------------------------------------------------------------

    [TestMethod]
    public void MatchNaturalLevels()
    {
        Assert.AreEqual(DataSyncNaturalMatch.Identical, Codec.MatchNatural(G("Video", ".mkv"), G("Video", ".mkv")));
        Assert.AreEqual(DataSyncNaturalMatch.Exact, Codec.MatchNatural(G("Video", ".mkv"), G("Video", ".mp4")));
        Assert.AreEqual(DataSyncNaturalMatch.Similar, Codec.MatchNatural(G(" video ", ".mkv"), G("Video", ".mkv")),
            "Identical needs an ordinal-equal name");
        Assert.AreEqual(DataSyncNaturalMatch.None, Codec.MatchNatural(G("Video", ".mkv"), G("Audio", ".mkv")));
    }

    [TestMethod]
    public void DiffOnlyAdds()
    {
        var diff = Codec.Diff(G("Video", ".avi", ".mkv"), G("Videos", ".mkv", ".mp4", ".webm"));
        CollectionAssert.AreEqual(new[] { "name", "ext:add:.mp4", "ext:add:.webm" },
            diff.Changes.Select(c => c.ChangeId).ToArray());
        Assert.AreEqual(DataSyncFieldChangeKind.AddMember, diff.Changes[1].Kind);
        Assert.AreEqual("extensions", diff.Changes[1].Path);
        Assert.AreEqual(1, diff.UnchangedChildren);
        Assert.AreEqual(1, diff.LocalOnlyChildren);
    }

    [TestMethod]
    public void MergeHonoursExclusionsAndKeepsLocalExtensions()
    {
        var local = ExtensionGroupContentV1.FromLocal("Video", [".avi", "a b"]);
        var incoming = G("Videos", ".mkv", ".mp4");
        var merged = Codec.Merge(local, incoming, new HashSet<string> { "ext:add:.mp4" });
        Assert.AreEqual(G("Video", ".a b", ".avi", ".mp4"), merged.Content);
        CollectionAssert.AreEqual(new[] { ".mp4" }, merged.AddedChildIds.ToArray());
        Assert.AreEqual(0, merged.ChildIdMap.Count);

        var renamed = Codec.Merge(local, incoming, new HashSet<string> { "name" });
        Assert.AreEqual("Videos", ((ExtensionGroupContentV1)renamed.Content).Name);
    }

    [TestMethod]
    public void PrepareCreateUsesTheNameOverride()
    {
        var created = Codec.PrepareCreate(G("Video", ".mkv"), "Video (NAS)");
        Assert.AreEqual(G("Video (NAS)", ".mkv"), created.Content);
        Assert.AreEqual(G("Video", ".mkv"), Codec.PrepareCreate(G("Video", ".mkv"), null).Content);
    }

    // ---- publish and the comparison form ------------------------------------------------------

    [TestMethod]
    public void PublishRemovesOverlaysAndWithholdsWhatAReaderWouldDrop()
    {
        var local = ExtensionGroupContentV1.FromLocal("Video", [".mkv", ".mp4", ".webm", "a b"]);
        var overlay = new DataSyncOverlay([".webm"], [new DataSyncHeldChild(".mp4", 3)]);
        var published = Untyped.Publish(local, overlay, childrenLocal: false);
        Assert.AreEqual(G("Video", ".mkv"), published.Content);
        Assert.AreEqual(3, published.ChildrenWithheld);
        Assert.IsNull(published.Held);
        Assert.IsTrue(published.Warnings.Any(w => w.Code == DataSyncWarningCode.OptionDropped));
    }

    [TestMethod]
    public void PublishHoldsAnEntityAReaderWouldHold()
    {
        var published = Untyped.Publish(G("", ".mkv"), DataSyncOverlay.None, false);
        Assert.IsNull(published.Content);
        Assert.AreEqual(DataSyncHeldReason.Invalid, published.Held);
        Assert.IsNotNull(published.HeldDetail);
    }

    [TestMethod]
    public void ComparisonFormIsTheCanonicalContentAndIgnoresOrder()
    {
        var content = G("Video", ".mkv", ".mp4");
        Assert.AreEqual(CanonicalJson.Serialize(Codec.Write(content)),
            CanonicalJson.Serialize(Untyped.ComparisonForm(content, "a0", true)));
        Assert.AreEqual(1, Codec.ComparisonFormVersion);

        // A stored ".MKV" and an incoming ".mkv" have the same shared hash.
        var stored = ExtensionGroupContentV1.FromLocal("Video", [".MKV", ".Mp4"]);
        var incoming = (ExtensionGroupContentV1)Codec.Read(Codec.Write(content), DataSyncLimits.Default).Content!;
        Assert.AreEqual(DataSyncContentForms.SharedHash(Codec, Untyped.Publish(stored, DataSyncOverlay.None, false).Content!,
                null, false, null),
            DataSyncContentForms.SharedHash(Codec, incoming, null, false, null));
    }

    [TestMethod]
    public void ChildDeletionCandidatesAskForNoUsage()
    {
        var input = new DataSyncChildCandidatesInput(G("V", ".a", ".b"), G("V", ".a", ".b"), DataSyncOverlay.None,
            G("V"), DataSyncMerge3Mode.ThreeWay, new Dictionary<string, string>(), false);
        Assert.AreEqual(0, Untyped.ChildDeletionCandidates(input).Count);
    }

    // ---- Merge3 -------------------------------------------------------------------------------

    [TestMethod]
    public void ThreeWaySetFormulaCoversEveryMembershipCase()
    {
        // One extension per (base, local, remote) membership; the name of each says which sets hold it.
        string[] all = [".b", ".l", ".r", ".bl", ".br", ".lr", ".blr"];
        var b = G("V", all.Where(e => e.Contains('b')).ToArray());
        var l = G("V", all.Where(e => e.Contains('l')).ToArray());
        var r = G("V", all.Where(e => e.Contains('r')).ToArray());
        var result = Untyped.Merge3(Input(b, l, r));

        // 000 absent; 001 peer added; 010 added here; 011 both added; 100 deleted on both; 101 deleted here (stands);
        // 110 deleted by the peer (removed here); 111 kept.
        CollectionAssert.AreEqual(new[] { ".blr", ".l", ".lr", ".r" }, Merged(result).Extensions.ToArray());
        CollectionAssert.AreEqual(new[] { ".r" }, result.AddedChildIds.ToArray());
        CollectionAssert.AreEqual(new[] { ".bl" }, result.RemovedChildIds.ToArray());
        Assert.AreEqual(0, result.HeldChildIds.Count);
        Assert.AreEqual(0, result.MassDeletionCandidates.Count);
        Assert.IsFalse(result.TypeChanged);
        Assert.IsTrue(result.Fields.All(f => f.Resolution != DataSyncFieldResolution.Conflict), "sets never conflict");

        var byPath = result.Fields.ToDictionary(f => f.Path);
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, byPath["ext:.r"].Resolution);
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, byPath["ext:.bl"].Resolution);
        Assert.AreEqual(DataSyncFieldResolution.KeptLocal, byPath["ext:.l"].Resolution);
        Assert.AreEqual(DataSyncFieldResolution.KeptLocal, byPath["ext:.br"].Resolution);
        Assert.IsFalse(byPath.ContainsKey("ext:.lr"));
        Assert.IsFalse(byPath.ContainsKey("ext:.blr"));
    }

    [TestMethod]
    public void FastForwardTakesTheRemoteSetAndKeepsLocalOnlyExtensions()
    {
        var overlay = new DataSyncOverlay([".mine"], []);
        var result = Untyped.Merge3(Input(null, G("V", ".a", ".b", ".mine"), G("W", ".b", ".c"),
            DataSyncMerge3Mode.FastForward, overlay));
        Assert.AreEqual(G("W", ".b", ".c", ".mine"), Merged(result));
        CollectionAssert.AreEqual(new[] { ".a" }, result.RemovedChildIds.ToArray());
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, result.Fields.Single(f => f.Path == "name").Resolution);
    }

    [TestMethod]
    public void AnExtensionAReaderWouldDropIsNeverRemovedByAMerge()
    {
        // The service stores any non-blank string with a dot; one a reader would drop (a space, a comma, too long) is
        // never published, so the peer's set says nothing about it — whatever the mode.
        const string tooLong = ".abcdefghijklmnopqrstuvwxyz0123456789";
        var local = ExtensionGroupContentV1.FromLocal("Odd", ["a b", "mkv", "x,y", tooLong]);
        Assert.IsFalse(ExtensionGroupCodec.IsValidExtension(tooLong, DataSyncLimits.Default));

        var fastForward = Untyped.Merge3(Input(G("Odd", ".mkv"), local, G("Odd", ".mkv", ".mp4"),
            DataSyncMerge3Mode.FastForward));
        CollectionAssert.AreEquivalent(new[] { ".a b", ".mkv", ".mp4", ".x,y", tooLong },
            Merged(fastForward).Extensions.ToArray());
        Assert.AreEqual(0, fastForward.RemovedChildIds.Count);
        CollectionAssert.AreEqual(new[] { ".mp4" }, fastForward.AddedChildIds.ToArray());

        var threeWay = Untyped.Merge3(Input(G("Odd", ".mkv"), local, G("Odd")));
        CollectionAssert.AreEquivalent(new[] { ".a b", ".x,y", tooLong }, Merged(threeWay).Extensions.ToArray(),
            "the peer removed .mkv; the rest were never its");

        var noBase = Untyped.Merge3(Input(null, local, G("Odd"), DataSyncMerge3Mode.NoBase));
        Assert.AreEqual(0, noBase.RemovedChildIds.Count);

        // What this device publishes afterwards is the peer's form: closure holds.
        Assert.AreEqual(PublishedForm(G("Odd", ".mkv", ".mp4")), PublishedForm(fastForward.Merged));
    }

    [TestMethod]
    public void NoBaseUnitesAndNeverRemoves()
    {
        var result = Untyped.Merge3(Input(null, G("V", ".a", ".b"), G("V", ".b", ".c"), DataSyncMerge3Mode.NoBase));
        Assert.AreEqual(G("V", ".a", ".b", ".c"), Merged(result));
        Assert.AreEqual(0, result.RemovedChildIds.Count);
    }

    [TestMethod]
    public void OverlayExtensionsAreAlwaysKept()
    {
        // The peer deleted .held and .mine since the base; both are overlay children here.
        var overlay = new DataSyncOverlay([".mine"], [new DataSyncHeldChild(".held", 1)]);
        var result = Untyped.Merge3(Input(G("V", ".a", ".held", ".mine"), G("V", ".a", ".held", ".mine"), G("V", ".a"),
            overlay: overlay));
        Assert.AreEqual(G("V", ".a", ".held", ".mine"), Merged(result));
        Assert.AreEqual(0, result.RemovedChildIds.Count);
        Assert.AreEqual(0, result.ReleasedChildIds.Count);
        Assert.IsFalse(result.Fields.Any(f => f.Path is "ext:.held" or "ext:.mine"));
    }

    [TestMethod]
    public void AHoldIsReleasedOnlyWhenThePeerReAddedItSinceTheBase()
    {
        var overlay = new DataSyncOverlay([".mine"], [new DataSyncHeldChild(".held", 1)]);
        var local = G("V", ".held", ".mine");

        var readded = Untyped.Merge3(Input(G("V"), local, G("V", ".held", ".mine"), overlay: overlay));
        CollectionAssert.AreEqual(new[] { ".held" }, readded.ReleasedChildIds.ToArray(),
            "a local-only extension is never released");

        var stillThere = Untyped.Merge3(Input(G("V", ".held"), local, G("V", ".held"), overlay: overlay));
        Assert.AreEqual(0, stillThere.ReleasedChildIds.Count, "a peer that merely still has it releases nothing");

        var noBase = Untyped.Merge3(Input(null, local, G("V", ".held"), DataSyncMerge3Mode.NoBase, overlay));
        Assert.AreEqual(0, noBase.ReleasedChildIds.Count);
    }

    [TestMethod]
    public void NameMergesLikeAScalar()
    {
        var b = G("Base");
        Assert.AreEqual("Remote", Merged(Untyped.Merge3(Input(b, G("Base"), G("Remote")))).Name);
        Assert.AreEqual("Local", Merged(Untyped.Merge3(Input(b, G("Local"), G("Base")))).Name);
        Assert.AreEqual(0, Untyped.Merge3(Input(b, G("Same"), G("Same"))).Fields.Count);

        var conflict = Untyped.Merge3(Input(b, G("Local"), G("Remote")));
        Assert.AreEqual("Local", Merged(conflict).Name, "a conflict keeps the local value");
        Assert.AreEqual(DataSyncFieldResolution.Conflict, conflict.Fields.Single().Resolution);

        var follow = Untyped.Merge3(Input(b, G("Local"), G("Remote"), mode: DataSyncLinkMode.Follow));
        Assert.AreEqual("Remote", Merged(follow).Name);
        Assert.AreEqual(DataSyncFieldResolution.FollowTookRemote, follow.Fields.Single().Resolution);

        var hub = Untyped.Merge3(Input(b, G("Local"), G("Remote"), mode: DataSyncLinkMode.Follow, lastEditorIsSelf: false));
        Assert.AreEqual(DataSyncFieldResolution.Conflict, hub.Fields.Single().Resolution,
            "following must not override another device's value");

        Assert.AreEqual(DataSyncFieldResolution.Conflict,
            Untyped.Merge3(Input(null, G("Local"), G("Remote"), DataSyncMerge3Mode.NoBase)).Fields.Single().Resolution);
        Assert.AreEqual("Remote",
            Merged(Untyped.Merge3(Input(null, G("Local"), G("Remote"), DataSyncMerge3Mode.FastForward))).Name);
    }

    [TestMethod]
    public void AThreeWayMergeNeedsABase() =>
        Assert.ThrowsException<ArgumentException>(() => Untyped.Merge3(Input(null, G("V"), G("V"))));

    [TestMethod]
    public void FastForwardClosure()
    {
        // Local groups also hold extensions a reader would drop: kept by the merge, and left out of what this device
        // publishes, so the published form still reaches the peer's.
        var random = new Random(3);
        for (var run = 0; run < 2_000; run++)
        {
            var l = WithUnpublishable(RandomGroup(random), random);
            var r = RandomGroup(random);
            var merged = Untyped.Merge3(Input(RandomGroup(random), l, r, DataSyncMerge3Mode.FastForward)).Merged;
            Assert.AreEqual(Form(r), PublishedForm(merged), $"L={l} R={r}");
        }
    }

    [TestMethod]
    public void SymmetricMerge()
    {
        var random = new Random(4);
        var compared = 0;
        for (var run = 0; run < 2_000; run++)
        {
            var b = RandomGroup(random);
            var l = random.Next(3) == 0 ? b : Edit(b, random);
            var r = random.Next(3) == 0 ? b : Edit(b, random);
            var forward = Untyped.Merge3(Input(b, l, r));
            var backward = Untyped.Merge3(Input(b, r, l));
            if (forward.Fields.Concat(backward.Fields).Any(f => f.Resolution == DataSyncFieldResolution.Conflict)) continue;
            compared++;
            Assert.AreEqual(Form(forward.Merged), Form(backward.Merged), $"B={b} L={l} R={r}");
        }

        Assert.IsTrue(compared > 1_000, compared.ToString());
    }

    private static string Form(object content) => CanonicalJson.Serialize(Untyped.ComparisonForm(content, null, false));

    /// <summary>The comparison form of what this device publishes for local content (§3.5).</summary>
    private static string PublishedForm(object content) =>
        Form(Untyped.Publish(content, DataSyncOverlay.None, false).Content!);

    private static readonly string[] Unpublishable = ["a b", "x,y", "a:b", ".abcdefghijklmnopqrstuvwxyz0123456789"];

    private static ExtensionGroupContentV1 WithUnpublishable(ExtensionGroupContentV1 group, Random random) =>
        ExtensionGroupContentV1.FromLocal(group.Name,
            group.Extensions.Concat(Unpublishable.Where(_ => random.Next(3) == 0)));

    private static readonly string[] Pool = [".a", ".b", ".c", ".d", ".e", ".f", ".g"];

    private static ExtensionGroupContentV1 RandomGroup(Random random) =>
        G(random.Next(3) == 0 ? "Other" : "Video", Pool.Where(_ => random.Next(2) == 0).ToArray());

    private static ExtensionGroupContentV1 Edit(ExtensionGroupContentV1 group, Random random)
    {
        var set = group.Extensions.ToHashSet();
        for (var i = random.Next(1, 3); i > 0; i--)
        {
            var e = Pool[random.Next(Pool.Length)];
            if (!set.Remove(e)) set.Add(e);
        }

        return G(random.Next(4) == 0 ? group.Name + "!" : group.Name, set.ToArray());
    }
}
