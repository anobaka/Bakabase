using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Controllers;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class FederationSharingWizardTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PreservesLegacyIdentityAndUnchangedSettingsWhenIdentityInitializationTriggersDefaultReload(bool firstRun)
    {
        using var directory = new DirectoryFixture();
        using var leases = new GrantLeaseRegistry();
        var options = new RacingOptions(new RemoteAccessOptions
        {
            Mode = RemoteAccessMode.Unrestricted,
            AllowLiveTranscode = true,
            ServerId = firstRun ? null : "legacy-server-id"
        });
        var store = new FederationStateStore(directory, directory);
        var node = await store.GetIdentityAsync();
        // The cloned node ID deliberately differs from the legacy server ID.
        var identity = new ReloadingIdentity(node, options);
        var peers = new FederationPeerService(store, identity, leases, TimeProvider.System);
        var legacy = new LegacyIdentity(firstRun ? "new-legacy-server-id" : "legacy-server-id");
        var controller = new FederationPeerController(peers, null!, identity, null!, null!, null!, null!, null!,
            legacy, options, TimeProvider.System, null!, store);

        await controller.Sharing(new FederationSharingRequest(true, true), default);

        Assert.AreEqual(firstRun ? "new-legacy-server-id" : "legacy-server-id", options.Value.ServerId);
        Assert.AreNotEqual(node.NodeId, options.Value.ServerId);
        Assert.IsTrue(options.Value.AllowLiveTranscode);
        Assert.IsTrue(options.Value.RequirePairing);
        Assert.AreEqual(RemoteAccessMode.Enabled, options.Value.Mode);
        Assert.AreEqual(1, options.SaveCount);
        Assert.AreEqual(firstRun ? 1 : 0, legacy.Calls);
        Assert.IsTrue(await store.IsSharingEnabledAsync());
    }

    private sealed class RacingOptions(RemoteAccessOptions value) : IBOptionsManager<RemoteAccessOptions>
    {
        public RemoteAccessOptions Value { get; private set; } = value;
        public int SaveCount { get; private set; }
        public void ReloadDefaults() => Value = new();
        public void Save(RemoteAccessOptions options) { Value = options; SaveCount++; }
        public Task SaveAsync(RemoteAccessOptions options) { Save(options); return Task.CompletedTask; }
        public Task SaveAsync(Action<RemoteAccessOptions> modify) { modify(Value); SaveCount++; return Task.CompletedTask; }
    }

    private sealed class ReloadingIdentity(NodeIdentity node, RacingOptions options) : INodeIdentityProvider
    {
        private bool _first = true;
        public async Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default)
        {
            if (_first)
            {
                _first = false;
                await Task.Yield();
                options.ReloadDefaults();
            }
            return node;
        }
    }

    private sealed class DirectoryFixture : IFederationDataDirectory, INodeIdSource, IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "federation-sharing-wizard-" + Guid.NewGuid().ToString("N"));
        public string Ensure() { Directory.CreateDirectory(Path); return Path; }
        public Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("cloned-federation-node");
        public void Dispose() => Directory.Delete(Path, true);
    }

    private sealed class LegacyIdentity(string serverId) : IRemoteAccessService
    {
        public int Calls { get; private set; }
        public Task<string> GetOrCreateServerIdAsync() { Calls++; return Task.FromResult(serverId); }
        public RemoteAccessMode GetEffectiveMode() => throw new NotSupportedException();
        public Task SetModeAsync(RemoteAccessMode? mode) => throw new NotSupportedException();
        public IReadOnlyList<RemoteAccessAddress> GetReachableAddresses() => throw new NotSupportedException();
        public bool GetAllowLiveTranscode() => throw new NotSupportedException();
        public Task SetAllowLiveTranscodeAsync(bool allow) => throw new NotSupportedException();
        public bool GetRequirePairing() => throw new NotSupportedException();
        public Task SetRequirePairingAsync(bool require) => throw new NotSupportedException();
        public Task<RemoteAccessServerDescriptor> GetServerDescriptorAsync() => throw new NotSupportedException();
    }
}
