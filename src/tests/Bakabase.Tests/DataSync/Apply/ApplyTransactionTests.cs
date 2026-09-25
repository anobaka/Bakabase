using System.Text.Json;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Models.Db;
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
