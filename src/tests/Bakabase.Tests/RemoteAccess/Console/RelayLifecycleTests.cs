using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Bakabase.Remoting.Components.Console;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// When relays start and stop, and which port each one gets: one per server however many
/// windows ask at once, gone with the server or the app, and never on a port something else
/// is using.
/// </summary>
[TestClass]
public class RelayLifecycleTests
{
    private ConsoleHarness _console = null!;
    private readonly List<FakeServer> _servers = [];

    [TestInitialize]
    public async Task Setup() => _console = await ConsoleHarness.StartAsync();

    [TestCleanup]
    public async Task Cleanup()
    {
        await _console.DisposeAsync();

        foreach (var server in _servers)
        {
            await server.DisposeAsync();
        }
    }

    private async Task<FakeServer> Server(string id, string name)
    {
        var server = await FakeServer.StartAsync(id, name, 46900);
        _servers.Add(server);
        return server;
    }

    private static bool Listening(int port)
    {
        try
        {
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    [TestMethod]
    public async Task Opening_the_same_server_from_many_places_at_once_starts_one_relay()
    {
        var desk = await Server("server-desk", "Desk");
        await _console.AddManagedAsync(desk);

        // The page, the tray and a second window, all at once.
        var urls = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => _console.Manager.OpenAsync(desk.ServerId, null))));

        Assert.AreEqual(1, urls.Select(u => ConsoleHarness.PortOf(u!.Url)).Distinct().Count());
        Assert.AreEqual(1, _console.Manager.RunningRelays.Count);

        // Each caller still got a ticket of its own.
        Assert.AreEqual(8, urls.Select(u => u!.Url).Distinct().Count());

        // One clock synchronisation, for the one relay that started.
        Assert.AreEqual(1, desk.Requests.Count(r => r.Path == "/remote-access/server-info"));
    }

    [TestMethod]
    public async Task Forgetting_a_server_while_it_is_being_opened_leaves_nothing_running()
    {
        var desk = await Server("server-desk", "Desk");
        await _console.AddManagedAsync(desk);

        // Slow enough to be caught mid-open: the relay is up and the first open is asking the
        // server for its clock.
        desk.ServerInfoDelay = TimeSpan.FromMilliseconds(1500);
        var opening = _console.Manager.OpenAsync(desk.ServerId, null);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!_console.Manager.RunningRelays.ContainsKey(desk.ServerId) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        var port = _console.Manager.RunningRelays[desk.ServerId];
        Assert.IsFalse(opening.IsCompleted, "the open finished before it could be interrupted");

        Assert.IsTrue(await _console.Manager.ForgetAsync(desk.ServerId));

        // No ticket to a relay that is gone.
        Assert.IsNull(await opening);
        Assert.AreEqual(0, _console.Manager.RunningRelays.Count);
        Assert.IsFalse(Listening(port), "the relay outlived the server it was for");
        Assert.IsNull(_console.Store.Find(desk.ServerId));
    }

    [TestMethod]
    public async Task Stopping_the_app_stops_every_relay_without_waiting_on_them_in_turn()
    {
        var desk = await Server("server-desk", "Desk");
        var nas = await Server("server-nas", "NAS");
        await _console.AddManagedAsync(desk);
        await _console.AddManagedAsync(nas);

        var ports = new List<int>();
        var hung = new List<Task<HttpResponseMessage>>();
        using var client = new HttpClient(new SocketsHttpHandler {UseProxy = false}) {Timeout = TimeSpan.FromMinutes(1)};

        foreach (var server in new[] {desk, nas})
        {
            var port = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(server.ServerId, null))!.Url);
            ports.Add(port);

            // A request that never finishes — a stream, a hub connection — through each.
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/hang");
            request.Headers.Host = $"127.0.0.1:{port}";
            hung.Add(client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead));
        }

        var waited = DateTime.UtcNow.AddSeconds(5);
        while (desk.Requests.All(r => r.Path != "/hang") || nas.Requests.All(r => r.Path != "/hang"))
        {
            Assert.IsTrue(DateTime.UtcNow < waited, "the hanging requests never reached the servers");
            await Task.Delay(20);
        }

        var watch = Stopwatch.StartNew();
        await _console.StopAsync();
        watch.Stop();

        foreach (var port in ports)
        {
            Assert.IsFalse(Listening(port), $"relay on {port} survived the app");
        }

        // Each relay may use its whole stop timeout on a connection that will not end; two in
        // turn would be twice that.
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(8.5), $"stopping took {watch.Elapsed}");

        foreach (var request in hung)
        {
            try
            {
                using var response = await request;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                // Cut off, which is the point.
            }
        }
    }

    [TestMethod]
    public async Task A_port_another_program_took_since_last_time_is_given_up_for_a_free_one()
    {
        var desk = await Server("server-desk", "Desk");
        await _console.AddManagedAsync(desk);

        // Last launch's relay port, now held by something that is not Bakabase.
        using var squatter = new TcpListener(IPAddress.Loopback, 0);
        squatter.Start();
        var taken = ((IPEndPoint) squatter.LocalEndpoint).Port;
        await _console.Store.MutateAsync(data => data.Servers.Single().RelayPort = taken);

        var port = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(desk.ServerId, null))!.Url);

        Assert.AreNotEqual(taken, port);
        Assert.AreEqual(port, _console.Store.Find(desk.ServerId)!.RelayPort, "the new port was not remembered");

        // The other program still has its port, and the relay answers on its own.
        Assert.IsTrue(squatter.Server.IsBound);
        Assert.AreEqual(HttpStatusCode.OK,
            (await ConsoleHarness.SendToRelayAsync(port, "/client/status")).StatusCode);
    }

    [TestMethod]
    public async Task A_server_no_longer_managed_keeps_its_origin_from_the_next_one_and_gets_it_back()
    {
        var desk = await Server("server-desk", "Desk");
        var nas = await Server("server-nas", "NAS");
        await _console.AddManagedAsync(desk);
        var deskPort = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(desk.ServerId, null))!.Url);

        Assert.IsTrue(await _console.Manager.ForgetAsync(desk.ServerId));

        // The browser still holds the desk's storage under its origin. The next server paired
        // here runs its own code on its relay's origin, and must not be handed that one.
        await _console.AddManagedAsync(nas);
        var nasPort = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(nas.ServerId, null))!.Url);

        Assert.AreNotEqual(deskPort, nasPort, "a new server was put on the origin of one no longer managed");

        // Paired again, after a restart even, the desk is the same server: back on its origin.
        var root = _console.Root;
        await _console.StopAsync();
        _console = await ConsoleHarness.StartAsync(root);
        await _console.AddManagedAsync(desk);

        Assert.AreEqual(deskPort, ConsoleHarness.PortOf((await _console.Manager.OpenAsync(desk.ServerId, null))!.Url));
        Assert.AreEqual(nasPort, ConsoleHarness.PortOf((await _console.Manager.OpenAsync(nas.ServerId, null))!.Url));
        Assert.IsNull(_console.Store.Read().RetiredRelayPorts, "the origin went back to its server");
    }

    [TestMethod]
    public async Task A_new_server_still_gets_a_relay_when_only_a_retired_origin_is_left()
    {
        var desk = await Server("server-desk", "Desk");
        var nas = await Server("server-nas", "NAS");
        await _console.AddManagedAsync(desk);
        var deskPort = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(desk.ServerId, null))!.Url);
        Assert.IsTrue(await _console.Manager.ForgetAsync(desk.ServerId));

        // A range with nothing left but the desk's old port.
        var options = _console.Get<RemoteConsoleOptions>();
        options.FirstRelayPort = deskPort;
        options.RelayPortRange = 1;

        await _console.AddManagedAsync(nas);

        Assert.AreEqual(deskPort, ConsoleHarness.PortOf((await _console.Manager.OpenAsync(nas.ServerId, null))!.Url));
        Assert.IsNull(_console.Store.Read().RetiredRelayPorts, "the desk still claims an origin the NAS now uses");
    }

    [TestMethod]
    public async Task Ports_other_programs_listen_on_are_skipped_whether_on_loopback_or_every_interface()
    {
        var desk = await Server("server-desk", "Desk");
        await _console.AddManagedAsync(desk);

        // The next free port in the harness's range, taken twice over: on 127.0.0.1, and the
        // one after on 0.0.0.0 — which on BSD-derived systems a later 127.0.0.1 bind with
        // SO_REUSEADDR could otherwise shadow, stealing the other program's local traffic.
        var first = LoopbackPortAllocator.Allocate(ConsoleHarness.FirstRelayPort);
        using var loopback = new TcpListener(IPAddress.Loopback, first);
        loopback.Start();

        var second = LoopbackPortAllocator.Allocate(first + 1);
        using var wildcard = new TcpListener(IPAddress.Any, second);
        wildcard.Start();

        var port = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(desk.ServerId, null))!.Url);

        Assert.AreNotEqual(first, port);
        Assert.AreNotEqual(second, port);
        Assert.IsTrue(port > second, $"{port} is below the ports the other programs hold");
    }

    [TestMethod]
    public async Task A_relay_listens_on_its_port_alone_whatever_the_environment_says()
    {
        var desk = await Server("server-desk", "Desk");
        await _console.AddManagedAsync(desk);

        // What would add a listener to an ordinary ASP.NET Core host — and to the app's own
        // server, which reads its environment — must not reach a relay's.
        var extra = LoopbackPortAllocator.Allocate(48500);
        var variables = new Dictionary<string, string>
        {
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{extra}",
            ["DOTNET_URLS"] = $"http://127.0.0.1:{extra}",
            ["ASPNETCORE_HTTP_PORTS"] = extra.ToString(),
            ["ASPNETCORE_ENVIRONMENT"] = "Development"
        };
        var previous = variables.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);

        int port;
        try
        {
            foreach (var (name, value) in variables)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            port = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(desk.ServerId, null))!.Url);
        }
        finally
        {
            foreach (var (name, value) in previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        Assert.IsTrue(Listening(port));
        Assert.IsFalse(Listening(extra), "the relay also listened where the environment told it to");

        // Loopback only: the relay is not reachable on any other address of this machine.
        var external = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));

        if (external != null)
        {
            using var client = new TcpClient();
            Assert.ThrowsException<SocketException>(() => client.Connect(external, port),
                $"the relay answered on {external}");
        }
    }
}
