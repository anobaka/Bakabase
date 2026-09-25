using System.Net;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
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

    [TestMethod]
    public async Task AMovedPeerIsAdoptedAtItsDiscoveredAddressOnlyAfterProvingItsGrant()
    {
        using var local = new Node("local", TimeSpan.Zero);
        using var remote = new Node("remote", TimeSpan.Zero);
        using var impostor = new Node("impostor", TimeSpan.Zero);
        var routes = new Dictionary<string, Node> { ["remote"] = remote };
        using var http = new HttpClient(new ProtocolHandler(routes));
        var wire = new FederationHttpClient(http);
        var pairing = new NodePairingClient(local.Store, local.Identity, wire, local.Clock, local.Leases);
        await pairing.ConnectAsync("http://remote", await remote.InviteAsync());

        // The old address went away; discovery nominates a lookalike and the real node's new address.
        routes.Remove("remote");
        routes["lookalike"] = impostor;
        routes["moved"] = remote;
        var discovery = new FixedDiscovery(new NodeDiscoveryCandidate("remote", "Remote", "http://lookalike"),
            new NodeDiscoveryCandidate("remote", "Remote", "http://moved"));
        await remote.Store.SetDisplayNameAsync("Renamed remote");
        var sessions = new PeerSessionFactory(local.Store, local.Identity, wire, local.Clock, discovery);
        Assert.AreEqual("http://moved", (await sessions.GetAsync("remote")).BaseAddress);
        var moved = (await local.Peers.GetStatusAsync()).Peers.Single();
        Assert.AreEqual("http://moved", moved.Address);
        Assert.AreEqual("Renamed remote", moved.Label, "A verified session follows the peer's name.");

        // Without a candidate that proves the grant, the peer simply stays unreachable.
        routes.Remove("moved");
        var stranded = new PeerSessionFactory(local.Store, local.Identity, wire, local.Clock,
            new FixedDiscovery(new NodeDiscoveryCandidate("remote", "Remote", "http://lookalike")));
        Assert.AreEqual("NodeUnreachable", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            stranded.GetAsync("remote"))).ErrorCode);
        Assert.AreEqual("http://moved", (await local.Peers.GetStatusAsync()).Peers.Single().Address);
    }

    [TestMethod]
    public async Task OneApprovalPairsBothWaysWithASingleUseOfferBoundToTheRequester()
    {
        using var reader = new Node("reader", TimeSpan.Zero);
        using var source = new Node("source", TimeSpan.Zero);
        using var other = new Node("other", TimeSpan.Zero);
        var routes = new Dictionary<string, Node> { ["reader"] = reader, ["source"] = source, ["other"] = other };
        using var http = new HttpClient(new ProtocolHandler(routes));
        var wire = new FederationHttpClient(http);
        await reader.Peers.SetSharingAsync(true);
        await source.Peers.SetSharingAsync(true);
        var readerPairing = new NodePairingClient(reader.Store, reader.Identity, wire, reader.Clock, reader.Leases, reader.Peers);
        var sourcePairing = new NodePairingClient(source.Store, source.Identity, wire, source.Clock, source.Leases, source.Peers);

        var outcome = await readerPairing.ConnectAsync("http://source", null, shareBackAddresses: ["http://reader"]);
        Assert.AreEqual("awaitingApproval", outcome.Outcome);
        // A retry reuses the transaction but mints a new code; the source must keep the newest one.
        Assert.AreEqual(outcome.RequestId, (await readerPairing.ConnectAsync("http://source", null,
            shareBackAddresses: ["http://reader"])).RequestId);
        var incoming = (await source.Peers.GetStatusAsync()).Requests.Single(r => r.Direction == "incoming");
        Assert.IsTrue(incoming.OffersReciprocalAccess);
        Assert.AreEqual("reader", await source.Peers.ApproveAsync(incoming.RequestId));

        // The source reads the requester back with the offered code, exactly once.
        var offer = await source.Peers.TakeReciprocalOfferAsync("reader");
        Assert.IsNotNull(offer);
        Assert.IsNull(await source.Peers.TakeReciprocalOfferAsync("reader"));
        Assert.AreEqual("InvalidPairingCode", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            new NodePairingClient(other.Store, other.Identity, wire, other.Clock, other.Leases)
                .ConnectAsync("http://reader", offer!.Code))).ErrorCode, "The code is bound to the source's NodeId.");
        Assert.AreEqual("IdentityConflict", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            sourcePairing.ConnectAsync("http://other", offer!.Code, expectedNodeId: "reader"))).ErrorCode);
        Assert.AreEqual("granted", (await sourcePairing.ConnectAsync(offer!.Addresses.Single(), offer.Code,
            expectedNodeId: "reader")).Outcome);
        Assert.AreEqual("InvalidPairingCode", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            sourcePairing.ConnectAsync("http://reader", offer.Code))).ErrorCode, "The code is single-use.");

        Assert.AreEqual("granted", (await readerPairing.ClaimAsync(outcome.RequestId!)).Outcome);
        var readerView = (await reader.Peers.GetStatusAsync()).Peers.Single();
        Assert.IsNotNull(readerView.OutboundGrant);
        Assert.IsNotNull(readerView.InboundGrant);

        // Removing a device forgets it in both directions.
        await reader.Peers.RemovePeerAsync("source");
        Assert.AreEqual(0, (await reader.Peers.GetStatusAsync()).Peers.Count);
        var sessions = new PeerSessionFactory(source.Store, source.Identity, wire, source.Clock);
        Assert.AreEqual("GrantRevoked", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            sessions.GetAsync("reader"))).ErrorCode);
    }

    [TestMethod]
    public async Task APeerSaysWhatKindOfInstallItIsThroughItsVerifiedHandshakeAndItIsKeptWhileOffline()
    {
        using var local = new Node("local", TimeSpan.Zero);
        using var remote = new Node("remote", TimeSpan.Zero, new Says(ServerKind.Headless, RemoteDevicePlatform.Linux));
        var handler = new ProtocolHandler(new Dictionary<string, Node> { ["remote"] = remote });
        using var http = new HttpClient(handler);
        var wire = new FederationHttpClient(http);
        await new NodePairingClient(local.Store, local.Identity, wire, local.Clock, local.Leases)
            .ConnectAsync("http://remote", await remote.InviteAsync());

        // Nothing is shown before a verified handshake said it.
        Assert.IsNull((await local.Peers.GetStatusAsync()).Peers.Single().Kind);

        var session = await new PeerSessionFactory(local.Store, local.Identity, wire, local.Clock).GetAsync("remote");
        Assert.AreEqual("headless", session.Info.Kind);
        Assert.AreEqual("linux", session.Info.Platform);
        var peer = (await local.Peers.GetStatusAsync()).Peers.Single();
        Assert.AreEqual(ServerKind.Headless, peer.Kind);
        Assert.AreEqual(RemoteDevicePlatform.Linux, peer.Platform);

        // Kept with the peer, so an offline one still shows what it is after a restart.
        var restarted = new FederationStateStore(local, local);
        var again = await new FederationPeerService(restarted, new NodeIdentityProvider(restarted), local.Leases,
            local.Clock).GetStatusAsync();
        Assert.AreEqual(ServerKind.Headless, again.Peers.Single().Kind);
        Assert.AreEqual(RemoteDevicePlatform.Linux, again.Peers.Single().Platform);
    }

    [TestMethod]
    public async Task AnOlderPeerThatSaysNothingPairsAndVerifiesAsBeforeAndIsShownWithout()
    {
        using var local = new Node("local", TimeSpan.Zero);
        using var remote = new Node("remote", TimeSpan.Zero);
        var handler = new ProtocolHandler(new Dictionary<string, Node> { ["remote"] = remote });
        using var http = new HttpClient(handler);
        var wire = new FederationHttpClient(http);
        await new NodePairingClient(local.Store, local.Identity, wire, local.Clock, local.Leases)
            .ConnectAsync("http://remote", await remote.InviteAsync());

        var session = await new PeerSessionFactory(local.Store, local.Identity, wire, local.Clock).GetAsync("remote");

        Assert.IsNull(session.Info.Kind);
        Assert.IsNull(session.Info.Platform);
        Assert.IsNull((await local.Peers.GetStatusAsync()).Peers.Single().Kind);
        // What an older peer sends has no such members at all.
        var older = JsonSerializer.Deserialize<NodeInfo>(
            "{\"nodeId\":\"remote\",\"libraryEpoch\":\"e\",\"name\":\"NAS\",\"protocolVersion\":1," +
            "\"serverTimeUtc\":\"2026-09-20T12:00:00+00:00\"}", FederationJson.Options)!;
        Assert.IsNull(older.Kind);
        Assert.IsNull(older.Platform);
    }

    [TestMethod]
    public async Task WhatANodeSaysItIsStaysOutsideTheHandshakeProofAndOutsideItsBudgetIsRefused()
    {
        var info = new NodeInfo("remote", "epoch", "NAS", 1, new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
        var key = NodeRequestSignature.RandomToken();

        // The proof signs a fixed list: a later node saying more still proves itself to an older one.
        Assert.AreEqual(NodeRequestSignature.HandshakeProof(key, info, "challenge-0123456789"),
            NodeRequestSignature.HandshakeProof(key, info with { Kind = "desktop", Platform = "windows" },
                "challenge-0123456789"));
        Assert.IsNull(ServerSelfDescriptionWords.KindOf("tablet"));

        using var local = new Node("local", TimeSpan.Zero);
        using var remote = new Node("remote", TimeSpan.Zero);
        var handler = new ProtocolHandler(new Dictionary<string, Node> { ["remote"] = remote });
        using var http = new HttpClient(handler);
        var pairing = new NodePairingClient(local.Store, local.Identity, new FederationHttpClient(http), local.Clock,
            local.Leases);

        // A word this build does not know is fine; one past the budget is not a node's answer.
        handler.DescribeInfo = described => described with { Kind = "tablet", Platform = "haiku" };
        Assert.AreEqual("granted", (await pairing.ConnectAsync("http://remote", await remote.InviteAsync())).Outcome);
        handler.DescribeInfo = described => described with { Kind = new string('x', 33) };
        Assert.AreEqual("InvalidNodeResponse", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            pairing.ConnectAsync("http://remote", null))).ErrorCode);
        handler.DescribeInfo = described => described with { Platform = "linux\n" };
        Assert.AreEqual("InvalidNodeResponse", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            pairing.ConnectAsync("http://remote", null))).ErrorCode);
    }

    [TestMethod]
    public async Task WhatANodeSaysAboutDataSyncIsBudgetedLikeItsOtherCapabilities()
    {
        using var local = new Node("local", TimeSpan.Zero);
        using var remote = new Node("remote", TimeSpan.Zero);
        var handler = new ProtocolHandler(new Dictionary<string, Node> { ["remote"] = remote });
        using var http = new HttpClient(handler);
        var pairing = new NodePairingClient(local.Store, local.Identity, new FederationHttpClient(http), local.Clock,
            local.Leases);

        // Within the budget, and kinds this build does not know, are a node's answer.
        handler.DescribeInfo = described => described with
        {
            DataSyncContractVersion = 1_000_000, DataSyncMinimumPeerContract = 0,
            DataSyncKinds = ["customProperty@1", "futureKind@7", new string('k', 128)], SharesDefinitions = true
        };
        Assert.AreEqual("granted", (await pairing.ConnectAsync("http://remote", await remote.InviteAsync())).Outcome);

        foreach (var outside in new Func<NodeInfo, NodeInfo>[]
                 {
                     d => d with { DataSyncKinds = Enumerable.Range(0, 65).Select(i => $"kind{i}@1").ToArray() },
                     d => d with { DataSyncKinds = [new string('k', 129)] },
                     d => d with { DataSyncKinds = [""] },
                     d => d with { DataSyncKinds = ["custom\nProperty@1"] },
                     d => d with { DataSyncContractVersion = -1 },
                     d => d with { DataSyncContractVersion = 1_000_001 },
                     d => d with { DataSyncMinimumPeerContract = -1 },
                     d => d with { DataSyncMinimumPeerContract = 1_000_001 },
                 })
        {
            handler.DescribeInfo = outside;
            Assert.AreEqual("InvalidNodeResponse", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
                pairing.ConnectAsync("http://remote", null))).ErrorCode);
        }
    }

    private sealed class Says(ServerKind? kind, RemoteDevicePlatform? platform) : IServerSelfDescription
    {
        public ServerKind? Kind => kind;
        public RemoteDevicePlatform? Platform => platform;
    }

    [TestMethod]
    public async Task DeviceNameIsUsersChoiceAndInvalidNamesAreRefused()
    {
        using var node = new Node("named", TimeSpan.Zero);
        await node.Store.SetDisplayNameAsync("  Living room NAS ");
        Assert.AreEqual("Living room NAS", (await node.Identity.GetAsync()).Name);
        Assert.AreEqual("InvalidDeviceName", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            node.Store.SetDisplayNameAsync(new string('x', FederationStateStore.MaxDisplayNameLength + 1)))).ErrorCode);
        await node.Store.SetDisplayNameAsync(null);
        Assert.AreEqual(Environment.MachineName, (await node.Identity.GetAsync()).Name);
    }

    private sealed class FixedDiscovery(params NodeDiscoveryCandidate[] candidates) : INodePeerDiscovery
    {
        public Task<IReadOnlyList<NodeDiscoveryCandidate>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeDiscoveryCandidate>>(candidates);
    }

    private sealed class ProtocolHandler(Dictionary<string, Node> nodes) : HttpMessageHandler
    {
        public bool HoldQueries { get; set; }
        public TimeSpan UnsignedInfoOffset { get; set; }
        public bool HoldBodies { get; set; }
        public bool RedirectQueries { get; set; }
        /// <summary>Changes what <c>/federation/v1/info</c> says, as a newer or broken node would.</summary>
        public Func<NodeInfo, NodeInfo>? DescribeInfo { get; set; }
        public TaskCompletionSource QueryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BodyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!nodes.TryGetValue(request.RequestUri!.Host, out var node))
                throw new HttpRequestException("No route to host.");
            var path = request.RequestUri.AbsolutePath;
            var bytes = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            T Body<T>() => JsonSerializer.Deserialize<T>(bytes, FederationJson.Options)!;
            try
            {
                var identity = await node.Identity.GetAsync(ct);
                object value;
                if (path == "/federation/v1/info")
                {
                    var info = new NodeInfo(identity.NodeId, identity.LibraryEpoch, identity.Name, 1,
                        node.Clock.GetUtcNow() + UnsignedInfoOffset).DescribedBy(node.Self);
                    value = DescribeInfo?.Invoke(info) ?? info;
                }
                else if (path == "/federation/v1/pair/code") value = await node.Peers.ExchangeCodeAsync(Body<NodePairCodeRequest>(), ct: ct);
                else if (path == "/federation/v1/pair/request") value = await node.Peers.RequestPairingAsync(Body<NodePairRequest>(), ct: ct);
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
        /// <summary>What this node says it is; null for one from before nodes said.</summary>
        public IServerSelfDescription? Self { get; }
        public Node(string id, TimeSpan offset, IServerSelfDescription? self = null)
        {
            _id = id;
            Self = self;
            Clock = new(offset);
            Store = new(this, this);
            Identity = new NodeIdentityProvider(Store);
            Peers = new(Store, Identity, Leases, Clock);
            Grants = new(Store, Identity, Leases, Clock, self);
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
