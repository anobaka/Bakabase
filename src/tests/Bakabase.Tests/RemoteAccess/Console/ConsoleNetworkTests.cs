using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// How the desktop app reaches a server it manages that is stored by name, through the
/// handlers it really composes — the console's probes and pairing, and a relay's forwarding —
/// with only the network underneath being the test's (<see cref="TestNetwork"/>).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ConsoleNetworkTests
{
    [TestMethod]
    public async Task A_server_known_by_a_name_that_resolves_IPv6_first_is_probed_paired_and_shown_at_once()
    {
        // As Windows resolves a computer name: its IPv6 address first, where the other device's
        // firewall drops every connection, while the server listens on IPv4. A browser on the
        // same machine opens it at once, and so must the app.
        var network = new TestNetwork();
        await using var console = await ConsoleHarness.StartAsync(options: o => o.Connector = network.Connector);
        await using var desk = await FakeServer.StartAsync("server-desk", "Desk", 47100);
        desk.PairingCode = "123456";
        network.Name("desk-pc", TestNetwork.Dropped, IPAddress.Loopback);
        var address = $"http://desk-pc:{desk.Port}";

        var watch = Stopwatch.StartNew();

        Assert.AreEqual(ManagedServerOutcome.Ok, (await console.Manager.ProbeAsync(address)).Outcome);
        Assert.AreEqual(ManagedServerOutcome.Ok, (await console.Manager.PairAsync(address, "123456")).Outcome);
        Assert.AreEqual(address, console.Store.Find("server-desk")!.BaseAddress);
        Assert.AreEqual(ManagedServerState.Online, (await console.Manager.GetAsync(true)).Servers.Single().State);

        var port = ConsoleHarness.PortOf((await console.Manager.OpenAsync("server-desk", null))!.Url);
        var response = await ConsoleHarness.SendToRelayAsync(port, "/resource/search");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(desk.Requests.Any(r => r.Path == "/resource/search" && r.SignatureValid == true),
            string.Join("\n", desk.Requests));
        // Waiting out the dropped address would take the ten seconds a connection may take.
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [TestMethod]
    public async Task A_relay_connects_to_the_address_that_said_who_it_is_not_to_another_its_name_resolves_to()
    {
        // One name, two installs behind it. The desk answers the question at once and is slow to
        // accept the connection right after it: raced again, that connection would go to the
        // other one, signed as this device.
        var network = new TestNetwork();
        await using var console = await ConsoleHarness.StartAsync(options: o =>
        {
            o.Connector = network.Connector;
            o.IdentityConnectionWindow = TimeSpan.FromMilliseconds(100);
        });
        await using var desk = await FakeServer.StartAsync("server-desk", "Desk", 47100);
        await using var other = await FakeServer.StartAsync("server-other", "Other", 47150);
        var deskAt = IPAddress.Parse("192.0.2.10");
        var otherAt = IPAddress.Parse("192.0.2.11");
        network.Name("desk-pc", deskAt, otherAt);
        var toDesk = network.RouteTo(deskAt, desk);
        network.RouteTo(otherAt, other);
        await console.AddManagedAsync(desk, baseAddress: $"http://desk-pc:{desk.Port}");

        var port = ConsoleHarness.PortOf((await console.Manager.OpenAsync("server-desk", null))!.Url);

        // Past the connection window: the relay's next connection asks again first.
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        toDesk.SlowAfter(1, TimeSpan.FromMilliseconds(800));

        var response = await ConsoleHarness.SendToRelayAsync(port, "/resource/search");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(desk.Requests.Any(r => r.Path == "/resource/search" && r.SignatureValid == true),
            string.Join("\n", desk.Requests));
        CollectionAssert.AreEqual(Array.Empty<string>(), other.Requests.Select(r => $"{r.Method} {r.Path}").ToArray());
    }
}
