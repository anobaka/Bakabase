using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.DataSync.Feed;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// A <see cref="DataSyncRefreshFixture"/> whose feed source is read as a reader would read it: head, manifest and
/// every page's bytes, parsed back from raw canonical JSON and reassembled. Optionally a second memory kind
/// (extension groups), and limits a test lowers.
/// </summary>
internal sealed class DataSyncFeedFixture
{
    public const string ReaderNode = "reader-node-1";
    public const string ReaderGrant = "grant-1";
    public const string ReaderActor = "1111111111111111";

    private DataSyncFeedFixture(DataSyncRefreshFixture refresh, MemoryDataSyncKind? groups, ReferenceFeedPageWriter writer)
    {
        R = refresh;
        Groups = groups;
        Writer = writer;
    }

    public DataSyncRefreshFixture R { get; }
    public MemoryDataSyncKind Kind => R.Kind;
    public MemoryDataSyncKind? Groups { get; }
    public ReferenceFeedPageWriter Writer { get; }
    public IServiceProvider Services => R.Services;
    public IDataSyncFeedSource Feed => Services.GetRequiredService<IDataSyncFeedSource>();
    public DataSyncFeedSnapshots Snapshots => Services.GetRequiredService<DataSyncFeedSnapshots>();
    public DataSyncStore Store => R.Store;
    public ManualTimeProvider Clock => R.Clock;

    public static async Task<DataSyncFeedFixture> CreateAsync(bool hasOrder = false, bool verified = true,
        DataSyncLimits? limits = null, bool extensionGroups = false, Action<IServiceCollection>? configure = null)
    {
        var writer = new ReferenceFeedPageWriter();
        var groups = extensionGroups ? new MemoryDataSyncKind(DataSyncKindIds.ExtensionGroup) : null;
        var refresh = await DataSyncRefreshFixture.CreateAsync(hasOrder, verified, s =>
        {
            s.AddSingleton<IDataSyncFeedPageWriter>(writer);
            if (limits is not null) s.AddSingleton(limits);
            if (groups is not null) s.AddScoped<IDataSyncKind>(_ => groups);
            configure?.Invoke(s);
        });
        return new DataSyncFeedFixture(refresh, groups, writer);
    }

    public static DataSyncReader Reader(string node = ReaderNode, string grant = ReaderGrant, string name = "PC-2") =>
        new(node, grant, name);

    public static DataSyncFeedQuery Query(params (string Kind, long Since)[] since) =>
        new("twoWay", since.ToDictionary(s => s.Kind, s => s.Since), null, "ok");

    public static DataSyncFeedQuery Query(string? actor, params (string Kind, long Since)[] since) =>
        Query(since) with {ReaderActorId = actor};

    public Task<DataSyncFeedHead> HeadAsync(DataSyncFeedQuery query, DataSyncReader? reader = null) =>
        Feed.GetHeadAsync(reader ?? Reader(), query, default);

    public Task<DataSyncFeedManifest> ManifestAsync(DataSyncFeedQuery query, DataSyncReader? reader = null) =>
        Feed.CreateSnapshotAsync(reader ?? Reader(), query, default);

    /// <summary>A manifest, then every page of every kind it serves, parsed and reassembled.</summary>
    public async Task<ReadSnapshot> ReadAsync(DataSyncFeedQuery query, DataSyncReader? reader = null)
    {
        reader ??= Reader();
        var manifest = await ManifestAsync(query, reader);
        var kinds = new Dictionary<string, ReadKind>(StringComparer.Ordinal);
        foreach (var kind in manifest.Kinds) kinds[kind.Kind] = await ReadKindAsync(Feed, reader, manifest, kind);
        return new ReadSnapshot(manifest, kinds);
    }

    /// <summary>Every page of one kind, following the pages' own cursors, as a receiver does.</summary>
    public static async Task<ReadKind> ReadKindAsync(IDataSyncFeedSource feed, DataSyncReader reader,
        DataSyncFeedManifest manifest, DataSyncFeedKind kind)
    {
        var pages = new List<byte[]>();
        string? cursor = null;
        while (true)
        {
            var bytes = await feed.GetPageAsync(reader, manifest.SnapshotId, kind.Kind, kind.SinceSeq, cursor, default);
            pages.Add(bytes);
            var page = ParsePage(bytes);
            Assert.AreEqual(manifest.SnapshotId, page["snapshotId"]!.GetValue<string>());
            Assert.AreEqual(kind.Kind, page["kind"]!.GetValue<string>());
            Assert.AreEqual(kind.SinceSeq, page["sinceSeq"]!.GetValue<long>());
            if (page["complete"]!.GetValue<bool>()) break;
            cursor = page["nextCursor"]!.GetValue<string>();
            Assert.IsTrue(pages.Count < 10_000, "the pages never end");
        }

        return new ReadKind(kind, pages, Reassemble(pages));
    }

    /// <summary>Raw canonical page bytes, parsed as a receiver does (depth 64, never federation JSON).</summary>
    public static JsonObject ParsePage(byte[] bytes) =>
        JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions {MaxDepth = 64})!.AsObject();

    /// <summary>Records in page order, each chunked record with its children put back.</summary>
    public static IReadOnlyList<JsonObject> Reassemble(IEnumerable<byte[]> pages)
    {
        var records = new List<JsonObject>();
        var byKey = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            foreach (var item in ParsePage(page)["records"]!.AsArray().Select(i => i!.AsObject()))
            {
                if (item["chunkOf"] is { } of)
                {
                    var record = byKey[of.GetValue<string>()];
                    var path = item["path"]!.GetValue<string>();
                    var content = record["content"]!.AsObject();
                    if (content[path] is not JsonArray children) content[path] = children = new JsonArray();
                    foreach (var child in item["items"]!.AsArray()) children.Add(child!.DeepClone());
                    continue;
                }

                var copy = item.DeepClone().AsObject();
                records.Add(copy);
                byKey[copy["keys"]![0]!.GetValue<string>()] = copy;
            }
        }

        return records;
    }

    public Task<DataSyncLocalStateDbModel> StateAsync() => R.StateAsync();

    public async Task<IReadOnlyList<DataSyncRestoreEvidence>> EvidenceAsync() =>
        DataSyncRestoreEvidence.Read((await StateAsync()).RestoreEvidenceJson);

    /// <summary>What a reader should find in a live record: exactly what the codec publishes for the definition.</summary>
    public JsonObject Published(string localKey) =>
        (JsonObject) Kind.MemoryCodec.Publish(Kind.Definitions[localKey].ToContent(), DataSyncOverlay.None, false).Content!;

    public async Task<DataSyncEntityDbModel> SetAsync(string localKey, Action<DataSyncEntityDbModel> change)
    {
        var db = R.Db;
        var row = await db.DataSyncEntities.SingleAsync(e => e.Kind == R.KindId && e.LocalKey == localKey && e.DeletedAtUtc == null);
        change(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return row;
    }

    public static async Task<DataSyncFeedException> RefusedAsync(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (DataSyncFeedException e)
        {
            return e;
        }

        Assert.Fail("The feed answered instead of refusing.");
        return null!;
    }
}

internal sealed record ReadKind(DataSyncFeedKind Manifest, IReadOnlyList<byte[]> Pages, IReadOnlyList<JsonObject> Records)
{
    public IReadOnlyList<string> PrimaryKeys => Records.Select(r => r["keys"]![0]!.GetValue<string>()).ToList();
}

internal sealed record ReadSnapshot(DataSyncFeedManifest Manifest, IReadOnlyDictionary<string, ReadKind> Kinds);

/// <summary>
/// The page writer the feed's tests run with: the pure engine's <see cref="DataSyncWireWriter"/> once it is on the
/// branch (package A), and until then a reference writer of the same format (§7.5.3, §7.5.4): canonical pages
/// <c>{"complete","kind","nextCursor","records","sinceSeq","snapshotId"}</c>, cursors <c>p{n}</c>, content larger than
/// <c>MaxChunkBytes</c> sent without its largest array and followed by chunk records, a record that cannot travel held
/// as <c>Invalid</c>, at most <c>MaxPageBytes</c> and <c>MaxRecordsPerPage</c> items a page.
/// </summary>
internal sealed class ReferenceFeedPageWriter : IDataSyncFeedPageWriter
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private int _calls;

    public int Calls => _calls;

    public IReadOnlyList<byte[]> WritePages(string snapshotId, string kind, long sinceSeq,
        IReadOnlyList<DataSyncWireRecord> records, DataSyncLimits limits)
    {
        Interlocked.Increment(ref _calls);
        try
        {
            return DataSyncWireWriter.WritePages(snapshotId, kind, sinceSeq, records, limits);
        }
        catch (NotImplementedException)
        {
            return Write(snapshotId, kind, sinceSeq, records, limits);
        }
    }

    private static IReadOnlyList<byte[]> Write(string snapshotId, string kind, long sinceSeq,
        IReadOnlyList<DataSyncWireRecord> records, DataSyncLimits limits)
    {
        var capacity = limits.MaxPageBytes - Size(Envelope(snapshotId, kind, sinceSeq, "p" + int.MaxValue, []));
        var items = records.SelectMany(r => Items(r, limits, capacity)).ToList();

        var groups = new List<List<JsonObject>>();
        var current = new List<JsonObject>();
        var currentBytes = 0;
        foreach (var item in items)
        {
            var bytes = Size(item);
            if (current.Count > 0 &&
                (current.Count >= limits.MaxRecordsPerPage || currentBytes + 1 + bytes > capacity))
            {
                groups.Add(current);
                current = [];
                currentBytes = 0;
            }

            currentBytes += (current.Count > 0 ? 1 : 0) + bytes;
            current.Add(item);
        }

        groups.Add(current);
        return groups.Select((group, i) => CanonicalJson.SerializeToUtf8Bytes(Envelope(snapshotId, kind, sinceSeq,
            i == groups.Count - 1 ? null : "p" + (i + 1), group))).ToList();
    }

    private static IEnumerable<JsonObject> Items(DataSyncWireRecord record, DataSyncLimits limits, int capacity)
    {
        if (record.Content is { } content && Size(content) > limits.MaxChunkBytes)
        {
            var path = content.Where(m => m.Value is JsonArray).OrderByDescending(m => Size(m.Value))
                .ThenBy(m => m.Key, StringComparer.Ordinal).Select(m => m.Key).FirstOrDefault();
            if (path is not null)
            {
                var chunks = new List<JsonArray>();
                var chunk = new JsonArray();
                foreach (var child in (JsonArray) content[path]!)
                {
                    if (Size(child) > capacity / 2) return [Record(Held(record))];
                    if (chunk.Count > 0 && Size(chunk) + Size(child) > limits.MaxChunkBytes)
                    {
                        chunks.Add(chunk);
                        chunk = new JsonArray();
                    }

                    chunk.Add(child!.DeepClone());
                }

                if (chunk.Count > 0) chunks.Add(chunk);
                if (chunks.Count > limits.MaxChunksPerEntity) return [Record(Held(record))];
                var head = (JsonObject) content.DeepClone();
                head.Remove(path);
                return new[] {Record(record with {Content = head, Chunks = chunks.Count})}.Concat(chunks.Select(
                    (items, i) => new JsonObject
                    {
                        ["chunkOf"] = record.Keys[0], ["index"] = i, ["items"] = items, ["path"] = path,
                    }));
            }
        }

        var item = Record(record);
        return Size(item) <= capacity ? [item] : [Record(Held(record))];
    }

    private static DataSyncWireRecord Held(DataSyncWireRecord record) =>
        record with {Content = null, Hash = null, OrderKey = null, HeldAtSource = DataSyncHeldReason.Invalid, Chunks = 0};

    private static JsonObject Record(DataSyncWireRecord record)
    {
        var json = new JsonObject
        {
            ["chunks"] = record.Chunks,
            ["deleted"] = record.Deleted,
            ["keys"] = new JsonArray(record.Keys.Select(k => (JsonNode?) JsonValue.Create(k)).ToArray()),
            ["origin"] = record.Origin,
            ["schemaVersion"] = record.SchemaVersion,
            ["seq"] = record.Seq,
            ["vv"] = JsonNode.Parse(record.Vv.ToCanonicalString()),
        };
        if (record.Content is not null) json["content"] = record.Content.DeepClone();
        if (record.Hash is not null) json["hash"] = record.Hash;
        if (record.HeldAtSource is { } held) json["heldAtSource"] = held.ToString();
        if (record.OrderKey is not null) json["orderKey"] = record.OrderKey;
        if (record.EditedBy is { } editor)
            json["editedBy"] = new JsonObject
                {["actorId"] = editor.ActorId, ["name"] = editor.Name, ["nodeId"] = editor.NodeId};
        return json;
    }

    private static JsonObject Envelope(string snapshotId, string kind, long sinceSeq, string? nextCursor,
        IEnumerable<JsonObject> items)
    {
        var page = new JsonObject
        {
            ["complete"] = nextCursor is null,
            ["kind"] = kind,
            ["records"] = new JsonArray(items.Select(i => (JsonNode?) i).ToArray()),
            ["sinceSeq"] = sinceSeq,
            ["snapshotId"] = snapshotId,
        };
        if (nextCursor is not null) page["nextCursor"] = nextCursor;
        return page;
    }

    private static int Size(JsonNode? node) => Utf8.GetByteCount(CanonicalJson.Serialize(node));
}
