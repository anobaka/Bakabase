using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bakabase.Tests.DataSync.Api;
using Bakabase.Tests.RemoteAccess.Service;
using Bakabase.TestKit.Implementations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation.Security;

/// <summary>
/// The gate split of §7.3: every row of the gate matrix (G1–G36, G22b, G31b), and the generated matrix over every Export
/// action × {library grant, datasync grant, none} × both switches. S is library sharing, D definitions sharing.
/// </summary>
public sealed partial class FederationGateTests
{
    private const string Info = "/federation/v1/info";
    private const string Head = "/federation/v1/export/datasync/head";
    private const string Manifest = "/federation/v1/export/datasync/manifest";
    private const string Changes = "/federation/v1/export/datasync/changes";
    private const string Handshake = "/federation/v1/export/handshake";
    private const string Queries = "/federation/v1/export/queries";

    // ---- G1–G5: info and pairing, before any grant ---------------------------------------------------------------

    [TestMethod]
    public async Task G01_InfoIsRefusedWithBothSwitchesOff()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.SwitchAsync(library: false, dataSync: false);
        var context = Context(Info, "GET");
        await gate.RunAsync(context, gate.InfoEndpoint);
        Assert.AreEqual((403, "SharingDisabled"), (context.Response.StatusCode, Error(context)));
        Assert.IsFalse(gate.ReachedEndpoint);
    }

    [TestMethod]
    public async Task G02_InfoAnswersWhenOnlyDefinitionsAreSharedAndSaysSo()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.SwitchAsync(library: false, dataSync: true);
        var context = Context(Info, "GET");
        await gate.RunAsync(context, gate.InfoEndpoint);
        Assert.AreEqual(200, context.Response.StatusCode);
        var info = Body<NodeInfo>(context);
        Assert.IsTrue(info.SharesDefinitions);
        Assert.AreEqual(DataSyncContract.Version, info.DataSyncContractVersion);
        Assert.AreEqual(DataSyncContract.MinimumPeerVersion, info.DataSyncMinimumPeerContract);
        Assert.IsNotNull(info.DataSyncKinds);

        await gate.SwitchAsync(library: true, dataSync: false);
        context = Context(Info, "GET");
        await gate.RunAsync(context, gate.InfoEndpoint);
        Assert.IsFalse(Body<NodeInfo>(context).SharesDefinitions);
    }

    [TestMethod]
    public async Task G03_LibraryPairingNeedsLibrarySharingEvenWithDefinitionsShared()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.SwitchAsync(library: false, dataSync: true);
        foreach (var step in new[] { "request", "code", "claim" })
        {
            var context = Context("/federation/v1/pair/" + step, "POST");
            await gate.RunAsync(context);
            Assert.AreEqual((403, "SharingDisabled"), (context.Response.StatusCode, Error(context)), step);
            Assert.IsFalse(gate.ReachedEndpoint);
        }
    }

    [TestMethod]
    public async Task G04_DefinitionsPairingNeedsDefinitionsSharingEvenWithTheLibraryShared()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.SwitchAsync(library: true, dataSync: false);
        foreach (var step in new[] { "request", "code", "claim" })
        {
            var context = Context("/federation/v1/pair/datasync/" + step, "POST");
            await gate.RunAsync(context);
            Assert.AreEqual((403, "DataSyncSharingDisabled"), (context.Response.StatusCode, Error(context)), step);
            Assert.IsFalse(gate.ReachedEndpoint);
        }
    }

    [TestMethod]
    public async Task G05_ADefinitionsRequestIsStoredAmongDefinitionsRequestsOnly()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.SwitchAsync(library: false, dataSync: true);
        var context = Context("/federation/v1/pair/datasync/request", "POST", "192.168.20.30");
        var request = new NodeDataSyncPairRequest("asker-node", "Asker", "asker-transaction",
            NodeRequestSignature.RandomToken(), NodeDataSyncIntents.TwoWay);
        await gate.RunAsync(context, Endpoint(http => gate.PairingController(http).PairRequest(request, default)));

        Assert.AreEqual(200, context.Response.StatusCode);
        Assert.AreEqual("awaitingApproval", Body<NodePairExchange>(context).Outcome);
        Assert.IsFalse((await gate.Peers.GetStatusAsync()).Requests.Any(r => r.NodeId == "asker-node"));
        var stored = (await gate.Peers.GetDataSyncStatusAsync()).Requests.Single(r => r.NodeId == "asker-node");
        Assert.AreEqual(("incoming", NodeDataSyncIntents.TwoWay, "192.168.20.30"),
            (stored.Direction, stored.Intent, stored.RemoteAddress));
        // A person at this device is told, and sent to where definitions requests are decided.
        var notification = gate.Notifications.Created.Single();
        Assert.AreEqual(("DataSync", AppNotificationSeverity.Warning), (notification.Source, notification.Severity));
        Assert.AreEqual("/data-sync?tab=requests",
            JsonDocument.Parse(notification.PayloadJson!).RootElement.GetProperty("route").GetString());
    }

    // ---- G6–G12: a grant reaches only the routes of its own scope, under its own switch ---------------------------

    [TestMethod]
    public async Task G06_ALibraryGrantCannotReadTheFeed()
    {
        using var gate = await ScopeGate.CreateAsync();
        var context = await gate.SendAsync(Head, "GET", gate.Library);
        Assert.AreEqual((403, "ScopeNotGranted"), (context.Response.StatusCode, Error(context)));
        Assert.IsFalse(gate.ReachedEndpoint);
    }

    [TestMethod]
    [DataRow(Queries, "POST")]
    [DataRow("/federation/v1/export/assets/asset-1", "GET")]
    [DataRow("/federation/v1/export/mapping-roots", "GET")]
    public async Task G07_G08_G09_ADataSyncGrantCannotReachTheLibrary(string path, string method)
    {
        using var gate = await ScopeGate.CreateAsync();
        var context = await gate.SendAsync(path, method, gate.DataSync);
        Assert.AreEqual((403, "ScopeNotGranted"), (context.Response.StatusCode, Error(context)));
        Assert.IsFalse(gate.ReachedEndpoint);
    }

    [TestMethod]
    public async Task G10_TheFeedIsClosedWithDefinitionsSharingOff()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.SwitchAsync(library: true, dataSync: false);
        var context = await gate.SendAsync(Manifest, "GET", gate.DataSync);
        Assert.AreEqual((403, "DataSyncSharingDisabled"), (context.Response.StatusCode, Error(context)));
        Assert.IsFalse(gate.ReachedEndpoint);
    }

    [TestMethod]
    public async Task G11_TheFeedAnswersADataSyncGrantWithTheLibraryNotShared()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.SwitchAsync(library: false, dataSync: true);
        var context = Context(Manifest, "GET");
        context.Request.QueryString = new QueryString("?mode=twoWay&since=customProperty:12,extensionGroup:0&state=ok");
        gate.Sign(context, gate.DataSync);
        await gate.RunAsync(context, Endpoint(http => gate.FeedController(http)
            .Manifest("twoWay", "customProperty:12,extensionGroup:0", null, "ok", http.RequestAborted)));

        Assert.AreEqual(200, context.Response.StatusCode);
        Assert.AreEqual("snapshot-1", Body<DataSyncFeedManifest>(context).SnapshotId);
        var (call, reader, query) = gate.Feed.Calls.Single();
        Assert.AreEqual(("manifest", "reader-node", gate.DataSync.GrantId, "Reader"),
            (call, reader.NodeId, reader.GrantId, reader.Name));
        Assert.AreEqual(("twoWay", 12L, 0L, "ok"),
            (query!.Mode, query.Since["customProperty"], query.Since["extensionGroup"], query.ReaderState));
    }

    [TestMethod]
    public async Task G12_ALibraryGrantStillReadsTheLibraryWithDefinitionsSharingOff()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.SwitchAsync(library: true, dataSync: false);
        var context = await gate.SendAsync(Queries, "POST", gate.Library);
        Assert.IsTrue(gate.ReachedEndpoint);
        Assert.AreEqual(FederationScopes.LibraryRead, FederationHttpContext.GetNodePrincipal(context)!.Scope);
    }

    // ---- G13–G15: the handshake and unsigned reads -----------------------------------------------------------------

    [TestMethod]
    public async Task G13_ADataSyncGrantHandshakesWithOnlyDefinitionsSharedAndIsToldItsScope()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.SwitchAsync(library: false, dataSync: true);
        var response = await gate.HandshakeAsync(gate.DataSync);
        Assert.AreEqual(200, response.Response.StatusCode);
        var proof = Body<NodeHandshakeResponse>(response);
        Assert.AreEqual(FederationScopes.DataSyncRead, proof.Scope);
        Assert.IsTrue(proof.Info.SharesDefinitions);
        Assert.IsTrue(NodeRequestSignature.FixedEquals(proof.Proof,
            NodeRequestSignature.HandshakeProof(gate.DataSync.Key, proof.Info, proof.Challenge)));
    }

    [TestMethod]
    public async Task G14_ALibraryGrantCannotHandshakeWithOnlyDefinitionsShared()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.SwitchAsync(library: false, dataSync: true);
        var response = await gate.HandshakeAsync(gate.Library);
        Assert.AreEqual((403, "SharingDisabled"), (response.Response.StatusCode, Error(response)));
        Assert.IsFalse(gate.ReachedEndpoint);
    }

    [TestMethod]
    public async Task G15_TheFeedNeedsASignature()
    {
        using var gate = await ScopeGate.CreateAsync();
        var context = await gate.SendAsync(Head, "GET", null);
        Assert.AreEqual((401, "NodeAuthenticationRequired"), (context.Response.StatusCode, Error(context)));
        Assert.IsFalse(gate.ReachedEndpoint);
    }

    // ---- G16–G18: remote access and routes outside the protocol ---------------------------------------------------

    [TestMethod]
    [DataRow(Head, "GET")]
    [DataRow(Handshake, "POST")]
    [DataRow(Info, "GET")]
    [DataRow("/federation/v1/pair/datasync/request", "POST")]
    public async Task G16_RemoteAccessDisabledClosesEveryNodeRoute(string path, string method)
    {
        using var gate = await ScopeGate.CreateAsync();
        gate.Remote.Mode = RemoteAccessMode.Disabled;
        var context = await gate.SendAsync(path, method, path.Contains("/export/") ? gate.DataSync : null);
        Assert.AreEqual((403, "RemoteAccessDisabled"), (context.Response.StatusCode, Error(context)));
        Assert.IsFalse(gate.ReachedEndpoint);
    }

    [TestMethod]
    [DataRow("/federation/local/peers", "GET")]
    [DataRow("/data-sync/overview", "GET")]
    [DataRow("/data-sync/sharing", "PUT")]
    public async Task G17_G18_ADataSyncSignatureNeverReachesAManagementRoute(string path, string method)
    {
        using var gate = await ScopeGate.CreateAsync();
        var context = await gate.SendAsync(path, method, gate.DataSync);
        Assert.AreEqual((403, "NodeRouteForbidden"), (context.Response.StatusCode, Error(context)));
        Assert.IsFalse(gate.ReachedEndpoint);
    }

    // ---- G19–G23: leases, revocation and identity changes ---------------------------------------------------------

    [TestMethod]
    public async Task G19_TurningLibrarySharingOffLeavesAnInflightFeedPageAlone()
    {
        using var gate = await ScopeGate.CreateAsync();
        gate.Feed.DuringPage = () => gate.Peers.SetSharingAsync(false);
        var context = Context(Changes, "GET");
        context.Request.QueryString = new QueryString("?snapshot=snapshot-1&kind=customProperty&since=0");
        gate.Sign(context, gate.DataSync);
        await gate.RunAsync(context, Endpoint(http => gate.FeedController(http)
            .Changes("snapshot-1", "customProperty", "0", null, http.RequestAborted)));

        Assert.AreEqual(200, context.Response.StatusCode);
        Assert.AreEqual(ScopeGate.Page, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
        Assert.IsFalse(gate.Leases.GetCancellationToken(gate.DataSync.GrantId).IsCancellationRequested);
        Assert.IsTrue(gate.Leases.GetCancellationToken(gate.Library.GrantId).IsCancellationRequested);
    }

    [TestMethod]
    public async Task G20_TurningDefinitionsSharingOffLeavesAnInflightLibraryPageAlone()
    {
        using var gate = await ScopeGate.CreateAsync();
        var context = Context("/federation/v1/export/queries/query-1/pages", "GET");
        gate.Sign(context, gate.Library);
        await gate.RunAsync(context, Endpoint(async http =>
        {
            await gate.Peers.SetDataSyncSharingAsync(false);
            http.RequestAborted.ThrowIfCancellationRequested();
            return new ContentResult { Content = "{\"page\":1}" };
        }));
        Assert.AreEqual(200, context.Response.StatusCode);
        Assert.IsTrue(gate.Leases.GetCancellationToken(gate.DataSync.GrantId).IsCancellationRequested);
    }

    [TestMethod]
    public async Task G21_RevokingAPeersLibraryGrantLeavesItsDataSyncGrant()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.Peers.RevokeAsync(gate.Library.GrantId);
        var context = await gate.SendAsync(Head, "GET", gate.DataSync);
        Assert.IsTrue(gate.ReachedEndpoint);
        Assert.AreEqual(FederationScopes.DataSyncRead, FederationHttpContext.GetNodePrincipal(context)!.Scope);
        var library = await gate.SendAsync(Queries, "POST", gate.Library);
        Assert.AreEqual((401, "GrantRevoked"), (library.Response.StatusCode, Error(library)));
    }

    /// <summary>G22: the switch is checked before authentication, so the old grant is not even looked at.</summary>
    [TestMethod]
    public async Task G22_AfterAnEpochRotationTheFeedIsClosedBeforeAnySignatureIsChecked()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.Peers.RotateLibraryEpochAsync();
        await gate.Peers.SetSharingAsync(true);
        var context = await gate.SendAsync(Head, "GET", gate.DataSync);
        Assert.AreEqual((403, "DataSyncSharingDisabled"), (context.Response.StatusCode, Error(context)));
        var garbage = Context(Head, "GET");
        garbage.Request.Headers.Authorization = "Bakabase-Node malformed";
        await gate.RunAsync(garbage);
        Assert.AreEqual("DataSyncSharingDisabled", Error(garbage));
        Assert.IsFalse(gate.ReachedEndpoint);
    }

    /// <summary>
    /// G22b: turned back on, info shows the new epoch (a reader's session stops there, before signing: see
    /// <c>PeerSessionScopeTests.AfterTheSourceRotatesItsEpochTheReaderStopsBeforeSigning</c>), and a handshake with the
    /// old grant is refused as revoked.
    /// </summary>
    [TestMethod]
    public async Task G22b_TurnedBackOnInfoShowsTheNewEpochAndTheOldGrantIsRevoked()
    {
        using var gate = await ScopeGate.CreateAsync();
        var rotated = await gate.Peers.RotateLibraryEpochAsync();
        await gate.SwitchAsync(library: false, dataSync: true);
        var info = Context(Info, "GET");
        await gate.RunAsync(info, gate.InfoEndpoint);
        Assert.AreEqual(rotated.LibraryEpoch, Body<NodeInfo>(info).LibraryEpoch);
        Assert.AreNotEqual(gate.DataSync.LibraryEpoch, rotated.LibraryEpoch);

        var handshake = await gate.HandshakeAsync(gate.DataSync);
        Assert.AreEqual((401, "GrantRevoked"), (handshake.Response.StatusCode, Error(handshake)));
    }

    [TestMethod]
    public async Task G23_RemovingThePeerRevokesItsDataSyncGrant()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.Peers.RemovePeerAsync("reader-node");
        var context = await gate.SendAsync(Head, "GET", gate.DataSync);
        Assert.AreEqual((401, "GrantRevoked"), (context.Response.StatusCode, Error(context)));
        Assert.IsFalse(gate.ReachedEndpoint);
    }

    // ---- G24–G30: codes, approvals and the two management planes --------------------------------------------------

    [TestMethod]
    public async Task G24_ALibraryCodeIsNotADefinitionsCodeAndIsNotUsedUpByTrying()
    {
        using var gate = await ScopeGate.CreateAsync();
        var code = (await gate.Peers.IssueInvitationAsync()).Code;
        var context = await gate.PairCodeAsync(new NodeDataSyncPairCodeRequest("asker-node", "Asker", code,
            "asker-transaction", NodeRequestSignature.RandomToken(), NodeDataSyncIntents.Follow));
        Assert.AreEqual((403, "InvalidPairingCode"), (context.Response.StatusCode, Error(context)));
        Assert.AreEqual("granted", (await gate.Peers.ExchangeCodeAsync(new NodePairCodeRequest("asker-node", "Asker",
            code, "library-asker", NodeRequestSignature.RandomToken()))).Outcome);
    }

    [TestMethod]
    public async Task G25_ADefinitionsCodeIsNotALibraryCodeAndIsNotUsedUpByTrying()
    {
        using var gate = await ScopeGate.CreateAsync();
        var code = (await gate.Peers.IssueDataSyncInvitationAsync(allowTwoWay: false)).Code;
        var library = await Assert.ThrowsExactlyAsync<FederationAccessException>(() => gate.Peers.ExchangeCodeAsync(
            new NodePairCodeRequest("asker-node", "Asker", code, "library-asker", NodeRequestSignature.RandomToken())));
        Assert.AreEqual("InvalidPairingCode", library.ErrorCode);
        var context = await gate.PairCodeAsync(new NodeDataSyncPairCodeRequest("asker-node", "Asker", code,
            "asker-transaction", NodeRequestSignature.RandomToken(), NodeDataSyncIntents.Follow));
        Assert.AreEqual("granted", Body<NodePairExchange>(context).Outcome);
    }

    [TestMethod]
    public async Task G26_ApprovingADefinitionsRequestNeitherCreatesNorRevokesLibraryAccess()
    {
        using var gate = await ScopeGate.CreateAsync();
        var libraryBefore = await LibraryGrantsAsync(gate);
        await gate.Peers.SubmitDataSyncRequestAsync(new NodeDataSyncPairRequest("reader-node", "Reader", "again",
            NodeRequestSignature.RandomToken(), NodeDataSyncIntents.Follow));
        await gate.Peers.ApproveDataSyncAsync("again");
        CollectionAssert.AreEqual(libraryBefore, await LibraryGrantsAsync(gate));
        Assert.AreEqual(FederationScopes.LibraryRead,
            (await gate.Grants.ValidateAsync(gate.Library.GrantId, gate.Library.LibraryEpoch)).Scope);
    }

    [TestMethod]
    public async Task G27_ApprovingALibraryRequestNeitherCreatesNorRevokesDefinitionsAccess()
    {
        using var gate = await ScopeGate.CreateAsync();
        var before = (await gate.Peers.GetDataSyncStatusAsync()).Grants.Select(g => g.GrantId).ToArray();
        await gate.Peers.RequestPairingAsync(new NodePairRequest("reader-node", "Reader", "library-again",
            NodeRequestSignature.RandomToken()));
        await gate.Peers.ApproveAsync("library-again");
        CollectionAssert.AreEqual(before, (await gate.Peers.GetDataSyncStatusAsync()).Grants.Select(g => g.GrantId).ToArray());
        await gate.SendAsync(Head, "GET", gate.DataSync);
        Assert.IsTrue(gate.ReachedEndpoint);
    }

    [TestMethod]
    public async Task G28_TheLibrarysOwnValidationRefusesADataSyncGrant()
    {
        using var gate = await ScopeGate.CreateAsync();
        var e = await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            gate.Grants.ValidateAsync(gate.DataSync.GrantId, gate.DataSync.LibraryEpoch));
        Assert.AreEqual(("ScopeNotGranted", 403), (e.ErrorCode, e.StatusCode));
    }

    [TestMethod]
    public async Task G29_TheDataSyncPlaneDoesNotSeeLibraryRequests()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.Peers.RequestPairingAsync(new NodePairRequest("asker-node", "Asker", "library-request",
            NodeRequestSignature.RandomToken()));
        var e = await Assert.ThrowsExactlyAsync<DataSyncProblemException>(() =>
            gate.DataSyncGrants.ApproveAsync("library-request", true, default));
        Assert.AreEqual(DataSyncProblemCode.RequestNotFound, e.Problem.Code);
        Assert.AreEqual("awaitingApproval",
            (await gate.Peers.GetStatusAsync()).Requests.Single(r => r.RequestId == "library-request").Status);
        Assert.IsFalse((await gate.DataSyncGrants.GetRequestsAsync(default)).Any(r => r.RequestId == "library-request"));
    }

    [TestMethod]
    public async Task G30_TheLibraryPlaneDoesNotSeeDefinitionsRequests()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.Peers.SubmitDataSyncRequestAsync(new NodeDataSyncPairRequest("asker-node", "Asker",
            "datasync-request", NodeRequestSignature.RandomToken(), NodeDataSyncIntents.Follow));
        var e = await Assert.ThrowsExactlyAsync<FederationAccessException>(() => gate.Peers.ApproveAsync("datasync-request"));
        Assert.AreEqual(("InvalidPairingRequest", 400), (e.ErrorCode, e.StatusCode));
        Assert.IsFalse((await gate.Peers.GetStatusAsync()).Requests.Any(r => r.RequestId == "datasync-request"));
        Assert.AreEqual("awaitingApproval",
            (await gate.Peers.GetDataSyncStatusAsync()).Requests.Single(r => r.NodeId == "asker-node").Status);
    }

    // ---- G31–G33: who may create definitions access through /data-sync (§7.1.5) ------------------------------------

    private static readonly (string Name, Func<DataSyncController, Task<DataSyncProblem?>> Call)[] AccessCreating =
    [
        ("PUT /data-sync/sharing {enabled:true}",
            async c => (await c.SetSharing(new DataSyncSharingInput(true, true), default)).Data),
        ("POST /data-sync/invitations", async c => (await c.CreateInvitation(new DataSyncInvitationInput(true), default)).Data!.Problem),
        ("POST /data-sync/requests/{id}/approve",
            async c => (await c.ApproveRequest("req-in-1", new DataSyncApproveInput(true, null), default)).Data!.Problem),
        ("POST /data-sync/links (two-way)",
            async c => (await c.CreateLink(new DataSyncLinkCreateInput("node-newpc", null, null, DataSyncLinkMode.TwoWay,
                [..Bakabase.Modules.DataSync.Abstractions.DataSyncKindIds.All]), default)).Data!.Problem),
    ];

    [TestMethod]
    public async Task G31_AnUnpairedUnrestrictedBrowserCannotCreateDefinitionsAccess()
    {
        foreach (var (name, call) in AccessCreating)
        {
            var fake = new FakeDataSyncService();
            var problem = await call(DataSyncControllerFor(fake,
                new RemoteAccessContext { IsLoopback = false, Mode = RemoteAccessMode.Unrestricted }));
            Assert.AreEqual(DataSyncProblemCode.NotAllowedOnThisDevice, problem?.Code, name);
            // Nothing changes: the facade is either not asked, or told the caller may not create access.
            Assert.IsTrue(fake.Calls.All(c => c.StartsWith("Create")), name);
        }
    }

    /// <summary>
    /// G31 refuses a caller that has not paired, so the way to a device key is part of the row: through the real gate,
    /// an unpaired caller of an Enabled server — pairing required or not — can neither approve a pairing request (the
    /// one it filed itself included) nor issue a code nor manage devices; the host and paired devices can. On an
    /// Unrestricted server every caller can, by design: the LAN browser is the operator there, and it is where a
    /// headless server's requests are answered, so there G31 holds only until the caller pairs itself (data-sync.md,
    /// "Who may create or widen access").
    /// </summary>
    [TestMethod]
    public void G31b_AnUnpairedCallerOfAnEnabledServerCannotPairItself()
    {
        string[] management =
        [
            nameof(RemoteAccessController.IssuePairingCode),
            nameof(RemoteAccessController.ApprovePairingRequest),
            nameof(RemoteAccessController.RejectPairingRequest),
            nameof(RemoteAccessController.GetPendingRequests),
            nameof(RemoteAccessController.GetDevices),
            nameof(RemoteAccessController.RevokeDevice),
            nameof(RemoteAccessController.RenameDevice),
        ];
        var device = new RemoteDevice { Id = "device-1", Name = "Desktop", Key = "key", CreatedAt = DateTime.UtcNow };
        var callers = new (string Who, RemoteAccessContext Remote, bool Admitted)[]
        {
            ("an unpaired caller of an Enabled server",
                new RemoteAccessContext { IsLoopback = false, Mode = RemoteAccessMode.Enabled }, false),
            ("a paired device", new RemoteAccessContext { IsLoopback = false, Mode = RemoteAccessMode.Enabled, Device = device },
                true),
            ("this device", new RemoteAccessContext { IsLoopback = true, Mode = RemoteAccessMode.Enabled }, true),
            ("an unpaired caller of an Unrestricted server",
                new RemoteAccessContext { IsLoopback = false, Mode = RemoteAccessMode.Unrestricted }, true),
        };
        foreach (var action in management)
        foreach (var (who, remote, admitted) in callers)
        {
            var http = new DefaultHttpContext();
            http.SetRemoteAccessContext(remote);
            var descriptor = new ControllerActionDescriptor
            {
                MethodInfo = typeof(RemoteAccessController).GetMethod(action)!,
                ControllerTypeInfo = typeof(RemoteAccessController).GetTypeInfo()
            };
            var filter = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), descriptor), []);
            new RemoteAccessAuthorizationFilter().OnAuthorization(filter);
            Assert.AreEqual(admitted, filter.Result is null, $"{action} by {who}");
            if (!admitted)
            {
                Assert.AreEqual(nameof(RemoteAccessDenialReason.HostOnly),
                    http.Response.Headers["X-Bakabase-Remote-Access"].ToString(), $"{action} by {who}");
            }
        }
    }

    [TestMethod]
    public async Task G32_TheSameBrowserCanStillShutAccessOff()
    {
        var unrestricted = new RemoteAccessContext { IsLoopback = false, Mode = RemoteAccessMode.Unrestricted };
        var fake = new FakeDataSyncService();
        var controller = DataSyncControllerFor(fake, unrestricted);
        Assert.IsNull((await controller.RejectRequest("req-in-1", default)).Data);
        Assert.IsNull((await controller.RevokeReader("node-nas", default)).Data);
        Assert.IsNull((await controller.SetSharing(new DataSyncSharingInput(false), default)).Data);
        CollectionAssert.AreEqual(new[] { "RejectRequestAsync", "RevokeReaderAsync", "SetSharingAsync" }, fake.Calls.ToArray());
    }

    [TestMethod]
    public async Task G33_APairedDeviceAndThisDeviceMayCreateDefinitionsAccess()
    {
        var paired = new RemoteAccessContext
        {
            IsLoopback = false, Mode = RemoteAccessMode.Enabled,
            Device = new RemoteDevice { Id = "device-1", Name = "Desktop", Key = "key", CreatedAt = DateTime.UtcNow }
        };
        var loopback = new RemoteAccessContext { IsLoopback = true, Mode = RemoteAccessMode.Disabled };
        foreach (var context in new[] { paired, loopback })
        foreach (var (name, call) in AccessCreating)
        {
            var fake = new FakeDataSyncService();
            Assert.IsNull(await call(DataSyncControllerFor(fake, context)), name);
            Assert.AreEqual(1, fake.Calls.Count, name);
        }
    }

    // ---- G34–G36: codes and two-way consent, remote access ---------------------------------------------------------

    [TestMethod]
    public async Task G34_ATwoWayRedemptionOfACodeWithoutConsentIsGrantedButNeverReadBack()
    {
        using var gate = await ScopeGate.CreateAsync();
        var code = (await gate.Peers.IssueDataSyncInvitationAsync(allowTwoWay: false)).Code;
        var context = await gate.PairCodeAsync(new NodeDataSyncPairCodeRequest("asker-node", "Asker", code,
            "asker-transaction", NodeRequestSignature.RandomToken(), NodeDataSyncIntents.TwoWay,
            new NodeReciprocalOffer(["http://asker.test:5000"], NodeRequestSignature.RandomToken())));

        var exchange = Body<NodePairExchange>(context);
        Assert.AreEqual(("granted", NodeDataSyncReadBack.Declined), (exchange.Outcome, exchange.ReadBack));
        Assert.IsNotNull(exchange.Credentials);
        Assert.IsNull(await gate.Peers.TakeDataSyncReciprocalOfferAsync("asker-node"), "No reciprocal code is redeemed.");
        CollectionAssert.AreEqual(new[] { "inbound asker-node TwoWay False" }, gate.Events.Raised.ToArray());
        Assert.AreEqual(0, gate.ReadBackRequests.Reader.Count, "Nothing connects back.");
    }

    [TestMethod]
    public async Task G35_ATwoWayRedemptionOfAConsentedCodeIsReadBack()
    {
        using var gate = await ScopeGate.CreateAsync();
        var code = (await gate.Peers.IssueDataSyncInvitationAsync(allowTwoWay: true)).Code;
        var context = await gate.PairCodeAsync(new NodeDataSyncPairCodeRequest("asker-node", "Asker", code,
            "asker-transaction", NodeRequestSignature.RandomToken(), NodeDataSyncIntents.TwoWay,
            new NodeReciprocalOffer(["http://asker.test:5000"], NodeRequestSignature.RandomToken())));

        Assert.AreEqual(("granted", NodeDataSyncReadBack.Started),
            (Body<NodePairExchange>(context).Outcome, Body<NodePairExchange>(context).ReadBack));
        // The runtime creates the creator's link (WaitingForPeerReview) on this event.
        CollectionAssert.AreEqual(new[] { "inbound asker-node TwoWay True" }, gate.Events.Raised.ToArray());
        // And this device connects back to the address the redeemer offered.
        var first = await gate.ReadBackRequests.Reader.ReadAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
        Assert.AreEqual("GET http://asker.test:5000/federation/v1/info", first);
    }

    [TestMethod]
    public async Task G36_WithRemoteAccessDisabledNoCodeIsMadeAndNoRequestApproved()
    {
        using var gate = await ScopeGate.CreateAsync();
        await gate.Peers.SubmitDataSyncRequestAsync(new NodeDataSyncPairRequest("asker-node", "Asker", "waiting",
            NodeRequestSignature.RandomToken(), NodeDataSyncIntents.Follow));
        gate.Remote.Mode = RemoteAccessMode.Disabled;

        var invitation = await Assert.ThrowsExactlyAsync<DataSyncProblemException>(() =>
            gate.DataSyncGrants.CreateInvitationAsync(new DataSyncInvitationInput(false), default));
        Assert.AreEqual(DataSyncProblemCode.RemoteAccessOff, invitation.Problem.Code);
        var approval = await Assert.ThrowsExactlyAsync<DataSyncProblemException>(() =>
            gate.DataSyncGrants.ApproveAsync("waiting", false, default));
        Assert.AreEqual(DataSyncProblemCode.RemoteAccessOff, approval.Problem.Code);
        Assert.AreEqual("awaitingApproval",
            (await gate.Peers.GetDataSyncStatusAsync()).Requests.Single(r => r.RequestId == "waiting").Status);
    }

    // ---- the generated matrix ------------------------------------------------------------------------------------

    /// <summary>
    /// Every Export action, from route metadata, under a library grant, a datasync grant and no grant, with each switch
    /// on and off: only a grant of the action's declared scope, with that scope's switch on, reaches it — through the
    /// early gate and the endpoint's own fail-closed check alike.
    /// </summary>
    [TestMethod]
    public async Task EveryExportActionUnderEveryScope()
    {
        using var gate = await ScopeGate.CreateAsync();
        var actions = FederationActions().Where(a => a.Endpoint?.Kind == FederationEndpointKind.Export).ToArray();
        Assert.IsTrue(actions.Length >= 12, actions.Length.ToString());
        var cases = 0;
        foreach (var library in new[] { false, true })
        foreach (var dataSync in new[] { false, true })
        {
            await gate.SwitchAsync(library, dataSync);
            foreach (var (type, action, path, methods, endpoint) in actions)
            foreach (var method in methods)
            foreach (var grant in new[] { gate.Library, gate.DataSync, null })
            {
                var grantScope = grant == null ? null
                    : grant == gate.Library ? FederationScopes.LibraryRead : FederationScopes.DataSyncRead;
                var expected = grantScope != null && FederationScopes.Admits(endpoint!.Scope, grantScope) &&
                               (grantScope == FederationScopes.LibraryRead ? library : dataSync);
                var context = Context(path, method);
                if (grant != null) gate.Sign(context, grant);
                var reached = false;
                await gate.RunAsync(context, http =>
                {
                    var descriptor = new ControllerActionDescriptor
                        { MethodInfo = action, ControllerTypeInfo = type.GetTypeInfo() };
                    var filter = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), descriptor), []);
                    new FederationLocalAccessFilter().OnAuthorizationAsync(filter).GetAwaiter().GetResult();
                    reached = filter.Result == null;
                    return Task.CompletedTask;
                });
                var name = $"{method} {path} as {grantScope ?? "nobody"} with S={library} D={dataSync}";
                Assert.AreEqual(expected, reached, name);
                if (!expected)
                    Assert.IsTrue(context.Response.StatusCode is 401 or 403, $"{name}: {context.Response.StatusCode}");
                cases++;
            }
        }
        Assert.IsTrue(cases >= 12 * 3 * 4, cases.ToString());
    }

    /// <summary>A scope the endpoint does not declare, or none at all, is refused by the endpoint check itself.</summary>
    [TestMethod]
    public async Task TheEndpointCheckRefusesAGrantOfAnotherScopeAndAnActionWithoutAScope()
    {
        foreach (var (action, scope, allowed) in new[]
                 {
                     (nameof(ScopedActions.Library), FederationScopes.LibraryRead, true),
                     (nameof(ScopedActions.Library), FederationScopes.DataSyncRead, false),
                     (nameof(ScopedActions.DataSync), FederationScopes.DataSyncRead, true),
                     (nameof(ScopedActions.DataSync), FederationScopes.LibraryRead, false),
                     (nameof(ScopedActions.Either), FederationScopes.LibraryRead, true),
                     (nameof(ScopedActions.Either), FederationScopes.DataSyncRead, true),
                     (nameof(ScopedActions.Either), FederationScopes.Any, false),
                     (nameof(ScopedActions.Unscoped), FederationScopes.LibraryRead, false),
                     (nameof(ScopedActions.Unscoped), FederationScopes.DataSyncRead, false),
                 })
        {
            var http = Context(Queries, "POST");
            FederationHttpContext.MarkHandled(http, FederationEndpointKind.Export,
                new NodePrincipal("grant", "reader", "owner", "epoch", 1, scope));
            var descriptor = new ControllerActionDescriptor
                { MethodInfo = typeof(ScopedActions).GetMethod(action)!, ControllerTypeInfo = typeof(ScopedActions).GetTypeInfo() };
            var filter = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), descriptor), []);
            await new FederationLocalAccessFilter().OnAuthorizationAsync(filter);
            Assert.AreEqual(allowed, filter.Result == null, $"{action} as {scope}");
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    private static T Body<T>(HttpContext context) =>
        JsonSerializer.Deserialize<T>(((MemoryStream)context.Response.Body).ToArray(), FederationJson.Options)!;

    private static async Task<string[]> LibraryGrantsAsync(ScopeGate gate) =>
        (await gate.Peers.GetStatusAsync()).Peers.Select(p => p.InboundGrant?.GrantId ?? "-").ToArray();

    /// <summary>
    /// The endpoint that runs <paramref name="action"/> and writes its result (<see cref="Execute"/>). A method group
    /// rather than a lambda: ASP0016 takes the <c>Task&lt;IActionResult&gt;</c> a lambda's nested action returns for
    /// the endpoint's own result.
    /// </summary>
    private static RequestDelegate Endpoint(Func<HttpContext, Task<IActionResult>> action) =>
        new ActionEndpoint(action).InvokeAsync;

    private sealed class ActionEndpoint(Func<HttpContext, Task<IActionResult>> action)
    {
        public Task InvokeAsync(HttpContext http) => Execute(http, () => action(http));
    }

    /// <summary>Runs an action and writes its result the way MVC and the federation exception filter would.</summary>
    private static async Task Execute(HttpContext http, Func<Task<IActionResult>> action)
    {
        IActionResult result;
        try { result = await action(); }
        catch (FederationAccessException e)
        {
            result = new ContentResult
            {
                StatusCode = e.StatusCode,
                Content = JsonSerializer.Serialize(new { code = e.ErrorCode, message = e.Message }, FederationJson.Options)
            };
        }
        switch (result)
        {
            case ContentResult content:
                http.Response.StatusCode = content.StatusCode ?? 200;
                await http.Response.WriteAsync(content.Content ?? "");
                break;
            case FileContentResult file:
                http.Response.StatusCode = 200;
                http.Response.ContentType = file.ContentType;
                await http.Response.Body.WriteAsync(file.FileContents);
                break;
            default:
                throw new NotSupportedException(result.GetType().Name);
        }
    }

    private static DataSyncController DataSyncControllerFor(IDataSyncService service, RemoteAccessContext remote)
    {
        var http = new DefaultHttpContext();
        http.SetRemoteAccessContext(remote);
        return new DataSyncController(service) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private sealed class ScopedActions
    {
        [FederationEndpoint(FederationEndpointKind.Export, Scope = FederationScopes.LibraryRead)] public void Library() { }
        [FederationEndpoint(FederationEndpointKind.Export, Scope = FederationScopes.DataSyncRead)] public void DataSync() { }
        [FederationEndpoint(FederationEndpointKind.Export, Scope = FederationScopes.Any)] public void Either() { }
        [FederationEndpoint(FederationEndpointKind.Export)] public void Unscoped() { }
    }

    /// <summary>
    /// A source node with both switches on and one reader holding both a library and a datasync grant, behind the real
    /// node gate and the legacy remote-access middleware; the endpoint is whatever a test runs there.
    /// </summary>
    private sealed class ScopeGate : IFederationDataDirectory, INodeIdSource, IDisposable
    {
        public const string Page = "{\"complete\":true,\"kind\":\"customProperty\",\"records\":[]}";
        private readonly ServiceProvider _services;

        private ScopeGate()
        {
            Store = new FederationStateStore(this, this);
            Identity = new NodeIdentityProvider(Store);
            Peers = new FederationPeerService(Store, Identity, Leases, TimeProvider.System);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<INodeInfoContributor>(sp => new DataSyncNodeInfoContributor(Store,
                sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<DataSyncNodeInfoContributor>.Instance));
            services.AddSingleton(Notifications);
            services.AddSingleton<INotificationService>(Notifications);
            services.AddSingleton<IBakabaseLocalizer, TestBakabaseLocalizer>();
            services.AddSingleton<IDataSyncFeedSource>(Feed);
            services.AddSingleton<IDataSyncGrantEvents>(Events);
            _services = services.BuildServiceProvider();
            Grants = new NodeGrantService(Store, Identity, Leases, TimeProvider.System, null,
                _services.GetRequiredService<INodeInfoContributor>());
            Auth = new NodeGrantAuthenticator(Grants, new NodeNonceCache(TimeProvider.System), TimeProvider.System);
            // Reading back goes to whatever address a redeemer offered; here it only records that it tried.
            var wire = new FederationHttpClient(new HttpClient(new RecordingHandler(ReadBackRequests)));
            var pairing = new NodePairingClient(Store, Identity, wire, TimeProvider.System, Leases, Peers);
            Flow = new FederationPairingFlow(pairing, Peers, null!, Remote, NullLogger<FederationPairingFlow>.Instance,
                _services);
            DataSyncGrants = new FederationDataSyncGrants(Peers, pairing, Flow, Store, Identity, Remote,
                new TestBOptionsManager<RemoteAccessOptions>(new RemoteAccessOptions()),
                new PeerSessionFactory(Store, Identity, wire, TimeProvider.System), new NoDiscovery());
        }

        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "federation-scope-gate-" + Guid.NewGuid().ToString("N"));
        public string Ensure() { Directory.CreateDirectory(Path); return Path; }
        public Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("owner-node");

        public RemoteService Remote { get; } = new();
        public GrantLeaseRegistry Leases { get; } = new();
        public FederationStateStore Store { get; }
        public NodeIdentityProvider Identity { get; }
        public FederationPeerService Peers { get; }
        public NodeGrantService Grants { get; }
        public NodeGrantAuthenticator Auth { get; }
        public FederationPairingFlow Flow { get; }
        public FederationDataSyncGrants DataSyncGrants { get; }
        public RecordingNotificationService Notifications { get; } = new();
        public RecordingFeed Feed { get; } = new();
        public RecordingEvents Events { get; } = new();
        public System.Threading.Channels.Channel<string> ReadBackRequests { get; } =
            System.Threading.Channels.Channel.CreateUnbounded<string>();
        public NodeCredentials Library { get; private set; } = null!;
        public NodeCredentials DataSync { get; private set; } = null!;
        public bool ReachedEndpoint { get; private set; }

        public static async Task<ScopeGate> CreateAsync()
        {
            var gate = new ScopeGate();
            await gate.SwitchAsync(library: true, dataSync: true);
            var librarySecret = NodeRequestSignature.RandomToken();
            await gate.Peers.RequestPairingAsync(new NodePairRequest("reader-node", "Reader", "library-grant", librarySecret));
            await gate.Peers.ApproveAsync("library-grant");
            gate.Library = (await gate.Peers.ClaimPairingAsync(new("library-grant", "reader-node", librarySecret))).Credentials!;
            var dataSyncSecret = NodeRequestSignature.RandomToken();
            await gate.Peers.SubmitDataSyncRequestAsync(new NodeDataSyncPairRequest("reader-node", "Reader",
                "datasync-grant", dataSyncSecret, NodeDataSyncIntents.Follow));
            await gate.Peers.ApproveDataSyncAsync("datasync-grant");
            gate.DataSync = (await gate.Peers.ClaimDataSyncAsync(new("datasync-grant", "reader-node", dataSyncSecret)))
                .Credentials!;
            return gate;
        }

        public async Task SwitchAsync(bool library, bool dataSync)
        {
            await Peers.SetSharingAsync(library);
            await Peers.SetDataSyncSharingAsync(dataSync);
        }

        public void Sign(HttpContext context, NodeCredentials credentials) =>
            context.Request.Headers.Authorization = NodeRequestSignature.Create(credentials, context.Request.Method,
                context.Request.Path, context.Request.QueryString.HasValue ? context.Request.QueryString.Value![1..] : "",
                NodeRequestSignature.Hash(((MemoryStream)context.Request.Body).ToArray()), DateTimeOffset.UtcNow);

        public async Task<HttpContext> SendAsync(string path, string method, NodeCredentials? credentials)
        {
            var context = Context(path, method);
            if (credentials != null) Sign(context, credentials);
            await RunAsync(context);
            return context;
        }

        public Task InfoEndpoint(HttpContext http) => Execute(http, () => PeerController(http).Info(http.RequestAborted));

        public async Task<HttpContext> HandshakeAsync(NodeCredentials credentials)
        {
            var context = Context(Handshake, "POST");
            var request = new NodeHandshakeRequest(NodeRequestSignature.RandomToken());
            context.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(request, FederationJson.Options));
            Sign(context, credentials);
            await RunAsync(context, Endpoint(http => PeerController(http).Handshake(request, http.RequestAborted)));
            return context;
        }

        public async Task<HttpContext> PairCodeAsync(NodeDataSyncPairCodeRequest request)
        {
            var context = Context("/federation/v1/pair/datasync/code", "POST", "192.168.20.30");
            await RunAsync(context, Endpoint(http => PairingController(http).PairCode(request, default)));
            return context;
        }

        public FederationPeerController PeerController(HttpContext http) =>
            new(Peers, null!, Identity, Grants, null!, null!, null!, null!, Remote, null!, TimeProvider.System, Flow, Store)
                { ControllerContext = new ControllerContext { HttpContext = http } };

        public FederationDataSyncPairingController PairingController(HttpContext http) =>
            new(Peers, new NodePairingRateLimiter(TimeProvider.System), Flow,
                NullLogger<FederationDataSyncPairingController>.Instance)
                { ControllerContext = new ControllerContext { HttpContext = http } };

        public DataSyncNodeController FeedController(HttpContext http) =>
            new(Peers) { ControllerContext = new ControllerContext { HttpContext = http } };

        public async Task RunAsync(HttpContext context, RequestDelegate? endpoint = null)
        {
            ReachedEndpoint = false;
            context.RequestServices = _services;
            var legacy = new RemoteAccessMiddleware(async ctx =>
            {
                ReachedEndpoint = true;
                if (endpoint != null) await endpoint(ctx);
            }, NullLogger<RemoteAccessMiddleware>.Instance);
            var middleware = new FederationAccessMiddleware(ctx => legacy.InvokeAsync(ctx, Remote, null!, null!));
            await middleware.InvokeAsync(context, Store, Auth, Remote, Leases);
        }

        public void Dispose()
        {
            Leases.Dispose();
            _services.Dispose();
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }

    private sealed class RecordingFeed : IDataSyncFeedSource
    {
        public List<(string Call, DataSyncReader Reader, DataSyncFeedQuery? Query)> Calls { get; } = [];
        public Func<Task>? DuringPage { get; set; }

        public Task<DataSyncFeedHead> GetHeadAsync(DataSyncReader reader, DataSyncFeedQuery query, CancellationToken ct)
        {
            Calls.Add(("head", reader, query));
            return Task.FromResult(new DataSyncFeedHead("owner-node", "epoch", "0123456789abcdef", 1, 1, "1.0.0", 0, [],
                new DataSyncSourceAttention(false, 0, 0, false, 0), null, null));
        }

        public Task<DataSyncFeedManifest> CreateSnapshotAsync(DataSyncReader reader, DataSyncFeedQuery query,
            CancellationToken ct)
        {
            Calls.Add(("manifest", reader, query));
            return Task.FromResult(new DataSyncFeedManifest("snapshot-1", 120_000, "owner-node", "epoch",
                "0123456789abcdef", 1, 1, "1.0.0", [], null, new DataSyncSourceAttention(false, 0, 0, false, 0)));
        }

        public async Task<byte[]> GetPageAsync(DataSyncReader reader, string snapshotId, string kind, long sinceSeq,
            string? cursor, CancellationToken ct)
        {
            Calls.Add(("changes", reader, null));
            if (DuringPage != null) await DuringPage();
            ct.ThrowIfCancellationRequested();
            return Encoding.UTF8.GetBytes(ScopeGate.Page);
        }
    }

    private sealed class RecordingEvents : IDataSyncGrantEvents
    {
        private readonly List<string> _raised = [];
        public IReadOnlyList<string> Raised { get { lock (_raised) return [.._raised]; } }
        public void OutboundGranted(string peerNodeId) { lock (_raised) _raised.Add("outbound " + peerNodeId); }

        public void InboundGranted(string peerNodeId, DataSyncRequestIntent intent, bool readBackStarted)
        {
            lock (_raised) _raised.Add($"inbound {peerNodeId} {intent} {readBackStarted}");
        }
    }

    private sealed class RecordingHandler(System.Threading.Channels.Channel<string> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            requests.Writer.TryWrite($"{request.Method} {request.RequestUri}");
            throw new HttpRequestException("No route to host.");
        }
    }

    private sealed class NoDiscovery : INodePeerDiscovery
    {
        public Task<IReadOnlyList<NodeDiscoveryCandidate>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeDiscoveryCandidate>>([]);
    }
}
