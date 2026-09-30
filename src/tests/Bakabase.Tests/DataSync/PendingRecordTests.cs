using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.DataSyncStoreFixture;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// Pending records (spec §8.4, §4.1), at the store: a record this device did not agree to is stored once, on its
/// base, and items refer to it by hash without holding a copy; <c>GetPendingAsync</c> lists them in merge order. The
/// apply-side rows of §13.5 (the cursor moving past a <c>ChangedDuringApply</c> item, B5 records applying on Apply all
/// without a refetch) belong to the apply runner's tests.
/// </summary>
[TestClass]
public class PendingRecordTests
{
    private const string TestKind = "testKind";
    private DataSyncStoreFixture _f = null!;

    [TestInitialize]
    public async Task Setup() =>
        _f = await DataSyncStoreFixture.CreateAsync(s => s.AddScoped<IDataSyncKind>(_ => new MemoryDataSyncKind(TestKind)));

    [TestMethod]
    public async Task Every_reason_stores_the_record_once_and_no_item_holds_a_copy()
    {
        var link = await _f.LinkAsync("peer-1");
        const string marker = "CONTENT-MARKER-5d1c";
        foreach (var reason in Enum.GetValues<DataSyncPendingReason>())
        {
            var key = NewKey();
            var content = new JsonObject {["name"] = marker};
            var first = Record([key], Vv((ActorB, 1)), seq: 10, content: content);
            var newer = Record([key], Vv((ActorB, 2)), seq: 11, content: content);
            await _f.Store.UpsertBasesAsync(link.Id,
                [new DataSyncBaseUpdate(Kind, Key(key), DataSyncBaseState.Unbound, null, null, null, Pending(first, reason), false)],
                default);
            await _f.Store.UpsertBasesAsync(link.Id,
                [new DataSyncBaseUpdate(Kind, Key(key), DataSyncBaseState.Unbound, null, null, null, Pending(newer, reason), false)],
                default);
            await _f.Store.UpsertItemsAsync(link.Id, "peer-1",
                [Draft(Kind, key, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name",
                    recordVv: newer.Vv, recordHash: Pending(newer, reason).RecordHash)],
                DateTime.UtcNow, default);

            var rows = await _f.Db.DataSyncPeerBases.AsNoTracking().Where(b => b.SyncKey == key).ToListAsync();
            Assert.AreEqual(1, rows.Count, $"{reason}: one row per link and entity");
            Assert.AreEqual(11, rows[0].PendingSeq, $"{reason}: the newer record replaced the older one");

            var stored = (await _f.Store.GetBasesAsync(link.Id, Kind, default)).Single(b => b.Key.Value == key);
            Assert.AreEqual(reason, stored.Pending!.Reason);
            Assert.AreEqual(newer.Vv, stored.Pending.Record.Vv);
            Assert.AreEqual(marker, stored.Pending.Record.Content!["name"]!.GetValue<string>());
            Assert.AreEqual(Pending(newer, reason).RecordHash, stored.Pending.RecordHash);
        }

        foreach (var item in await _f.ItemsAsync())
        {
            Assert.IsFalse(item.PayloadJson.Contains(marker), "an item never holds a copy of the record");
            Assert.IsNotNull(item.RecordHash);
            Assert.IsNotNull(item.RecordVvJson);
        }
    }

    [TestMethod]
    public async Task A_pending_record_round_trips_with_its_flags_and_clears()
    {
        var link = await _f.LinkAsync("peer-1");
        var entity = await _f.LiveAsync("1");
        var flags = new DataSyncMergeFlags(DataSyncChildDeletionMode.ReviewEach);
        var agreed = Record([entity.SyncKey], Vv((ActorB, 1)));
        var waiting = Record([entity.SyncKey], Vv((ActorB, 2)), seq: 5);
        await _f.Store.UpsertBasesAsync(link.Id,
        [
            new DataSyncBaseUpdate(Kind, Key(entity.SyncKey), DataSyncBaseState.Normal, null, agreed,
                new Dictionary<string, string> {["p1"] = "l1"}, Pending(waiting, DataSyncPendingReason.Conflict, flags),
                false),
        ], default);

        var stored = (await _f.Store.GetBasesAsync(link.Id, Kind, default)).Single();
        Assert.AreEqual(agreed.Vv, stored.Vv);
        Assert.AreEqual(agreed.Vv, stored.Record!.Vv);
        Assert.AreEqual("l1", stored.ChildMap["p1"]);
        Assert.AreEqual(flags, stored.Pending!.Flags);

        // Clearing the pending record keeps the agreement.
        await _f.Store.UpsertBasesAsync(link.Id,
            [new DataSyncBaseUpdate(Kind, Key(entity.SyncKey), DataSyncBaseState.Normal, null, null, null, null, true)],
            default);
        stored = (await _f.Store.GetBasesAsync(link.Id, Kind, default)).Single();
        Assert.IsNull(stored.Pending);
        Assert.AreEqual(agreed.Vv, stored.Vv);
        Assert.AreEqual("l1", stored.ChildMap["p1"]);
    }

    [TestMethod]
    public async Task An_exclusion_collects_the_record_keys_and_keeps_the_last_agreement()
    {
        var link = await _f.LinkAsync("peer-1");
        var entity = await _f.LiveAsync("1");
        var agreed = Record([entity.SyncKey], Vv((ActorB, 1)));
        await _f.Store.UpsertBasesAsync(link.Id,
            [new DataSyncBaseUpdate(Kind, Key(entity.SyncKey), DataSyncBaseState.Normal, null, agreed, null, null, false)],
            default);
        var other = NewKey();

        await _f.Store.UpsertBasesAsync(link.Id,
        [
            new DataSyncBaseUpdate(Kind, Key(entity.SyncKey), DataSyncBaseState.Excluded,
                DataSyncExclusionReason.NotSyncedHere, Record([entity.SyncKey, other], Vv((ActorB, 2))), null, null, true),
        ], default);

        var stored = (await _f.Store.GetBasesAsync(link.Id, Kind, default)).Single();
        Assert.AreEqual(DataSyncExclusionReason.NotSyncedHere, stored.Exclusion);
        CollectionAssert.AreEquivalent(new[] {entity.SyncKey, other}, stored.ExclusionKeys.ToArray());
        Assert.AreEqual(agreed.Vv, stored.Vv, "an exclusion is not an agreement");
    }

    [TestMethod]
    public async Task An_agreed_record_gets_the_comparison_form_hash_of_this_build()
    {
        var link = await _f.LinkAsync("peer-1");
        var key = NewKey();
        var content = new JsonObject {["name"] = "Genre"};
        var record = Record([key], Vv((ActorB, 1)), content: content) with {OrderKey = "a0"};

        await _f.Store.UpsertBasesAsync(link.Id,
            [new DataSyncBaseUpdate(TestKind, Key(key), DataSyncBaseState.Normal, null, record, null, null, false)],
            default);
        await _f.Store.UpsertBasesAsync(link.Id,
        [
            new DataSyncBaseUpdate(Kind, Key(key), DataSyncBaseState.Normal, null, record, null, null, false),
        ], default);

        var rows = await _f.Db.DataSyncPeerBases.AsNoTracking().Where(b => b.SyncKey == key).ToListAsync();
        Assert.AreEqual(ContentHash.Of(new JsonObject {["name"] = "Genre", ["orderKey"] = "a0"}),
            rows.Single(r => r.Kind == TestKind).SharedHash);
        Assert.IsNull(rows.Single(r => r.Kind == Kind).SharedHash, "no codec is registered for that kind here");
    }

    [TestMethod]
    public async Task Every_pending_record_is_listed_in_merge_order()
    {
        var link = await _f.LinkAsync("peer-1");
        var keys = new List<string>();
        foreach (var (reason, seq) in new[] { (DataSyncPendingReason.Conflict, 3L), (DataSyncPendingReason.Retry, 1L),
                     (DataSyncPendingReason.Held, 2L) })
        {
            var entity = await _f.LiveAsync(seq.ToString());
            keys.Add(entity.SyncKey);
            await _f.Store.UpsertBasesAsync(link.Id,
            [
                new DataSyncBaseUpdate(Kind, Key(entity.SyncKey), DataSyncBaseState.Normal, null, null, null,
                    Pending(Record([entity.SyncKey], Vv((ActorB, 1)), seq), reason), false),
            ], default);
        }

        var pending = await _f.Store.GetPendingAsync(link.Id, default);
        CollectionAssert.AreEqual(new[] { keys[1], keys[2], keys[0] }, pending.Select(p => p.Key.Value).ToList(),
            "by the record's Seq");
        Assert.AreEqual(DataSyncPendingReason.Retry, pending[0].Reason);
    }
}
