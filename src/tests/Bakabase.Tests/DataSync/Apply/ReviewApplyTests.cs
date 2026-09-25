using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.Apply.DataSyncApplyFixture;

namespace Bakabase.Tests.DataSync.Apply;

/// <summary>
/// §8.10.3 and §8.3 step 5: the first-link review and copy once, applied by the runner. Inside the transaction the
/// plan is made again and resolved non-strictly; what applied gets its revision and a base with the merge's child map,
/// held and changed items wait as pending records, skipped ones are remembered, and the link's cursors, first contact
/// and state are written last.
/// </summary>
[TestClass]
public class ReviewApplyTests
{
    private DataSyncApplyFixture _f = null!;
    private DataSyncPeer _peer = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _f = await CreateAsync();
        _peer = new DataSyncPeer("PC-1");
    }

    private async Task<DataSyncLinkDbModel> AwaitingReviewAsync(DataSyncLinkMode mode = DataSyncLinkMode.TwoWay)
    {
        var link = await _f.LinkAsync(_peer, mode, firstContactDone: false);
        var db = _f.NewDb();
        db.DataSyncLinks.Single(l => l.Id == link.Id).State = DataSyncLinkState.AwaitingReview;
        await db.SaveChangesAsync();
        return await _f.LinkRowAsync(link.Id);
    }

    /// <summary>Every item's default, and Link to the only candidate where the person must choose.</summary>
    private static List<DataSyncPlanDecision> Decide(DataSyncPlan plan, Func<DataSyncPlanItem, DataSyncPlanDecision?>? own = null)
    {
        var decisions = new List<DataSyncPlanDecision>();
        foreach (var item in plan.Kinds.SelectMany(k => k.Items))
        {
            if (own?.Invoke(item) is { } mine)
            {
                decisions.Add(mine);
                continue;
            }

            if (item.AllowedResolutions.Contains(DataSyncPlanResolution.Link) && item.Candidates.Count == 1)
            {
                var candidate = item.Candidates[0];
                decisions.Add(new DataSyncPlanDecision(item.ItemId, DataSyncPlanResolution.Link, candidate.LocalKey, null, [],
                    candidate.ReviewToken));
            }
        }

        return DataSyncPlanner.CompleteDecisions(plan, decisions).ToList();
    }

    private Task<int?> ApplyAsync(DataSyncReviewEntry review, IReadOnlyList<DataSyncPlanDecision> decisions) =>
        _f.Runner.RunReviewAsync(review.ReviewId, decisions, new DataSyncApplyOptions(false),
            _f.Args("DataSyncReview:" + review.ReviewId));

    [TestMethod]
    public async Task A_first_link_review_creates_links_and_records_bases_cursors_and_first_contact()
    {
        var link = await AwaitingReviewAsync();
        var genre = _f.Kind.Add(Content("Genre", ("x", "Rock")));
        await _f.RefreshAsync();
        var created = _peer.Record([SyncKey.New().Value], _peer.Next(), Content("Mood", ("m", "Calm")), "a0");
        var linked = _peer.Record([SyncKey.New().Value], _peer.Next(), Content("Genre", ("a", "Rock"), ("b", "Jazz")), "a1");
        var review = _f.Reviews.Stage(link.Id, false, _f.Pull(_peer, full: true, (Item, created), (Item, linked)));
        var plan = await _f.PlanAsync(review);

        var logId = await ApplyAsync(review, Decide(plan));

        Assert.IsNotNull(logId);
        var mood = _f.Kind.KeyOf("Mood");
        var moodRow = await _f.RowAsync(mood);
        Assert.AreEqual((created.Keys[0], true), (moodRow.SyncKey, moodRow.CreatedBySync));
        Assert.AreEqual(created.Vv, Vv(moodRow.VvJson), "an exact create takes the record's vector");
        CollectionAssert.Contains((await _f.KeysOfAsync(genre)).ToList(), linked.Keys[0], "linked: the peer's key is an alias");
        CollectionAssert.AreEquivalent(new[] { "Rock", "Jazz" }, _f.Kind[genre].Children.Select(c => c.Label).ToArray());

        var bases = await _f.BasesAsync(link.Id);
        Assert.AreEqual(2, bases.Count(b => b.State == DataSyncBaseState.Normal && b.PendingReason is null));
        var genreBase = bases.Single(b => b.SyncKey == (_f.RowAsync(genre).Result).SyncKey);
        Assert.AreEqual("x", DataSyncStoredJson.ReadChildMap(genreBase.ChildMapJson)["a"], "the merge's child map");

        var row = await _f.LinkRowAsync(link.Id);
        Assert.AreEqual(DataSyncLinkState.Active, row.State);
        Assert.IsNotNull(row.FirstContactCompletedAtUtc);
        Assert.AreEqual(Math.Max(created.Seq, linked.Seq), DataSyncStoredJson.ReadCounters(row.CursorsJson, "x")[Item]);
        Assert.AreEqual(DataSyncHistoryKind.FirstLink, (await _f.HistoryAsync()).Single(l => l.Id == logId).Kind);
        Assert.AreEqual(logId, _f.Reviews.Get(review.ReviewId)!.ApplyLogId);

        await _f.RefreshAsync();
        Assert.AreEqual(moodRow.Seq, (await _f.RowAsync(mood)).Seq, "no echo");
    }

    [TestMethod]
    public async Task Copy_once_leaves_the_link_stopped_and_skipped_items_are_remembered()
    {
        var link = await AwaitingReviewAsync(DataSyncLinkMode.Off);
        var skipped = _peer.Record([SyncKey.New().Value], _peer.Next(), Content("Skip me"), "a0");
        var copied = _peer.Record([SyncKey.New().Value], _peer.Next(), Content("Copy me"), "a1");
        var review = _f.Reviews.Stage(link.Id, true, _f.Pull(_peer, full: true, (Item, skipped), (Item, copied)));
        var plan = await _f.PlanAsync(review);
        var skipItem = plan.Kinds.SelectMany(k => k.Items).Single(i => i.Incoming.Name == "Skip me");

        var logId = await ApplyAsync(review, Decide(plan, item => item.ItemId == skipItem.ItemId
            ? new DataSyncPlanDecision(item.ItemId, DataSyncPlanResolution.Skip, null, null, [], item.ReviewToken)
            : null));

        Assert.AreEqual(DataSyncHistoryKind.CopyOnce, (await _f.HistoryAsync()).Single(l => l.Id == logId).Kind);
        Assert.IsTrue(_f.Kind.Definitions.Values.Any(d => d.Name == "Copy me"));
        Assert.IsFalse(_f.Kind.Definitions.Values.Any(d => d.Name == "Skip me"));
        var row = await _f.LinkRowAsync(link.Id);
        Assert.AreEqual((DataSyncLinkMode.Off, DataSyncLinkState.Stopped), (row.Mode, row.State));
        var excluded = (await _f.BasesAsync(link.Id)).Single(b => b.SyncKey == skipped.Keys[0]);
        Assert.AreEqual((DataSyncBaseState.Excluded, DataSyncExclusionReason.Skipped), (excluded.State, excluded.ExclusionReason));
    }

    [TestMethod]
    public async Task An_item_that_changed_since_the_review_waits_as_a_Retry_record_and_the_rest_applies()
    {
        var link = await AwaitingReviewAsync();
        var genre = _f.Kind.Add(Content("Genre", ("x", "Rock")));
        await _f.RefreshAsync();
        var linked = _peer.Record([SyncKey.New().Value], _peer.Next(), Content("Genre", ("a", "Rock"), ("b", "Jazz")), "a1");
        var created = _peer.Record([SyncKey.New().Value], _peer.Next(), Content("Mood"), "a0");
        var review = _f.Reviews.Stage(link.Id, false, _f.Pull(_peer, full: true, (Item, linked), (Item, created)));
        var decisions = Decide(await _f.PlanAsync(review));

        // The person decided; meanwhile the linked definition changed here, so linking would now change more.
        _f.Kind.Definitions[genre] = Content("Genre", ("x", "Rocks"));
        var logId = await ApplyAsync(review, decisions);

        Assert.IsNotNull(logId);
        Assert.IsTrue(_f.Kind.Definitions.Values.Any(d => d.Name == "Mood"), "the rest applied");
        Assert.AreEqual(1, _f.Kind[genre].Children.Count, "nothing written to the changed item (v3.1 B1)");
        var waiting = (await _f.BasesAsync(link.Id)).Single(b => b.PendingReason is not null);
        Assert.AreEqual(DataSyncPendingReason.Retry, waiting.PendingReason);
        StringAssert.Contains((await _f.HistoryAsync()).Single(l => l.Id == logId).ResultJson, "ChangedSinceReview");

        // The link's first ordinary pull merges it.
        var next = await _f.Runner.RunAutoSyncAsync(Context(await _f.LinkRowAsync(link.Id), _peer), null, _f.Args());
        Assert.IsTrue(next.NewInboxItems + next.Applied > 0);
    }

    [TestMethod]
    public async Task A_25000_child_local_definition_with_invalid_children_survives_an_update()
    {
        var link = await AwaitingReviewAsync();
        var children = Enumerable.Range(0, 25_000).Select(i => new TestChild("c" + i, "L" + i)).ToList();
        children.Add(new TestChild("bad-1", ""));
        children.Add(new TestChild(new string('u', 200), "Long id"));
        var tags = _f.Kind.Add(new TestItemContent("Tags", null, children));
        await _f.RefreshAsync();
        var record = _peer.Record([SyncKey.New().Value], _peer.Next(), Content("Tags", ("n", "New one")), "a0");
        var review = _f.Reviews.Stage(link.Id, false, _f.Pull(_peer, full: true, (Item, record)));

        await ApplyAsync(review, Decide(await _f.PlanAsync(review)));

        var after = _f.Kind[tags].Children;
        Assert.AreEqual(children.Count + 1, after.Count, "every local child is written back, valid or not (v3.1 B3)");
        CollectionAssert.AreEqual(children, after.Take(children.Count).ToList());
        Assert.AreEqual("New one", after[^1].Label);
    }

    [TestMethod]
    public async Task An_expired_review_ends_the_task_with_nothing_applied()
    {
        await AwaitingReviewAsync();
        await Assert.ThrowsExceptionAsync<BTaskException>(() => _f.Runner.RunReviewAsync("gone", [],
            new DataSyncApplyOptions(false), _f.Args("DataSyncReview:gone")));
    }
}
