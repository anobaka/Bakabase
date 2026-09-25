using System.Globalization;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Tests.DataSync.Apply;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync.Golden;

/// <summary>
/// §13.4 <c>WireGoldenTests</c>: pages this build's wire writer wrote once, checked in under
/// <c>Fixtures/DataSync/Wire</c>, for every record shape — live (with aliases), tombstone, held at the source,
/// <c>childrenLocal</c>, chunked — read back through the real reader and assembler to exactly the records written, and
/// written again byte for byte. A wire change that stops this build reading an earlier build's pages, or writes other
/// bytes for the same records, fails here: bump the contract, or confirm the change and write the goldens again with
/// <c>DATASYNC_WRITE_GOLDENS=&lt;dir&gt;</c> (<see cref="WriteGoldens"/>).
/// </summary>
[TestClass]
public class WireGoldenTests
{
    private const string Snapshot = "golden-wire";
    private const string Origin = "peer-node";
    private static readonly DataSyncLimits Limits = DataSyncLimits.Default;

    /// <summary>Small chunks, so one entity arrives as several (§7.5.4).</summary>
    private static readonly DataSyncLimits ChunkLimits = DataSyncLimits.Default with { MaxChunkBytes = 256 };

    private static readonly DataSyncActorId Peer = new("0000000000000bee");
    private static readonly DataSyncEditorRef Editor = new(Origin, "PC-1", Peer.Value);
    private static readonly IDataSyncKindCodec Items = TestItemCodec.Instance;
    private static readonly IDataSyncKindCodec Groups = ExtensionGroupCodec.Instance;

    private static string PathOf(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "DataSync", "Wire", name);

    private static string K(int n) => n.ToString("x32", CultureInfo.InvariantCulture);

    private static DataSyncVersionVector Vv(long counter) => DataSyncVersionVector.Empty.With(Peer, counter);

    private static DataSyncWireRecord Live(IReadOnlyList<string> keys, long seq, JsonObject content, string? orderKey,
        int schemaVersion = 1) =>
        new(keys, Origin, seq, Vv(seq), Editor, false, schemaVersion, orderKey, content, ContentHash.Of(content), null, 0);

    private static TestItemContent T(string name, int children, string label = "Label") =>
        new(name, null, Enumerable.Range(1, children).Select(i =>
            new TestChild("c" + i.ToString(CultureInfo.InvariantCulture), label + " " + i.ToString(CultureInfo.InvariantCulture))));

    /// <summary>A published record's content as a source builds it (§3.5): the shared publication helper.</summary>
    private static JsonObject Published(IDataSyncKindCodec codec, object content, bool childrenLocal = false) =>
        DataSyncPublication.Of(codec, content, DataSyncOverlay.None, childrenLocal, null, null).Content!;

    // ---- the records the goldens hold ------------------------------------------------------------------

    private static IReadOnlyList<DataSyncWireRecord> ItemShapes() =>
    [
        Live([K(1), K(11)], 1, Published(Items, T("Genre", 2)), "a0"),
        new([K(2)], Origin, 2, Vv(2), Editor, true, 1, null, null, null, null, 0),
        new([K(3)], Origin, 3, Vv(3), Editor, false, 1, "a1", null, null, DataSyncHeldReason.PendingDecision, 0),
        Live([K(4)], 4, Published(Items, T("Tags", 3), childrenLocal: true), "a2"),
    ];

    private static IReadOnlyList<DataSyncWireRecord> ItemChunked() =>
        [Live([K(5)], 5, Published(Items, T("Big", 40, "A rather long label, so the children need several chunks")), "a3")];

    private static IReadOnlyList<DataSyncWireRecord> GroupShapes() =>
    [
        Live([K(21)], 21, Published(Groups, new ExtensionGroupContentV1("Video", [".avi", ".mkv"])), null),
        new([K(22)], Origin, 22, Vv(22), Editor, true, 1, null, null, null, null, 0),
    ];

    private static readonly (string File, string Kind, Func<IReadOnlyList<DataSyncWireRecord>> Records, DataSyncLimits Limits)[]
        Goldens =
        [
            ("shapes.testItem.page.json", TestItemCodec.Kind, ItemShapes, Limits),
            ("chunked.testItem.page.json", TestItemCodec.Kind, ItemChunked, ChunkLimits),
            ("shapes.extensionGroup.page.json", DataSyncKindIds.ExtensionGroup, GroupShapes, Limits),
        ];

    // ---- reading ---------------------------------------------------------------------------------------

    /// <summary>A record as canonical JSON, so two records compare by what they say, whatever order members came in.</summary>
    private static string Canonical(DataSyncWireRecord record) => CanonicalJson.Serialize(DataSyncWireFormat.ToJson(record));

    private static (DataSyncPageReadResult Page, DataSyncStagedKind Staged) Read(string file, IDataSyncKindCodec codec)
    {
        var page = DataSyncWireReader.ReadPage(File.ReadAllBytes(PathOf(file)), Snapshot, codec.Descriptor.Kind, Limits);
        Assert.IsNull(page.Problem, $"{file} reads");
        var assembler = new DataSyncRecordAssembler(codec, Limits);
        assembler.Add(page);
        var staged = assembler.Complete(page.Records.Max(r => r.Seq), false);
        Assert.IsNull(assembler.Problem, $"{file} assembles");
        return (page, staged);
    }

    [TestMethod]
    public void Every_golden_page_is_what_this_build_writes_for_its_records()
    {
        foreach (var (file, kind, records, limits) in Goldens)
        {
            var pages = DataSyncWireWriter.WritePages(Snapshot, kind, 0, records(), limits);
            Assert.AreEqual(1, pages.Count, file);
            CollectionAssert.AreEqual(File.ReadAllBytes(PathOf(file)), pages[0],
                $"{file}: this build writes other bytes for the same records");
        }
    }

    [TestMethod]
    public void Live_tombstone_held_and_childrenLocal_records_parse_to_the_records_written()
    {
        var expected = ItemShapes();
        var (page, staged) = Read("shapes.testItem.page.json", Items);

        CollectionAssert.AreEqual(expected.Select(Canonical).ToList(), page.Records.Select(Canonical).ToList());
        var entities = staged.Entities.ToDictionary(e => e.Record.Keys[0]);

        var live = entities[K(1)];
        Assert.AreEqual((null, (DataSyncHeldReason?) null), (live.Unknown?.ToJsonString(), live.Held));
        CollectionAssert.AreEqual(new[] { K(1), K(11) }, live.Record.Keys.ToArray(), "primary first, then its alias");
        Assert.AreEqual(T("Genre", 2), live.Content);
        Assert.AreEqual((Vv(1), "a0"), (live.Record.Vv, live.Record.OrderKey));
        Assert.AreEqual(Editor, live.Record.EditedBy);

        var tombstone = entities[K(2)];
        Assert.AreEqual((true, (object?) null, (DataSyncHeldReason?) null),
            (tombstone.Record.Deleted, tombstone.Content, tombstone.Held));

        var held = entities[K(3)];
        Assert.AreEqual((DataSyncHeldReason?) DataSyncHeldReason.PendingDecision, held.Record.HeldAtSource);
        Assert.AreEqual((DataSyncHeldReason?) DataSyncHeldReason.AtSource, held.Held, "the reader holds it too");
        Assert.IsNull(held.Content);

        var local = entities[K(4)];
        Assert.IsNull(local.Held);
        Assert.IsTrue(DataSyncRecordValidation.ChildrenLocalOf(local.Record.Content), "\"childrenLocal\": true (§3.6)");
        Assert.AreEqual(0, Items.ChildrenOf(local.Content!).Count, "no children travel");
        Assert.AreEqual("Tags", Items.NameOf(local.Content!));
    }

    [TestMethod]
    public void A_chunked_record_is_reassembled_whole_and_its_hash_holds()
    {
        var expected = ItemChunked().Single();
        var (page, staged) = Read("chunked.testItem.page.json", Items);

        var record = page.Records.Single();
        Assert.IsTrue(record.Chunks > 1, "it travels in several chunks");
        Assert.AreEqual(record.Chunks, page.Chunks.Count);
        Assert.IsNull(record.Content!["children"], "its children arrive in the chunks");
        var entity = staged.Entities.Single();
        Assert.IsNull(entity.Held, "the reassembled content matches its hash");
        Assert.AreEqual(0, entity.Record.Chunks);
        Assert.IsTrue(JsonNode.DeepEquals(expected.Content, entity.Record.Content));
        Assert.AreEqual(40, Items.ChildrenOf(entity.Content!).Count);
    }

    [TestMethod]
    public void An_extension_group_page_parses_to_the_records_written()
    {
        var expected = GroupShapes();
        var (page, staged) = Read("shapes.extensionGroup.page.json", Groups);

        CollectionAssert.AreEqual(expected.Select(Canonical).ToList(), page.Records.Select(Canonical).ToList());
        var video = staged.Entities.Single(e => e.Record.Keys[0] == K(21));
        Assert.AreEqual(new ExtensionGroupContentV1("Video", [".avi", ".mkv"]), video.Content);
        Assert.IsTrue(staged.Entities.Single(e => e.Record.Keys[0] == K(22)).Record.Deleted);
    }

    // ---- writing the goldens ---------------------------------------------------------------------------

    [TestMethod]
    public void WriteGoldensWhenAsked()
    {
        if (Environment.GetEnvironmentVariable("DATASYNC_WRITE_GOLDENS") is not { Length: > 0 } dir)
        {
            foreach (var (file, _, _, _) in Goldens)
                Assert.IsTrue(File.Exists(PathOf(file)), $"{file} is copied next to the tests");
            return;
        }

        WriteGoldens(Path.Combine(dir, "Wire"));
    }

    /// <summary>Writes every golden page into <paramref name="dir"/> with this build's writer.</summary>
    public static void WriteGoldens(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var (file, kind, records, limits) in Goldens)
            File.WriteAllBytes(Path.Combine(dir, file), DataSyncWireWriter.WritePages(Snapshot, kind, 0, records(), limits).Single());
    }
}
