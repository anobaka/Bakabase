using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// Several managed servers shown through their own relays at once, each signing with its
/// own key and each keeping its origin across restarts.
/// </summary>
/// <remarks>
/// <para>
/// The thin client had one relay and one key, so "which key signs this" could not go wrong.
/// Here every relay is composed from the same code against the same store, and the failure
/// worth guarding is quiet: a request to one server's window signed with another server's
/// key is refused as a revoked device, which reads exactly like the user having been locked
/// out.
/// </para>
/// <para>
/// Stable ports are the other half. The browser keys localStorage to the origin, port
/// included, so a server whose relay moved between launches loses every setting its UI keeps
/// in the browser — silently, since nothing failed.
/// </para>
/// </remarks>
[TestClass]
public class ManagedServerRelayTests
{
    private ConsoleHarness _console = null!;
    private FakeServer _desk = null!;
    private FakeServer _nas = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _console = await ConsoleHarness.StartAsync();
        _desk = await FakeServer.StartAsync("server-desk", "Desk", 46900);
        _nas = await FakeServer.StartAsync("server-nas", "NAS", 46900);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _console.DisposeAsync();
        await _desk.DisposeAsync();
        await _nas.DisposeAsync();
    }

    [TestMethod]
    public async Task Two_servers_run_at_once_each_signed_with_its_own_key()
    {
        var (deskDevice, _) = await _console.AddManagedAsync(_desk);
        var (nasDevice, _) = await _console.AddManagedAsync(_nas);

        var deskUrl = (await _console.Manager.OpenAsync(_desk.ServerId, null))!.Url;
        var nasUrl = (await _console.Manager.OpenAsync(_nas.ServerId, null))!.Url;

        var deskPort = ConsoleHarness.PortOf(deskUrl);
        var nasPort = ConsoleHarness.PortOf(nasUrl);

        Assert.AreNotEqual(deskPort, nasPort);
        Assert.AreEqual(2, _console.Manager.RunningRelays.Count);

        var deskResponse = await ConsoleHarness.SendToRelayAsync(deskPort, "/resource/search?page=2");
        var nasResponse = await ConsoleHarness.SendToRelayAsync(nasPort, "/resource/search?page=3");

        Assert.AreEqual(HttpStatusCode.OK, deskResponse.StatusCode, string.Join("\n", _console.Logs));
        Assert.AreEqual(HttpStatusCode.OK, nasResponse.StatusCode, string.Join("\n", _console.Logs));

        var atDesk = _desk.Requests.Single(r => r.Path == "/resource/search");
        var atNas = _nas.Requests.Single(r => r.Path == "/resource/search");

        // Each arrived where it was sent, signed as the device that server knows, with a
        // signature that server can verify — not merely a header that looks right.
        Assert.AreEqual("page=2", atDesk.Query);
        Assert.AreEqual(deskDevice, atDesk.DeviceId);
        Assert.IsTrue(atDesk.SignatureValid);

        Assert.AreEqual("page=3", atNas.Query);
        Assert.AreEqual(nasDevice, atNas.DeviceId);
        Assert.IsTrue(atNas.SignatureValid);
    }

    [TestMethod]
    public async Task A_post_through_a_relay_is_signed_body_and_all()
    {
        await _console.AddManagedAsync(_desk);
        var port = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_desk.ServerId, null))!.Url);

        var response = await ConsoleHarness.SendToRelayAsync(port, "/resource/search", HttpMethod.Post,
            "{\"keyword\":\"x\"}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(_desk.Requests.Single(r => r.Path == "/resource/search").SignatureValid);
    }

    [TestMethod]
    public async Task A_hub_connection_goes_through_a_relay_signed()
    {
        var (deviceId, _) = await _console.AddManagedAsync(_desk);
        var port = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_desk.ServerId, null))!.Url);

        // The relay is a slim host, unlike the thin client's: upgrades must still pass.
        using var socket = new System.Net.WebSockets.ClientWebSocket();
        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/hub/echo"), timeout.Token);

        await socket.SendAsync("ping"u8.ToArray(), System.Net.WebSockets.WebSocketMessageType.Text, true,
            timeout.Token);
        var buffer = new byte[64];
        var echoed = await socket.ReceiveAsync(buffer, timeout.Token);

        Assert.AreEqual("ping", System.Text.Encoding.UTF8.GetString(buffer, 0, echoed.Count));

        var upgrade = _desk.Requests.Single(r => r.Path == "/hub/echo");
        Assert.AreEqual(deviceId, upgrade.DeviceId);
        Assert.IsTrue(upgrade.SignatureValid);
    }

    [TestMethod]
    public async Task Relay_ports_survive_a_restart()
    {
        await _console.AddManagedAsync(_desk);
        await _console.AddManagedAsync(_nas);

        var deskPort = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_desk.ServerId, null))!.Url);
        var nasPort = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_nas.ServerId, null))!.Url);

        // Remembered with the server, not only in memory.
        Assert.AreEqual(deskPort, _console.Store.Find(_desk.ServerId)!.RelayPort);
        Assert.AreEqual(nasPort, _console.Store.Find(_nas.ServerId)!.RelayPort);

        var root = _console.Root;
        await _console.StopAsync();

        _console = await ConsoleHarness.StartAsync(root);

        // Opened in the other order this time: the port is the server's, not the launch's.
        var nasAgain = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_nas.ServerId, null))!.Url);
        var deskAgain = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_desk.ServerId, null))!.Url);

        Assert.AreEqual(deskPort, deskAgain);
        Assert.AreEqual(nasPort, nasAgain);
    }

    [TestMethod]
    public async Task A_port_another_server_owns_is_not_handed_out_while_its_relay_is_stopped()
    {
        await _console.AddManagedAsync(_desk);
        var deskPort = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_desk.ServerId, null))!.Url);

        var root = _console.Root;
        await _console.StopAsync();
        _console = await ConsoleHarness.StartAsync(root);

        await _console.AddManagedAsync(_nas);

        // The desk's relay has not started in this session, and its port is free — but it is
        // the desk's, and giving it to the NAS would hand the NAS the desk's browser storage.
        var nasPort = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_nas.ServerId, null))!.Url);

        Assert.AreNotEqual(deskPort, nasPort);
    }

    [TestMethod]
    public async Task Open_hands_out_a_single_use_ticket_for_that_relay()
    {
        await _console.AddManagedAsync(_desk);

        var url = new Uri((await _console.Manager.OpenAsync(_desk.ServerId, "/#/resource"))!.Url);

        Assert.AreEqual("127.0.0.1", url.Host);
        Assert.AreEqual("/", url.AbsolutePath);
        Assert.AreEqual("#/resource", url.Fragment);
        StringAssert.StartsWith(url.Query, $"?{RelayNavigationTokens.QueryName}=");

        var token = Uri.UnescapeDataString(url.Query[(RelayNavigationTokens.QueryName.Length + 2)..]);
        var tokens = _console.Get<RelayNavigationTokens>();

        // Minted for that relay's port and nothing else, and good once.
        Assert.IsFalse(tokens.TryConsume(token, url.Port + 1));

        var again = new Uri((await _console.Manager.OpenAsync(_desk.ServerId, null))!.Url);
        var secondToken = Uri.UnescapeDataString(again.Query[(RelayNavigationTokens.QueryName.Length + 2)..]);

        Assert.IsTrue(tokens.TryConsume(secondToken, again.Port));
        Assert.IsFalse(tokens.TryConsume(secondToken, again.Port));
    }

    [TestMethod]
    public async Task Forgetting_revokes_there_deletes_the_key_and_stops_the_relay()
    {
        var (deviceId, key) = await _console.AddManagedAsync(_desk);
        await _console.AddManagedAsync(_nas);

        var port = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_desk.ServerId, null))!.Url);
        Assert.AreEqual(HttpStatusCode.OK, (await ConsoleHarness.SendToRelayAsync(port, "/ping")).StatusCode);

        Assert.IsTrue(await _console.Manager.ForgetAsync(_desk.ServerId));

        // Asked the server, as this device, to forget this device.
        var revoke = _desk.Requests.Single(r => r.Method == "DELETE");
        Assert.AreEqual($"/remote-access/devices/{deviceId}", revoke.Path);
        Assert.IsTrue(revoke.SignatureValid);
        Assert.IsFalse(_desk.KnownDevices.ContainsKey(deviceId));

        // The key is gone from memory and from disk; the other server is untouched.
        Assert.IsNull(_console.Store.Find(_desk.ServerId));
        Assert.IsNotNull(_console.Store.Find(_nas.ServerId));
        StringAssert.DoesNotMatch(await System.IO.File.ReadAllTextAsync(_console.ManagedFile),
            new System.Text.RegularExpressions.Regex(System.Text.RegularExpressions.Regex.Escape(key)));

        Assert.IsFalse(_console.Manager.RunningRelays.ContainsKey(_desk.ServerId));
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => ConsoleHarness.SendToRelayAsync(port, "/ping"));

        Assert.IsNull(await _console.Manager.OpenAsync(_desk.ServerId, null));
    }

    [TestMethod]
    public async Task Forgetting_a_server_that_is_gone_still_forgets_it_here()
    {
        await _console.AddManagedAsync(_desk);
        await _desk.DisposeAsync();
        _desk = await FakeServer.StartAsync("placeholder", "Placeholder", 46950);

        Assert.IsTrue(await _console.Manager.ForgetAsync("server-desk"));
        Assert.IsNull(_console.Store.Find("server-desk"));
    }
}
