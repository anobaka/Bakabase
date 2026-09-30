using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Queries;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Modules.RemoteAccess.Services;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Implementations;
using Bakabase.Tests.RemoteAccess.Console;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

/// <summary>
/// "Make this a new device" on an installation whose data directory was copied from
/// another computer: the copy must stop answering as that computer — for library sharing and
/// for remote access alike — or each takes the other for itself. It stops managing the servers
/// the original manages too, here only: their keys are the original's as well.
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

        await install.ResetAsync(asNewNode: true, replaceInstallIdentity: true);

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
    public async Task Recovering_an_unreadable_sharing_state_keeps_the_installs_identity_and_its_paired_devices()
    {
        // Only the sharing state was lost. The devices that manage this one still expect this
        // install's identity, and a new one would leave every one of them facing a stranger.
        using var install = new Install("this-server-id");
        var paired = await install.PairDeviceAsync();
        var hungUp = false;
        install.Connections.Track(paired, "hub-connection", () => hungUp = true);
        await File.WriteAllTextAsync(install.FederationStateFile, "{broken");
        var unreadable = await Assert.ThrowsExactlyAsync<FederationAccessException>(() => install.Identity.GetAsync());
        Assert.AreEqual("SharingStateUnavailable", unreadable.ErrorCode);
        var managed = new FakeManagedServerService();

        await install.ResetAsync(asNewNode: true, services: Desktop(managed));

        Assert.AreEqual("this-server-id", await install.RemoteAccess.GetOrCreateServerIdAsync());
        Assert.AreEqual("this-server-id", install.Options.ServerId);
        Assert.IsNotNull(install.Devices.Find(paired));
        Assert.IsFalse(hungUp, "a paired device was hung up");
        Assert.IsTrue(managed.Calls.IsEmpty, "the servers this device manages were touched");

        // A new node, of its own id, as before the install's identity could be replaced with it.
        Assert.AreNotEqual("this-server-id", (await install.Identity.GetAsync()).NodeId);
    }

    [TestMethod]
    public async Task Restoring_a_library_keeps_the_installs_identity_and_its_paired_devices()
    {
        using var install = new Install("this-server-id");
        var before = await install.Identity.GetAsync();
        var paired = await install.PairDeviceAsync();
        var managed = new FakeManagedServerService();

        await install.ResetAsync(asNewNode: false, services: Desktop(managed));

        Assert.AreEqual("this-server-id", await install.RemoteAccess.GetOrCreateServerIdAsync());
        Assert.AreEqual(before.NodeId, (await install.Identity.GetAsync()).NodeId);
        Assert.IsNotNull(install.Devices.Find(paired));
        Assert.IsTrue(managed.Calls.IsEmpty, "the servers this device manages were touched");
    }

    [TestMethod]
    public async Task A_new_install_identity_without_a_new_node_is_refused_rather_than_left_out()
    {
        // Answered 200 with a new library generation only, the caller would take the install's
        // identity for replaced while it still answers as the one it was copied from.
        using var install = new Install("copied-server-id");
        var before = await install.Identity.GetAsync();
        var paired = await install.PairDeviceAsync();

        var refused = await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            install.ResetAsync(asNewNode: false, replaceInstallIdentity: true));

        Assert.AreEqual("InvalidIdentityReset", refused.ErrorCode);
        Assert.AreEqual(400, refused.StatusCode);
        Assert.AreEqual("copied-server-id", await install.RemoteAccess.GetOrCreateServerIdAsync());
        Assert.AreEqual(before, await install.Identity.GetAsync());
        Assert.IsNotNull(install.Devices.Find(paired));
    }

    [TestMethod]
    public async Task A_new_device_identity_stops_managing_every_server_here_without_telling_any_of_them()
    {
        // The copy's keys are the original install's: the servers know the two as one paired
        // device, and a revoke from the copy would cut the original off as well.
        await using var console = await ConsoleHarness.StartAsync();
        await using var desk = await FakeServer.StartAsync("server-desk", "Desk", 46900);
        await using var study = await FakeServer.StartAsync("server-study", "Study", 46900);
        var (original, _) = await console.AddManagedAsync(desk);
        Assert.IsNotNull(await console.Manager.OpenAsync(desk.ServerId, null));
        Assert.AreEqual(ManagedServerOutcome.AwaitingApproval,
            (await console.Manager.PairAsync(study.BaseAddress, null)).Outcome);
        var deskSeen = desk.Requests.Count;
        using var install = new Install("copied-server-id");

        await install.ResetAsync(asNewNode: true, replaceInstallIdentity: true,
            services: Desktop(console.Manager));

        Assert.AreNotEqual("copied-server-id", await install.RemoteAccess.GetOrCreateServerIdAsync());
        Assert.AreEqual(0, console.Store.Read().Servers.Count, "a managed server and its key were kept");
        Assert.AreEqual(0, console.Manager.RunningRelays.Count, "a relay outlived its key");
        var listing = await console.Manager.GetAsync(false);
        Assert.AreEqual(0, listing.Servers.Count);
        Assert.AreEqual(0, listing.Requests.Count, "a request this copy filed is still waited on");

        // An approval that comes now is for nobody.
        study.Approved = true;
        await Task.Delay(300);
        var studySeen = study.Requests.Count;
        await Task.Delay(500);
        Assert.AreEqual(studySeen, study.Requests.Count, "the filed request was still being claimed");
        Assert.AreEqual(0, console.Store.Read().Servers.Count);

        // Nothing reached the server, and it still lets the original in.
        Assert.AreEqual(deskSeen, desk.Requests.Count, "the reset talked to a managed server");
        Assert.IsTrue(desk.KnownDevices.ContainsKey(original), "the original install was cut off");

        // Paired again from the copy, with a key of its own.
        desk.PairingCode = "123456";
        Assert.AreEqual(ManagedServerOutcome.Ok, (await console.Manager.PairAsync(desk.BaseAddress, "123456")).Outcome);
        Assert.AreNotEqual(original, console.Store.Find(desk.ServerId)!.DeviceId);
        Assert.IsNotNull(await console.Manager.OpenAsync(desk.ServerId, null));
    }

    [TestMethod]
    public async Task A_headless_server_which_manages_nothing_still_becomes_a_new_device()
    {
        using var install = new Install("copied-server-id");
        var paired = await install.PairDeviceAsync();
        // Composed as a NAS or Docker server is: no IManagedServerService.
        var headless = new ServiceCollection().AddLogging().BuildServiceProvider();

        await install.ResetAsync(asNewNode: true, replaceInstallIdentity: true, services: headless);

        var serverId = await install.RemoteAccess.GetOrCreateServerIdAsync();
        Assert.AreNotEqual("copied-server-id", serverId);
        Assert.AreEqual(serverId, (await install.Identity.GetAsync()).NodeId);
        Assert.IsNull(install.Devices.Find(paired));
    }

    [TestMethod]
    public async Task Servers_that_cannot_be_forgotten_leave_the_install_as_it_was()
    {
        // Forgotten first, while nothing else has changed: a new identity with the old managed
        // servers — or the old identity without its paired devices — would be half a reset.
        using var install = new Install("copied-server-id");
        var before = await install.Identity.GetAsync();
        var paired = await install.PairDeviceAsync();
        var managed = new FakeManagedServerService { ForgetAllFailure = new IOException("The disk is full.") };

        var refused = await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            install.ResetAsync(asNewNode: true, replaceInstallIdentity: true, services: Desktop(managed)));

        Assert.AreEqual("ManagedServersNotForgotten", refused.ErrorCode);
        Assert.AreEqual(500, refused.StatusCode);
        CollectionAssert.AreEqual(new[] { "ForgetAllLocally" }, managed.Calls.ToArray());
        Assert.AreEqual("copied-server-id", await install.RemoteAccess.GetOrCreateServerIdAsync());
        Assert.AreEqual("copied-server-id", install.Options.ServerId);
        Assert.AreEqual(before, await install.Identity.GetAsync());
        Assert.IsNotNull(install.Devices.Find(paired));
    }

    /// <summary>The desktop app's request services: the relays it composes, and nothing else this reads.</summary>
    private static IServiceProvider Desktop(IManagedServerService managed) =>
        new ServiceCollection().AddLogging().AddSingleton(managed).BuildServiceProvider();

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

        public string FederationStateFile =>
            System.IO.Path.Combine(((IFederationDataDirectory) this).Ensure(), FederationStateStore.FileName);

        /// <param name="services">
        /// The request's services, where the controller finds what the host composed; none at all
        /// when left out.
        /// </param>
        public Task ResetAsync(bool asNewNode, bool replaceInstallIdentity = false, IServiceProvider? services = null)
        {
            _controller.ControllerContext = services == null
                ? new ControllerContext()
                : new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } };

            return _controller.Reset(new FederationIdentityResetRequest(asNewNode, replaceInstallIdentity), _browsing,
                Devices, Connections, default);
        }

        public void Dispose()
        {
            _browsing.Dispose();
            _queries.Dispose();
            _leases.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
