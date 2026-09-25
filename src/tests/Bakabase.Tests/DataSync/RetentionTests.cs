using Bakabase.InsideWorld.Business.Components.DataSync.Feed;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.DataSyncStoreFixture;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// Retention (spec §4.6) at the store: tombstones stop being served after 180 days but are never deleted, and floors
/// are per kind, so a kind is superseded only by its own floor; closed items, apply logs, readers and retired actors
/// are pruned by their own rules; retention runs once a day under the gate, and keeps the newest five data sync
/// backups. Serving a superseded cursor from 0 and row T2 are the feed's and the merger's parts of this class.
/// </summary>
[TestClass]
public class RetentionTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private DataSyncStoreFixture _f = null!;

    [TestInitialize]
    public async Task Setup() => _f = await DataSyncStoreFixture.CreateAsync();

    [TestMethod]
    public async Task A_tombstone_stops_being_served_after_180_days_and_is_never_deleted()
    {
        var old = await _f.TombstoneAsync(await _f.LiveAsync("1"));
        var recent = await _f.TombstoneAsync(await _f.LiveAsync("2"));
        var neverServed = await _f.TombstoneAsync(await _f.LiveAsync("3"), served: false);
        var otherKind = await _f.TombstoneAsync(await _f.LiveAsync("4", kind: DataSyncKindIds.ExtensionGroup));
        await Age(old, 181);
        await Age(neverServed, 400);
        await Age(recent, 179);
        await Age(otherKind, 10);

        await _f.Store.PruneAsync(Now, default);

        Assert.IsFalse((await _f.ByPrimaryAsync(old.SyncKey))!.TombstoneServed);
        Assert.IsTrue((await _f.ByPrimaryAsync(recent.SyncKey))!.TombstoneServed);
        Assert.AreEqual(4, await _f.Db.DataSyncEntities.CountAsync(), "tombstones are kept forever");
        var floors = await FloorsAsync();
        Assert.AreEqual(old.Seq, floors[Kind], "the floor is the highest Seq retention stopped serving");
        Assert.IsFalse(floors.ContainsKey(DataSyncKindIds.ExtensionGroup),
            "an unserved tombstone of one kind never supersedes another kind's cursor");
        Assert.AreEqual(1, (await _f.Store.CountPublishedAsync(Kind, default)).Tombstones);

        // A floor never goes down, and a later prune with nothing new keeps it.
        await _f.Store.PruneAsync(Now.AddDays(1), default);
        Assert.AreEqual(old.Seq, (await FloorsAsync())[Kind]);
    }

    [TestMethod]
    public async Task Floors_are_per_kind_so_a_kind_below_another_kinds_floor_is_never_superseded()
    {
        // Extension groups change early (low Seq); a custom property tombstone much later becomes unserved.
        var group = await _f.LiveAsync("1", kind: DataSyncKindIds.ExtensionGroup);
        await _f.LiveAsync("2");
        var old = await _f.TombstoneAsync(await _f.LiveAsync("3"));
        await Age(old, 181);
        await _f.Store.PruneAsync(Now, default);
        var state = (await _f.Store.GetLocalStateAsync(default))!;
        var floor = DataSyncCursorRules.FloorOf(state, Kind);
        Assert.AreEqual(old.Seq, floor);
        Assert.IsTrue(group.Seq < floor, "the extension groups' MaxSeq is below the custom properties' floor");

        Assert.AreEqual(0, DataSyncCursorRules.FloorOf(state, DataSyncKindIds.ExtensionGroup));
        Assert.IsFalse(DataSyncCursorRules.IsSuperseded(group.Seq, DataSyncCursorRules.FloorOf(state, DataSyncKindIds.ExtensionGroup),
            state.LastSeq, false), "a kind is superseded only by its own floor (gate fix B2)");
        Assert.IsTrue(DataSyncCursorRules.IsSuperseded(floor - 1, floor, state.LastSeq, false),
            "a reader that may have missed the unserved tombstone is served from 0");
        Assert.IsFalse(DataSyncCursorRules.IsSuperseded(floor, floor, state.LastSeq, false), "it saw the tombstone");
        Assert.IsFalse(DataSyncCursorRules.IsSuperseded(0, floor, state.LastSeq, false), "a fresh reader reads from 0 anyway");
        Assert.IsTrue(DataSyncCursorRules.IsSuperseded(state.LastSeq + 1, 0, state.LastSeq, false),
            "a cursor above LastSeq: sequence numbers this database never issued");
        Assert.IsTrue(DataSyncCursorRules.IsSuperseded(1, 0, state.LastSeq, recordedReaderAhead: true));
        Assert.IsTrue(DataSyncCursorRules.IsReaderAhead(new Dictionary<string, long> {[Kind] = state.LastSeq + 1}, state.LastSeq));
        Assert.IsFalse(DataSyncCursorRules.IsReaderAhead(new Dictionary<string, long> {[Kind] = state.LastSeq}, state.LastSeq));
    }

    [TestMethod]
    public async Task Retention_runs_once_a_day_under_the_gate_in_its_own_transaction()
    {
        var old = await _f.TombstoneAsync(await _f.LiveAsync("1"));
        await Age(old, 181);
        var clock = new ManualTimeProvider(Now);
        var gate = _f.Services.GetRequiredService<DataSyncGate>();
        var retention = new DataSyncRetention(gate, _f.Services.GetRequiredService<IServiceScopeFactory>(),
            _f.Services.GetRequiredService<IDataSyncDataDirectory>(), clock);

        Task<bool> run;
        using (await gate.EnterAsync(null, default))
        {
            run = retention.RunIfDueAsync(default);
            await Task.Delay(100);
            Assert.IsFalse(run.IsCompleted, "retention waits for the gate");
        }

        Assert.IsTrue(await run);
        Assert.IsFalse((await _f.ByPrimaryAsync(old.SyncKey))!.TombstoneServed);
        Assert.IsFalse(await retention.RunIfDueAsync(default), "once a day");
        clock.Advance(DataSyncRetention.Interval);
        Assert.IsTrue(await retention.RunIfDueAsync(default));
    }

    [TestMethod]
    public void Only_the_newest_five_data_sync_backups_are_kept()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"RetentionTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var backups = Enumerable.Range(1, 7).Select(i => $"data-sync-20260901-00000{i}.db").ToList();
            foreach (var name in backups.Concat(["app.db", "data-sync-notes.txt"]))
                File.WriteAllText(Path.Combine(folder, name), name);
            Directory.CreateDirectory(Path.Combine(folder, "2.4.0"));

            Assert.AreEqual(2, DataSyncRetention.PruneBackups(folder));

            CollectionAssert.AreEquivalent(backups.Skip(2).Concat(["app.db", "data-sync-notes.txt"]).ToList(),
                Directory.GetFiles(folder).Select(Path.GetFileName).ToList(), "the app's own backups are never touched");
            Assert.IsTrue(Directory.Exists(Path.Combine(folder, "2.4.0")));
            Assert.AreEqual(0, DataSyncRetention.PruneBackups(Path.Combine(folder, "missing")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task Closed_items_are_kept_90_days()
    {
        var link = await _f.LinkAsync("peer-1");
        var keys = Enumerable.Range(0, 3).Select(_ => NewKey()).ToList();
        await _f.Store.UpsertItemsAsync(link.Id, "peer-1",
            keys.Select(k => Draft(Kind, k, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name"))
                .ToList(), Now.AddDays(-400), default);
        var items = await _f.Db.DataSyncInboxItems.OrderBy(i => i.Id).ToListAsync();
        items[0].ClosedAtUtc = Now.AddDays(-91);
        items[1].ClosedAtUtc = Now.AddDays(-89);
        await _f.Db.SaveChangesAsync();

        await _f.Store.PruneAsync(Now, default);

        CollectionAssert.AreEqual(new[] {items[1].Id, items[2].Id},
            await _f.Db.DataSyncInboxItems.AsNoTracking().OrderBy(i => i.Id).Select(i => i.Id).ToListAsync(),
            "an open item is kept however old it is");
    }

    [TestMethod]
    public async Task Apply_logs_keep_the_newest_500_or_30_days_within_64_MiB_pruning_oldest_first()
    {
        // 520 logs a year old, then 20 from this week.
        for (var i = 0; i < 540; i++)
            _f.Db.DataSyncApplyLogs.Add(Log(i < 520 ? Now.AddDays(-365).AddMinutes(i) : Now.AddDays(-2).AddMinutes(i), 1));
        await _f.Db.SaveChangesAsync();
        var ids = await _f.Db.DataSyncApplyLogs.OrderBy(l => l.Id).Select(l => l.Id).ToListAsync();

        await _f.Store.PruneAsync(Now, default);

        var kept = await _f.Db.DataSyncApplyLogs.OrderBy(l => l.Id).Select(l => l.Id).ToListAsync();
        CollectionAssert.AreEqual(ids.Skip(40).ToList(), kept, "the newest 500; the 20 recent ones are among them");

        // Every one of 510 logs is recent: all are kept.
        _f.Db.DataSyncApplyLogs.RemoveRange(_f.Db.DataSyncApplyLogs);
        for (var i = 0; i < 510; i++) _f.Db.DataSyncApplyLogs.Add(Log(Now.AddDays(-1).AddSeconds(i), 1));
        await _f.Db.SaveChangesAsync();
        await _f.Store.PruneAsync(Now, default);
        Assert.AreEqual(510, await _f.Db.DataSyncApplyLogs.CountAsync());

        // Over 64 MiB of pre-images: the oldest go until the rest fits.
        const int mib = 1 << 20;
        await ReplaceLogsAsync(40 * mib, 20 * mib, 30 * mib, 10 * mib);
        await _f.Store.PruneAsync(Now, default);
        CollectionAssert.AreEqual(new[] {20 * mib, 30 * mib, 10 * mib}, await LogSizesAsync());

        // Oldest first only: once one log is pruned every older one goes too, even one that would fit, so a newer
        // log is never pruned while an older one is kept (v3.1 N15).
        await ReplaceLogsAsync(5 * mib, 50 * mib, 20 * mib);
        await _f.Store.PruneAsync(Now, default);
        CollectionAssert.AreEqual(new[] {20 * mib}, await LogSizesAsync());
    }

    private async Task ReplaceLogsAsync(params int[] sizesOldestFirst)
    {
        _f.Db.DataSyncApplyLogs.RemoveRange(_f.Db.DataSyncApplyLogs);
        foreach (var size in sizesOldestFirst) _f.Db.DataSyncApplyLogs.Add(Log(Now.AddHours(-1), size));
        await _f.Db.SaveChangesAsync();
    }

    private Task<List<int>> LogSizesAsync() =>
        _f.Db.DataSyncApplyLogs.AsNoTracking().OrderBy(l => l.Id).Select(l => l.PreImageBytes).ToListAsync();

    [TestMethod]
    public async Task The_newest_log_stays_even_over_the_budget()
    {
        _f.Db.DataSyncApplyLogs.Add(Log(Now.AddMinutes(-2), 10));
        _f.Db.DataSyncApplyLogs.Add(Log(Now.AddMinutes(-1), 100 << 20));
        await _f.Db.SaveChangesAsync();

        await _f.Store.PruneAsync(Now, default);

        Assert.AreEqual(100 << 20, (await _f.Db.DataSyncApplyLogs.SingleAsync()).PreImageBytes);
    }

    [TestMethod]
    public async Task Readers_are_forgotten_after_180_days_and_retired_actors_when_no_vector_names_them()
    {
        var query = new DataSyncFeedQuery("twoWay", new Dictionary<string, long>(), null, "ok");
        await _f.Store.TouchReaderAsync(new DataSyncReader("node-old", "g1", "Old"), query, 1, Now.AddDays(-200), default);
        await _f.Store.TouchReaderAsync(new DataSyncReader("node-new", "g2", "New"), query, 1, Now.AddDays(-20), default);

        // Two retired actors: B is still named by an entity's vector, C by nothing.
        await _f.LiveAsync("1", Vv((ActorA, 1), (ActorB, 4)));
        var state = (await _f.Store.GetLocalStateAsync(default))!;
        state.RetiredActorsJson = DataSyncStoredJson.WriteCounters(new Dictionary<string, long>
            {[ActorB] = 4, [ActorC] = 9});
        await _f.Store.SaveLocalStateAsync(state, default);

        await _f.Store.PruneAsync(Now, default);

        CollectionAssert.AreEqual(new[] {"node-new"}, (await _f.Store.GetReadersAsync(default)).Select(r => r.NodeId).ToArray());
        var retired = DataSyncStoredJson.ReadCounters((await _f.Store.GetLocalStateAsync(default))!.RetiredActorsJson, "");
        CollectionAssert.AreEquivalent(new[] {ActorB}, retired.Keys.ToArray());
    }

    private async Task<IReadOnlyDictionary<string, long>> FloorsAsync() =>
        DataSyncStoredJson.ReadCounters((await _f.Store.GetLocalStateAsync(default))!.TombstoneFloorSeqsJson, "");

    private async Task Age(DataSyncEntityDbModel tombstone, int days)
    {
        tombstone.DeletedAtUtc = Now.AddDays(-days);
        await _f.Db.SaveChangesAsync();
    }

    private static DataSyncApplyLogDbModel Log(DateTime at, int preImageBytes) => new()
    {
        Kind = DataSyncHistoryKind.AutoSync, AppliedAtUtc = at, SummaryJson = "{}", ResultJson = "[]",
        PreImageJson = "{}", PreImageBytes = preImageBytes,
    };
}
