using System.Net;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Tests.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Transport;

/// <summary>
/// Sessions with a <c>datasync.read</c> grant (§7.6) and the routing rule of §7.1.1: a peer's library routing
/// (label, address, library epoch, kind, platform, browsing switch) belongs to library access, and nothing data sync
/// does — pairing, handshakes, relocation — ever rewrites it for a peer with library access in either direction.
/// </summary>
[TestClass]
public sealed class PeerSessionScopeTests
{
    private const string Head = "/federation/v1/export/datasync/head";
    private const string Queries = "/federation/v1/export/queries";

    [TestMethod]
    public async Task ADataSyncSessionUsesItsOwnGrantAndIgnoresTheBrowsingSwitch()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        var library = await source.GrantLibraryAsync(reader);
        var dataSync = await source.GrantDataSyncAsync(reader);
        var sessions = reader.Sessions();
        var transport = reader.Transport(sessions);

        var librarySession = await sessions.GetAsync("source");
        var dataSyncSession = await sessions.GetAsync("source", FederationScopes.DataSyncRead);
        Assert.AreEqual((FederationScopes.LibraryRead, library.GrantId), (librarySession.Scope, librarySession.GrantId));
        Assert.AreEqual((FederationScopes.DataSyncRead, dataSync.GrantId), (dataSyncSession.Scope, dataSyncSession.GrantId));

        // Browsing off: the library session and its transport stop, the datasync ones do not.
        await reader.Peers.SetEnabledAsync("source", false);
        Assert.AreEqual("NodeNotAuthorized", await ErrorOf(sessions.GetAsync("source")));
        Assert.AreEqual(dataSync.GrantId, (await sessions.GetAsync("source", FederationScopes.DataSyncRead)).GrantId);
        Assert.AreEqual("NodeSessionChanged", await ErrorOf(transport.SendAsync(librarySession, HttpMethod.Post, Queries)));
        using (var head = await transport.SendAsync(dataSyncSession, HttpMethod.Get, Head))
            Assert.AreEqual(HttpStatusCode.OK, head.StatusCode);

        // Each grant reaches only its own routes at the source.
        using (var wrongScope = await transport.SendAsync(dataSyncSession, HttpMethod.Post, Queries))
            Assert.AreEqual(HttpStatusCode.Forbidden, wrongScope.StatusCode);

        // Forgetting definitions access ends the datasync session and its transport.
        await reader.Peers.ForgetOutboundDataSyncAsync("source");
        Assert.AreEqual("NodeSessionChanged", await ErrorOf(transport.SendAsync(dataSyncSession, HttpMethod.Get, Head)));
        reader.Clock.Now += TimeSpan.FromMinutes(2);
        Assert.AreEqual("NodeNotAuthorized", await ErrorOf(sessions.GetAsync("source", FederationScopes.DataSyncRead)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            sessions.GetAsync("source", FederationScopes.Any).GetAwaiter().GetResult());
    }

    [TestMethod]
    public async Task LibraryAndDataSyncSessionsAreCachedAndReportedApart()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        await source.GrantLibraryAsync(reader);
        await source.GrantDataSyncAsync(reader);
        var sessions = reader.Sessions();
        await sessions.GetAsync("source");
        await sessions.GetAsync("source", FederationScopes.DataSyncRead);
        Assert.AreEqual("Online", sessions.GetConnectionState("source"));
        Assert.AreEqual("Online", sessions.GetConnectionState("source", FederationScopes.DataSyncRead));

        await source.Peers.RevokeDataSyncAsync("reader");
        reader.Clock.Now += TimeSpan.FromMinutes(2);
        source.Clock.Now += TimeSpan.FromMinutes(2);
        Assert.AreEqual("GrantRevoked", await ErrorOf(sessions.GetAsync("source", FederationScopes.DataSyncRead)));
        Assert.AreEqual("Unauthorized", sessions.GetConnectionState("source", FederationScopes.DataSyncRead));
        Assert.AreEqual("Online", sessions.GetConnectionState("source"));
        Assert.AreEqual(FederationScopes.LibraryRead, (await sessions.GetAsync("source")).Scope);
    }

    /// <summary>A peer this device browses keeps every part of its library routing through a datasync pairing.</summary>
    [TestMethod]
    public async Task DataSyncPairingNeverRewritesThePeerThisDeviceBrowses()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source", self: new Says(ServerKind.Headless, RemoteDevicePlatform.Linux));
        network.Route("source-alt", source);
        var reader = network.Add("reader");
        await source.Store.SetDisplayNameAsync("NAS");
        await source.GrantLibraryAsync(reader);
        await reader.Sessions().GetAsync("source");
        await reader.Peers.SetEnabledAsync("source", false);
        var before = reader.Peer("source").GetRawText();

        await source.Store.SetDisplayNameAsync("Renamed NAS");
        await source.Peers.SetDataSyncSharingAsync(true);
        var code = (await source.Peers.IssueDataSyncInvitationAsync(false)).Code;
        Assert.AreEqual("granted", (await reader.Pairing.ConnectDataSyncAsync("http://source-alt", code,
            NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract)).Outcome);
        await reader.Sessions().GetAsync("source", FederationScopes.DataSyncRead);

        AssertLibraryRoutingUnchanged(before, reader.Peer("source"));
        Assert.AreEqual("http://source-alt", reader.Peer("source").GetProperty("dataSyncAddress").GetString());
        Assert.AreEqual("NAS", reader.Peer("source").GetProperty("label").GetString());
    }

    /// <summary>A peer that browses this device keeps what this device knows of it too.</summary>
    [TestMethod]
    public async Task DataSyncPairingNeverRewritesAPeerThatBrowsesThisDevice()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        // Only the other way: the source reads the reader's library, so the reader knows it without an address.
        await reader.GrantLibraryAsync(source);
        var before = reader.Peer("source").GetRawText();
        Assert.AreEqual(System.Text.Json.JsonValueKind.Null, reader.Peer("source").GetProperty("address").ValueKind);

        await source.Store.SetDisplayNameAsync("Claimed name");
        await source.GrantDataSyncAsync(reader);
        await reader.Sessions().GetAsync("source", FederationScopes.DataSyncRead);

        AssertLibraryRoutingUnchanged(before, reader.Peer("source"));
        Assert.AreEqual("http://source", reader.Peer("source").GetProperty("dataSyncAddress").GetString());
    }

    /// <summary>For a peer only data sync knows, pairing fills what is empty, never what is set.</summary>
    [TestMethod]
    public async Task ForAPeerOnlyDataSyncKnowsPairingFillsWhatIsEmptyAndNeverWhatIsSet()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        network.Route("source-alt", source);
        var reader = network.Add("reader");
        await source.Store.SetDisplayNameAsync("NAS");
        await source.GrantDataSyncAsync(reader);
        var peer = reader.Peer("source");
        Assert.AreEqual("NAS", peer.GetProperty("label").GetString());
        Assert.AreEqual("http://source", peer.GetProperty("address").GetString());
        Assert.AreEqual("http://source", peer.GetProperty("dataSyncAddress").GetString());
        var epoch = peer.GetProperty("libraryEpoch").GetString();
        Assert.AreEqual((await source.Identity.GetAsync()).LibraryEpoch, epoch);

        await source.Store.SetDisplayNameAsync("Claimed name");
        var code = (await source.Peers.IssueDataSyncInvitationAsync(false)).Code;
        await reader.Pairing.ConnectDataSyncAsync("http://source-alt", code, NodeDataSyncIntents.Follow,
            DataSyncTestNetwork.Contract);
        peer = reader.Peer("source");
        Assert.AreEqual("NAS", peer.GetProperty("label").GetString());
        Assert.AreEqual("http://source", peer.GetProperty("address").GetString());
        Assert.AreEqual("http://source-alt", peer.GetProperty("dataSyncAddress").GetString());
        Assert.IsTrue(peer.GetProperty("enabled").GetBoolean());
    }

    /// <summary>
    /// A verified datasync handshake follows the name, kind and platform of a peer only data sync knows; for a peer
    /// with library access, only library handshakes do.
    /// </summary>
    [TestMethod]
    public async Task ADataSyncHandshakeFollowsOnlyAPeerWithoutLibraryAccess()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source", self: new Says(ServerKind.Headless, RemoteDevicePlatform.Linux));
        var reader = network.Add("reader");
        await source.Store.SetDisplayNameAsync("NAS");
        await source.GrantDataSyncAsync(reader);
        await source.Store.SetDisplayNameAsync("Renamed NAS");

        await reader.Sessions().GetAsync("source", FederationScopes.DataSyncRead);
        var view = (await reader.Peers.GetStatusAsync()).Peers.Single();
        Assert.AreEqual(("Renamed NAS", ServerKind.Headless, RemoteDevicePlatform.Linux), (view.Label, view.Kind, view.Platform));

        // Once it is browsed too, its routing is the library's.
        await source.GrantLibraryAsync(reader);
        await source.Store.SetDisplayNameAsync("Name from data sync");
        var sessions = reader.Sessions();
        await sessions.GetAsync("source", FederationScopes.DataSyncRead);
        Assert.AreEqual("Renamed NAS", (await reader.Peers.GetStatusAsync()).Peers.Single().Label);
        await sessions.GetAsync("source");
        Assert.AreEqual("Name from data sync", (await reader.Peers.GetStatusAsync()).Peers.Single().Label);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RelocationMovesTheDataSyncAddressAndTheLibrarysOnlyWithoutLibraryAccess(bool browsed)
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        if (browsed) await source.GrantLibraryAsync(reader);
        await source.GrantDataSyncAsync(reader);

        network.Route("source", null);
        network.Route("moved", source);
        var sessions = reader.Sessions(new FixedDiscovery(new NodeDiscoveryCandidate("source", "Source", "http://moved")));
        Assert.AreEqual("http://moved", (await sessions.GetAsync("source", FederationScopes.DataSyncRead)).BaseAddress);

        var peer = reader.Peer("source");
        Assert.AreEqual("http://moved", peer.GetProperty("dataSyncAddress").GetString());
        Assert.AreEqual(browsed ? "http://source" : "http://moved", peer.GetProperty("address").GetString());
    }

    /// <summary>Relocation adopts an address only while the datasync grant it proved is still the stored one.</summary>
    [TestMethod]
    public async Task RelocationWritesNothingOnceTheGrantItProvedIsNoLongerStored()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        await source.GrantDataSyncAsync(reader);
        network.Route("source", null);
        network.Route("moved", source);
        var discovery = new FixedDiscovery(new NodeDiscoveryCandidate("source", "Source", "http://moved"))
        {
            // While the old address is being given up, a person stops reading the peer's definitions.
            OnDiscover = () => reader.Peers.ForgetOutboundDataSyncAsync("source")
        };

        Assert.AreEqual("http://moved",
            (await reader.Sessions(discovery).GetAsync("source", FederationScopes.DataSyncRead)).BaseAddress);

        Assert.IsNull(reader.OutboundDataSync("source"));
        var peer = reader.Peer("source");
        Assert.AreEqual("http://source", peer.GetProperty("dataSyncAddress").GetString());
        Assert.AreEqual("http://source", peer.GetProperty("address").GetString());
    }

    /// <summary>
    /// A device approves a two-way definitions request from an impostor claiming a device it browses, and reads it
    /// back as offered. The impostor gets definitions access both ways — which approving a stranger means — but the
    /// real device's library routing is untouched: browsing it still reaches the real device.
    /// </summary>
    [TestMethod]
    public async Task AnApprovedImpostorClaimingABrowsedDeviceNeverRedirectsLibraryBrowsing()
    {
        using var network = new DataSyncTestNetwork();
        var real = network.Add("x", "real-x");
        var approver = network.Add("approver");
        var impostor = network.Add("x", "impostor");
        await real.Store.SetDisplayNameAsync("Real X");
        await impostor.Store.SetDisplayNameAsync("Impostor");
        await real.Peers.SetSharingAsync(true);
        var code = (await real.Peers.IssueInvitationAsync()).Code;
        await approver.Pairing.ConnectAsync("http://real-x", code);
        await approver.Sessions().GetAsync("x");
        var before = approver.Peer("x").GetRawText();

        await approver.Peers.SetDataSyncSharingAsync(true);
        await impostor.Peers.SetDataSyncSharingAsync(true);
        var request = await impostor.Pairing.ConnectDataSyncAsync("http://approver", null, NodeDataSyncIntents.TwoWay,
            DataSyncTestNetwork.Contract, ["http://impostor"]);
        await approver.Peers.ApproveDataSyncAsync(request.RequestId);
        var offer = (await approver.Peers.TakeDataSyncReciprocalOfferAsync("x"))!;
        Assert.AreEqual("granted", (await approver.Pairing.ConnectDataSyncAsync(offer.Addresses.Single(), offer.Code,
            NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract, expectedNodeId: "x")).Outcome);
        var sessions = approver.Sessions();
        Assert.AreEqual("Impostor", (await sessions.GetAsync("x", FederationScopes.DataSyncRead)).Info.Name);

        AssertLibraryRoutingUnchanged(before, approver.Peer("x"));
        var library = await sessions.GetAsync("x");
        Assert.AreEqual(("http://real-x", "Real X"), (library.BaseAddress, library.Info.Name));
        Assert.AreEqual("Real X", (await approver.Peers.GetStatusAsync()).Peers.Single().Label);
    }

    /// <summary>
    /// G22b on the reader: after the source replaced its library identity and turned definitions sharing back on,
    /// the reader's session stops at <c>/info</c>, before it signs anything, and the old grant is refused.
    /// </summary>
    [TestMethod]
    public async Task AfterTheSourceRotatesItsEpochTheReaderStopsBeforeSigning()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        var grant = await source.GrantDataSyncAsync(reader);
        var sessions = reader.Sessions();
        await sessions.GetAsync("source", FederationScopes.DataSyncRead);

        await source.Peers.RotateLibraryEpochAsync();
        await source.Peers.SetDataSyncSharingAsync(true);
        Assert.AreNotEqual(grant.LibraryEpoch, (await source.InfoAsync()).LibraryEpoch);
        reader.Clock.Now += TimeSpan.FromMinutes(2);
        lock (network.Requests) network.Requests.Clear();

        Assert.AreEqual("LibraryEpochChanged", await ErrorOf(sessions.GetAsync("source", FederationScopes.DataSyncRead)));
        CollectionAssert.AreEqual(new[] { "GET source /federation/v1/info" }, network.Requests);
        Assert.AreEqual("IdentityConflict", sessions.GetConnectionState("source", FederationScopes.DataSyncRead));
        Assert.AreEqual("GrantRevoked", await ErrorOf(source.Grants.CreateHandshakeAsync(grant.GrantId,
            NodeRequestSignature.RandomToken())));
    }

    private static void AssertLibraryRoutingUnchanged(string before, System.Text.Json.JsonElement after)
    {
        using var expected = System.Text.Json.JsonDocument.Parse(before);
        foreach (var member in new[] { "label", "address", "libraryEpoch", "kind", "platform", "enabled" })
            Assert.AreEqual(expected.RootElement.GetProperty(member).GetRawText(), after.GetProperty(member).GetRawText(),
                member);
    }

    private static async Task<string> ErrorOf(Task task)
    {
        var e = await Assert.ThrowsExactlyAsync<FederationAccessException>(() => task);
        return e.ErrorCode;
    }

    private sealed class Says(ServerKind? kind, RemoteDevicePlatform? platform) : IServerSelfDescription
    {
        public ServerKind? Kind => kind;
        public RemoteDevicePlatform? Platform => platform;
    }

    private sealed class FixedDiscovery(params NodeDiscoveryCandidate[] candidates) : INodePeerDiscovery
    {
        public Func<Task>? OnDiscover { get; init; }

        public async Task<IReadOnlyList<NodeDiscoveryCandidate>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            if (OnDiscover != null) await OnDiscover();
            return candidates;
        }
    }
}
