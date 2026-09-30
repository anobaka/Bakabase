using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.DataSync.Runtime;

/// <summary>
/// First contact on both sides (§8.3): the initiator stages its first sync's snapshot once, for its person's Start, and
/// says so once; the approver waits for the initiator's first sync through the head's counterpart without building a
/// snapshot, then runs the ordinary merge; a kind added to a link is merged as usual.
/// </summary>
[TestClass]
public class DataSyncFirstContactTests
{
    private static Action<Bakabase.Modules.DataSync.Models.Db.DataSyncLinkDbModel> BeforeFirstContact(
        DataSyncLinkState state, DataSyncLinkInitiator initiator) => l =>
    {
        l.State = state;
        l.Initiator = initiator;
        l.FirstContactCompletedAtUtc = null;
        l.LastFullReconciliationAtUtc = null;
        l.PeerLibraryEpoch = null;
        l.CursorsJson = "{}";
    };

    [TestMethod]
    public async Task The_initiator_stages_its_first_sync_once_and_says_so_once()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("nas", BeforeFirstContact(DataSyncLinkState.AwaitingReview, DataSyncLinkInitiator.ThisDevice));
        var peer = h.Peers.Peers["nas"];

        await h.FetchOnceAsync();
        var preview = h.State.PeekPreview(link.Id)!;
        CollectionAssert.AreEquivalent(DataSyncKindIds.All.ToArray(), preview.Kinds.Select(k => k.Kind).ToArray());
        Assert.IsTrue(preview.Kinds.All(k => k.FullReconciliation));
        Assert.IsTrue(peer.ManifestQueries.Single().Since.Values.All(s => s == 0), "a full snapshot");
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, h.Link(link.Id).State, "the person's Start decides");
        Assert.AreEqual("epoch-1", h.Link(link.Id).PeerLibraryEpoch);
        Assert.AreEqual(0, h.State.StagedLinks().Count, "a first sync is never applied automatically");
        Assert.AreEqual(1, h.Observer.Count("review:"));

        // A staged first sync is never replaced by a cycle.
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        peer.MaxSeq["customProperty"] = 40;
        await h.FetchOnceAsync();
        Assert.AreSame(preview, h.State.PeekPreview(link.Id));
        Assert.AreEqual(1, peer.Manifests);
        Assert.AreEqual(2, peer.HeadQueries.Count, "the head is still polled");
        Assert.AreEqual(1, h.Observer.Count("review:"));
    }

    [TestMethod]
    public async Task A_copy_once_is_staged_for_its_start()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        h.Grants.Outbound.Add("nas");
        h.Peers.Add("nas");
        var created = await h.Links.CreateAsync(new DataSyncLinkCreate("nas", null, null, DataSyncLinkMode.Follow,
            [DataSyncKindIds.ExtensionGroup], CopyOnce: true), false, default);
        Assert.IsNull(created.Problem, "a copy once from a peer this device reads sends nothing");
        Assert.AreEqual((DataSyncLinkMode.Off, DataSyncLinkState.AwaitingReview), (created.Link!.Mode, created.Link.State));

        await h.FetchOnceAsync();
        CollectionAssert.AreEqual(new[] {DataSyncKindIds.ExtensionGroup},
            h.State.PeekPreview(created.Link.Id)!.Kinds.Select(k => k.Kind).ToArray());
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
        Assert.IsTrue(peer.HeadQueries.All(q => q.ReaderState == "waitingForPeerReview"),
            "it waits for the initiator's first sync, and says so");
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, h.Link(link.Id).State);

        peer.Counterpart = new DataSyncFeedCounterpart("twoWay", true, DataSyncKindIds.All);
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        await h.FetchOnceAsync();
        Assert.AreEqual(DataSyncLinkState.Active, h.Link(link.Id).State);
        Assert.IsNull(h.State.PeekPreview(link.Id), "only the initiator previews (deviation 5)");
        Assert.IsTrue(peer.ManifestQueries.Single().Since.Values.All(s => s == 0));
        var pull = h.State.PeekPull(link.Id)!;
        Assert.AreEqual(2, pull.Kinds.Count);

        // The first apply completes the first contact.
        await h.Btm.Start(DataSyncTaskIds.Apply);
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);
        var first = h.Runner.AutoSyncs.Single();
        Assert.IsNull(first.Link.FirstContactCompletedAtUtc);
        Assert.AreEqual(DataSyncLinkMode.TwoWay, first.Link.GetEffectiveMode());
        var after = h.Link(link.Id);
        Assert.IsNotNull(after.FirstContactCompletedAtUtc);
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
        Assert.AreEqual(1, h.Observer.Count($"applied:{link.Id}:next"));
    }

    [TestMethod]
    public async Task A_kind_added_to_an_active_link_is_merged_as_usual_on_either_side()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        void OneKind(Bakabase.Modules.DataSync.Models.Db.DataSyncLinkDbModel l)
        {
            l.SetKinds([DataSyncKindIds.ExtensionGroup]);
            l.SetCursors(new Dictionary<string, long> {["extensionGroup"] = 3});
        }

        var initiator = h.AddLink("a", OneKind);
        var approver = h.AddLink("b", l =>
        {
            OneKind(l);
            l.Initiator = DataSyncLinkInitiator.Peer;
        });
        h.Grants.Readers.Add(new Bakabase.Modules.DataSync.Services.DataSyncGrantView("a", "A"));
        h.Grants.Readers.Add(new Bakabase.Modules.DataSync.Services.DataSyncGrantView("b", "B"));
        foreach (var link in new[] {initiator, approver})
        {
            var updated = await h.Links.UpdateAsync(link.Id, null, DataSyncKindIds.All, true, default);
            Assert.IsNull(updated.Problem);
            Assert.AreEqual(DataSyncLinkState.Active, updated.Link!.State);
        }

        await h.FetchOnceAsync();

        // Only a link's first sync is previewed: the added kind is pulled from 0 and merged, its name matches asked.
        foreach (var link in new[] {initiator, approver})
        {
            Assert.IsNull(h.State.PeekPreview(link.Id));
            CollectionAssert.AreEqual(new[] {DataSyncKindIds.CustomProperty},
                h.State.PeekPull(link.Id)!.Kinds.Select(k => k.Kind).ToArray());
        }

        await h.Btm.Start(DataSyncTaskIds.Apply);
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);
        foreach (var link in new[] {initiator, approver})
            Assert.AreEqual(1, h.Observer.Count($"applied:{link.Id}:next"), "not the link's first sync");
    }
}
