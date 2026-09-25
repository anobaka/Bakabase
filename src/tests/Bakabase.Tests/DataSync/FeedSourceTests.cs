using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.DataSync.Feed;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.DataSyncFeedFixture;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// The feed source (spec §7.5, content side): snapshots built from exactly what their Refresh published, served as
/// raw canonical pages without the gate; cursors superseded per kind; totals, hashes and the snapshot limits; the
/// head with its counterpart, attention and <c>SeenCounter</c>; the reader log; and the reader-ahead check with the
/// B1(b) settle rule.
/// </summary>
[TestClass]
public class FeedSourceTests
{
    private const string Kind = DataSyncKindIds.CustomProperty;

    #region Snapshots and pages (§7.5.2–§7.5.4)

    [TestMethod]
    public async Task A_snapshot_serves_what_its_Refresh_published_as_raw_canonical_pages()
    {
        var f = await CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Horror"), ("b", "Drama"));
        f.Kind.Add("2", "Mood", ("m", "Calm"));
        var device = f.R.Identity.Device;

        var read = await f.ReadAsync(Query((Kind, 0)));

        var state = await f.StateAsync();
        var manifest = read.Manifest;
        Assert.AreEqual((device.NodeId, device.LibraryEpoch, state.ActorId),
            (manifest.NodeId, manifest.LibraryEpoch, manifest.ActorId));
        Assert.AreEqual((DataSyncContract.Version, DataSyncContract.MinimumPeerVersion),
            (manifest.ContractVersion, manifest.MinimumPeerContract));
        Assert.IsFalse(string.IsNullOrEmpty(manifest.AppVersion));
        Assert.AreEqual(120_000, manifest.ExpiresInMs);
        var kind = manifest.Kinds.Single();
        Assert.AreEqual((Kind, 1, 0L, false, 2, 2, 0, 0L), (kind.Kind, kind.SchemaVersion, kind.SinceSeq,
            kind.CursorSuperseded, kind.RecordCount, kind.LiveCount, kind.TombstoneCount, kind.TombstoneFloorSeq));
        Assert.AreEqual(state.LastSeq, kind.MaxSeq);

        var rows = (await f.R.RowsAsync()).OrderBy(r => r.Seq).ToList();
        var records = read.Kinds[Kind].Records;
        CollectionAssert.AreEqual(rows.Select(r => r.SyncKey).ToList(), read.Kinds[Kind].PrimaryKeys.ToList(),
            "in Seq order");
        foreach (var (record, row) in records.Zip(rows))
        {
            Assert.AreEqual(row.Seq, record["seq"]!.GetValue<long>());
            Assert.AreEqual(row.VvJson, CanonicalJson.Serialize(record["vv"]));
            Assert.AreEqual(device.NodeId, record["origin"]!.GetValue<string>());
            Assert.IsFalse(record["deleted"]!.GetValue<bool>());
            Assert.AreEqual((1, 0), (record["schemaVersion"]!.GetValue<int>(), record["chunks"]!.GetValue<int>()));
            var editor = record["editedBy"]!.AsObject();
            Assert.AreEqual((state.ActorId, device.NodeId, device.Name), (editor["actorId"]!.GetValue<string>(),
                editor["nodeId"]!.GetValue<string>(), editor["name"]!.GetValue<string>()));
            Assert.IsTrue(JsonNode.DeepEquals(f.Published(row.LocalKey), record["content"]),
                "exactly what the codec publishes, nothing local");
            Assert.AreEqual(ContentHash.Of(record["content"]), record["hash"]!.GetValue<string>());
            Assert.IsFalse(record.ContainsKey("orderKey"), "a kind without order");
            Assert.IsFalse(record.ContainsKey("heldAtSource"));
        }

        Assert.AreEqual(DataSyncFeedPageScanner.KindContentHash(records.Select(r => new DataSyncFeedPageRecord(
            r["keys"]![0]!.GetValue<string>(), r["seq"]!.GetValue<long>(), r["hash"]!.GetValue<string>()))), kind.ContentHash);
        foreach (var page in read.Kinds[Kind].Pages)
            CollectionAssert.AreEqual(CanonicalJson.SerializeToUtf8Bytes(JsonNode.Parse(page)), page, "canonical bytes");
    }

    [TestMethod]
    public async Task A_snapshot_is_frozen_when_it_is_made()
    {
        var f = await CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Horror"));
        var manifest = await f.ManifestAsync(Query((Kind, 0)));
        var before = await ReadKindAsync(f.Feed, Reader(), manifest, manifest.Kinds.Single());

        f.Kind.Definitions["1"].Name = "Genres";
        await f.R.RefreshAsync();

        var after = await ReadKindAsync(f.Feed, Reader(), manifest, manifest.Kinds.Single());
        CollectionAssert.AreEqual(before.Pages.Single(), after.Pages.Single(), "pages were precomputed");
        Assert.AreEqual("Genre", after.Records.Single()["content"]!["name"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Pages_are_raw_canonical_bytes_that_federation_json_could_not_carry()
    {
        var f = await CreateAsync();
        var deep = f.Kind.Add("1", "Regions");
        JsonObject nested = new() {["leaf"] = "Tokyo"};
        for (var i = 0; i < 40; i++) nested = new JsonObject {["node"] = nested};
        deep.Extra["tree"] = nested;

        var read = await f.ReadAsync(Query((Kind, 0)));

        var page = read.Kinds[Kind].Pages.Single();
        Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<JsonObject>(page, FederationJson.Options),
            "federation JSON stops at depth 32 (F61)");
        Assert.IsTrue(JsonNode.DeepEquals(f.Published("1"), read.Kinds[Kind].Records.Single()["content"]));
    }

    [TestMethod]
    public async Task An_incremental_snapshot_serves_newer_records_and_reports_the_kinds_totals()
    {
        var f = await CreateAsync();
        f.Kind.Add("1", "Genre");
        f.Kind.Add("2", "Mood");
        f.Kind.Add("3", "Year");
        var first = await f.ManifestAsync(Query((Kind, 0)));
        var cursor = first.Kinds.Single().MaxSeq;
        var deleted = await f.R.RowAsync("2");

        f.Kind.Remove("2");
        f.Clock.Advance(DataSyncFeedSnapshots.ManifestInterval);
        var read = await f.ReadAsync(Query((Kind, cursor)));

        var kind = read.Manifest.Kinds.Single();
        Assert.AreEqual((cursor, false, 1), (kind.SinceSeq, kind.CursorSuperseded, kind.RecordCount));
        Assert.AreEqual((2, 1), (kind.LiveCount, kind.TombstoneCount),
            "an incremental pull whose only record is a tombstone still reports the true live total");
        Assert.IsTrue(kind.MaxSeq > cursor);
        var tombstone = read.Kinds[Kind].Records.Single();
        Assert.AreEqual(deleted.SyncKey, tombstone["keys"]![0]!.GetValue<string>());
        Assert.IsTrue(tombstone["deleted"]!.GetValue<bool>());
        Assert.IsFalse(tombstone.ContainsKey("content") || tombstone.ContainsKey("hash") || tombstone.ContainsKey("orderKey"));
        Assert.AreEqual(DataSyncFeedPageScanner.KindContentHash([
            new DataSyncFeedPageRecord(deleted.SyncKey, kind.MaxSeq, DataSyncFeedPageScanner.TombstoneHash)]), kind.ContentHash);
    }

    [TestMethod]
    public async Task Held_entities_are_served_without_content_and_unsynced_ones_not_at_all()
    {
        var f = await CreateAsync();
        f.Kind.Add("1", "Genre");
        f.Kind.Add("2", "Broken").Unreadable = true;
        f.Kind.Add("3", "Huge").Extra["tooBig"] = true;
        f.Kind.Add("4", "Mine");
        f.Kind.Add("5", "Suspect");
        f.Kind.Add("6", "Gone");
        await f.R.RefreshAsync();
        await f.Store.SetEntityStateAsync(Kind, "4", DataSyncEntitySyncState.LocalOnly, default);
        f.R.Db.ChangeTracker.Clear();
        await f.SetAsync("5", r => r.PublishHeld = true);
        f.Kind.Remove("6");
        await f.R.RefreshAsync();
        await f.R.Db.DataSyncEntities.Where(e => e.LocalKey == "6").ExecuteUpdateAsync(s => s.SetProperty(e => e.TombstoneServed, false));

        var read = await f.ReadAsync(Query((Kind, 0)));

        var byKey = read.Kinds[Kind].Records.ToDictionary(r => r["keys"]![0]!.GetValue<string>());
        async Task<JsonObject> RecordOf(string localKey) => byKey[(await f.R.RowAsync(localKey)).SyncKey];
        Assert.IsTrue((await RecordOf("1")).ContainsKey("content"));
        foreach (var (localKey, reason) in new[]
                 {
                     ("2", DataSyncHeldReason.LocalUnreadable), ("3", DataSyncHeldReason.Invalid),
                     ("5", DataSyncHeldReason.PendingDecision),
                 })
        {
            var record = await RecordOf(localKey);
            Assert.AreEqual(reason.ToString(), record["heldAtSource"]!.GetValue<string>(), localKey);
            Assert.IsFalse(record.ContainsKey("content") || record.ContainsKey("hash"), localKey);
        }

        Assert.AreEqual(4, byKey.Count, "a definition kept on this device only and an unserved tombstone are never served");
        var kind = read.Manifest.Kinds.Single();
        Assert.AreEqual((4, 4, 0), (kind.RecordCount, kind.LiveCount, kind.TombstoneCount));
        Assert.AreEqual(3, read.Kinds[Kind].Records.Count(r =>
            r.ContainsKey("heldAtSource")), "held records are hashed as held");
    }

    [TestMethod]
    public async Task A_record_carries_every_key_of_its_entity_primary_first()
    {
        var f = await CreateAsync();
        f.Kind.Add("1", "Genre");
        await f.R.RefreshAsync();
        var row = await f.R.Db.DataSyncEntities.SingleAsync();
        var aliases = new[] {SyncKey.New().Value, SyncKey.New().Value};
        await f.Services.GetRequiredService<DataSyncIdentityStore>()
            .AddAliasesAsync(row, aliases.Select(a => new SyncKey(a)), null, default);

        var read = await f.ReadAsync(Query((Kind, 0)));

        CollectionAssert.AreEqual(new[] {row.SyncKey}.Concat(aliases.OrderBy(a => a, StringComparer.Ordinal)).ToList(),
            read.Kinds[Kind].Records.Single()["keys"]!.AsArray().Select(k => k!.GetValue<string>()).ToList());
        CollectionAssert.AreEqual(new[] {"p", "a", "b"},
            DataSyncFeedRecords.KeysOf("p", ["b", "a", "c", "p"], 3).ToArray(), "capped at MaxKeysPerEntity, stably");
    }

    [TestMethod]
    public async Task A_kind_with_order_serves_its_order_keys()
    {
        var f = await CreateAsync(hasOrder: true);
        // A fractional index key, as the engine's order planner makes them (§3.7).
        f.R.Detector.Answer = entries => entries.Where(e => e.OrderKey is null).ToDictionary(e => e.LocalKey, _ => "a1");
        f.Kind.Add("1", "Genre");

        var read = await f.ReadAsync(Query((Kind, 0)));

        Assert.AreEqual("a1", read.Kinds[Kind].Records.Single()["orderKey"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Pages_follow_their_cursors_and_refuse_what_the_snapshot_did_not_serve()
    {
        var f = await CreateAsync(limits: DataSyncLimits.Default with {MaxRecordsPerPage = 2}, extensionGroups: true);
        for (var i = 1; i <= 5; i++) f.Kind.Add(i.ToString(), "P" + i);

        var read = await f.ReadAsync(Query((Kind, 0)));

        var kind = read.Kinds[Kind];
        Assert.AreEqual(3, kind.Pages.Count);
        Assert.AreEqual(5, kind.Records.Count);
        CollectionAssert.AreEqual((await f.R.RowsAsync()).OrderBy(r => r.Seq).Select(r => r.SyncKey).ToList(),
            kind.PrimaryKeys.ToList());
        var snapshot = read.Manifest.SnapshotId;
        var cursor = ParsePage(kind.Pages[0])["nextCursor"]!.GetValue<string>();
        CollectionAssert.AreEqual(kind.Pages[1], await f.Feed.GetPageAsync(Reader(), snapshot, Kind, 0, cursor, default));

        Assert.AreEqual((409, "SnapshotMismatch"), Status(await RefusedAsync(() =>
            f.Feed.GetPageAsync(Reader(), snapshot, Kind, 3, null, default))), "another since");
        Assert.AreEqual((409, "SnapshotMismatch"), Status(await RefusedAsync(() =>
            f.Feed.GetPageAsync(Reader(), snapshot, Kind, 0, "p9", default))), "a cursor naming no page");
        Assert.AreEqual((404, "UnknownKind"), Status(await RefusedAsync(() =>
            f.Feed.GetPageAsync(Reader(), snapshot, DataSyncKindIds.ExtensionGroup, 0, null, default))),
            "a kind the reader did not name");
        Assert.AreEqual((410, "SnapshotExpired"), Status(await RefusedAsync(() =>
            f.Feed.GetPageAsync(Reader(), "0123456789abcdef", Kind, 0, null, default))));
        Assert.AreEqual((410, "SnapshotExpired"), Status(await RefusedAsync(() =>
            f.Feed.GetPageAsync(Reader("other-node", "grant-2"), snapshot, Kind, 0, null, default))),
            "another reader's snapshot is none of its business");
    }

    [TestMethod]
    public async Task Large_content_arrives_in_chunks_its_hash_covering_the_reassembled_content()
    {
        var f = await CreateAsync(limits: DataSyncLimits.Default with {MaxChunkBytes = 2_000});
        f.Kind.Add("1", "Tags", Enumerable.Range(0, 200).Select(i => ($"t{i}", $"Tag number {i}")).ToArray());
        f.Kind.Add("2", "Small", ("s", "One"));

        var read = await f.ReadAsync(Query((Kind, 0)));

        var items = read.Kinds[Kind].Pages.SelectMany(p => ParsePage(p)["records"]!.AsArray()).ToList();
        var head = items.Single(i => i!["chunks"]?.GetValue<int>() > 0)!.AsObject();
        Assert.AreEqual(head["chunks"]!.GetValue<int>(), items.Count(i => i!.AsObject().ContainsKey("chunkOf")));
        var records = read.Kinds[Kind].Records;
        Assert.AreEqual(2, records.Count);
        var tags = records.Single(r => r["content"]!["name"]!.GetValue<string>() == "Tags");
        Assert.IsTrue(JsonNode.DeepEquals(f.Published("1"), tags["content"]), "reassembled");
        Assert.AreEqual(ContentHash.Of(tags["content"]), tags["hash"]!.GetValue<string>());
        Assert.AreEqual(DataSyncFeedPageScanner.KindContentHash(records.Select(r => new DataSyncFeedPageRecord(
                r["keys"]![0]!.GetValue<string>(), r["seq"]!.GetValue<long>(), r["hash"]!.GetValue<string>()))),
            read.Manifest.Kinds.Single().ContentHash, "chunks take no part in the kind hash");
    }

    [TestMethod]
    public async Task A_kind_the_source_does_not_publish_is_left_out()
    {
        var f = await CreateAsync();
        f.Kind.Add("1", "Genre");

        var manifest = await f.ManifestAsync(Query((Kind, 0), (DataSyncKindIds.ExtensionGroup, 0), ("futureKind", 0)));

        CollectionAssert.AreEqual(new[] {Kind}, manifest.Kinds.Select(k => k.Kind).ToArray());
    }

    #endregion

    #region Snapshot limits (§7.5.2)

    [TestMethod]
    public async Task A_snapshot_lives_two_minutes_sliding_on_each_page_read_and_a_new_manifest_replaces_it()
    {
        var f = await CreateAsync();
        f.Kind.Add("1", "Genre");
        var first = await f.ManifestAsync(Query((Kind, 0)));

        Task<byte[]> Page(DataSyncFeedManifest m) => f.Feed.GetPageAsync(Reader(), m.SnapshotId, Kind, 0, null, default);
        f.Clock.Advance(TimeSpan.FromSeconds(90));
        await Page(first);
        f.Clock.Advance(TimeSpan.FromSeconds(90));
        await Page(first);
        f.Clock.Advance(TimeSpan.FromSeconds(119));
        await Page(first);

        var second = await f.ManifestAsync(Query((Kind, 0)));
        Assert.AreNotEqual(first.SnapshotId, second.SnapshotId);
        Assert.AreEqual("SnapshotExpired", (await RefusedAsync(() => Page(first))).Code, "one snapshot per reader grant");
        await Page(second);

        f.Clock.Advance(DataSyncFeedSnapshots.Ttl);
        var expired = await RefusedAsync(() => Page(second));
        Assert.AreEqual((410, "SnapshotExpired", true), (expired.Status, expired.Code, expired.Retryable));
        Assert.AreEqual(0, f.Snapshots.TotalBytes);
    }

    [TestMethod]
    public async Task One_manifest_per_reader_every_ten_seconds()
    {
        var f = await CreateAsync();
        f.Kind.Add("1", "Genre");
        await f.ManifestAsync(Query((Kind, 0)));
        var writes = f.Writer.Calls;

        f.Clock.Advance(TimeSpan.FromSeconds(4));
        var refused = await RefusedAsync(() => f.ManifestAsync(Query((Kind, 0))));
        Assert.AreEqual((429, "TooManySnapshots", true, (int?) 6), (refused.Status, refused.Code, refused.Retryable,
            refused.RetryAfterSeconds));
        Assert.AreEqual(writes, f.Writer.Calls, "refused before any work");

        await f.ManifestAsync(Query((Kind, 0)), Reader("reader-node-2", "grant-2"));
        f.Clock.Advance(TimeSpan.FromSeconds(6));
        await f.ManifestAsync(Query((Kind, 0)));
    }

    [TestMethod]
    public async Task A_snapshot_too_large_is_refused_for_good_and_too_many_bytes_in_snapshots_is_busy()
    {
        var f = await CreateAsync();
        for (var i = 1; i <= 20; i++) f.Kind.Add(i.ToString(), "Property " + i, ("a", "Option " + i));
        await f.ManifestAsync(Query((Kind, 0)), Reader("probe", "grant-0"));
        var size = f.Snapshots.Find("grant-0")!.Bytes;

        var tight = Source(f, DataSyncLimits.Default with {MaxSnapshotBytes = size - 1}, new DataSyncFeedSnapshots(f.Clock));
        var tooLarge = await RefusedAsync(() => tight.CreateSnapshotAsync(Reader(), Query((Kind, 0)), default));
        Assert.AreEqual((413, "SnapshotTooLarge", false), (tooLarge.Status, tooLarge.Code, tooLarge.Retryable));

        var snapshots = new DataSyncFeedSnapshots(f.Clock);
        var shared = Source(f, DataSyncLimits.Default with {MaxSnapshotBytesTotal = size + size / 2}, snapshots);
        await shared.CreateSnapshotAsync(Reader(), Query((Kind, 0)), default);
        var busy = await RefusedAsync(() =>
            shared.CreateSnapshotAsync(Reader("reader-node-2", "grant-2"), Query((Kind, 0)), default));
        Assert.AreEqual((503, "Busy", true, (int?) 30), (busy.Status, busy.Code, busy.Retryable, busy.RetryAfterSeconds));

        f.Clock.Advance(DataSyncFeedSnapshots.Ttl);
        await shared.CreateSnapshotAsync(Reader("reader-node-2", "grant-2"), Query((Kind, 0)), default);
    }

    [TestMethod]
    public async Task Pages_never_take_the_gate_and_a_manifest_waits_for_it_then_answers_busy()
    {
        var f = await CreateAsync();
        f.Kind.Add("1", "Genre");
        var manifest = await f.ManifestAsync(Query((Kind, 0)));
        var impatient = Source(f, DataSyncLimits.Default, new DataSyncFeedSnapshots(f.Clock),
            TimeSpan.FromMilliseconds(50));

        using (await f.R.Gate.EnterAsync(null, default))
        {
            var page = await f.Feed.GetPageAsync(Reader(), manifest.SnapshotId, Kind, 0, null, default)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(page.Length > 0, "an apply holding the gate never fails a page read");

            var busy = await RefusedAsync(() => impatient.CreateSnapshotAsync(Reader(), Query((Kind, 0)), default));
            Assert.AreEqual((503, "Busy", true), (busy.Status, busy.Code, busy.Retryable));
        }
    }

    [TestMethod]
    public async Task While_the_actor_is_unverified_manifests_are_busy_and_heads_still_answer()
    {
        var f = await CreateAsync(verified: false);
        f.Kind.Add("1", "Genre");
        await f.R.LinkAsync("peer-1");

        var busy = await RefusedAsync(() => f.ManifestAsync(Query((Kind, 0))));
        Assert.AreEqual((503, "Busy", (int?) 30), (busy.Status, busy.Code, busy.RetryAfterSeconds));
        Assert.AreEqual(0, f.Writer.Calls);

        var head = await f.HeadAsync(Query((Kind, 0)));
        Assert.AreEqual(0, head.Seq, "Refresh never runs while the actor is unverified (§5.6)");
        Assert.AreEqual(Kind, head.Kinds.Single().Kind);
        Assert.AreEqual(0, await f.R.Db.DataSyncEntities.CountAsync());

        f.R.Guard.MarkVerified();
        await f.ManifestAsync(Query((Kind, 0)));
    }

    #endregion

    #region Head (§7.5.1)

    [TestMethod]
    public async Task A_head_tells_the_sequence_heads_the_counterpart_and_the_attention_counts()
    {
        var f = await CreateAsync(extensionGroups: true);
        f.Kind.Add("1", "Genre");
        f.Groups!.Add("g", "Images");
        f.Kind.MemoryCodec.ComparisonFormVersion = 3;
        await f.Store.AddLinkAsync(new DataSyncLinkDbModel
        {
            PeerNodeId = ReaderNode, PeerName = "PC-2", Mode = DataSyncLinkMode.Follow, State = DataSyncLinkState.Active,
            Initiator = DataSyncLinkInitiator.ThisDevice, KindsJson = $"[\"{Kind}\"]",
            FirstContactCompletedAtUtc = f.R.Now,
        }, default);
        var paused = await f.R.LinkAsync("peer-paused", DataSyncLinkState.Paused, DataSyncPauseReason.PeerReset);
        await f.R.LinkAsync("peer-review", DataSyncLinkState.AwaitingReview);
        await f.Store.UpsertItemsAsync(paused.Id, "peer-paused",
            [DataSyncStoreFixture.Draft(Kind, SyncKey.New().Value, DataSyncInboxItemType.FieldConflict,
                DataSyncInboxItemOrigin.Merger, "name")], f.R.Now, default);

        var head = await f.HeadAsync(Query((Kind, 0)));

        var state = await f.StateAsync();
        var device = f.R.Identity.Device;
        Assert.AreEqual((device.NodeId, device.LibraryEpoch, state.ActorId, state.LastSeq),
            (head.NodeId, head.LibraryEpoch, head.ActorId, head.Seq));
        Assert.AreEqual((DataSyncContract.Version, DataSyncContract.MinimumPeerVersion),
            (head.ContractVersion, head.MinimumPeerContract));
        Assert.IsFalse(string.IsNullOrEmpty(head.AppVersion));
        CollectionAssert.AreEqual(new[] {DataSyncKindIds.ExtensionGroup, Kind}, head.Kinds.Select(k => k.Kind).ToArray(),
            "every kind this device publishes, whether or not the reader named it");
        var maxSeqs = (await f.R.Db.DataSyncEntities.AsNoTracking().ToListAsync())
            .GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Max(e => e.Seq));
        foreach (var kind in head.Kinds)
        {
            Assert.AreEqual(maxSeqs[kind.Kind], kind.MaxSeq, kind.Kind);
            Assert.AreEqual((1, false), (kind.SchemaVersion, kind.CursorSuperseded), kind.Kind);
        }

        Assert.AreEqual(3, head.Kinds.Single(k => k.Kind == Kind).ComparisonFormVersion, "the source codec's (N5)");
        Assert.AreEqual(("follow", true), (head.Counterpart!.Mode, head.Counterpart.FirstContactCompleted),
            "this device's own link to the reader (N6)");
        CollectionAssert.AreEqual(new[] {Kind}, head.Counterpart.Kinds.ToArray());
        Assert.AreEqual(new DataSyncSourceAttention(false, 1, 1, false, 1), head.Attention);
        Assert.IsNull(head.SeenCounter, "the reader declared no actor");

        var stranger = await f.HeadAsync(Query((Kind, 0)), Reader("stranger", "grant-9"));
        Assert.IsNull(stranger.Counterpart, "the counterpart is told only to the peer it is about");
    }

    [TestMethod]
    public async Task A_head_uses_a_Refresh_at_most_five_seconds_old()
    {
        var f = await CreateAsync();
        f.Kind.Add("1", "Genre");
        var first = await f.HeadAsync(Query((Kind, 0)));
        Assert.IsTrue(first.Seq > 0);

        f.Kind.Definitions["1"].Name = "Genres";
        f.Clock.Advance(TimeSpan.FromSeconds(4));
        Assert.AreEqual(first.Seq, (await f.HeadAsync(Query((Kind, 0)))).Seq, "coalesced into the recent Refresh");

        f.Clock.Advance(TimeSpan.FromSeconds(2));
        var later = await f.HeadAsync(Query((Kind, 0)));
        Assert.IsTrue(later.Seq > first.Seq);
        Assert.AreEqual(later.Seq, later.Kinds.Single().MaxSeq);
    }

    [TestMethod]
    public async Task Heads_and_manifests_are_logged_as_reads()
    {
        var f = await CreateAsync();
        f.Kind.Add("1", "Genre");
        var head = await f.HeadAsync(Query((Kind, 0)) with {Mode = "follow", ReaderState = "needsYou:2"});

        var reader = (await f.Store.GetReadersAsync(default)).Single();
        Assert.AreEqual((ReaderNode, "PC-2", "follow", "needsYou:2", head.Seq),
            (reader.NodeId, reader.Name, reader.Mode, reader.State, reader.LastSeqServed));
        Assert.AreEqual(f.R.Now, reader.LastReadAtUtc);

        f.Clock.Advance(TimeSpan.FromMinutes(1));
        await f.ManifestAsync(Query((Kind, 0)) with {Mode = "follow", ReaderState = "ok"});
        reader = (await f.Store.GetReadersAsync(default)).Single();
        Assert.AreEqual(("ok", f.R.Now), (reader.State, reader.LastReadAtUtc));
    }

    #endregion

    #region SeenCounter (§7.5.1 step 4)

    [TestMethod]
    public async Task SeenCounter_is_the_highest_counter_of_the_readers_actor_in_any_stored_vector()
    {
        var f = await CreateAsync();
        await f.R.CheckAsync();
        var identity = f.Services.GetRequiredService<DataSyncIdentityStore>();

        async Task<long?> Seen(string? actor = ReaderActor) => (await f.HeadAsync(Query(actor, (Kind, 0)))).SeenCounter;

        Assert.IsNull(await Seen(), "no vector names it yet");

        // Maintained on every save once built: entities, tombstones, bases, pending records.
        var entity = await identity.InsertFreshAsync(Row("e1", Vv((ReaderActor, 5), (DataSyncStoreFixture.ActorA, 8))), default);
        Assert.AreEqual(5, await Seen());
        var doomed = await identity.InsertFreshAsync(Row("e2", Vv((ReaderActor, 3))), default);
        await identity.TombstoneAsync(doomed, new DataSyncTombstoneWrite(Vv((ReaderActor, 6)),
            DataSyncTombstoneKind.Deleted, true, null), default);
        Assert.AreEqual(6, await Seen());
        var link = await f.R.LinkAsync("peer-b");
        await f.Store.UpsertBasesAsync(link.Id, [new DataSyncBaseUpdate(Kind, new SyncKey(entity.SyncKey),
            DataSyncBaseState.Normal, null, DataSyncStoreFixture.Record([entity.SyncKey], Vv((ReaderActor, 7))), null, null,
            false)], default);
        Assert.AreEqual(7, await Seen());
        var pendingKey = SyncKey.New();
        await f.Store.UpsertBasesAsync(link.Id, [new DataSyncBaseUpdate(Kind, pendingKey, DataSyncBaseState.Unbound, null,
            null, null, DataSyncStoreFixture.Pending(DataSyncStoreFixture.Record([pendingKey.Value], Vv((ReaderActor, 9))),
                DataSyncPendingReason.Conflict), false)], default);
        Assert.AreEqual(9, await Seen());
        Assert.AreEqual(8, await Seen(DataSyncStoreFixture.ActorA));
        Assert.IsNull(await Seen("2222222222222222"));
        Assert.IsNull(await Seen(null));

        // Rebuilt from the database at a start; never lowered while the process runs.
        var counters = f.Services.GetRequiredService<DataSyncSeenCounters>();
        counters.Reset();
        Assert.AreEqual(9, await Seen());
        await f.Store.DeleteLinkAsync(link.Id, default);
        Assert.AreEqual(9, await Seen(), "a counter once seen stays seen");
        counters.Reset();
        Assert.AreEqual(6, await Seen(), "the tombstone's, after the bases went with their link");
    }

    [TestMethod]
    public async Task Observing_a_save_never_fails_it_and_a_failed_save_observes_nothing()
    {
        var f = await CreateAsync();
        await f.R.CheckAsync();
        var counters = f.Services.GetRequiredService<DataSyncSeenCounters>();
        Assert.IsNull(await counters.GetAsync(ReaderActor, default));

        var db = f.Store.Db;
        db.DataSyncEntities.Add(new DataSyncEntityDbModel
        {
            Kind = Kind, LocalKey = "x", SyncKey = "not-a-key", OriginNodeId = "n", LocalHash = "h", SharedHash = "s",
            VvJson = Vv((ReaderActor, 4)).ToCanonicalString(), Seq = 1,
        });
        db.DataSyncEntities.Add(new DataSyncEntityDbModel
        {
            Kind = Kind, LocalKey = "y", SyncKey = "not-a-key", OriginNodeId = "n", LocalHash = "h", SharedHash = "s",
            VvJson = Vv((ReaderActor, 4)).ToCanonicalString(), Seq = 2,
        });
        await Assert.ThrowsExceptionAsync<DbUpdateException>(() => db.SaveChangesAsync(), "the unique key index");
        db.ChangeTracker.Clear();
        Assert.IsNull(await counters.GetAsync(ReaderActor, default));

        // A vector that does not parse fails its own readers, never the save that writes it.
        var corrupted = new DataSyncPeerBaseDbModel {LinkId = 1, Kind = Kind, SyncKey = SyncKey.New().Value, VvJson = "not json"};
        db.DataSyncPeerBases.Add(corrupted);
        await db.SaveChangesAsync();
        Assert.IsNull(await counters.GetAsync(ReaderActor, default));
        db.DataSyncPeerBases.Remove(corrupted);
        await db.SaveChangesAsync();
    }

    #endregion

    #region The reader-ahead check (§7.5.1 step 1, §5.6, gate fix B1(b))

    [TestMethod]
    public async Task A_reader_ahead_is_reported_before_any_counter_and_served_from_0_until_it_reads_in_step()
    {
        var f = await CreateAsync();
        f.Kind.Add("1", "Genre");
        await f.R.RefreshAsync();
        await f.R.LinkAsync("peer-x");
        var before = await f.StateAsync();
        var lost = before.LastSeq + 5;
        f.Kind.Definitions["1"].Name = "Genres";

        // The reader has seen sequence numbers this database never issued.
        var head = await f.HeadAsync(Query((Kind, lost)));
        var state = await f.StateAsync();
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, state.RestoreReason);
        Assert.AreEqual(DataSyncLinkState.Paused, (await f.R.LinkRowAsync("peer-x")).State);
        Assert.AreNotEqual(before.ActorId, state.ActorId);
        Assert.AreEqual(state.ActorId, head.ActorId);
        Assert.IsTrue(head.Kinds.All(k => k.CursorSuperseded));
        var vv = DataSyncVersionVector.ParseStored((await f.R.RowAsync("1")).VvJson);
        Assert.AreEqual(1, vv.Counters[before.ActorId], "no counter was issued under the actor the restore lost");
        Assert.AreEqual(1, vv.Counters[state.ActorId], "the pending edit became a revision of the new actor");
        var evidence = (await f.EvidenceAsync()).Single();
        Assert.AreEqual((DataSyncRestoreEvidence.Reader, ReaderNode, (bool?) null), (evidence.Source, evidence.NodeId, evidence.Settled));

        // Recorded: never reported again while it has not read in step; manifests wait for the restore choice.
        Assert.IsTrue((await f.HeadAsync(Query((Kind, lost)))).Kinds.All(k => k.CursorSuperseded));
        Assert.AreEqual(state.ActorId, (await f.StateAsync()).ActorId, "not a second detection");
        var pending = await RefusedAsync(() => f.ManifestAsync(Query((Kind, lost))));
        Assert.AreEqual((409, "SourceRestorePending", true, (int?) 3600),
            (pending.Status, pending.Code, pending.Retryable, pending.RetryAfterSeconds));

        await ChooseAsync(f);

        // Its cursor is still ahead: served from 0, not settled.
        var fromZero = await f.ReadAsync(Query((Kind, lost)));
        var kind = fromZero.Manifest.Kinds.Single();
        Assert.AreEqual((true, 0L, 1), (kind.CursorSuperseded, kind.SinceSeq, kind.RecordCount));
        Assert.IsNull((await f.EvidenceAsync()).Single().Settled);

        // In step now, but a head never settles: a head alone cannot tell a lowered cursor from a stale one that this
        // device's new changes overtook.
        var cursor = kind.MaxSeq;
        Assert.IsTrue((await f.HeadAsync(Query((Kind, cursor)))).Kinds.All(k => k.CursorSuperseded));
        Assert.IsNull((await f.EvidenceAsync()).Single().Settled);

        // The first manifest read in step is still served from 0, and settles it.
        f.Clock.Advance(DataSyncFeedSnapshots.ManifestInterval);
        var settling = (await f.ManifestAsync(Query((Kind, cursor)))).Kinds.Single();
        Assert.AreEqual((true, 0L), (settling.CursorSuperseded, settling.SinceSeq));
        Assert.IsTrue((await f.EvidenceAsync()).Single().Settled);
        Assert.IsFalse((await f.HeadAsync(Query((Kind, cursor)))).Kinds.Single().CursorSuperseded);
        f.Clock.Advance(DataSyncFeedSnapshots.ManifestInterval);
        Assert.AreEqual(cursor, (await f.ManifestAsync(Query((Kind, cursor)))).Kinds.Single().SinceSeq);

        // Ahead again later: a new detection.
        var settledActor = (await f.StateAsync()).ActorId;
        await f.HeadAsync(Query((Kind, (await f.StateAsync()).LastSeq + 1)));
        state = await f.StateAsync();
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, state.RestoreReason);
        Assert.AreNotEqual(settledActor, state.ActorId);
    }

    [TestMethod]
    public async Task A_manifest_from_a_reader_ahead_is_reported_first_and_waits_for_the_restore_choice()
    {
        var f = await CreateAsync();
        f.Kind.Add("1", "Genre");
        await f.R.RefreshAsync();
        var before = await f.StateAsync();

        var pending = await RefusedAsync(() => f.ManifestAsync(Query((Kind, before.LastSeq + 1))));

        Assert.AreEqual("SourceRestorePending", pending.Code);
        var after = await f.StateAsync();
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected, after.RestoreReason);
        Assert.AreNotEqual(before.ActorId, after.ActorId);
        Assert.AreEqual(0, f.Writer.Calls);
    }

    [TestMethod]
    public async Task A_reader_in_step_is_never_reported()
    {
        var f = await CreateAsync();
        f.Kind.Add("1", "Genre");
        await f.R.RefreshAsync();
        var state = await f.StateAsync();

        var head = await f.HeadAsync(Query((Kind, state.LastSeq), ("otherKind", state.LastSeq)));

        Assert.IsFalse(head.Kinds.Single().CursorSuperseded);
        Assert.IsNull((await f.StateAsync()).RestoreReason);
        Assert.AreEqual(0, (await f.EvidenceAsync()).Count);
    }

    /// <summary>The restore choice (E's task body) ends the pending restore; the evidence stays.</summary>
    private static async Task ChooseAsync(DataSyncFeedFixture f)
    {
        // The test's own context may still track the row as it was before the detection.
        f.R.Db.ChangeTracker.Clear();
        var state = await f.StateAsync();
        state.RestoreReason = null;
        state.RestoreLinkId = null;
        state.RestoreDetail = null;
        await f.Store.SaveLocalStateAsync(state, default);
        f.R.Db.ChangeTracker.Clear();
    }

    #endregion

    private static (int, string) Status(DataSyncFeedException e) => (e.Status, e.Code);

    private static DataSyncFeedSource Source(DataSyncFeedFixture f, DataSyncLimits limits,
        DataSyncFeedSnapshots snapshots, TimeSpan? gateTimeout = null)
    {
        var sp = f.Services;
        return new DataSyncFeedSource(sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<DataSyncGate>(),
            sp.GetRequiredService<DataSyncActorGuard>(), sp.GetRequiredService<DataSyncRefreshCoordinator>(), snapshots,
            sp.GetRequiredService<DataSyncSeenCounters>(), sp.GetRequiredService<IDataSyncFeedPageWriter>(), limits,
            f.Clock)
        {
            GateTimeout = gateTimeout ?? DataSyncGate.RequestTimeout,
        };
    }

    private static DataSyncVersionVector Vv(params (string Actor, long Counter)[] counters) =>
        DataSyncStoreFixture.Vv(counters);

    private static DataSyncEntityDbModel Row(string localKey, DataSyncVersionVector vv) => new()
    {
        Kind = Kind, LocalKey = localKey, OriginNodeId = "origin-node", LocalHash = "local:" + localKey,
        SharedHash = "shared:" + localKey, VvJson = vv.ToCanonicalString(), State = DataSyncEntitySyncState.Synced,
    };
}
