using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Service.Components.Federation;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

/// <summary>
/// The Service's side of <c>datasync.read</c> (<see cref="FederationDataSyncGrants"/>): remote access coupled to the
/// sharing switch only from Disabled (§7.1.3, N3), remote access required where this device must be reachable
/// (§7.2.3, §7.2.4), what data sync is told about grants, and how failures read.
/// </summary>
[TestClass]
public sealed class DataSyncScopeServiceTests
{
    /// <summary>
    /// Asked to, turning sharing on opens remote access with pairing required — only from Disabled. A mode the user
    /// opened further, a Docker install's Unrestricted included, is never touched (unlike the library wizard, F68).
    /// </summary>
    [TestMethod]
    [DataRow(RemoteAccessMode.Disabled, true, true)]
    [DataRow(RemoteAccessMode.Disabled, false, false)]
    [DataRow(RemoteAccessMode.Enabled, true, false)]
    [DataRow(RemoteAccessMode.Unrestricted, true, false)]
    public async Task SharingOnOpensRemoteAccessOnlyFromDisabled(RemoteAccessMode mode, bool ask, bool opened)
    {
        using var node = new Node(mode);
        node.Options.Value.Mode = mode;
        node.Options.Value.AllowLiveTranscode = true;

        await node.Grants.SetSharingEnabledAsync(true, ask, default);

        Assert.IsTrue(await node.Grants.IsSharingEnabledAsync(default));
        Assert.AreEqual(opened ? 1 : 0, node.Options.Saves);
        var options = node.Options.Value;
        Assert.AreEqual(opened ? RemoteAccessMode.Enabled : mode, options.Mode);
        Assert.AreEqual(opened, options.RequirePairing);
        Assert.IsTrue(options.AllowLiveTranscode);
        Assert.AreEqual("legacy-server-id", options.ServerId, "The install's own identity is kept.");
        Assert.IsFalse(await node.Store.IsSharingEnabledAsync(), "Library sharing is its own switch.");
    }

    [TestMethod]
    public async Task SharingOffNeverTouchesRemoteAccess()
    {
        using var node = new Node(RemoteAccessMode.Disabled);
        await node.Grants.SetSharingEnabledAsync(true, false, default);
        await node.Grants.SetSharingEnabledAsync(false, true, default);
        Assert.IsFalse(await node.Grants.IsSharingEnabledAsync(default));
        Assert.AreEqual(0, node.Options.Saves);
        Assert.AreEqual(RemoteAccessMode.Disabled, await node.Grants.GetRemoteAccessModeAsync(default));
    }

    /// <summary>§7.2.4 step 1: two-way needs this device readable — sharing on, remote access on, an address.</summary>
    [TestMethod]
    public async Task ATwoWayRequestNeedsThisDeviceToBeReadable()
    {
        using var node = new Node(RemoteAccessMode.Enabled);
        var input = new DataSyncAccessRequestInput(null, "http://192.168.1.9:5000", null, DataSyncRequestIntent.TwoWay);

        Assert.AreEqual(DataSyncProblemCode.SharingOff, (await Problem(node.Grants.RequestAccessAsync(input, default))).Code);
        await node.Grants.SetSharingEnabledAsync(true, false, default);
        node.Remote.Mode = RemoteAccessMode.Disabled;
        Assert.AreEqual(DataSyncProblemCode.RemoteAccessOff, (await Problem(node.Grants.RequestAccessAsync(input, default))).Code);
        node.Remote.Mode = RemoteAccessMode.Enabled;
        Assert.AreEqual(DataSyncProblemCode.RemoteAccessOff, (await Problem(node.Grants.RequestAccessAsync(input, default))).Code,
            "No address the other device could use.");
        Assert.AreEqual(DataSyncProblemCode.PeerUnreachable, (await Problem(node.Grants.RequestAccessAsync(
            new DataSyncAccessRequestInput("node-unknown", null, null, DataSyncRequestIntent.Follow), default))).Code);
        Assert.AreEqual(0, node.Requests.Count, "Nothing was sent.");
    }

    [TestMethod]
    public async Task ACodeNeedsSharingAndRemoteAccess()
    {
        using var node = new Node(RemoteAccessMode.Enabled);
        Assert.AreEqual(DataSyncProblemCode.SharingOff, (await Problem(node.Grants.CreateInvitationAsync(
            new DataSyncInvitationInput(true), default))).Code);
        await node.Grants.SetSharingEnabledAsync(true, false, default);
        node.Remote.Mode = RemoteAccessMode.Disabled;
        Assert.AreEqual(DataSyncProblemCode.RemoteAccessOff, (await Problem(node.Grants.CreateInvitationAsync(
            new DataSyncInvitationInput(true), default))).Code);
    }

    [TestMethod]
    public async Task ApprovalTellsDataSyncAndDropsAnOfferThatIsNotTaken()
    {
        using var node = new Node(RemoteAccessMode.Enabled);
        await node.Grants.SetSharingEnabledAsync(true, false, default);
        var offer = new NodeReciprocalOffer(["http://192.168.1.9:5000"], NodeRequestSignature.RandomToken());
        await node.Peers.SubmitDataSyncRequestAsync(new NodeDataSyncPairRequest("node-pc", "PC", "two-way",
            NodeRequestSignature.RandomToken(), NodeDataSyncIntents.TwoWay, offer));
        await node.Peers.SubmitDataSyncRequestAsync(new NodeDataSyncPairRequest("node-laptop", "Laptop", "no-offer",
            NodeRequestSignature.RandomToken(), NodeDataSyncIntents.TwoWay));
        await node.Peers.SubmitDataSyncRequestAsync(new NodeDataSyncPairRequest("node-phone", "Phone", "follow",
            NodeRequestSignature.RandomToken(), NodeDataSyncIntents.Follow));

        var declined = await node.Grants.ApproveAsync("two-way", readBack: false, default);
        Assert.AreEqual((DataSyncRequestIntent.TwoWay, false, (string?)null),
            (declined.Intent, declined.ReadBackGranted, declined.ReadBackError));
        Assert.IsNull(await node.Peers.TakeDataSyncReciprocalOfferAsync("node-pc"), "The offer is never used later.");

        var nothingOffered = await node.Grants.ApproveAsync("no-offer", readBack: true, default);
        Assert.AreEqual((false, "NoReciprocalOffer"), (nothingOffered.ReadBackGranted, nothingOffered.ReadBackError));

        var follow = await node.Grants.ApproveAsync("follow", readBack: true, default);
        Assert.AreEqual((DataSyncRequestIntent.Follow, false, (string?)null),
            (follow.Intent, follow.ReadBackGranted, follow.ReadBackError));

        CollectionAssert.AreEqual(new[]
        {
            "inbound node-pc TwoWay False", "inbound node-laptop TwoWay False", "inbound node-phone Follow False"
        }, node.Events.Raised.ToArray());
        Assert.AreEqual(0, node.Requests.Count, "Nothing connected back.");
        var grants = await node.Grants.GetGrantsAsync(default);
        CollectionAssert.AreEquivalent(new[] { "node-pc", "node-laptop", "node-phone" }, grants.Select(g => g.NodeId).ToArray());
        Assert.IsTrue(grants.All(g => g.GrantedAt.Kind == DateTimeKind.Utc));

        // Revoking one device ends its access and nobody else's.
        await node.Grants.RevokeAsync("node-pc", default);
        CollectionAssert.AreEquivalent(new[] { "node-laptop", "node-phone" },
            (await node.Grants.GetGrantsAsync(default)).Select(g => g.NodeId).ToArray());
    }

    [TestMethod]
    public async Task RejectingOrCancellingWhatIsNotThereSaysSo()
    {
        using var node = new Node(RemoteAccessMode.Enabled);
        await node.Grants.SetSharingEnabledAsync(true, false, default);
        Assert.AreEqual(DataSyncProblemCode.RequestNotFound, (await Problem(node.Grants.RejectAsync("nothing", default))).Code);
        Assert.AreEqual(DataSyncProblemCode.RequestNotFound, (await Problem(node.Grants.CancelOutgoingAsync("nothing", default))).Code);
        await node.Peers.SubmitDataSyncRequestAsync(new NodeDataSyncPairRequest("node-pc", "PC", "waiting",
            NodeRequestSignature.RandomToken(), NodeDataSyncIntents.Follow));
        await node.Grants.RejectAsync("waiting", default);
        Assert.AreEqual("rejected", (await node.Grants.GetRequestsAsync(default)).Single().Status);
        Assert.AreEqual(DataSyncProblemCode.RequestNotFound, (await Problem(node.Grants.ApproveAsync("waiting", true, default))).Code);
        await node.Grants.SetSharingEnabledAsync(false, false, default);
        Assert.AreEqual(DataSyncProblemCode.SharingOff, (await Problem(node.Grants.ApproveAsync("waiting", true, default))).Code);
    }

    /// <summary>A request naming a device this one knows, from another address, carries the claim warning.</summary>
    [TestMethod]
    public async Task ARequestClaimingAKnownDeviceFromElsewhereIsFlagged()
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS");
        await nas.Grants.SetSharingEnabledAsync(true, false, default);
        await desk.Grants.SetSharingEnabledAsync(true, false, default);
        var code = await nas.Grants.CreateInvitationAsync(new DataSyncInvitationInput(false), default);
        await desk.Grants.RequestAccessAsync(new DataSyncAccessRequestInput(null, nas.Address, code.Code,
            DataSyncRequestIntent.Follow), default);

        await desk.Peers.SubmitDataSyncRequestAsync(new NodeDataSyncPairRequest("node-nas", "NAS", "from-elsewhere",
            NodeRequestSignature.RandomToken(), NodeDataSyncIntents.Follow), "192.168.7.7");
        await desk.Peers.SubmitDataSyncRequestAsync(new NodeDataSyncPairRequest("node-nas", "NAS", "from-there",
            NodeRequestSignature.RandomToken(), NodeDataSyncIntents.Follow), "127.0.0.1");
        await desk.Peers.SubmitDataSyncRequestAsync(new NodeDataSyncPairRequest("node-new", "New", "stranger",
            NodeRequestSignature.RandomToken(), NodeDataSyncIntents.Follow), "192.168.7.8");

        var requests = (await desk.Grants.GetRequestsAsync(default))
            .Where(r => r.Direction == DataSyncRequestDirection.Incoming).ToDictionary(r => r.RequestId);
        Assert.AreEqual((true, nas.Address), (requests["from-elsewhere"].ClaimsKnownDevice, requests["from-elsewhere"].KnownAddress));
        Assert.AreEqual((false, (string?)null), (requests["from-there"].ClaimsKnownDevice, requests["from-there"].KnownAddress));
        Assert.AreEqual((false, (string?)null), (requests["stranger"].ClaimsKnownDevice, requests["stranger"].KnownAddress));
    }

    [TestMethod]
    public void WhatAPeerAnsweredReadsAsDataSyncErrors()
    {
        foreach (var (code, status, expected) in new (string, int, DataSyncPeerErrorCode)[]
                 {
                     ("NodeUnreachable", 503, DataSyncPeerErrorCode.Unreachable),
                     ("GrantRevoked", 401, DataSyncPeerErrorCode.AccessRevoked),
                     ("InvalidNodeSignature", 401, DataSyncPeerErrorCode.AccessRevoked),
                     ("DataSyncSharingDisabled", 403, DataSyncPeerErrorCode.PeerSharingOff),
                     ("SharingDisabled", 403, DataSyncPeerErrorCode.PeerSharingOff),
                     ("RemoteAccessDisabled", 403, DataSyncPeerErrorCode.PeerRemoteAccessOff),
                     ("NodeRouteForbidden", 403, DataSyncPeerErrorCode.PeerTooOld),
                     ("ProtocolUnsupported", 409, DataSyncPeerErrorCode.PeerTooOld),
                     ("PeerTooOld", 409, DataSyncPeerErrorCode.PeerTooOld),
                     ("ThisTooOld", 409, DataSyncPeerErrorCode.ThisTooOld),
                     ("LibraryEpochChanged", 409, DataSyncPeerErrorCode.PeerReset),
                     ("IdentityConflict", 409, DataSyncPeerErrorCode.IdentityConflict),
                     ("PairingBusy", 429, DataSyncPeerErrorCode.Busy),
                     ("PairingRateLimited", 429, DataSyncPeerErrorCode.Busy),
                     ("NodeResponseTooLarge", 502, DataSyncPeerErrorCode.TooLarge),
                     ("InvalidNodeResponse", 502, DataSyncPeerErrorCode.InvalidResponse),
                     ("SomethingNew", 503, DataSyncPeerErrorCode.Unreachable),
                 })
            Assert.AreEqual(expected, ((DataSyncPeerException)FederationDataSyncGrants.MapPeer(
                new FederationAccessException(code, status, "message"))).Code, code);
        Assert.AreEqual(DataSyncProblemCode.InvitationInvalid, ((DataSyncProblemException)FederationDataSyncGrants.MapPeer(
            new FederationAccessException("InvalidPairingCode", 403, "message"))).Problem.Code);
    }

    private static async Task<DataSyncProblem> Problem(Task task) =>
        (await Assert.ThrowsExactlyAsync<DataSyncProblemException>(() => task)).Problem;

    /// <summary>One node with the Service's bridge; the network answers nothing and records what was sent.</summary>
    private sealed class Node : IFederationDataDirectory, INodeIdSource, IDisposable
    {
        private readonly ServiceProvider _services;

        public Node(RemoteAccessMode mode)
        {
            Remote.Mode = mode;
            Store = new FederationStateStore(this, this);
            var identity = new NodeIdentityProvider(Store);
            Peers = new FederationPeerService(Store, identity, Leases, TimeProvider.System);
            _services = new ServiceCollection().AddSingleton<IDataSyncGrantEvents>(Events).BuildServiceProvider();
            var wire = new FederationHttpClient(new HttpClient(new RecordingHandler(Requests)));
            var pairing = new NodePairingClient(Store, identity, wire, TimeProvider.System, Leases, Peers);
            var flow = new FederationPairingFlow(pairing, Peers, null!, Remote,
                NullLogger<FederationPairingFlow>.Instance, _services);
            Grants = new FederationDataSyncGrants(Peers, pairing, flow, Store, identity, Remote, Options,
                new PeerSessionFactory(Store, identity, wire, TimeProvider.System), new NoDiscovery());
        }

        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "federation-datasync-grants-" + Guid.NewGuid().ToString("N"));
        public string Ensure() { Directory.CreateDirectory(Path); return Path; }
        public Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("node-this");

        public AddressedRemoteAccess Remote { get; } = new();
        public CountingOptions Options { get; } = new();
        public RecordingGrantEvents Events { get; } = new();
        public List<string> Requests { get; } = [];
        public GrantLeaseRegistry Leases { get; } = new();
        public FederationStateStore Store { get; }
        public FederationPeerService Peers { get; }
        public FederationDataSyncGrants Grants { get; }

        public void Dispose()
        {
            Leases.Dispose();
            _services.Dispose();
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }

    private sealed class CountingOptions : IBOptionsManager<RemoteAccessOptions>
    {
        public RemoteAccessOptions Value { get; private set; } = new() { ServerId = "legacy-server-id" };
        public int Saves { get; private set; }
        public void Save(RemoteAccessOptions options) { Value = options; Saves++; }
        public Task SaveAsync(RemoteAccessOptions options) { Save(options); return Task.CompletedTask; }
        public Task SaveAsync(Action<RemoteAccessOptions> modify) { modify(Value); Saves++; return Task.CompletedTask; }
    }

    private sealed class RecordingHandler(List<string> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (requests) requests.Add($"{request.Method} {request.RequestUri}");
            throw new HttpRequestException("No route to host.");
        }
    }

    private sealed class NoDiscovery : INodePeerDiscovery
    {
        public Task<IReadOnlyList<NodeDiscoveryCandidate>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeDiscoveryCandidate>>([]);
    }
}
