using System.Text;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Security;

[TestClass]
public sealed class NodeSecurityTests
{
    [TestMethod]
    public async Task IdentityIsInheritedOnceAndCorruptionDoesNotSilentlyCreateANewNode()
    {
        using var node = new TestNode();
        var first = await node.Identity.GetAsync();
        node.Source.Id = "imported-legacy-server-id";
        var restart = new FederationStateStore(node.Directory, node.Source);
        Assert.AreEqual(first, await restart.GetIdentityAsync());
        await File.WriteAllTextAsync(Path.Combine(node.Directory.Path, FederationStateStore.FileName), "{broken");
        var corrupt = new FederationStateStore(node.Directory, node.Source);
        var error = await Assert.ThrowsExactlyAsync<FederationAccessException>(() => corrupt.GetIdentityAsync());
        Assert.AreEqual("SharingStateUnavailable", error.ErrorCode);
        Assert.AreEqual("{broken", await File.ReadAllTextAsync(Path.Combine(node.Directory.Path, FederationStateStore.FileName)));
        var explicitReset = new FederationPeerService(corrupt, new NodeIdentityProvider(corrupt), node.Leases, node.Clock);
        var clone = await explicitReset.ResetAsNewNodeAsync();
        Assert.AreNotEqual(first.NodeId, clone.NodeId);
        Assert.IsFalse((await explicitReset.GetDataSyncStatusAsync()).SharingEnabled);
    }

    [TestMethod]
    public async Task ANewNodeTakesItsHostsNewIdentitySoBothKeepAnsweringUnderOneId()
    {
        // Creating a new device identity replaces the install's identity first; the node follows it,
        // as the first node did, so a peer, a managed server and a beacon of one install stay one id.
        using var node = new TestNode();
        var first = await node.Identity.GetAsync();
        Assert.AreEqual(node.Source.Id, first.NodeId);

        node.Source.Id = "replaced-server-id";
        var clone = await node.Peers.ResetAsNewNodeAsync(inheritHostIdentity: true);

        Assert.AreEqual("replaced-server-id", clone.NodeId);
        Assert.AreNotEqual(first.LibraryEpoch, clone.LibraryEpoch);
        Assert.AreEqual(clone, await new NodeIdentityProvider(new FederationStateStore(node.Directory, node.Source)).GetAsync());

        // A host that kept its identity — an unreadable state recovered — gives the node nothing.
        var recovered = await node.Peers.ResetAsNewNodeAsync();
        Assert.AreNotEqual("replaced-server-id", recovered.NodeId);
        Assert.AreNotEqual(clone.LibraryEpoch, recovered.LibraryEpoch);
    }

    [TestMethod]
    public async Task FailedAtomicWriteDoesNotPublishNewStateOrAlterExistingFile()
    {
        using var node = new TestNode();
        await node.Peers.SetDataSyncSharingAsync(true);
        var before = await File.ReadAllTextAsync(Path.Combine(node.Directory.Path, FederationStateStore.FileName));
        node.Directory.FailWrites = true;
        await Assert.ThrowsExactlyAsync<IOException>(() => node.Peers.SetDataSyncSharingAsync(false));
        Assert.IsTrue((await node.Peers.GetDataSyncStatusAsync()).SharingEnabled);
        Assert.AreEqual(before, await File.ReadAllTextAsync(Path.Combine(node.Directory.Path, FederationStateStore.FileName)));
    }

    private static async Task AssertInvalidCode(Task<NodePairExchange> exchange) =>
        Assert.AreEqual("InvalidPairingCode",
            (await Assert.ThrowsExactlyAsync<FederationAccessException>(() => exchange)).ErrorCode);

    [TestMethod]
    public async Task SignatureBindsAudienceMethodPathRawQueryBodyAndRejectsReplay()
    {
        using var node = new TestNode();
        var grant = await node.GrantAsync();
        const string path = "/federation/v1/export/datasync/head";
        var body = NodeRequestSignature.Hash(Encoding.UTF8.GetBytes("{\"text\":\"name\"}"));
        var signature = NodeRequestSignature.Create(grant, "POST", path, "?x=1&x=2", body, node.Clock.GetUtcNow());
        foreach (var (method, route, query, hash) in new[]
                 {
                     ("GET", path, "?x=1&x=2", body), ("POST", path + "/other", "?x=1&x=2", body),
                     ("POST", path, "x=1&x=2", body), ("POST", path, "?x=1&x=2", NodeRequestSignature.Hash([]))
                 })
            Assert.AreEqual("InvalidNodeSignature", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
                node.Auth.AuthenticateAsync(signature, method, route, query, hash))).ErrorCode);
        var principal = await node.Auth.AuthenticateAsync(signature, "POST", path, "?x=1&x=2", body);
        Assert.AreEqual(grant.AudienceNodeId, principal.AudienceNodeId);
        Assert.AreEqual("SignatureReplayed", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            node.Auth.AuthenticateAsync(signature, "POST", path, "?x=1&x=2", body))).ErrorCode);
        var wrongAudience = NodeRequestSignature.Create(grant with { AudienceNodeId = "another-node" }, "POST", path,
            "", body, node.Clock.GetUtcNow());
        Assert.AreEqual("InvalidNodeSignature", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            node.Auth.AuthenticateAsync(wrongAudience, "POST", path, "", body))).ErrorCode);
    }

    [TestMethod]
    public async Task RevokeAndEpochResetInvalidateGrantsWhileSharingToggleLeavesOutboundAlone()
    {
        using var node = new TestNode();
        var grant = await node.GrantAsync();
        var incoming = node.Leases.GetCancellationToken(grant.GrantId);
        var outgoing = node.Leases.GetCancellationToken(GrantLeaseRegistry.OutboundKey("other-owner-grant"));
        await node.Peers.SetDataSyncSharingAsync(false);
        Assert.IsTrue(incoming.IsCancellationRequested);
        Assert.IsFalse(outgoing.IsCancellationRequested);
        await node.Peers.SetDataSyncSharingAsync(true);
        await node.Grants.ValidateAsync(grant.GrantId, grant.LibraryEpoch);
        await node.Peers.RevokeDataSyncAsync(grant.SubjectNodeId);
        await node.Peers.SetDataSyncSharingAsync(true);
        Assert.AreEqual("GrantRevoked", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            node.Grants.ValidateAsync(grant.GrantId, grant.LibraryEpoch))).ErrorCode);
        grant = await node.GrantAsync();
        await node.Peers.IssueDataSyncInvitationAsync();
        var before = await node.Identity.GetAsync();
        var after = await node.Peers.RotateLibraryEpochAsync();
        Assert.AreEqual(before.NodeId, after.NodeId);
        Assert.AreNotEqual(before.LibraryEpoch, after.LibraryEpoch);
        Assert.IsFalse(outgoing.IsCancellationRequested);
        Assert.IsFalse((await node.Peers.GetDataSyncStatusAsync()).SharingEnabled);
        Assert.AreEqual(0, (await node.Peers.GetDataSyncStatusAsync()).Requests.Count);
        await node.Peers.SetDataSyncSharingAsync(true);
        await Assert.ThrowsExactlyAsync<FederationAccessException>(() => node.Grants.ValidateAsync(grant.GrantId, grant.LibraryEpoch));
        var clone = await node.Peers.ResetAsNewNodeAsync();
        Assert.AreNotEqual(before.NodeId, clone.NodeId);
        Assert.IsTrue(outgoing.IsCancellationRequested);
        Assert.IsFalse((await node.Peers.GetDataSyncStatusAsync()).SharingEnabled);
    }

    [TestMethod]
    public async Task ExpiredOrSelfPairingCannotIssueGrantAndLegacySignaturesAreNotNodeCredentials()
    {
        using var node = new TestNode();
        await node.Peers.SetDataSyncSharingAsync(true);
        var local = await node.Identity.GetAsync();
        await Assert.ThrowsExactlyAsync<FederationAccessException>(() => node.Peers.SubmitDataSyncRequestAsync(
            new(local.NodeId, "Self", "self-transaction", NodeRequestSignature.RandomToken(), NodeDataSyncIntents.Follow)));
        var grant = await node.GrantAsync();
        var signature = NodeRequestSignature.Create(grant, "POST", "/federation/v1/export/datasync/head", "",
            NodeRequestSignature.Hash([]), node.Clock.GetUtcNow());
        node.Clock.Now += TimeSpan.FromMinutes(6);
        Assert.AreEqual("SignatureExpired", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            node.Auth.AuthenticateAsync(signature, "POST", "/federation/v1/export/datasync/head", "", NodeRequestSignature.Hash([])))).ErrorCode);
        Assert.IsNull(NodeRequestSignature.Parse(signature.Replace("Bakabase-Node", "Bakabase-Device")));
        Assert.IsNull(NodeRequestSignature.Parse("Bakabase-Node malformed"));
        Assert.IsTrue(NodeRequestSignature.HasScheme(" Bakabase-Node-malformed"));
    }

    private sealed class TestNode : IDisposable
    {
        public TestDirectory Directory { get; } = new();
        public Source Source { get; } = new();
        public Clock Clock { get; } = new();
        public GrantLeaseRegistry Leases { get; } = new();
        public FederationStateStore Store { get; }
        public INodeIdentityProvider Identity { get; }
        public FederationPeerService Peers { get; }
        public NodeGrantService Grants { get; }
        public NodeGrantAuthenticator Auth { get; }
        public TestNode()
        {
            Store = new(Directory, Source);
            Identity = new NodeIdentityProvider(Store);
            Peers = new(Store, Identity, Leases, Clock);
            Grants = new(Store, Identity, Leases, Clock);
            Auth = new(Grants, new NodeNonceCache(Clock), Clock);
        }
        public async Task<NodeCredentials> GrantAsync()
        {
            await Peers.SetDataSyncSharingAsync(true);
            var request = new NodeDataSyncPairRequest("reader-a", "Reader A", NodeRequestSignature.RandomToken(18), NodeRequestSignature.RandomToken(), NodeDataSyncIntents.Follow);
            await Peers.SubmitDataSyncRequestAsync(request);
            await Peers.ApproveDataSyncAsync(request.TransactionId);
            return (await Peers.ClaimDataSyncAsync(new(request.TransactionId, request.NodeId, request.ClaimSecret))).Credentials!;
        }
        public void Dispose() { Leases.Dispose(); System.IO.Directory.Delete(Directory.Path, true); }
    }
    private sealed class TestDirectory : IFederationDataDirectory
    {
        public bool FailWrites { get; set; }
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bakabase-node-tests-" + Guid.NewGuid().ToString("N"));
        public string Ensure()
        {
            if (FailWrites) throw new IOException("Simulated storage failure.");
            System.IO.Directory.CreateDirectory(Path);
            return Path;
        }
    }
    private sealed class Source : INodeIdSource
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(Id);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
