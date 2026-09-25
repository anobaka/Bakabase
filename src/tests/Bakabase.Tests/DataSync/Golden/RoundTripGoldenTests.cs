using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.TestKit.DataSync;
using Bakabase.Tests.DataSync.Apply;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync.Golden;

/// <summary>
/// §13.4 <c>RoundTripGoldenTests</c>, per real kind: definitions made on one provider are published, travel as the
/// feed's real page bytes (<see cref="InProcessPeerClient"/>), are read and assembled as a receiver does, created on
/// an empty second provider by the apply runner, and published again there. Keys, comparison forms and child ids
/// (for extension groups, the extensions) come back equal, and what the first provider publishes matches
/// <c>Fixtures/DataSync/Golden/{kind}.v{schemaVersion}.json</c> once keys are placeholders.
/// <c>DATASYNC_WRITE_GOLDENS=&lt;dir&gt;</c> writes the golden again.
/// </summary>
/// <remarks>The custom property kind joins with package B's fixture (v3.1 §11.2's <c>DataSyncFixture</c>).</remarks>
[TestClass]
public class RoundTripGoldenTests
{
    private const string Groups = DataSyncKindIds.ExtensionGroup;
    private static readonly DataSyncLimits Limits = DataSyncLimits.Default;

    private static string GoldenPath(string kind, int schemaVersion) => Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "DataSync", "Golden", $"{kind}.v{schemaVersion}.json");

    private static Task<DataSyncApplyFixture> ProviderAsync() =>
        DataSyncApplyFixture.CreateAsync(s => s.AddSingleton<IDataSyncPeerClient>(new InProcessPeerClient()));

    [TestMethod]
    public async Task Extension_groups_travel_to_an_empty_provider_and_publish_again_unchanged()
    {
        var a = await ProviderAsync();
        var b = await ProviderAsync();
        await InProcessPeerClient.WireAsync(a.Services, b.Services);
        // Mixed case, a missing dot, blanks around one, a duplicate after folding, and a group with none.
        await a.AddGroupAsync("Video", ".MKV", "mp4", " .Avi ", ".mkv");
        await a.AddGroupAsync("Documents", ".pdf", ".DOCX");
        await a.AddGroupAsync("Nothing yet");
        await a.RefreshAsync();

        // A's feed as B reads it: real page bytes, reader and assembler.
        var fromA = await PullAsync(b, a);
        var published = fromA.Kinds.Single().Entities.Select(e => e.Record).ToList();
        Assert.AreEqual(3, published.Count);
        Assert.IsTrue(fromA.Kinds.Single().Entities.All(e => e.Held is null), "every record is valid where it arrives");
        await AssertGoldenAsync(Groups, ExtensionGroupCodec.Instance, published);

        // Created on the empty provider by an ordinary pull (row N3: no natural match there).
        var aState = await a.StateAsync();
        var peerA = new DataSyncPeer(fromA.PeerName, fromA.PeerNodeId, aState.ActorId);
        var link = await b.LinkAsync(peerA, DataSyncLinkMode.TwoWay, true, Groups);
        var applied = await b.Runner.RunAutoSyncAsync(DataSyncApplyFixture.Context(link, peerA), fromA, b.Args());
        Assert.AreEqual(3, applied.Applied);
        Assert.AreEqual(3, (await b.ExtensionGroups.GetAll()).Length);

        // B publishes them again; A reads them back.
        await b.RefreshAsync();
        var fromB = (await PullAsync(a, b)).Kinds.Single().Entities.Select(e => e.Record).ToList();
        Assert.AreEqual(3, fromB.Count);
        foreach (var original in published)
        {
            var again = fromB.Single(r => r.Keys[0] == original.Keys[0]);
            CollectionAssert.AreEqual(original.Keys.ToArray(), again.Keys.ToArray(), "the same keys");
            Assert.AreEqual(original.Origin, again.Origin, "the same origin");
            Assert.IsTrue(JsonNode.DeepEquals(original.Content, again.Content), "the same content, extensions included");
            Assert.AreEqual(original.Hash, again.Hash);
            Assert.AreEqual(SharedHash(original), SharedHash(again), "the same comparison form");
            Assert.AreEqual(original.Vv, again.Vv, "an exact create takes the record's vector, and B revised nothing");
        }

        // The side rows agree too: each device computed the same SharedHash with its own codec (§3.4).
        var rowsA = await a.RowsAsync(Groups);
        var rowsB = await b.RowsAsync(Groups);
        foreach (var row in rowsA)
            Assert.AreEqual(row.SharedHash, rowsB.Single(r => r.SyncKey == row.SyncKey).SharedHash, row.LocalKey);
    }

    private static string? SharedHash(DataSyncWireRecord record) =>
        DataSyncPublication.SharedHashOfRecord(ExtensionGroupCodec.Instance, record, Limits);

    /// <summary>
    /// <paramref name="reader"/> reads <paramref name="source"/>'s extension groups from 0 through its peer client,
    /// page by page following the cursors, and stages them as the fetch half does (§8.10.2 step 3).
    /// </summary>
    private static async Task<DataSyncStagedPull> PullAsync(DataSyncApplyFixture reader, DataSyncApplyFixture source)
    {
        var client = InProcessPeerClient.ClientOf(reader.Services);
        var peer = source.Identity.Device;
        var query = new DataSyncFeedQuery("twoWay", new Dictionary<string, long> { [Groups] = 0 },
            (await reader.StateAsync()).ActorId, "ok");
        var manifest = await client.GetManifestAsync(peer.NodeId, query, default);
        var kind = manifest.Kinds.Single(k => k.Kind == Groups);
        var assembler = new DataSyncRecordAssembler(ExtensionGroupCodec.Instance, Limits);
        string? cursor = null;
        do
        {
            var page = DataSyncWireReader.ReadPage(
                await client.GetPageAsync(peer.NodeId, manifest.SnapshotId, Groups, kind.SinceSeq, cursor, default),
                manifest.SnapshotId, Groups, Limits);
            Assert.IsNull(page.Problem);
            assembler.Add(page);
            cursor = page.Page!.NextCursor;
        } while (cursor is not null);

        var staged = assembler.Complete(kind, fullReconciliation: true);
        Assert.IsNull(assembler.Problem, "the manifest's hash and counts hold");
        return new DataSyncStagedPull(peer.NodeId, peer.Name, manifest, [staged], reader.Now);
    }

    /// <summary>
    /// What a kind publishes, with keys as placeholders in name order: each record's keys, content, order key and
    /// schema version (the vector, editor and Seq are the device's own and differ run to run).
    /// </summary>
    private static async Task AssertGoldenAsync(string kind, IDataSyncKindCodec codec,
        IReadOnlyList<DataSyncWireRecord> records)
    {
        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal);
        var ordered = records.OrderBy(r => codec.NameOf(codec.Read(r.Content!, Limits).Content!), StringComparer.Ordinal)
            .ToList();
        foreach (var key in ordered.SelectMany(r => r.Keys)) placeholders.TryAdd(key, $"key-{placeholders.Count + 1}");
        var golden = new JsonArray(ordered.Select(r => (JsonNode) new JsonObject
        {
            ["keys"] = new JsonArray(r.Keys.Select(k => (JsonNode) JsonValue.Create(placeholders[k])!).ToArray()),
            ["schemaVersion"] = r.SchemaVersion,
            ["orderKey"] = r.OrderKey,
            ["content"] = r.Content!.DeepClone(),
        }).ToArray());
        var text = golden.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        var schemaVersion = codec.Descriptor.SchemaVersion;

        if (Environment.GetEnvironmentVariable("DATASYNC_WRITE_GOLDENS") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(Path.Combine(dir, "Golden"));
            await File.WriteAllTextAsync(Path.Combine(dir, "Golden", $"{kind}.v{schemaVersion}.json"), text,
                new UTF8Encoding(false));
            return;
        }

        var path = GoldenPath(kind, schemaVersion);
        Assert.IsTrue(File.Exists(path), $"{Path.GetFileName(path)} is missing: write it with DATASYNC_WRITE_GOLDENS.");
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(await File.ReadAllTextAsync(path)), golden),
            $"{kind} publishes other content than {Path.GetFileName(path)}: bump its schemaVersion and add an Upgrade, " +
            "or confirm an additive, ignorable field and write the golden again.");
    }
}
