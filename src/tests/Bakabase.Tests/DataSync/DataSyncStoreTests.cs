using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.TestKit.DataSync;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.DataSyncStoreFixture;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// The store's own behaviour (spec §4.4, §4.5, §6.2, §7.5.1, §7.5.6): lazy rows, Seq, what the feed reads, entity
/// state, links, readers, history and attention.
/// </summary>
[TestClass]
public class DataSyncStoreTests
{
    private DataSyncStoreFixture _f = null!;

    [TestInitialize]
    public async Task Setup() => _f = await DataSyncStoreFixture.CreateAsync();

    [TestMethod]
    public async Task There_is_no_row_and_no_seq_before_the_first_refresh()
    {
        var sp = await TestServiceBuilder.BuildServiceProvider();
        var store = sp.GetRequiredService<IDataSyncStore>();

        Assert.IsNull(await store.GetLocalStateAsync(default));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => store.NextSeqAsync(default));
        Assert.AreEqual(0, (await store.GetLinksAsync(default)).Count);
        Assert.AreEqual(0, (await store.GetReadersAsync(default)).Count);
    }

    [TestMethod]
    public async Task The_local_state_row_is_created_once_with_a_derived_actor()
    {
        var state = (await _f.Store.GetLocalStateAsync(default))!;

        Assert.AreEqual(DataSyncLocalStateRows.SingletonId, state.Id);
        Assert.AreEqual(1, state.ActorGeneration);
        Assert.AreEqual(Bakabase.Modules.DataSync.Identity.DataSyncActorId
            .Derive(state.NodeId, state.LibraryEpoch, state.ActorSalt).Value, state.ActorId);
        Assert.AreEqual(32, state.DbInstanceId.Length);
        Assert.AreEqual(0, state.LastSeq);

        // Saving a detached copy replaces the stored values instead of inserting a second row.
        var copy = state with {NewDefinitionsStayLocal = true};
        _f.Db.Entry(state).State = EntityState.Detached;
        await _f.Store.SaveLocalStateAsync(copy, default);
        Assert.AreEqual(1, await _f.Db.DataSyncLocalStates.CountAsync());
        Assert.IsTrue((await _f.Store.GetLocalStateAsync(default))!.NewDefinitionsStayLocal);
    }

    [TestMethod]
    public async Task Seq_comes_only_from_the_state_row_and_never_repeats_across_scopes()
    {
        Assert.AreEqual(1, await _f.Store.NextSeqAsync(default));
        Assert.AreEqual(2, await _f.Store.NextSeqAsync(default));
        await _f.Db.SaveChangesAsync();

        // Another scope issues numbers in between; this scope's copy of the row is stale but unchanged, so it is
        // read again before the next number.
        using (var scope = _f.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            var other = scope.ServiceProvider.GetRequiredService<DataSyncStore>();
            Assert.AreEqual(3, await other.NextSeqAsync(default));
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().SaveChangesAsync();
        }

        Assert.AreEqual(4, await _f.Store.NextSeqAsync(default));
    }

    [TestMethod]
    public async Task The_feed_reads_synced_rows_and_served_tombstones_in_seq_order()
    {
        var synced = await _f.LiveAsync("1");
        var localOnly = await _f.LiveAsync("2", state: DataSyncEntitySyncState.LocalOnly);
        var served = await _f.TombstoneAsync(await _f.LiveAsync("3"));
        var unserved = await _f.TombstoneAsync(await _f.LiveAsync("4"), served: false);
        var held = await _f.LiveAsync("5");
        held.PublishHeld = true;
        await _f.Db.SaveChangesAsync();

        var all = await _f.Store.GetPublishedChangedSinceAsync(Kind, 0, default);
        CollectionAssert.AreEqual(new[] {synced.SyncKey, served.SyncKey, held.SyncKey},
            all.Select(r => r.SyncKey).ToArray(), "Seq order; held rows are served as HeldAtSource");
        CollectionAssert.DoesNotContain(all.Select(r => r.SyncKey).ToList(), localOnly.SyncKey);
        CollectionAssert.DoesNotContain(all.Select(r => r.SyncKey).ToList(), unserved.SyncKey);

        var since = await _f.Store.GetPublishedChangedSinceAsync(Kind, served.Seq, default);
        CollectionAssert.AreEqual(new[] {held.SyncKey}, since.Select(r => r.SyncKey).ToArray());
        Assert.AreEqual((2, 1), await _f.Store.CountPublishedAsync(Kind, default));
    }

    [TestMethod]
    public async Task Leaving_synced_closes_items_clears_pending_and_publish_held_and_rejoining_drops_the_bases()
    {
        var link = await _f.LinkAsync("peer-1");
        var e = await _f.LiveAsync("1");
        await _f.Store.SetOverlayAsync(Kind, e.LocalKey,
            new DataSyncOverlay(["kept"], [new DataSyncHeldChild("held", link.Id)]), default);
        e.PublishHeld = true;
        await _f.Store.UpsertBasesAsync(link.Id,
        [
            new DataSyncBaseUpdate(Kind, Key(e.SyncKey), DataSyncBaseState.Normal, null,
                Record([e.SyncKey], Vv((ActorB, 1))), null,
                Pending(Record([e.SyncKey], Vv((ActorB, 2))), DataSyncPendingReason.Conflict), false),
        ], default);
        await _f.Store.UpsertItemsAsync(link.Id, "peer-1",
            [Draft(Kind, e.SyncKey, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name")],
            DateTime.UtcNow, default);
        var seq = e.Seq;

        await _f.Store.SetEntityStateAsync(Kind, e.LocalKey, DataSyncEntitySyncState.Detached, default);

        Assert.AreEqual(DataSyncEntitySyncState.Detached, e.State);
        Assert.IsTrue(e.Seq > seq);
        Assert.IsFalse(e.PublishHeld);
        Assert.IsNull(e.RawHash, "Refresh reads it again");
        Assert.AreEqual(DataSyncInboxClosure.Superseded, (await _f.ItemsAsync()).Single().Closure);
        var overlay = DataSyncStoredJson.ReadOverlay(e.OverlayJson);
        CollectionAssert.AreEqual(new[] {"kept", "held"}, overlay.LocalOnlyChildren.ToArray(),
            "no item is left to decide the hold");
        Assert.AreEqual(0, overlay.HeldChildren.Count);
        var peerBase = (await _f.Store.GetBasesAsync(link.Id, Kind, default)).Single();
        Assert.IsNull(peerBase.Pending);
        Assert.IsNotNull(peerBase.Record, "the agreement stays");

        // The same state again changes nothing.
        seq = e.Seq;
        await _f.Store.SetEntityStateAsync(Kind, e.LocalKey, DataSyncEntitySyncState.Detached, default);
        Assert.AreEqual(seq, e.Seq);

        // Rejoining: the next pulls merge it through its keys with no base (§3.6).
        await _f.Store.SetEntityStateAsync(Kind, e.LocalKey, DataSyncEntitySyncState.Synced, default);
        Assert.AreEqual(0, (await _f.Store.GetBasesAsync(link.Id, Kind, default)).Count);
        Assert.IsTrue(e.Seq > seq);
    }

    [TestMethod]
    public async Task Overlays_and_childrenLocal_mark_the_row_for_refresh()
    {
        var e = await _f.LiveAsync("1");
        var overlay = new DataSyncOverlay(["a"], [new DataSyncHeldChild("b", 3)]);

        await _f.Store.SetOverlayAsync(Kind, e.LocalKey, overlay, default);
        Assert.IsNull(e.RawHash);
        var stored = DataSyncStoredJson.ReadOverlay(e.OverlayJson);
        CollectionAssert.AreEqual(new[] {"a"}, stored.LocalOnlyChildren.ToArray());
        Assert.AreEqual(new DataSyncHeldChild("b", 3), stored.HeldChildren.Single());
        Assert.IsFalse(e.OverlayJson!.Contains("hiddenChildIds"), "computed members are not stored");

        await _f.Store.SetOverlayAsync(Kind, e.LocalKey, DataSyncOverlay.None, default);
        Assert.IsNull(e.OverlayJson);

        e.RawHash = "raw";
        await _f.Store.SetChildrenLocalAsync(Kind, e.LocalKey, true, default);
        Assert.IsTrue(e.ChildrenLocal);
        Assert.IsNull(e.RawHash);

        await Assert.ThrowsExceptionAsync<KeyNotFoundException>(() =>
            _f.Store.SetChildrenLocalAsync(Kind, "missing", true, default));
    }

    /// <summary>
    /// The link views' counts come from one grouped query (§11.2): the same numbers as reading every base, per link,
    /// over the kinds this build knows only.
    /// </summary>
    [TestMethod]
    public async Task Base_counts_per_link_match_reading_every_base()
    {
        var one = await _f.LinkAsync("peer-1");
        var two = await _f.LinkAsync("peer-2");
        await _f.LinkAsync("peer-3");
        DataSyncBaseUpdate Base(string kind, DataSyncBaseState state, DataSyncPendingReason? pending = null,
            DataSyncExclusionReason? exclusion = null)
        {
            var key = NewKey();
            var record = Record([key], Vv((ActorB, 1)));
            return new DataSyncBaseUpdate(kind, Key(key), state, exclusion,
                state is DataSyncBaseState.Normal or DataSyncBaseState.MissingAtPeer ? record : null, null,
                pending is { } reason ? Pending(Record([key], Vv((ActorB, 2)), seq: 2), reason) : null, false);
        }

        await _f.Store.UpsertBasesAsync(one.Id,
        [
            Base(Kind, DataSyncBaseState.Normal),
            Base(Kind, DataSyncBaseState.Normal, DataSyncPendingReason.Conflict),
            Base(Kind, DataSyncBaseState.Normal, DataSyncPendingReason.Held),
            Base(Kind, DataSyncBaseState.Held),
            Base(Kind, DataSyncBaseState.Held, DataSyncPendingReason.Held),
            Base(Kind, DataSyncBaseState.Excluded, exclusion: DataSyncExclusionReason.Skipped),
            Base(DataSyncKindIds.ExtensionGroup, DataSyncBaseState.MissingAtPeer),
            Base(DataSyncKindIds.ExtensionGroup, DataSyncBaseState.Unbound, DataSyncPendingReason.Retry),
        ], default);
        await _f.Store.UpsertBasesAsync(two.Id,
        [
            Base(Kind, DataSyncBaseState.Excluded, exclusion: DataSyncExclusionReason.NotSyncedHere),
            // A kind this build does not know is never counted.
            Base("futureKind", DataSyncBaseState.Excluded, exclusion: DataSyncExclusionReason.Skipped),
        ], default);

        var all = await _f.Store.CountBasesAsync(null, default);
        Assert.AreEqual(new DataSyncBaseCounts(Pending: 4, Excluded: 1, Held: 3, MissingAtPeer: 1), all[one.Id]);
        Assert.AreEqual(new DataSyncBaseCounts(0, 1, 0, 0), all[two.Id]);
        Assert.AreEqual(2, all.Count, "a link without bases has no entry");
        foreach (var link in new[] { one, two })
        {
            var bases = new List<DataSyncPeerBase>();
            foreach (var kind in DataSyncKindIds.All) bases.AddRange(await _f.Store.GetBasesAsync(link.Id, kind, default));
            Assert.AreEqual(new DataSyncBaseCounts(bases.Count(b => b.Pending is not null),
                    bases.Count(b => b.State == DataSyncBaseState.Excluded),
                    bases.Count(b => b.State == DataSyncBaseState.Held || b.Pending?.Reason == DataSyncPendingReason.Held),
                    bases.Count(b => b.State == DataSyncBaseState.MissingAtPeer)),
                all[link.Id], $"link {link.Id}");
        }

        var single = await _f.Store.CountBasesAsync(two.Id, default);
        Assert.AreEqual(all[two.Id], single.Single().Value);
    }

    [TestMethod]
    public async Task One_link_per_peer()
    {
        var link = await _f.LinkAsync("peer-1");
        Assert.IsTrue(link.Id > 0);
        Assert.AreEqual(link.Id, (await _f.Store.GetLinkByPeerAsync("peer-1", default))!.Id);

        link.PeerName = "renamed";
        await _f.Store.UpdateLinkAsync(link, default);
        Assert.AreEqual("renamed", (await _f.Db.DataSyncLinks.AsNoTracking().SingleAsync()).PeerName);

        await Assert.ThrowsExceptionAsync<DbUpdateException>(() => _f.LinkAsync("peer-1"));
    }

    [TestMethod]
    public async Task A_reader_row_is_written_on_the_first_read_on_a_change_and_every_10_minutes()
    {
        var reader = new DataSyncReader("node-pc2", "grant-1", "PC-2");
        var t0 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        DataSyncFeedQuery Query(string state) => new("twoWay", new Dictionary<string, long>(), null, state);

        await _f.Store.TouchReaderAsync(reader, Query("ok"), 10, t0, default);
        await _f.Store.TouchReaderAsync(reader, Query("ok"), 11, t0.AddMinutes(1), default);
        Assert.AreEqual(10, (await StoredReaderAsync()).LastSeqServed, "throttled");
        var shown = (await _f.Store.GetReadersAsync(default)).Single();
        Assert.AreEqual((11L, DateTimeKind.Utc), (shown.LastSeqServed, shown.LastReadAtUtc.Kind),
            "the latest read is shown anyway");

        await _f.Store.TouchReaderAsync(reader, Query("paused:ByUser"), 12, t0.AddMinutes(2), default);
        Assert.AreEqual("paused:ByUser", (await StoredReaderAsync()).State, "a declared state change is written");

        await _f.Store.TouchReaderAsync(reader, Query("paused:ByUser"), 13, t0.AddMinutes(5), default);
        Assert.AreEqual(12, (await StoredReaderAsync()).LastSeqServed);
        await _f.Store.TouchReaderAsync(reader, Query("paused:ByUser"), 14, t0.AddMinutes(12), default);
        var stored = await StoredReaderAsync();
        Assert.AreEqual(14, stored.LastSeqServed, "10 minutes after the last write");
        Assert.AreEqual(t0, stored.FirstReadAtUtc);
    }

    [TestMethod]
    public async Task History_entries_are_listed_newest_first()
    {
        var first = await _f.Store.AddHistoryAsync(Log(DataSyncHistoryKind.FirstLink), default);
        var second = await _f.Store.AddHistoryAsync(Log(DataSyncHistoryKind.AutoSync), default);

        CollectionAssert.AreEqual(new[] {second, first},
            (await _f.Store.GetHistoryAsync(default)).Select(l => l.Id).ToArray());
        var entry = (await _f.Store.GetHistoryEntryAsync(first, default))!;
        entry.UndoneAtUtc = DateTime.UtcNow;
        await _f.Db.SaveChangesAsync();
        Assert.IsNotNull((await _f.Store.GetHistoryAsync(default)).Single(l => l.Id == first).UndoneAtUtc);
    }

    [TestMethod]
    public async Task Attention_counts_decisions_pauses_restores_and_reviews_without_names()
    {
        var paused = await _f.LinkAsync("peer-1", state: DataSyncLinkState.Paused);
        await _f.LinkAsync("peer-2", state: DataSyncLinkState.AwaitingReview);
        await _f.Store.UpsertItemsAsync(paused.Id, "peer-1",
            [Draft(Kind, NewKey(), DataSyncInboxItemType.DeletedThere, DataSyncInboxItemOrigin.Merger)],
            DateTime.UtcNow, default);

        var attention = await _f.Store.GetAttentionAsync(default);
        Assert.AreEqual(new Bakabase.Modules.DataSync.Wire.DataSyncSourceAttention(false, 1, 1, false, 1), attention);

        var state = (await _f.Store.GetLocalStateAsync(default))!;
        state.RestoreReason = DataSyncPauseReason.LocalRestoreDetected;
        await _f.Store.SaveLocalStateAsync(state, default);
        ((TestDataSyncHostKind) _f.Services.GetRequiredService<IDataSyncHostKind>()).IsHeadless = true;
        attention = await _f.Store.GetAttentionAsync(default);
        Assert.IsTrue(attention.RestorePending && attention.Headless);
    }

    private Task<DataSyncReaderDbModel> StoredReaderAsync() => _f.Db.DataSyncReaders.AsNoTracking().SingleAsync();

    private static DataSyncApplyLogDbModel Log(DataSyncHistoryKind kind) => new()
    {
        Kind = kind, SummaryJson = "{}", ResultJson = "[]", PreImageJson = "{}",
    };
}
