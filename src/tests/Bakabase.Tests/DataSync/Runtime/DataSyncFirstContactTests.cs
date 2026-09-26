using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.DataSync.Runtime;

/// <summary>
/// First contact on both sides (§8.3): the initiator stages its review once and says so once; the approver waits for
/// the initiator's review through the head's counterpart without building a snapshot, then runs the ordinary merge
/// with B5 skipped; a kind added to a link gets a first contact of its own.
/// </summary>
[TestClass]
public class DataSyncFirstContactTests
{
    private static Action<Bakabase.Modules.DataSync.Models.Db.DataSyncLinkDbModel> BeforeFirstContact(
        DataSyncLinkState state, DataSyncLinkInitiator initiator) => l =>
    {
        l.State = state;
        l.Initiator = initiator;
        l.FirstContactKindsJson = null;
        l.FirstContactCompletedAtUtc = null;
        l.LastFullReconciliationAtUtc = null;
        l.PeerLibraryEpoch = null;
        l.CursorsJson = "{}";
    };

    [TestMethod]
    public async Task The_initiator_stages_its_review_once_and_says_so_once()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("nas", BeforeFirstContact(DataSyncLinkState.AwaitingReview, DataSyncLinkInitiator.ThisDevice));
        var peer = h.Peers.Peers["nas"];

        await h.FetchOnceAsync();
        var review = h.Reviews.Staged.Single();
        Assert.AreEqual(link.Id, review.LinkId);
        Assert.IsFalse(review.CopyOnce);
        CollectionAssert.AreEquivalent(DataSyncKindIds.All.ToArray(), review.Pull.Kinds.Select(k => k.Kind).ToArray());
        Assert.IsTrue(review.Pull.Kinds.All(k => k.FullReconciliation));
        Assert.IsTrue(peer.ManifestQueries.Single().Since.Values.All(s => s == 0), "a full snapshot");
        Assert.AreEqual(review.ReviewId, h.Link(link.Id).ReviewId);
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, h.Link(link.Id).State, "the review decides");
        Assert.AreEqual("epoch-1", h.Link(link.Id).PeerLibraryEpoch);
        Assert.AreEqual(0, h.StagedPulls.LinksWaiting().Count, "a review is never applied automatically");
        Assert.AreEqual(1, h.Observer.Count("review:"));

        // A current review is never replaced by a cycle.
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        peer.MaxSeq["customProperty"] = 40;
        await h.FetchOnceAsync();
        Assert.AreEqual(1, h.Reviews.Staged.Count);
        Assert.AreEqual(1, peer.Manifests);
        Assert.AreEqual(2, peer.HeadQueries.Count, "the head is still polled");
        Assert.AreEqual(1, h.Observer.Count("review:"));

        // Expired after 60 min idle, or lost on a restart: the next cycle fetches again.
        h.Reviews.Expire(link.Id);
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        await h.FetchOnceAsync();
        Assert.AreEqual(2, h.Reviews.Staged.Count);
        Assert.AreEqual(2, h.Observer.Count("review:"));
    }

    [TestMethod]
    public async Task Links_awaiting_their_review_each_fetch_it_once_however_many_there_are()
    {
        // The real store: with more links awaiting a review than it kept before, the reviews evicted each other, and
        // every cycle fetched a full snapshot and announced it again for each link (§8.3, §9.4).
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false,
            configure: s => s.AddSingleton<IDataSyncReviewStore>(sp =>
                new DataSyncReviewStore(new ClockTime(sp.GetRequiredService<IDataSyncClock>()))));
        var peers = new[] {"a", "b", "c", "d"};
        foreach (var peer in peers)
            h.AddLink(peer, BeforeFirstContact(DataSyncLinkState.AwaitingReview, DataSyncLinkInitiator.ThisDevice));

        for (var cycle = 0; cycle < 3; cycle++)
        {
            await h.FetchOnceAsync();
            h.Clock.Advance(DataSyncSchedule.PollInterval);
        }

        foreach (var peer in peers) Assert.AreEqual(1, h.Peers.Peers[peer].Manifests, $"{peer}: one full snapshot");
        Assert.AreEqual(peers.Length, h.Observer.Count("review:"));
        var reviews = h.Provider.GetRequiredService<IDataSyncReviewStore>();
        foreach (var link in h.Store.All())
            Assert.AreEqual(link.ReviewId, reviews.PeekForLink(link.Id)?.ReviewId, "each link keeps its own review");

        // The cycle's polling does not keep a review nobody reads: after an idle hour each is fetched once more.
        h.Clock.Advance(DataSyncReviewStore.IdleTimeout);
        await h.FetchOnceAsync();
        foreach (var peer in peers) Assert.AreEqual(2, h.Peers.Peers[peer].Manifests, peer);
    }

    /// <summary>The runtime's clock as the review store reads time.</summary>
    private sealed class ClockTime(IDataSyncClock clock) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc));
    }

    [TestMethod]
    public async Task A_copy_once_is_staged_as_a_copy_once_review()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        h.Grants.Outbound.Add("nas");
        h.Peers.Add("nas");
        var created = await h.Links.CreateAsync(new DataSyncLinkCreate("nas", null, null, DataSyncLinkMode.Follow,
            [DataSyncKindIds.ExtensionGroup], CopyOnce: true), false, default);
        Assert.IsNull(created.Problem, "a copy once from a peer this device reads sends nothing");
        Assert.AreEqual(DataSyncLinkMode.Off, created.Link!.Mode);
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, created.Link.State);

        await h.FetchOnceAsync();
        var review = h.Reviews.Staged.Single();
        Assert.IsTrue(review.CopyOnce);
        CollectionAssert.AreEqual(new[] {DataSyncKindIds.ExtensionGroup}, review.Pull.Kinds.Select(k => k.Kind).ToArray());
        Assert.AreEqual("follow", h.Peers.Peers["nas"].HeadQueries.Single().Mode, "a copy once reads like Follow");
    }

    [TestMethod]
    public async Task The_approver_waits_for_the_counterpart_then_runs_the_ordinary_merge_as_its_first_pull()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("pc", BeforeFirstContact(DataSyncLinkState.WaitingForPeerReview, DataSyncLinkInitiator.Peer));
        var peer = h.Peers.Peers["pc"];
        peer.Counterpart = new DataSyncFeedCounterpart("twoWay", false, DataSyncKindIds.All);

        await h.FetchOnceAsync();
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        await h.FetchOnceAsync();
        Assert.AreEqual(0, peer.Manifests, "a waiting link never builds a snapshot");
        Assert.AreEqual(2, peer.HeadQueries.Count);
        Assert.IsTrue(peer.HeadQueries.All(q => q.ReaderState == "awaitingReview"));
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, h.Link(link.Id).State);

        peer.Counterpart = new DataSyncFeedCounterpart("twoWay", true, DataSyncKindIds.All);
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        await h.FetchOnceAsync();
        Assert.AreEqual(DataSyncLinkState.Active, h.Link(link.Id).State);
        Assert.AreEqual(0, h.Reviews.Staged.Count, "only the initiator reviews (deviation 5)");
        Assert.IsTrue(peer.ManifestQueries.Single().Since.Values.All(s => s == 0));
        var pull = h.StagedPulls.Peek(link.Id)!;
        Assert.AreEqual(2, pull.Kinds.Count);

        // The first apply skips B5 for every kind, and completes the first contact.
        await h.Btm.Start(DataSyncTaskIds.Apply);
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);
        var first = h.Runner.AutoSyncs.Single();
        CollectionAssert.AreEquivalent(DataSyncKindIds.All.ToArray(), first.Context.FirstContactKinds.ToArray());
        Assert.AreEqual(DataSyncLinkMode.TwoWay, first.Context.EffectiveMode);
        var after = h.Link(link.Id);
        Assert.IsNotNull(after.FirstContactCompletedAtUtc);
        CollectionAssert.AreEquivalent(DataSyncKindIds.All.ToArray(), after.GetFirstContactKinds().ToArray());
        Assert.IsNotNull(after.LastFullReconciliationAtUtc, "a first pull from 0 is a full reconciliation");
        Assert.AreEqual(1, h.Observer.Count($"applied:{link.Id}:first"), "First sync with X, once");

        // The next pull is ordinary.
        h.Store.Edit(link.Id, l => l.SetCursors(new Dictionary<string, long>
            {["extensionGroup"] = 3, ["customProperty"] = 5}));
        peer.MaxSeq["customProperty"] = 8;
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        await h.FetchOnceAsync();
        await h.Btm.Start(DataSyncTaskIds.Apply);
        await DataSyncRuntimeHarness.WaitUntilAsync(() => h.Runner.AutoSyncs.Count == 2, "the second pull is applied");
        Assert.AreEqual(0, h.Runner.AutoSyncs.Last().Context.FirstContactKinds.Count);
        Assert.AreEqual(1, h.Observer.Count($"applied:{link.Id}:next"));
    }

    [TestMethod]
    public async Task A_kind_added_to_an_active_link_gets_a_first_contact_of_its_own()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        void OneKind(Bakabase.Modules.DataSync.Models.Db.DataSyncLinkDbModel l)
        {
            l.SetKinds([DataSyncKindIds.ExtensionGroup]);
            l.SetFirstContactKinds([DataSyncKindIds.ExtensionGroup]);
            l.SetCursors(new Dictionary<string, long> {["extensionGroup"] = 3});
        }

        var initiator = h.AddLink("a", OneKind);
        var approver = h.AddLink("b", l =>
        {
            OneKind(l);
            l.Initiator = DataSyncLinkInitiator.Peer;
        });
        h.Grants.Readers.Add(new Bakabase.Modules.DataSync.Services.DataSyncGrantView("a", "A", h.Clock.UtcNow));
        h.Grants.Readers.Add(new Bakabase.Modules.DataSync.Services.DataSyncGrantView("b", "B", h.Clock.UtcNow));
        foreach (var link in new[] {initiator, approver})
        {
            var updated = await h.Links.UpdateAsync(link.Id, null, DataSyncKindIds.All, true, default);
            Assert.IsNull(updated.Problem);
            Assert.AreEqual(DataSyncLinkState.Active, updated.Link!.State);
        }

        await h.FetchOnceAsync();

        // The initiator reviews the added kind only; the kind it already syncs has nothing new.
        var review = h.Reviews.Staged.Single();
        Assert.AreEqual(initiator.Id, review.LinkId);
        CollectionAssert.AreEqual(new[] {DataSyncKindIds.CustomProperty}, review.Pull.Kinds.Select(k => k.Kind).ToArray());
        var initiatorQuery = h.Peers.Peers["a"].ManifestQueries.Single();
        CollectionAssert.AreEqual(new[] {DataSyncKindIds.CustomProperty}, initiatorQuery.Since.Keys.ToArray());
        Assert.IsNull(h.StagedPulls.Peek(initiator.Id));

        // The approver merges it as usual, from 0, with B5 skipped for that kind.
        var pull = h.StagedPulls.Peek(approver.Id)!;
        CollectionAssert.AreEqual(new[] {DataSyncKindIds.CustomProperty}, pull.Kinds.Select(k => k.Kind).ToArray());
        await h.Btm.Start(DataSyncTaskIds.Apply);
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);
        CollectionAssert.AreEqual(new[] {DataSyncKindIds.CustomProperty},
            h.Runner.AutoSyncs.Single().Context.FirstContactKinds.ToArray());
        CollectionAssert.AreEquivalent(DataSyncKindIds.All.ToArray(), h.Link(approver.Id).GetFirstContactKinds().ToArray());
        Assert.AreEqual(1, h.Observer.Count($"applied:{approver.Id}:next"), "not the link's first sync");
    }
}
