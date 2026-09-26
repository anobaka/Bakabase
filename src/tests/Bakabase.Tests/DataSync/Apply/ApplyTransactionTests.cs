using System.Text.Json;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.Apply.DataSyncApplyFixture;

namespace Bakabase.Tests.DataSync.Apply;

/// <summary>
/// The apply transaction (§8.10.2, §8.10.5; v3.1 H6): a failure at any step rolls everything back — the side tables
/// and the service caches equal the state before the apply, and the link records <c>ApplyFailed</c>; chunks commit on
/// their own; an item that changed during the apply waits as a <c>Retry</c> record; invalid local children survive an
/// update; a regression anomaly takes Refresh's revisions back with it; a changed actor is checked and retried once.
/// </summary>
[TestClass]
public class ApplyTransactionTests
{
    private static async Task<(DataSyncApplyFixture F, DataSyncFailureInjector Injector)> GroupsAsync()
    {
        var injector = new DataSyncFailureInjector();
        var f = await CreateAsync(s => FailingDataSyncKind.AddExtensionGroups(s, injector), extensionGroups: false);
        return (f, injector);
    }

    /// <summary>
    /// Everything an apply writes, read through a fresh context (the side tables, the local state, the extension group
    /// table) and through the service's cache; the link row's error columns are the failure's own.
    /// </summary>
    private static async Task<string> SnapshotAsync(DataSyncApplyFixture f)
    {
        var db = f.NewDb();
        await using var scope = f.Services.CreateAsyncScope();
        var cached = (await scope.ServiceProvider.GetRequiredService<IExtensionGroupService>().GetAll())
            .Select(g => new { g.Id, g.Name, Extensions = g.Extensions?.OrderBy(e => e).ToList() }).OrderBy(g => g.Id);
        return JsonSerializer.Serialize(new
        {
            Entities = await db.DataSyncEntities.AsNoTracking().OrderBy(e => e.Id).ToListAsync(),
            Aliases = await db.DataSyncKeyAliases.AsNoTracking().OrderBy(a => a.Id).ToListAsync(),
            Bases = await db.DataSyncPeerBases.AsNoTracking().OrderBy(b => b.Id).ToListAsync(),
            Items = await db.DataSyncInboxItems.AsNoTracking().OrderBy(i => i.Id).ToListAsync(),
            Logs = await db.DataSyncApplyLogs.AsNoTracking().OrderBy(l => l.Id).ToListAsync(),
            State = await db.DataSyncLocalStates.AsNoTracking().ToListAsync(),
            Rows = await db.ExtensionGroups.AsNoTracking().OrderBy(g => g.Id).ToListAsync(),
            Cursors = await db.DataSyncLinks.AsNoTracking().OrderBy(l => l.Id).Select(l => l.CursorsJson).ToListAsync(),
            Cached = cached,
        });
    }

    [TestMethod]
    [DataRow("operation")]
    [DataRow("entity")]
    [DataRow("base")]
    [DataRow("pending")]
    [DataRow("inbox")]
    [DataRow("history")]
    [DataRow("link")]
    public async Task A_failure_at_any_step_leaves_the_database_and_the_caches_as_they_were(string step)
    {
        var (f, injector) = await GroupsAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer);
        var video = SyncKey.New().Value;
        var v1 = peer.Next();
        await f.ApplyAsync(link, peer, (Groups, peer.Record([video], v1, GroupContent("Video", ".mkv"))));
        await f.AddGroupAsync("Docs", ".pdf");
        await f.RefreshAsync();

        var pull = f.Pull(peer,
            (Groups, peer.Record([SyncKey.New().Value], peer.Next(), GroupContent("Audio", ".mp3"))),
            (Groups, peer.Record([SyncKey.New().Value], peer.Next(), GroupContent("Images", ".png"))),
            (Groups, peer.Record([video], peer.Next(v1), GroupContent("Video", ".mkv", ".mp4"))),
            (Groups, peer.Record([SyncKey.New().Value], peer.Next(), GroupContent("Docs", ".doc"))));
        var before = await SnapshotAsync(f);

        injector.FailOperation = step == "operation" ? (_, n) => n == 2 : null;
        injector.FailSave = step switch
        {
            "entity" => t => DataSyncFailureInjector.Saves<DataSyncEntityDbModel>(t, e => e.CreatedBySync && e.Id == 0),
            "base" => t => DataSyncFailureInjector.Saves<DataSyncPeerBaseDbModel>(t, b => b.PendingReason == null),
            "pending" => t => DataSyncFailureInjector.Saves<DataSyncPeerBaseDbModel>(t, b => b.PendingReason != null),
            "inbox" => t => DataSyncFailureInjector.Saves<DataSyncInboxItemDbModel>(t),
            "history" => t => DataSyncFailureInjector.Saves<DataSyncApplyLogDbModel>(t),
            // The cursor, written last (§8.10.2); the failure record that follows it writes no cursor.
            "link" => t => t.Entries<DataSyncLinkDbModel>().Any(e =>
                e.State == EntityState.Modified && e.Property(l => l.CursorsJson).IsModified),
            _ => null,
        };
        var outcome = await f.ApplyAsync(link, peer, pull);
        injector.FailOperation = null;
        injector.FailSave = null;

        Assert.AreEqual((0, (int?) null), (outcome.Applied, outcome.ApplyLogId));
        Assert.AreEqual(DataSyncApplyRunner.ApplyFailedCode, (await f.LinkRowAsync(link.Id)).LastErrorCode,
            "the injected failure was met");
        Assert.AreEqual(before, await SnapshotAsync(f), "rolled back, caches reset (§8.10.5)");

        var retried = await f.ApplyAsync(link, peer, pull);
        Assert.AreEqual(3, retried.Applied, "two creates and an update; Docs is a question");
        Assert.AreEqual(1, (await f.OpenItemsAsync()).Count(i => i.Type == DataSyncInboxItemType.LinkSuggestion));
        Assert.IsNull((await f.LinkRowAsync(link.Id)).LastErrorCode, "cleared on success");
    }

    /// <summary>
    /// One adapter batch writes a create (AddRange: saved, and in the service's cache, at once) and then an update
    /// (Put). A failed save of the update, or a stop between the two, rolls the create back: the kind counted as
    /// touched before its first write, so its cache is dropped too, and nothing only the cache still holds is ever
    /// published (§8.10.5).
    /// </summary>
    [TestMethod]
    [DataRow("save")]
    [DataRow("stop")]
    public async Task A_batch_that_fails_between_two_of_its_writes_leaves_no_cache_ahead_of_the_database(string how)
    {
        var (f, injector) = await GroupsAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer);
        var video = SyncKey.New().Value;
        var v1 = peer.Next();
        await f.ApplyAsync(link, peer, (Groups, peer.Record([video], v1, GroupContent("Video", ".mkv"))));
        await f.RefreshAsync();
        var before = await SnapshotAsync(f);
        var rows = (await f.RowsAsync(Groups)).Count;

        using var cts = new CancellationTokenSource();
        var created = false;
        injector.FailSave = t =>
        {
            if (t.Entries<Bakabase.Abstractions.Models.Db.ExtensionGroupDbModel>().Any(e => e.State == EntityState.Added))
            {
                created = true;
                if (how == "stop") cts.Cancel();
                return false;
            }

            return how == "save" && created && t.Entries<Bakabase.Abstractions.Models.Db.ExtensionGroupDbModel>()
                .Any(e => e.State == EntityState.Modified);
        };
        var pull = f.Pull(peer,
            (Groups, peer.Record([SyncKey.New().Value], peer.Next(), GroupContent("Audio", ".mp3"))),
            (Groups, peer.Record([video], peer.Next(v1), GroupContent("Video", ".mkv", ".mp4"))));

        if (how == "stop")
        {
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
                f.Runner.RunAutoSyncAsync(Context(link, peer), pull, f.Args(ct: cts.Token)));
        }
        else
        {
            var outcome = await f.Runner.RunAutoSyncAsync(Context(link, peer), pull, f.Args());
            Assert.AreEqual(0, outcome.Applied);
            Assert.AreEqual(DataSyncApplyRunner.ApplyFailedCode, (await f.LinkRowAsync(link.Id)).LastErrorCode);
        }

        injector.FailSave = null;
        Assert.IsTrue(created, "the create was written before the failure");
        Assert.AreEqual(before, await SnapshotAsync(f), "the service's cache matches the database again");
        await f.RefreshAsync();
        Assert.AreEqual(rows, (await f.RowsAsync(Groups)).Count, "Refresh finds no definition that only the cache had");
    }

    /// <summary>
    /// The savepoint Convert writes phase one under (§8.5.6): taking it back undoes the rows and the service's cache
    /// written since, and the transaction goes on.
    /// </summary>
    [TestMethod]
    public async Task A_savepoint_rollback_takes_back_the_rows_and_the_caches_written_since()
    {
        var (f, _) = await GroupsAsync();
        await f.AddGroupAsync("Docs", ".pdf");
        await f.RefreshAsync();
        var before = await SnapshotAsync(f);

        await using (var s = await DataSyncApplySession.OpenAsync(f.Services.GetRequiredService<IServiceScopeFactory>(), default))
        {
            await s.BeginAsync(default);
            await s.LoadStateAsync(default);
            await s.SavepointAsync("phase-one", default);
            await s.Writer(Groups).ApplyAsync(new ApplyBatch(Groups,
            [
                new CreateEntityOperation("item-1", new EntityKeys([SyncKey.New()]), "node-x", 0,
                    GroupContent("Audio", ".mp3")),
            ]), default);
            Assert.AreEqual(2, (await s.Services.GetRequiredService<IExtensionGroupService>().GetAll()).Length);

            await s.RollbackToSavepointAsync("phase-one");
            Assert.AreEqual(1, (await s.Services.GetRequiredService<IExtensionGroupService>().GetAll()).Length,
                "the service reads the database again");
            await s.CommitAsync(default);
        }

        Assert.AreEqual(before, await SnapshotAsync(f));
    }

    /// <summary>
    /// A rollback to a savepoint keeps what the transaction wrote before it, which no other connection sees before the
    /// commit. A scope that loads the kind's cache in between (from the committed rows) leaves nothing stale behind:
    /// after the commit the service serves what the database holds, and the next Refresh finds nothing to publish.
    /// </summary>
    [TestMethod]
    public async Task A_cache_loaded_between_a_savepoint_rollback_and_the_commit_is_dropped_by_the_commit()
    {
        var (f, _) = await GroupsAsync();
        var video = await f.AddGroupAsync("Video", ".mkv");
        await f.RefreshAsync();
        var id = int.Parse(video, System.Globalization.CultureInfo.InvariantCulture);
        var scopes = f.Services.GetRequiredService<IServiceScopeFactory>();

        await using (var s = await DataSyncApplySession.OpenAsync(scopes, default))
        {
            using var lease = await f.Gate.EnterAsync(null, default);
            await s.BeginAsync(default);
            var groups = s.Services.GetRequiredService<IExtensionGroupService>();
            s.Writer(Groups);
            await groups.Put(id, new Bakabase.Abstractions.Models.Input.ExtensionGroupPutInputModel("Movies", [".mkv"]));
            // Recorded in the same transaction, as an apply records the hashes of what it wrote.
            await s.Refresher.RefreshAsync(lease, [Groups], false, default);
            await s.LoadStateAsync(default);
            await s.SavepointAsync("phase-one", default);
            await groups.Put(id, new Bakabase.Abstractions.Models.Input.ExtensionGroupPutInputModel("Clips", [".mkv"]));

            await s.RollbackToSavepointAsync("phase-one");
            Assert.AreEqual("Movies", (await groups.Get(id)).Name, "the session serves what its transaction keeps");

            // Another scope drops and loads the cache before the commit: it sees only the committed "Video".
            await using (var other = scopes.CreateAsyncScope())
            {
                other.ServiceProvider.GetServices<IDataSyncKind>().Single(k => k.Codec.Descriptor.Kind == Groups)
                    .ResetCaches();
                Assert.AreEqual("Video",
                    (await other.ServiceProvider.GetRequiredService<IExtensionGroupService>().Get(id)).Name);
            }

            await s.CommitAsync(default);
            await f.Services.GetRequiredService<DataSyncActorWatermarkFile>().WriteAsync(await f.StateAsync(), default);
        }

        Assert.AreEqual("Movies", (await f.NewDb().ExtensionGroups.AsNoTracking().SingleAsync(g => g.Id == id)).Name);
        Assert.AreEqual("Movies", (await f.GroupAsync(video))!.Name, "the service serves what was committed");
        var committed = await f.RowAsync(video, Groups);
        await f.RefreshAsync();
        var refreshed = await f.RowAsync(video, Groups);
        Assert.AreEqual((committed.Seq, committed.VvJson, committed.RawHash),
            (refreshed.Seq, refreshed.VvJson, refreshed.RawHash), "no revision of stale content");
    }

    /// <summary>
    /// The local state a review page reads (<c>GET /data-sync/reviews/{id}</c>) is read in a deferred transaction: it
    /// answers while another connection holds SQLite's write lock, and so never holds up a writer either.
    /// </summary>
    [TestMethod]
    public async Task The_local_state_a_review_reads_does_not_wait_for_a_writer()
    {
        var (f, _) = await GroupsAsync();
        var docs = await f.AddGroupAsync("Docs", ".pdf");
        await f.RefreshAsync();
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(f.NewDb().Database.GetConnectionString())
        {
            Pooling = false,
        };
        await using var writer = new Microsoft.Data.Sqlite.SqliteConnection(builder.ToString());
        await writer.OpenAsync();
        await using (var begin = writer.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE";
            await begin.ExecuteNonQueryAsync();
        }

        await using var scope = f.Services.CreateAsyncScope();
        var read = scope.ServiceProvider.GetRequiredService<IDataSyncLocalStateReader>().ReadAsync([Groups], default);
        var first = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(5)));
        await writer.CloseAsync();

        Assert.AreSame(read, first, "the read waited for the writer");
        Assert.IsTrue((await read)[Groups].Entities.Any(e => e.LocalKey == docs));
        Assert.IsNull(scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().Database.CurrentTransaction,
            "the read transaction is gone");
    }

    [TestMethod]
    public async Task A_failure_in_chunk_two_keeps_chunk_one_and_the_cursor_and_the_resent_records_meet_row_K4()
    {
        var (f, injector) = await GroupsAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer, firstContactDone: false);
        var records = Enumerable.Range(0, 250)
            .Select(i => (Groups, peer.Record([SyncKey.New().Value], peer.Next(), GroupContent("G" + i, ".e" + i))))
            .ToArray();
        var pull = f.Pull(peer, records);

        injector.FailOperation = (_, n) => n == 229;
        var outcome = await f.ApplyAsync(link, peer, pull);
        injector.FailOperation = null;

        Assert.AreEqual(0, outcome.Applied);
        Assert.AreEqual(200, (await f.ExtensionGroups.GetAll()).Length, "the first chunk committed");
        var row = await f.LinkRowAsync(link.Id);
        Assert.AreEqual(("{}", DataSyncApplyRunner.ApplyFailedCode), (row.CursorsJson, row.LastErrorCode),
            "the cursor did not move");
        var seqs = (await f.RowsAsync(Groups)).ToDictionary(r => r.SyncKey, r => r.Seq);

        var again = await f.ApplyAsync(link, peer, pull);

        Assert.AreEqual(50, again.Applied, "row K4 for the 200 already applied, creates for the rest");
        Assert.AreEqual(250, (await f.ExtensionGroups.GetAll()).Length);
        foreach (var (key, seq) in seqs) Assert.AreEqual(seq, (await f.ByKeyAsync(key, Groups))!.Seq, "no second revision");
        Assert.AreEqual(records.Max(r => r.Item2.Seq),
            DataSyncStoredJson.ReadCounters((await f.LinkRowAsync(link.Id)).CursorsJson, "x")[Groups]);
    }

    [TestMethod]
    public async Task An_item_that_changed_during_the_apply_waits_as_a_Retry_record_and_the_rest_applies()
    {
        var (f, injector) = await GroupsAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer);
        var video = SyncKey.New().Value;
        var v1 = peer.Next();
        await f.ApplyAsync(link, peer, (Groups, peer.Record([video], v1, GroupContent("Video", ".mkv"))));
        var localKey = (await f.ByKeyAsync(video, Groups))!.LocalKey;

        injector.BeforeBatch = async (batch, service) =>
        {
            if (batch.Operations.OfType<UpdateEntityOperation>().Any())
                await service.Put(int.Parse(localKey), new Bakabase.Abstractions.Models.Input.ExtensionGroupPutInputModel(
                    "Video", [".mkv", ".avi"]));
        };
        var update = peer.Record([video], peer.Next(v1), GroupContent("Video", ".mkv", ".mp4"));
        var audio = peer.Record([SyncKey.New().Value], peer.Next(), GroupContent("Audio", ".mp3"));
        var outcome = await f.ApplyAsync(link, peer, (Groups, update), (Groups, audio));
        injector.BeforeBatch = null;

        Assert.AreEqual(1, outcome.Applied, "the create applied");
        var b = (await f.BasesAsync(link.Id)).Single(x => x.SyncKey == video);
        Assert.AreEqual((DataSyncPendingReason?) DataSyncPendingReason.Retry, b.PendingReason);
        Assert.AreEqual(v1.ToCanonicalString(), b.VvJson, "its base did not advance");
        Assert.AreEqual(audio.Seq, DataSyncStoredJson.ReadCounters((await f.LinkRowAsync(link.Id)).CursorsJson, "x")[Groups],
            "the cursor moved past it (§7.5.5)");
        var log = (await f.HistoryAsync()).Last();
        StringAssert.Contains(log.ResultJson, "ChangedDuringApply");

        // The next apply re-merges the Retry record (no refetch) and applies it.
        await f.RefreshAsync();
        var next = await f.Runner.RunAutoSyncAsync(Context(await f.LinkRowAsync(link.Id), peer), null, f.Args());
        Assert.AreEqual(1, next.Applied);
        CollectionAssert.AreEquivalent(new[] { ".avi", ".mkv", ".mp4" },
            (await f.GroupAsync(localKey))!.Extensions!.Select(e => e.ToLowerInvariant()).ToArray());
        Assert.IsNull((await f.BasesAsync(link.Id)).Single(x => x.SyncKey == video).PendingReason);
    }

    [TestMethod]
    public async Task A_regression_anomaly_rolls_back_the_Refresh_revisions_of_the_same_transaction()
    {
        var f = await CreateAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer);
        var mine = f.Kind.Add(Content("Mood", ("m", "Calm")));
        await f.RefreshAsync();
        var state = await f.StateAsync();
        var row = await f.RowAsync(mine);
        // A local edit Refresh would turn into a revision under the current actor…
        f.Kind.Definitions[mine] = f.Kind[mine].With(name: "Moods");
        // …and a record showing a counter of that actor this database never issued (row A1).
        var ahead = Vv(row.VvJson).With(new DataSyncActorId(state.ActorId), state.ActorCounter + 5);
        var outcome = await f.ApplyAsync(link, peer, (Item, peer.Record([row.SyncKey], peer.Next(ahead), Content("Mood"), "a0")));

        Assert.AreEqual(DataSyncPauseReason.LocalRestoreSuspected, outcome.Paused);
        Assert.AreEqual(row.VvJson, (await f.RowAsync(mine)).VvJson, "Refresh's revision rolled back with the merge");
        var after = await f.StateAsync();
        Assert.AreNotEqual(state.ActorId, after.ActorId, "rotated, outside any transaction (§5.6)");
        Assert.AreEqual(state.ActorCounter + 5,
            DataSyncStoredJson.ReadCounters(after.RetiredActorsJson, "x")[state.ActorId], "the recorded counter");
        Assert.AreEqual(DataSyncLinkState.Paused, (await f.LinkRowAsync(link.Id)).State);
    }

    [TestMethod]
    public async Task A_changed_actor_rolls_back_is_checked_and_retried_once()
    {
        var identity = new FlippingDeviceIdentity();
        var f = await CreateAsync(identityOverride: identity);
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer);
        var before = await f.StateAsync();
        var key = SyncKey.New().Value;
        var vv = peer.Next();

        // The identity changes after the check and before Refresh (the check, the session, then Refresh).
        identity.FlipAfter(3);
        var outcome = await f.ApplyAsync(link, peer, (Item, peer.Record([key], vv, Content("Genre"), "a0")));

        Assert.AreEqual(1, outcome.Applied, "retried once after the check");
        var after = await f.StateAsync();
        Assert.AreEqual(identity.Second.NodeId, after.NodeId);
        Assert.AreNotEqual(before.ActorId, after.ActorId, "a deliberate reset rotates (and pauses nothing)");
        Assert.AreEqual(DataSyncLinkState.Active, (await f.LinkRowAsync(link.Id)).State);
        Assert.AreEqual(vv, Vv((await f.ByKeyAsync(key))!.VvJson));
    }

    /// <summary><paramref name="count"/> valid order keys, ascending.</summary>
    private static List<string> OrderKeys(int count)
    {
        var keys = new List<string>();
        string? previous = null;
        for (var i = 0; i < count; i++) keys.Add(previous = Bakabase.Modules.DataSync.Ordering.FractionalIndex.Between(previous, null));
        return keys;
    }

    /// <summary>
    /// The actor guard handles evidence outside the gate: in the gap between two chunks it may rotate, pause the links
    /// and clear the evidence (§5.6). Asserts nothing of <paramref name="retired"/> was issued after that: no stored
    /// vector names it above its recorded counter, and the new actor's counter was not overwritten by a stale row.
    /// </summary>
    private static async Task<long> AssertNothingIssuedUnderTheRetiredActorAsync(DataSyncApplyFixture f, string retired)
    {
        var state = await f.StateAsync();
        Assert.AreNotEqual(retired, state.ActorId, "the guard rotated between the chunks");
        var recorded = DataSyncStoredJson.ReadCounters(state.RetiredActorsJson, "x")[retired];
        var highest = (await f.RowsAsync()).Max(r => Vv(r.VvJson).Counters.GetValueOrDefault(retired));
        Assert.AreEqual(recorded, highest,
            "the last counter the retired actor committed is its recorded one; none above it (§5.6)");
        return recorded;
    }

    [TestMethod]
    public async Task A_rotation_between_two_chunks_stops_the_apply_before_it_issues_under_the_retired_actor()
    {
        var f = await CreateAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer, firstContactDone: false);
        var order = OrderKeys(250);
        var created = Enumerable.Range(0, 250).Select(_ => (Key: SyncKey.New().Value, Vv: peer.Next())).ToList();
        await f.ApplyAsync(link, peer, created.Select((c, i) => (Item, peer.Record([c.Key], c.Vv, Content("P" + i), order[i])))
            .ToArray());
        var cursor = (await f.LinkRowAsync(link.Id)).CursorsJson;
        var history = (await f.HistoryAsync()).Count;

        // Renamed here and given a child there: each merge adds a counter of this device's own (MergedNoConflict).
        foreach (var key in f.Kind.Definitions.Keys.ToList()) f.Kind.Definitions[key] = f.Kind[key].With(name: f.Kind[key].Name + "!");
        var db = f.NewDb();
        (await db.DataSyncLinks.SingleAsync(l => l.Id == link.Id)).OnceFlagsJson =
            DataSyncStoredJson.WriteFlags(new DataSyncMergeFlags(SkipLargeChange: true));
        await db.SaveChangesAsync();
        var edits = created.Select((c, i) => (Item, peer.Record([c.Key], peer.Next(c.Vv), Content("P" + i, ("x", "X")), order[i])))
            .ToArray();

        string? retired = null;
        var gaps = 0;
        f.Runner.BetweenChunks = async () =>
        {
            if (gaps++ > 0) return;
            retired = (await f.StateAsync()).ActorId;
            // A reader saw a sequence number this database never issued (§7.5.1): rotation, every link paused.
            await f.Guard.ReportReaderAheadAsync("node-reader", default);
        };
        DataSyncAutoSyncOutcome outcome;
        try
        {
            outcome = await f.ApplyAsync(link, peer, edits);
        }
        finally
        {
            f.Runner.BetweenChunks = null;
        }

        Assert.AreEqual(1, gaps, "the apply stopped at its first gap");
        Assert.AreEqual((DataSyncPauseReason?) DataSyncPauseReason.LocalRestoreDetected, outcome.Paused,
            "the retry found the link paused by the restore");
        Assert.IsNull(outcome.ApplyLogId);
        await AssertNothingIssuedUnderTheRetiredActorAsync(f, retired!);
        var state = await f.StateAsync();
        Assert.AreEqual(0, state.ActorCounter, "the new actor issued nothing, and no stale row overwrote its counter");
        Assert.AreEqual(200, f.Kind.Definitions.Values.Count(d => d.Children.Count == 1), "only the first chunk stands");
        var row = await f.LinkRowAsync(link.Id);
        Assert.AreEqual((DataSyncLinkState.Paused, cursor), (row.State, row.CursorsJson), "the cursor did not move");
        Assert.AreEqual(history, (await f.HistoryAsync()).Count, "nothing after the rotation was recorded as applied");
    }

    [TestMethod]
    public async Task A_rotation_between_two_chunks_of_a_review_is_never_issued_under_and_the_review_plans_again()
    {
        var f = await CreateAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer, firstContactDone: false);
        var order = OrderKeys(250);
        var created = Enumerable.Range(0, 250).Select(_ => (Key: SyncKey.New().Value, Vv: peer.Next())).ToList();
        await f.ApplyAsync(link, peer, created.Select((c, i) => (Item, peer.Record([c.Key], c.Vv, Content("P" + i), order[i])))
            .ToArray());
        // A child here and another there: reviewing the peer's records merges both, with a counter of this device's
        // own and no key to add, so a stale local state row would issue it before anything reloaded the row.
        foreach (var key in f.Kind.Definitions.Keys.ToList())
            f.Kind.Definitions[key] = f.Kind[key].With(children: [new TestChild("h" + key, "Here")]);
        var db = f.NewDb();
        (await db.DataSyncLinks.SingleAsync(l => l.Id == link.Id)).State = DataSyncLinkState.AwaitingReview;
        await db.SaveChangesAsync();
        var review = f.Reviews.Stage(link.Id, false, f.Pull(peer, full: true, created
            .Select((c, i) => (Item, peer.Record([c.Key], peer.Next(c.Vv), Content("P" + i, ("t" + i, "There")), order[i])))
            .ToArray()));
        var plan = await f.PlanAsync(review);
        var decisions = Bakabase.Modules.DataSync.Planning.DataSyncPlanner.CompleteDecisions(plan, []).ToList();

        string? retired = null;
        var gaps = 0;
        f.Runner.BetweenChunks = async () =>
        {
            if (gaps++ > 0) return;
            retired = (await f.StateAsync()).ActorId;
            await f.Guard.ReportReaderAheadAsync("node-reader", default);
        };
        int? logId;
        try
        {
            logId = await f.Runner.RunReviewAsync(review.ReviewId, decisions, new DataSyncApplyOptions(false),
                f.Args("DataSyncReview:" + review.ReviewId));
        }
        finally
        {
            f.Runner.BetweenChunks = null;
        }

        Assert.AreEqual(1, gaps, "the first attempt stopped at its first gap; the retry fit in one chunk");
        Assert.IsNotNull(logId, "the retry planned again and applied what was left");
        await AssertNothingIssuedUnderTheRetiredActorAsync(f, retired!);
        var state = await f.StateAsync();
        Assert.AreEqual(250, f.Kind.Definitions.Values.Count(d => d.Children.Count == 2), "every definition holds both children");
        Assert.AreEqual(50, (await f.RowsAsync()).Count(r => Vv(r.VvJson).Counters.GetValueOrDefault(state.ActorId) > 0),
            "what the first chunk did not reach was merged under the new actor");
    }

    /// <summary>
    /// A chunk commits its holds with their <c>ChildDeletedInUse</c> items (§9.1): state-derived items are drafted only
    /// when the hold is made, and a later pull meets the hold already agreed (row K4) and drafts nothing. An apply that
    /// failed after that chunk must not leave a child withheld with nobody asked.
    /// </summary>
    [TestMethod]
    public async Task A_hold_a_chunk_committed_keeps_its_item_when_the_apply_fails_at_a_later_gap()
    {
        var f = await CreateAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer, firstContactDone: false);
        var order = OrderKeys(250);
        var created = Enumerable.Range(0, 250).Select(_ => (Key: SyncKey.New().Value, Vv: peer.Next())).ToList();
        await f.ApplyAsync(link, peer, created.Select((c, i) =>
            (Item, peer.Record([c.Key], c.Vv, Content("P" + i, ("a" + i, "A"), ("b" + i, "B")), order[i]))).ToArray());
        var p0 = f.Kind.KeyOf("P0");
        f.Kind.Use(p0, "a0", 3);

        // The peer deletes child a of each (in use on P0 only) and renames b; the large change is let through.
        async Task SkipLargeChangeAsync()
        {
            var db = f.NewDb();
            (await db.DataSyncLinks.SingleAsync(l => l.Id == link.Id)).OnceFlagsJson =
                DataSyncStoredJson.WriteFlags(new DataSyncMergeFlags(SkipLargeChange: true));
            await db.SaveChangesAsync();
        }

        await SkipLargeChangeAsync();
        var edits = created.Select((c, i) =>
                (Item, peer.Record([c.Key], peer.Next(c.Vv), Content("P" + i, ("b" + i, "B2")), order[i])))
            .ToArray();

        var gaps = 0;
        f.Runner.BetweenChunks = () =>
        {
            gaps++;
            throw new InvalidOperationException("The disk is full.");
        };
        try
        {
            await f.ApplyAsync(link, peer, edits);
        }
        finally
        {
            f.Runner.BetweenChunks = null;
        }

        Assert.AreEqual(1, gaps);
        Assert.AreEqual(DataSyncApplyRunner.ApplyFailedCode, (await f.LinkRowAsync(link.Id)).LastErrorCode);
        var held = DataSyncStoredJson.ReadOverlay((await f.RowAsync(p0)).OverlayJson).HeldChildren;
        CollectionAssert.AreEqual(new[] { "a0" }, held.Select(h => h.ChildId).ToArray(), "the first chunk stands");
        var item = (await f.OpenItemsAsync()).Single(i => i.Type == DataSyncInboxItemType.ChildDeletedInUse);
        Assert.AreEqual(("child:a0", (int?) link.Id), (item.SubjectPath, item.LinkId), "committed with its hold");

        // The pull again: the first chunk's entities meet row K4 and draft nothing; the item stands, once.
        await SkipLargeChangeAsync();
        await f.ApplyAsync(link, peer, f.Pull(peer, edits));
        Assert.IsNull((await f.LinkRowAsync(link.Id)).LastErrorCode);
        Assert.AreEqual(250, f.Kind.Definitions.Values.Count(d => d.Children.Any(c => c.Label == "B2")));
        Assert.AreEqual(item.Id, (await f.OpenItemsAsync()).Single(i => i.Type == DataSyncInboxItemType.ChildDeletedInUse).Id);
        CollectionAssert.AreEqual(new[] { "a0", "b0" }, f.Kind[p0].Children.Select(c => c.Id).ToArray(), "still held");
    }

    [TestMethod]
    public async Task A_stopped_apply_rolls_back_and_rethrows_the_cancellation_unchanged()
    {
        var (f, injector) = await GroupsAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer);
        await f.AddGroupAsync("Docs", ".pdf");
        await f.RefreshAsync();
        var before = await SnapshotAsync(f);
        using var cts = new CancellationTokenSource();
        injector.BeforeBatch = (_, _) =>
        {
            cts.Cancel();
            return Task.CompletedTask;
        };

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => f.Runner.RunAutoSyncAsync(
            Context(link, peer), f.Pull(peer, (Groups, peer.Record([SyncKey.New().Value], peer.Next(),
                GroupContent("Audio", ".mp3")))), f.Args(ct: cts.Token)));
        injector.BeforeBatch = null;

        Assert.AreEqual(before, await SnapshotAsync(f), "the task ends Cancelled with nothing written (v3.1 M-f)");
        Assert.IsNull((await f.LinkRowAsync(link.Id)).LastErrorCode, "a stop is not a failure");
    }

    /// <summary>The link's next apply lets a large change through (B5, §8.7), as its once flag does.</summary>
    private static async Task SkipLargeChangeAsync(DataSyncApplyFixture f, DataSyncLinkDbModel link)
    {
        var db = f.NewDb();
        (await db.DataSyncLinks.SingleAsync(l => l.Id == link.Id)).OnceFlagsJson =
            DataSyncStoredJson.WriteFlags(new DataSyncMergeFlags(SkipLargeChange: true));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A chunk commits before the next begins and other writers take SQLite's lock in between (§8.10.2), so the usage
    /// the merge read before the first chunk (§2.7) is read again for each later one: an entity the peer deleted is
    /// deleted by itself only while it has no values (§8.6). One that gained values in the gap waits as a <c>Retry</c>
    /// record, and the next merge asks about it.
    /// </summary>
    [TestMethod]
    public async Task An_automatic_deletion_in_a_later_chunk_waits_when_the_entity_gained_values_in_the_gap()
    {
        var f = await CreateAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer, firstContactDone: false);
        var order = OrderKeys(250);
        var created = Enumerable.Range(0, 250).Select(_ => (Key: SyncKey.New().Value, Vv: peer.Next())).ToList();
        await f.ApplyAsync(link, peer, created.Select((c, i) => (Item, peer.Record([c.Key], c.Vv, Content("P" + i), order[i])))
            .ToArray());
        var last = f.Kind.KeyOf("P249");

        // The peer renames the first 249 and deletes the last: the deletion is in the second chunk.
        await SkipLargeChangeAsync(f, link);
        var records = created.Select((c, i) => i < 249
                ? (Item, peer.Record([c.Key], peer.Next(c.Vv), Content("P" + i + "!"), order[i]))
                : (Item, peer.Tombstone([c.Key], peer.Next(c.Vv))))
            .ToArray();
        var gaps = 0;
        f.Runner.BetweenChunks = () =>
        {
            gaps++;
            // A resource takes a value of the last definition while the apply leaves the lock free.
            f.Kind.Values[last] = 1;
            return Task.CompletedTask;
        };
        try
        {
            await f.ApplyAsync(link, peer, records);
        }
        finally
        {
            f.Runner.BetweenChunks = null;
        }

        Assert.AreEqual(1, gaps);
        Assert.IsTrue(f.Kind.Definitions.ContainsKey(last), "a definition with values is never deleted without a person");
        Assert.AreEqual(249, f.Kind.Definitions.Values.Count(d => d.Name.EndsWith('!')), "the rest of the chunk applied");
        Assert.AreEqual(DataSyncPendingReason.Retry,
            (await f.BasesAsync(link.Id)).Single(b => b.SyncKey == created[249].Key).PendingReason);
        Assert.IsNull((await f.LinkRowAsync(link.Id)).LastErrorCode);

        // The next merge reads the values and asks.
        await f.ApplyAsync(link, peer);
        var item = (await f.OpenItemsAsync()).Single(i => i.Type == DataSyncInboxItemType.DeletedThere);
        Assert.AreEqual(created[249].Key, item.SyncKey);
        Assert.IsTrue(f.Kind.Definitions.ContainsKey(last));
    }

    /// <summary>
    /// §8.5.4 step 3 across chunks: a child the peer deleted is removed by itself only while nothing here uses it. One
    /// resources took in the gap before its chunk stays, its entity's record waits as <c>Retry</c>, and the next merge
    /// holds the child with its question.
    /// </summary>
    [TestMethod]
    public async Task A_child_removal_in_a_later_chunk_waits_when_resources_took_the_child_in_the_gap()
    {
        var f = await CreateAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer, firstContactDone: false);
        var order = OrderKeys(250);
        var created = Enumerable.Range(0, 250).Select(_ => (Key: SyncKey.New().Value, Vv: peer.Next())).ToList();
        await f.ApplyAsync(link, peer, created.Select((c, i) =>
            (Item, peer.Record([c.Key], c.Vv, Content("P" + i, ("a" + i, "A"), ("b" + i, "B")), order[i]))).ToArray());
        var last = f.Kind.KeyOf("P249");

        // The peer deletes child b of each, unused here when the merge reads the usage.
        await SkipLargeChangeAsync(f, link);
        var edits = created.Select((c, i) =>
            (Item, peer.Record([c.Key], peer.Next(c.Vv), Content("P" + i, ("a" + i, "A")), order[i]))).ToArray();
        f.Runner.BetweenChunks = () =>
        {
            f.Kind.Use(last, "b249", 3);
            return Task.CompletedTask;
        };
        try
        {
            await f.ApplyAsync(link, peer, edits);
        }
        finally
        {
            f.Runner.BetweenChunks = null;
        }

        CollectionAssert.AreEqual(new[] { "a249", "b249" }, f.Kind[last].Children.Select(c => c.Id).ToArray(),
            "a child in use is never removed without a person");
        Assert.AreEqual(249, f.Kind.Definitions.Values.Count(d => d.Children.Count == 1), "every other removal applied");
        Assert.AreEqual(DataSyncPendingReason.Retry,
            (await f.BasesAsync(link.Id)).Single(b => b.SyncKey == created[249].Key).PendingReason);

        // The next merge holds it and asks.
        await f.ApplyAsync(link, peer);
        var item = (await f.OpenItemsAsync()).Single(i => i.Type == DataSyncInboxItemType.ChildDeletedInUse);
        Assert.AreEqual(("child:b249", created[249].Key), (item.SubjectPath, item.SyncKey));
        CollectionAssert.AreEqual(new[] { "b249" },
            DataSyncStoredJson.ReadOverlay((await f.RowAsync(last)).OverlayJson).HeldChildren.Select(h => h.ChildId).ToArray());
    }

    /// <summary>
    /// The reproduction over the real custom property kind: the value a resource took in the gap is not deleted with
    /// its property. The adapter checks too (<see cref="DeleteEntityOperation.RequireNoValues"/>).
    /// </summary>
    [TestMethod]
    public async Task A_custom_property_that_gained_a_value_in_the_gap_is_kept_with_its_value()
    {
        var f = await CreateAsync(customProperties: true);
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer, DataSyncLinkMode.TwoWay, false, Properties);
        var order = OrderKeys(250);
        var created = Enumerable.Range(0, 250).Select(_ => (Key: SyncKey.New().Value, Vv: peer.Next())).ToList();
        var codec = Bakabase.Modules.DataSync.Kinds.CustomProperties.CustomPropertyCodec.Instance;
        System.Text.Json.Nodes.JsonObject Text(string name) => codec.Write(
            new Bakabase.Modules.DataSync.Kinds.CustomProperties.CustomPropertyContentV1
            {
                Name = name, Type = Bakabase.Abstractions.Models.Domain.Constants.PropertyType.SingleLineText,
            });
        await f.ApplyAsync(link, peer,
            created.Select((c, i) => (Properties, peer.Record([c.Key], c.Vv, Text("P" + i), order[i]))).ToArray());
        var p249 = (await f.CustomProperties.GetAll()).Single(p => p.Name == "P249");

        await SkipLargeChangeAsync(f, link);
        var records = created.Select((c, i) => i < 249
                ? (Properties, peer.Record([c.Key], peer.Next(c.Vv), Text("P" + i + "!"), order[i]))
                : (Properties, peer.Tombstone([c.Key], peer.Next(c.Vv))))
            .ToArray();
        f.Runner.BetweenChunks = async () =>
        {
            await using var scope = f.Services.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<Bakabase.Modules.Property.Abstractions.Services.ICustomPropertyValueService>()
                .AddDbModelRange([new Bakabase.Modules.Property.Abstractions.Models.Db.CustomPropertyValueDbModel
                {
                    ResourceId = 1, PropertyId = p249.Id,
                    Scope = (int) Bakabase.Abstractions.Models.Domain.Constants.PropertyValueScope.Manual,
                    Value = "Hello",
                }]);
        };
        try
        {
            await f.ApplyAsync(link, peer, records);
        }
        finally
        {
            f.Runner.BetweenChunks = null;
        }

        var properties = await f.CustomProperties.GetAll();
        Assert.AreEqual(249, properties.Count(p => p.Name.EndsWith('!')));
        Assert.IsTrue(properties.Any(p => p.Id == p249.Id), "the property with a value stays");
        await using var read = f.Services.CreateAsyncScope();
        var values = await read.ServiceProvider
            .GetRequiredService<Bakabase.Modules.Property.Abstractions.Services.ICustomPropertyValueService>()
            .GetAllDbModels(v => v.PropertyId == p249.Id, false);
        Assert.AreEqual(1, values.Count, "and so does its value");
        Assert.AreEqual(DataSyncPendingReason.Retry,
            (await f.BasesAsync(link.Id)).Single(b => b.SyncKey == created[249].Key).PendingReason);
    }
}

/// <summary>An identity that answers another node from the n-th call after <see cref="FlipAfter"/>.</summary>
internal sealed class FlippingDeviceIdentity : IDataSyncDeviceIdentity
{
    private int _countdown = -1;
    private bool _flipped;

    public DataSyncDevice First { get; } = Bakabase.TestKit.DataSync.TestDataSyncDeviceIdentity.NewDevice();
    public DataSyncDevice Second { get; } = Bakabase.TestKit.DataSync.TestDataSyncDeviceIdentity.NewDevice();

    public void FlipAfter(int calls) => _countdown = calls;

    public Task<DataSyncDevice> GetAsync(CancellationToken ct)
    {
        if (_countdown > 0 && --_countdown == 0) _flipped = true;
        return Task.FromResult(_flipped ? Second : First);
    }
}
