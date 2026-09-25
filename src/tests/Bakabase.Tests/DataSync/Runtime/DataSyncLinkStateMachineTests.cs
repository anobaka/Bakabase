using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Tests.DataSync.Runtime;

/// <summary>The link state machine (§8.1), one link per peer, and the resume actions of §8.7.</summary>
[TestClass]
public class DataSyncLinkStateMachineTests
{
    private static DataSyncLinkCreate Create(string peer, DataSyncLinkMode mode = DataSyncLinkMode.Follow) =>
        new(peer, null, null, mode, null);

    [TestMethod]
    public async Task Create_goes_to_review_with_access_and_waits_for_access_without_it()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        h.Grants.Outbound.Add("a");
        h.Grants.Peers.Add(new DataSyncPeerCandidate("a", "Studio PC", null, true, false, 1, true, true, false, null,
            "online"));

        var withAccess = await h.Links.CreateAsync(Create("a"), true, default);
        Assert.IsNull(withAccess.Problem);
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, withAccess.Link!.State);
        Assert.AreEqual(DataSyncLinkInitiator.ThisDevice, withAccess.Link.Initiator);
        Assert.AreEqual("Studio PC", withAccess.Link.PeerName);
        CollectionAssert.AreEqual(DataSyncKindIds.All.ToArray(), withAccess.Link.GetKinds().ToArray());
        Assert.AreEqual(0, h.Grants.Sent.Count, "a Follow link to a peer this device reads sends nothing");

        var withoutAccess = await h.Links.CreateAsync(Create("b") with {Kinds = ["customProperty"]}, true, default);
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, withoutAccess.Link!.State);
        Assert.AreEqual("req-b", withoutAccess.RequestId);
        Assert.AreEqual("req-b", withoutAccess.Link.PendingRequestId);
        Assert.AreEqual(DataSyncRequestIntent.Follow, h.Grants.Sent.Single().Intent);
        CollectionAssert.AreEqual(new[] {"customProperty"}, withoutAccess.Link.GetKinds().ToArray());

        var again = await h.Links.CreateAsync(Create("a"), true, default);
        Assert.AreEqual(DataSyncProblemCode.LinkExists, again.Problem!.Code, "one link per peer");
        Assert.AreEqual(2, h.Store.All().Count);

        var unknownKind = await h.Links.CreateAsync(Create("c") with {Kinds = ["savedSearch"]}, true, default);
        Assert.AreEqual(DataSyncProblemCode.UnknownKind, unknownKind.Problem!.Code);
    }

    [TestMethod]
    public async Task Access_creating_creates_need_the_switches_and_a_caller_who_may_create_access()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        h.Grants.Outbound.Add("a");

        h.Grants.SharingEnabled = false;
        Assert.AreEqual(DataSyncProblemCode.SharingOff,
            (await h.Links.CreateAsync(Create("a", DataSyncLinkMode.TwoWay), true, default)).Problem!.Code);
        h.Grants.SharingEnabled = true;
        h.Grants.RemoteAccessMode = RemoteAccessMode.Disabled;
        Assert.AreEqual(DataSyncProblemCode.RemoteAccessOff,
            (await h.Links.CreateAsync(Create("a", DataSyncLinkMode.TwoWay), true, default)).Problem!.Code);
        h.Grants.RemoteAccessMode = RemoteAccessMode.Unrestricted;

        // Two-way to a peer that does not read this device mints a reciprocal code: refused for an unpaired caller.
        Assert.AreEqual(DataSyncProblemCode.NotAllowedOnThisDevice,
            (await h.Links.CreateAsync(Create("a", DataSyncLinkMode.TwoWay), false, default)).Problem!.Code);
        Assert.AreEqual(DataSyncProblemCode.NotAllowedOnThisDevice,
            (await h.Links.CreateAsync(Create("b"), false, default)).Problem!.Code);
        Assert.AreEqual(0, h.Grants.Sent.Count);
        Assert.AreEqual(0, h.Store.All().Count, "nothing changed");

        // Nothing to send: the same caller may create it.
        h.Grants.Readers.Add(new DataSyncGrantView("a", "A", h.Clock.UtcNow));
        var created = await h.Links.CreateAsync(Create("a", DataSyncLinkMode.TwoWay), false, default);
        Assert.IsNull(created.Problem);
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, created.Link!.State);
    }

    [TestMethod]
    public async Task A_request_that_ends_stops_the_link_and_keeps_it_with_its_reason()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var rejected = (await h.Links.CreateAsync(Create("a"), true, default)).Link!;
        var expired = (await h.Links.CreateAsync(Create("b", DataSyncLinkMode.TwoWay), true, default)).Link!;
        var waiting = (await h.Links.CreateAsync(Create("c"), true, default)).Link!;
        h.Grants.Requests.Add(Request("req-a", "rejected", h.Clock.UtcNow.AddMinutes(10)));
        h.Grants.Requests.Add(Request("req-b", "pending", h.Clock.UtcNow.AddMinutes(-1)));
        h.Grants.Requests.Add(Request("req-c", "pending", h.Clock.UtcNow.AddMinutes(10)));

        await h.FetchOnceAsync();

        var a = h.Link(rejected.Id);
        Assert.AreEqual(DataSyncLinkState.Stopped, a.State);
        Assert.AreEqual(DataSyncLinkService.AccessRejected, a.LastErrorCode);
        Assert.AreEqual(DataSyncLinkMode.Off, a.Mode);
        Assert.AreEqual(DataSyncLinkMode.Follow, a.LastMode, "turning it on again asks with the same mode");
        Assert.AreEqual(DataSyncLinkService.AccessExpired, h.Link(expired.Id).LastErrorCode);
        Assert.AreEqual(DataSyncLinkMode.TwoWay, h.Link(expired.Id).LastMode);
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, h.Link(waiting.Id).State);
        Assert.AreEqual(h.Clock.UtcNow + DataSyncSchedule.PollInterval, h.Link(waiting.Id).NextAttemptAtUtc);
        Assert.AreEqual(0, h.Peers.Peers.Count, "a link waiting for access never calls the peer");
    }

    private static DataSyncAccessRequestView Request(string id, string status, DateTime expiresAt) =>
        new(id, DataSyncRequestDirection.Outgoing, "x", "X", DataSyncRequestIntent.Follow, status, expiresAt, null,
            false, null, false);

    [TestMethod]
    public async Task An_outbound_grant_moves_a_waiting_link_to_its_side_of_the_first_contact()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var initiated = (await h.Links.CreateAsync(Create("a"), true, default)).Link!;
        var approver = await h.Links.OnInboundGrantedAsync("b", DataSyncRequestIntent.TwoWay, true, false,
            "unreachable", "B", null, default);

        await h.Links.OnOutboundGrantedAsync("a", default);
        await h.Links.OnOutboundGrantedAsync("b", default);

        Assert.AreEqual(DataSyncLinkState.AwaitingReview, h.Link(initiated.Id).State);
        Assert.IsNull(h.Link(initiated.Id).PendingRequestId);
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, h.Link(approver!.Id).State);
        Assert.IsNull(h.Link(approver.Id).LastErrorCode);
        Assert.AreEqual(h.Clock.UtcNow, h.Link(approver.Id).NextAttemptAtUtc);
    }

    [TestMethod]
    public async Task A_two_way_inbound_grant_creates_the_approvers_link_once_per_peer()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);

        var link = await h.Links.OnInboundGrantedAsync("b", DataSyncRequestIntent.TwoWay, true, true, null, "B",
            ["customProperty"], default);
        Assert.AreEqual(DataSyncLinkMode.TwoWay, link!.Mode);
        Assert.AreEqual(DataSyncLinkInitiator.Peer, link.Initiator);
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, link.State);
        CollectionAssert.AreEqual(new[] {"customProperty"}, link.GetKinds().ToArray());

        await h.Links.OnInboundGrantedAsync("b", DataSyncRequestIntent.TwoWay, true, true, null, "B", null, default);
        Assert.AreEqual(1, h.Store.All().Count, "the existing link is updated, not duplicated");

        Assert.IsNull(await h.Links.OnInboundGrantedAsync("c", DataSyncRequestIntent.Follow, false, false, null, "C",
            null, default), "a Follow grant makes no link here");
        Assert.IsNull(await h.Links.OnInboundGrantedAsync("d", DataSyncRequestIntent.TwoWay, false, false, null, "D",
            null, default), "two-way approved without receiving back makes no link");
        Assert.AreEqual(1, h.Store.All().Count);
    }

    [TestMethod]
    public async Task A_failed_read_back_still_creates_the_approvers_link_awaiting_access()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);

        var link = await h.Links.OnInboundGrantedAsync("b", DataSyncRequestIntent.TwoWay, true, false,
            "PeerUnreachable", "B", null, default);

        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, link!.State);
        Assert.AreEqual(DataSyncLinkInitiator.Peer, link.Initiator);
        Assert.AreEqual(DataSyncLinkMode.TwoWay, link.Mode);
        Assert.AreEqual(DataSyncLinkService.ReadBackFailed, link.LastErrorCode);
        Assert.AreEqual("PeerUnreachable", link.LastErrorDetail);

        // The same event through the grant events handler, as D raises it: the read-back is still in flight.
        h.GrantEvents.InboundGranted("c", DataSyncRequestIntent.TwoWay, true);
        await h.GrantEvents.DrainAsync(default);
        var c = h.Store.All().Single(l => l.PeerNodeId == "c");
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, c.State);
        Assert.IsNull(c.LastErrorCode, "a read-back in flight is not a failure");
        h.Grants.Outbound.Add("d");
        h.GrantEvents.InboundGranted("d", DataSyncRequestIntent.TwoWay, true);
        await h.GrantEvents.DrainAsync(default);
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, h.Store.All().Single(l => l.PeerNodeId == "d").State);
    }

    [TestMethod]
    public async Task Try_again_after_a_failed_read_back_asks_only_to_read_the_peer_back()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = (await h.Links.OnInboundGrantedAsync("b", DataSyncRequestIntent.TwoWay, true, false,
            "PeerUnreachable", "B", null, default))!;

        Assert.AreEqual(DataSyncProblemCode.NotAllowedOnThisDevice,
            (await h.Links.RequestAccessAgainAsync(link.Id, false, default)).Problem!.Code);
        var again = await h.Links.RequestAccessAgainAsync(link.Id, true, default);
        Assert.IsNull(again.Problem);
        Assert.AreEqual(DataSyncRequestIntent.Follow, h.Grants.Sent.Single().Intent, "the peer already reads this device");
        Assert.AreEqual("req-b", again.Link!.PendingRequestId);
        Assert.IsNull(again.Link.LastErrorCode);
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, again.Link.State);

        h.Grants.Answer = input => new DataSyncAccessRequestOutcome("granted", null, input.PeerNodeId!, "B", null);
        h.Grants.Outbound.Add("b");
        var granted = await h.Links.RequestAccessAgainAsync(link.Id, true, default);
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, granted.Link!.State);
    }

    [TestMethod]
    public async Task An_inbound_grant_updates_an_existing_link()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var stoppedDone = h.AddLink("a", l =>
        {
            l.Mode = DataSyncLinkMode.Off;
            l.LastMode = DataSyncLinkMode.Follow;
            l.State = DataSyncLinkState.Stopped;
        });
        var stoppedNew = h.AddLink("b", l =>
        {
            l.Mode = DataSyncLinkMode.Off;
            l.State = DataSyncLinkState.Stopped;
            l.FirstContactCompletedAtUtc = null;
            l.FirstContactKindsJson = null;
        });
        var active = h.AddLink("c", l => l.Mode = DataSyncLinkMode.Follow);

        foreach (var peer in new[] {"a", "b", "c"})
            await h.Links.OnInboundGrantedAsync(peer, DataSyncRequestIntent.TwoWay, true, true, null, null, null, default);

        Assert.AreEqual(DataSyncLinkState.Active, h.Link(stoppedDone.Id).State);
        Assert.AreEqual(DataSyncLinkMode.TwoWay, h.Link(stoppedDone.Id).Mode);
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, h.Link(stoppedNew.Id).State);
        Assert.AreEqual(DataSyncLinkInitiator.Peer, h.Link(stoppedNew.Id).Initiator);
        Assert.AreEqual(DataSyncLinkState.Active, h.Link(active.Id).State, "any other state is kept");
        Assert.AreEqual(DataSyncLinkMode.TwoWay, h.Link(active.Id).Mode);
        Assert.AreEqual(0, h.Store.Deleted.Count + h.Store.Stopped.Count, "bases and pending records are kept");
    }

    [TestMethod]
    public async Task Off_stops_the_link_and_on_resumes_it_without_a_new_review()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("a");
        h.Grants.Readers.Add(new DataSyncGrantView("a", "A", h.Clock.UtcNow));
        h.StagedPulls.Put(link.Id, new DataSyncStagedPull("a", "A",
            h.Peers.Peers["a"].Manifest(new DataSyncFeedQuery("twoWay", new Dictionary<string, long>(), null, null)),
            [], h.Clock.UtcNow));

        var off = await h.Links.UpdateAsync(link.Id, DataSyncLinkMode.Off, null, false, default);
        Assert.IsNull(off.Problem, "reducing access is open to every caller");
        Assert.AreEqual(DataSyncLinkState.Stopped, off.Link!.State);
        Assert.AreEqual(DataSyncLinkMode.Off, off.Link.Mode);
        Assert.AreEqual(DataSyncLinkMode.TwoWay, off.Link.LastMode);
        CollectionAssert.AreEqual(new[] {link.Id}, h.Store.Stopped);
        Assert.IsNull(h.StagedPulls.Peek(link.Id));

        var on = await h.Links.UpdateAsync(link.Id, DataSyncLinkMode.TwoWay, null, false, default);
        Assert.IsNull(on.Problem);
        Assert.AreEqual(DataSyncLinkState.Active, on.Link!.State, "no new review: its first contact is done");
        Assert.AreEqual(0, h.Grants.Sent.Count);
        Assert.AreEqual(3, on.Link.GetCursors()["extensionGroup"], "incremental: cursors kept");
    }

    [TestMethod]
    public async Task Pause_and_the_resume_actions()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("a");

        var paused = await h.Links.PauseByUserAsync(link.Id, default);
        Assert.AreEqual(DataSyncLinkState.Paused, paused.Link!.State);
        Assert.AreEqual(DataSyncPauseReason.ByUser, paused.Link.PausedReason);
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid,
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.ApplyAsUsual, true, default)).Problem!.Code);
        var resumed = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.Resume, true, default);
        Assert.AreEqual(DataSyncLinkState.Active, resumed.Link!.State);
        Assert.IsNull(resumed.Link.PausedReason);

        await h.Links.PauseAsync(link.Id, DataSyncPauseReason.MassDeletion, "deletions=182;kind=customProperty", default);
        var usual = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.ApplyAsUsual, true, default);
        Assert.AreEqual(DataSyncLinkState.Active, usual.Link!.State);
        Assert.IsTrue(usual.Link.GetOnceFlags().SkipDeletionBreaker);
        await h.Links.PauseAsync(link.Id, DataSyncPauseReason.KindEmptied, "kind=customProperty", default);
        var review = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.ReviewDeletions, true, default);
        Assert.IsTrue(review.Link!.GetOnceFlags().DeletionsAsItems);
        Assert.IsTrue(review.Link!.GetOnceFlags().SkipDeletionBreaker, "flags accumulate until an apply consumes them");

        h.Store.OpenItems[link.Id] = new DataSyncLimits().MaxOpenInboxItemsPerLink;
        await h.Links.PauseAsync(link.Id, DataSyncPauseReason.TooManyDecisions, null, default);
        Assert.AreEqual("tooManyDecisions",
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.Resume, true, default)).Problem!.Detail);
        h.Store.OpenItems[link.Id] = 10;
        Assert.AreEqual(DataSyncLinkState.Active,
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.Resume, true, default)).Link!.State);

        await h.Links.PauseAsync(link.Id, DataSyncPauseReason.PeerIdentityDuplicated, null, default);
        Assert.AreEqual(DataSyncLinkState.Active,
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.Resume, true, default)).Link!.State);
        Assert.AreEqual(5, h.Observer.Count("paused:"), "every pause is observed once");
    }

    [TestMethod]
    public async Task A_restored_peer_resumes_from_zero()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("a");
        await h.Links.PauseAsync(link.Id, DataSyncPauseReason.PeerReset, DataSyncLinkService.RestoredDetail, default);
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid,
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default)).Problem!.Code);

        var resumed = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.Resume, true, default);
        Assert.AreEqual(DataSyncLinkState.Active, resumed.Link!.State);
        Assert.AreEqual(0, resumed.Link.GetCursors().Count, "the next pull is a full reconciliation");
        Assert.IsNull(resumed.Link.LastFullReconciliationAtUtc);
    }

    [TestMethod]
    public async Task A_reset_peer_is_asked_for_access_again_and_gets_a_new_first_contact()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("a", l =>
        {
            l.Mode = DataSyncLinkMode.Follow;
            l.PeerAddress = "192.168.1.20:5000";
            l.SetKinds(["customProperty"]);
        });
        await h.Links.PauseAsync(link.Id, DataSyncPauseReason.PeerReset, "epochChanged", default);
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid,
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.Resume, true, default)).Problem!.Code,
            "a reset peer revoked every grant: resuming cannot work");
        Assert.AreEqual(DataSyncProblemCode.NotAllowedOnThisDevice,
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, false, default)).Problem!.Code);

        var asked = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default);
        Assert.IsNull(asked.Problem);
        var sent = h.Grants.Sent.Single();
        Assert.AreEqual(DataSyncRequestIntent.Follow, sent.Intent, "the link's mode");
        Assert.AreEqual("192.168.1.20:5000", sent.Address);
        CollectionAssert.AreEqual(new[] {link.Id}, h.Store.Deleted, "bases cleared: the old link is reset");

        var fresh = asked.Link!;
        Assert.AreNotEqual(link.Id, fresh.Id);
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, fresh.State);
        Assert.AreEqual(DataSyncLinkInitiator.ThisDevice, fresh.Initiator);
        Assert.AreEqual("req-a", fresh.PendingRequestId);
        Assert.IsNull(fresh.PeerLibraryEpoch, "the new epoch is learnt from the next head");
        CollectionAssert.AreEqual(new[] {"customProperty"}, fresh.GetKinds().ToArray());

        await h.Links.OnOutboundGrantedAsync("a", default);
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, h.Link(fresh.Id).State);
    }

    [TestMethod]
    public async Task A_local_restore_is_resumed_only_by_a_restore_choice()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(daemon: true, registerFetchTask: false);
        var link = h.AddLink("a");
        await h.Links.PauseAsync(link.Id, DataSyncPauseReason.LocalRestoreSuspected, null, default);
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid,
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.Resume, true, default)).Problem!.Code);

        Assert.IsNull((await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.ThisDeviceWins, true, default)).Problem);
        await DataSyncRuntimeHarness.WaitUntilAsync(() => h.Runner.Restores.Count == 1, "the restore task ran");
        Assert.AreEqual((DataSyncRestoreChoice.ThisDeviceWins, (int?) link.Id), h.Runner.Restores.Single(),
            "scoped to the link a suspected restore came through");
    }

    [TestMethod]
    public async Task Start_anyway_starts_an_approver_that_waits_for_its_peers_review()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("a", l =>
        {
            l.State = DataSyncLinkState.WaitingForPeerReview;
            l.Initiator = DataSyncLinkInitiator.Peer;
        });
        var started = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.StartAnyway, true, default);
        Assert.AreEqual(DataSyncLinkState.Active, started.Link!.State);
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid,
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.StartAnyway, true, default)).Problem!.Code);
    }

    [TestMethod]
    public async Task Reset_deletes_the_link_and_forgets_its_pull_and_review()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("a", l => l.State = DataSyncLinkState.AwaitingReview);
        var manifest = h.Peers.Peers["a"].Manifest(new DataSyncFeedQuery("twoWay", new Dictionary<string, long>(), null,
            null));
        h.StagedPulls.Put(link.Id, new DataSyncStagedPull("a", "A", manifest, [], h.Clock.UtcNow));
        var review = h.Reviews.Stage(link.Id, false, new DataSyncStagedPull("a", "A", manifest, [], h.Clock.UtcNow));

        Assert.IsNull(await h.Links.ResetAsync(link.Id, default));
        CollectionAssert.AreEqual(new[] {link.Id}, h.Store.Deleted);
        Assert.IsNull(h.StagedPulls.Peek(link.Id));
        CollectionAssert.AreEqual(new[] {review.ReviewId}, h.Reviews.Discarded);
        Assert.AreEqual(1, h.Observer.Count("removed:"));
        Assert.AreEqual(DataSyncProblemCode.LinkNotFound, (await h.Links.ResetAsync(link.Id, default))!.Code);
    }
}
