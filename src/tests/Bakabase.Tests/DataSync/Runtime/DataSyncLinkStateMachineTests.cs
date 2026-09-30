using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.DependencyInjection;

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
        h.Grants.Peers.Add(new DataSyncPeerCandidate("a", "Studio PC", null, true, false, 1, true, true, false, null));

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
        h.Grants.Readers.Add(new DataSyncGrantView("a", "A"));
        var created = await h.Links.CreateAsync(Create("a", DataSyncLinkMode.TwoWay), false, default);
        Assert.IsNull(created.Problem);
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, created.Link!.State);
    }

    [TestMethod]
    public async Task A_two_way_link_made_with_a_code_redeems_it_one_way()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        // No offer to be read back goes with a code (§7.2.3), so nothing here needs to be shared for it.
        h.Grants.SharingEnabled = false;
        h.Grants.Answer = _ => new DataSyncAccessRequestOutcome("granted", null, "a", "A", null);

        var created = await h.Links.CreateAsync(
            new DataSyncLinkCreate(null, "10.0.0.5:5000", "12345678", DataSyncLinkMode.TwoWay, null), true, default);

        Assert.IsNull(created.Problem);
        Assert.AreEqual(DataSyncRequestIntent.Follow, h.Grants.Sent.Single().Intent);
        Assert.AreEqual((DataSyncLinkMode.TwoWay, DataSyncLinkState.AwaitingReview, true),
            (created.Link!.Mode, created.Link.State, created.Link.ReadBackDeclined), "A does not read this device");
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
        Assert.AreEqual(h.Clock.UtcNow + DataSyncSchedule.PollInterval, h.State.GetAttempt(waiting.Id).Due);
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
        Assert.IsTrue(h.State.IsDue(a.Id, h.Clock.UtcNow), "a stopped link is never fetched, and due once it is on");
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
        var revoked = nameof(DataSyncPeerErrorCode.AccessRevoked);
        Assert.AreEqual(revoked, h.Link(follow.Id).LastErrorCode);
        Assert.AreEqual(revoked, h.Link(twoWay.Id).LastErrorCode, "AccessMissing is stored as AccessRevoked");
        Assert.AreEqual(nameof(DataSyncPeerErrorCode.PeerSharingOff), h.Link(sharingOff.Id).LastErrorCode);

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
        h.Grants.Requests.Add(Request("req-a", "awaitingApproval", h.Clock.UtcNow.AddMinutes(10)));
        h.Grants.Requests.Add(Request("req-b", "awaitingApproval", h.Clock.UtcNow.AddMinutes(10)));
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
        h.GrantEvents.InboundGranted("a");
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
        var approver = await h.Links.OnInboundGrantedAsync("b", true, false,
            "unreachable", "B", null, default);

        await h.Links.OnOutboundGrantedAsync("a", default);
        await h.Links.OnOutboundGrantedAsync("b", default);

        Assert.AreEqual(DataSyncLinkState.AwaitingReview, h.Link(initiated.Id).State);
        Assert.IsNull(h.Link(initiated.Id).PendingRequestId);
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, h.Link(approver!.Id).State);
        Assert.IsNull(h.Link(approver.Id).LastErrorCode);
        Assert.IsTrue(h.State.IsDue(approver.Id, h.Clock.UtcNow));
    }

    [TestMethod]
    public async Task A_two_way_inbound_grant_creates_the_approvers_link_once_per_peer()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);

        var link = await h.Links.OnInboundGrantedAsync("b", true, true, null, "B",
            ["customProperty"], default);
        Assert.AreEqual(DataSyncLinkMode.TwoWay, link!.Mode);
        Assert.AreEqual(DataSyncLinkInitiator.Peer, link.Initiator);
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, link.State);
        CollectionAssert.AreEqual(new[] {"customProperty"}, link.GetKinds().ToArray());

        await h.Links.OnInboundGrantedAsync("b", true, true, null, "B", null, default);
        Assert.AreEqual(1, h.Store.All().Count, "the existing link is updated, not duplicated");

        Assert.IsNull(await h.Links.OnInboundGrantedAsync("c", false, false, null, "C", null, default),
            "a Follow grant, or two-way without receiving back, makes no link");
        Assert.AreEqual(1, h.Store.All().Count);
    }

    [TestMethod]
    public async Task A_failed_read_back_still_creates_the_approvers_link_awaiting_access()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);

        var link = await h.Links.OnInboundGrantedAsync("b", true, false,
            "PeerUnreachable", "B", null, default);

        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, link!.State);
        Assert.AreEqual(DataSyncLinkInitiator.Peer, link.Initiator);
        Assert.AreEqual(DataSyncLinkMode.TwoWay, link.Mode);
        Assert.AreEqual(DataSyncLinkService.ReadBackFailed, link.LastErrorCode);
        Assert.AreEqual("PeerUnreachable", link.LastErrorDetail);
    }

    [TestMethod]
    public async Task Try_again_after_a_failed_read_back_asks_only_to_read_the_peer_back()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = (await h.Links.OnInboundGrantedAsync("b", true, false,
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
        h.Grants.Readers.Add(new DataSyncGrantView("a", "A"));
        var cleared = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default);
        Assert.IsFalse(cleared.Link!.ReadBackDeclined);
        Assert.AreEqual(1, h.Grants.Sent.Count);
    }

    [TestMethod]
    public async Task A_copy_once_onto_a_stopped_link_starts_from_a_fresh_row_and_stays_a_copy_once()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var stopped = h.AddLink("a", l =>
        {
            l.Mode = DataSyncLinkMode.Off;
            l.LastMode = DataSyncLinkMode.Follow;
            l.State = DataSyncLinkState.Stopped;
        });

        var copy = await h.Links.CreateAsync(new DataSyncLinkCreate("a", null, null, DataSyncLinkMode.Follow, null,
            CopyOnce: true), true, default);
        Assert.IsNull(copy.Problem);
        CollectionAssert.AreEqual(new[] {stopped.Id}, h.Store.Deleted, "one link per peer: the stopped row goes");
        var id = copy.Link!.Id;
        Assert.AreEqual((DataSyncLinkMode.Off, DataSyncLinkState.AwaitingReview, 0),
            (copy.Link.Mode, copy.Link.State, copy.Link.GetCursors().Count));

        await h.Links.PauseByUserAsync(id, default);
        var resumed = await h.Links.ResumeAsync(id, DataSyncResumeAction.Resume, true, default);
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, resumed.Link!.State, "the copy once did not silently end");

        h.Peers.Peers["a"].HeadErrors.Enqueue(new DataSyncPeerException(DataSyncPeerErrorCode.AccessRevoked));
        await h.FetchOnceAsync();
        Assert.AreEqual((DataSyncLinkState.AwaitingReview, nameof(DataSyncPeerErrorCode.AccessRevoked)),
            (h.Link(id).State, h.Link(id).LastErrorCode));
        Assert.IsTrue(h.Link(id).IsFetchable(), "retried hourly like any link in its first contact");
        h.Clock.Advance(DataSyncSchedule.AccessRetry);
        await h.FetchOnceAsync();
        Assert.IsNull(h.Link(id).LastErrorCode);
    }

    [TestMethod]
    public async Task A_copy_once_turned_on_before_its_start_becomes_the_links_first_sync()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        h.AddLink("a", l =>
        {
            l.Mode = DataSyncLinkMode.Off;
            l.LastMode = DataSyncLinkMode.TwoWay;
            l.State = DataSyncLinkState.Stopped;
        });
        var copy = await h.Links.CreateAsync(new DataSyncLinkCreate("a", null, null, DataSyncLinkMode.Follow, null,
            CopyOnce: true), true, default);
        var id = copy.Link!.Id;
        Assert.AreEqual((DataSyncLinkMode.Off, DataSyncLinkState.AwaitingReview), (copy.Link.Mode, copy.Link.State));
        await h.FetchOnceAsync();
        var copied = h.State.PeekPreview(id);
        Assert.IsNotNull(copied, "the cycle stages the copy once's snapshot");

        // The rule editor's receive arrow: Follow, still waiting for its first sync.
        var on = await h.Links.UpdateAsync(id, DataSyncLinkMode.Follow, null, true, default);
        Assert.IsNull(on.Problem);
        Assert.AreEqual((DataSyncLinkMode.Follow, DataSyncLinkMode.Follow, DataSyncLinkState.AwaitingReview),
            (on.Link!.Mode, on.Link.LastMode, on.Link.State));
        Assert.IsNull(h.State.PeekPreview(id), "the copy once's snapshot goes");

        // The next cycle stages the link's own.
        await h.FetchOnceAsync();
        Assert.AreNotSame(copied, h.State.PeekPreview(id) ?? copied);
    }

    [TestMethod]
    public async Task Approving_a_two_way_request_turns_a_waiting_copy_once_into_the_link()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("a", l =>
        {
            l.Mode = DataSyncLinkMode.Off;
            l.LastMode = DataSyncLinkMode.TwoWay;
            l.State = DataSyncLinkState.AwaitingReview;
            l.FirstContactCompletedAtUtc = null;
        });
        h.State.StagePreview(link.Id, new DataSyncStagedPull("a", "A", h.Peers.Peers["a"].Manifest(
            new DataSyncFeedQuery("follow", new Dictionary<string, long>(), null, null)), [], h.Clock.UtcNow));

        await h.Links.OnInboundGrantedAsync("a", true, true, null, null, null, default);

        Assert.AreEqual((DataSyncLinkMode.TwoWay, DataSyncLinkState.AwaitingReview),
            (h.Link(link.Id).Mode, h.Link(link.Id).State));
        Assert.IsNull(h.State.PeekPreview(link.Id), "its first sync is staged again, as the link's");
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
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, h.Link(synced.Id).State,
            "its stop needs the gate: without it, the fetch cycle ends the request it no longer finds");
        await using (var gate = await DataSyncGateHold.TryEnterAsync(
                         h.Provider.GetRequiredService<IDataSyncGateEntry>(), null, default))
            await h.Links.OnRequestCancelledAsync(synced.Id, default, gate);

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
        });
        var active = h.AddLink("c", l => l.Mode = DataSyncLinkMode.Follow);
        foreach (var link in new[] {stoppedDone, stoppedNew, active}) h.Store.SetOpenItems(link.Id, 1);

        foreach (var peer in new[] {"a", "b", "c"})
            await h.Links.OnInboundGrantedAsync(peer, true, true, null, null, null, default);

        Assert.AreEqual(DataSyncLinkState.Active, h.Link(stoppedDone.Id).State);
        Assert.AreEqual(DataSyncLinkMode.TwoWay, h.Link(stoppedDone.Id).Mode);
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, h.Link(stoppedNew.Id).State);
        Assert.AreEqual(DataSyncLinkInitiator.Peer, h.Link(stoppedNew.Id).Initiator);
        Assert.AreEqual(DataSyncLinkState.Active, h.Link(active.Id).State, "any other state is kept");
        Assert.AreEqual(DataSyncLinkMode.TwoWay, h.Link(active.Id).Mode);
        Assert.AreEqual(0, h.Store.Deleted.Count);
        Assert.IsTrue(h.Store.Items.All(i => i.ClosedAtUtc is null), "never stopped or reset: nothing closed");
    }

    [TestMethod]
    public async Task Off_stops_the_link_and_on_resumes_it_without_a_new_review()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("a");
        h.Store.SetOpenItems(link.Id, 1);
        h.Grants.Readers.Add(new DataSyncGrantView("a", "A"));
        h.State.StagePull(link.Id, new DataSyncStagedPull("a", "A",
            h.Peers.Peers["a"].Manifest(new DataSyncFeedQuery("twoWay", new Dictionary<string, long>(), null, null)),
            [], h.Clock.UtcNow));

        var off = await h.Links.UpdateAsync(link.Id, DataSyncLinkMode.Off, null, false, default);
        Assert.IsNull(off.Problem, "reducing access is open to every caller");
        Assert.AreEqual(DataSyncLinkState.Stopped, off.Link!.State);
        Assert.AreEqual(DataSyncLinkMode.Off, off.Link.Mode);
        Assert.AreEqual(DataSyncLinkMode.TwoWay, off.Link.LastMode);
        Assert.AreEqual(DataSyncInboxClosure.LinkStopped, h.Store.Items.Single().Closure);
        Assert.IsNull(h.State.PeekPull(link.Id));

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
        Assert.IsTrue(h.State.PeekPull(link.Id)!.Kinds.All(k => k.FullReconciliation));
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
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.StartAnyway, true, default)).Problem!.Code);
        var resumed = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.Resume, true, default);
        Assert.AreEqual(DataSyncLinkState.Active, resumed.Link!.State);
        Assert.IsNull(resumed.Link.PausedReason);

        await h.Links.PauseAsync(link.Id, DataSyncPauseReason.PeerIdentityDuplicated, null, default);
        Assert.AreEqual(DataSyncLinkState.Active,
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.Resume, true, default)).Link!.State);
        Assert.AreEqual(2, h.Observer.Count("paused:"), "every pause is observed once");
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
    public async Task A_reset_peer_asked_again_starts_over_from_a_fresh_row()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = await ResetPeerLinkAsync(h, "a");
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid,
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.Resume, true, default)).Problem!.Code,
            "a reset peer revoked every grant: resuming cannot work");
        Assert.AreEqual(DataSyncProblemCode.NotAllowedOnThisDevice,
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, false, default)).Problem!.Code);

        var asked = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default);
        Assert.IsNull(asked.Problem);
        var sent = h.Grants.Sent.Single();
        Assert.AreEqual((DataSyncRequestIntent.Follow, "192.168.1.20:5000"), (sent.Intent, sent.Address));
        CollectionAssert.Contains(h.Grants.Changes.ToArray(), "forget:a",
            "the credentials the reset revoked are forgotten first: only the answer reads as access");

        // The old link goes when the person asks; a fresh one waits for access and runs a new first contact.
        CollectionAssert.AreEqual(new[] {link.Id}, h.Store.Deleted);
        Assert.AreEqual(1, h.Observer.Count($"removed:{link.Id}"));
        var fresh = h.Store.All().Single(l => l.PeerNodeId == "a");
        Assert.AreEqual(fresh.Id, asked.Link!.Id);
        Assert.AreEqual((DataSyncLinkState.AwaitingAccess, DataSyncLinkInitiator.ThisDevice, DataSyncLinkMode.Follow,
            "req-a"), (fresh.State, fresh.Initiator, fresh.Mode, fresh.PendingRequestId));
        Assert.IsNull(fresh.PausedReason);
        Assert.IsNull(fresh.PeerLibraryEpoch, "the new epoch is learnt from the next head");
        Assert.IsNull(fresh.FirstContactCompletedAtUtc);
        Assert.AreEqual(0, fresh.GetCursors().Count);
        CollectionAssert.AreEqual(new[] {"customProperty"}, fresh.GetKinds().ToArray());

        // Granted (the claim loop's event): on to its review.
        h.Grants.Outbound.Add("a");
        await h.Links.OnOutboundGrantedAsync("a", default);
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, h.Link(fresh.Id).State);
    }

    [TestMethod]
    public async Task A_reset_peers_fresh_link_ends_or_is_withdrawn_like_any_request()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var rejected = (await h.Links.ResumeAsync((await ResetPeerLinkAsync(h, "a")).Id,
            DataSyncResumeAction.AskAccessAgain, true, default)).Link!;
        var withdrawn = (await h.Links.ResumeAsync((await ResetPeerLinkAsync(h, "b")).Id,
            DataSyncResumeAction.AskAccessAgain, true, default)).Link!;

        h.Grants.Requests.Add(Request("req-a", "rejected", h.Clock.UtcNow.AddMinutes(10)));
        h.Grants.Requests.Add(Request("req-b", "awaitingApproval", h.Clock.UtcNow.AddMinutes(10)));
        await h.FetchOnceAsync();
        var stopped = h.Link(rejected.Id);
        Assert.AreEqual((DataSyncLinkState.Stopped, DataSyncLinkService.AccessRejected),
            (stopped.State, stopped.LastErrorCode), "stopped, kept with Dismiss");

        await h.Links.OnRequestCancelledAsync(withdrawn.Id, default);
        Assert.IsFalse(h.Store.All().Any(l => l.PeerNodeId == "b"), "a link made for the request goes with it");
    }

    [TestMethod]
    public async Task A_reset_peer_that_grants_at_once_resets_the_link_at_once()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = await ResetPeerLinkAsync(h, "a");
        h.Grants.Answer = _ => new DataSyncAccessRequestOutcome("granted", null, "a", "Peer a", null);

        var asked = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.AskAccessAgain, true, default);

        CollectionAssert.AreEqual(new[] {link.Id}, h.Store.Deleted, "the grant resets the old link");
        var fresh = h.Store.All().Single();
        Assert.AreNotEqual(link.Id, fresh.Id);
        Assert.AreEqual((fresh.Id, DataSyncLinkState.AwaitingReview, (DataSyncPauseReason?) null),
            (asked.Link!.Id, fresh.State, fresh.PausedReason));
    }

    /// <summary>A Follow link whose peer looks reset (B1), with a first contact behind it.</summary>
    private static async Task<DataSyncLinkDbModel> ResetPeerLinkAsync(DataSyncRuntimeHarness h, string peer)
    {
        var link = h.AddLink(peer, l =>
        {
            l.Mode = DataSyncLinkMode.Follow;
            l.PeerAddress = "192.168.1.20:5000";
            l.SetKinds(["customProperty"]);
        });
        await h.Links.PauseAsync(link.Id, DataSyncPauseReason.PeerReset, "epochChanged", default);
        return link;
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
            l.FirstContactCompletedAtUtc = null;
        });
        Assert.AreEqual(h.Clock.UtcNow + DataSyncSchedule.StartAnywayAfter, h.Link(link.Id).GetStartAnywayAt());

        // Offered only after the initiator has not finished its review for 7 days (§8.3).
        h.Clock.Advance(DataSyncSchedule.StartAnywayAfter - TimeSpan.FromMinutes(1));
        var early = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.StartAnyway, true, default);
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid, early.Problem!.Code);
        Assert.AreEqual("tooEarly", early.Problem.Detail);
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, h.Link(link.Id).State);

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.Store.Edit(link.Id, l => l.LastErrorCode = nameof(DataSyncPeerErrorCode.AccessRevoked));
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid, (await h.Links.ResumeAsync(link.Id,
            DataSyncResumeAction.StartAnyway, true, default)).Problem!.Code, "not while the peer refuses the link");
        h.Store.Edit(link.Id, l => l.LastErrorCode = null);
        var started = await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.StartAnyway, true, default);
        Assert.AreEqual(DataSyncLinkState.Active, started.Link!.State);
        Assert.IsNull(started.Link.GetStartAnywayAt());
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid,
            (await h.Links.ResumeAsync(link.Id, DataSyncResumeAction.StartAnyway, true, default)).Problem!.Code);
    }

    [TestMethod]
    public async Task Reset_deletes_the_link_and_forgets_its_staged_pulls()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("a", l => l.State = DataSyncLinkState.AwaitingReview);
        var manifest = h.Peers.Peers["a"].Manifest(new DataSyncFeedQuery("twoWay", new Dictionary<string, long>(), null,
            null));
        h.State.StagePull(link.Id, new DataSyncStagedPull("a", "A", manifest, [], h.Clock.UtcNow));
        h.State.StagePreview(link.Id, new DataSyncStagedPull("a", "A", manifest, [], h.Clock.UtcNow));

        Assert.IsNull(await h.Links.ResetAsync(link.Id, default));
        CollectionAssert.AreEqual(new[] {link.Id}, h.Store.Deleted);
        Assert.IsNull(h.State.PeekPull(link.Id));
        Assert.IsNull(h.State.PeekPreview(link.Id));
        Assert.AreEqual(1, h.Observer.Count("removed:"));
        Assert.AreEqual(DataSyncProblemCode.LinkNotFound, (await h.Links.ResetAsync(link.Id, default))!.Code);
    }
}
