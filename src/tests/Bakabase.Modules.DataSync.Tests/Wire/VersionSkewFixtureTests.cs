using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Tests.Merging;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Merging.MergeFixture;

namespace Bakabase.Modules.DataSync.Tests.Wire;

/// <summary>
/// §13.8, the pure part: pages and heads another build wrote, checked in under <c>Fixtures/VersionSkew</c> and read
/// through the real reader, assembler and merger. A wire change that stops this build reading them fails here.
/// <c>DATASYNC_WRITE_FIXTURES=&lt;dir&gt;</c> writes them again (<see cref="WriteFixtures"/>); the unknown
/// <c>PropertyType</c> and <c>state.json</c> fixtures belong to the custom property codec and the federation state.
/// </summary>
[TestClass]
public class VersionSkewFixtureTests
{
    private static readonly SyncKey A = K(0xa), B = K(0xb);
    private static readonly DataSyncLimits Limits = DataSyncLimits.Default;

    private const string NewerSnapshot = "snap-newer";
    private const string UnknownSnapshot = "snap-unknown";
    private const string FormSnapshot = "snap-form";

    // ---- reading ---------------------------------------------------------------------------------

    private static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "VersionSkew", name);

    private static JsonObject Json(string name) => JsonNode.Parse(File.ReadAllText(PathOf(name)))!.AsObject();

    private static DataSyncFeedHead Head(JsonNode node) =>
        node.Deserialize<DataSyncFeedHead>(DataSyncJson.Options) ?? throw new InvalidDataException("no head");

    /// <summary>A checked-in page, read and staged exactly as a pull stages it.</summary>
    private static (DataSyncStagedKind Staged, IReadOnlyList<DataSyncWireRecord> Records) Stage(string file,
        string snapshotId, IDataSyncKindCodec codec)
    {
        var page = DataSyncWireReader.ReadPage(File.ReadAllBytes(PathOf(file)), snapshotId, codec.Descriptor.Kind, Limits);
        Assert.IsNull(page.Problem, $"{file} reads");
        var assembler = new DataSyncRecordAssembler(codec, Limits);
        assembler.Add(page);
        var staged = assembler.Complete(page.Records.Max(r => r.Seq), false);
        Assert.IsNull(assembler.Problem, $"{file} assembles");
        return (staged, page.Records);
    }

    private static DataSyncMergeResult Merge(MergeFixture f, DataSyncStagedKind staged, IDataSyncKindCodec? codec = null)
    {
        f.NoPull = true;
        var manifestKind = new DataSyncFeedKind(staged.Kind, staged.SchemaVersion, staged.MaxSeq, 0, 1_000, 0,
            DataSyncWireFormat.KindContentHash(staged.Entities.Select(e => e.Record)), 0, staged.Entities.Count, false);
        var manifest = new DataSyncFeedManifest("snap", 120_000, PeerNode, "epoch", Peer.Value, DataSyncContract.Version,
            DataSyncContract.MinimumPeerVersion, "2.5.0", [manifestKind], null, new DataSyncSourceAttention(false, 0, 0, false, 0));
        var input = f.Input() with
        {
            Incoming = new DataSyncStagedPull(PeerNode, "PC-1", manifest, [staged], new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc)),
        };
        if (codec is not null)
        {
            input = input with
            {
                Codecs = new Dictionary<string, IDataSyncKindCodec>(input.Codecs) { [codec.Descriptor.Kind] = codec },
            };
        }

        return DataSyncMerger.Merge(input);
    }

    // ---- a newer schema ----------------------------------------------------------------------------

    [TestMethod]
    public void ANewerSchemaIsHeldTheCursorAdvancesAndTheUpgradeMergesIt()
    {
        var head = Head(Json("newer-schema.head.json"));
        Assert.IsNull(DataSyncVersionSkew.CheckContract(head.ContractVersion, head.MinimumPeerContract));
        Assert.AreEqual(2, head.Kinds.Single().SchemaVersion);

        var (staged, _) = Stage("newer-schema.testItem.page.json", NewerSnapshot, Items);
        Assert.AreEqual(DataSyncHeldReason.NewerSchema, staged.Entities.Single(e => e.Record.Keys[0] == A.Value).Held);
        Assert.IsNull(staged.Entities.Single(e => e.Record.Keys[0] == B.Value).Held, "a record of this schema is read");

        var f = new MergeFixture();
        f.Local("1", A, T("Genre", ("1", "Action")), Vv((Self, 1)));
        f.Base(A, f.Record(A, T("Genre", ("1", "Action")), Vv((Self, 1))));
        var r = Merge(f, staged);
        var held = r.BaseUpdates.Single(u => u.Key == A).Pending!;
        Assert.AreEqual(DataSyncPendingReason.Held, held.Reason);
        Assert.AreEqual(staged.MaxSeq, r.CursorAdvance[ItemKind], "the cursor still advances");
        var ops = r.Batches.SelectMany(b => b.Operations).ToList();
        Assert.IsInstanceOfType<CreateEntityOperation>(ops.Single(), "only the record of this schema applies");

        // The upgrade: KindSchemaVersionsJson changes, the Held record is re-merged (§8.4 condition 6).
        var upgraded = new TestItemCodec(2);
        var changed = DataSyncVersionSkew.ChangedKinds(new Dictionary<string, int> { [ItemKind] = 1, [GroupKind] = 1 },
            DataSyncVersionSkew.SchemaVersions([upgraded, Groups]));
        CollectionAssert.AreEqual(new[] { ItemKind }, changed.ToArray());
        Assert.IsTrue(DataSyncPendingRecords.ShouldRemerge(held, 10, false, DataSyncMergeFlags.None, true));

        var g = new MergeFixture { NoPull = true };
        g.Local("1", A, T("Genre", ("1", "Action")), Vv((Self, 1)));
        g.Base(A, f.Record(A, T("Genre", ("1", "Action")), Vv((Self, 1))), pending: held);
        g.PendingToMerge.Add((ItemKind, A));
        var input = g.Input();
        var after = DataSyncMerger.Merge(input with
        {
            Codecs = new Dictionary<string, IDataSyncKindCodec>(input.Codecs) { [ItemKind] = upgraded },
        });
        var update = (UpdateEntityOperation)after.Batches.SelectMany(b => b.Operations).Single();
        Assert.AreEqual("Genre 2", ((TestItemContent)upgraded.ReadLocal(update.MergedContent)).Name);
        var revision = after.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.FastForward, revision.Revision);
        Assert.AreEqual(5L, (long)revision.Unknown!["rating"]!, "a member even the upgrade does not know is kept");
    }

    // ---- an unknown top-level member ---------------------------------------------------------------

    [TestMethod]
    public void AnUnknownMemberIsPreservedMergedAndRepublished()
    {
        var (staged, records) = Stage("unknown-member.extensionGroup.page.json", UnknownSnapshot, Groups);
        var hop1 = records.Single();
        Assert.AreEqual("film", (string)staged.Entities.Single().Unknown!["icon"]!);

        // An older build (this one) takes the newer build's record: the member is part of the content it keeps.
        var video = new ExtensionGroupContentV1("Video", [".mkv"]);
        var f = new MergeFixture();
        f.Local("5", A, video, Vv((Third, 1)), kind: GroupKind);
        f.Base(A, f.Record(A, video, Vv((Third, 1)), kind: GroupKind, editedBy: ThirdEditor), kind: GroupKind);
        var revision = Merge(f, staged).Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.FastForward, revision.Revision);
        Assert.IsTrue(revision.ResultEqualsRemote);
        Assert.AreEqual("film", (string)revision.Unknown!["icon"]!);

        // After a local edit, this build publishes it back.
        var published = DataSyncPublication.Of(Groups, new ExtensionGroupContentV1("Video", [".mkv", ".mp4"]),
            DataSyncOverlay.None, false, null, revision.Unknown);
        Assert.AreEqual("film", (string)published.Content!["icon"]!);
        Assert.AreEqual(hop1.Hash, DataSyncPublication.Of(Groups, video, DataSyncOverlay.None, false, null, revision.Unknown).Hash,
            "unchanged, it republishes the newer build's record byte for byte");
    }

    [TestMethod]
    public void ASecondOlderHopNeverRegressesTheNewerBuildsValue()
    {
        var (_, first) = Stage("unknown-member.extensionGroup.page.json", UnknownSnapshot, Groups);
        var (staged, _) = Stage("unknown-member.extensionGroup.hop2.page.json", UnknownSnapshot, Groups);

        // The newer build changed the icon to "clapper" since; an older device renamed the group meanwhile and
        // republished the icon it kept ("film").
        var f = new MergeFixture { PeerActorId = Third.Value };
        f.Local("5", A, new ExtensionGroupContentV1("Video", [".mkv"]), Vv((Third, 1), (Peer, 3)), kind: GroupKind,
            unknown: new JsonObject { ["icon"] = "clapper" });
        f.Base(A, first.Single(), kind: GroupKind);
        var r = Merge(f, staged);
        var update = (UpdateEntityOperation)r.Batches.SelectMany(b => b.Operations).Single();
        Assert.AreEqual("Videos", ((ExtensionGroupContentV1)Groups.ReadLocal(update.MergedContent)).Name);
        var revision = r.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.MergedNoConflict, revision.Revision);
        Assert.AreEqual("clapper", (string)revision.Unknown!["icon"]!, "merged per member: the older hop changed nothing");
        Assert.AreEqual(0, r.Inbox.Count);
    }

    // ---- the contract ------------------------------------------------------------------------------

    [TestMethod]
    public void TheContractInAHeadDecidesWhoIsTooOld()
    {
        var heads = Json("contract.heads.json");
        var current = Head(heads["current"]!);
        Assert.IsNull(DataSyncVersionSkew.CheckContract(current.ContractVersion, current.MinimumPeerContract),
            "a member this build does not know is ignored");

        var older = Head(heads["olderThanMinimum"]!);
        Assert.AreEqual(DataSyncLinkState.PeerTooOld, DataSyncVersionSkew.CheckContract(older.ContractVersion, older.MinimumPeerContract));

        var none = heads["noContract"]!.AsObject();
        Assert.IsFalse(none.ContainsKey("contractVersion"));
        Assert.AreEqual(DataSyncLinkState.PeerTooOld, DataSyncVersionSkew.CheckContract((int?)none["contractVersion"],
            (int?)none["minimumPeerContract"]));

        var newer = Head(heads["peerMinimumAboveThis"]!);
        Assert.AreEqual(DataSyncLinkState.ThisTooOld, DataSyncVersionSkew.CheckContract(newer.ContractVersion, newer.MinimumPeerContract));
    }

    // ---- a comparison form version bump ------------------------------------------------------------

    [TestMethod]
    public void APeersOtherComparisonFormIsDriftNeverADuplicateActor()
    {
        var head = Head(Json("comparison-form.head.json"));
        var (staged, records) = Stage("comparison-form.testItem.page.json", FormSnapshot, Items);
        var vv = records.Single().Vv;

        MergeFixture Local(int? peerFormVersion)
        {
            var f = new MergeFixture { PeerComparisonFormVersion = peerFormVersion };
            f.Local("1", A, T("Genre", ("1", "Action")), vv);
            return f;
        }

        var peerVersion = head.Kinds.Single(k => k.Kind == ItemKind).ComparisonFormVersion;
        Assert.IsTrue(DataSyncVersionSkew.IsComparisonFormMismatch(peerVersion, Items));
        var drift = Merge(Local(peerVersion), staged);
        Assert.IsNull(drift.Pause);
        Assert.IsNull(drift.Anomaly);
        Assert.AreEqual(0, drift.Revisions.Count, "no revision");
        Assert.AreEqual(records.Single(), drift.BaseUpdates.Single().Record, "base := R");
        Assert.AreEqual(DataSyncMergeNoteCodes.NormalizationChanged, drift.Notes.Single().Code);

        // The same page from a peer on this build's form version is a duplicated actor.
        Assert.AreEqual(DataSyncPauseReason.PeerIdentityDuplicated, Merge(Local(Items.ComparisonFormVersion), staged).Pause);
    }

    [TestMethod]
    public void ThisBuildsComparisonFormBumpRecomputesHashesWithoutRevisions()
    {
        var bumped = new BumpedFormCodec(Items);
        var changed = DataSyncVersionSkew.ChangedKinds(new Dictionary<string, int> { [ItemKind] = 1 },
            DataSyncVersionSkew.ComparisonFormVersions([bumped]));
        CollectionAssert.AreEqual(new[] { ItemKind }, changed.ToArray());

        foreach (var node in Json("comparison-form.rows.json")["rows"]!.AsArray())
        {
            var content = node!["content"]!.AsObject();
            var row = new DataSyncRefreshRow(DataSyncEntitySyncState.Synced, (string)node["sharedHash"]!,
                (string?)node["orderKey"], DataSyncOverlay.None, false, false, null);
            Assert.AreEqual(DataSyncRefreshAction.HashesOnly, DataSyncRefreshRules.Evaluate(Items, row, content, false, null).Action,
                "the stored hash is this form's");

            // Refresh recomputes every SharedHash of a changed kind first (no revision, no Seq, §3.4, §6.1)…
            var recomputed = DataSyncPublication.Of(bumped, bumped.ReadLocal(content), row.Overlay, row.ChildrenLocal,
                row.OrderKey, row.Unknown).SharedHash;
            Assert.AreNotEqual(row.SharedHash, recomputed);
            Assert.AreEqual(DataSyncRefreshAction.LocalEdit, DataSyncRefreshRules.Evaluate(bumped, row, content, false, null).Action,
                "comparing against the old form's hash would read as an edit");
            // …so the comparison that follows finds nothing to revise.
            Assert.AreEqual(DataSyncRefreshAction.HashesOnly,
                DataSyncRefreshRules.Evaluate(bumped, row with { SharedHash = recomputed }, content, false, null).Action);
        }
    }

    /// <summary>This build's test kind with its comparison form changed (version 2): every form carries a marker.</summary>
    private sealed class BumpedFormCodec(IDataSyncKindCodec inner) : IDataSyncKindCodec
    {
        public DataSyncKindDescriptor Descriptor => inner.Descriptor;
        public int ComparisonFormVersion => inner.ComparisonFormVersion + 1;

        public JsonObject ComparisonForm(object publishedContent, string? orderKey, bool childrenLocal)
        {
            var form = inner.ComparisonForm(publishedContent, orderKey, childrenLocal);
            form["formVersion"] = ComparisonFormVersion;
            return form;
        }

        public JsonObject Upgrade(JsonObject content, int fromSchemaVersion) => inner.Upgrade(content, fromSchemaVersion);
        public CodecReadResult Read(JsonObject content, DataSyncLimits limits) => inner.Read(content, limits);
        public object ReadLocal(JsonObject content) => inner.ReadLocal(content);
        public JsonObject Write(object content) => inner.Write(content);
        public string NameOf(object content) => inner.NameOf(content);
        public string? SubtypeOf(object content) => inner.SubtypeOf(content);
        public int ChildCountOf(object content) => inner.ChildCountOf(content);
        public DataSyncNaturalMatch MatchNatural(object incoming, object local) => inner.MatchNatural(incoming, local);
        public EntityDiff Diff(object local, object incoming) => inner.Diff(local, incoming);
        public MergeResult Merge(object local, object incoming, IReadOnlySet<string> acceptedChangeIds) =>
            inner.Merge(local, incoming, acceptedChangeIds);
        public MergeResult PrepareCreate(object incoming, string? nameOverride) => inner.PrepareCreate(incoming, nameOverride);
        public DataSyncPublishable Publish(object localContent, DataSyncOverlay overlay, bool childrenLocal) =>
            inner.Publish(localContent, overlay, childrenLocal);
        public IReadOnlyList<string> ChildDeletionCandidates(DataSyncChildCandidatesInput input) =>
            inner.ChildDeletionCandidates(input);
        public DataSyncMerge3Result Merge3(DataSyncMerge3Input input) => inner.Merge3(input);
        public IReadOnlyList<DataSyncChildInfo> ChildrenOf(object content) => inner.ChildrenOf(content);
    }

    // ---- writing the fixtures ----------------------------------------------------------------------

    [TestMethod]
    public void WriteFixturesWhenAsked()
    {
        if (Environment.GetEnvironmentVariable("DATASYNC_WRITE_FIXTURES") is not { Length: > 0 } dir)
        {
            Assert.IsTrue(File.Exists(PathOf("newer-schema.testItem.page.json")), "the fixtures are copied next to the tests");
            return;
        }

        WriteFixtures(dir);
    }

    /// <summary>
    /// Writes every fixture into <paramref name="dir"/> as a build other than this one would: records of schema
    /// version 2, content with members this build's codecs do not know, heads naming other contracts and comparison
    /// form versions.
    /// </summary>
    public static void WriteFixtures(string dir)
    {
        Directory.CreateDirectory(dir);

        JsonObject With(object content, IDataSyncKindCodec codec, params (string Name, JsonNode Value)[] members)
        {
            var json = codec.Write(content);
            foreach (var (name, value) in members) json[name] = value;
            return json;
        }

        DataSyncWireRecord Record(SyncKey key, JsonObject content, DataSyncVersionVector vv, long seq, int schemaVersion,
            DataSyncEditorRef editedBy, string origin, string? orderKey) =>
            new([key.Value], origin, seq, vv, editedBy, false, schemaVersion, orderKey, content, ContentHash.Of(content), null, 0);

        void Page(string name, string snapshotId, string kind, params DataSyncWireRecord[] records)
        {
            var pages = DataSyncWireWriter.WritePages(snapshotId, kind, 0, records, Limits);
            File.WriteAllBytes(Path.Combine(dir, name), pages.Single());
        }

        DataSyncFeedHead HeadOf(int contract, int minimum, params DataSyncFeedKindHead[] kinds) =>
            new(PeerNode, "epoch", Peer.Value, contract, minimum, "2.5.0-beta.1", kinds.Max(k => k.MaxSeq), kinds,
                new DataSyncSourceAttention(false, 0, 0, false, 0), null, null);

        void Write(string name, JsonNode node) =>
            File.WriteAllText(Path.Combine(dir, name), node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
                new UTF8Encoding(false));

        JsonNode HeadJson(DataSyncFeedHead head) => JsonSerializer.SerializeToNode(head, DataSyncJson.Options)!;

        // A newer schema: record A was written by schema 2 (and carries a member no schema here knows), B by 1.
        Write("newer-schema.head.json", HeadJson(HeadOf(1, 1, new DataSyncFeedKindHead(ItemKind, 2, 12, false, 1))));
        Page("newer-schema.testItem.page.json", NewerSnapshot, ItemKind,
            Record(A, With(T("Genre 2", ("1", "Action")), Items, ("rating", 5)), Vv((Self, 1), (Peer, 1)), 11, 2,
                PeerEditor, SelfNode, "a0"),
            Record(B, Items.Write(T("Mood")), Vv((Peer, 2)), 12, 1, PeerEditor, PeerNode, "a1"));

        // An unknown member: a newer build added "icon"; an older device later renamed the group and kept it.
        var video = new ExtensionGroupContentV1("Video", [".mkv"]);
        Page("unknown-member.extensionGroup.page.json", UnknownSnapshot, GroupKind,
            Record(A, With(video, Groups, ("icon", "film")), Vv((Third, 1), (Peer, 2)), 21, 1, PeerEditor, ThirdNode, null));
        Page("unknown-member.extensionGroup.hop2.page.json", UnknownSnapshot, GroupKind,
            Record(A, With(new ExtensionGroupContentV1("Videos", [".mkv"]), Groups, ("icon", "film")),
                Vv((Third, 2), (Peer, 2)), 31, 1, ThirdEditor, ThirdNode, null));

        // Contracts.
        var current = HeadJson(HeadOf(1, 1, new DataSyncFeedKindHead(ItemKind, 1, 3, false, 1))).AsObject();
        current["futureMember"] = new JsonObject { ["anything"] = true };
        var noContract = HeadJson(HeadOf(1, 1, new DataSyncFeedKindHead(ItemKind, 1, 3, false, 1))).AsObject();
        noContract.Remove("contractVersion");
        noContract.Remove("minimumPeerContract");
        Write("contract.heads.json", new JsonObject
        {
            ["current"] = current,
            ["olderThanMinimum"] = HeadJson(HeadOf(0, 0, new DataSyncFeedKindHead(ItemKind, 1, 3, false, 1))),
            ["noContract"] = noContract,
            ["peerMinimumAboveThis"] = HeadJson(HeadOf(2, 2, new DataSyncFeedKindHead(ItemKind, 1, 3, false, 1))),
        });

        // A comparison form version 2 at the peer: the same vector as this device's entity, another form.
        Write("comparison-form.head.json", HeadJson(HeadOf(1, 1, new DataSyncFeedKindHead(ItemKind, 1, 41, false, 2))));
        Page("comparison-form.testItem.page.json", FormSnapshot, ItemKind,
            Record(A, Items.Write(T("Genre", "#0090ff", null, ("1", "Action"))), Vv((Self, 3), (Peer, 2)), 41, 1,
                PeerEditor, SelfNode, null));

        // Rows as this build stores them, hashed with comparison form version 1.
        var rows = new JsonArray();
        foreach (var (localKey, content, orderKey) in new (string, TestItemContent, string?)[]
                 {
                     ("1", T("Genre", "#0090ff", "Tags", ("1", "Action"), ("2", "Drama")), "a0"),
                     ("2", T("Mood"), "a1"),
                     ("3", T("Artist", null, null, ("7", "Comedy"), ("8", "Comedy")), null),
                 })
        {
            rows.Add(new JsonObject
            {
                ["localKey"] = localKey,
                ["content"] = Items.Write(content),
                ["orderKey"] = orderKey,
                ["sharedHash"] = DataSyncPublication.Of(Items, content, DataSyncOverlay.None, false, orderKey, null).SharedHash,
            });
        }

        Write("comparison-form.rows.json", new JsonObject { ["comparisonFormVersion"] = 1, ["rows"] = rows });
    }
}
