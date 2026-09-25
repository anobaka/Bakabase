using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Ordering;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Wire.WireTestData;

namespace Bakabase.Modules.DataSync.Tests.Wire;

[TestClass]
public class WireReaderTests
{
    private const string Kind = TestItemCodec.Kind;

    private static IReadOnlyList<byte[]> Write(params DataSyncWireRecord[] records) =>
        DataSyncWireWriter.WritePages(Snapshot, Kind, 0, records, Small);

    private static DataSyncPageReadResult Read(byte[] page) =>
        DataSyncWireReader.ReadPage(page, Snapshot, Kind, Small);

    /// <summary>A one-page page with the given record objects (canonical, like a source writes it).</summary>
    private static byte[] Page(params JsonNode[] items) => ToBytes(new JsonObject
    {
        ["complete"] = true, ["kind"] = Kind, ["records"] = new JsonArray(items.Select(i => (JsonNode?)i.DeepClone()).ToArray()),
        ["sinceSeq"] = 0, ["snapshotId"] = Snapshot,
    });

    private static JsonObject RecordJson(DataSyncWireRecord record) => DataSyncWireFormat.ToJson(record);

    // ---- records, chunks, reassembly ------------------------------------------------------------

    [TestMethod]
    public void RecordsRoundTripThroughPagesAndTheAssembler()
    {
        var records = new[]
        {
            Live(1, 1, Item("First", 3), orderKey: "a0", keys: [Key(1), Key(11)]),
            Tombstone(2, 4),
            HeldAtSource(3, 6, DataSyncHeldReason.PendingDecision),
            Live(4, 9, Item("Fourth", 0)),
        };
        var written = DataSyncWireWriter.WriteKind(Snapshot, Kind, 0, records, Small);
        var assembler = Assemble(written.Pages, TestItemCodec.Instance, Small);
        Assert.IsNull(assembler.Problem);
        Assert.AreEqual(written.ContentHash, assembler.ContentHash);
        var staged = assembler.Complete(new DataSyncFeedKind(Kind, 1, 9, 0, 2, 1, written.ContentHash, 0, 4, false), false);
        Assert.IsNull(assembler.Problem);
        Assert.AreEqual(4, staged.Entities.Count);

        var first = staged.Entities[0];
        Assert.AreEqual(records[0].Vv, first.Record.Vv);
        CollectionAssert.AreEqual(records[0].Keys.ToArray(), first.Record.Keys.ToArray());
        Assert.AreEqual(records[0].EditedBy, first.Record.EditedBy);
        Assert.AreEqual("a0", first.Record.OrderKey);
        Assert.AreEqual("First", first.DisplayName);
        Assert.AreEqual(TestItemCodec.Instance.ReadLocal(Item("First", 3)), first.Content);
        Assert.AreEqual(ContentHash.Of(Item("First", 3)), first.ValidatedHash);

        Assert.IsNull(staged.Entities[1].Content);
        Assert.IsNull(staged.Entities[1].Held);
        Assert.IsTrue(staged.Entities[1].Record.Deleted);
        Assert.AreEqual(DataSyncHeldReason.AtSource, staged.Entities[2].Held);
        Assert.AreEqual(DataSyncHeldReason.PendingDecision, staged.Entities[2].Record.HeldAtSource);
        Assert.AreEqual("#2", staged.Entities[2].DisplayName);
    }

    [TestMethod]
    public void ChunksAreReassembledAcrossPages()
    {
        var content = Item("Big", 120, 30);
        var pages = Write(Live(1, 1, content), Live(2, 2, Item("Small", 1)));
        Assert.IsTrue(pages.Count > 2);
        var assembler = Assemble(pages, TestItemCodec.Instance, Small);
        var staged = assembler.Complete(2, false);
        Assert.IsNull(assembler.Problem);
        var big = staged.Entities[0];
        Assert.IsNull(big.Held);
        Assert.AreEqual(0, big.Record.Chunks);
        Assert.AreEqual(CanonicalJson.Serialize(content), CanonicalJson.Serialize(big.Record.Content));
        Assert.AreEqual(120, ((TestItemContent)big.Content!).Children.Count);
    }

    [TestMethod]
    public void AHashMismatchHoldsTheEntity()
    {
        var record = RecordJson(Live(1, 1, Item("Named", 2)));
        record["content"]!["name"] = "Tampered";
        var assembler = new DataSyncRecordAssembler(TestItemCodec.Instance, Small);
        assembler.Add(Read(Page(record)));
        var entity = assembler.Complete(1, false).Entities.Single();
        Assert.IsNull(assembler.Problem);
        Assert.AreEqual(DataSyncHeldReason.Invalid, entity.Held);
        Assert.IsNull(entity.Content);
        Assert.AreEqual("Tampered", entity.DisplayName, "a readable name is still shown");
    }

    [TestMethod]
    public void AMissingChunkHoldsTheEntity()
    {
        var pages = Write(Live(1, 1, Item("Big", 120, 30)));
        var items = pages.SelectMany(p => ((JsonArray)ParsePage(p)["records"]!).Select(n => n!)).ToList();
        items.RemoveAt(2);
        var assembler = new DataSyncRecordAssembler(TestItemCodec.Instance, DataSyncLimits.Default);
        assembler.Add(DataSyncWireReader.ReadPage(Page(items.ToArray()), Snapshot, Kind, DataSyncLimits.Default));
        var entity = assembler.Complete(1, false).Entities.Single();
        Assert.IsNull(assembler.Problem);
        Assert.AreEqual(DataSyncHeldReason.Invalid, entity.Held);
        Assert.AreEqual("Big", entity.DisplayName);
    }

    [TestMethod]
    public void AChunkOfNoChunkedRecordDiscardsThePull()
    {
        var chunk = new JsonObject { ["chunkOf"] = Key(9), ["index"] = 0, ["items"] = new JsonArray(), ["path"] = "children" };
        var assembler = new DataSyncRecordAssembler(TestItemCodec.Instance, Small);
        assembler.Add(Read(Page(RecordJson(Live(1, 1, Item("A", 1))), chunk)));
        assembler.Complete(1, false);
        Assert.AreEqual(DataSyncWireReader.Corrupted, assembler.Problem);
    }

    [TestMethod]
    public void DuplicateKeysDiscardThePull()
    {
        // Across two records of one kind, even on different pages.
        var first = MarkIncomplete(Page(RecordJson(Live(1, 1, Item("A", 1)))));
        var second = Page(RecordJson(Live(2, 2, Item("B", 1), keys: [Key(2), Key(1)])));
        var assembler = Assemble([first, second], TestItemCodec.Instance, Small);
        Assert.AreEqual(DataSyncWireReader.Corrupted, assembler.Problem);
        Assert.AreEqual(0, assembler.Complete(2, false).Entities.Count);

        // Within one record the reader refuses the page.
        var twice = RecordJson(Live(1, 1, Item("A", 1)));
        twice["keys"] = new JsonArray(Key(1), Key(1));
        Assert.AreEqual(DataSyncWireReader.Corrupted, Read(Page(twice)).Problem);
    }

    [TestMethod]
    public void KindContentHashAndCountsAreCheckedAgainstTheManifest()
    {
        var records = new[] { Live(1, 1, Item("A", 1)), Tombstone(2, 2) };
        var written = DataSyncWireWriter.WriteKind(Snapshot, Kind, 0, records, Small);
        DataSyncFeedKind Manifest(string hash, int count = 2, long since = 0) =>
            new(Kind, 1, 2, 0, 1, 1, hash, since, count, false);

        var good = Assemble(written.Pages, TestItemCodec.Instance, Small);
        good.Complete(Manifest(written.ContentHash), false);
        Assert.IsNull(good.Problem);

        var wrongHash = Assemble(written.Pages, TestItemCodec.Instance, Small);
        Assert.AreEqual(0, wrongHash.Complete(Manifest(ContentHash.Of(new JsonArray())), false).Entities.Count);
        Assert.AreEqual(DataSyncWireReader.Corrupted, wrongHash.Problem);

        var wrongCount = Assemble(written.Pages, TestItemCodec.Instance, Small);
        wrongCount.Complete(Manifest(written.ContentHash, count: 3), false);
        Assert.AreEqual(DataSyncWireReader.Corrupted, wrongCount.Problem);

        var wrongSince = Assemble(written.Pages, TestItemCodec.Instance, Small);
        wrongSince.Complete(Manifest(written.ContentHash, since: 1), false);
        Assert.AreEqual(DataSyncWireReader.Corrupted, wrongSince.Problem);
    }

    [TestMethod]
    public void AMissingLastPageDiscardsThePull()
    {
        var pages = Write(Enumerable.Range(1, 12).Select(i => Live(i, i, Item("I" + i, 2))).ToArray());
        Assert.IsTrue(pages.Count > 1);
        var assembler = Assemble(pages.Take(pages.Count - 1).ToList(), TestItemCodec.Instance, Small);
        Assert.IsNull(assembler.Problem);
        assembler.Complete(12, false);
        Assert.AreEqual(DataSyncWireReader.Corrupted, assembler.Problem);

        // A page after the complete one is corrupt too.
        var extra = Assemble([..pages, pages[^1]], TestItemCodec.Instance, Small);
        Assert.AreEqual(DataSyncWireReader.Corrupted, extra.Problem);
    }

    [TestMethod]
    public void RecordsAboveTheManifestMaxSeqDiscardThePull()
    {
        var assembler = Assemble(Write(Live(1, 5, Item("A", 1))), TestItemCodec.Instance, Small);
        assembler.Complete(4, false);
        Assert.AreEqual(DataSyncWireReader.Corrupted, assembler.Problem);
    }

    [TestMethod]
    public void MoreLiveEntitiesThanTheKindAllowsIsTooLarge()
    {
        var limits = DataSyncLimits.Default with { MaxExtensionGroups = 2 };
        var records = new[]
        {
            Live(1, 1, Group("A", ".a")), Live(2, 2, Group("B", ".b")), Tombstone(3, 3), Live(4, 4, Group("C", ".c")),
        };
        var pages = DataSyncWireWriter.WritePages(Snapshot, "extensionGroup", 0, records, limits);
        var assembler = new DataSyncRecordAssembler(ExtensionGroupCodec.Instance, limits);
        foreach (var page in pages) assembler.Add(DataSyncWireReader.ReadPage(page, Snapshot, "extensionGroup", limits));
        assembler.Complete(4, false);
        Assert.AreEqual(DataSyncWireReader.TooLarge, assembler.Problem);
    }

    [TestMethod]
    public void EntitiesAreHeldForNewerSchemasAndCodecRefusals()
    {
        var newer = Live(1, 1, Item("A", 1), schemaVersion: 2);
        var older = Live(2, 2, Item("B", 1)) with { SchemaVersion = 0 };
        var invalid = Live(3, 3, new JsonObject { ["name"] = "" });
        var assembler = new DataSyncRecordAssembler(TestItemCodec.Instance, Small);
        assembler.Add(Read(Page(RecordJson(newer), RecordJson(invalid))));
        var staged = assembler.Complete(3, false);
        Assert.AreEqual(DataSyncHeldReason.NewerSchema, staged.Entities[0].Held);
        Assert.AreEqual(DataSyncHeldReason.Invalid, staged.Entities[1].Held);
        Assert.AreEqual("#1", staged.Entities[1].DisplayName, "an empty name falls back to the index");
        Assert.AreEqual(DataSyncWireReader.Corrupted, Read(Page(RecordJson(older))).Problem,
            "a schema version below 1 is no wire value");
    }

    [TestMethod]
    public void UnknownRecordMembersAreIgnoredAndUnknownHeldReasonsReadAsAtSource()
    {
        var record = RecordJson(Live(1, 1, Item("A", 1)));
        record["x-future"] = new JsonObject { ["a"] = 1 };
        var held = RecordJson(HeldAtSource(2, 2));
        held["heldAtSource"] = "SomethingNew";
        var result = Read(Page(record, held));
        Assert.IsNull(result.Problem);
        Assert.AreEqual(2, result.Records.Count);
        Assert.AreEqual(DataSyncHeldReason.AtSource, result.Records[1].HeldAtSource);
        Assert.AreEqual(2, result.Page!.Records.Count);
    }

    // ---- page problems ------------------------------------------------------------------------

    [TestMethod]
    public void ForeignSnapshotsAndKindsAreWrongSnapshot()
    {
        var page = Write(Live(1, 1, Item("A", 1)))[0];
        Assert.AreEqual(DataSyncWireReader.WrongSnapshot, DataSyncWireReader.ReadPage(page, "other", Kind, Small).Problem);
        Assert.AreEqual(DataSyncWireReader.WrongSnapshot,
            DataSyncWireReader.ReadPage(page, Snapshot, "extensionGroup", Small).Problem);
    }

    [TestMethod]
    public void OversizedPagesAreTooLarge()
    {
        var page = Write(Live(1, 1, Item("A", 1)))[0];
        Assert.AreEqual(DataSyncWireReader.TooLarge,
            DataSyncWireReader.ReadPage(page, Snapshot, Kind, Small with { MaxPageBytes = page.Length - 1 }).Problem);
        var many = Page(Enumerable.Range(1, 6).Select(i => (JsonNode)RecordJson(Live(i, i, Item("I", 0)))).ToArray());
        Assert.AreEqual(DataSyncWireReader.TooLarge, Read(many).Problem);
    }

    [TestMethod]
    public void DepthSixtyFourIsAcceptedAndSixtyFiveRefused()
    {
        // The page object, its records array, the record and its content are four levels.
        byte[] PageWithDepth(int depth)
        {
            JsonNode nested = new JsonArray();
            for (var i = 0; i < depth - 5; i++) nested = new JsonArray(nested);
            var content = new JsonObject { ["name"] = "Deep", ["x"] = nested };
            return Page(RecordJson(Live(1, 1, content)));
        }

        Assert.IsNull(DataSyncWireReader.ReadPage(PageWithDepth(64), Snapshot, Kind, DataSyncLimits.Default).Problem);
        Assert.AreEqual(DataSyncWireReader.Corrupted,
            DataSyncWireReader.ReadPage(PageWithDepth(65), Snapshot, Kind, DataSyncLimits.Default).Problem);
    }

    [TestMethod]
    public void NonIntegersDuplicatesAndBadUtf8AreCorrupted()
    {
        var text = Text(Write(Live(1, 7, Item("A", 1)))[0]);
        foreach (var number in new[] { "1.5", "1e30", "9223372036854775808", "7.0", "7e0", "-9223372036854775809" })
        {
            var mutated = text.Replace("\"seq\":7", "\"seq\":" + number);
            Assert.AreEqual(DataSyncWireReader.Corrupted, Read(Encoding.UTF8.GetBytes(mutated)).Problem, number);
        }

        var inContent = text.Replace("\"name\":\"A\"", "\"name\":\"A\",\"x\":2.5");
        Assert.AreEqual(DataSyncWireReader.Corrupted, Read(Encoding.UTF8.GetBytes(inContent)).Problem);

        var duplicate = text.Replace("\"deleted\":false", "\"deleted\":false,\"deleted\":false");
        Assert.AreEqual(DataSyncWireReader.Corrupted, Read(Encoding.UTF8.GetBytes(duplicate)).Problem);

        var bytes = Encoding.UTF8.GetBytes(text.Replace("\"A\"", "\"A\u00e9\""));
        var index = Array.IndexOf(bytes, (byte)0xC3);
        bytes[index + 1] = 0x28;
        Assert.AreEqual(DataSyncWireReader.Corrupted, Read(bytes).Problem);

        var bom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        Assert.AreEqual(DataSyncWireReader.Corrupted, Read(bom).Problem);
    }

    [TestMethod]
    public void MalformedRecordsAreCorrupted()
    {
        var valid = RecordJson(Live(1, 5, Item("A", 1)));
        var cases = new Dictionary<string, Action<JsonObject>>
        {
            ["no keys"] = r => r["keys"] = new JsonArray(),
            ["bad key"] = r => r["keys"] = new JsonArray("not-a-key"),
            ["bad origin"] = r => r["origin"] = "node a",
            ["seq 0"] = r => r["seq"] = 0,
            ["bad vv"] = r => r["vv"] = new JsonObject { ["xyz"] = 1 },
            ["vv counter 0"] = r => r["vv"] = new JsonObject { [Actor] = 0 },
            ["no deleted"] = r => r.Remove("deleted"),
            ["bad editor"] = r => r["editedBy"]!["actorId"] = "nope",
            ["bad order key"] = r => r["orderKey"] = "a00",
            ["tombstone with content"] = r => r["deleted"] = true,
            ["held with content"] = r => r["heldAtSource"] = "Invalid",
            ["live without hash"] = r => r.Remove("hash"),
            ["negative chunks"] = r => r["chunks"] = -1,
            ["content not an object"] = r => r["content"] = "x",
            // Taken into local state, a longer order key would make this device's own writer refuse every later
            // snapshot (DataSyncWireWriter.Validate).
            ["order key over 128"] = r => r["orderKey"] = "a0" + new string('V', 127),
            ["editor name over 128"] = r => r["editedBy"]!["name"] = new string('x', 129),
        };
        Assert.IsTrue(FractionalIndex.IsValid("a0" + new string('V', 127)), "only its length is wrong");
        foreach (var (name, mutate) in cases)
        {
            var record = (JsonObject)valid.DeepClone();
            mutate(record);
            Assert.AreEqual(DataSyncWireReader.Corrupted, Read(Page(record)).Problem, name);
        }

        Assert.IsNull(Read(Page(valid)).Problem);
        var longest = (JsonObject)valid.DeepClone();
        longest["orderKey"] = "a0" + new string('V', 126);
        longest["editedBy"]!["name"] = new string('x', 128);
        Assert.IsNull(Read(Page(longest)).Problem, "exactly at the limits");
    }

    // ---- the assembler's own guards on peer input ------------------------------------------------------

    /// <summary>A chunked record and its chunks, as a source writes them, in one page's items.</summary>
    private static List<JsonObject> ChunkedItems()
    {
        var pages = Write(Live(1, 1, Item("Big", 120, 30)));
        var items = pages.SelectMany(p => ((JsonArray)ParsePage(p)["records"]!).Select(n => (JsonObject)n!.DeepClone())).ToList();
        Assert.IsTrue(items[0]["chunks"]!.GetValue<int>() >= 3, "the record travels in chunks");
        return items;
    }

    private static DataSyncIncomingEntity StageChunked(IEnumerable<JsonObject> items, DataSyncLimits? assemblerLimits = null)
    {
        var assembler = new DataSyncRecordAssembler(TestItemCodec.Instance, assemblerLimits ?? DataSyncLimits.Default);
        assembler.Add(DataSyncWireReader.ReadPage(Page(items.Cast<JsonNode>().ToArray()), Snapshot, Kind, DataSyncLimits.Default));
        var entity = assembler.Complete(1, false).Entities.Single();
        Assert.IsNull(assembler.Problem, "one entity's problem holds that entity, never the pull");
        return entity;
    }

    [TestMethod]
    public void ChunksMustBeExactlyZeroToNMinusOne()
    {
        var items = ChunkedItems();
        foreach (var chunk in items.Skip(1)) chunk["index"] = chunk["index"]!.GetValue<int>() + 1;
        Assert.AreEqual(DataSyncHeldReason.Invalid, StageChunked(items).Held);
    }

    [TestMethod]
    public void ChunksMustAgreeOnTheirPath()
    {
        var items = ChunkedItems();
        items[^1]["path"] = "other";
        Assert.AreEqual(DataSyncHeldReason.Invalid, StageChunked(items).Held);
    }

    [TestMethod]
    public void AChunkPathMustNotOverwriteTheHeadContent()
    {
        // The head already carries the chunks' member: put back over it, the result would still match the hash (the
        // source hashed the whole content), so only this rule tells the two apart.
        var items = ChunkedItems();
        items[0]["content"]!["children"] = new JsonArray(new JsonObject { ["id"] = "zz", ["label"] = "Z" });
        Assert.AreEqual(DataSyncHeldReason.Invalid, StageChunked(items).Held);
    }

    [TestMethod]
    public void MoreChunksThanAllowedAreHeldOnReceiveToo()
    {
        var items = ChunkedItems();
        var chunks = items[0]["chunks"]!.GetValue<int>();
        Assert.IsNull(StageChunked(items).Held, "within the limit it is reassembled");
        Assert.AreEqual(DataSyncHeldReason.Invalid,
            StageChunked(ChunkedItems(), DataSyncLimits.Default with { MaxChunksPerEntity = chunks - 1 }).Held);
    }

    [TestMethod]
    public void AChunkIndexSentTwiceDiscardsThePull()
    {
        var items = ChunkedItems();
        items.Add((JsonObject)items[1].DeepClone());
        var assembler = new DataSyncRecordAssembler(TestItemCodec.Instance, DataSyncLimits.Default);
        assembler.Add(DataSyncWireReader.ReadPage(Page(items.Cast<JsonNode>().ToArray()), Snapshot, Kind, DataSyncLimits.Default));
        Assert.AreEqual(DataSyncWireReader.Corrupted, assembler.Problem);
    }

    [TestMethod]
    public void PagesMustAgreeAndKeepTheSeqOrder()
    {
        var first = MarkIncomplete(Page(RecordJson(Live(1, 5, Item("A", 1)))));

        // A lower Seq on a later page.
        var lower = Assemble([first, Page(RecordJson(Live(2, 3, Item("B", 1))))], TestItemCodec.Instance, Small);
        Assert.AreEqual(DataSyncWireReader.Corrupted, lower.Problem);

        // Pages served from different sinces.
        var since = ParsePage(Page(RecordJson(Live(2, 7, Item("B", 1)))));
        since["sinceSeq"] = 1;
        var mixed = Assemble([first, ToBytes(since)], TestItemCodec.Instance, Small);
        Assert.AreEqual(DataSyncWireReader.Corrupted, mixed.Problem);

        var sound = Assemble([first, Page(RecordJson(Live(2, 7, Item("B", 1))))], TestItemCodec.Instance, Small);
        Assert.IsNull(sound.Problem);
    }

    [TestMethod]
    public void APageOfAnotherKindIsWrongSnapshot()
    {
        var page = DataSyncWireWriter.WritePages(Snapshot, "extensionGroup", 0, [Live(1, 1, Group("Video", ".mkv"))],
            Small)[0];
        var assembler = new DataSyncRecordAssembler(TestItemCodec.Instance, Small);
        assembler.Add(DataSyncWireReader.ReadPage(page, Snapshot, "extensionGroup", Small));
        Assert.AreEqual(DataSyncWireReader.WrongSnapshot, assembler.Problem);
    }

    [TestMethod]
    [DataRow("label")]
    [DataRow("member name")]
    [DataRow("chunk item")]
    public void TextNoReaderCanDecodeInsideContentHoldsOnlyThatEntity(string where)
    {
        // What an older source serves: content with an escaped unpaired surrogate (a parser accepts it and cannot
        // decode it). v3.1 §6.3 holds that entity; the page and every other record are read.
        byte[] page;
        if (where == "chunk item")
        {
            var pages = Write(Live(1, 1, Item("Big", 120, 30)), Live(2, 2, Item("Fine", 1)));
            // A label inside one of the chunks becomes an escaped lone surrogate.
            var text = Text(pages[1]);
            var at = text.IndexOf("\"label\":\"", StringComparison.Ordinal) + "\"label\":\"".Length;
            pages = [pages[0], Encoding.UTF8.GetBytes(text[..at] + "\\ud800" + text[at..]), .. pages.Skip(2)];
            var chunked = Assemble(pages, TestItemCodec.Instance, Small);
            var stagedChunks = chunked.Complete(2, false);
            Assert.IsNull(chunked.Problem);
            Assert.AreEqual(DataSyncHeldReason.Invalid, stagedChunks.Entities[0].Held);
            Assert.IsNull(stagedChunks.Entities[1].Held);
            return;
        }

        var content = Item("Odd", 2);
        if (where == "label") ((JsonObject)((JsonArray)content["children"]!)[0]!)["label"] = "a\ud800b";
        else content["x\udc00"] = 1;
        // Built as a source that does not check writes it: the hash is over the content as it was, escape included.
        page = Page(RecordJson(Live(1, 1, content)), RecordJson(Live(2, 2, Item("Fine", 1))));
        StringAssert.Contains(Text(page), where == "label" ? "\\ud800" : "\\udc00");

        var result = Read(page);
        Assert.IsNull(result.Problem, "the page is read");
        var assembler = new DataSyncRecordAssembler(TestItemCodec.Instance, Small);
        assembler.Add(result);
        var staged = assembler.Complete(2, false);
        Assert.IsNull(assembler.Problem);
        Assert.AreEqual(DataSyncHeldReason.Invalid, staged.Entities[0].Held, "the content no longer matches its hash");
        Assert.AreEqual("Odd", staged.Entities[0].DisplayName);
        Assert.IsNull(staged.Entities[1].Held);
    }

    [TestMethod]
    public void TextNoReaderCanDecodeInTheEnvelopeRefusesThePage()
    {
        var record = RecordJson(Live(1, 1, Item("A", 1)));
        ((JsonObject)record["editedBy"]!)["name"] = "PC\ud800";
        Assert.AreEqual(DataSyncWireReader.Corrupted, Read(Page(record)).Problem);

        var unknown = RecordJson(Live(1, 1, Item("A", 1)));
        unknown["future"] = "x\ud800";
        Assert.AreEqual(DataSyncWireReader.Corrupted, Read(Page(unknown)).Problem,
            "outside content, a record's own members are the envelope");
    }

    [TestMethod]
    public void SeqMustBeAboveSinceAndInOrder()
    {
        var page = ParsePage(Write(Live(1, 3, Item("A", 1)), Live(2, 4, Item("B", 1)))[0]);
        page["sinceSeq"] = 3;
        Assert.AreEqual(DataSyncWireReader.Corrupted, Read(ToBytes(page)).Problem);

        var outOfOrder = Page(RecordJson(Live(1, 4, Item("A", 1))), RecordJson(Live(2, 3, Item("B", 1))));
        Assert.AreEqual(DataSyncWireReader.Corrupted, Read(outOfOrder).Problem);
    }

    [TestMethod]
    public void PageEnvelopeMustBeConsistent()
    {
        var page = ParsePage(Write(Live(1, 1, Item("A", 1)))[0]);
        page["nextCursor"] = "p1";
        Assert.AreEqual(DataSyncWireReader.Corrupted, Read(ToBytes(page)).Problem, "complete with a cursor");
        page.Remove("nextCursor");
        page["complete"] = false;
        Assert.AreEqual(DataSyncWireReader.Corrupted, Read(ToBytes(page)).Problem, "incomplete without a cursor");
        Assert.AreEqual(DataSyncWireReader.Corrupted, Read("[]"u8.ToArray()).Problem);
        Assert.AreEqual(DataSyncWireReader.Corrupted, Read("{}"u8.ToArray()).Problem);
    }

    // ---- fuzz ---------------------------------------------------------------------------------

    [TestMethod]
    public void FiveHundredSeededMutationsNeverThrow()
    {
        var pages = DataSyncWireWriter.WritePages(Snapshot, Kind, 0,
            [Live(1, 1, Item("Big é", 120, 30)), Tombstone(2, 2), HeldAtSource(3, 3), Live(4, 4, Item("B", 3))], Small);
        var random = new Random(1435);
        var tokens = new[] { "1.5", "1e30", "9223372036854775808", "-0", "null", "\"\\ud800\"", "[]", "{}" };
        var outcomes = new Dictionary<string, int>();
        for (var run = 0; run < 500; run++)
        {
            var mutated = pages.Select(p => (byte[])p.Clone()).ToList();
            var target = random.Next(mutated.Count);
            var bytes = mutated[target];
            switch (run % 7)
            {
                case 0:
                    for (var flips = random.Next(1, 4); flips > 0; flips--)
                        bytes[random.Next(bytes.Length)] = (byte)random.Next(256);
                    break;
                case 1:
                    bytes = bytes[..random.Next(bytes.Length)];
                    break;
                case 2:
                {
                    var text = Encoding.UTF8.GetString(bytes);
                    var members = Regex.Matches(text, "\"[a-zA-Z]+\":(true|false|\\d+|\"[^\"]*\")");
                    if (members.Count > 0)
                    {
                        var m = members[random.Next(members.Count)];
                        text = text.Insert(m.Index + m.Length, "," + m.Value);
                    }

                    bytes = Encoding.UTF8.GetBytes(text);
                    break;
                }
                case 3:
                {
                    var text = Encoding.UTF8.GetString(bytes);
                    var numbers = Regex.Matches(text, "(?<=:)\\d+");
                    if (numbers.Count > 0)
                    {
                        var m = numbers[random.Next(numbers.Count)];
                        text = text[..m.Index] + tokens[random.Next(tokens.Length)] + text[(m.Index + m.Length)..];
                    }

                    bytes = Encoding.UTF8.GetBytes(text);
                    break;
                }
                case 4:
                {
                    var position = random.Next(bytes.Length);
                    var bad = random.Next(3) switch
                    {
                        0 => new byte[] { 0xFF },
                        1 => new byte[] { 0xC3, 0x28 },
                        _ => new byte[] { 0xED, 0xA0, 0x80 },   // an encoded surrogate
                    };
                    bytes = bytes[..position].Concat(bad).Concat(bytes[position..]).ToArray();
                    break;
                }
                case 5:
                {
                    // A content string (a label or a name) gains an escaped lone surrogate.
                    var text = Encoding.UTF8.GetString(bytes);
                    var strings = Regex.Matches(text, "\"(label|name)\":\"");
                    if (strings.Count > 0)
                    {
                        var m = strings[random.Next(strings.Count)];
                        text = text.Insert(m.Index + m.Length, random.Next(2) == 0 ? "\\ud800" : "\\udfff");
                    }

                    bytes = Encoding.UTF8.GetBytes(text);
                    break;
                }
                default:
                {
                    // Swap two pages or drop one.
                    if (random.Next(2) == 0 && mutated.Count > 1) mutated.RemoveAt(random.Next(mutated.Count));
                    else (mutated[0], mutated[^1]) = (mutated[^1], mutated[0]);
                    target = -1;
                    break;
                }
            }

            if (target >= 0) mutated[target] = bytes;

            var assembler = new DataSyncRecordAssembler(TestItemCodec.Instance, Small);
            foreach (var page in mutated)
            {
                var result = DataSyncWireReader.ReadPage(page, Snapshot, Kind, Small);
                Assert.AreEqual(result.Problem is null, result.Page is not null);
                assembler.Add(result);
            }

            var staged = assembler.Complete(4, false);
            var outcome = assembler.Problem ?? (staged.Entities.Any(e => e.Held is not null && e.Held != DataSyncHeldReason.AtSource)
                ? "held"
                : "ok");
            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
        }

        // The mutations reach more than one outcome, so the loop really exercised the reader and the assembler.
        Assert.IsTrue(outcomes.GetValueOrDefault(DataSyncWireReader.Corrupted) > 100, string.Join(",", outcomes));
        Assert.IsTrue(outcomes.Count >= 2, string.Join(",", outcomes));
    }

    private static byte[] MarkIncomplete(byte[] page)
    {
        var json = ParsePage(page);
        json["complete"] = false;
        json["nextCursor"] = "p1";
        return ToBytes(json);
    }
}
