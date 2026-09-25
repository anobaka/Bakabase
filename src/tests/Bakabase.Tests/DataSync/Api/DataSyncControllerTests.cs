using System.Collections;
using System.Reflection;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Service.Controllers;
using Bakabase.Service.Models.Input.DataSync;
using Bakabase.Tests.DataSync.Runtime;
using Microsoft.Extensions.DependencyInjection;
using static Bakabase.Tests.DataSync.Api.DataSyncApiHarness;

namespace Bakabase.Tests.DataSync.Api;

/// <summary>
/// Every §10.1 endpoint through the controller and the real facade over fakes of packages C and D (§13.5): reads
/// never wait for the gate and never write, gated actions answer Busy while it is held, the "Creates access" column
/// refuses an unpaired Unrestricted caller, the management plane cannot reach library grants, a resolve batch holds
/// every conflict of an entity, and every time in every answer is UTC.
/// </summary>
[TestClass]
public class DataSyncControllerTests
{
    private static readonly string[] AllKinds = [..DataSyncKindIds.All];

    /// <summary>A small world: two links, items, an entity with bases, a reader, requests and a history entry.</summary>
    private static async Task<DataSyncApiHarness> WorldAsync(Action<IServiceCollection>? configure = null)
    {
        var h = await DataSyncApiHarness.CreateAsync(configure);
        var nas = h.AddLink("node-nas", l => l.PeerName = "NAS");
        var pc = h.AddLink("node-pc", l =>
        {
            l.PeerName = "PC-2";
            l.Mode = DataSyncLinkMode.Follow;
        });
        h.AddEntity("12", Key(1));
        h.Store.Bases[(nas.Id, DataSyncKindIds.CustomProperty)] =
        [
            new DataSyncPeerBase(DataSyncKindIds.CustomProperty, new SyncKey(Key(1)), DataSyncBaseState.Normal, null,
                null, new Dictionary<string, string>(), null, null, []),
            new DataSyncPeerBase(DataSyncKindIds.CustomProperty, new SyncKey(Key(2)), DataSyncBaseState.Excluded,
                DataSyncExclusionReason.Skipped, null, new Dictionary<string, string>(), null, null, [Key(2)]),
        ];
        h.AddItem(DataSyncInboxItemType.FieldConflict, nas.Id, Key(1));
        h.AddItem(DataSyncInboxItemType.DeletedThere, pc.Id, Key(3), subjectPath: "");
        h.Store.Readers.Add(new DataSyncReaderDbModel
        {
            NodeId = "node-nas", Name = "NAS", FirstReadAtUtc = h.Clock.UtcNow, LastReadAtUtc = h.Clock.UtcNow,
            Mode = "twoWay", State = "ok", LastSeqServed = 3, NotifiedAtUtc = h.Clock.UtcNow,
        });
        h.Grants.Readers.Add(new DataSyncGrantView("node-nas", "NAS",
            DateTime.SpecifyKind(h.Clock.UtcNow, DateTimeKind.Unspecified)));
        h.Grants.Requests.Add(new DataSyncAccessRequestView("req-in-1", DataSyncRequestDirection.Incoming, "node-new",
            "New PC", DataSyncRequestIntent.TwoWay, "awaitingApproval", h.Clock.UtcNow.AddMinutes(10), "192.168.1.40",
            false, null, false));
        h.Grants.Requests.Add(new DataSyncAccessRequestView("req-in-old", DataSyncRequestDirection.Incoming,
            "node-old", "Old PC", DataSyncRequestIntent.Follow, "awaitingApproval", h.Clock.UtcNow.AddMinutes(-1),
            "192.168.1.41", false, null, false));
        h.Grants.Peers.Add(new DataSyncPeerCandidate("node-nas", "NAS", "http://192.168.1.10:5000", true, true, 1,
            true, true, true, null, "online"));
        h.Store.History.Add(new DataSyncApplyLogDbModel
        {
            Id = 1, Kind = DataSyncHistoryKind.AutoSync, LinkId = nas.Id, PeerNodeId = "node-nas", PeerName = "NAS",
            AppliedAtUtc = DateTime.SpecifyKind(h.Clock.UtcNow, DateTimeKind.Unspecified),
            SummaryJson = DataSyncHistoryJson.WriteSummary(new DataSyncHistoryCounts(1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)),
            ResultJson = "{\"items\":[]}", PreImageJson = "{}",
        });
        return h;
    }

    // ---- the gate ------------------------------------------------------------------------------------------------

    /// <summary>Every read of §10.1, with what it needs to find.</summary>
    private static IEnumerable<(string Name, Func<DataSyncController, Task<object?>> Call)> Reads() =>
    [
        ("overview", async c => (await c.GetOverview(default)).Data),
        ("map", async c => (await c.GetMap(default)).Data),
        ("peers", async c => (await c.GetPeers(false, default)).Data),
        ("peers?discover", async c => (await c.GetPeers(true, default)).Data),
        ("links", async c => (await c.GetLinks(default)).Data),
        ("review", async c => (await c.GetReview("review-gone", default)).Data),
        ("review changes", async c => (await c.GetReviewChanges("review-gone", "plan", "item", null, 0, 10, default)).Data),
        ("requests", async c => (await c.GetRequests(default)).Data),
        ("readers", async c => (await c.GetReaders(default)).Data),
        ("inbox", async c => (await c.GetInbox(false, null, null, 0, 100, default)).Data),
        ("inbox item", async c => (await c.GetInboxItem(1, default)).Data),
        ("inbox preview", async c => (await c.PreviewInboxItem(1, default)).Data),
        ("entities", async c => (await c.GetEntities(DataSyncKindIds.CustomProperty, default)).Data),
        ("history", async c => (await c.GetHistory(default)).Data),
        ("history entry", async c => (await c.GetHistoryEntry(1, default)).Data),
        ("undo preview", async c => (await c.PreviewUndo(1, default)).Data),
        ("restore", async c => (await c.GetRestore(default)).Data),
    ];

    [TestMethod]
    public async Task Reads_answer_while_the_gate_is_held()
    {
        await using var h = await WorldAsync();
        using var _ = h.Gate.Hold();
        foreach (var (name, call) in Reads())
        {
            var data = await h.CallAsync(Callers.Loopback, call);
            Assert.AreNotEqual(DataSyncProblemCode.Busy, ProblemOf(data)?.Code, name);
        }

        Assert.AreEqual(0, h.Gate.Entered, "no read entered the gate");
    }

    [TestMethod]
    public async Task Gated_actions_answer_busy_while_the_gate_is_held_and_change_nothing()
    {
        await using var h = await WorldAsync();
        var item = h.Store.Items[0];
        var writes = h.Store.Writes;
        using (h.Gate.Hold())
        {
            var busy = new (string Name, Func<DataSyncController, Task<DataSyncProblem?>> Call)[]
            {
                ("create link", async c => (await c.CreateLink(new DataSyncLinkCreateInput("node-x", null, null,
                    DataSyncLinkMode.Follow, AllKinds), default)).Data!.Problem),
                ("update link", async c => (await c.UpdateLink(1, new DataSyncLinkUpdateInput(DataSyncLinkMode.Off, null),
                    default)).Data!.Problem),
                ("pause link", async c => (await c.PauseLink(1, default)).Data!.Problem),
                ("resume link", async c => (await c.ResumeLink(1, new DataSyncLinkResumeInputModel(), default)).Data!.Problem),
                ("reset link", async c => (await c.ResetLink(1, default)).Data),
                ("pause all", async c => (await c.SetAllPaused(new DataSyncPausedInputModel { Paused = true }, default)).Data),
                ("new definitions stay local", async c => (await c.SetSharing(
                    new DataSyncSharingInput(false, false, true), default)).Data),
                ("apply review", async c => (await c.ApplyReview("review-1", new DataSyncReviewApplyInput([], true),
                    default)).Data!.Problem),
                ("resolve", async c => (await c.Resolve(new DataSyncResolveBatchInput(
                    [new DataSyncResolveInput(item.Id, DataSyncInboxAction.KeepLocal, item.Token, null, null, null, null)],
                    false), default)).Data!.Problem),
                ("entity setting", async c => (await c.SetEntitySync(DataSyncKindIds.CustomProperty, "12",
                    new DataSyncEntitySyncInput(DataSyncEntitySyncState.Detached, null, null, null), default)).Data!.Problem),
                ("undo", async c => (await c.Undo(1, default)).Data!.Problem),
                ("restore", async c => (await c.ChooseRestore(new DataSyncRestoreChoiceInputModel
                    { Choice = DataSyncRestoreChoice.ThisDeviceWins }, default)).Data!.Problem),
            };
            foreach (var (name, call) in busy)
            {
                Assert.AreEqual(DataSyncProblemCode.Busy, (await h.CallAsync(Callers.Loopback, call))?.Code, name);
            }
        }

        Assert.AreEqual(writes, h.Store.Writes, "a busy answer changed something");
        Assert.AreEqual(0, h.Grants.Sent.Count);
        Assert.IsTrue(h.Btm.Tasks.All(t => !DataSyncTaskIds.IsWriteTask(t.Id)), "a busy answer enqueued a task");
    }

    [TestMethod]
    public async Task Ungated_actions_answer_while_the_gate_is_held()
    {
        await using var h = await WorldAsync();
        using var _ = h.Gate.Hold();

        Assert.IsNull((await h.CallAsync(Callers.Loopback, c => c.SetSharing(new DataSyncSharingInput(true, true),
            default))).Data);
        Assert.AreEqual(DataSyncTaskIds.Fetch, (await h.CallAsync(Callers.Loopback,
            c => c.SyncNow(new DataSyncSyncNowInputModel(), default))).Data!.TaskId);
        Assert.IsNull((await h.CallAsync(Callers.Loopback, c => c.RevokeReader("node-nas", default))).Data);
        Assert.IsNull((await h.CallAsync(Callers.Loopback, c => c.CreateInvitation(new DataSyncInvitationInput(false),
            default))).Data!.Problem);
        Assert.IsNull((await h.CallAsync(Callers.Loopback, c => c.RejectRequest("req-in-1", default))).Data);
        Assert.AreEqual(DataSyncProblemCode.UnknownItem, (await h.CallAsync(Callers.Loopback,
            c => c.CancelTask("DataSyncUndo:42", default))).Data!.Code);
    }

    // ---- GETs never write --------------------------------------------------------------------------------------

    [TestMethod]
    public async Task Gets_never_write()
    {
        await using var h = await WorldAsync();
        var writes = h.Store.Writes;
        foreach (var (name, call) in Reads()) await h.CallAsync(Callers.Loopback, call);

        Assert.AreEqual(writes, h.Store.Writes);
        Assert.AreEqual(0, h.Notifications.Records.Count);
        Assert.AreEqual(0, h.Grants.Changes.Count);
        Assert.AreEqual(0, h.Grants.Sent.Count);

        // Discovery saw a peer this device syncs with: its link is due now, in memory only (§8.2, F78).
        Assert.IsTrue(h.State.IsWoken(1));
        Assert.AreEqual(1, (await h.CallAsync(Callers.Loopback, c => c.GetPeers(true, default))).Data!
            .Single(p => p.NodeId == "node-nas").LinkId);
    }

    // ---- the access rule (§7.1.5) --------------------------------------------------------------------------------

    [TestMethod]
    public async Task An_unpaired_unrestricted_browser_may_not_create_access_and_nothing_is_sent()
    {
        await using var h = await WorldAsync();
        var browser = Callers.UnpairedUnrestricted;

        var link = (await h.CallAsync(browser, c => c.CreateLink(new DataSyncLinkCreateInput("node-newpc", null, null,
            DataSyncLinkMode.TwoWay, AllKinds), default))).Data!;
        Assert.AreEqual(DataSyncProblemCode.NotAllowedOnThisDevice, link.Problem?.Code);
        var copy = (await h.CallAsync(browser, c => c.CreateCopyOnce(new DataSyncCopyOnceInput(null,
            "192.168.1.40:34567", "48213705", AllKinds), default))).Data!;
        Assert.AreEqual(DataSyncProblemCode.NotAllowedOnThisDevice, copy.Problem?.Code);
        Assert.AreEqual(DataSyncProblemCode.NotAllowedOnThisDevice, (await h.CallAsync(browser,
            c => c.SetSharing(new DataSyncSharingInput(true, true), default))).Data?.Code);
        Assert.AreEqual(DataSyncProblemCode.NotAllowedOnThisDevice, (await h.CallAsync(browser,
            c => c.CreateInvitation(new DataSyncInvitationInput(true), default))).Data!.Problem?.Code);
        Assert.AreEqual(DataSyncProblemCode.NotAllowedOnThisDevice, (await h.CallAsync(browser,
            c => c.ApproveRequest("req-in-1", new DataSyncApproveInput(true, null), default))).Data!.Problem?.Code);

        Assert.AreEqual(0, h.Grants.Sent.Count, "no request was sent");
        Assert.AreEqual(0, h.Grants.Changes.Count, "no access changed");
        Assert.AreEqual(2, h.Store.All().Count, "no link row was made");

        // Shutting access off stays open to it.
        Assert.IsNull((await h.CallAsync(browser, c => c.SetSharing(new DataSyncSharingInput(false), default))).Data);
        Assert.IsNull((await h.CallAsync(browser, c => c.RevokeReader("node-nas", default))).Data);
        Assert.IsNull((await h.CallAsync(browser, c => c.RejectRequest("req-in-1", default))).Data);
        CollectionAssert.AreEqual(new[] { "sharing:False:False", "revoke:node-nas", "reject:req-in-1" },
            h.Grants.Changes.ToArray());
    }

    [TestMethod]
    public async Task This_device_and_a_paired_device_create_access()
    {
        foreach (var caller in new[] { Callers.Loopback, Callers.Paired })
        {
            await using var h = await WorldAsync();
            var result = (await h.CallAsync(caller, c => c.CreateLink(new DataSyncLinkCreateInput("node-newpc", null,
                null, DataSyncLinkMode.TwoWay, AllKinds), default))).Data!;

            Assert.IsNull(result.Problem);
            Assert.AreEqual(DataSyncLinkState.AwaitingAccess, result.Link!.State);
            Assert.AreEqual("req-node-newpc", result.RequestId);
            Assert.AreEqual(DataSyncRequestIntent.TwoWay, h.Grants.Sent.Single().Intent);
            Assert.IsFalse(h.Gate.IsHeld, "the gate is released after the row");
        }
    }

    [TestMethod]
    public async Task A_follow_link_to_a_device_this_one_already_reads_sends_nothing()
    {
        await using var h = await WorldAsync();
        h.Grants.Outbound.Add("node-den");
        var result = (await h.CallAsync(Callers.UnpairedUnrestricted, c => c.CreateLink(
            new DataSyncLinkCreateInput("node-den", null, null, DataSyncLinkMode.Follow, AllKinds), default))).Data!;

        Assert.IsNull(result.Problem);
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, result.Link!.State);
        Assert.AreEqual(0, h.Grants.Sent.Count);
    }

    [TestMethod]
    public async Task The_management_plane_never_sees_library_requests()
    {
        // G29: a library request's id is not a datasync request, so approving it here finds nothing.
        await using var h = await WorldAsync();
        var result = (await h.CallAsync(Callers.Loopback, c => c.ApproveRequest("library-request-1",
            new DataSyncApproveInput(true, null), default))).Data!;

        Assert.AreEqual(DataSyncProblemCode.RequestNotFound, result.Problem?.Code);
        Assert.AreEqual(DataSyncProblemCode.RequestNotFound, (await h.CallAsync(Callers.Loopback,
            c => c.RejectRequest("library-request-1", default))).Data?.Code);
        Assert.AreEqual(0, h.Grants.Changes.Count);
    }

    [TestMethod]
    public async Task With_remote_access_off_no_code_or_approval_is_given()
    {
        // G36: a device nobody can reach can neither hand out a code nor approve a request.
        await using var h = await WorldAsync();
        h.Grants.RemoteAccessMode = RemoteAccessMode.Disabled;

        Assert.AreEqual(DataSyncProblemCode.RemoteAccessOff, (await h.CallAsync(Callers.Loopback,
            c => c.CreateInvitation(new DataSyncInvitationInput(false), default))).Data!.Problem?.Code);
        Assert.AreEqual(DataSyncProblemCode.RemoteAccessOff, (await h.CallAsync(Callers.Loopback,
            c => c.ApproveRequest("req-in-1", new DataSyncApproveInput(true, null), default))).Data!.Problem?.Code);
        Assert.AreEqual(0, h.Grants.Changes.Count);

        h.Grants.RemoteAccessMode = RemoteAccessMode.Enabled;
        h.Grants.SharingEnabled = false;
        Assert.AreEqual(DataSyncProblemCode.SharingOff, (await h.CallAsync(Callers.Loopback,
            c => c.CreateInvitation(new DataSyncInvitationInput(false), default))).Data!.Problem?.Code);
    }

    [TestMethod]
    public async Task A_refusal_of_the_federation_side_comes_back_as_it_is()
    {
        await using var h = await WorldAsync();
        h.Grants.Refuse = new DataSyncProblem(DataSyncProblemCode.InvitationInvalid, "expired");
        Assert.AreEqual(DataSyncProblemCode.InvitationInvalid, (await h.CallAsync(Callers.Loopback,
            c => c.CreateInvitation(new DataSyncInvitationInput(false), default))).Data!.Problem?.Code);
    }

    [TestMethod]
    public async Task Approving_two_way_with_receive_back_makes_the_approvers_link_with_the_kinds_asked_for()
    {
        await using var h = await WorldAsync();
        var result = (await h.CallAsync(Callers.Paired, c => c.ApproveRequest("req-in-1",
            new DataSyncApproveInput(true, [DataSyncKindIds.CustomProperty]), default))).Data!;

        Assert.IsNull(result.Problem);
        Assert.IsTrue(result.ReadBackGranted);
        var link = result.CreatedLink!;
        Assert.AreEqual("node-new", link.PeerNodeId);
        Assert.AreEqual(DataSyncLinkMode.TwoWay, link.Mode);
        Assert.AreEqual(DataSyncLinkInitiator.Peer, link.Initiator);
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, link.State);
        CollectionAssert.AreEqual(new[] { DataSyncKindIds.CustomProperty }, link.Kinds.ToArray());
        CollectionAssert.AreEqual(new[] { "approve:req-in-1:True" }, h.Grants.Changes.ToArray());
    }

    [TestMethod]
    public async Task A_failed_read_back_still_makes_the_approvers_link_waiting_for_access()
    {
        // N14: the failure needs a link view to be shown on.
        await using var h = await WorldAsync();
        h.Grants.Approval = (request, _) => new DataSyncApprovalOutcome(request.NodeId, request.NodeName,
            request.Intent, false, "Unreachable");
        var link = (await h.CallAsync(Callers.Loopback, c => c.ApproveRequest("req-in-1",
            new DataSyncApproveInput(true, null), default))).Data!.CreatedLink!;

        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, link.State);
        Assert.AreEqual(DataSyncLinkService.ReadBackFailed, link.LastErrorCode);
    }

    [TestMethod]
    public async Task Cancelling_a_request_this_device_filed_drops_the_link_that_waited_for_it()
    {
        await using var h = await WorldAsync();
        var waiting = h.AddLink("node-away", l =>
        {
            l.State = DataSyncLinkState.AwaitingAccess;
            l.PendingRequestId = "req-out-1";
        });
        h.Grants.Requests.Add(new DataSyncAccessRequestView("req-out-1", DataSyncRequestDirection.Outgoing, "node-away",
            "Away", DataSyncRequestIntent.Follow, "awaitingApproval", h.Clock.UtcNow.AddMinutes(5), null, false, null,
            false));

        Assert.IsNull((await h.CallAsync(Callers.Loopback, c => c.CancelRequest("req-out-1", default))).Data);
        Assert.IsNull(h.Store.Get(waiting.Id));
        Assert.AreEqual(DataSyncProblemCode.RequestNotFound, (await h.CallAsync(Callers.Loopback,
            c => c.CancelRequest("req-in-1", default))).Data?.Code, "an incoming request is not withdrawn");
    }

    // ---- resolving (§9.2) ----------------------------------------------------------------------------------------

    [TestMethod]
    public async Task Every_open_conflict_of_an_entity_is_resolved_together()
    {
        await using var h = await WorldAsync();
        var first = h.Store.Items.Single(i => i.Type == DataSyncInboxItemType.FieldConflict);
        var second = h.AddItem(DataSyncInboxItemType.ChildRenameConflict, 2, Key(1), subjectPath: "choice:a1");

        var partial = (await h.CallAsync(Callers.Loopback, c => c.Resolve(new DataSyncResolveBatchInput(
            [Resolution(first, DataSyncInboxAction.KeepLocal)], true), default))).Data!;
        Assert.AreEqual(DataSyncProblemCode.ResolveTogether, partial.Problem?.Code);
        Assert.AreEqual(second.Id.ToString(), partial.Problem!.Detail);
        Assert.IsNull(partial.TaskId);

        var whole = (await h.CallAsync(Callers.Loopback, c => c.Resolve(new DataSyncResolveBatchInput(
            [Resolution(first, DataSyncInboxAction.KeepLocal), Resolution(second, DataSyncInboxAction.UseRemote)], true),
            default))).Data!;
        Assert.IsNull(whole.Problem);
        StringAssert.StartsWith(whole.TaskId, DataSyncTaskIds.ResolvePrefix + ":");
        Assert.IsNotNull(h.Btm.GetTaskViewModel(whole.TaskId!));
    }

    [TestMethod]
    public async Task A_resolve_batch_is_checked_before_anything_is_enqueued()
    {
        await using var h = await WorldAsync();
        var conflict = h.Store.Items.Single(i => i.Type == DataSyncInboxItemType.FieldConflict);
        var deletedOnFollow = h.Store.Items.Single(i => i.Type == DataSyncInboxItemType.DeletedThere);
        var suggestion = h.AddItem(DataSyncInboxItemType.LinkSuggestion, 1, Key(5), "",
            Payload(candidates: [new DataSyncInboxCandidate("12", "Rating", "Number", DataSyncNaturalMatch.Exact, true),
                new DataSyncInboxCandidate("13", "Rating", "Text", DataSyncNaturalMatch.Clash, false)]));
        var closed = h.AddItem(DataSyncInboxItemType.TypeChange, 1, Key(6), "");
        h.Store.CloseWhere(i => i.Id == closed.Id, DataSyncInboxClosure.ResolvedElsewhere);

        async Task<DataSyncProblem?> Resolve(params DataSyncResolveInput[] items) =>
            (await h.CallAsync(Callers.Loopback, c => c.Resolve(new DataSyncResolveBatchInput(items, false), default)))
            .Data!.Problem;

        Assert.AreEqual(DataSyncProblemCode.NothingSelected, (await Resolve())?.Code);
        Assert.AreEqual(DataSyncProblemCode.UnknownItem,
            (await Resolve(new DataSyncResolveInput(999, DataSyncInboxAction.KeepLocal, "t", null, null, null, null)))?.Code);
        Assert.AreEqual(DataSyncProblemCode.InboxItemClosed, (await Resolve(Resolution(closed, DataSyncInboxAction.Convert)))?.Code);
        Assert.AreEqual(DataSyncProblemCode.InboxItemChanged,
            (await Resolve(Resolution(conflict, DataSyncInboxAction.KeepLocal) with { Token = "old" }))?.Code);
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid,
            (await Resolve(Resolution(conflict, DataSyncInboxAction.Link)))?.Code, "not an action of a conflict");
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid,
            (await Resolve(Resolution(deletedOnFollow, DataSyncInboxAction.RestoreEverywhere)))?.Code,
            "restoring everywhere is two-way only");
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid,
            (await Resolve(Resolution(conflict, DataSyncInboxAction.UseCustom) with { CustomValue = "a\0b" }))?.Code);
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid,
            (await Resolve(Resolution(suggestion, DataSyncInboxAction.Link) with { TargetLocalKey = "13" }))?.Code,
            "a candidate of another type cannot be chosen");
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid,
            (await Resolve(Resolution(conflict, DataSyncInboxAction.KeepLocal), Resolution(conflict, DataSyncInboxAction.KeepLocal)))?.Code);
        Assert.IsTrue(h.Btm.Tasks.All(t => !DataSyncTaskIds.IsWriteTask(t.Id)));

        Assert.IsNull(await Resolve(Resolution(suggestion, DataSyncInboxAction.Link) with { TargetLocalKey = "12" }));
        Assert.IsNull(await Resolve(Resolution(conflict, DataSyncInboxAction.UseCustom) with { CustomValue = "Genres" }));
    }

    [TestMethod]
    public async Task Applying_all_of_a_large_change_enqueues_the_apply_right_after_the_resolution()
    {
        await using var h = await WorldAsync();
        await h.Btm.Initialize();
        var large = h.AddItem(DataSyncInboxItemType.LargeChange, 1, SyncKey.LinkLevel.Value, "largeChange", kind: "");

        var start = (await h.CallAsync(Callers.Loopback, c => c.Resolve(new DataSyncResolveBatchInput(
            [Resolution(large, DataSyncInboxAction.ApplyAll)], false), default))).Data!;
        Assert.IsNull(start.Problem);

        await DataSyncRuntimeHarness.WaitUntilAsync(() => h.Btm.GetTaskViewModel(start.TaskId!)?.Status ==
                                                          BTaskStatus.Completed, "the resolution completed");
        await DataSyncRuntimeHarness.WaitUntilAsync(() => h.Btm.GetTaskViewModel(DataSyncTaskIds.Apply) is not null,
            "DataSyncApply was enqueued");
    }

    // ---- the inbox -----------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task An_item_offers_the_actions_of_its_links_mode_and_a_closed_one_none()
    {
        await using var h = await WorldAsync();
        var onFollow = h.Store.Items.Single(i => i.Type == DataSyncInboxItemType.DeletedThere);
        var view = (await h.CallAsync(Callers.Loopback, c => c.GetInboxItem(onFollow.Id, default))).Data!;
        CollectionAssert.AreEqual(new[] { DataSyncInboxAction.DeleteHere, DataSyncInboxAction.KeepHereOnly },
            view.AllowedActions.ToArray());
        Assert.IsNull(view.DefaultAction);
        Assert.AreEqual("PC-2", view.PeerName);

        // Both devices follow each other: two-way (§8.1).
        h.Store.Edit(2, l => l.CounterpartJson = "{\"mode\":\"follow\",\"firstContactCompleted\":true,\"kinds\":[]}");
        view = (await h.CallAsync(Callers.Loopback, c => c.GetInboxItem(onFollow.Id, default))).Data!;
        CollectionAssert.Contains(view.AllowedActions.ToArray(), DataSyncInboxAction.RestoreEverywhere);

        h.Store.CloseWhere(i => i.Id == onFollow.Id, DataSyncInboxClosure.ResolvedHere);
        view = (await h.CallAsync(Callers.Loopback, c => c.GetInboxItem(onFollow.Id, default))).Data!;
        Assert.AreEqual(0, view.AllowedActions.Count);
        Assert.AreEqual(DateTimeKind.Utc, view.ClosedAt!.Value.Kind);
    }

    [TestMethod]
    public async Task A_type_change_is_previewed_with_this_devices_values_and_nothing_else_is()
    {
        await using var h = await WorldAsync();
        var change = h.AddItem(DataSyncInboxItemType.TypeChange, 1, Key(7), "",
            Payload(remoteSubtype: "SingleChoice", localSubtype: "MultipleChoice"), localKey: "30");

        var preview = (await h.CallAsync(Callers.Loopback, c => c.PreviewInboxItem(change.Id, default))).Data!;
        Assert.AreEqual(210, preview.LossyCount);
        Assert.AreEqual(("30", "SingleChoice"), h.CustomProperties.Previewed.Single());
        Assert.IsNull((await h.CallAsync(Callers.Loopback, c => c.PreviewInboxItem(1, default))).Data);
        Assert.IsNull((await h.CallAsync(Callers.Loopback, c => c.PreviewInboxItem(404, default))).Data);
    }

    // ---- links, overview, readers, entities --------------------------------------------------------------------

    [TestMethod]
    public async Task The_overview_and_the_link_views_say_what_this_device_knows()
    {
        await using var h = await WorldAsync();
        h.HostKind.IsHeadless = true;
        h.Reviews.Stage(1, false, new DataSyncStagedPull("node-nas", "NAS",
            new DataSyncFeedManifest("snap", 1, "node-nas", "epoch-1", "0123456789abcdef", 1, 1, "2.4.0", [], null,
                new DataSyncSourceAttention(false, 0, 0, false, 0)), [], h.Clock.UtcNow));

        var overview = (await h.CallAsync(Callers.Paired, c => c.GetOverview(default))).Data!;
        Assert.AreEqual("This PC", overview.DeviceName);
        Assert.AreEqual("node-self", overview.NodeId);
        Assert.IsTrue(overview.IsHeadless);
        Assert.IsTrue(overview.CanManageSharing);
        Assert.AreEqual(2, overview.OpenInboxItems);
        Assert.AreEqual(1, overview.PendingRequests, "an expired request no longer waits");
        Assert.AreEqual(1, overview.Kinds.Single(k => k.Kind == DataSyncKindIds.CustomProperty).Count);
        Assert.AreEqual(DataSyncStatusLevel.NeedsYou, overview.Status.Level);
        Assert.AreEqual(2, overview.Status.Links);

        var nas = (await h.CallAsync(Callers.Loopback, c => c.GetLinks(default))).Data!.Single(l => l.Id == 1);
        Assert.AreEqual(1, nas.OpenItems);
        Assert.AreEqual(1, nas.ExcludedCount);
        Assert.IsTrue(nas.PeerMayReadUs);
        Assert.AreEqual("twoWay", nas.PeerModeTowardsUs);
        Assert.AreEqual("review-1", nas.ReviewId);
        Assert.IsFalse(nas.PeerOnline, "no head answered in this process");

        var readers = (await h.CallAsync(Callers.Loopback, c => c.GetReaders(default))).Data!;
        Assert.AreEqual("NAS", readers.Single().Name);
    }

    [TestMethod]
    public async Task Sync_now_answers_the_fetch_task_or_that_the_link_is_unknown()
    {
        await using var h = await WorldAsync();
        Assert.AreEqual(DataSyncTaskIds.Fetch, (await h.CallAsync(Callers.Loopback,
            c => c.SyncNow(new DataSyncSyncNowInputModel { LinkId = 1 }, default))).Data!.TaskId);
        Assert.AreEqual(DataSyncProblemCode.LinkNotFound, (await h.CallAsync(Callers.Loopback,
            c => c.SyncNow(new DataSyncSyncNowInputModel { LinkId = 99 }, default))).Data!.Problem?.Code);
    }

    [TestMethod]
    public async Task A_definition_this_device_follows_says_when_it_differs_from_the_source()
    {
        await using var h = await WorldAsync();
        var record = new DataSyncWireRecord([Key(9)], "node-pc", 4, DataSyncVersionVector.Empty, null, false, 1, null,
            new System.Text.Json.Nodes.JsonObject { ["name"] = "Mood" }, null, null, 0);
        var sameHash = Bakabase.Modules.DataSync.Canonical.ContentHash.Of(record.Content);
        h.AddEntity("40", Key(9), configure: e => e.SharedHash = sameHash);
        h.AddEntity("41", Key(10), configure: e => e.SharedHash = "sha256:changed-here");
        h.Store.Bases[(2, DataSyncKindIds.CustomProperty)] =
        [
            new DataSyncPeerBase(DataSyncKindIds.CustomProperty, new SyncKey(Key(9)), DataSyncBaseState.Normal, null,
                null, new Dictionary<string, string>(), null, record, []),
            new DataSyncPeerBase(DataSyncKindIds.CustomProperty, new SyncKey(Key(10)), DataSyncBaseState.Normal, null,
                null, new Dictionary<string, string>(), null, record with { Keys = [Key(10)] }, []),
        ];

        var entities = (await h.CallAsync(Callers.Loopback, c => c.GetEntities(DataSyncKindIds.CustomProperty, default)))
            .Data!;
        Assert.IsFalse(entities.Single(e => e.LocalKey == "40").DiffersFromSource);
        Assert.IsTrue(entities.Single(e => e.LocalKey == "41").DiffersFromSource);
        Assert.AreEqual(1, entities.Single(e => e.LocalKey == "12").OpenItems);
        Assert.AreEqual(0, (await h.CallAsync(Callers.Loopback, c => c.GetEntities("unknownKind", default))).Data!.Count);
    }

    // ---- entity settings (§6.6) --------------------------------------------------------------------------------

    [TestMethod]
    public async Task An_entity_setting_applies_at_once_under_the_gate_and_is_undoable()
    {
        await using var h = await WorldAsync();
        h.Refresher.ActorChangesLeft = 1;
        h.CustomProperties.Contents["12"] = new System.Text.Json.Nodes.JsonObject { ["name"] = "Genre" };

        var start = (await h.CallAsync(Callers.Loopback, c => c.SetEntitySync(DataSyncKindIds.CustomProperty, "12",
            new DataSyncEntitySyncInput(DataSyncEntitySyncState.Detached, true, ["opt-1"], null), default))).Data!;

        Assert.IsNull(start.Problem);
        Assert.IsNull(start.TaskId, "no task is started (N15)");
        var entity = h.Store.Entities.Single(e => e.LocalKey == "12");
        Assert.AreEqual(DataSyncEntitySyncState.Detached, entity.State);
        Assert.IsTrue(entity.ChildrenLocal);
        StringAssert.Contains(entity.OverlayJson, "opt-1");
        Assert.IsNotNull(h.Store.Item(1).ClosedAtUtc, "stopping to sync it closes its items");
        Assert.AreEqual(DataSyncInboxClosure.Superseded, h.Store.Item(1).Closure);

        // Refresh ran under the gate; the actor changed once under it, so it was checked and run again (§5.6).
        Assert.AreEqual(2, h.Refresher.Calls.Count);
        Assert.IsTrue(h.Refresher.Calls.All(c => c.LeaseHeld));
        var log = h.Store.History.Single(l => l.Kind == DataSyncHistoryKind.EntitySetting);
        StringAssert.Contains(log.PreImageJson, "\"entitySettings\"");
        Assert.AreEqual("Genre", DataSyncHistoryJson.ReadItems(log.ResultJson).Single().Name);

        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid, (await h.CallAsync(Callers.Loopback,
            c => c.SetEntitySync(DataSyncKindIds.ExtensionGroup, "3", new DataSyncEntitySyncInput(null, true, null, null),
                default))).Data!.Problem?.Code, "extension groups have no \"definition only\"");
        Assert.AreEqual(DataSyncProblemCode.UnknownKind, (await h.CallAsync(Callers.Loopback,
            c => c.SetEntitySync("unknownKind", "3", new DataSyncEntitySyncInput(null, true, null, null), default)))
            .Data!.Problem?.Code);
        Assert.AreEqual(DataSyncProblemCode.UnknownItem, (await h.CallAsync(Callers.Loopback,
            c => c.SetEntitySync(DataSyncKindIds.CustomProperty, "404",
                new DataSyncEntitySyncInput(DataSyncEntitySyncState.LocalOnly, null, null, null), default))).Data!.Problem?.Code);
    }

    // ---- history and undo --------------------------------------------------------------------------------------

    [TestMethod]
    public async Task Undo_is_checked_and_enqueued_once()
    {
        await using var h = await WorldAsync();
        h.Store.History.Add(new DataSyncApplyLogDbModel
        {
            Id = 2, Kind = DataSyncHistoryKind.Undo, AppliedAtUtc = h.Clock.UtcNow, SummaryJson = "{}",
            ResultJson = "[]", PreImageJson = "{}", UndoOfLogId = 1,
        });

        var entries = (await h.CallAsync(Callers.Loopback, c => c.GetHistory(default))).Data!;
        Assert.AreEqual(DataSyncUndoState.Available, entries.Single(e => e.Id == 1).UndoState);
        Assert.AreEqual(2, entries.Single(e => e.Id == 1).Counts.Updated);
        Assert.AreEqual(DataSyncUndoState.Expired, entries.Single(e => e.Id == 2).UndoState, "there is no redo");

        Assert.AreEqual(DataSyncProblemCode.UndoNotAvailable, (await h.CallAsync(Callers.Loopback,
            c => c.Undo(2, default))).Data!.Problem?.Code);
        Assert.AreEqual(DataSyncProblemCode.UndoNotAvailable, (await h.CallAsync(Callers.Loopback,
            c => c.PreviewUndo(404, default))).Data!.Problem?.Code);

        h.UndoPreviewer.Preview = new DataSyncUndoPreview(false, [],
            new DataSyncProblem(DataSyncProblemCode.UndoNotAvailable, "inUse"));
        Assert.AreEqual("inUse", (await h.CallAsync(Callers.Loopback, c => c.Undo(1, default))).Data!.Problem?.Detail);

        h.UndoPreviewer.Preview = new DataSyncUndoPreview(true, [], null);
        Assert.AreEqual(DataSyncTaskIds.Undo(1), (await h.CallAsync(Callers.Loopback, c => c.Undo(1, default))).Data!.TaskId);
        Assert.AreEqual(DataSyncProblemCode.ApplyInProgress,
            (await h.CallAsync(Callers.Loopback, c => c.Undo(1, default))).Data!.Problem?.Code, "one at a time");
    }

    // ---- restore (§9.5) ----------------------------------------------------------------------------------------

    [TestMethod]
    public async Task A_suspected_restore_is_chosen_for_its_link_and_a_detected_one_for_every_link()
    {
        await using var h = await WorldAsync();
        await h.Btm.Initialize();

        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid, (await h.CallAsync(Callers.Loopback,
            c => c.ChooseRestore(new DataSyncRestoreChoiceInputModel { Choice = DataSyncRestoreChoice.ThisDeviceWins },
                default))).Data!.Problem?.Code, "nothing to choose");

        h.Store.LocalState = h.Store.LocalState! with
        {
            RestoreReason = DataSyncPauseReason.LocalRestoreSuspected, RestoreLinkId = 2,
            RestoreDetectedAtUtc = DateTime.SpecifyKind(h.Clock.UtcNow, DateTimeKind.Unspecified),
            RestoreEvidenceJson = "[{\"source\":\"peer\",\"nodeId\":\"node-pc\",\"counter\":9}]",
        };
        h.Store.Edit(2, l =>
        {
            l.State = DataSyncLinkState.Paused;
            l.PausedReason = DataSyncPauseReason.LocalRestoreSuspected;
        });

        var view = (await h.CallAsync(Callers.Loopback, c => c.GetRestore(default))).Data!;
        Assert.IsTrue(view.Pending);
        Assert.AreEqual(2, view.LinkId);
        Assert.AreEqual(1, view.PausedLinks);
        Assert.AreEqual("PC-2", view.EvidenceFromName);
        Assert.AreEqual(DateTimeKind.Utc, view.DetectedAt!.Value.Kind);

        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid, (await h.CallAsync(Callers.Loopback,
            c => c.ChooseRestore(new DataSyncRestoreChoiceInputModel
                { Choice = DataSyncRestoreChoice.OthersWin, LinkId = 1 }, default))).Data!.Problem?.Code);
        var start = (await h.CallAsync(Callers.Loopback, c => c.ChooseRestore(new DataSyncRestoreChoiceInputModel
            { Choice = DataSyncRestoreChoice.OthersWin }, default))).Data!;
        Assert.AreEqual(DataSyncTaskIds.Restore, start.TaskId);
        await DataSyncRuntimeHarness.WaitUntilAsync(() => h.Runner.Restores.Count == 1, "the restore ran");
        Assert.AreEqual((DataSyncRestoreChoice.OthersWin, (int?) 2), h.Runner.Restores.Single());

        h.Store.LocalState = h.Store.LocalState! with
            { RestoreReason = DataSyncPauseReason.LocalRestoreDetected, RestoreLinkId = null };
        await DataSyncRuntimeHarness.WaitUntilAsync(() => h.Btm.GetTaskViewModel(DataSyncTaskIds.Restore)?.Status ==
                                                          BTaskStatus.Completed, "the first choice finished");
        Assert.AreEqual(DataSyncTaskIds.Restore, (await h.CallAsync(Callers.Loopback, c => c.ChooseRestore(
            new DataSyncRestoreChoiceInputModel { Choice = DataSyncRestoreChoice.ThisDeviceWins }, default))).Data!.TaskId,
            "a second choice in one process runs too");
        await DataSyncRuntimeHarness.WaitUntilAsync(() => h.Runner.Restores.Count == 2, "the second restore ran");
        Assert.AreEqual((DataSyncRestoreChoice.ThisDeviceWins, (int?) null), h.Runner.Restores.Last());
    }

    // ---- times ---------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task Every_time_in_every_answer_is_utc()
    {
        await using var h = await WorldAsync();
        h.Store.Edit(1, l => l.CounterpartJson = "{\"mode\":\"twoWay\",\"firstContactCompleted\":true,\"kinds\":[]}");
        var answers = new List<(string, object?)>();
        foreach (var (name, call) in Reads()) answers.Add((name, await h.CallAsync(Callers.Loopback, call)));
        answers.Add(("invitation", (await h.CallAsync(Callers.Loopback,
            c => c.CreateInvitation(new DataSyncInvitationInput(true), default))).Data));
        answers.Add(("approval", (await h.CallAsync(Callers.Loopback,
            c => c.ApproveRequest("req-in-1", new DataSyncApproveInput(true, null), default))).Data));

        var times = 0;
        foreach (var (name, answer) in answers)
        {
            foreach (var (path, time) in Times(answer, name))
            {
                times++;
                Assert.AreEqual(DateTimeKind.Utc, time.Kind, path);
            }
        }

        Assert.IsTrue(times > 15, $"only {times} times were looked at");
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private static DataSyncResolveInput Resolution(DataSyncInboxItemDbModel item, DataSyncInboxAction action) =>
        new(item.Id, action, item.Token, null, null, null, null);

    private static DataSyncProblem? ProblemOf(object? data) => data switch
    {
        DataSyncProblem problem => problem,
        null => null,
        _ => data.GetType().GetProperty("Problem")?.GetValue(data) as DataSyncProblem,
    };

    /// <summary>Every <see cref="DateTime"/> reachable from an answer, with where it was found.</summary>
    private static IEnumerable<(string Path, DateTime Time)> Times(object? value, string path, int depth = 0)
    {
        if (value is null || depth > 12) yield break;
        switch (value)
        {
            case DateTime time:
                yield return (path, time);
                yield break;
            case string or Enum or System.Text.Json.Nodes.JsonNode:
                yield break;
            case IEnumerable items:
            {
                var index = 0;
                foreach (var item in items)
                {
                    foreach (var found in Times(item, $"{path}[{index++}]", depth + 1)) yield return found;
                }

                yield break;
            }
        }

        var type = value.GetType();
        if (type.IsPrimitive || type == typeof(decimal) || type.Namespace?.StartsWith("Bakabase") != true) yield break;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0) continue;
            foreach (var found in Times(property.GetValue(value), $"{path}.{property.Name}", depth + 1))
                yield return found;
        }
    }
}
