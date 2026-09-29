using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Queries;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Modules.RemoteAccess.Services;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

/// <summary>
/// "Create a new device identity" on an installation whose data directory was copied from
/// another computer: the copy must stop answering as that computer — for library sharing and
/// for remote access alike — or each takes the other for itself.
/// </summary>
[TestClass]
public sealed class FederationIdentityResetTests
{
    [TestMethod]
    public async Task A_new_device_identity_replaces_the_installs_identity_and_what_was_paired_under_the_old_one()
    {
        using var install = new Install("copied-server-id");
        var copied = await install.Identity.GetAsync();
        Assert.AreEqual("copied-server-id", copied.NodeId);
        var paired = await install.PairDeviceAsync();
        var hungUp = false;
        install.Connections.Track(paired, "hub-connection", () => hungUp = true);
        await install.Devices.RequestPairingAsync("Tablet", RemoteDevicePlatform.Android, null);

        await install.ResetAsync(asNewNode: true);

        var serverId = await install.RemoteAccess.GetOrCreateServerIdAsync();
        Assert.AreNotEqual("copied-server-id", serverId);
        Assert.AreEqual(serverId, install.Options.ServerId, "the new identity was not kept");
        var node = await install.Identity.GetAsync();
        Assert.AreEqual(serverId, node.NodeId, "the node and its install no longer answer under one id");
        Assert.AreNotEqual(copied.LibraryEpoch, node.LibraryEpoch);

        // Paired with the other install's identity, and with keys that install issued.
        Assert.IsFalse(install.Devices.HasAnyDevice);
        Assert.AreEqual(0, install.Devices.GetPendingRequests().Count);
        Assert.IsTrue(hungUp, "a paired device's hub connection outlived the reset");

        // Nothing else about remote access changes.
        Assert.AreEqual(RemoteAccessMode.Enabled, install.RemoteAccess.GetEffectiveMode());
        Assert.IsTrue(install.RemoteAccess.GetRequirePairing());
    }

    [TestMethod]
    public async Task Restoring_a_library_keeps_the_installs_identity_and_its_paired_devices()
    {
        using var install = new Install("this-server-id");
        var before = await install.Identity.GetAsync();
        var paired = await install.PairDeviceAsync();

        await install.ResetAsync(asNewNode: false);

        Assert.AreEqual("this-server-id", await install.RemoteAccess.GetOrCreateServerIdAsync());
        Assert.AreEqual(before.NodeId, (await install.Identity.GetAsync()).NodeId);
        Assert.IsNotNull(install.Devices.Find(paired));
    }

    private sealed class Install : IFederationDataDirectory, IRemoteAccessDataDirectory, IListeningAddressProvider,
        IDisposable
    {
        private readonly string _root =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "federation-identity-reset-" + Guid.NewGuid().ToString("N"));

        private readonly GrantLeaseRegistry _leases = new();
        private readonly FederatedQueryCoordinator _queries = new(null!);
        private readonly FederationBrowsingControl _browsing;
        private readonly FederationPeerController _controller;

        public RemoteAccessOptions Options { get; }
        public RemoteAccessService RemoteAccess { get; }
        public INodeIdentityProvider Identity { get; }
        public RemoteDeviceService Devices { get; }
        public RemoteConnectionRegistry Connections { get; } = new();

        public Install(string serverId)
        {
            Options = new RemoteAccessOptions
            {
                ServerId = serverId, Mode = RemoteAccessMode.Enabled, RequirePairing = true
            };
            var manager = new TestBOptionsManager<RemoteAccessOptions>(Options);
            RemoteAccess = new RemoteAccessService(manager, new RemoteAccessDefaults(RemoteAccessMode.Disabled),
                new RemoteAccessHostInfo("1.0.0-test"), this, NullLogger<RemoteAccessService>.Instance);
            // The host's own bridge: the node inherits the install's identity.
            var store = new FederationStateStore(this, new FederationNodeIdSource(RemoteAccess));
            Identity = new NodeIdentityProvider(store);
            var peers = new FederationPeerService(store, Identity, _leases, TimeProvider.System);
            Devices = new RemoteDeviceService(new RemoteDeviceStore(this));
            _browsing = new FederationBrowsingControl(store, _queries, new FederationMediaSessions());
            _controller = new FederationPeerController(peers, null!, Identity, null!, null!, null!, null!, null!,
                RemoteAccess, manager, TimeProvider.System, null!, store);
        }

        string IFederationDataDirectory.Path => System.IO.Path.Combine(_root, "federation");

        string IFederationDataDirectory.Ensure() =>
            Directory.CreateDirectory(System.IO.Path.Combine(_root, "federation")).FullName;

        string IRemoteAccessDataDirectory.Path => System.IO.Path.Combine(_root, "remote-access");

        string IRemoteAccessDataDirectory.Ensure() =>
            Directory.CreateDirectory(System.IO.Path.Combine(_root, "remote-access")).FullName;

        public IReadOnlyList<string> GetListeningAddresses() => [];

        public async Task<string> PairDeviceAsync()
        {
            var code = await Devices.IssuePairingCodeAsync();
            return (await Devices.PairWithCodeAsync(code.Code, "Phone", RemoteDevicePlatform.Android)).Credentials!
                .DeviceId;
        }

        public Task ResetAsync(bool asNewNode) =>
            _controller.Reset(new FederationIdentityResetRequest(asNewNode), _browsing, Devices, Connections, default);

        public void Dispose()
        {
            _browsing.Dispose();
            _queries.Dispose();
            _leases.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
