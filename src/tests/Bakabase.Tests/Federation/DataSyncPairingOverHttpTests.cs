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
/// (§7.2): a two-way request approved with read-back, a code made with two-way consent, a read-back that fails, the
/// claim loop, what data sync is told and in which order, and how a device that cannot take part says so.
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
        // The grant first, then how the read-back it announced went.
        CollectionAssert.AreEqual(new[] { "inbound node-desk TwoWay True", "outbound node-desk" }, nas.Events.Raised.ToArray());
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

        // Approving again reads nothing back and reports no failure: the NAS already reads the desk. Data sync hears
        // of the grant and of the access it announced again, in the same order, which changes nothing for a link it
        // already has.
        var again = await nas.Grants.ApproveAsync(request.RequestId, readBack: true, default);
        Assert.AreEqual((true, (string?)null), (again.ReadBackGranted, again.ReadBackError));
        CollectionAssert.AreEqual(
            new[]
            {
                "inbound node-desk TwoWay True", "outbound node-desk", "inbound node-desk TwoWay True",
                "outbound node-desk"
            },
            nas.Events.Raised.ToArray());
        Assert.AreEqual(1, (await nas.Grants.GetGrantsAsync(default)).Count);
    }

    /// <summary>
    /// §7.2.4 and N14 over HTTP: when the approver cannot read the requester back — nothing answers where the requester
    /// said it could be read, or it stopped sharing meanwhile — the requester still gets its access, and data sync
    /// hears of the grant and then why the approver's link waits for access.
    /// </summary>
    [TestMethod]
    [DataRow(false, "Unreachable")]
    [DataRow(true, "PeerSharingOff")]
    public async Task AFailedReadBackLeavesTheGrantAndSaysWhy(bool reachable, string expected)
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS");
        await desk.Grants.SetSharingEnabledAsync(true, false, default);
        await nas.Grants.SetSharingEnabledAsync(true, false, default);
        if (!reachable) desk.Remote.Addresses = [$"http://127.0.0.1:{LoopbackPortAllocator.Allocate(49000)}"];
        await desk.Grants.RequestAccessAsync(
            new DataSyncAccessRequestInput("node-nas", nas.Address, null, DataSyncRequestIntent.TwoWay), default);
        if (reachable) await desk.Grants.SetSharingEnabledAsync(false, false, default);
        var request = (await nas.Grants.GetRequestsAsync(default)).Single();

        var approval = await nas.Grants.ApproveAsync(request.RequestId, readBack: true, default);

        Assert.AreEqual((DataSyncRequestIntent.TwoWay, false, expected),
            (approval.Intent, approval.ReadBackGranted, approval.ReadBackError));
        CollectionAssert.AreEqual(new[] { "inbound node-desk TwoWay True", $"readBackFailed node-desk {expected}" },
            nas.Events.Raised.ToArray());
        Assert.IsFalse(await nas.Grants.HasOutboundGrantAsync("node-desk", default));
        Assert.AreEqual("node-desk", (await nas.Grants.GetGrantsAsync(default)).Single().NodeId);

        // The requester collects the access it asked for all the same.
        await desk.Flow.ClaimPendingAsync(default);
        CollectionAssert.AreEqual(new[] { "outbound node-nas" }, desk.Events.Raised.ToArray());
        Assert.IsTrue(await desk.Grants.HasOutboundGrantAsync("node-nas", default));
        Assert.AreEqual(0, (await desk.Grants.GetGrantsAsync(default)).Count);
    }

    /// <summary>
    /// §7.2.4 and N14 when the requester's offered address accepts connections and never answers: the read-back gives
    /// up as unreachable at the client's own deadline, after the grant, and data sync hears it. The page that approved
    /// going away meanwhile changes nothing: the offer was taken, so the read-back goes on to its end.
    /// </summary>
    [TestMethod]
    public async Task AReadBackNobodyAnswersFailsAsUnreachableEvenWhenTheApproverLeaves()
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS");
        await desk.Grants.SetSharingEnabledAsync(true, false, default);
        await nas.Grants.SetSharingEnabledAsync(true, false, default);
        await desk.Grants.RequestAccessAsync(
            new DataSyncAccessRequestInput("node-nas", nas.Address, null, DataSyncRequestIntent.TwoWay), default);
        var request = (await nas.Grants.GetRequestsAsync(default)).Single();
        desk.Answer = DataSyncNodeHost.Silent;

        using var page = new CancellationTokenSource();
        var approving = nas.Grants.ApproveAsync(request.RequestId, readBack: true, page.Token);
        await nas.WaitForEventAsync("inbound node-desk TwoWay True");
        page.Cancel();
        var approval = await approving;

        Assert.AreEqual((DataSyncRequestIntent.TwoWay, false, "Unreachable"),
            (approval.Intent, approval.ReadBackGranted, approval.ReadBackError));
        CollectionAssert.AreEqual(new[] { "inbound node-desk TwoWay True", "readBackFailed node-desk Unreachable" },
            nas.Events.Raised.ToArray());
        Assert.AreEqual("node-desk", (await nas.Grants.GetGrantsAsync(default)).Single().NodeId);
        Assert.IsFalse(await nas.Grants.HasOutboundGrantAsync("node-desk", default));

        // The requester collects the access it asked for all the same.
        desk.Answer = null;
        await desk.Flow.ClaimPendingAsync(default);
        CollectionAssert.AreEqual(new[] { "outbound node-nas" }, desk.Events.Raised.ToArray());
    }

    /// <summary>
    /// A device that accepts connections and never answers: asking it for its definitions is unreachable (a data sync
    /// error, not a cancellation nobody asked for), and it holds up no other request of the claim round.
    /// </summary>
    [TestMethod]
    public async Task ADeviceThatNeverAnswersIsUnreachableAndHoldsUpNoOtherClaim()
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS");
        await using var pc = await DataSyncNodeHost.StartAsync("node-pc", "PC");
        await nas.Grants.SetSharingEnabledAsync(true, false, default);
        await pc.Grants.SetSharingEnabledAsync(true, false, default);
        Task<DataSyncAccessRequestOutcome> Ask(DataSyncNodeHost target) => desk.Grants.RequestAccessAsync(
            new DataSyncAccessRequestInput(null, target.Address, null, DataSyncRequestIntent.Follow), default);
        // The PC is asked first, so its request is the first the claim round meets.
        await Ask(pc);
        await Ask(nas);
        await nas.Grants.ApproveAsync((await nas.Grants.GetRequestsAsync(default)).Single().RequestId, false, default);
        pc.Answer = DataSyncNodeHost.Silent;

        var asking = Peer(Ask(pc));
        await desk.Flow.ClaimPendingAsync(default);
        var unanswered = await asking;

        Assert.AreEqual((DataSyncPeerErrorCode.Unreachable, "timeout"), (unanswered.Code, unanswered.Message));
        CollectionAssert.AreEqual(new[] { "outbound node-nas" }, desk.Events.Raised.ToArray());
        Assert.IsFalse(await desk.Grants.HasOutboundGrantAsync("node-pc", default));

        // Its request still waits, and is claimed once it answers again.
        pc.Answer = null;
        await pc.Grants.ApproveAsync((await pc.Grants.GetRequestsAsync(default)).Single().RequestId, false, default);
        await desk.Flow.ClaimPendingAsync(default);
        CollectionAssert.AreEqual(new[] { "outbound node-nas", "outbound node-pc" }, desk.Events.Raised.ToArray());
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

    /// <summary>
    /// A code made with two-way consent answers "started" at once. When the reading back then fails, data sync hears of
    /// it after the grant, so the creator's link can say why it waits for access (N14).
    /// </summary>
    [TestMethod]
    public async Task AFailedReadBackOfARedeemedCodeIsReportedAfterTheGrant()
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS");
        await nas.Grants.SetSharingEnabledAsync(true, false, default);
        await desk.Grants.SetSharingEnabledAsync(true, false, default);
        desk.Remote.Addresses = [$"http://127.0.0.1:{LoopbackPortAllocator.Allocate(49000)}"];
        var invitation = await nas.Grants.CreateInvitationAsync(new DataSyncInvitationInput(true), default);

        var redeemed = await desk.Grants.RequestAccessAsync(
            new DataSyncAccessRequestInput("node-nas", nas.Address, invitation.Code, DataSyncRequestIntent.TwoWay), default);

        Assert.AreEqual(("granted", NodeDataSyncReadBack.Started), (redeemed.Outcome, redeemed.ReadBack));
        await nas.WaitForEventAsync("readBackFailed node-desk Unreachable");
        CollectionAssert.AreEqual(new[] { "inbound node-desk TwoWay True", "readBackFailed node-desk Unreachable" },
            nas.Events.Raised.ToArray());
        Assert.IsFalse(await nas.Grants.HasOutboundGrantAsync("node-desk", default));
        Assert.IsTrue(await desk.Grants.HasOutboundGrantAsync("node-nas", default));
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
