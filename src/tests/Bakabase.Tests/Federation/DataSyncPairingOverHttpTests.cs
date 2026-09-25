using System.Net;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Service.Components.Federation;
using Bakabase.Tests.RemoteAccess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

/// <summary>
/// Definitions pairing between two real nodes over HTTP, through the Service's gates, model binding and controllers
/// (§7.2): a two-way request approved with read-back, a code made with two-way consent, the claim loop, what data
/// sync is told, and how a device that cannot take part says so.
/// </summary>
[TestClass]
public sealed class DataSyncPairingOverHttpTests
{
    [TestMethod]
    public async Task ATwoWayRequestIsApprovedAndTheRequesterReadBack()
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS");
        await desk.Grants.SetSharingEnabledAsync(true, true, default);
        await nas.Grants.SetSharingEnabledAsync(true, true, default);

        var asked = await desk.Grants.RequestAccessAsync(
            new DataSyncAccessRequestInput(null, nas.Address, null, DataSyncRequestIntent.TwoWay), default);
        Assert.AreEqual(("awaitingApproval", "node-nas", "NAS", (string?)null),
            (asked.Outcome, asked.PeerNodeId, asked.PeerName, asked.ReadBack));

        // The NAS files it among definitions requests, tells a person, and says where it came from.
        var request = (await nas.Grants.GetRequestsAsync(default)).Single();
        Assert.AreEqual((DataSyncRequestDirection.Incoming, "node-desk", "Desk", DataSyncRequestIntent.TwoWay, "127.0.0.1"),
            (request.Direction, request.NodeId, request.NodeName, request.Intent, request.RemoteAddress));
        Assert.AreEqual(DateTimeKind.Utc, request.ExpiresAt.Kind);
        Assert.IsFalse(request.ClaimsKnownDevice);
        Assert.AreEqual("DataSync", nas.Notifications.Created.Single().Source);
        Assert.AreEqual(DataSyncRequestDirection.Outgoing, (await desk.Grants.GetRequestsAsync(default)).Single().Direction);

        var approval = await nas.Grants.ApproveAsync(request.RequestId, readBack: true, default);
        Assert.AreEqual(("node-desk", "Desk", DataSyncRequestIntent.TwoWay, true, (string?)null),
            (approval.PeerNodeId, approval.PeerName, approval.Intent, approval.ReadBackGranted, approval.ReadBackError));
        CollectionAssert.AreEqual(new[] { "outbound node-desk", "inbound node-desk TwoWay True" }, nas.Events.Raised.ToArray());
        // The desk granted the NAS through its reciprocal code: a grant made here, with nothing to read back.
        CollectionAssert.AreEqual(new[] { "inbound node-nas Follow False" }, desk.Events.Raised.ToArray());

        // The desk's claim loop collects its own grant and says so.
        await desk.Flow.ClaimPendingAsync(default);
        await desk.Flow.ClaimPendingAsync(default);
        CollectionAssert.AreEqual(new[] { "inbound node-nas Follow False", "outbound node-nas" }, desk.Events.Raised.ToArray());

        Assert.IsTrue(await desk.Grants.HasOutboundGrantAsync("node-nas", default));
        Assert.IsTrue(await nas.Grants.HasOutboundGrantAsync("node-desk", default));
        Assert.AreEqual("node-nas", (await desk.Grants.GetGrantsAsync(default)).Single().NodeId);
        Assert.AreEqual("node-desk", (await nas.Grants.GetGrantsAsync(default)).Single().NodeId);
        var peer = (await desk.Grants.GetPeersAsync(false, default)).Single();
        Assert.AreEqual(("node-nas", "NAS", nas.Address, true, true), (peer.NodeId, peer.Name, peer.Address, peer.WeMayRead, peer.TheyMayRead));

        // Neither device has library access to the other.
        Assert.IsTrue((await desk.Peers.GetStatusAsync()).Peers.All(p => p.InboundGrant == null && p.OutboundGrant == null));
        Assert.IsTrue((await nas.Peers.GetStatusAsync()).Peers.All(p => p.InboundGrant == null && p.OutboundGrant == null));
    }

    [TestMethod]
    public async Task ADataSyncSessionReachesTheFeedAndNothingElse()
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS");
        await nas.Grants.SetSharingEnabledAsync(true, false, default);
        var code = await nas.Grants.CreateInvitationAsync(new DataSyncInvitationInput(false), default);
        await desk.Grants.RequestAccessAsync(
            new DataSyncAccessRequestInput(null, nas.Address, code.Code, DataSyncRequestIntent.Follow), default);

        var session = await desk.Sessions.GetAsync("node-nas", FederationScopes.DataSyncRead);
        Assert.AreEqual(FederationScopes.DataSyncRead, session.Scope);
        Assert.IsTrue(session.Info.SharesDefinitions);
        Assert.AreEqual("Online", desk.Sessions.GetConnectionState("node-nas", FederationScopes.DataSyncRead));
        Assert.AreEqual("NodeNotAuthorized", (await Assert.ThrowsExactlyAsync<Bakabase.Modules.Federation.Security.FederationAccessException>(
            () => desk.Sessions.GetAsync("node-nas"))).ErrorCode);

        // The feed controller is reached (this host has no feed source, so it says so), a library route is not.
        using (var head = await desk.Transport.SendAsync(session, HttpMethod.Get, "/federation/v1/export/datasync/head"))
        {
            Assert.AreEqual(HttpStatusCode.NotImplemented, head.StatusCode);
            Assert.AreEqual("NotImplemented", await CodeAsync(head));
        }
        await nas.Peers.SetSharingAsync(true);
        using (var library = await desk.Transport.SendAsync(session, HttpMethod.Get, "/federation/v1/export/mapping-roots"))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, library.StatusCode);
            Assert.AreEqual("ScopeNotGranted", await CodeAsync(library));
        }
        using (var invalid = await desk.Transport.SendAsync(session, HttpMethod.Get,
                   "/federation/v1/export/datasync/head?since=customProperty:-1"))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.AreEqual("InvalidFeedQuery", await CodeAsync(invalid));
        }
    }

    [TestMethod]
    public async Task ACodeMadeWithTwoWayConsentIsReadBackInTheBackground()
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS");
        await nas.Grants.SetSharingEnabledAsync(true, false, default);
        await desk.Grants.SetSharingEnabledAsync(true, false, default);

        var invitation = await nas.Grants.CreateInvitationAsync(new DataSyncInvitationInput(true), default);
        Assert.AreEqual(8, invitation.Code.Length);
        Assert.AreEqual(DateTimeKind.Utc, invitation.ExpiresAt.Kind);
        CollectionAssert.AreEqual(new[] { nas.Address }, invitation.Addresses.ToArray());
        Assert.IsTrue(invitation.AllowTwoWay);

        var redeemed = await desk.Grants.RequestAccessAsync(
            new DataSyncAccessRequestInput("node-nas", nas.Address, invitation.Code, DataSyncRequestIntent.TwoWay), default);
        Assert.AreEqual(("granted", NodeDataSyncReadBack.Started), (redeemed.Outcome, redeemed.ReadBack));
        CollectionAssert.Contains(desk.Events.Raised.ToArray(), "outbound node-nas");
        CollectionAssert.Contains(nas.Events.Raised.ToArray(), "inbound node-desk TwoWay True");

        // The code's creator reads the redeemer back, asking only to follow it.
        await nas.WaitForEventAsync("outbound node-desk");
        await desk.WaitForEventAsync("inbound node-nas Follow False");
        Assert.IsTrue(await nas.Grants.HasOutboundGrantAsync("node-desk", default));
    }

    [TestMethod]
    public async Task ATwoWayRedemptionOfACodeWithoutConsentIsDeclined()
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS");
        await nas.Grants.SetSharingEnabledAsync(true, false, default);
        await desk.Grants.SetSharingEnabledAsync(true, false, default);
        var invitation = await nas.Grants.CreateInvitationAsync(new DataSyncInvitationInput(false), default);

        var redeemed = await desk.Grants.RequestAccessAsync(
            new DataSyncAccessRequestInput(null, nas.Address, invitation.Code, DataSyncRequestIntent.TwoWay), default);

        Assert.AreEqual(("granted", NodeDataSyncReadBack.Declined), (redeemed.Outcome, redeemed.ReadBack));
        CollectionAssert.AreEqual(new[] { "inbound node-desk TwoWay False" }, nas.Events.Raised.ToArray());
        Assert.IsFalse(await nas.Grants.HasOutboundGrantAsync("node-desk", default));
        Assert.AreEqual(0, (await desk.Grants.GetGrantsAsync(default)).Count);
    }

    [TestMethod]
    public async Task ADeviceThatCannotTakePartSaysWhyBeforeAnythingIsFiled()
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS");
        await using var old = await DataSyncNodeHost.StartAsync("node-old", "Old NAS", dataSync: false);
        Task Ask(DataSyncNodeHost target, string? code = null) => desk.Grants.RequestAccessAsync(
            new DataSyncAccessRequestInput(null, target.Address, code, DataSyncRequestIntent.Follow), default);

        Assert.AreEqual(DataSyncPeerErrorCode.PeerSharingOff, (await Peer(Ask(nas))).Code, "Nothing shared: info says no.");
        await nas.Peers.SetSharingAsync(true);
        Assert.AreEqual(DataSyncPeerErrorCode.PeerSharingOff, (await Peer(Ask(nas))).Code, "Only the library shared.");
        await old.Peers.SetSharingAsync(true);
        Assert.AreEqual(DataSyncPeerErrorCode.PeerTooOld, (await Peer(Ask(old))).Code);
        await nas.Grants.SetSharingEnabledAsync(true, false, default);
        nas.Remote.Mode = RemoteAccessMode.Disabled;
        Assert.AreEqual(DataSyncPeerErrorCode.PeerRemoteAccessOff, (await Peer(Ask(nas))).Code);
        nas.Remote.Mode = RemoteAccessMode.Enabled;
        var problem = await Assert.ThrowsExactlyAsync<DataSyncProblemException>(() => Ask(nas, "00000000"));
        Assert.AreEqual(DataSyncProblemCode.InvitationInvalid, problem.Problem.Code);
        var unreachable = $"http://127.0.0.1:{LoopbackPortAllocator.Allocate(49000)}";
        Assert.AreEqual(DataSyncPeerErrorCode.Unreachable, (await Peer(desk.Grants.RequestAccessAsync(
            new DataSyncAccessRequestInput(null, unreachable, null, DataSyncRequestIntent.Follow), default))).Code);

        Assert.AreEqual(0, (await nas.Grants.GetRequestsAsync(default)).Count);
        Assert.AreEqual(0, (await desk.Grants.GetRequestsAsync(default)).Count);
    }

    private static async Task<DataSyncPeerException> Peer(Task task) =>
        await Assert.ThrowsExactlyAsync<DataSyncPeerException>(() => task);

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString();
    }
}
