using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Remoting.Components.Forwarding;
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

    /// <summary>What a proxy on this computer in fake-IP mode answers a name with (198.18.0.0/15).</summary>
    private static readonly IPAddress ProxyAddress = IPAddress.Parse("198.18.0.29");

    [TestMethod]
    public async Task A_name_a_proxy_on_this_computer_took_over_is_said_to_be_one_and_nothing_goes_there()
    {
        // As Clash in fake-IP mode answered jaxs-Mac-mini.local on the Windows PC in the field.
        var network = new TestNetwork();
        await using var console = await ConsoleHarness.StartAsync(options: o => o.Connector = network.Connector);
        await using var nas = await FakeServer.StartAsync("server-nas", "NAS", 47100);
        nas.PairingCode = "123456";
        // Were the proxy's address dialled, it would reach the server: nothing may.
        network.RouteTo(ProxyAddress, nas);
        network.Name("nas.local", ProxyAddress);
        var address = $"http://nas.local:{nas.Port}";

        var probe = await console.Manager.ProbeAsync(address);

        Assert.AreEqual(ManagedServerOutcome.ProxyFakeAddress, probe.Outcome);
        StringAssert.Contains(probe.Detail, "198.18.0.29");
        Assert.AreEqual(ManagedServerOutcome.ProxyFakeAddress, (await console.Manager.PairAsync(address, "123456")).Outcome);
        Assert.AreEqual(ManagedServerOutcome.ProxyFakeAddress, (await console.Manager.PairAsync(address, null)).Outcome);
        // Typed as it is, the proxy's address is refused the same way.
        Assert.AreEqual(ManagedServerOutcome.ProxyFakeAddress,
            (await console.Manager.ProbeAsync($"{ProxyAddress}:{nas.Port}")).Outcome);

        CollectionAssert.AreEqual(Array.Empty<string>(), nas.Requests.Select(r => $"{r.Method} {r.Path}").ToArray());
        Assert.AreEqual(0, console.Store.Read().Servers.Count);
    }

    [TestMethod]
    public async Task A_managed_server_whose_name_a_proxy_took_over_is_offline_for_that_and_its_relay_says_so()
    {
        var network = new TestNetwork();
        await using var console = await ConsoleHarness.StartAsync(options: o =>
        {
            o.Connector = network.Connector;
            o.IdentityRetryInterval = TimeSpan.FromMilliseconds(100);
        });
        await using var nas = await FakeServer.StartAsync("server-nas", "NAS", 47100);
        network.RouteTo(ProxyAddress, nas);
        network.Name("nas.local", ProxyAddress);
        await console.AddManagedAsync(nas, baseAddress: $"http://nas.local:{nas.Port}");

        var listed = (await console.Manager.GetAsync(true)).Servers.Single();

        Assert.AreEqual(ManagedServerState.Offline, listed.State);
        Assert.AreEqual(ManagedServerOutcome.ProxyFakeAddress, listed.OfflineReason);

        var port = ConsoleHarness.PortOf((await console.Manager.OpenAsync("server-nas", null))!.Url);

        // A navigation is shown the proxy, and the way out of it, in both languages.
        var page = await ConsoleHarness.NavigateAsync($"http://127.0.0.1:{port}/", "same-origin");
        var html = await page.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, page.StatusCode);
        StringAssert.Contains(html, "A proxy on this device is in the way of NAS");
        StringAssert.Contains(html, "fake-ip-filter");
        StringAssert.Contains(html, "本机的代理软件拦住了 NAS");
        Assert.IsFalse(html.Contains("is running", StringComparison.Ordinal), html);

        // A fetch is told the same, in the refusal the frontend reads.
        var fetch = await ConsoleHarness.SendToRelayAsync(port, "/resource/search");
        var message = JsonDocument.Parse(await fetch.Content.ReadAsStringAsync()).RootElement
            .GetProperty("message").GetString()!;

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, fetch.StatusCode);
        Assert.AreEqual(nameof(ClientForwardingFailure.ServerUnreachable),
            fetch.Headers.GetValues(UpstreamForwarder.FailureHeader).Single());
        StringAssert.Contains(message, $"nas.local:{nas.Port} leads into a proxy on this computer");
        StringAssert.Contains(message, "“+.local”");
        CollectionAssert.AreEqual(Array.Empty<string>(), nas.Requests.Select(r => $"{r.Method} {r.Path}").ToArray());

        // The name set to DIRECT in the proxy: it resolves to the server again, and the relay
        // goes back to forwarding once it has asked who answers there.
        network.Name("nas.local", IPAddress.Loopback);
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        Assert.AreEqual(HttpStatusCode.OK, (await ConsoleHarness.SendToRelayAsync(port, "/resource/search")).StatusCode);

        listed = (await console.Manager.GetAsync(true)).Servers.Single();
        Assert.AreEqual(ManagedServerState.Online, listed.State);
        Assert.IsNull(listed.OfflineReason);
    }
}
