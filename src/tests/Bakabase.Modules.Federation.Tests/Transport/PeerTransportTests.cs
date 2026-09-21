using System.Net;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Transport;

[TestClass]
public sealed class PeerTransportTests
{
    [TestMethod]
    public async Task RestoreRetainsOutboundAccessAndMappingsWhileCloneClearsThemAndBothDisableSharingAndBrowsing()
    {
        using var local = new Node("local", TimeSpan.Zero);
        using var remote = new Node("remote", TimeSpan.Zero);
        using var http = new HttpClient(new ProtocolHandler(new Dictionary<string, Node> { ["remote"] = remote }));
        var wire = new FederationHttpClient(http);
        var pairing = new NodePairingClient(local.Store, local.Identity, wire, local.Clock, local.Leases);
        await pairing.ConnectAsync("http://remote", await remote.InviteAsync());
        var mappings = new[] { new NodePathMapping("root", Path.GetTempPath()) };
        await local.Peers.SetPathMappingsAsync("remote", mappings);
        await local.Peers.SetSharingAsync(true);
        await local.Store.SetBrowsingEnabledAsync(true);
        var before = await local.Peers.GetStatusAsync();

        var restored = await local.Peers.RotateLibraryEpochAsync();
        var restartStore = new FederationStateStore(local, local);
        var restartIdentity = new NodeIdentityProvider(restartStore);
        var restartPeers = new FederationPeerService(restartStore, restartIdentity, local.Leases, local.Clock);
        var status = await restartPeers.GetStatusAsync();
        Assert.AreEqual(before.Identity.NodeId, restored.NodeId);
        Assert.AreNotEqual(before.Identity.LibraryEpoch, restored.LibraryEpoch);
        Assert.IsFalse(status.SharingEnabled);
        Assert.IsFalse(await restartStore.IsBrowsingEnabledAsync());
        Assert.AreEqual(before.Peers.Single().OutboundGrant, status.Peers.Single().OutboundGrant);
        CollectionAssert.AreEqual(mappings, status.Peers.Single().PathMappings.ToArray());
        var sessions = new PeerSessionFactory(restartStore, restartIdentity, wire, local.Clock);
        Assert.AreEqual("remote", (await sessions.GetAsync("remote")).NodeId);

        await restartPeers.SetSharingAsync(true);
        await restartStore.SetBrowsingEnabledAsync(true);
        var clone = await restartPeers.ResetAsNewNodeAsync();
        var cloneStore = new FederationStateStore(local, local);
        var clonePeers = new FederationPeerService(cloneStore, new NodeIdentityProvider(cloneStore), local.Leases, local.Clock);
        var cloneStatus = await clonePeers.GetStatusAsync();
        Assert.AreNotEqual(restored.NodeId, clone.NodeId);
        Assert.AreNotEqual(restored.LibraryEpoch, clone.LibraryEpoch);
        Assert.IsFalse(cloneStatus.SharingEnabled);
        Assert.IsFalse(await cloneStore.IsBrowsingEnabledAsync());
        Assert.AreEqual(0, cloneStatus.Peers.Count);
        Assert.AreEqual(0, cloneStatus.Requests.Count);
    }

    [TestMethod]
    public async Task TwoOwnersKeepIndependentKeysClocksSessionsAndOutboundPermissionWhenLocalSharingIsOff()
    {
        using var local = new Node("local", TimeSpan.Zero);
        using var east = new Node("east", TimeSpan.FromMinutes(40));
        using var west = new Node("west", TimeSpan.FromMinutes(-40));
        var handler = new ProtocolHandler(new Dictionary<string, Node> { ["east"] = east, ["west"] = west })
            { UnsignedInfoOffset = TimeSpan.FromMinutes(3) };
        using var http = new HttpClient(handler);
        var wire = new FederationHttpClient(http);
        var pairing = new NodePairingClient(local.Store, local.Identity, wire, local.Clock, local.Leases);
        var eastCode = await east.InviteAsync();
        var westCode = await west.InviteAsync();
        var outcomes = await Task.WhenAll(pairing.ConnectAsync("http://east", eastCode), pairing.ConnectAsync("http://west", westCode));
        Assert.IsTrue(outcomes.All(o => o.Outcome == "granted"));
        Assert.IsFalse((await local.Peers.GetStatusAsync()).SharingEnabled);
        var sessions = new PeerSessionFactory(local.Store, local.Identity, wire, local.Clock);
        var verified = await Task.WhenAll(sessions.GetAsync("east"), sessions.GetAsync("west"));
        Assert.AreEqual(TimeSpan.FromMinutes(40), verified[0].ClockOffset);
        Assert.AreEqual(TimeSpan.FromMinutes(-40), verified[1].ClockOffset);
        Assert.AreNotEqual(verified[0].Credentials.Key, verified[1].Credentials.Key);
        var transport = new NodeTransport(local.Store, sessions, wire, local.Clock, local.Leases);
        foreach (var session in verified)
        {
            using var response = await transport.SendAsync(session, HttpMethod.Post, "/federation/v1/export/queries", new { text = session.NodeId });
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }
        await east.Peers.RevokeAsync(verified[0].GrantId);
        using var denied = await transport.SendAsync(verified[0], HttpMethod.Post, "/federation/v1/export/queries", new { text = "east" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var stillWorks = await transport.SendAsync(verified[1], HttpMethod.Post, "/federation/v1/export/queries", new { text = "west" });
        Assert.AreEqual(HttpStatusCode.OK, stillWorks.StatusCode);
    }

    [TestMethod]
    public async Task PendingClaimSurvivesClientRestartAndDisabledPeerCancelsAnInflightRequest()
    {
        using var local = new Node("local", TimeSpan.Zero);
        using var remote = new Node("remote", TimeSpan.Zero);
        await remote.Peers.SetSharingAsync(true);
        var handler = new ProtocolHandler(new Dictionary<string, Node> { ["remote"] = remote });
        using var http = new HttpClient(handler);
        var wire = new FederationHttpClient(http);
        var pairing = new NodePairingClient(local.Store, local.Identity, wire, local.Clock, local.Leases);
        var pending = await pairing.ConnectAsync("http://remote", null);
        Assert.AreEqual("awaitingApproval", pending.Outcome);
        await remote.Peers.ApproveAsync(pending.RequestId!);
        var restartedStore = new FederationStateStore(local, local);
        var restartedIdentity = new NodeIdentityProvider(restartedStore);
        var restartedClient = new NodePairingClient(restartedStore, restartedIdentity, wire, local.Clock, local.Leases);
        Assert.AreEqual("granted", (await restartedClient.ClaimAsync(pending.RequestId!)).Outcome);
        var restartedPeers = new FederationPeerService(restartedStore, restartedIdentity, local.Leases, local.Clock);
        var sessions = new PeerSessionFactory(restartedStore, restartedIdentity, wire, local.Clock);
        var transport = new NodeTransport(restartedStore, sessions, wire, local.Clock, local.Leases);
        var snapshot = await sessions.GetAsync("remote");
        handler.HoldQueries = true;
        var flight = transport.SendAsync(snapshot, HttpMethod.Post, "/federation/v1/export/queries", new { text = "wait" });
        await handler.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await restartedPeers.SetEnabledAsync("remote", false);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await flight);
        Assert.AreEqual("NodeSessionChanged", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            transport.SendAsync(snapshot, HttpMethod.Post, "/federation/v1/export/queries"))).ErrorCode);
        handler.HoldQueries = false;
        await restartedPeers.SetEnabledAsync("remote", true);
        using var resumed = await transport.SendAsync(snapshot, HttpMethod.Post, "/federation/v1/export/queries");
        Assert.AreEqual(HttpStatusCode.OK, resumed.StatusCode);
        handler.HoldBodies = true;
        using var streaming = await transport.SendAsync(snapshot, HttpMethod.Get, "/federation/v1/export/assets/test-asset");
        await using var body = await streaming.Content.ReadAsStreamAsync();
        var read = body.ReadAsync(new byte[16]).AsTask();
        await handler.BodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var outgoing = local.Leases.GetCancellationToken(GrantLeaseRegistry.OutboundKey(snapshot.GrantId));
        await restartedPeers.ForgetOutboundAsync("remote");
        Assert.IsTrue(outgoing.IsCancellationRequested);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await read);
    }

    [TestMethod]
    public async Task ChangedAddressCannotMasqueradeAsPairedIdentityAndSignedRequestsCannotRedirect()
    {
        using var local = new Node("local", TimeSpan.Zero);
        using var owner = new Node("owner", TimeSpan.Zero);
        using var impostor = new Node("impostor", TimeSpan.Zero);
        var routes = new Dictionary<string, Node> { ["owner"] = owner };
        var handler = new ProtocolHandler(routes);
        using var http = new HttpClient(handler);
        var wire = new FederationHttpClient(http);
        var pairing = new NodePairingClient(local.Store, local.Identity, wire, local.Clock, local.Leases);
        await pairing.ConnectAsync("http://owner", await owner.InviteAsync());
        routes["owner"] = impostor;
        var sessions = new PeerSessionFactory(local.Store, local.Identity, wire, local.Clock);
        Assert.AreEqual("IdentityConflict", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() => sessions.GetAsync("owner"))).ErrorCode);
        routes["owner"] = owner;
        var session = await sessions.GetAsync("owner");
        handler.RedirectQueries = true;
        var transport = new NodeTransport(local.Store, sessions, wire, local.Clock, local.Leases);
        Assert.AreEqual("NodeRedirectRefused", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            transport.SendAsync(session, HttpMethod.Post, "/federation/v1/export/queries"))).ErrorCode);
        await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            transport.SendAsync(session, HttpMethod.Get, "/federation/v1/../options"));
    }

    private sealed class ProtocolHandler(Dictionary<string, Node> nodes) : HttpMessageHandler
    {
        public bool HoldQueries { get; set; }
        public TimeSpan UnsignedInfoOffset { get; set; }
        public bool HoldBodies { get; set; }
        public bool RedirectQueries { get; set; }
        public TaskCompletionSource QueryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BodyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var node = nodes[request.RequestUri!.Host];
            var path = request.RequestUri.AbsolutePath;
            var bytes = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            T Body<T>() => JsonSerializer.Deserialize<T>(bytes, FederationJson.Options)!;
            try
            {
                var identity = await node.Identity.GetAsync(ct);
                object value;
                if (path == "/federation/v1/info")
                    value = new NodeInfo(identity.NodeId, identity.LibraryEpoch, identity.Name, 1, node.Clock.GetUtcNow() + UnsignedInfoOffset);
                else if (path == "/federation/v1/pair/code") value = await node.Peers.ExchangeCodeAsync(Body<NodePairCodeRequest>(), ct);
                else if (path == "/federation/v1/pair/request") value = await node.Peers.RequestPairingAsync(Body<NodePairRequest>(), ct);
                else if (path == "/federation/v1/pair/claim") value = await node.Peers.ClaimPairingAsync(Body<NodePairClaimRequest>(), ct);
                else
                {
                    var query = request.RequestUri.Query;
                    var principal = await node.Auth.AuthenticateAsync(request.Headers.GetValues("Authorization").Single(),
                        request.Method.Method, path, query.Length == 0 ? "" : query[1..], NodeRequestSignature.Hash(bytes), ct);
                    if (path.EndsWith("/handshake"))
                        value = await node.Grants.CreateHandshakeAsync(principal.GrantId, Body<NodeHandshakeRequest>().Challenge, ct);
                    else
                    {
                        if (RedirectQueries) return new(HttpStatusCode.TemporaryRedirect)
                            { Headers = { Location = new Uri("http://other/federation/v1/export/queries") } };
                        QueryStarted.TrySetResult();
                        if (HoldQueries) await Task.Delay(Timeout.Infinite, ct);
                        if (HoldBodies) return new(HttpStatusCode.OK) { Content = new StreamContent(new HeldBody(BodyStarted)) };
                        value = new { nodeId = identity.NodeId };
                    }
                }
                return Json(HttpStatusCode.OK, value);
            }
            catch (FederationAccessException e) { return Json((HttpStatusCode)e.StatusCode, new { code = e.ErrorCode, message = e.Message }); }
        }
        private static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status)
            { Content = new StringContent(JsonSerializer.Serialize(value, FederationJson.Options), Encoding.UTF8, "application/json") };
    }
    private sealed class HeldBody(TaskCompletionSource started) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class Node : IFederationDataDirectory, INodeIdSource, IDisposable
    {
        private readonly string _id;
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "federation-transport-" + Guid.NewGuid().ToString("N"));
        public string Ensure() { Directory.CreateDirectory(Path); return Path; }
        public Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(_id);
        public Clock Clock { get; }
        public GrantLeaseRegistry Leases { get; } = new();
        public FederationStateStore Store { get; }
        public INodeIdentityProvider Identity { get; }
        public FederationPeerService Peers { get; }
        public NodeGrantService Grants { get; }
        public NodeGrantAuthenticator Auth { get; }
        public Node(string id, TimeSpan offset)
        {
            _id = id;
            Clock = new(offset);
            Store = new(this, this);
            Identity = new NodeIdentityProvider(Store);
            Peers = new(Store, Identity, Leases, Clock);
            Grants = new(Store, Identity, Leases, Clock);
            Auth = new(Grants, new NodeNonceCache(Clock), Clock);
        }
        public async Task<string> InviteAsync()
        {
            await Peers.SetSharingAsync(true);
            return (await Peers.IssueInvitationAsync()).Code;
        }
        public void Dispose() { Leases.Dispose(); if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
    private sealed class Clock(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero) + offset;
    }
}
