using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.Apply.DataSyncApplyFixture;

namespace Bakabase.Tests.DataSync.Apply;

/// <summary>
/// The apply half of one cycle (§8.10.2) end to end on real SQLite: creates, fast-forwards, deletions and conflicts
/// from a peer's records, RecordApply's hashes and vectors, bases and pending records, the cursor, history and the
/// inbox.
/// </summary>
[TestClass]
public class AutoSyncApplyTests
{
    [TestMethod]
    public async Task A_peer_create_is_applied_with_the_records_keys_and_vector_and_Refresh_then_changes_nothing()
    {
        var f = await CreateAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer);
        var key = SyncKey.New().Value;
        var vv = peer.Next();
        var record = peer.Record([key], vv, Content("Genre", ("a", "Rock"), ("b", "Jazz")), "a0");

        var outcome = await f.ApplyAsync(link, peer, (Item, record));

        Assert.AreEqual(1, outcome.Applied);
        Assert.IsNotNull(outcome.ApplyLogId);
        var localKey = f.Kind.KeyOf("Genre");
        var row = await f.RowAsync(localKey);
        Assert.AreEqual(key, row.SyncKey, "the record's primary key (§5.1)");
        Assert.AreEqual(vv, Vv(row.VvJson), "an exact create takes the peer's vector");
        Assert.AreEqual((peer.NodeId, peer.ActorId), (row.LastEditorNodeId, row.LastActorId));
        Assert.IsTrue(row.CreatedBySync);
        Assert.AreEqual("a0", row.OrderKey);
        var bases = await f.BasesAsync(link.Id);
        Assert.AreEqual(1, bases.Count);
        Assert.AreEqual((key, DataSyncBaseState.Normal, (DataSyncPendingReason?) null),
            (bases[0].SyncKey, bases[0].State, bases[0].PendingReason));
        Assert.AreEqual(record.Seq.ToString(), DataSyncVersionVectorCursor(await f.LinkRowAsync(link.Id), Item));

        var seq = row.Seq;
        await f.RefreshAsync();
        var after = await f.RowAsync(localKey);
        Assert.AreEqual((row.VvJson, seq, row.SharedHash), (after.VvJson, after.Seq, after.SharedHash),
            "echo prevention: hashes came from the re-read (§6.4)");
    }

    [TestMethod]
    public async Task Pause_all_pressed_while_the_apply_waited_for_the_gate_applies_nothing()
    {
        var f = await CreateAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer);
        var record = peer.Record([SyncKey.New().Value], peer.Next(), Content("Genre", ("a", "Rock")), "a0");
        await f.RefreshAsync();
        await using (var db = f.NewDb())
        {
            var state = await db.DataSyncLocalStates.SingleAsync();
            state.AllPaused = true;
            await db.SaveChangesAsync();
        }

        var outcome = await f.ApplyAsync(link, peer, (Item, record));

        Assert.AreEqual((DataSyncPauseReason?) DataSyncPauseReason.AllPaused, outcome.Paused);
        Assert.AreEqual(0, outcome.Applied);
        Assert.IsFalse(f.Kind.Definitions.Values.Any(d => d.Name == "Genre"), "nothing is written");
        Assert.AreEqual(0, (await f.HistoryAsync()).Count);
        var row = await f.LinkRowAsync(link.Id);
        Assert.AreEqual((DataSyncLinkState.Active, "{}"), (row.State, row.CursorsJson), "the link is not paused itself");
    }

    [TestMethod]
    public async Task A_fast_forward_updates_the_entity_and_the_second_delivery_changes_nothing()
    {
        var f = await CreateAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer);
        var key = SyncKey.New().Value;
        var v1 = peer.Next();
        await f.ApplyAsync(link, peer, (Item, peer.Record([key], v1, Content("Genre", ("a", "Rock")), "a0")));
        var localKey = f.Kind.KeyOf("Genre");

        var v2 = peer.Next(v1);
        var pull = f.Pull(peer, (Item, peer.Record([key], v2, Content("Genres", ("a", "Rock"), ("b", "Jazz")), "a0")));
        var outcome = await f.ApplyAsync(link, peer, pull);

        Assert.AreEqual(1, outcome.Applied);
        Assert.AreEqual("Genres", f.Kind[localKey].Name);
        CollectionAssert.AreEqual(new[] { "Rock", "Jazz" }, f.Kind[localKey].Children.Select(c => c.Label).ToArray());
        var row = await f.RowAsync(localKey);
        Assert.AreEqual(v2, Vv(row.VvJson));
        var history = await f.HistoryAsync();
        Assert.AreEqual(2, history.Count);
        Assert.AreEqual(DataSyncHistoryKind.AutoSync, history[1].Kind);

        var again = await f.ApplyAsync(link, peer, pull);
        Assert.AreEqual(0, again.Applied, "the same pull delivered twice changes nothing (invariant I3)");
        Assert.AreEqual(2, (await f.HistoryAsync()).Count, "nothing applied, no history entry");
        Assert.AreEqual(row.Seq, (await f.RowAsync(localKey)).Seq);
    }

    [TestMethod]
    public async Task A_peer_deletion_applies_by_itself_only_under_8_6_and_otherwise_asks()
    {
        var f = await CreateAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer);
        var (genreKey, moodKey) = (SyncKey.New().Value, SyncKey.New().Value);
        var (v1, v2) = (peer.Next(), peer.Next());
        await f.ApplyAsync(link, peer, (Item, peer.Record([genreKey], v1, Content("Genre", ("a", "Rock")), "a0")),
            (Item, peer.Record([moodKey], v2, Content("Mood", ("m", "Calm")), "a1")));
        var (genre, mood) = (f.Kind.KeyOf("Genre"), f.Kind.KeyOf("Mood"));
        f.Kind.Values[mood] = 2;

        var outcome = await f.ApplyAsync(link, peer, (Item, peer.Tombstone([genreKey], peer.Next(v1))),
            (Item, peer.Tombstone([moodKey], peer.Next(v2))));

        Assert.IsFalse(f.Kind.Definitions.ContainsKey(genre), "created by sync, unused, unchanged: deleted by itself");
        Assert.IsNotNull((await f.ByKeyAsync(genreKey))!.DeletedAtUtc);
        Assert.IsTrue(f.Kind.Definitions.ContainsKey(mood), "a definition with values is never deleted without a decision");
        Assert.AreEqual(1, outcome.NewInboxItems);
        var asked = (await f.OpenItemsAsync()).Single();
        Assert.AreEqual((DataSyncInboxItemType.DeletedThere, moodKey), (asked.Type, asked.SyncKey));
        Assert.AreEqual(DataSyncHistoryKind.AutoSync, (await f.HistoryAsync()).Single(l => l.Id == outcome.ApplyLogId).Kind);
    }

    [TestMethod]
    public async Task Mass_deletion_pauses_the_link_before_anything_is_deleted()
    {
        var f = await CreateAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer);
        var records = Enumerable.Range(0, 11)
            .Select(i => peer.Record([SyncKey.New().Value], peer.Next(), Content("Item " + i), "a" + i)).ToList();
        await f.ApplyAsync(link, peer, records.Select(r => (Item, r)).ToArray());
        var cursor = (await f.LinkRowAsync(link.Id)).CursorsJson;

        var outcome = await f.ApplyAsync(link, peer,
            records.Select(r => (Item, peer.Tombstone(r.Keys, peer.Next(r.Vv)))).ToArray());

        Assert.AreEqual(DataSyncPauseReason.MassDeletion, outcome.Paused);
        Assert.AreEqual(11, f.Kind.Definitions.Count, "nothing deleted (§8.7 B2)");
        var row = await f.LinkRowAsync(link.Id);
        Assert.AreEqual((DataSyncLinkState.Paused, DataSyncPauseReason.MassDeletion), (row.State, row.PausedReason));
        Assert.AreEqual(cursor, row.CursorsJson, "the cursor did not move");
        Assert.AreEqual(1, (await f.HistoryAsync()).Count);

        var again = await f.ApplyAsync(link, peer, records.Select(r => (Item, r)).ToArray());
        Assert.AreEqual((DataSyncPauseReason?) DataSyncPauseReason.MassDeletion, again.Paused, "a paused link applies nothing");
    }

    [TestMethod]
    public async Task A_failed_apply_is_recorded_on_the_link_with_a_backoff_and_a_later_success_clears_it()
    {
        var f = await CreateAsync();
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer);
        var record = peer.Record([SyncKey.New().Value], peer.Next(), Content("Genre"), "a0");
        f.Kind.FailOn = op => op is CreateEntityOperation ? new InvalidOperationException("the disk is full") : null;

        var first = await f.ApplyAsync(link, peer, (Item, record));
        var second = await f.ApplyAsync(link, peer, (Item, record));

        Assert.AreEqual((0, (int?) null), (first.Applied, first.ApplyLogId), "the pull is dropped, the task does not fail");
        Assert.AreEqual(0, second.Applied);
        var failed = await f.LinkRowAsync(link.Id);
        Assert.AreEqual((2, DataSyncApplyRunner.ApplyFailedCode), (failed.ConsecutiveFailures, failed.LastErrorCode));
        StringAssert.Contains(failed.LastErrorDetail, "the disk is full");
        Assert.AreEqual(f.Now + TimeSpan.FromMinutes(2), failed.NextAttemptAtUtc, "the backoff grows");
        Assert.AreEqual("0", DataSyncVersionVectorCursor(failed, Item), "the cursor did not move");
        Assert.AreEqual(0, f.Kind.Definitions.Count);
        Assert.AreEqual(0, (await f.HistoryAsync()).Count);

        f.Kind.FailOn = null;
        var third = await f.ApplyAsync(link, peer, (Item, record));
        Assert.AreEqual(1, third.Applied);
        var cleared = await f.LinkRowAsync(link.Id);
        Assert.AreEqual((0, (string?) null, (string?) null),
            (cleared.ConsecutiveFailures, cleared.LastErrorCode, cleared.LastErrorDetail));
    }

    [TestMethod]
    public async Task A_stop_that_lands_right_after_the_commit_leaves_a_finished_apply()
    {
        using var cts = new CancellationTokenSource();
        var f = await CreateAsync(s => s.AddSingleton<IDataSyncApplyListener>(new StoppingListener(cts)));
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer);

        // The listeners run after the commit: the stop lands between the commit and the steps that follow it.
        var outcome = await f.Runner.RunAutoSyncAsync(Context(link, peer),
            f.Pull(peer, (Item, peer.Record([SyncKey.New().Value], peer.Next(), Content("Genre"), "a0"))),
            f.Args(ct: cts.Token));

        Assert.IsTrue(cts.IsCancellationRequested);
        Assert.AreEqual(1, outcome.Applied, "committed, and reported as applied rather than Cancelled");
        Assert.IsNotNull(outcome.ApplyLogId);
        Assert.AreEqual((await f.StateAsync()).ActorCounter,
            f.Services.GetRequiredService<Bakabase.InsideWorld.Business.Components.DataSync.Persistence.DataSyncActorWatermarkFile>()
                .Read().Watermark!.Counter, "actor.json follows the commit (§5.6)");
    }

    /// <summary>A person stopping the task the moment an apply has committed.</summary>
    private sealed class StoppingListener(CancellationTokenSource cts) : IDataSyncApplyListener
    {
        public void OnApplied(DataSyncAppliedEvent applied) => cts.Cancel();
    }

    private static string? DataSyncVersionVectorCursor(Bakabase.Modules.DataSync.Models.Db.DataSyncLinkDbModel link,
        string kind) =>
        Bakabase.InsideWorld.Business.Components.DataSync.Persistence.DataSyncStoredJson
            .ReadCounters(link.CursorsJson, "CursorsJson").GetValueOrDefault(kind).ToString();
}
