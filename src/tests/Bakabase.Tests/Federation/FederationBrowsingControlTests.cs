using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Queries;
using Bakabase.Modules.Federation.Security;
using Bakabase.Service.Components.Federation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class FederationBrowsingControlTests
{
    [TestMethod]
    public async Task OptInPersistsAndDisablingCancelsReadsClearsMediaWithoutClosingInboundSharing()
    {
        using var directory = new StateDirectory();
        var store = new FederationStateStore(directory, directory);
        using var grants = new GrantLeaseRegistry();
        var peers = new FederationPeerService(store, new NodeIdentityProvider(store), grants, TimeProvider.System);
        await peers.SetSharingAsync(true);
        using var queries = new FederatedQueryCoordinator(null!);
        var media = new FederationMediaSessions();
        using var control = new FederationBrowsingControl(store, queries, media);
        Assert.IsFalse(await control.IsEnabledAsync());
        Assert.AreEqual("BrowsingDisabled", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            control.GetLifetimeAsync(default))).ErrorCode);
        await control.SetEnabledAsync(true, default);
        var active = await control.GetLifetimeAsync(default);
        Assert.IsTrue(await new FederationStateStore(directory, directory).IsBrowsingEnabledAsync());
        var reference = new ResourceRef("self", "epoch", 1);
        var asset = new FederatedAsset(new string('a', 64), "audio", "track.wav", "audio/wav", null, null,
            DateTimeOffset.UtcNow.AddMinutes(3));
        var detail = new FederatedResourceDetail(reference, "Self", "Track", "track.wav", "HasFile", [], [], [], [], [asset], null);
        media.Remember(detail, null, active);
        var source = media.GetAsset(new AssetRef(reference, asset.AssetId));
        var ticket = media.Issue(source, null, active);
        await control.SetEnabledAsync(false, default);
        Assert.IsTrue(active.IsCancellationRequested);
        Assert.IsTrue(await store.IsSharingEnabledAsync());
        Assert.AreEqual("AssetExpired", Assert.ThrowsExactly<FederationQueryException>(() => media.GetTicket(ticket.Id)).Code);
        Assert.ThrowsExactly<OperationCanceledException>(() => media.Remember(detail, null, active));
        Assert.ThrowsExactly<OperationCanceledException>(() => media.Issue(source, null, active));
        await control.SetEnabledAsync(true, default);
        Assert.IsFalse((await control.GetLifetimeAsync(default)).IsCancellationRequested);
        Assert.IsTrue(active.IsCancellationRequested);
        Assert.AreEqual("AssetExpired", Assert.ThrowsExactly<FederationQueryException>(() => media.GetAsset(source.Ref)).Code);
    }

    internal sealed class StateDirectory : IFederationDataDirectory, INodeIdSource, IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "federation-browsing-" + Guid.NewGuid().ToString("N"));
        public string Ensure() { Directory.CreateDirectory(Path); return Path; }
        public Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("self");
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
