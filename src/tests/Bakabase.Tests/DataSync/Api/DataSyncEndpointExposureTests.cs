using System.Reflection;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bakabase.Service.Models.Input.DataSync;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync.Api;

/// <summary>
/// Who reaches <c>/data-sync</c>, and who may create definitions access through it (spec §7.1.5, §10.1).
/// </summary>
/// <remarks>
/// The remote-access gate decides who reaches the endpoints at all: this device's own window, a paired device, and an
/// unpaired LAN browser only in Unrestricted mode. That last caller may read and shut access off, but never grant it:
/// the controller refuses it the actions that always create or widen access before the service sees the request, and
/// tells the service who is asking where only the service knows whether a call sends a request or mints a reciprocal
/// code (links, copy once).
/// </remarks>
[TestClass]
public class DataSyncEndpointExposureTests
{
    /// <summary>The §10.1 table, written out: adding a route is a decision about who may call it.</summary>
    private static readonly string[] Table =
    [
        "GET /data-sync/overview GetDataSyncOverview",
        "GET /data-sync/map GetDataSyncMap",
        "PUT /data-sync/sharing SetDataSyncSharing",
        "GET /data-sync/peers GetDataSyncPeers",
        "GET /data-sync/links GetDataSyncLinks",
        "POST /data-sync/links CreateDataSyncLink",
        "PUT /data-sync/links/{id:int} UpdateDataSyncLink",
        "POST /data-sync/links/{id:int}/pause PauseDataSyncLink",
        "POST /data-sync/links/{id:int}/resume ResumeDataSyncLink",
        "DELETE /data-sync/links/{id:int} ResetDataSyncLink",
        "POST /data-sync/sync-now SyncDataSyncNow",
        "PUT /data-sync/paused SetDataSyncAllPaused",
        "DELETE /data-sync/access/{nodeId} ForgetDataSyncAccess",
        "POST /data-sync/copy-once CreateDataSyncCopyOnce",
        "GET /data-sync/reviews/{reviewId} GetDataSyncReview",
        "POST /data-sync/reviews/{reviewId}/refetch RefetchDataSyncReview",
        "GET /data-sync/reviews/{reviewId}/changes GetDataSyncReviewChanges",
        "POST /data-sync/reviews/{reviewId}/apply ApplyDataSyncReview",
        "DELETE /data-sync/reviews/{reviewId}/apply CancelDataSyncReviewApply",
        "DELETE /data-sync/reviews/{reviewId} DiscardDataSyncReview",
        "GET /data-sync/requests GetDataSyncRequests",
        "POST /data-sync/requests/{id}/approve ApproveDataSyncRequest",
        "POST /data-sync/requests/{id}/reject RejectDataSyncRequest",
        "DELETE /data-sync/requests/{id} CancelDataSyncRequest",
        "GET /data-sync/readers GetDataSyncReaders",
        "DELETE /data-sync/readers/{nodeId} RevokeDataSyncReader",
        "POST /data-sync/invitations CreateDataSyncInvitation",
        "GET /data-sync/inbox GetDataSyncInbox",
        "GET /data-sync/inbox/{id:long} GetDataSyncInboxItem",
        "GET /data-sync/inbox/{id:long}/preview PreviewDataSyncInboxItem",
        "POST /data-sync/inbox/resolve ResolveDataSyncInbox",
        "GET /data-sync/entities GetDataSyncEntities",
        "PUT /data-sync/entities/{kind}/{localKey} SetDataSyncEntitySync",
        "GET /data-sync/history GetDataSyncHistory",
        "GET /data-sync/history/{id:int} GetDataSyncHistoryEntry",
        "GET /data-sync/history/{id:int}/undo-preview PreviewDataSyncUndo",
        "POST /data-sync/history/{id:int}/undo UndoDataSync",
        "GET /data-sync/restore GetDataSyncRestore",
        "POST /data-sync/restore ChooseDataSyncRestore",
        "DELETE /data-sync/tasks/{taskId} CancelDataSyncTask",
    ];

    private static readonly string[] AllKinds = [..DataSyncKindIds.All];

    private sealed record Route(string Method, string Path, string? OperationId, MethodInfo Action)
    {
        public override string ToString() => $"{Method} {Path} {OperationId}";
    }

    private static IReadOnlyList<Route> Routes()
    {
        var prefix = typeof(DataSyncController).GetCustomAttribute<RouteAttribute>()!.Template.TrimStart('~')
            .Trim('/');
        return typeof(DataSyncController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(action => action.GetCustomAttributes<HttpMethodAttribute>()
                .SelectMany(http => http.HttpMethods.Select(method =>
                {
                    var template = http.Template?.Trim('/') ?? string.Empty;
                    return new Route(method, template.Length == 0 ? $"/{prefix}" : $"/{prefix}/{template}",
                        action.GetCustomAttribute<Swashbuckle.AspNetCore.Annotations.SwaggerOperationAttribute>()
                            ?.OperationId, action);
                })))
            .ToList();
    }

    // ---- exposure ------------------------------------------------------------------------------------------------

    [TestMethod]
    public void The_routes_are_exactly_the_table()
    {
        var routes = Routes().Select(r => r.ToString()).ToArray();
        CollectionAssert.AreEquivalent(Table, routes,
            $"routes: {string.Join(Environment.NewLine, routes.Except(Table).Concat(Table.Except(routes)))}");

        var publicActions = typeof(DataSyncController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.AreEqual(Table.Length, publicActions.Length, "every public method is exactly one routed action");
    }

    [TestMethod]
    public void No_action_is_remote_accessible_or_runs_on_the_user_machine()
    {
        // Management-level: a paired device or an Unrestricted browser gets through the gate by who it is, never by
        // a marker. Nothing here does anything on the machine the server runs on.
        Assert.IsNull(typeof(DataSyncController).GetCustomAttribute<RemoteAccessibleAttribute>());
        Assert.IsNull(typeof(DataSyncController).GetCustomAttribute<RunsOnUserMachineAttribute>());
        foreach (var route in Routes())
        {
            Assert.IsNull(route.Action.GetCustomAttribute<RemoteAccessibleAttribute>(), route.ToString());
            Assert.IsNull(route.Action.GetCustomAttribute<RunsOnUserMachineAttribute>(), route.ToString());
        }
    }

    [TestMethod]
    public void Nothing_is_under_federation()
    {
        // Outside /federation, a node-signed request never reaches these, and none of them is a protocol route.
        foreach (var route in Routes())
        {
            StringAssert.StartsWith(route.Path, "/data-sync/", route.ToString());
            Assert.IsFalse(route.Path.Contains("federation", StringComparison.OrdinalIgnoreCase), route.ToString());
        }
    }

    [TestMethod]
    public void The_management_plane_reaches_nothing_but_the_data_sync_facade()
    {
        // Library grants live behind the loopback-only /federation/local API; the only thing this controller can
        // call is the data sync facade, which has no library grant in its vocabulary.
        var parameters = typeof(DataSyncController).GetConstructors().Single().GetParameters();
        CollectionAssert.AreEqual(new[] {typeof(IDataSyncService)}, parameters.Select(p => p.ParameterType).ToArray());
    }

    [TestMethod]
    public void The_gate_admits_this_device_a_paired_device_and_an_unrestricted_browser_only()
    {
        foreach (var route in Routes())
        {
            Assert.IsNull(Authorize(route, Loopback).Result, $"loopback: {route}");
            Assert.IsNull(Authorize(route, Paired).Result, $"paired: {route}");
            Assert.IsNull(Authorize(route, UnpairedUnrestricted).Result, $"unrestricted: {route}");

            foreach (var refused in new[] {Remote(RemoteAccessMode.Enabled), null})
            {
                var context = Authorize(route, refused);
                Assert.IsInstanceOfType<ObjectResult>(context.Result, route.ToString());
                Assert.AreEqual(403, ((ObjectResult) context.Result!).StatusCode, route.ToString());
                Assert.AreEqual(nameof(RemoteAccessDenialReason.HostOnly),
                    context.HttpContext.Response.Headers["X-Bakabase-Remote-Access"].ToString(), route.ToString());
            }
        }
    }

    // ---- every row calls the facade ------------------------------------------------------------------------------

    [TestMethod]
    public async Task Every_facade_member_is_reached_by_its_action()
    {
        var fake = new FakeDataSyncService();
        var controller = Controller(fake, Loopback);

        await controller.GetOverview(default);
        await controller.GetMap(default);
        await controller.SetSharing(new DataSyncSharingInput(true), default);
        await controller.GetPeers(true, default);
        await controller.GetLinks(default);
        await controller.CreateLink(new DataSyncLinkCreateInput("node-nas", null, null, DataSyncLinkMode.Follow, AllKinds),
            default);
        await controller.UpdateLink(1, new DataSyncLinkUpdateInput(DataSyncLinkMode.TwoWay, null), default);
        await controller.PauseLink(1, default);
        await controller.ResumeLink(5, new DataSyncLinkResumeInputModel(), default);
        await controller.ResetLink(6, default);
        await controller.SyncNow(new DataSyncSyncNowInputModel(), default);
        await controller.SetAllPaused(new DataSyncPausedInputModel {Paused = true}, default);
        await controller.ForgetAccess("node-nas", default);
        await controller.CreateCopyOnce(new DataSyncCopyOnceInput("node-nas", null, null, AllKinds), default);
        await controller.GetReview(FakeDataSyncService.ReviewId, default);
        await controller.RefetchReview(FakeDataSyncService.ReviewId, default);
        await controller.GetReviewChanges(FakeDataSyncService.ReviewId, FakeDataSyncService.PlanId,
            "customProperty/k/a0000000000000000000000000000002", null, 0, 100, default);
        await controller.ApplyReview(FakeDataSyncService.ReviewId, new DataSyncReviewApplyInput([], true), default);
        await controller.CancelReviewApply(FakeDataSyncService.ReviewId, default);
        await controller.DiscardReview(FakeDataSyncService.ReviewId, default);
        await controller.GetRequests(default);
        await controller.ApproveRequest("req-in-1", new DataSyncApproveInput(true, null), default);
        await controller.RejectRequest("req-in-1", default);
        await controller.CancelRequest("req-out-1", default);
        await controller.GetReaders(default);
        await controller.RevokeReader("node-nas", default);
        await controller.CreateInvitation(new DataSyncInvitationInput(true), default);
        await controller.GetInbox(ct: default);
        await controller.GetInboxItem(1, default);
        await controller.PreviewInboxItem(FakeDataSyncService.TypeChangeItemId, default);
        await controller.Resolve(new DataSyncResolveBatchInput(
            [new DataSyncResolveInput(1, DataSyncInboxAction.KeepLocal, "token", null, null, null, null)], false), default);
        await controller.GetEntities(DataSyncKindIds.CustomProperty, default);
        await controller.SetEntitySync(DataSyncKindIds.CustomProperty, "12",
            new DataSyncEntitySyncInput(DataSyncEntitySyncState.LocalOnly, null, null, null), default);
        await controller.GetHistory(default);
        await controller.GetHistoryEntry(1, default);
        await controller.PreviewUndo(1, default);
        await controller.Undo(1, default);
        await controller.GetRestore(default);
        await controller.ChooseRestore(new DataSyncRestoreChoiceInputModel {Choice = DataSyncRestoreChoice.ThisDeviceWins},
            default);
        await controller.CancelTask("DataSyncApply:review-1", default);

        var members = typeof(IDataSyncService).GetMethods().Select(m => m.Name).ToArray();
        CollectionAssert.AreEquivalent(members, fake.Calls.ToArray(),
            $"missing: {string.Join(", ", members.Except(fake.Calls))}");
    }

    [TestMethod]
    public async Task Parameters_reach_the_facade_as_sent()
    {
        var fake = new FakeDataSyncService();
        var controller = Controller(fake, Loopback);

        var inbox = (await controller.GetInbox(false, "node-nas", DataSyncKindIds.CustomProperty, 0, 5, default)).Data!;
        Assert.AreEqual(5, inbox.Items.Count);
        Assert.IsTrue(inbox.Items.All(i => i.PeerNodeId == "node-nas" && i.Kind == DataSyncKindIds.CustomProperty));

        // A change page is at most 500 rows, whatever is asked for.
        var page = (await controller.GetReviewChanges(FakeDataSyncService.ReviewId, FakeDataSyncService.PlanId,
            "customProperty/k/a0000000000000000000000000000002", null, -3, 10_000, default)).Data!;
        Assert.AreEqual(240, page.Changes.Count);
        Assert.AreEqual("add:0", page.Changes[0].ChangeId);

        Assert.IsNotNull((await controller.PreviewInboxItem(FakeDataSyncService.TypeChangeItemId, default)).Data);
        Assert.IsNull((await controller.PreviewInboxItem(1, default)).Data);
    }

    // ---- the access rule (§7.1.5) --------------------------------------------------------------------------------

    /// <summary>
    /// Every call that creates or widens definitions access, the problem it answered, and whether only the service
    /// can tell (it sends a request or mints a reciprocal code) or the action always does.
    /// </summary>
    private static IEnumerable<(string Name, bool ServiceDecides, Func<DataSyncController, Task<DataSyncProblem?>> Call)>
        CreatingAccess() =>
    [
        ("PUT sharing {enabled:true}", false,
            async c => (await c.SetSharing(new DataSyncSharingInput(true, true), default)).Data),
        ("POST links (two-way, to a device this one cannot read yet)", true,
            async c => (await c.CreateLink(
                    new DataSyncLinkCreateInput("node-newpc", null, null, DataSyncLinkMode.TwoWay, AllKinds), default))
                .Data!.Problem),
        ("POST links (follow, by address and code)", true,
            async c => (await c.CreateLink(
                    new DataSyncLinkCreateInput(null, "192.168.1.40:34567", "48213705", DataSyncLinkMode.Follow, AllKinds),
                    default)).Data!
                .Problem),
        ("PUT links/{id} to two-way, the peer not reading this device yet", true,
            async c => (await c.UpdateLink(5, new DataSyncLinkUpdateInput(DataSyncLinkMode.TwoWay, null), default))
                .Data!.Problem),
        ("POST links/{id}/resume asking for access again", false,
            async c => (await c.ResumeLink(9,
                new DataSyncLinkResumeInputModel {Action = DataSyncResumeAction.AskAccessAgain}, default)).Data!.Problem),
        ("POST copy-once (by address and code)", true,
            async c => (await c.CreateCopyOnce(
                new DataSyncCopyOnceInput(null, "192.168.1.40:34567", "48213705", AllKinds), default)).Data!.Problem),
        ("POST copy-once (from a device this one cannot read yet)", true,
            async c => (await c.CreateCopyOnce(
                new DataSyncCopyOnceInput("node-newpc", null, null, AllKinds), default)).Data!.Problem),
        ("POST requests/{id}/approve", false,
            async c => (await c.ApproveRequest("req-in-1", new DataSyncApproveInput(true, null), default)).Data!
                .Problem),
        ("POST invitations", false,
            async c => (await c.CreateInvitation(new DataSyncInvitationInput(true), default)).Data!.Problem),
    ];

    /// <summary>A stopped two-way link whose peer still reads this device: turning it back on asks nobody.</summary>
    private const int StoppedTwoWayLinkId = 30;

    /// <summary>
    /// Link and copy-once calls that send no request and mint no code: they create no access, so the gate's word is
    /// enough (§7.1.5, §8.1).
    /// </summary>
    private static IEnumerable<(string Name, Func<DataSyncController, Task<DataSyncProblem?>> Call)> CreatingNoAccess() =>
    [
        ("POST links (follow, a device this one already reads)",
            async c => (await c.CreateLink(
                new DataSyncLinkCreateInput("node-nas", null, null, DataSyncLinkMode.Follow, AllKinds), default)).Data!
                .Problem),
        ("POST links (two-way, a device that already reads this one)",
            async c => (await c.CreateLink(
                new DataSyncLinkCreateInput("node-nas", null, null, DataSyncLinkMode.TwoWay, AllKinds), default)).Data!
                .Problem),
        ("PUT links/{id} kinds of a two-way link, the mode sent along",
            async c => (await c.UpdateLink(1,
                new DataSyncLinkUpdateInput(DataSyncLinkMode.TwoWay, [DataSyncKindIds.CustomProperty]), default)).Data!
                .Problem),
        ("PUT links/{id} kinds only",
            async c => (await c.UpdateLink(5, new DataSyncLinkUpdateInput(null, [DataSyncKindIds.CustomProperty]),
                default)).Data!.Problem),
        ("PUT links/{id} a stopped two-way link back on",
            async c => (await c.UpdateLink(StoppedTwoWayLinkId,
                new DataSyncLinkUpdateInput(DataSyncLinkMode.TwoWay, null), default)).Data!.Problem),
        ("PUT links/{id} a stopped follow link back on",
            async c => (await c.UpdateLink(6, new DataSyncLinkUpdateInput(DataSyncLinkMode.Follow, null), default))
                .Data!.Problem),
        ("POST copy-once (from a device this one already reads)",
            async c => (await c.CreateCopyOnce(new DataSyncCopyOnceInput("node-nas", null, null, AllKinds), default))
                .Data!.Problem),
    ];

    /// <summary>Every call that reduces access or only reads; open to whoever the gate admits.</summary>
    private static IEnumerable<(string Name, Func<DataSyncController, Task<DataSyncProblem?>> Call)> ReducingAccess() =>
    [
        ("PUT sharing {enabled:false}", async c => (await c.SetSharing(new DataSyncSharingInput(false), default)).Data),
        ("POST requests/{id}/reject", async c => (await c.RejectRequest("req-in-1", default)).Data),
        ("DELETE requests/{id}", async c => (await c.CancelRequest("req-out-1", default)).Data),
        ("DELETE readers/{nodeId}", async c => (await c.RevokeReader("node-nas", default)).Data),
        ("DELETE access/{nodeId}", async c => (await c.ForgetAccess("node-nas", default)).Data),
        ("DELETE links/{id}", async c => (await c.ResetLink(6, default)).Data),
        ("POST links/{id}/pause", async c => (await c.PauseLink(1, default)).Data!.Problem),
        ("PUT links/{id} off",
            async c => (await c.UpdateLink(1, new DataSyncLinkUpdateInput(DataSyncLinkMode.Off, null), default)).Data!
                .Problem),
        ("PUT links/{id} to follow",
            async c => (await c.UpdateLink(1, new DataSyncLinkUpdateInput(DataSyncLinkMode.Follow, null), default))
                .Data!.Problem),
        ("POST links/{id}/resume", async c => (await c.ResumeLink(5, new DataSyncLinkResumeInputModel(), default)).Data!
            .Problem),
        ("PUT paused", async c => (await c.SetAllPaused(new DataSyncPausedInputModel {Paused = true}, default)).Data),
    ];

    [TestMethod]
    public async Task An_unpaired_unrestricted_browser_may_not_create_access()
    {
        foreach (var context in new[] {UnpairedUnrestricted, null})
        {
            foreach (var (name, serviceDecides, call) in CreatingAccess())
            {
                var fake = new FakeDataSyncService();
                var problem = await call(Controller(fake, context));

                Assert.AreEqual(DataSyncProblemCode.NotAllowedOnThisDevice, problem?.Code, name);
                // Where only the service can tell, it is asked, and told the caller may not create access; the fake
                // refuses on that word alone.
                Assert.AreEqual(serviceDecides ? 1 : 0, fake.Calls.Count,
                    $"{name} reached the service: {string.Join(", ", fake.Calls)}");
            }
        }
    }

    [TestMethod]
    public async Task What_creates_no_access_is_open_to_whoever_the_gate_admits()
    {
        foreach (var context in new[] {Loopback, Paired, UnpairedUnrestricted})
        {
            foreach (var (name, call) in CreatingNoAccess())
            {
                var fake = new FakeDataSyncService();
                fake.Links =
                [
                    ..fake.Links,
                    fake.Links.Single(l => l.Id == 1) with
                    {
                        Id = StoppedTwoWayLinkId, PeerNodeId = "node-den", PeerName = "Den PC",
                        State = DataSyncLinkState.Stopped, Mode = DataSyncLinkMode.Off
                    },
                ];
                var problem = await call(Controller(fake, context));

                Assert.IsNull(problem, $"{name}: {problem}");
                Assert.AreEqual(1, fake.Calls.Count, name);
            }
        }
    }

    [TestMethod]
    public async Task This_device_and_a_paired_device_may_create_access()
    {
        foreach (var context in new[] {Loopback, Paired})
        {
            foreach (var (name, _, call) in CreatingAccess())
            {
                var fake = new FakeDataSyncService();
                var problem = await call(Controller(fake, context));

                Assert.IsNull(problem, $"{name}: {problem}");
                Assert.AreEqual(1, fake.Calls.Count, name);
            }
        }
    }

    [TestMethod]
    public async Task Access_can_be_shut_off_from_wherever_the_gate_admits()
    {
        foreach (var context in new[] {Loopback, Paired, UnpairedUnrestricted})
        {
            foreach (var (name, call) in ReducingAccess())
            {
                var fake = new FakeDataSyncService();
                var problem = await call(Controller(fake, context));

                Assert.IsNull(problem, $"{name}: {problem}");
                Assert.AreEqual(1, fake.Calls.Count, name);
            }
        }
    }

    [TestMethod]
    public async Task The_overview_says_whether_this_caller_may_create_access()
    {
        var fake = new FakeDataSyncService {Overview = new FakeDataSyncService().Overview with {CanManageSharing = false}};
        Assert.IsTrue((await Controller(fake, Loopback).GetOverview(default)).Data!.CanManageSharing);
        Assert.IsTrue((await Controller(fake, Paired).GetOverview(default)).Data!.CanManageSharing);

        fake.Overview = fake.Overview with {CanManageSharing = true};
        Assert.IsFalse((await Controller(fake, UnpairedUnrestricted).GetOverview(default)).Data!.CanManageSharing);
        Assert.IsFalse((await Controller(fake, null).GetOverview(default)).Data!.CanManageSharing);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private static RemoteAccessContext Loopback => new() {IsLoopback = true, Mode = RemoteAccessMode.Disabled};

    private static RemoteAccessContext Remote(RemoteAccessMode mode, RemoteDevice? device = null) =>
        new() {IsLoopback = false, Mode = mode, Device = device};

    /// <summary>The desktop app's relay managing this server, or any other paired device.</summary>
    private static RemoteAccessContext Paired =>
        Remote(RemoteAccessMode.Enabled,
            new RemoteDevice {Id = "device-1", Name = "Desktop", Key = "key", CreatedAt = FakeDataSyncService.Now});

    /// <summary>A LAN browser admitted only because the mode is Unrestricted (a Docker install's default).</summary>
    private static RemoteAccessContext UnpairedUnrestricted => Remote(RemoteAccessMode.Unrestricted);

    private static DataSyncController Controller(IDataSyncService service, RemoteAccessContext? remote)
    {
        var http = new DefaultHttpContext();
        if (remote != null)
        {
            http.SetRemoteAccessContext(remote);
        }

        return new DataSyncController(service) {ControllerContext = new ControllerContext {HttpContext = http}};
    }

    private static AuthorizationFilterContext Authorize(Route route, RemoteAccessContext? remote)
    {
        var http = new DefaultHttpContext();
        if (remote != null)
        {
            http.SetRemoteAccessContext(remote);
        }

        var descriptor = new ControllerActionDescriptor
        {
            MethodInfo = route.Action,
            ControllerTypeInfo = typeof(DataSyncController).GetTypeInfo()
        };
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), descriptor),
            new List<IFilterMetadata>());
        new RemoteAccessAuthorizationFilter().OnAuthorization(context);
        return context;
    }
}
