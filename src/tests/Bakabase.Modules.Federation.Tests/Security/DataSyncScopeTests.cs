using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Security;

/// <summary>
/// <c>datasync.read</c> as a grant of its own (§7.1): its switch and leases, its lifecycle next to library access
/// (the §7.1.4 table, row by row), its pairing by request, by code and by read-back (§7.2), and a state file an older
/// build reads without ever seeing it.
/// </summary>
[TestClass]
public sealed class DataSyncScopeTests
{
    private const string LibraryQuery = "/federation/v1/export/queries";

    // ---- principals and switches (§7.1.2) -------------------------------------------------------------------------

    [TestMethod]
    public async Task APrincipalCarriesItsGrantsScopeAndTheHandshakeSaysWhichOne()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var library = await source.GrantLibraryAsync(network.Add("library-reader"));
        var dataSync = await source.GrantDataSyncAsync(network.Add("datasync-reader"));

        Assert.AreEqual(FederationScopes.LibraryRead, (await Authenticate(source, library)).Scope);
        Assert.AreEqual(FederationScopes.DataSyncRead, (await Authenticate(source, dataSync)).Scope);
        Assert.AreEqual(FederationScopes.LibraryRead,
            (await source.Grants.CreateHandshakeAsync(library.GrantId, NodeRequestSignature.RandomToken())).Scope);
        Assert.AreEqual(FederationScopes.DataSyncRead,
            (await source.Grants.CreateHandshakeAsync(dataSync.GrantId, NodeRequestSignature.RandomToken())).Scope);
        Assert.AreEqual("GrantRevoked", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            source.Grants.CreateHandshakeAsync("unknown-grant", NodeRequestSignature.RandomToken()))).ErrorCode);
    }

    /// <summary>G28: the library's export services refuse a datasync grant themselves, behind the gate.</summary>
    [TestMethod]
    public async Task TheLibraryExportServicesRefuseADataSyncGrantAsScopeNotGranted()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var library = await source.GrantLibraryAsync(network.Add("library-reader"));
        var dataSync = await source.GrantDataSyncAsync(network.Add("datasync-reader"));

        var refused = await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            source.Grants.ValidateAsync(dataSync.GrantId, dataSync.LibraryEpoch));
        Assert.AreEqual("ScopeNotGranted", refused.ErrorCode);
        Assert.AreEqual(403, refused.StatusCode);
        Assert.AreEqual(FederationScopes.LibraryRead,
            (await source.Grants.ValidateAsync(library.GrantId, library.LibraryEpoch)).Scope);
    }

    [TestMethod]
    public async Task EachGrantAnswersOnlyToItsOwnSwitch()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var library = await source.GrantLibraryAsync(network.Add("library-reader"));
        var dataSync = await source.GrantDataSyncAsync(network.Add("datasync-reader"));

        await source.Peers.SetSharingAsync(false);
        Assert.AreEqual("SharingDisabled", await ErrorOf(Authenticate(source, library)));
        Assert.AreEqual(FederationScopes.DataSyncRead, (await Authenticate(source, dataSync)).Scope);

        await source.Peers.SetSharingAsync(true);
        await source.Peers.SetDataSyncSharingAsync(false);
        Assert.AreEqual("DataSyncSharingDisabled", await ErrorOf(Authenticate(source, dataSync)));
        Assert.AreEqual(FederationScopes.LibraryRead, (await Authenticate(source, library)).Scope);

        // With nothing shared, an unknown grant is told so, as before there were two kinds.
        await source.Peers.SetSharingAsync(false);
        Assert.AreEqual("SharingDisabled", await ErrorOf(source.Grants.CreateHandshakeAsync("unknown-grant",
            NodeRequestSignature.RandomToken())));
    }

    // ---- the switch and leases (§7.1.3) ---------------------------------------------------------------------------

    [TestMethod]
    public async Task LeasesArePerGrantSoOneSwitchNeverCancelsTheOthersReads()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var library = await source.GrantLibraryAsync(network.Add("library-reader"));
        var dataSync = await source.GrantDataSyncAsync(network.Add("datasync-reader"));
        var libraryLease = source.Leases.GetCancellationToken(library.GrantId);
        var dataSyncLease = source.Leases.GetCancellationToken(dataSync.GrantId);
        var outbound = source.Leases.GetCancellationToken(GrantLeaseRegistry.OutboundKey("some-outbound-grant"));

        await source.Peers.SetSharingAsync(false);
        Assert.IsTrue(libraryLease.IsCancellationRequested);
        Assert.IsFalse(dataSyncLease.IsCancellationRequested, "Library sharing off never touches a definitions read.");

        await source.Peers.SetSharingAsync(true);
        libraryLease = source.Leases.GetCancellationToken(library.GrantId);
        Assert.IsFalse(libraryLease.IsCancellationRequested);
        await source.Peers.SetDataSyncSharingAsync(false);
        Assert.IsTrue(dataSyncLease.IsCancellationRequested);
        Assert.IsFalse(libraryLease.IsCancellationRequested, "Definitions sharing off never touches a library read.");
        await source.Peers.SetDataSyncSharingAsync(true);
        Assert.IsFalse(source.Leases.GetCancellationToken(dataSync.GrantId).IsCancellationRequested);
        Assert.IsFalse(outbound.IsCancellationRequested);
    }

    /// <summary>
    /// The remote-mode monitor cancels every inbound lease when remote access is closed and resumes them all on any
    /// other change. A resumed lease never re-opens a switched-off scope: the switch is checked on every read.
    /// </summary>
    [TestMethod]
    public async Task TheRemoteModeMonitorCancelsBothAndItsResumeNeverReopensASwitchedOffScope()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var library = await source.GrantLibraryAsync(network.Add("library-reader"));
        var dataSync = await source.GrantDataSyncAsync(network.Add("datasync-reader"));
        var libraryLease = source.Leases.GetCancellationToken(library.GrantId);
        var dataSyncLease = source.Leases.GetCancellationToken(dataSync.GrantId);

        source.Leases.CancelInbound();
        Assert.IsTrue(libraryLease.IsCancellationRequested);
        Assert.IsTrue(dataSyncLease.IsCancellationRequested);
        Assert.AreEqual("GrantRevoked", await ErrorOf(Authenticate(source, dataSync)));

        await source.Peers.SetDataSyncSharingAsync(false);
        source.Leases.Resume();
        Assert.AreEqual(FederationScopes.LibraryRead, (await Authenticate(source, library)).Scope);
        Assert.AreEqual("DataSyncSharingDisabled", await ErrorOf(Authenticate(source, dataSync)));
        await source.Peers.SetDataSyncSharingAsync(true);
        Assert.AreEqual(FederationScopes.DataSyncRead, (await Authenticate(source, dataSync)).Scope);
    }

    // ---- lifecycle (§7.1.4), row by row ---------------------------------------------------------------------------

    [TestMethod]
    public async Task RevokeAsyncRevokesExactlyTheGrantItNamesOfEitherKind()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        var library = await source.GrantLibraryAsync(reader);
        var dataSync = await source.GrantDataSyncAsync(reader);
        var dataSyncLease = source.Leases.GetCancellationToken(dataSync.GrantId);

        await source.Peers.RevokeAsync(dataSync.GrantId);
        Assert.IsTrue(dataSyncLease.IsCancellationRequested);
        Assert.AreEqual("GrantRevoked", await ErrorOf(Authenticate(source, dataSync)));
        Assert.AreEqual(FederationScopes.LibraryRead, (await Authenticate(source, library)).Scope);

        dataSync = await source.GrantDataSyncAsync(reader);
        await source.Peers.RevokeAsync(library.GrantId);
        Assert.AreEqual("GrantRevoked", await ErrorOf(Authenticate(source, library)));
        Assert.AreEqual(FederationScopes.DataSyncRead, (await Authenticate(source, dataSync)).Scope);
    }

    [TestMethod]
    public async Task RevokeDataSyncAsyncEndsTheSubjectsDataSyncGrantsOnly()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        var other = network.Add("other");
        var library = await source.GrantLibraryAsync(reader);
        var dataSync = await source.GrantDataSyncAsync(reader);
        var otherDataSync = await source.GrantDataSyncAsync(other);
        var lease = source.Leases.GetCancellationToken(dataSync.GrantId);

        await source.Peers.RevokeDataSyncAsync("reader");

        Assert.IsTrue(lease.IsCancellationRequested);
        Assert.AreEqual("GrantRevoked", await ErrorOf(Authenticate(source, dataSync)));
        Assert.AreEqual(FederationScopes.LibraryRead, (await Authenticate(source, library)).Scope);
        Assert.AreEqual(FederationScopes.DataSyncRead, (await Authenticate(source, otherDataSync)).Scope);
        var view = (await source.Peers.GetStatusAsync()).Peers.Single(p => p.NodeId == "reader");
        Assert.IsNull(view.InboundDataSyncGrant);
        Assert.AreEqual(library.GrantId, view.InboundGrant?.GrantId);
    }

    /// <summary>G26 and G27: approving one kind of access neither issues nor revokes the other.</summary>
    [TestMethod]
    public async Task ApprovingOneKindNeverIssuesOrRevokesTheOther()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        var library = await source.GrantLibraryAsync(reader);
        var dataSync = await source.GrantDataSyncAsync(reader);

        // A second datasync approval replaces the datasync grant only.
        var replaced = await source.GrantDataSyncAsync(reader);
        Assert.AreNotEqual(dataSync.GrantId, replaced.GrantId);
        Assert.AreEqual("GrantRevoked", await ErrorOf(Authenticate(source, dataSync)));
        Assert.AreEqual(FederationScopes.LibraryRead, (await Authenticate(source, library)).Scope);

        // A second library approval replaces the library grant only.
        var libraryAgain = await source.GrantLibraryAsync(reader);
        Assert.AreEqual("GrantRevoked", await ErrorOf(Authenticate(source, library)));
        Assert.AreEqual(FederationScopes.LibraryRead, (await Authenticate(source, libraryAgain)).Scope);
        Assert.AreEqual(FederationScopes.DataSyncRead, (await Authenticate(source, replaced)).Scope);

        var state = source.ReadState();
        Assert.AreEqual(2, state.GetProperty("inboundGrants").EnumerateObject().Count());
        Assert.AreEqual(2, state.GetProperty("inboundDataSyncGrants").EnumerateObject().Count());
    }

    [TestMethod]
    public async Task ADataSyncApprovalAddsAnUnknownPeerButNeverRenamesAKnownOne()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        await reader.Store.SetDisplayNameAsync("Reader PC");
        await source.GrantDataSyncAsync(reader);
        Assert.AreEqual("Reader PC", (await source.Peers.GetStatusAsync()).Peers.Single().Label);

        // A request under the same NodeId claiming another name leaves the known name as it is.
        await reader.Store.SetDisplayNameAsync("Someone else");
        await source.GrantDataSyncAsync(reader);
        Assert.AreEqual("Reader PC", (await source.Peers.GetStatusAsync()).Peers.Single().Label);
        // Unlike a library approval, which follows the requester's name (F69).
        await source.GrantLibraryAsync(reader);
        Assert.AreEqual("Someone else", (await source.Peers.GetStatusAsync()).Peers.Single().Label);
    }

    [TestMethod]
    public async Task RemovingAPeerEndsBothKindsBothWaysWithEveryRequestAndCode()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        await source.GrantLibraryAsync(reader);
        await source.GrantDataSyncAsync(reader);
        await reader.GrantDataSyncAsync(source);
        await reader.GrantLibraryAsync(source);
        await source.Peers.CreateDataSyncReciprocalInvitationAsync("reader");
        // A waiting request each way.
        await reader.Pairing.ConnectDataSyncAsync("http://source", null, NodeDataSyncIntents.TwoWay,
            DataSyncTestNetwork.Contract, ["http://reader"]);
        await source.Pairing.ConnectDataSyncAsync("http://reader", null, NodeDataSyncIntents.Follow,
            DataSyncTestNetwork.Contract);
        var outboundDataSync = source.OutboundDataSync("reader")!;
        var outboundLease = source.Leases.GetCancellationToken(GrantLeaseRegistry.OutboundKey(outboundDataSync.GrantId));

        await source.Peers.RemovePeerAsync("reader");

        Assert.IsTrue(outboundLease.IsCancellationRequested);
        var state = source.ReadState();
        Assert.IsFalse(state.GetProperty("peers").TryGetProperty("reader", out _));
        Assert.IsFalse(state.GetProperty("outboundGrants").TryGetProperty("reader", out _));
        Assert.IsFalse(state.GetProperty("outboundDataSyncGrants").TryGetProperty("reader", out _));
        Assert.IsTrue(state.GetProperty("inboundGrants").EnumerateObject().All(g => g.Value.GetProperty("revoked").GetBoolean()));
        Assert.IsTrue(state.GetProperty("inboundDataSyncGrants").EnumerateObject().All(g => g.Value.GetProperty("revoked").GetBoolean()));
        Assert.AreEqual(0, state.GetProperty("incomingDataSyncRequests").GetArrayLength());
        Assert.AreEqual(0, state.GetProperty("outgoingDataSyncRequests").GetArrayLength());
        Assert.AreEqual(0, state.GetProperty("dataSyncReciprocalInvitations").GetArrayLength());
    }

    [TestMethod]
    public async Task ForgettingOneKindOfOutboundAccessLeavesTheOtherAndTheBrowsingSwitch()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        await source.GrantLibraryAsync(reader);
        await source.GrantDataSyncAsync(reader);

        // "Done — stop reading X": datasync credentials go; library access and Enabled stay.
        await reader.Peers.ForgetOutboundDataSyncAsync("source");
        Assert.IsNull(reader.OutboundDataSync("source"));
        Assert.IsNotNull(reader.Outbound("source"));
        Assert.IsTrue(reader.Peer("source").GetProperty("enabled").GetBoolean());

        // "Stop browsing": library credentials go and Enabled turns off; datasync access stays.
        await source.GrantDataSyncAsync(reader);
        await reader.Peers.ForgetOutboundAsync("source");
        Assert.IsNull(reader.Outbound("source"));
        Assert.IsNotNull(reader.OutboundDataSync("source"));
        Assert.IsFalse(reader.Peer("source").GetProperty("enabled").GetBoolean());
        var view = (await reader.Peers.GetStatusAsync()).Peers.Single();
        Assert.IsNull(view.OutboundGrant);
        Assert.AreEqual(reader.OutboundDataSync("source")!.GrantId, view.OutboundDataSyncGrant?.GrantId);
    }

    [TestMethod]
    public async Task RotatingTheLibraryEpochEndsDefinitionsAccessToo()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        var dataSync = await source.GrantDataSyncAsync(reader);
        await source.Peers.IssueDataSyncInvitationAsync(allowTwoWay: true);
        await source.Peers.CreateDataSyncReciprocalInvitationAsync("reader");
        await network.Add("asker").Pairing.ConnectDataSyncAsync("http://source", null, NodeDataSyncIntents.Follow,
            DataSyncTestNetwork.Contract);
        await reader.Peers.SetDataSyncSharingAsync(true);
        var outbound = await reader.GrantDataSyncAsync(source);
        var lease = source.Leases.GetCancellationToken(dataSync.GrantId);

        await source.Peers.RotateLibraryEpochAsync();

        Assert.IsTrue(lease.IsCancellationRequested);
        var state = source.ReadState();
        Assert.IsFalse(state.GetProperty("dataSyncSharingEnabled").GetBoolean());
        Assert.IsTrue(state.GetProperty("inboundDataSyncGrants").EnumerateObject().All(g => g.Value.GetProperty("revoked").GetBoolean()));
        Assert.AreEqual(0, state.GetProperty("incomingDataSyncRequests").GetArrayLength());
        Assert.AreEqual(JsonValueKind.Null, state.GetProperty("dataSyncInvitation").ValueKind);
        Assert.AreEqual(0, state.GetProperty("dataSyncReciprocalInvitations").GetArrayLength());
        // What this device may read elsewhere is not its library's identity.
        Assert.AreEqual(outbound, source.OutboundDataSync("reader"));
        await source.Peers.SetDataSyncSharingAsync(true);
        Assert.AreEqual("GrantRevoked", await ErrorOf(Authenticate(source, dataSync)));
    }

    [TestMethod]
    public async Task ResettingAsANewNodeStartsWithNoDefinitionsAccess()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        await source.GrantDataSyncAsync(reader);
        await reader.Peers.SetDataSyncSharingAsync(true);
        await reader.GrantDataSyncAsync(source);

        await source.Peers.ResetAsNewNodeAsync();

        var state = source.ReadState();
        Assert.IsFalse(state.GetProperty("dataSyncSharingEnabled").GetBoolean());
        foreach (var collection in new[] { "inboundDataSyncGrants", "outboundDataSyncGrants" })
            Assert.AreEqual(0, state.GetProperty(collection).EnumerateObject().Count(), collection);
        Assert.AreEqual(0, (await source.Peers.GetDataSyncStatusAsync()).Peers.Count);
    }

    // ---- pairing (§7.2) -------------------------------------------------------------------------------------------

    /// <summary>G5 on the service: a request is filed apart from library requests and claimed on its own route.</summary>
    [TestMethod]
    public async Task ARequestIsFiledApartFromLibraryRequestsAndClaimedByTheLoop()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        await source.Peers.SetDataSyncSharingAsync(true);

        var pending = await reader.Pairing.ConnectDataSyncAsync("http://source", null, NodeDataSyncIntents.TwoWay,
            DataSyncTestNetwork.Contract);
        Assert.AreEqual("awaitingApproval", pending.Outcome);
        Assert.AreEqual("source", pending.PeerNodeId);
        Assert.AreEqual(0, (await source.Peers.GetStatusAsync()).Requests.Count, "Library requests never see it.");
        var incoming = (await source.Peers.GetDataSyncStatusAsync()).Requests.Single();
        Assert.AreEqual(("incoming", "reader", NodeDataSyncIntents.TwoWay), (incoming.Direction, incoming.NodeId, incoming.Intent));
        Assert.AreEqual(0, (await reader.Pairing.ClaimPendingDataSyncAsync()).Count);

        var approval = await source.Peers.ApproveDataSyncAsync(incoming.RequestId);
        Assert.AreEqual(("reader", NodeDataSyncIntents.TwoWay, false), (approval.NodeId, approval.Intent, approval.HasReciprocal));
        CollectionAssert.AreEqual(new[] { "source" }, (await reader.Pairing.ClaimPendingDataSyncAsync()).ToArray());
        Assert.AreEqual(0, (await reader.Pairing.ClaimPendingDataSyncAsync()).Count, "Claimed once.");
        Assert.IsNull(reader.Outbound("source"));
        Assert.AreEqual(approval.NodeId, reader.OutboundDataSync("source")!.SubjectNodeId);
        Assert.IsTrue(network.Requests.Any(r => r.EndsWith("/federation/v1/pair/datasync/claim")));
        Assert.IsFalse(network.Requests.Any(r => r.EndsWith("/federation/v1/pair/request") || r.EndsWith("/federation/v1/pair/claim")));
    }

    /// <summary>G24 and G25: a code is redeemed only on its own kind's route and is not used up on the other.</summary>
    [TestMethod]
    public async Task CodesNeverCrossBetweenLibraryAndDefinitions()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        await source.Peers.SetSharingAsync(true);
        await source.Peers.SetDataSyncSharingAsync(true);
        var libraryCode = (await source.Peers.IssueInvitationAsync()).Code;
        var dataSyncCode = (await source.Peers.IssueDataSyncInvitationAsync(allowTwoWay: false)).Code;

        Assert.AreEqual("InvalidPairingCode", await ErrorOf(reader.Pairing.ConnectDataSyncAsync("http://source",
            libraryCode, NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract)));
        Assert.AreEqual("InvalidPairingCode", await ErrorOf(reader.Pairing.ConnectAsync("http://source", dataSyncCode)));

        Assert.AreEqual("granted", (await reader.Pairing.ConnectAsync("http://source", libraryCode)).Outcome);
        Assert.AreEqual("granted", (await reader.Pairing.ConnectDataSyncAsync("http://source", dataSyncCode,
            NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract)).Outcome);
        Assert.IsNotNull(reader.Outbound("source"));
        Assert.IsNotNull(reader.OutboundDataSync("source"));
        Assert.AreNotEqual(reader.Outbound("source")!.GrantId, reader.OutboundDataSync("source")!.GrantId);
    }

    [TestMethod]
    public async Task ACodeCountsItsWrongAttemptsAndIsSingleUse()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        await source.Peers.SetDataSyncSharingAsync(true);
        var code = (await source.Peers.IssueDataSyncInvitationAsync(allowTwoWay: false)).Code;
        var reader = network.Add("reader");
        for (var i = 0; i < 5; i++)
            Assert.AreEqual("InvalidPairingCode", await ErrorOf(reader.Pairing.ConnectDataSyncAsync("http://source",
                "00000000", NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract)));
        Assert.AreEqual("InvalidPairingCode", await ErrorOf(reader.Pairing.ConnectDataSyncAsync("http://source", code,
            NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract)), "Five wrong attempts use a code up.");

        code = (await source.Peers.IssueDataSyncInvitationAsync(allowTwoWay: false)).Code;
        Assert.AreEqual("granted", (await reader.Pairing.ConnectDataSyncAsync("http://source", code,
            NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract)).Outcome);
        Assert.AreEqual("InvalidPairingCode", await ErrorOf(network.Add("another").Pairing.ConnectDataSyncAsync(
            "http://source", code, NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract)));
    }

    /// <summary>
    /// G34 and G35 on the service: a two-way redemption is read back only when the code's creator consented when it
    /// made the code; otherwise the grant stands alone and the redeemer's offer is dropped.
    /// </summary>
    [TestMethod]
    [DataRow(false, NodeDataSyncIntents.TwoWay, NodeDataSyncReadBack.Declined)]
    [DataRow(true, NodeDataSyncIntents.TwoWay, NodeDataSyncReadBack.Started)]
    [DataRow(true, NodeDataSyncIntents.Follow, null)]
    [DataRow(false, NodeDataSyncIntents.Follow, null)]
    public async Task ATwoWayCodeIsReadBackOnlyWithItsCreatorsConsent(bool allowTwoWay, string intent, string? readBack)
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        await source.Peers.SetDataSyncSharingAsync(true);
        await reader.Peers.SetDataSyncSharingAsync(true);
        var code = (await source.Peers.IssueDataSyncInvitationAsync(allowTwoWay)).Code;

        var outcome = await reader.Pairing.ConnectDataSyncAsync("http://source", code, intent,
            DataSyncTestNetwork.Contract, intent == NodeDataSyncIntents.TwoWay ? ["http://reader"] : null);

        Assert.AreEqual("granted", outcome.Outcome);
        Assert.AreEqual(readBack, outcome.ReadBack);
        Assert.AreEqual(("reader", intent, readBack), source.Redeemed.Single());
        var offer = await source.Peers.TakeDataSyncReciprocalOfferAsync("reader");
        Assert.AreEqual(readBack == NodeDataSyncReadBack.Started, offer != null, "Only a consented read-back keeps the offer.");
        if (offer != null)
            Assert.AreEqual("granted", (await source.Pairing.ConnectDataSyncAsync(offer.Addresses.Single(), offer.Code,
                NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract, expectedNodeId: "reader")).Outcome);
        // No reciprocal code was redeemed without consent.
        Assert.AreEqual(readBack == NodeDataSyncReadBack.Started, source.OutboundDataSync("reader") != null);
    }

    /// <summary>§7.2.4: one approval reads a two-way requester back with a single-use code bound to the approver.</summary>
    [TestMethod]
    public async Task OneApprovalReadsATwoWayRequesterBackWithASingleUseCodeBoundToTheApprover()
    {
        using var network = new DataSyncTestNetwork();
        var initiator = network.Add("initiator");
        var approver = network.Add("approver");
        var stranger = network.Add("stranger");
        await initiator.Peers.SetDataSyncSharingAsync(true);
        await approver.Peers.SetDataSyncSharingAsync(true);
        await stranger.Peers.SetDataSyncSharingAsync(true);

        var pending = await initiator.Pairing.ConnectDataSyncAsync("http://approver", null, NodeDataSyncIntents.TwoWay,
            DataSyncTestNetwork.Contract, ["http://initiator"]);
        var approval = await approver.Peers.ApproveDataSyncAsync(pending.RequestId);
        Assert.IsTrue(approval.HasReciprocal);
        var offer = (await approver.Peers.TakeDataSyncReciprocalOfferAsync("initiator"))!;
        Assert.IsNull(await approver.Peers.TakeDataSyncReciprocalOfferAsync("initiator"), "Taken once.");

        Assert.AreEqual("InvalidPairingCode", await ErrorOf(stranger.Pairing.ConnectDataSyncAsync("http://initiator",
            offer.Code, NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract)), "Bound to the approver's NodeId.");
        Assert.AreEqual("IdentityConflict", await ErrorOf(approver.Pairing.ConnectDataSyncAsync("http://stranger",
            offer.Code, NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract, expectedNodeId: "initiator")));
        var readBack = await approver.Pairing.ConnectDataSyncAsync(offer.Addresses.Single(), offer.Code,
            NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract, expectedNodeId: "initiator");
        Assert.AreEqual(("granted", (string?)null), (readBack.Outcome, readBack.ReadBack));
        Assert.AreEqual(("approver", NodeDataSyncIntents.Follow, (string?)null), initiator.Redeemed.Single(),
            "A reciprocal code grants and is never read back again.");
        Assert.AreEqual("InvalidPairingCode", await ErrorOf(approver.Pairing.ConnectDataSyncAsync("http://initiator",
            offer.Code, NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract)), "Single-use.");

        CollectionAssert.AreEqual(new[] { "approver" }, (await initiator.Pairing.ClaimPendingDataSyncAsync()).ToArray());
        Assert.IsNotNull(initiator.OutboundDataSync("approver"));
        Assert.IsNotNull(approver.OutboundDataSync("initiator"));
        // Neither side gained library access.
        Assert.IsNull(initiator.Outbound("approver"));
        Assert.IsNull(approver.Outbound("initiator"));
    }

    /// <summary>
    /// §7.2.4 step 7: whether the approver reads a two-way requester back travels with the claim, so the requester's
    /// link can say it is not read back. A Follow request, and an approval that says nothing, carry no word.
    /// </summary>
    [TestMethod]
    [DataRow(false, NodeDataSyncReadBack.Declined)]
    [DataRow(true, NodeDataSyncReadBack.Started)]
    public async Task ATwoWayApprovalTellsTheRequesterWhetherItIsReadBack(bool receiveBack, string expected)
    {
        using var network = new DataSyncTestNetwork();
        var initiator = network.Add("initiator");
        var approver = network.Add("approver");
        var follower = network.Add("follower");
        await initiator.Peers.SetDataSyncSharingAsync(true);
        await approver.Peers.SetDataSyncSharingAsync(true);

        var twoWay = await initiator.Pairing.ConnectDataSyncAsync("http://approver", null, NodeDataSyncIntents.TwoWay,
            DataSyncTestNetwork.Contract, ["http://initiator"]);
        Assert.IsNull(twoWay.ReadBack, "Nothing is said before the approval.");
        await approver.Peers.ApproveDataSyncAsync(twoWay.RequestId, receiveBack);
        var claimed = (await initiator.Pairing.ClaimPendingDataSyncOutcomesAsync()).Single();
        Assert.AreEqual(("approver", "granted", expected), (claimed.PeerNodeId, claimed.Outcome, claimed.ReadBack));
        // Claimed again (a crash between the claim and its event), it says the same.
        Assert.AreEqual(expected, (await initiator.Pairing.ClaimDataSyncAsync(twoWay.RequestId)).ReadBack);

        var follow = await follower.Pairing.ConnectDataSyncAsync("http://approver", null, NodeDataSyncIntents.Follow,
            DataSyncTestNetwork.Contract);
        await approver.Peers.ApproveDataSyncAsync(follow.RequestId, receiveBack);
        Assert.IsNull((await follower.Pairing.ClaimPendingDataSyncOutcomesAsync()).Single().ReadBack);
    }

    /// <summary>
    /// §7.2.4: a two-way offer the initiator withdrew — its request cancelled, its reading of the approver stopped, or
    /// the approver's access to it revoked — reads nothing back, though the approver still holds the request that
    /// carried it.
    /// </summary>
    [TestMethod]
    [DataRow("cancel")]
    [DataRow("stopReading")]
    [DataRow("revoke")]
    public async Task AWithdrawnTwoWayOfferReadsNothingBack(string withdrawal)
    {
        using var network = new DataSyncTestNetwork();
        var initiator = network.Add("initiator");
        var approver = network.Add("approver");
        await initiator.Peers.SetDataSyncSharingAsync(true);
        await approver.Peers.SetDataSyncSharingAsync(true);
        var pending = await initiator.Pairing.ConnectDataSyncAsync("http://approver", null, NodeDataSyncIntents.TwoWay,
            DataSyncTestNetwork.Contract, ["http://initiator"]);

        switch (withdrawal)
        {
            case "cancel":
                Assert.IsTrue(await initiator.Peers.CancelOutgoingDataSyncAsync(pending.RequestId));
                break;
            case "stopReading":
                await initiator.Peers.ForgetOutboundDataSyncAsync("approver");
                break;
            default:
                await initiator.Peers.RevokeDataSyncAsync("approver");
                break;
        }
        Assert.AreEqual(0, initiator.ReadState().GetProperty("dataSyncReciprocalInvitations").GetArrayLength());

        var approval = await approver.Peers.ApproveDataSyncAsync(pending.RequestId);
        Assert.IsTrue(approval.HasReciprocal, "The approver's copy of the request still carries the offer.");
        var offer = (await approver.Peers.TakeDataSyncReciprocalOfferAsync("initiator"))!;
        Assert.AreEqual("InvalidPairingCode", await ErrorOf(approver.Pairing.ConnectDataSyncAsync(
            offer.Addresses.Single(), offer.Code, NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract,
            expectedNodeId: "initiator")));
        Assert.AreEqual(0, initiator.Redeemed.Count);
        Assert.AreEqual(0, (await initiator.Peers.GetDataSyncStatusAsync()).Grants.Count);
        Assert.IsNull(approver.OutboundDataSync("initiator"));
    }

    /// <summary>Cancelling one two-way request leaves the offer of another that still waits for the same device.</summary>
    [TestMethod]
    public async Task CancellingOneTwoWayRequestKeepsTheOfferOfAnotherToTheSameDevice()
    {
        using var network = new DataSyncTestNetwork();
        var initiator = network.Add("initiator");
        var approver = network.Add("approver");
        network.Route("approver-2", approver);
        await initiator.Peers.SetDataSyncSharingAsync(true);
        await approver.Peers.SetDataSyncSharingAsync(true);
        var first = await initiator.Pairing.ConnectDataSyncAsync("http://approver", null, NodeDataSyncIntents.TwoWay,
            DataSyncTestNetwork.Contract, ["http://initiator"]);
        var second = await initiator.Pairing.ConnectDataSyncAsync("http://approver-2", null,
            NodeDataSyncIntents.TwoWay, DataSyncTestNetwork.Contract, ["http://initiator"]);
        Assert.AreNotEqual(first.RequestId, second.RequestId);

        Assert.IsTrue(await initiator.Peers.CancelOutgoingDataSyncAsync(first.RequestId));

        await approver.Peers.ApproveDataSyncAsync(second.RequestId);
        var offer = (await approver.Peers.TakeDataSyncReciprocalOfferAsync("initiator"))!;
        Assert.AreEqual("granted", (await approver.Pairing.ConnectDataSyncAsync(offer.Addresses.Single(), offer.Code,
            NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract, expectedNodeId: "initiator")).Outcome);
    }

    /// <summary>
    /// §7.2.4 "Try again": where a two-way requester offered to be read stays once its offer is taken, as a last resort
    /// only. A device this one knows is asked where it is known, never where an unverified offer in its name points.
    /// </summary>
    [TestMethod]
    public async Task WhereARequesterOfferedToBeReadIsAskedOnlyWhenNothingElseIsKnown()
    {
        using var network = new DataSyncTestNetwork();
        var initiator = network.Add("initiator");
        var approver = network.Add("approver");
        network.Route("initiator-2", initiator);
        await initiator.Peers.SetDataSyncSharingAsync(true);
        await approver.Peers.SetDataSyncSharingAsync(true);
        var pending = await initiator.Pairing.ConnectDataSyncAsync("http://approver", null, NodeDataSyncIntents.TwoWay,
            DataSyncTestNetwork.Contract, ["http://initiator", "http://initiator-2"]);
        await approver.Peers.ApproveDataSyncAsync(pending.RequestId);
        Assert.AreEqual(0, (await approver.Peers.GetDataSyncAddressesAsync("initiator")).Count);

        var offer = (await approver.Peers.TakeDataSyncReciprocalOfferAsync("initiator"))!;
        CollectionAssert.AreEqual(new[] { "http://initiator", "http://initiator-2" },
            (await approver.Peers.GetDataSyncAddressesAsync("initiator")).ToArray());
        Assert.IsNull((await approver.Peers.GetDataSyncStatusAsync()).Peers.Single().Address,
            "An offered address is not shown as where the device is.");

        // Read back at the second address: verified, it replaces what was only offered.
        await approver.Pairing.ConnectDataSyncAsync("http://initiator-2", offer.Code, NodeDataSyncIntents.Follow,
            DataSyncTestNetwork.Contract, expectedNodeId: "initiator");
        CollectionAssert.AreEqual(new[] { "http://initiator-2" },
            (await approver.Peers.GetDataSyncAddressesAsync("initiator")).ToArray());
        Assert.AreEqual(JsonValueKind.Null,
            approver.Peer("initiator").GetProperty("dataSyncOfferedAddresses").ValueKind);

        // A later two-way request in the known device's name, from elsewhere, never redirects where it is asked.
        await approver.Peers.SubmitDataSyncRequestAsync(new NodeDataSyncPairRequest("initiator", "Initiator",
            "impostor", NodeRequestSignature.RandomToken(), NodeDataSyncIntents.TwoWay,
            new NodeReciprocalOffer(["http://elsewhere"], NodeRequestSignature.RandomToken())), "10.0.0.66");
        await approver.Peers.ApproveDataSyncAsync("impostor");
        Assert.IsNotNull(await approver.Peers.TakeDataSyncReciprocalOfferAsync("initiator"));
        CollectionAssert.AreEqual(new[] { "http://initiator-2" },
            (await approver.Peers.GetDataSyncAddressesAsync("initiator")).ToArray());
    }

    /// <summary>
    /// This device's own refusals on the way to a peer carry codes no peer answers (§7.6), and nothing is sent: its own
    /// sharing off when a two-way offer is made, and too many offers of its own waiting.
    /// </summary>
    [TestMethod]
    public async Task ARequestersOwnRefusalsAreToldApartFromThePeers()
    {
        using var network = new DataSyncTestNetwork();
        var initiator = network.Add("initiator");
        var approver = network.Add("approver");
        await approver.Peers.SetDataSyncSharingAsync(true);
        Task<NodeDataSyncPairingOutcome> AskBothWays() => initiator.Pairing.ConnectDataSyncAsync("http://approver", null,
            NodeDataSyncIntents.TwoWay, DataSyncTestNetwork.Contract, ["http://initiator"]);

        Assert.AreEqual("LocalDataSyncSharingDisabled", await ErrorOf(AskBothWays()));

        await initiator.Peers.SetDataSyncSharingAsync(true);
        for (var i = 0; i < 64; i++) await initiator.Peers.CreateDataSyncReciprocalInvitationAsync("other-" + i);
        Assert.AreEqual("LocalPairingBusy", await ErrorOf(AskBothWays()));

        Assert.IsFalse(network.Requests.Any(r => r.Contains("/pair/")));
        Assert.AreEqual(0, initiator.ReadState().GetProperty("outgoingDataSyncRequests").GetArrayLength());
    }

    [TestMethod]
    public async Task ARequesterRefusesAPeerThatCannotTakePartBeforeSendingAnything()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        await source.Peers.SetDataSyncSharingAsync(true);
        Task<NodeDataSyncPairingOutcome> Ask() => reader.Pairing.ConnectDataSyncAsync("http://source", null,
            NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract);

        source.ContractVersion = null;
        Assert.AreEqual("PeerTooOld", await ErrorOf(Ask()));
        source.ContractVersion = 0;
        Assert.AreEqual("PeerTooOld", await ErrorOf(Ask()));
        source.ContractVersion = 2;
        source.MinimumPeerContract = 2;
        Assert.AreEqual("ThisTooOld", await ErrorOf(Ask()));
        source.MinimumPeerContract = 1;
        await source.Peers.SetDataSyncSharingAsync(false);
        await source.Peers.SetSharingAsync(true);
        Assert.AreEqual("PeerSharingOff", await ErrorOf(Ask()));
        await source.Peers.SetSharingAsync(false);
        Assert.AreEqual("SharingDisabled", await ErrorOf(Ask()), "With nothing shared, info itself answers no.");

        Assert.IsFalse(network.Requests.Any(r => r.Contains("/pair/")));
        Assert.AreEqual(0, reader.ReadState().GetProperty("outgoingDataSyncRequests").GetArrayLength());
    }

    [TestMethod]
    public async Task DataSyncRequestsAreValidatedAndBoundedLikeLibraryRequests()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        await source.Peers.SetDataSyncSharingAsync(true);
        NodeDataSyncPairRequest Request(int i, string intent = NodeDataSyncIntents.Follow) =>
            new("reader-" + i, "Reader", "request-" + i, NodeRequestSignature.RandomToken(), intent);

        Assert.AreEqual("InvalidPairingRequest", await ErrorOf(source.Peers.SubmitDataSyncRequestAsync(
            Request(0, "library"), "10.0.0.9")));
        Assert.AreEqual("InvalidPairingRequest", await ErrorOf(source.Peers.SubmitDataSyncRequestAsync(
            Request(0) with { NodeId = "source" })));
        for (var i = 0; i < 4; i++) await source.Peers.SubmitDataSyncRequestAsync(Request(i), "10.0.0.9");
        Assert.AreEqual("PairingBusy", await ErrorOf(source.Peers.SubmitDataSyncRequestAsync(Request(4), "10.0.0.9")));
        Assert.AreEqual("PairingRejected", await ErrorOf(source.Peers.SubmitDataSyncRequestAsync(
            Request(0) with { NodeId = "impostor" }, "10.0.0.8")));

        // Rejecting frees the address's slot; approving a rejected request is refused.
        Assert.IsTrue(await source.Peers.RejectDataSyncAsync("request-0"));
        Assert.IsFalse(await source.Peers.RejectDataSyncAsync("no-such-request"));
        await source.Peers.SubmitDataSyncRequestAsync(Request(5), "10.0.0.9");
        Assert.AreEqual("RequestNotFound", await ErrorOf(source.Peers.ApproveDataSyncAsync("request-0")));
        Assert.AreEqual("RequestNotFound", await ErrorOf(source.Peers.ApproveDataSyncAsync("no-such-request")));

        await source.Peers.SetDataSyncSharingAsync(false);
        Assert.AreEqual("DataSyncSharingDisabled", await ErrorOf(source.Peers.ApproveDataSyncAsync("request-1")));
        Assert.AreEqual("DataSyncSharingDisabled", await ErrorOf(source.Peers.SubmitDataSyncRequestAsync(Request(6))));
        Assert.AreEqual("DataSyncSharingDisabled", await ErrorOf(source.Peers.IssueDataSyncInvitationAsync(false)));
        Assert.AreEqual("DataSyncSharingDisabled", await ErrorOf(source.Peers.CreateDataSyncReciprocalInvitationAsync("reader-1")));
        // Library pairing is not what datasync sharing gates.
        Assert.AreEqual("SharingDisabled", await ErrorOf(source.Peers.IssueInvitationAsync()));
    }

    // ---- downgrade safety (§7.1.1) ----------------------------------------------------------------------------------

    /// <summary>What a person actually does (grants, requests, codes) is invisible to a build from before data sync.</summary>
    [TestMethod]
    public async Task DefinitionsAccessMadeByPairingIsInvisibleToAnOlderBuild()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        var library = await source.GrantLibraryAsync(network.Add("library-reader"));
        await source.GrantDataSyncAsync(reader);
        await reader.Peers.SetDataSyncSharingAsync(true);
        await reader.GrantDataSyncAsync(source);
        await source.Peers.IssueDataSyncInvitationAsync(allowTwoWay: true);
        await source.Peers.CreateDataSyncReciprocalInvitationAsync("reader");
        await network.Add("asker").Pairing.ConnectDataSyncAsync("http://source", null, NodeDataSyncIntents.Follow,
            DataSyncTestNetwork.Contract);

        var older = JsonSerializer.Deserialize<PreDataSyncState>(source.ReadState().GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        CollectionAssert.AreEquivalent(new[] { library.GrantId }, older.InboundGrants.Keys.ToArray());
        Assert.AreEqual(0, older.OutboundGrants.Count);
        // Only the library code's own transaction.
        CollectionAssert.AreEqual(new[] { "library-reader" }, older.IncomingRequests.Concat(older.OutgoingRequests)
            .Select(r => r.GetProperty("nodeId").GetString()).ToArray());
        Assert.IsNull(older.Invitation);
        Assert.AreEqual(0, older.ReciprocalInvitations.Count);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    private static Task<NodePrincipal> Authenticate(DataSyncTestNode source, NodeCredentials credentials,
        string path = LibraryQuery) =>
        source.Auth.AuthenticateAsync(NodeRequestSignature.Create(credentials, "POST", path, "",
            NodeRequestSignature.Hash([]), source.Clock.GetUtcNow()), "POST", path, "", NodeRequestSignature.Hash([]));

    private static async Task<string> ErrorOf(Task task)
    {
        var e = await Assert.ThrowsExactlyAsync<FederationAccessException>(() => task);
        return e.ErrorCode;
    }

    /// <summary><c>FederationState</c>'s members as a build from before data sync declares them.</summary>
    private sealed class PreDataSyncState
    {
        public Dictionary<string, JsonElement> InboundGrants { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> OutboundGrants { get; set; } = new(StringComparer.Ordinal);
        public List<JsonElement> IncomingRequests { get; set; } = [];
        public List<JsonElement> OutgoingRequests { get; set; } = [];
        public JsonNode? Invitation { get; set; }
        public List<JsonElement> ReciprocalInvitations { get; set; } = [];
    }
}
