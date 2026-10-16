using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Tests.Security;
using Bakabase.Modules.Federation.Transport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Transport;

/// <summary>
/// Verified definitions sessions retain identity and reject stale grants during relocation.
/// </summary>
[TestClass]
public sealed class PeerSessionScopeTests
{
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
        var code = (await source.Peers.IssueDataSyncInvitationAsync()).Code;
        await reader.Pairing.ConnectDataSyncAsync("http://source-alt", code, NodeDataSyncIntents.Follow,
            DataSyncTestNetwork.Contract);
        peer = reader.Peer("source");
        Assert.AreEqual("NAS", peer.GetProperty("label").GetString());
        Assert.AreEqual("http://source", peer.GetProperty("address").GetString());
        Assert.AreEqual("http://source-alt", peer.GetProperty("dataSyncAddress").GetString());
    }

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

    private static async Task<string> ErrorOf(Task task)
    {
        var e = await Assert.ThrowsExactlyAsync<FederationAccessException>(() => task);
        return e.ErrorCode;
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
