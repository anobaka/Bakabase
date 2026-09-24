using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// Which pages can open a WebSocket through a managed server's relay: a real relay, a real
/// upstream, and the handshake a browser really sends.
/// </summary>
/// <remarks>
/// <para>
/// A relay signs everything it forwards with this device's key, so a WebSocket through it is
/// a connection to the managed server's UI hub with full control — the options it pushes
/// include the server's third-party cookies and API keys. Chromium, and so WebView2, sends no
/// fetch metadata on a handshake, only <c>Origin</c>; before the relay judged handshakes by
/// it, any page in any browser on this machine could open one, since a handshake is a GET.
/// </para>
/// <para>
/// And what goes the other way: a handshake from the relay's own page is forwarded with the
/// server's own origin, so a server on this machine — which takes the relay for a loopback
/// caller and judges handshakes by <c>Origin</c> too — keeps serving its own UI's hub.
/// </para>
/// </remarks>
[TestClass]
public class RelayWebSocketOriginTests
{
    private ConsoleHarness _console = null!;
    private FakeServer _desk = null!;
    private FakeServer _nas = null!;
    private int _deskPort;
    private int _nasPort;

    [TestInitialize]
    public async Task Setup()
    {
        _console = await ConsoleHarness.StartAsync();
        _desk = await FakeServer.StartAsync("server-desk", "Desk", 46900);
        _nas = await FakeServer.StartAsync("server-nas", "NAS", 46900);
        await _console.AddManagedAsync(_desk);
        await _console.AddManagedAsync(_nas);
        _deskPort = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_desk.ServerId, null))!.Url);
        _nasPort = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_nas.ServerId, null))!.Url);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _console.DisposeAsync();
        await _desk.DisposeAsync();
        await _nas.DisposeAsync();
    }

    /// <summary>A handshake as Chromium sends it: <c>Origin</c> when given, no fetch metadata.</summary>
    private static async Task<ClientWebSocket> OpenAsync(string url, string? origin)
    {
        var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.Proxy = null;
        if (origin != null)
        {
            socket.Options.SetRequestHeader("Origin", origin);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await socket.ConnectAsync(new Uri(url), timeout.Token);
        }
        catch (WebSocketException)
        {
        }

        return socket;
    }

    private static async Task<string> EchoAsync(ClientWebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await socket.SendAsync("ping"u8.ToArray(), WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[64];
        var echoed = await socket.ReceiveAsync(buffer, timeout.Token);
        return Encoding.UTF8.GetString(buffer, 0, echoed.Count);
    }

    private static void AssertRefusedByRelay(ClientWebSocket socket, string because)
    {
        Assert.AreNotEqual(WebSocketState.Open, socket.State, because);
        Assert.AreEqual(HttpStatusCode.BadRequest, socket.HttpStatusCode, because);
        Assert.IsNotNull(socket.HttpResponseHeaders, because);
        Assert.IsTrue(socket.HttpResponseHeaders.TryGetValue("X-Bakabase-Client", out var reason), because);
        Assert.AreEqual(nameof(ClientForwardingFailure.ForeignCaller), reason.Single(), because);
    }

    [TestMethod]
    public async Task A_handshake_from_any_other_page_is_refused_before_the_server_sees_it()
    {
        foreach (var address in new[] {$"127.0.0.1:{_deskPort}", $"localhost:{_deskPort}"})
        foreach (var origin in new[]
                 {
                     "https://evil.example", "null",
                     // This device's own window: the local server's page is not the managed
                     // server's.
                     $"http://localhost:{_console.ServicePort}", $"http://127.0.0.1:{_console.ServicePort}",
                     // Another managed server's page, one relay over.
                     $"http://127.0.0.1:{_nasPort}", $"http://localhost:{_nasPort}"
                 })
        {
            using var socket = await OpenAsync($"ws://{address}/hub/echo", origin);
            AssertRefusedByRelay(socket, $"ws://{address} from {origin}");
        }

        Assert.IsFalse(_desk.Requests.Any(r => r.Path == "/hub/echo"), "A refused handshake reached the server");
        lock (_console.Logs)
        {
            Assert.IsTrue(_console.Logs.Any(l => l.Contains("Refused ForeignOrigin request GET /hub/echo")),
                string.Join("\n", _console.Logs));
        }
    }

    [TestMethod]
    public async Task The_relays_own_page_keeps_its_hub_and_the_server_sees_its_own_origin()
    {
        foreach (var (address, origin) in new[]
                 {
                     ($"127.0.0.1:{_deskPort}", $"http://127.0.0.1:{_deskPort}"),
                     ($"localhost:{_deskPort}", $"http://localhost:{_deskPort}")
                 })
        {
            using var socket = await OpenAsync($"ws://{address}/hub/echo", origin);
            Assert.AreEqual(WebSocketState.Open, socket.State, $"{origin}: HTTP {socket.HttpStatusCode}");
            Assert.AreEqual("ping", await EchoAsync(socket), origin);
        }

        var upgrades = _desk.Requests.Where(r => r.Path == "/hub/echo").ToList();
        Assert.AreEqual(2, upgrades.Count);
        foreach (var upgrade in upgrades)
        {
            // Signed as this device, and presented as the server's own UI — which is what
            // the relay's page is.
            Assert.IsTrue(upgrade.SignatureValid);
            Assert.AreEqual(_desk.BaseAddress, upgrade.Origin);
        }
    }

    [TestMethod]
    public async Task A_native_client_names_no_page_and_is_served_as_before()
    {
        using var socket = await OpenAsync($"ws://127.0.0.1:{_deskPort}/hub/echo", null);

        Assert.AreEqual(WebSocketState.Open, socket.State);
        Assert.AreEqual("ping", await EchoAsync(socket));
        Assert.IsNull(_desk.Requests.Single(r => r.Path == "/hub/echo").Origin);
    }

    [TestMethod]
    public async Task Only_the_relays_own_page_is_forwarded_as_the_servers()
    {
        async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? origin)
        {
            using var handler = new SocketsHttpHandler {AllowAutoRedirect = false, UseProxy = false, UseCookies = false};
            using var client = new HttpClient(handler);
            var request = new HttpRequestMessage(method, $"http://127.0.0.1:{_deskPort}{path}");
            if (origin != null)
            {
                request.Headers.TryAddWithoutValidation("Origin", origin);
            }

            if (method != HttpMethod.Get)
            {
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(request);
            await response.Content.LoadIntoBufferAsync();
            return response;
        }

        Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/own-write", $"http://127.0.0.1:{_deskPort}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/own-write-localhost", $"http://localhost:{_deskPort}")).StatusCode);
        // A GET from another page, from an engine without fetch metadata: served, as before,
        // and forwarded naming that page — never passed off as the server's own.
        Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/foreign-read", "https://evil.example")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/native-read", null)).StatusCode);

        string? OriginAt(string path) => _desk.Requests.Single(r => r.Path == path).Origin;

        Assert.AreEqual(_desk.BaseAddress, OriginAt("/own-write"));
        Assert.AreEqual(_desk.BaseAddress, OriginAt("/own-write-localhost"));
        Assert.AreEqual("https://evil.example", OriginAt("/foreign-read"));
        Assert.IsNull(OriginAt("/native-read"));
    }
}
