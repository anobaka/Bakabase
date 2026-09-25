using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.Apply.DataSyncApplyFixture;

namespace Bakabase.Tests.DataSync.Apply;

/// <summary>
/// §9.5, the <c>DataSyncRestore</c> task bodies. "This device's definitions win" gives every live synced entity and
/// every served tombstone a <c>RestoreWins</c> revision over every vector this device knows of it — bases, pending
/// records, items — and the counters its retired actors recorded (gate fix B1(a)); "take the others'" raises nothing
/// and lets the first link's next full reconciliation follow the peer. Either way cursors reset, bases are kept, the
/// paused links resume and the restore is cleared. A restore suspected through one link is scoped to it.
/// </summary>
[TestClass]
public class RestoreTests
{
    private DataSyncApplyFixture _f = null!;
    private DataSyncPeer _peer = null!;
    private DataSyncLinkDbModel _link = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _f = await CreateAsync();
        _peer = new DataSyncPeer("PC-1");
        _link = await _f.LinkAsync(_peer);
    }

    /// <summary>A peer entity synced here, and one this device made and synced (so it carries this device's counter).</summary>
    private async Task<(string PeerKey, string LocalKey, DataSyncVersionVector PeerVv)> SyncedAsync()
    {
        var key = SyncKey.New().Value;
        var vv = _peer.Next();
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], vv, Content("Genre", ("a", "Rock")), "a0")));
        var mine = _f.Kind.Add(Content("Mood", ("m", "Calm")));
        await _f.RefreshAsync();
        return (key, mine, vv);
    }

    /// <summary>
    /// A restore the way the guard records it (§5.6): the actor rotates, the old one retires with its recorded
    /// counter, the links pause.
    /// </summary>
    private async Task<(string RetiredActor, long Recorded)> RestoreDetectedAsync(long lostCounters,
        DataSyncPauseReason reason = DataSyncPauseReason.LocalRestoreDetected, DataSyncLinkDbModel? only = null)
    {
        var db = _f.NewDb();
        var state = db.DataSyncLocalStates.Single();
        var retired = state.ActorId;
        var recorded = state.ActorCounter + lostCounters;
        var salt = DataSyncActorId.NewSalt();
        state.RetiredActorsJson = DataSyncStoredJson.WriteCounters(new Dictionary<string, long> { [retired] = recorded });
        state.ActorSalt = salt;
        state.ActorId = DataSyncActorId.Derive(state.NodeId, state.LibraryEpoch, salt).Value;
        state.ActorCounter = 0;
        state.ActorGeneration++;
        state.RestoreReason = reason;
        state.RestoreLinkId = only?.Id;
        state.RestoreDetectedAtUtc = _f.Now;
        foreach (var link in db.DataSyncLinks.Where(l => only == null || l.Id == only.Id))
        {
            link.State = DataSyncLinkState.Paused;
            link.PausedReason = reason;
        }

        await db.SaveChangesAsync();
        await _f.Services.GetRequiredService<DataSyncActorWatermarkFile>().WriteAsync(state, default);
        return (retired, recorded);
    }

    [TestMethod]
    public async Task This_device_wins_dominates_every_known_vector_and_the_retired_actors_recorded_counters()
    {
        var (peerKey, mine, peerVv) = await SyncedAsync();
        var mineKey = (await _f.RowAsync(mine)).SyncKey;
        // A pending record of the peer that this device never agreed to.
        _f.Kind.Definitions[_f.Kind.KeyOf("Genre")] = _f.Kind[_f.Kind.KeyOf("Genre")].With(name: "Style");
        var pendingVv = _peer.Next(peerVv);
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([peerKey], pendingVv, Content("Kind", ("a", "Rock")), "a0")));
        var (retired, recorded) = await RestoreDetectedAsync(lostCounters: 7);

        var logId = await _f.Runner.RunRestoreAsync(DataSyncRestoreChoice.ThisDeviceWins, null, _f.Args("DataSyncRestore"));

        Assert.IsNotNull(logId);
        var state = await _f.StateAsync();
        Assert.IsNull(state.RestoreReason);
        foreach (var key in new[] { peerKey, mineKey })
        {
            var vv = Vv((await _f.ByKeyAsync(key))!.VvJson);
            Assert.IsTrue(vv[new DataSyncActorId(retired)] >= recorded,
                "the counters the retired actor issued and lost are covered (B1(a))");
            Assert.IsTrue(vv[new DataSyncActorId(state.ActorId)] > 0, "+ self");
        }

        Assert.AreEqual(DataSyncVvRelation.Dominates,
            Vv((await _f.ByKeyAsync(peerKey))!.VvJson).CompareTo(pendingVv), "the pending record's vector is covered");
        var link = await _f.LinkRowAsync(_link.Id);
        Assert.AreEqual((DataSyncLinkState.Active, (DataSyncPauseReason?) null, "{}"),
            (link.State, link.PausedReason, link.CursorsJson), "resumed, cursors to 0");
        Assert.IsTrue((await _f.BasesAsync(_link.Id)).Any(b => b.SyncKey == peerKey), "bases are kept");
        Assert.AreEqual(DataSyncHistoryKind.Restore, (await _f.HistoryAsync()).Single(l => l.Id == logId).Kind);
    }

    [TestMethod]
    public async Task A_suspected_restore_is_scoped_to_its_link()
    {
        var (peerKey, mine, _) = await SyncedAsync();
        var mineRow = await _f.RowAsync(mine);
        var peerRow = await _f.ByKeyAsync(peerKey);
        var nas = new DataSyncPeer("NAS");
        var other = await _f.LinkAsync(nas);
        var otherCursors = "{\"testItem\":5}";
        var db = _f.NewDb();
        db.DataSyncLinks.Single(l => l.Id == other.Id).CursorsJson = otherCursors;
        await db.SaveChangesAsync();
        await RestoreDetectedAsync(lostCounters: 3, DataSyncPauseReason.LocalRestoreSuspected, _link);

        await _f.Runner.RunRestoreAsync(DataSyncRestoreChoice.ThisDeviceWins, _link.Id, _f.Args("DataSyncRestore"));

        Assert.AreEqual(DataSyncVvRelation.Dominates,
            Vv((await _f.ByKeyAsync(peerKey))!.VvJson).CompareTo(Vv(peerRow!.VvJson)), "the link's entity is revised");
        Assert.AreEqual(mineRow.VvJson, (await _f.RowAsync(mine)).VvJson,
            "only entities the suspected link has a base for are revised");
        Assert.AreEqual(otherCursors, (await _f.LinkRowAsync(other.Id)).CursorsJson, "only its cursors reset");
        Assert.AreEqual("{}", (await _f.LinkRowAsync(_link.Id)).CursorsJson);
        Assert.AreEqual(DataSyncLinkState.Active, (await _f.LinkRowAsync(_link.Id)).State);
    }

    [TestMethod]
    public async Task Others_win_raises_nothing_and_the_first_links_next_full_reconciliation_follows_the_peer()
    {
        var (peerKey, _, peerVv) = await SyncedAsync();
        var genre = _f.Kind.KeyOf("Genre");
        var before = await _f.RowAsync(genre);
        await RestoreDetectedAsync(lostCounters: 2);

        await _f.Runner.RunRestoreAsync(DataSyncRestoreChoice.OthersWin, null, _f.Args("DataSyncRestore"));

        Assert.AreEqual(before.VvJson, (await _f.RowAsync(genre)).VvJson, "no counters are raised");
        Assert.IsTrue(_f.Runner.TakesTheirsNext(_link.Id));

        // A concurrent local change meets the peer's in the full reconciliation: the peer's version is taken.
        _f.Kind.Definitions[genre] = _f.Kind[genre].With(name: "Mine");
        var theirs = _peer.Next(peerVv);
        await _f.ApplyAsync(_link, _peer, _f.Pull(_peer, full: true,
            (Item, _peer.Record([peerKey], theirs, Content("Theirs", ("a", "Rock")), "a0"))));

        Assert.AreEqual("Theirs", _f.Kind[genre].Name, "the Follow rule, for this cycle only");
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count, "no question");
        Assert.IsFalse(_f.Runner.TakesTheirsNext(_link.Id), "consumed");
    }

    [TestMethod]
    public async Task No_pending_restore_does_nothing()
    {
        await SyncedAsync();
        Assert.IsNull(await _f.Runner.RunRestoreAsync(DataSyncRestoreChoice.ThisDeviceWins, null, _f.Args("DataSyncRestore")));
        Assert.AreEqual(0, (await _f.HistoryAsync()).Count(l => l.Kind == DataSyncHistoryKind.Restore));
    }
}
