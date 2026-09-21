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
        Assert.IsFalse((await explicitReset.GetStatusAsync()).SharingEnabled);
    }

    [TestMethod]
    public async Task FailedAtomicWriteDoesNotPublishNewStateOrAlterExistingFile()
    {
        using var node = new TestNode();
        await node.Peers.SetSharingAsync(true);
        var before = await File.ReadAllTextAsync(Path.Combine(node.Directory.Path, FederationStateStore.FileName));
        node.Directory.FailWrites = true;
        await Assert.ThrowsExactlyAsync<IOException>(() => node.Peers.SetSharingAsync(false));
        Assert.IsTrue((await node.Peers.GetStatusAsync()).SharingEnabled);
        Assert.AreEqual(before, await File.ReadAllTextAsync(Path.Combine(node.Directory.Path, FederationStateStore.FileName)));
    }

    [TestMethod]
    public async Task PairingApprovalIsDirectionalSecretBoundAndSurvivesRestart()
    {
        using var node = new TestNode();
        await node.Peers.SetSharingAsync(true);
        var request = new NodePairRequest("reader-a", "Reader A", "transaction-a", NodeRequestSignature.RandomToken());
        Assert.AreEqual("awaitingApproval", (await node.Peers.RequestPairingAsync(request)).Outcome);
        Assert.AreEqual("PairingRejected", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            node.Peers.RequestPairingAsync(request with { NodeId = "another-reader" }))).ErrorCode);
        Assert.IsNull((await node.Peers.ClaimPairingAsync(new(request.TransactionId, request.NodeId, request.ClaimSecret))).Credentials);
        await node.Peers.ApproveAsync(request.TransactionId);
        var restart = new FederationStateStore(node.Directory, node.Source);
        var peers = new FederationPeerService(restart, new NodeIdentityProvider(restart), node.Leases, node.Clock);
        var exchange = await peers.ClaimPairingAsync(new(request.TransactionId, request.NodeId, request.ClaimSecret));
        Assert.AreEqual("granted", exchange.Outcome);
        Assert.AreEqual("reader-a", exchange.Credentials!.SubjectNodeId);
        Assert.AreEqual((await node.Identity.GetAsync()).NodeId, exchange.Credentials.AudienceNodeId);
        var status = await peers.GetStatusAsync();
        Assert.IsNotNull(status.Peers.Single().InboundGrant);
        Assert.IsNull(status.Peers.Single().OutboundGrant);
        var error = await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            peers.ClaimPairingAsync(new(request.TransactionId, "reader-b", request.ClaimSecret)));
        Assert.AreEqual("PairingRejected", error.ErrorCode);
    }

    [TestMethod]
    public async Task InvitationAttemptsArePersistedAndCodeIsSingleUse()
    {
        using var node = new TestNode();
        await node.Peers.SetSharingAsync(true);
        var invitation = await node.Peers.IssueInvitationAsync();
        for (var i = 0; i < 5; i++)
            Assert.AreEqual("rejected", (await node.Peers.ExchangeCodeAsync(new("reader", "Reader", "bad",
                "attempt-" + i, NodeRequestSignature.RandomToken()))).Outcome);
        Assert.AreEqual("rejected", (await node.Peers.ExchangeCodeAsync(new("reader", "Reader", invitation.Code,
            "correct-but-exhausted", NodeRequestSignature.RandomToken()))).Outcome);
        invitation = await node.Peers.IssueInvitationAsync();
        var request = new NodePairCodeRequest("reader", "Reader", invitation.Code, "valid-transaction", NodeRequestSignature.RandomToken());
        var first = await node.Peers.ExchangeCodeAsync(request);
        Assert.AreEqual("granted", first.Outcome);
        Assert.AreEqual(first.Credentials, (await node.Peers.ExchangeCodeAsync(request)).Credentials);
        Assert.AreEqual("rejected", (await node.Peers.ExchangeCodeAsync(request with { TransactionId = "another-transaction" })).Outcome);
    }

    [TestMethod]
    public async Task SignatureBindsAudienceMethodPathRawQueryBodyAndRejectsReplay()
    {
        using var node = new TestNode();
        var grant = await node.GrantAsync();
        const string path = "/federation/v1/export/queries";
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
        await node.Peers.SetSharingAsync(false);
        Assert.IsTrue(incoming.IsCancellationRequested);
        Assert.IsFalse(outgoing.IsCancellationRequested);
        await node.Peers.SetSharingAsync(true);
        await node.Grants.ValidateAsync(grant.GrantId, grant.LibraryEpoch);
        await node.Peers.RevokeAsync(grant.GrantId);
        await node.Peers.SetSharingAsync(true);
        Assert.AreEqual("GrantRevoked", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            node.Grants.ValidateAsync(grant.GrantId, grant.LibraryEpoch))).ErrorCode);
        grant = await node.GrantAsync();
        await node.Store.SetBrowsingEnabledAsync(true);
        await node.Peers.IssueInvitationAsync();
        var before = await node.Identity.GetAsync();
        var after = await node.Peers.RotateLibraryEpochAsync();
        Assert.AreEqual(before.NodeId, after.NodeId);
        Assert.AreNotEqual(before.LibraryEpoch, after.LibraryEpoch);
        Assert.IsFalse(outgoing.IsCancellationRequested);
        Assert.IsFalse((await node.Peers.GetStatusAsync()).SharingEnabled);
        Assert.IsFalse(await node.Store.IsBrowsingEnabledAsync());
        Assert.AreEqual(0, (await node.Peers.GetStatusAsync()).Requests.Count);
        await node.Peers.SetSharingAsync(true);
        await Assert.ThrowsExactlyAsync<FederationAccessException>(() => node.Grants.ValidateAsync(grant.GrantId, grant.LibraryEpoch));
        var clone = await node.Peers.ResetAsNewNodeAsync();
        Assert.AreNotEqual(before.NodeId, clone.NodeId);
        Assert.IsTrue(outgoing.IsCancellationRequested);
        Assert.IsFalse((await node.Peers.GetStatusAsync()).SharingEnabled);
    }

    [TestMethod]
    public async Task ExpiredOrSelfPairingCannotIssueGrantAndLegacySignaturesAreNotNodeCredentials()
    {
        using var node = new TestNode();
        await node.Peers.SetSharingAsync(true);
        var local = await node.Identity.GetAsync();
        await Assert.ThrowsExactlyAsync<FederationAccessException>(() => node.Peers.RequestPairingAsync(
            new(local.NodeId, "Self", "self-transaction", NodeRequestSignature.RandomToken())));
        var grant = await node.GrantAsync();
        var signature = NodeRequestSignature.Create(grant, "POST", "/federation/v1/export/queries", "",
            NodeRequestSignature.Hash([]), node.Clock.GetUtcNow());
        node.Clock.Now += TimeSpan.FromMinutes(6);
        Assert.AreEqual("SignatureExpired", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            node.Auth.AuthenticateAsync(signature, "POST", "/federation/v1/export/queries", "", NodeRequestSignature.Hash([])))).ErrorCode);
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
            await Peers.SetSharingAsync(true);
            var request = new NodePairRequest("reader-a", "Reader A", NodeRequestSignature.RandomToken(18), NodeRequestSignature.RandomToken());
            await Peers.RequestPairingAsync(request);
            await Peers.ApproveAsync(request.TransactionId);
            return (await Peers.ClaimPairingAsync(new(request.TransactionId, request.NodeId, request.ClaimSecret))).Credentials!;
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
