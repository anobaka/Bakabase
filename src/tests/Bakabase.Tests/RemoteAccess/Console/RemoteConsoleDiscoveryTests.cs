using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Console;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// Finding servers to manage: by the remote-access beacons every server answers, not by
/// library sharing, and never this device itself.
/// </summary>
/// <remarks>
/// Library sharing's search only lists a server whose sharing is switched on — off by
/// default, and unrelated to whether it can be managed — so a NAS left at its defaults never
/// appeared. The beacons are what the thin client found servers by.
/// </remarks>
[TestClass]
public class RemoteConsoleDiscoveryTests
{
    private ConsoleHarness _console = null!;

    [TestInitialize]
    public async Task Setup() => _console = await ConsoleHarness.StartAsync();

    [TestCleanup]
    public async Task Cleanup() => await _console.DisposeAsync();

    private static DiscoveredServer Beacon(string id, string name, string address, bool local = false,
        string version = "2.5.0") =>
        new(id, name, address, version, 1, local);

    [TestMethod]
    public async Task Other_servers_are_listed_once_each_and_this_device_never()
    {
        // Already managed here, though not reachable right now: still worth listing, and marked.
        await _console.Store.MutateAsync(data => data.Servers.Add(new ClientServerConnection
        {
            ServerId = "server-desk",
            ServerName = "Desk",
            BaseAddress = "http://192.168.1.7:34567",
            DeviceId = "device-1",
            DeviceKey = "key",
            PairedAt = DateTime.UtcNow
        }));

        _console.Discovery.Found =
        [
            // This device's own server, answering only over the LAN — the loopback answer does
            // not always arrive — so only its identity gives it away.
            Beacon(ConsoleHarness.OwnServerId, "Me", "http://192.168.1.2:34567"),
            // Something else answering from loopback: this machine, whatever it calls itself,
            // and so on its LAN address too.
            Beacon("server-loopback", "Also me", "http://127.0.0.1:5000", local: true),
            Beacon("server-loopback", "Also me", "http://192.168.1.2:5000"),
            // A NAS at its defaults, found by the probe and by mDNS.
            Beacon("server-nas", "NAS", "http://192.168.1.5:34567", version: "2.4.0"),
            Beacon("server-nas", "NAS", "http://nas.local:34567", version: "2.4.0"),
            Beacon("server-desk", "Desk", "http://192.168.1.7:34567"),
            // Not a usable answer.
            Beacon(" ", "Nameless", "http://192.168.1.99:34567")
        ];

        var found = await _console.Manager.DiscoverAsync();

        CollectionAssert.AreEqual(new[] {"server-nas", "server-desk"}, found.Servers.Select(s => s.ServerId).ToArray());

        var nas = found.Servers[0];
        Assert.AreEqual("NAS", nas.Name);
        Assert.AreEqual("http://192.168.1.5:34567", nas.Address, "the first answer's address, as discovery ranked it");
        Assert.AreEqual("2.4.0", nas.AppVersion);
        Assert.IsFalse(nas.AlreadyManaged);
        Assert.IsTrue(found.Servers[1].AlreadyManaged);

        // A bounded search, the one the thin client used.
        Assert.AreEqual(1, _console.Discovery.Searches);
        Assert.AreEqual(TimeSpan.FromSeconds(3), _console.Discovery.LastTimeout);
    }

    [TestMethod]
    public async Task Nothing_searches_the_network_until_somebody_asks()
    {
        // Starting, listing (with probing) and opening the devices page's data are not a
        // search: the beacons go out only when the user asks for nearby devices.
        await _console.Manager.GetAsync(true);
        _console.Manager.ListTargets();

        Assert.AreEqual(0, _console.Discovery.Searches);
    }

    [TestMethod]
    public async Task Nothing_found_is_an_empty_list_rather_than_an_error()
    {
        _console.Discovery.Found = [Beacon(ConsoleHarness.OwnServerId, "Me", "http://127.0.0.1:34567", local: true)];

        Assert.AreEqual(0, (await _console.Manager.DiscoverAsync()).Servers.Count);
    }

    [TestMethod]
    public void The_app_can_search_even_when_the_server_registered_no_discovery()
    {
        // What the desktop app's own server registers comes first and is kept; without it the
        // console brings the beacon discovery itself, so the endpoint always has one.
        var bare = new ServiceCollection().AddLogging().AddRemoteConsole();
        using (var provider = bare.BuildServiceProvider())
        {
            Assert.IsInstanceOfType<ServerDiscovery>(provider.GetRequiredService<IServerDiscovery>());
        }

        var existing = new FakeDiscovery();
        var composed = new ServiceCollection().AddLogging().AddSingleton<IServerDiscovery>(existing).AddRemoteConsole();
        using (var provider = composed.BuildServiceProvider())
        {
            Assert.AreSame(existing, provider.GetRequiredService<IServerDiscovery>());
        }

        Assert.AreEqual(1, bare.Count(d => d.ServiceType == typeof(IServerDiscovery)));
        Assert.AreEqual(1, composed.Count(d => d.ServiceType == typeof(IServerDiscovery)));
    }
}
