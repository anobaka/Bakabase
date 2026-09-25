using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Tests.Wire;

/// <summary>Records, pages and a full write → read → assemble round trip for wire tests.</summary>
internal static class WireTestData
{
    public const string Snapshot = "snap-1";
    public const string Actor = "0123456789abcdef";

    public static string Key(int i) => i.ToString("x32", CultureInfo.InvariantCulture);

    public static DataSyncVersionVector Vv(long counter) =>
        DataSyncVersionVector.Empty.With(new DataSyncActorId(Actor), counter);

    public static DataSyncWireRecord Live(int i, long seq, JsonObject content, string? orderKey = null,
        int schemaVersion = 1, IReadOnlyList<string>? keys = null) =>
        new(keys ?? [Key(i)], "node-a", seq, Vv(seq), new DataSyncEditorRef("node-a", "PC-1", Actor), false,
            schemaVersion, orderKey, content, ContentHash.Of(content), null, 0);

    public static DataSyncWireRecord Tombstone(int i, long seq) =>
        new([Key(i)], "node-a", seq, Vv(seq), null, true, 1, null, null, null, null, 0);

    public static DataSyncWireRecord HeldAtSource(int i, long seq, DataSyncHeldReason reason = DataSyncHeldReason.Invalid) =>
        new([Key(i)], "node-a", seq, Vv(seq), null, false, 1, null, null, null, reason, 0);

    public static JsonObject Group(string name, params string[] extensions) => new()
    {
        ["extensions"] = new JsonArray(extensions.Select(e => (JsonNode?)JsonValue.Create(e)).ToArray()),
        ["name"] = name,
    };

    /// <summary>Test item content with <paramref name="children"/> children whose labels are about <paramref name="labelLength"/> long.</summary>
    public static JsonObject Item(string name, int children, int labelLength = 8)
    {
        var content = new TestItemContent(name, "#e5484d", Enumerable.Range(0, children)
            .Select(i => new TestChild("c" + i.ToString(CultureInfo.InvariantCulture),
                ("L" + i.ToString(CultureInfo.InvariantCulture)).PadRight(labelLength, 'x'))));
        return TestItemCodec.Instance.Write(content);
    }

    public static DataSyncLimits Small { get; } = DataSyncLimits.Default with
    {
        MaxPageBytes = 4096, MaxChunkBytes = 1024, MaxRecordsPerPage = 5,
    };

    /// <summary>Reads every page into one assembler, as a receiver does.</summary>
    public static DataSyncRecordAssembler Assemble(IReadOnlyList<byte[]> pages, IDataSyncKindCodec codec,
        DataSyncLimits limits, string snapshot = Snapshot)
    {
        var assembler = new DataSyncRecordAssembler(codec, limits);
        foreach (var page in pages)
            assembler.Add(DataSyncWireReader.ReadPage(page, snapshot, codec.Descriptor.Kind, limits));
        return assembler;
    }

    public static JsonObject ParsePage(byte[] page) => (JsonObject)JsonNode.Parse(page)!;

    public static byte[] ToBytes(JsonNode node) => CanonicalJson.SerializeToUtf8Bytes(node);

    public static string Text(byte[] page) => Encoding.UTF8.GetString(page);
}
