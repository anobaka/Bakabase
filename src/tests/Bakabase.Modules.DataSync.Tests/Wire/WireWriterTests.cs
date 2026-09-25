using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Wire.WireTestData;

namespace Bakabase.Modules.DataSync.Tests.Wire;

[TestClass]
public class WireWriterTests
{
    private const string GroupKind = "extensionGroup";

    [TestMethod]
    public void WritesTheSpecifiedRecordAndPageShape()
    {
        var records = new[] { Live(1, 131, Group("Video", ".mkv"), orderKey: "a0V", keys: [Key(1), Key(2)]) };
        var pages = DataSyncWireWriter.WritePages(Snapshot, GroupKind, 120, records, DataSyncLimits.Default);
        Assert.AreEqual(1, pages.Count);
        var hash = ContentHash.Of(Group("Video", ".mkv"));
        Assert.AreEqual(
            "{\"complete\":true,\"kind\":\"extensionGroup\",\"records\":[" +
            "{\"chunks\":0,\"content\":{\"extensions\":[\".mkv\"],\"name\":\"Video\"},\"deleted\":false," +
            "\"editedBy\":{\"actorId\":\"0123456789abcdef\",\"name\":\"PC-1\",\"nodeId\":\"node-a\"}," +
            $"\"hash\":\"{hash}\",\"keys\":[\"{Key(1)}\",\"{Key(2)}\"],\"orderKey\":\"a0V\",\"origin\":\"node-a\"," +
            "\"schemaVersion\":1,\"seq\":131,\"vv\":{\"0123456789abcdef\":131}}]," +
            "\"sinceSeq\":120,\"snapshotId\":\"snap-1\"}",
            Text(pages[0]));
    }

    [TestMethod]
    public void TombstonesAndHeldRecordsCarryNoContent()
    {
        var pages = DataSyncWireWriter.WritePages(Snapshot, GroupKind, 0,
            [Tombstone(1, 1), HeldAtSource(2, 2, DataSyncHeldReason.LocalUnreadable)], DataSyncLimits.Default);
        var items = (JsonArray)ParsePage(pages[0])["records"]!;
        Assert.AreEqual(
            $"{{\"chunks\":0,\"deleted\":true,\"keys\":[\"{Key(1)}\"],\"origin\":\"node-a\",\"schemaVersion\":1,\"seq\":1," +
            "\"vv\":{\"0123456789abcdef\":1}}", CanonicalJson.Serialize(items[0]));
        Assert.AreEqual(
            $"{{\"chunks\":0,\"deleted\":false,\"heldAtSource\":\"LocalUnreadable\",\"keys\":[\"{Key(2)}\"]," +
            "\"origin\":\"node-a\",\"schemaVersion\":1,\"seq\":2,\"vv\":{\"0123456789abcdef\":2}}",
            CanonicalJson.Serialize(items[1]));
    }

    [TestMethod]
    [DataRow("label")]
    [DataRow("member name")]
    public void ContentWithTextNoReaderCanDecodeIsHeldAtTheSource(string where)
    {
        // Canonical JSON writes an unpaired surrogate as an escape a reader parses and cannot decode. Served as it
        // was, one such string made every reader refuse the whole page, at every pull.
        var content = Item("Odd", 2);
        if (where == "label") ((JsonObject)((JsonArray)content["children"]!)[0]!)["label"] = "a\ud800b";
        else content["x\udc00"] = 1;
        var written = DataSyncWireWriter.WriteKind(Snapshot, TestItemCodec.Kind, 0,
            [Live(1, 1, content), Live(2, 2, Item("Fine", 1))], Small);

        Assert.AreEqual(DataSyncHeldReason.Invalid, written.Records[0].HeldAtSource);
        Assert.IsNull(written.Records[0].Content);
        Assert.IsNull(written.Records[1].HeldAtSource);
        Assert.IsFalse(written.Pages.Any(p => Text(p).Contains("\\ud", StringComparison.OrdinalIgnoreCase)));

        var assembler = Assemble(written.Pages, TestItemCodec.Instance, Small);
        var staged = assembler.Complete(2, false);
        Assert.IsNull(assembler.Problem, "the page is read");
        Assert.AreEqual(DataSyncHeldReason.AtSource, staged.Entities[0].Held, "only that entity waits");
        Assert.IsNull(staged.Entities[1].Held);
        Assert.AreEqual(written.ContentHash, assembler.ContentHash);
    }

    [TestMethod]
    public void BytesAreDeterministicAndCanonical()
    {
        var records = Enumerable.Range(1, 40)
            .Select(i => i % 7 == 0 ? Tombstone(i, i) : Live(i, i, Item("Item é " + i, i % 5, 30))).ToList();
        var first = DataSyncWireWriter.WritePages(Snapshot, TestItemCodec.Kind, 0, records, Small);
        var second = DataSyncWireWriter.WritePages(Snapshot, TestItemCodec.Kind, 0, records, Small);
        Assert.AreEqual(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            CollectionAssert.AreEqual(first[i], second[i]);
            Assert.AreEqual(Text(first[i]), CanonicalJson.Serialize(JsonNode.Parse(first[i])));
        }
    }

    [TestMethod]
    public void AnEmptyKindIsOneCompletePage()
    {
        var pages = DataSyncWireWriter.WritePages(Snapshot, GroupKind, 7, [], DataSyncLimits.Default);
        Assert.AreEqual(1, pages.Count);
        Assert.AreEqual("{\"complete\":true,\"kind\":\"extensionGroup\",\"records\":[],\"sinceSeq\":7,\"snapshotId\":\"snap-1\"}",
            Text(pages[0]));
    }

    [TestMethod]
    public void PagesStayWithinTheSmallLimitsAndChainTheirCursors()
    {
        var records = Enumerable.Range(1, 60).Select(i => Live(i, i, Item("Item " + i, i % 20, 40))).ToList();
        var pages = DataSyncWireWriter.WritePages(Snapshot, TestItemCodec.Kind, 0, records, Small);
        Assert.IsTrue(pages.Count > 12);
        for (var i = 0; i < pages.Count; i++)
        {
            Assert.IsTrue(pages[i].Length <= Small.MaxPageBytes, $"page {i}: {pages[i].Length}");
            var page = ParsePage(pages[i]);
            Assert.IsTrue(((JsonArray)page["records"]!).Count <= Small.MaxRecordsPerPage);
            var last = i == pages.Count - 1;
            Assert.AreEqual(last, (bool)page["complete"]!);
            Assert.AreEqual(last ? null : DataSyncWireWriter.CursorOf(i + 1), (string?)page["nextCursor"]);
        }
    }

    [TestMethod]
    public void PagesStayUnderOneMebibyteAndFiveHundredRecords()
    {
        var limits = DataSyncLimits.Default;
        var records = new List<DataSyncWireRecord>();
        for (var i = 1; i <= 1_200; i++) records.Add(Live(i, i, Group("Group " + i, ".mkv", ".mp4")));
        // One entity of about 3 MiB: chunked at 256 KiB.
        records.Add(Live(5_000, 5_000, Item("Big", 30_000, 90)));
        var written = DataSyncWireWriter.WriteKind(Snapshot, TestItemCodec.Kind, 0, records, limits);

        Assert.IsTrue(written.Pages.Count >= 6);
        var chunkCount = 0;
        foreach (var bytes in written.Pages)
        {
            Assert.IsTrue(bytes.Length <= limits.MaxPageBytes, bytes.Length.ToString());
            var items = (JsonArray)ParsePage(bytes)["records"]!;
            Assert.IsTrue(items.Count <= limits.MaxRecordsPerPage);
            foreach (var item in items.OfType<JsonObject>().Where(o => o.ContainsKey("chunkOf")))
            {
                chunkCount++;
                Assert.IsTrue(CanonicalJson.SerializeToUtf8Bytes(item).Length <= limits.MaxChunkBytes);
            }
        }

        Assert.IsTrue(chunkCount >= 12, chunkCount.ToString());
        Assert.AreEqual(written.Pages.Sum(p => (long)p.Length), written.TotalBytes);
    }

    [TestMethod]
    public void ContentIsChunkedOnlyAboveMaxChunkBytes()
    {
        // Find the largest test item that is not chunked, then one child more.
        var children = 1;
        while (CanonicalJson.SerializeToUtf8Bytes(Item("Edge", children + 1, 20)).Length <= Small.MaxChunkBytes)
            children++;
        var atLimit = Item("Edge", children, 20);
        var aboveLimit = Item("Edge", children + 1, 20);
        Assert.IsTrue(CanonicalJson.SerializeToUtf8Bytes(atLimit).Length <= Small.MaxChunkBytes);

        var whole = ParsePage(DataSyncWireWriter.WritePages(Snapshot, TestItemCodec.Kind, 0, [Live(1, 1, atLimit)], Small)[0]);
        Assert.AreEqual(1, ((JsonArray)whole["records"]!).Count);
        Assert.AreEqual(0, (int)whole["records"]![0]!["chunks"]!);

        var pages = DataSyncWireWriter.WritePages(Snapshot, TestItemCodec.Kind, 0, [Live(1, 1, aboveLimit)], Small);
        var items = pages.SelectMany(p => ((JsonArray)ParsePage(p)["records"]!).OfType<JsonObject>()).ToList();
        var head = items[0];
        Assert.IsTrue((int)head["chunks"]! > 0);
        Assert.IsFalse(((JsonObject)head["content"]!).ContainsKey("children"));
        Assert.AreEqual(ContentHash.Of(aboveLimit), (string?)head["hash"], "the hash covers the reassembled content");
        var chunks = items.Skip(1).ToList();
        Assert.AreEqual((int)head["chunks"]!, chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.AreEqual(Key(1), (string?)chunks[i]["chunkOf"]);
            Assert.AreEqual(i, (int)chunks[i]["index"]!);
            Assert.AreEqual("children", (string?)chunks[i]["path"]);
            Assert.IsTrue(CanonicalJson.SerializeToUtf8Bytes(chunks[i]).Length <= Small.MaxChunkBytes);
        }

        var staged = Assemble(pages, TestItemCodec.Instance, Small).Complete(1, false);
        Assert.AreEqual(ContentHash.Of(aboveLimit), ContentHash.Of(staged.Entities[0].Record.Content));
        Assert.IsNull(staged.Entities[0].Held);
    }

    [TestMethod]
    public void ARecordWithASubtreeLargerThanAPageIsHeldAtSource()
    {
        // One child alone is larger than a page: it cannot travel.
        var content = new JsonObject
        {
            ["children"] = new JsonArray(new JsonObject { ["id"] = "c", ["label"] = new string('x', 5_000) }),
            ["name"] = "Huge",
        };
        var written = DataSyncWireWriter.WriteKind(Snapshot, TestItemCodec.Kind, 0, [Live(1, 1, content)], Small);
        var record = written.Records.Single();
        Assert.AreEqual(DataSyncHeldReason.Invalid, record.HeldAtSource);
        Assert.IsNull(record.Content);
        Assert.IsNull(record.Hash);
        Assert.AreEqual("Invalid", (string?)ParsePage(written.Pages[0])["records"]![0]!["heldAtSource"]);
        Assert.AreEqual(DataSyncWireFormat.KindContentHash([record]), written.ContentHash);

        // The same at the default limits: a subtree over 1 MiB.
        var big = new JsonObject
        {
            ["children"] = new JsonArray(new JsonObject { ["id"] = "c", ["label"] = new string('x', 1_100_000) }),
            ["name"] = "Huge",
        };
        var atDefault = DataSyncWireWriter.WriteKind(Snapshot, TestItemCodec.Kind, 0, [Live(1, 1, big)], DataSyncLimits.Default);
        Assert.AreEqual(DataSyncHeldReason.Invalid, atDefault.Records.Single().HeldAtSource);
    }

    [TestMethod]
    public void MoreChunksThanAllowedIsHeldAtSource()
    {
        // About 8 KiB of children: nine chunks at 1 KiB, and too large for a 4 KiB page as a whole.
        var limits = Small with { MaxChunksPerEntity = 2 };
        var written = DataSyncWireWriter.WriteKind(Snapshot, TestItemCodec.Kind, 0, [Live(1, 1, Item("Many", 200, 20))],
            limits);
        Assert.AreEqual(DataSyncHeldReason.Invalid, written.Records.Single().HeldAtSource);
    }

    [TestMethod]
    public void ContentWithoutAnArrayTravelsWholeWhenItFitsAPage()
    {
        var content = new JsonObject { ["name"] = "Wide", ["x-note"] = new string('n', 2_000) };
        var written = DataSyncWireWriter.WriteKind(Snapshot, TestItemCodec.Kind, 0, [Live(1, 1, content)], Small);
        Assert.IsNull(written.Records.Single().HeldAtSource);
        Assert.AreEqual(0, (int)ParsePage(written.Pages[0])["records"]![0]!["chunks"]!);
    }

    [TestMethod]
    public void KindContentHashFollowsTheSpecifiedArray()
    {
        var live = Live(1, 3, Group("Video", ".mkv"));
        var records = new[] { live, Tombstone(2, 5), HeldAtSource(3, 8) };
        var expected = ContentHash.Of(JsonNode.Parse(
            $"[[\"{Key(1)}\",3,\"{live.Hash}\"],[\"{Key(2)}\",5,\"tombstone\"],[\"{Key(3)}\",8,\"held\"]]"));
        Assert.AreEqual(expected, DataSyncWireFormat.KindContentHash(records));
        Assert.AreEqual(expected, DataSyncWireWriter.WriteKind(Snapshot, GroupKind, 0, records, Small).ContentHash);
    }

    [TestMethod]
    public void CallerMistakesThrow()
    {
        var content = Group("Video", ".mkv");
        var limits = DataSyncLimits.Default;
        Assert.ThrowsException<ArgumentException>(() => DataSyncWireWriter.WritePages(Snapshot, GroupKind, 0,
            [Live(1, 1, content) with { Hash = ContentHash.Of(Group("Other")) }], limits));
        Assert.ThrowsException<ArgumentException>(() => DataSyncWireWriter.WritePages(Snapshot, GroupKind, 0,
            [Live(1, 2, content), Live(2, 1, content)], limits));
        Assert.ThrowsException<ArgumentException>(() => DataSyncWireWriter.WritePages(Snapshot, GroupKind, 5,
            [Live(1, 5, content)], limits));
        Assert.ThrowsException<ArgumentException>(() => DataSyncWireWriter.WritePages(Snapshot, GroupKind, 0,
            [Live(1, 1, content), Live(2, 2, content, keys: [Key(3), Key(1)])], limits));
        Assert.ThrowsException<ArgumentException>(() => DataSyncWireWriter.WritePages(Snapshot, GroupKind, 0,
            [Live(1, 1, content) with { Chunks = 2 }], limits));
        Assert.ThrowsException<ArgumentException>(() => DataSyncWireWriter.WritePages(Snapshot, GroupKind, 0,
            [Tombstone(1, 1) with { Content = content }], limits));
        Assert.ThrowsException<ArgumentException>(() => DataSyncWireWriter.WritePages(Snapshot, GroupKind, 0,
            [Live(1, 1, content) with { Origin = "node a" }], limits));
        Assert.ThrowsException<ArgumentException>(() => DataSyncWireWriter.WritePages(Snapshot, GroupKind, 0,
            [Live(1, 1, content, orderKey: "a00")], limits));
        Assert.ThrowsException<ArgumentException>(() => DataSyncWireWriter.WritePages(Snapshot, GroupKind, 0,
            [Live(1, 1, content, orderKey: "a0" + new string('V', 127))], limits));
        Assert.ThrowsException<ArgumentException>(() => DataSyncWireWriter.WritePages(Snapshot, GroupKind, 0,
            [Live(1, 1, content, keys: Enumerable.Range(1, 65).Select(Key).ToList())], limits));
        Assert.ThrowsException<ArgumentException>(() => DataSyncWireWriter.WritePages(Snapshot, GroupKind, 0,
            [Live(1, 1, content) with { EditedBy = new DataSyncEditorRef("node-a", "PC", "nope") }], limits));
        Assert.ThrowsException<ArgumentException>(() => DataSyncWireWriter.WritePages("bad id!", GroupKind, 0, [], limits));
        Assert.ThrowsException<ArgumentException>(() => DataSyncWireWriter.WritePages(Snapshot, "Bad", 0, [], limits));
    }

    [TestMethod]
    public void EditorNamesAreMadeReadableRatherThanRefused()
    {
        var name = "PC\u0007" + new string('n', 200) + "\ud800";
        var record = Live(1, 1, Group("Video", ".mkv")) with { EditedBy = new DataSyncEditorRef("node-a", name, Actor) };
        var written = DataSyncWireWriter.WriteKind(Snapshot, GroupKind, 0, [record], DataSyncLimits.Default);
        var editor = written.Records.Single().EditedBy!;
        Assert.AreEqual(DataSyncLimits.Default.MaxEditorNameLength, editor.Name.Length);
        Assert.IsTrue(editor.Name.StartsWith("PC\uFFFDnnn", StringComparison.Ordinal));

        var read = DataSyncWireReader.ReadPage(written.Pages[0], Snapshot, GroupKind, DataSyncLimits.Default);
        Assert.IsNull(read.Problem);
        Assert.AreEqual(editor, read.Records.Single().EditedBy);
    }

    [TestMethod]
    public void CursorsRoundTrip()
    {
        Assert.IsNull(DataSyncWireWriter.CursorOf(0));
        Assert.AreEqual("p1", DataSyncWireWriter.CursorOf(1));
        for (var i = 0; i < 1000; i += 37) Assert.AreEqual(i, DataSyncWireWriter.PageIndexOf(DataSyncWireWriter.CursorOf(i)));
        foreach (var bad in new[] { "", "p", "p0", "p01", "q1", "p-1", "p1x", "p99999999999" })
            Assert.IsNull(DataSyncWireWriter.PageIndexOf(bad), bad);
    }

    [TestMethod]
    public void ExtensionGroupsRoundTripThroughTheirCodec()
    {
        var records = new[] { Live(1, 1, Group("Video", ".mkv", ".mp4")), Tombstone(2, 2) };
        var staged = Assemble(DataSyncWireWriter.WritePages(Snapshot, GroupKind, 0, records, DataSyncLimits.Default),
            ExtensionGroupCodec.Instance, DataSyncLimits.Default).Complete(2, true);
        Assert.AreEqual(new ExtensionGroupContentV1("Video", [".mkv", ".mp4"]), staged.Entities[0].Content);
        Assert.IsTrue(staged.Entities[1].Record.Deleted);
        Assert.IsTrue(staged.FullReconciliation);
    }
}
