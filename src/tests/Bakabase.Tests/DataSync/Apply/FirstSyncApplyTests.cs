using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.Apply.DataSyncApplyFixture;

namespace Bakabase.Tests.DataSync.Apply;

/// <summary>
/// §8.3: a first sync's Start — the ordinary merge of the previewed snapshot, applied by the runner with the person's
/// choices while the link awaits it — and a copy once, which also stops the link in the same transaction.
/// </summary>
[TestClass]
public class FirstSyncApplyTests
{
    private DataSyncApplyFixture _f = null!;
    private DataSyncPeer _peer = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _f = await CreateAsync();
        _peer = new DataSyncPeer("PC-1");
    }

    private async Task<DataSyncLinkDbModel> AwaitingStartAsync(DataSyncLinkMode mode = DataSyncLinkMode.TwoWay)
    {
        var link = await _f.LinkAsync(_peer, mode, firstContactDone: false);
        await _f.SetLinkStateAsync(link.Id, DataSyncLinkState.AwaitingReview);
        return await _f.LinkRowAsync(link.Id);
    }

    private Task<DataSyncAutoSyncOutcome> StartAsync(DataSyncLinkDbModel link, DataSyncStagedPull pull,
        params DataSyncFirstSyncChoice[] choices) =>
        _f.Runner.RunAutoSyncAsync(link.Id, pull, _f.Args(DataSyncTaskIds.Review(link.Id)), choices);

    private DataSyncWireRecord New(string name, params (string Id, string Label)[] children) =>
        _peer.Record([SyncKey.New().Value], _peer.Next(), Content(name, children), "a0");

    [TestMethod]
    public async Task Start_merges_the_snapshot_asks_about_name_matches_and_completes_the_first_contact()
    {
        var link = await AwaitingStartAsync();
        _f.Kind.Add(Content("Genre", ("x", "Rock")));
        await _f.RefreshAsync();
        var created = New("Mood", ("m", "Calm"));
        var named = New("Genre", ("a", "Rock"), ("b", "Jazz"));

        var outcome = await StartAsync(link, _f.Pull(_peer, full: true, (Item, created), (Item, named)));

        Assert.AreEqual((DataSyncAutoSyncEnd.Committed, true), (outcome.End, outcome.FirstSync));
        var mood = await _f.RowAsync(_f.Kind.KeyOf("Mood"));
        Assert.AreEqual((created.Keys[0], true, created.Vv), (mood.SyncKey, mood.CreatedBySync, Vv(mood.VvJson)));
        Assert.AreEqual(DataSyncInboxItemType.LinkSuggestion, (await _f.OpenItemsAsync()).Single().Type,
            "a name match is asked under Needs you, never linked");
        var row = await _f.LinkRowAsync(link.Id);
        Assert.AreEqual(DataSyncLinkState.Active, row.State);
        Assert.IsNotNull(row.FirstContactCompletedAtUtc);
        Assert.AreEqual(named.Seq, DataSyncStoredJson.ReadCounters(row.CursorsJson, "x")[Item]);
        Assert.AreEqual(DataSyncHistoryKind.FirstLink, (await _f.HistoryAsync()).Single().Kind);

        await _f.RefreshAsync();
        Assert.AreEqual(mood.Seq, (await _f.RowAsync(mood.LocalKey)).Seq, "no echo");
    }

    /// <summary>D14: a definition already bound by key takes the peer's newer version as it is, never over it.</summary>
    [TestMethod]
    public async Task A_definition_bound_by_key_takes_a_newer_version_without_dominating_it()
    {
        var link = await AwaitingStartAsync();
        var genre = _f.Kind.Add(Content("Genre", ("x", "Rock"), ("y", "Jazz")));
        await _f.RefreshAsync();
        var row = await _f.RowAsync(genre);
        var newer = _peer.Record([row.SyncKey], _peer.Next(Vv(row.VvJson)),
            Content("Genres", ("x", "Rock"), ("y", "Jazz")), "a0");

        await StartAsync(link, _f.Pull(_peer, full: true, (Item, newer)));

        Assert.AreEqual("Genres", _f.Kind[genre].Name);
        Assert.AreEqual(newer.Vv, Vv((await _f.RowAsync(genre)).VvJson));
    }

    [TestMethod]
    public async Task A_copy_once_takes_its_preview_answers_remembers_skips_and_stops()
    {
        var link = await AwaitingStartAsync(DataSyncLinkMode.Off);
        var genre = _f.Kind.Add(Content("Genre", ("x", "Rock")));
        await _f.RefreshAsync();
        var skipped = New("Skip me");
        var copied = New("Copy me");
        var named = New("Genre", ("a", "Rock"), ("b", "Jazz"));

        await StartAsync(link, _f.Pull(_peer, full: true, (Item, skipped), (Item, copied), (Item, named)),
            new DataSyncFirstSyncChoice(Item, skipped.Keys[0], DataSyncFirstSyncAction.Skip),
            new DataSyncFirstSyncChoice(Item, named.Keys[0], DataSyncFirstSyncAction.Link, genre));

        Assert.AreEqual(DataSyncHistoryKind.CopyOnce, (await _f.HistoryAsync()).Single().Kind);
        Assert.IsTrue(_f.Kind.Definitions.Values.Any(d => d.Name == "Copy me"));
        Assert.IsFalse(_f.Kind.Definitions.Values.Any(d => d.Name == "Skip me"));
        CollectionAssert.Contains((await _f.KeysOfAsync(genre)).ToList(), named.Keys[0], "linked as answered");
        CollectionAssert.AreEquivalent(new[] { "Rock", "Jazz" }, _f.Kind[genre].Children.Select(c => c.Label).ToArray());
        var row = await _f.LinkRowAsync(link.Id);
        Assert.AreEqual((DataSyncLinkMode.Off, DataSyncLinkState.Stopped), (row.Mode, row.State));
        var excluded = (await _f.BasesAsync(link.Id)).Single(b => b.SyncKey == skipped.Keys[0]);
        Assert.AreEqual((DataSyncBaseState.Excluded, DataSyncExclusionReason.Skipped), (excluded.State, excluded.ExclusionReason));
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count, "nothing waits on a stopped link");
    }

    /// <summary>D8: whether it is a copy once is the link's mode read in the transaction, not what it was at Start.</summary>
    [TestMethod]
    [DataRow(DataSyncLinkMode.Follow)]
    [DataRow(DataSyncLinkMode.TwoWay)]
    public async Task A_copy_once_turned_into_a_link_before_its_start_ran_is_the_links_first_sync(
        DataSyncLinkMode turnedInto)
    {
        var link = await AwaitingStartAsync(DataSyncLinkMode.Off);
        var db = _f.NewDb();
        var stored = db.DataSyncLinks.Single(l => l.Id == link.Id);
        stored.Mode = turnedInto;
        stored.LastMode = turnedInto;
        await db.SaveChangesAsync();

        await StartAsync(link, _f.Pull(_peer, full: true, (Item, New("Copy me"))));

        Assert.AreEqual(DataSyncHistoryKind.FirstLink, (await _f.HistoryAsync()).Single().Kind);
        var after = await _f.LinkRowAsync(link.Id);
        Assert.AreEqual((turnedInto, DataSyncLinkState.Active), (after.Mode, after.State), "it keeps running");
    }

    [TestMethod]
    public async Task A_start_for_a_link_that_no_longer_awaits_it_applies_nothing()
    {
        var link = await _f.LinkAsync(_peer);

        var outcome = await StartAsync(link, _f.Pull(_peer, full: true, (Item, New("Mood"))));

        Assert.AreEqual(DataSyncAutoSyncEnd.NotApplied, outcome.End);
        Assert.IsFalse(_f.Kind.Definitions.Values.Any(d => d.Name == "Mood"));
        Assert.AreEqual(0, (await _f.HistoryAsync()).Count);
    }
}
