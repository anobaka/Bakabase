using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
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
        // The real listing leaves an expired request out rather than listing it as expired.
        h.Grants.Requests.Add(Request("req-b", "awaitingApproval", h.Clock.UtcNow.AddMinutes(-1)));
        h.Grants.Requests.Add(Request("req-c", "awaitingApproval", h.Clock.UtcNow.AddMinutes(10)));

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
    public async Task A_request_no_longer_listed_ends_the_wait_unless_access_arrived()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var expiring = (await h.Links.CreateAsync(Create("a"), true, default)).Link!;
        var forgotten = (await h.Links.CreateAsync(Create("b", DataSyncLinkMode.TwoWay), true, default)).Link!;
        var granted = (await h.Links.CreateAsync(Create("c"), true, default)).Link!;
        h.Grants.Requests.Add(Request("req-a", "awaitingApproval", h.Clock.UtcNow.AddMinutes(10)));
        h.Grants.Requests.Add(Request("req-c", "awaitingApproval", h.Clock.UtcNow.AddMinutes(10)));
        // req-b went with the device's datasync state ("Done — stop reading", the device removed): never listed.

        await h.FetchOnceAsync();
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, h.Link(expiring.Id).State, "still listed: it waits");
        var b = h.Link(forgotten.Id);
        Assert.AreEqual((DataSyncLinkState.Stopped, DataSyncLinkService.AccessExpired, DataSyncLinkMode.TwoWay),
            (b.State, b.LastErrorCode, b.LastMode), "nobody can answer it: it stops, kept with Dismiss");

        // Nobody approved within ten minutes, and the listing dropped the requests; one was granted meanwhile, its
        // event lost.
        h.Grants.Outbound.Add("c");
        h.Clock.Advance(TimeSpan.FromMinutes(11));
        await h.FetchOnceAsync();
        var a = h.Link(expiring.Id);
        Assert.AreEqual((DataSyncLinkState.Stopped, DataSyncLinkMode.Off, DataSyncLinkService.AccessExpired),
            (a.State, a.Mode, a.LastErrorCode));
        Assert.IsNull(a.NextAttemptAtUtc, "not polled for ever");
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, h.Link(granted.Id).State, "access that arrived counts first");
    }

    [TestMethod]
    public async Task A_revoked_link_asks_for_access_again_and_only_the_answer_brings_it_back()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var follow = h.AddLink("a", l => l.Mode = DataSyncLinkMode.Follow);
        var twoWay = h.AddLink("b");
        var sharingOff = h.AddLink("c");
        h.Peers.Peers["a"].HeadErrors.Enqueue(new DataSyncPeerException(DataSyncPeerErrorCode.AccessRevoked));
        h.Peers.Peers["b"].HeadErrors.Enqueue(new DataSyncPeerException(DataSyncPeerErrorCode.AccessMissing));
        h.Peers.Peers["c"].HeadErrors.Enqueue(new DataSyncPeerException(DataSyncPeerErrorCode.PeerSharingOff));
        await h.FetchOnceAsync();
        Assert.AreEqual(DataSyncLinkState.AccessRevoked, h.Link(follow.Id).State);
        Assert.AreEqual(DataSyncLinkState.AccessRevoked, h.Link(twoWay.Id).State);
        Assert.AreEqual(DataSyncLinkState.PeerSharingOff, h.Link(sharingOff.Id).State);

        // Asking cannot help a peer that turned sharing off; a caller who may not create access is refused.
        Assert.AreEqual("notApplicable:askAccessAgain", (await h.Links.ResumeAsync(sharingOff.Id,
            DataSyncResumeAction.AskAccessAgain, true, default)).Problem!.Detail);
        Assert.AreEqual(DataSyncProblemCode.NotAllowedOnThisDevice, (await h.Links.ResumeAsync(follow.Id,
            DataSyncResumeAction.AskAccessAgain, false, default)).Problem!.Code);
        Assert.AreEqual(0, h.Grants.Sent.Count);
        Assert.IsTrue(h.Grants.Outbound.Contains("a"), "nothing is forgotten for a refused call");

        // The credentials the peer refused are forgotten, then a request goes out with the link's mode.
        var asked = await h.Links.ResumeAsync(follow.Id, DataSyncResumeAction.AskAccessAgain, true, default);
        Assert.IsNull(asked.Problem);
        var sent = h.Grants.Sent.Single();
        Assert.AreEqual((DataSyncRequestIntent.Follow, "a"), (sent.Intent, sent.PeerNodeId));
        CollectionAssert.Contains(h.Grants.Changes.ToArray(), "forget:a");
        Assert.AreEqual((DataSyncLinkState.AwaitingAccess, "req-a"), (asked.Link!.State, asked.Link.PendingRequestId));
        Assert.AreEqual(3, asked.Link.GetCursors()["extensionGroup"], "the same epoch: cursors and bases stay");
        Assert.IsNotNull(asked.Link.FirstContactCompletedAtUtc);
        Assert.IsNull(asked.Link.LastErrorCode);

        // Two-way, to a peer that does not read this device: a two-way request, with its offer.
        Assert.IsNull((await h.Links.ResumeAsync(twoWay.Id, DataSyncResumeAction.AskAccessAgain, true, default))
            .Problem);
        Assert.AreEqual(DataSyncRequestIntent.TwoWay, h.Grants.Sent.Last().Intent);

        // Waiting, nothing reads as access; granted (the claim loop), the link goes back to work with what it had.
        h.Grants.Requests.Add(OutgoingRequest(h, "req-a", "awaitingApproval"));
        h.Grants.Requests.Add(OutgoingRequest(h, "req-b", "awaitingApproval"));
        await h.FetchOnceAsync();
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, h.Link(follow.Id).State);
        h.Grants.Outbound.Add("a");
        await h.Links.OnOutboundGrantedAsync("a", default);
        Assert.AreEqual((DataSyncLinkState.Active, (string?) null),
            (h.Link(follow.Id).State, h.Link(follow.Id).PendingRequestId));
        Assert.AreEqual(0, h.Store.Deleted.Count);

        // Not approved in time: stopped like any request that ended, and turning it on again asks again.
        h.Clock.Advance(TimeSpan.FromMinutes(11));
        await h.FetchOnceAsync();
        var stopped = h.Link(twoWay.Id);
        Assert.AreEqual((DataSyncLinkState.Stopped, DataSyncLinkService.AccessExpired, DataSyncLinkMode.TwoWay),
            (stopped.State, stopped.LastErrorCode, stopped.LastMode));
        var on = await h.Links.UpdateAsync(twoWay.Id, DataSyncLinkMode.TwoWay, null, true, default);
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, on.Link!.State, "no credentials left to mistake for access");
        Assert.AreEqual(3, h.Grants.Sent.Count);
    }

    [TestMethod]
    public async Task A_two_way_request_approved_without_reading_back_says_so_until_the_peer_reads_this_device()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = (await h.Links.CreateAsync(Create("a", DataSyncLinkMode.TwoWay), true, default)).Link!;
        Assert.IsFalse(link.ReadBackDeclined, "a request says nothing before it is approved");

        // The claim loop: granted, and the exchange says the approver does not read this device back (§7.2.4 step 7).
        h.Grants.Outbound.Add("a");
        h.GrantEvents.OutboundGranted("a", "declined");
        await h.GrantEvents.DrainAsync(default);
        Assert.AreEqual((DataSyncLinkState.AwaitingReview, true),
            (h.Link(link.Id).State, h.Link(link.Id).ReadBackDeclined));

        // [Ask a to keep in step]: the note stays while that request waits, and after an approval without reading back.
        h.Store.Edit(link.Id, l =>
        {
            l.State = DataSyncLinkState.Active;
            l.FirstContactCompletedAtUtc = h.Clock.UtcNow;
        });
        var asked = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default);
        Assert.IsNull(asked.Problem);
        Assert.AreEqual(DataSyncRequestIntent.TwoWay, h.Grants.Sent.Last().Intent);
        Assert.IsTrue(asked.Link!.ReadBackDeclined, "asked, not answered");
        h.GrantEvents.OutboundGranted("a", "declined");
        await h.GrantEvents.DrainAsync(default);
        Assert.IsTrue(h.Link(link.Id).ReadBackDeclined);

        // Once the peer reads this device — its read-back redeemed this device's code — the note goes.
        h.GrantEvents.InboundGranted("a", DataSyncRequestIntent.Follow, false);
        await h.GrantEvents.DrainAsync(default);
        Assert.IsFalse(h.Link(link.Id).ReadBackDeclined);

        // A grant whose read-back started clears it too; a Follow link is never marked.
        h.Store.Edit(link.Id, l => l.ReadBackDeclined = true);
        h.GrantEvents.OutboundGranted("a", "started");
        await h.GrantEvents.DrainAsync(default);
        Assert.IsFalse(h.Link(link.Id).ReadBackDeclined);
        var follow = h.AddLink("f", l => l.Mode = DataSyncLinkMode.Follow);
        h.GrantEvents.OutboundGranted("f", "declined");
        await h.GrantEvents.DrainAsync(default);
        Assert.IsFalse(h.Link(follow.Id).ReadBackDeclined);
    }

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
    public async Task A_read_back_that_fails_later_records_why_on_the_approvers_link()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);

        // A code redeemed two-way: the grant event comes first, the read-back's outcome after it (§7.2.4).
        h.GrantEvents.InboundGranted("b", DataSyncRequestIntent.TwoWay, true);
        await h.GrantEvents.DrainAsync(default);
        var link = h.Store.All().Single(l => l.PeerNodeId == "b");
        Assert.IsNull(link.LastErrorCode, "a read-back in flight is not a failure");
        var changes = h.Observer.Count($"changed:{link.Id}:");

        h.GrantEvents.ReadBackFailed("b", nameof(DataSyncPeerErrorCode.Unreachable));
        await h.GrantEvents.DrainAsync(default);
        link = h.Store.All().Single(l => l.PeerNodeId == "b");
        Assert.AreEqual((DataSyncLinkState.AwaitingAccess, DataSyncLinkInitiator.Peer), (link.State, link.Initiator));
        Assert.AreEqual((DataSyncLinkService.ReadBackFailed, nameof(DataSyncPeerErrorCode.Unreachable)),
            (link.LastErrorCode, link.LastErrorDetail));
        Assert.AreEqual(changes + 1, h.Observer.Count($"changed:{link.Id}:"), "the status shows it");

        // "Try again" is offered on it; a link this device reads, or one that asked on its own, is not touched.
        Assert.IsNull((await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default)).Problem);
        var asked = h.Store.All().Single(l => l.PeerNodeId == "b");
        h.GrantEvents.ReadBackFailed("b", nameof(DataSyncPeerErrorCode.AccessRevoked));
        await h.GrantEvents.DrainAsync(default);
        Assert.AreEqual((asked.LastErrorCode, asked.PendingRequestId),
            (h.Store.All().Single(l => l.PeerNodeId == "b").LastErrorCode, "req-b"));

        var active = h.AddLink("c");
        h.GrantEvents.ReadBackFailed("c", nameof(DataSyncPeerErrorCode.Unreachable));
        await h.GrantEvents.DrainAsync(default);
        Assert.IsNull(h.Link(active.Id).LastErrorCode);
    }

    [TestMethod]
    public async Task Try_again_after_a_failed_read_back_asks_only_to_read_the_peer_back()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = (await h.Links.OnInboundGrantedAsync("b", DataSyncRequestIntent.TwoWay, true, false,
            "PeerUnreachable", "B", null, default))!;

        // "Try again" is AskAccessAgain on a link that waits for access (§7.2.4, N14).
        Assert.AreEqual(DataSyncProblemCode.NotAllowedOnThisDevice,
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, false, default)).Problem!.Code);
        var again = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default);
        Assert.IsNull(again.Problem);
        Assert.AreEqual(DataSyncRequestIntent.Follow, h.Grants.Sent.Single().Intent, "the peer already reads this device");
        Assert.AreEqual("req-b", again.RequestId);
        Assert.AreEqual("req-b", again.Link!.PendingRequestId);
        Assert.IsNull(again.Link.LastErrorCode);
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, again.Link.State);

        h.Grants.Answer = input => new DataSyncAccessRequestOutcome("granted", null, input.PeerNodeId!, "B", null);
        h.Grants.Outbound.Add("b");
        var granted = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default);
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, granted.Link!.State);
    }

    [TestMethod]
    public async Task Asking_a_peer_that_declined_to_read_back_to_keep_in_step_sends_a_two_way_request()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        // A code without two-way consent was redeemed two-way: this device reads the peer, the peer not this one.
        var link = h.AddLink("a", l => l.ReadBackDeclined = true);
        var plain = h.AddLink("b");

        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid,
            (await h.Links.ResumeAsync(plain.Id, DataSyncResumeAction.AskAccessAgain, true, default)).Problem!.Code,
            "a link the peer reads back, or one it never declined, has nothing to ask");
        Assert.AreEqual(DataSyncProblemCode.NotAllowedOnThisDevice,
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, false, default)).Problem!.Code);
        h.Grants.SharingEnabled = false;
        Assert.AreEqual(DataSyncProblemCode.SharingOff,
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default)).Problem!.Code);
        h.Grants.SharingEnabled = true;
        Assert.AreEqual(0, h.Grants.Sent.Count);

        var asked = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default);
        Assert.IsNull(asked.Problem);
        var sent = h.Grants.Sent.Single();
        Assert.AreEqual(DataSyncRequestIntent.TwoWay, sent.Intent, "an ordinary two-way request, with its offer");
        Assert.AreEqual("a", sent.PeerNodeId);
        Assert.AreEqual("req-a", asked.RequestId);
        Assert.AreEqual(DataSyncLinkState.Active, asked.Link!.State, "the link keeps syncing meanwhile");
        Assert.IsTrue(asked.Link.ReadBackDeclined, "asked, not answered: it stays until the peer reads this device");

        // Once the peer reads this device, a declined note is only cleared.
        h.Store.Edit(link.Id, l => l.ReadBackDeclined = true);
        h.Grants.Readers.Add(new DataSyncGrantView("a", "A", h.Clock.UtcNow));
        var cleared = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default);
        Assert.IsFalse(cleared.Link!.ReadBackDeclined);
        Assert.AreEqual(1, h.Grants.Sent.Count);
    }

    [TestMethod]
    public async Task The_approval_and_its_queued_grant_event_make_one_link_whichever_writes_first()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        h.Grants.Outbound.Add("b");
        Task drained;
        Task<DataSyncLinkDbModel?> approval;
        using (h.RowTransactions.Hold())
        {
            // The pairing flow's event is drained first and waits to insert; the approval's own call has looked for
            // a link meanwhile and found none (§8.1: one link per peer).
            h.GrantEvents.InboundGranted("b", DataSyncRequestIntent.TwoWay, true);
            drained = h.GrantEvents.DrainAsync(default);
            await DataSyncRuntimeHarness.WaitUntilAsync(() => h.RowTransactions.Waiting == 1, "the event waits");
            approval = h.Links.OnInboundGrantedAsync("b", DataSyncRequestIntent.TwoWay, true, true, null, "B",
                ["customProperty"], default);
            Assert.IsFalse(approval.IsCompleted, "the approval waits behind the event's insert");
            Assert.AreEqual(0, h.Store.All().Count);
        }

        await drained.WaitAsync(TimeSpan.FromSeconds(10));
        var approved = await approval.WaitAsync(TimeSpan.FromSeconds(10));
        var link = h.Store.All().Single();
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, link.State);
        Assert.AreEqual(DataSyncLinkInitiator.Peer, link.Initiator);
        Assert.AreEqual(link.Id, approved!.Id, "the second caller updated the link the first made");
    }

    [TestMethod]
    public async Task A_copy_once_onto_a_stopped_link_stays_a_copy_once_through_a_pause_and_a_peer_error()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var stopped = h.AddLink("a", l =>
        {
            l.Mode = DataSyncLinkMode.Off;
            l.LastMode = DataSyncLinkMode.Follow;
            l.State = DataSyncLinkState.Stopped;
            l.NextAttemptAtUtc = null;
        });

        var copy = await h.Links.CreateAsync(new DataSyncLinkCreate("a", null, null, DataSyncLinkMode.Follow, null,
            CopyOnce: true), true, default);
        Assert.IsNull(copy.Problem);
        Assert.AreEqual(stopped.Id, copy.Link!.Id, "one link per peer: the stopped row is reused");
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, copy.Link.State);
        Assert.IsNull(copy.Link.FirstContactCompletedAtUtc, "its review is a first contact again");
        Assert.AreEqual(3, copy.Link.GetCursors()["extensionGroup"], "cursors and bases stay");

        await h.Links.PauseByUserAsync(stopped.Id, default);
        var resumed = await h.Links.ResumeAsync(stopped.Id, DataSyncResumeAction.Resume, true, default);
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, resumed.Link!.State, "the copy once did not silently end");

        h.Peers.Peers["a"].HeadErrors.Enqueue(new DataSyncPeerException(DataSyncPeerErrorCode.AccessRevoked));
        await h.FetchOnceAsync();
        Assert.AreEqual(DataSyncLinkState.AccessRevoked, h.Link(stopped.Id).State);
        Assert.IsTrue(h.Link(stopped.Id).IsFetchable(), "retried hourly like any link in its first contact");
        h.Clock.Advance(DataSyncSchedule.AccessRetry);
        await h.FetchOnceAsync();
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, h.Link(stopped.Id).State);
    }

    [TestMethod]
    public async Task A_second_copy_once_onto_a_stopped_row_stages_its_own_review()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var stopped = h.AddLink("a", l =>
        {
            l.Mode = DataSyncLinkMode.Off;
            l.LastMode = DataSyncLinkMode.TwoWay;
            l.State = DataSyncLinkState.Stopped;
            l.NextAttemptAtUtc = null;
        });
        // The first copy once's review, applied a few minutes ago and kept for its result screen.
        var first = h.Reviews.Stage(stopped.Id, true, new DataSyncStagedPull("a", "Peer a",
            h.Peers.Peers["a"].Manifest(new DataSyncFeedQuery("follow", new Dictionary<string, long>(), null, null)),
            [], h.Clock.UtcNow));
        h.Reviews.MarkApplying(first.ReviewId, "DataSyncReview:" + first.ReviewId);
        h.Reviews.MarkApplied(first.ReviewId, 7);

        var copy = await h.Links.CreateAsync(new DataSyncLinkCreate("a", null, null, DataSyncLinkMode.Follow, null,
            CopyOnce: true), true, default);
        Assert.IsNull(copy.Problem);
        CollectionAssert.AreEqual(new[] {first.ReviewId}, h.Reviews.Discarded,
            "the earlier copy once's review is not this one's");

        await h.FetchOnceAsync();
        Assert.AreEqual(2, h.Reviews.Staged.Count, "the cycle stages the new copy once's review");
        var second = h.Reviews.Staged.Last();
        Assert.IsTrue(second.CopyOnce);
        Assert.AreEqual(second.ReviewId, h.Reviews.PeekForLink(stopped.Id)!.ReviewId);
    }

    [TestMethod]
    public async Task A_copy_once_turned_on_before_its_review_is_applied_becomes_the_links_first_contact()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var stopped = h.AddLink("a", l =>
        {
            l.Mode = DataSyncLinkMode.Off;
            l.LastMode = DataSyncLinkMode.TwoWay;
            l.State = DataSyncLinkState.Stopped;
            l.NextAttemptAtUtc = null;
        });
        var copy = await h.Links.CreateAsync(new DataSyncLinkCreate("a", null, null, DataSyncLinkMode.Follow, null,
            CopyOnce: true), true, default);
        Assert.AreEqual((DataSyncLinkMode.Off, DataSyncLinkState.AwaitingReview), (copy.Link!.Mode, copy.Link.State));
        await h.FetchOnceAsync();
        var copyReview = h.Reviews.Staged.Single();
        Assert.IsTrue(copyReview.CopyOnce);
        Assert.AreEqual(copyReview.ReviewId, h.Link(stopped.Id).ReviewId);

        // The rule editor's receive arrow: Follow, still waiting for its first contact.
        var on = await h.Links.UpdateAsync(stopped.Id, DataSyncLinkMode.Follow, null, true, default);
        Assert.IsNull(on.Problem);
        Assert.AreEqual((DataSyncLinkMode.Follow, DataSyncLinkMode.Follow, DataSyncLinkState.AwaitingReview),
            (on.Link!.Mode, on.Link.LastMode, on.Link.State));
        CollectionAssert.AreEqual(new[] {copyReview.ReviewId}, h.Reviews.Discarded, "the copy once's review goes");
        Assert.IsNull(on.Link.ReviewId);
        Assert.IsFalse(Bakabase.InsideWorld.Business.Components.DataSync.Apply.DataSyncReviewStore
            .AppliesAsCopyOnce(copyReview, on.Link), "applied anyway, it would be the link's first contact");

        // The next cycle stages the link's own review.
        await h.FetchOnceAsync();
        var review = h.Reviews.Staged.Last();
        Assert.AreNotEqual(copyReview.ReviewId, review.ReviewId);
        Assert.IsFalse(review.CopyOnce);
        Assert.AreEqual(review.ReviewId, h.Link(stopped.Id).ReviewId);
    }

    [TestMethod]
    public async Task Approving_a_two_way_request_turns_a_waiting_copy_once_into_the_link_and_leaves_an_applying_review()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        DataSyncLinkDbModel CopyOnce(string peer) => h.AddLink(peer, l =>
        {
            l.Mode = DataSyncLinkMode.Off;
            l.LastMode = DataSyncLinkMode.TwoWay;
            l.State = DataSyncLinkState.AwaitingReview;
            l.FirstContactCompletedAtUtc = null;
            l.FirstContactKindsJson = null;
        });
        var staged = CopyOnce("a");
        var applying = CopyOnce("b");
        DataSyncReviewEntry Stage(DataSyncLinkDbModel link) => h.Reviews.Stage(link.Id, true, new DataSyncStagedPull(
            link.PeerNodeId, link.PeerName, h.Peers.Peers[link.PeerNodeId].Manifest(new DataSyncFeedQuery("follow",
                new Dictionary<string, long>(), null, null)), [], h.Clock.UtcNow));
        var stagedReview = Stage(staged);
        var applyingReview = Stage(applying);
        h.Reviews.MarkApplying(applyingReview.ReviewId, "DataSyncReview:" + applyingReview.ReviewId);

        foreach (var peer in new[] {"a", "b"})
            await h.Links.OnInboundGrantedAsync(peer, DataSyncRequestIntent.TwoWay, true, true, null, null, null, default);

        foreach (var link in new[] {staged, applying})
        {
            Assert.AreEqual((DataSyncLinkMode.TwoWay, DataSyncLinkState.AwaitingReview),
                (h.Link(link.Id).Mode, h.Link(link.Id).State));
        }

        CollectionAssert.AreEqual(new[] {stagedReview.ReviewId}, h.Reviews.Discarded,
            "a review being applied finishes as the link's first contact");
        Assert.IsNotNull(h.Reviews.Get(applyingReview.ReviewId));
    }

    [TestMethod]
    public async Task Withdrawing_a_request_drops_only_a_link_made_for_it()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var fresh = (await h.Links.CreateAsync(Create("a"), true, default)).Link!;
        var synced = h.AddLink("b", l =>
        {
            l.Mode = DataSyncLinkMode.Off;
            l.LastMode = DataSyncLinkMode.TwoWay;
            l.State = DataSyncLinkState.Stopped;
        });
        h.Grants.Outbound.Remove("b");
        var turnedOn = await h.Links.UpdateAsync(synced.Id, DataSyncLinkMode.Follow, null, true, default);
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, turnedOn.Link!.State, "no access: a request went out");

        await h.Links.OnRequestCancelledAsync(fresh.Id, default);
        await h.Links.OnRequestCancelledAsync(synced.Id, default);

        CollectionAssert.AreEqual(new[] {fresh.Id}, h.Store.Deleted, "a link made for the request has nothing to keep");
        var kept = h.Link(synced.Id);
        Assert.AreEqual(DataSyncLinkState.Stopped, kept.State);
        Assert.AreEqual(DataSyncLinkMode.Follow, kept.LastMode);
        Assert.AreEqual(DataSyncLinkService.AccessCancelled, kept.LastErrorCode);
        Assert.AreEqual(3, kept.GetCursors()["extensionGroup"], "its sync state is kept");
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
        Assert.AreEqual(3, on.Link.GetCursors()["extensionGroup"], "no new review: cursors and bases kept");

        // The stop closed the link's items and kept its pending records: the next pull re-merges them all (§8.4
        // condition 4), although the peer has nothing new.
        var peer = h.Peers.Peers["a"];
        await h.FetchOnceAsync();
        var since = peer.ManifestQueries.Single().Since;
        Assert.AreEqual(2, since.Count);
        Assert.IsTrue(since.Values.All(s => s == 0), "a full reconciliation");
        Assert.IsTrue(h.StagedPulls.Peek(link.Id)!.Kinds.All(k => k.FullReconciliation));
        await h.Btm.Start(DataSyncTaskIds.Apply);
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);
        Assert.AreEqual(h.Clock.UtcNow, h.Link(link.Id).LastFullReconciliationAtUtc);

        // Once: then incremental again.
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        await h.FetchOnceAsync();
        Assert.AreEqual(1, peer.ManifestQueries.Count);
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
    public async Task A_reset_peer_is_asked_for_access_again_and_only_the_grant_resets_the_link()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = await ResetPeerLinkAsync(h);
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

        // Asked, not granted: the link waits with everything it had (§8.7 B1, N11).
        Assert.AreEqual(0, h.Store.Deleted.Count, "nothing is reset before the grant");
        var waiting = h.Link(link.Id);
        Assert.AreEqual(link.Id, asked.Link!.Id);
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, waiting.State);
        Assert.AreEqual(DataSyncLinkInitiator.ThisDevice, waiting.Initiator);
        Assert.AreEqual("req-a", waiting.PendingRequestId);
        Assert.IsNotNull(waiting.FirstContactCompletedAtUtc);
        Assert.AreEqual("epoch-1", waiting.PeerLibraryEpoch, "the old epoch holds until the reset");

        // The credentials this device held for the peer are kept (never forgotten along with the request that went
        // out), and while the request waits they grant nothing.
        Assert.IsTrue(h.Grants.Outbound.Contains("a"));
        CollectionAssert.DoesNotContain(h.Grants.Changes.ToArray(), "forget:a");
        h.Grants.Requests.Add(OutgoingRequest(h, "req-a", "awaitingApproval"));
        await h.FetchOnceAsync();
        Assert.AreEqual(0, h.Store.Deleted.Count);
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, h.Link(link.Id).State);

        // Granted (the claim loop's event): now the link is reset and runs a new first contact.
        await h.Links.OnOutboundGrantedAsync("a", default);
        CollectionAssert.AreEqual(new[] {link.Id}, h.Store.Deleted, "bases cleared: the old link is reset");
        var fresh = h.Store.All().Single(l => l.PeerNodeId == "a");
        Assert.AreNotEqual(link.Id, fresh.Id);
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, fresh.State);
        Assert.AreEqual(DataSyncLinkInitiator.ThisDevice, fresh.Initiator);
        Assert.AreEqual(DataSyncLinkMode.Follow, fresh.Mode);
        Assert.IsNull(fresh.PausedReason);
        Assert.IsNull(fresh.PeerLibraryEpoch, "the new epoch is learnt from the next head");
        Assert.IsNull(fresh.FirstContactCompletedAtUtc);
        Assert.AreEqual(0, fresh.GetCursors().Count);
        CollectionAssert.AreEqual(new[] {"customProperty"}, fresh.GetKinds().ToArray());
        Assert.AreEqual(1, h.Observer.Count($"removed:{link.Id}"));
    }

    [TestMethod]
    public async Task A_reset_peer_that_does_not_grant_leaves_the_link_paused_with_everything_it_had()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = await ResetPeerLinkAsync(h);
        Assert.IsNull((await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default)).Problem);

        // Rejected: back to the pause, the error says why, and the stale credentials never read as a grant.
        h.Grants.Requests.Add(OutgoingRequest(h, "req-a", "rejected"));
        await h.FetchOnceAsync();
        var back = h.Link(link.Id);
        Assert.AreEqual(DataSyncLinkState.Paused, back.State);
        Assert.AreEqual(DataSyncPauseReason.PeerReset, back.PausedReason);
        Assert.AreEqual("epochChanged", back.PausedDetail);
        Assert.AreEqual(DataSyncLinkService.AccessRejected, back.LastErrorCode);
        Assert.IsNull(back.PendingRequestId);
        Assert.AreEqual(0, h.Store.Deleted.Count);

        // Asked again, then withdrawn by the person: back to the pause as well, never deleted as a link made for
        // the request.
        h.Grants.Requests.Clear();
        Assert.IsNull((await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default)).Problem);
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, h.Link(link.Id).State);
        await h.Links.OnRequestCancelledAsync(link.Id, default);
        Assert.AreEqual(DataSyncLinkState.Paused, h.Link(link.Id).State);
        Assert.AreEqual(DataSyncLinkService.AccessCancelled, h.Link(link.Id).LastErrorCode);
        Assert.AreEqual(0, h.Store.Deleted.Count);
    }

    [TestMethod]
    public async Task A_reset_peers_request_that_expires_unanswered_never_reads_revoked_credentials_as_the_grant()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = await ResetPeerLinkAsync(h);
        Assert.IsNull((await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default)).Problem);
        CollectionAssert.DoesNotContain(h.Grants.Changes.ToArray(), "forget:a",
            "forgetting them would withdraw the request that just went out");
        h.Grants.Requests.Add(OutgoingRequest(h, "req-a", "awaitingApproval"));

        // Credentials this device holds for the peer while the request is out are never its answer.
        h.Grants.Outbound.Add("a");
        await h.FetchOnceAsync();
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, h.Link(link.Id).State);

        // Nobody approved within ten minutes: the listing drops the request, and the link goes back to its pause with
        // everything it had (§8.7 B1, N11).
        h.Clock.Advance(TimeSpan.FromMinutes(11));
        await h.FetchOnceAsync();
        var back = h.Link(link.Id);
        Assert.AreEqual(
            (DataSyncLinkState.Paused, (DataSyncPauseReason?) DataSyncPauseReason.PeerReset, "epochChanged"),
            (back.State, back.PausedReason, back.PausedDetail));
        Assert.AreEqual((DataSyncLinkService.AccessExpired, (string?) null),
            (back.LastErrorCode, back.PendingRequestId));
        Assert.AreEqual(0, h.Store.Deleted.Count, "nothing was reset");
        Assert.AreEqual(1, h.Store.All().Count);

        // Only the request's own answer resets it.
        h.Grants.Requests.Clear();
        Assert.IsNull((await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default)).Problem);
        h.Grants.Requests.Add(OutgoingRequest(h, "req-a", "granted"));
        await h.FetchOnceAsync();
        CollectionAssert.AreEqual(new[] {link.Id}, h.Store.Deleted);
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, h.Store.All().Single().State);
    }

    [TestMethod]
    public async Task A_reset_peer_asked_again_where_another_install_answers_keeps_the_links_credentials()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = await ResetPeerLinkAsync(h);
        // The pause also stands for another install answering at the peer's address (IdentityConflict): the request
        // meets it there too.
        h.Grants.Answer = _ => throw new DataSyncPeerException(DataSyncPeerErrorCode.IdentityConflict);

        var asked = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default);

        Assert.AreEqual(DataSyncProblemCode.PeerReset, asked.Problem!.Code);
        Assert.AreEqual((DataSyncLinkState.Paused, (DataSyncPauseReason?) DataSyncPauseReason.PeerReset),
            (h.Link(link.Id).State, h.Link(link.Id).PausedReason));
        Assert.IsTrue(h.Grants.Outbound.Contains("a"), "the credentials, which may still be good, are kept");
        CollectionAssert.DoesNotContain(h.Grants.Changes.ToArray(), "forget:a");
        Assert.AreEqual(0, h.Store.Deleted.Count);

        // Once the peer answers as itself, the request goes out and the link waits for its grant as usual.
        h.Grants.Answer = input => new DataSyncAccessRequestOutcome("awaitingApproval", "req-a", "a", "Peer a", null);
        var again = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default);
        Assert.IsNull(again.Problem);
        Assert.IsTrue(DataSyncLinkService.WaitsForResetGrant(h.Link(link.Id)));
        Assert.IsTrue(h.Grants.Outbound.Contains("a"));
    }

    /// <summary>A Follow link whose peer looks reset (B1), with a first contact behind it.</summary>
    private static async Task<DataSyncLinkDbModel> ResetPeerLinkAsync(DataSyncRuntimeHarness h)
    {
        var link = h.AddLink("a", l =>
        {
            l.Mode = DataSyncLinkMode.Follow;
            l.PeerAddress = "192.168.1.20:5000";
            l.SetKinds(["customProperty"]);
        });
        await h.Links.PauseAsync(link.Id, DataSyncPauseReason.PeerReset, "epochChanged", default);
        return link;
    }

    private static DataSyncAccessRequestView OutgoingRequest(DataSyncRuntimeHarness h, string requestId, string status) =>
        new(requestId, DataSyncRequestDirection.Outgoing, "a", "Peer a", DataSyncRequestIntent.Follow, status,
            h.Clock.UtcNow.AddMinutes(10), null, false, null, false);

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
            l.FirstContactCompletedAtUtc = null;
            l.FirstContactKindsJson = null;
        });
        Assert.AreEqual(h.Clock.UtcNow + DataSyncSchedule.StartAnywayAfter, h.Link(link.Id).GetStartAnywayAt());

        // Offered only after the initiator has not finished its review for 7 days (§8.3).
        h.Clock.Advance(DataSyncSchedule.StartAnywayAfter - TimeSpan.FromMinutes(1));
        var early = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.StartAnyway, true, default);
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid, early.Problem!.Code);
        Assert.AreEqual("tooEarly", early.Problem.Detail);
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, h.Link(link.Id).State);

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        var started = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.StartAnyway, true, default);
        Assert.AreEqual(DataSyncLinkState.Active, started.Link!.State);
        Assert.IsNull(started.Link.GetStartAnywayAt());
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
