using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Models.Constants;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Service;

/// <summary>
/// Who can reach this Service's SignalR hubs from a page in this computer's browser, by any
/// of their transports — over a real listener, with the handshake a browser really sends.
/// </summary>
/// <remarks>
/// <para>
/// The hubs push what the UI shows — every options object among it, third-party cookies and
/// API keys included — so a page that holds a hub connection reads the lot. The Service maps
/// two, <c>/hub/ui</c> and <c>/hub/progressor</c>, and nothing else accepts a WebSocket;
/// each hub offers WebSockets, server-sent events and long polling.
/// </para>
/// <para>
/// Every handshake here is sent as Chromium sends one — and so WebView2 — with <c>Origin</c>
/// and no fetch metadata at all. That is the shape a guard reading <c>Sec-Fetch-Site</c>
/// alone never saw.
/// </para>
/// </remarks>
[TestClass]
public class LoopbackHubAccessTests
{
    private const string Attacker = "https://attacker.example";

    private static readonly string[] Extensions =
    [
        "chrome-extension://dhdgffkkebhmkfjojejmpbldmpobfkfo",
        "moz-extension://0c8d8b0e-8f7c-4b6a-9d8e-2f1c3b4a5d6e"
    ];

    private static void AssertRefused(ClientWebSocket socket, string because)
    {
        Assert.AreNotEqual(WebSocketState.Open, socket.State, because);
        Assert.AreEqual(HttpStatusCode.Forbidden, socket.HttpStatusCode, because);
        Assert.IsNotNull(socket.HttpResponseHeaders, because);
        Assert.IsTrue(socket.HttpResponseHeaders.TryGetValue("X-Bakabase-Remote-Access", out var reason), because);
        Assert.AreEqual("HostOnly", reason.Single(), because);
    }

    private static async Task AssertConnected(ClientWebSocket socket, string because)
    {
        Assert.AreEqual(WebSocketState.Open, socket.State, $"{because}: HTTP {socket.HttpStatusCode}");
        StringAssert.Contains(await ServiceGateHost.PingOverSocketAsync(socket), "\"result\":\"pong\"", because);
    }

    [TestMethod]
    public async Task A_handshake_naming_another_page_is_refused_on_every_hub()
    {
        await using var host = await ServiceGateHost.StartAsync([], origins: new ServiceCorsOrigins(RuntimeMode.Dev));

        foreach (var hub in ServiceGateHost.Hubs)
        foreach (var address in new[] {$"127.0.0.1:{host.Port}", $"localhost:{host.Port}"})
        foreach (var origin in new[]
                 {
                     // A relay page, in both of its spellings: another server's own code.
                     ServiceGateHost.RelayOrigin, "http://localhost:34650",
                     // Any website, and an opaque (sandboxed, data:) page.
                     Attacker, "null",
                     // This device's own window, on the other address form — not the origin
                     // the handshake was sent to, and 127.0.0.1 is not on the CORS list.
                     address.StartsWith("localhost") ? host.Origin : null
                 })
        {
            if (origin == null)
            {
                continue;
            }

            using var socket = await host.OpenSocketAsync(hub, origin, address);
            AssertRefused(socket, $"ws://{address}{hub} from {origin}");
        }
    }

    [TestMethod]
    public async Task This_devices_window_and_the_pages_it_trusts_keep_every_hub()
    {
        await using var host = await ServiceGateHost.StartAsync([], origins: new ServiceCorsOrigins(RuntimeMode.Dev));

        foreach (var hub in ServiceGateHost.Hubs)
        {
            // The window, on either address: the origin the handshake was sent to.
            using (var own = await host.OpenSocketAsync(hub, host.Origin))
            {
                await AssertConnected(own, $"{hub}: this device's window on 127.0.0.1");
            }

            using (var own = await host.OpenSocketAsync(hub, $"http://localhost:{host.Port}", $"localhost:{host.Port}"))
            {
                await AssertConnected(own, $"{hub}: this device's window on localhost");
            }

            // yarn dev in a development build, the userscript's sites and the extensions its
            // manager runs in: the pages CORS already lets in.
            foreach (var origin in new[] {ServiceGateHost.DevOrigin, "https://exhentai.org"}.Concat(Extensions))
            {
                using var trusted = await host.OpenSocketAsync(hub, origin);
                await AssertConnected(trusted, $"{hub}: {origin}");
            }

            // A native client names no page: not something a browser sends, and judged as
            // before.
            using var native = await host.OpenSocketAsync(hub, null);
            await AssertConnected(native, $"{hub}: no Origin");
        }
    }

    [TestMethod]
    public async Task A_packaged_build_refuses_yarn_devs_handshake()
    {
        await using var host = await ServiceGateHost.StartAsync([],
            origins: new ServiceCorsOrigins(RuntimeMode.WinForms));

        foreach (var hub in ServiceGateHost.Hubs)
        {
            using (var dev = await host.OpenSocketAsync(hub, ServiceGateHost.DevOrigin))
            {
                AssertRefused(dev, $"{hub}: yarn dev in a packaged build");
            }

            using (var own = await host.OpenSocketAsync(hub, host.Origin))
            {
                await AssertConnected(own, $"{hub}: this device's window");
            }

            using var userscript = await host.OpenSocketAsync(hub, "https://www.north-plus.net");
            await AssertConnected(userscript, $"{hub}: the userscript");
        }
    }

    [TestMethod]
    public async Task Fetch_metadata_cannot_talk_a_foreign_handshake_in()
    {
        // An engine that does label its handshake, claiming what it likes: Origin decides.
        await using var host = await ServiceGateHost.StartAsync([], origins: new ServiceCorsOrigins(RuntimeMode.Dev));

        foreach (var site in new[] {"same-origin", "none", "same-site"})
        {
            using var socket = await host.OpenSocketAsync("/hub/ui", ServiceGateHost.RelayOrigin,
                headers: new Dictionary<string, string> {["Sec-Fetch-Site"] = site, ["Sec-Fetch-Mode"] = "websocket"});
            AssertRefused(socket, site);
        }
    }

    [TestMethod]
    public async Task No_http2_handshake_can_reach_the_plain_http_listener()
    {
        // An HTTP/2 WebSocket is an extended CONNECT, and needs HTTP/2 — which a browser only
        // speaks over TLS, and Kestrel on a cleartext endpoint that also takes HTTP/1.1 does not
        // speak at all: without ALPN it cannot tell, and takes HTTP/1.1. The Service listens
        // that way (UseUrls with plain http), so the guard's extended-CONNECT rule is only ever
        // exercised by the matrix. This pins the premise: the day it stops holding, a real
        // extended CONNECT belongs in these tests.
        await using var host = await ServiceGateHost.StartAsync([], origins: new ServiceCorsOrigins(RuntimeMode.Dev));
        using var handler = new SocketsHttpHandler {UseProxy = false};
        using var client = new HttpClient(handler)
        {
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Timeout = TimeSpan.FromSeconds(10)
        };

        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.GetAsync($"{host.Origin}/hub/ui"));
    }

    // ---- the transports a hub falls back to ----

    private const string Handshake = "{\"protocol\":\"json\",\"version\":1}\u001e";
    private const string PingInvocation = "{\"type\":1,\"invocationId\":\"7\",\"target\":\"Ping\",\"arguments\":[]}\u001e";

    private static async Task<string> NegotiateAsync(ServiceGateHost host, string hub, string? site, string? origin)
    {
        var response = await host.SendAsync(HttpMethod.Post, $"{hub}/negotiate?negotiateVersion=1", site, origin);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"negotiate {hub} as {origin}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("connectionToken").GetString()!;
    }

    private static HttpRequestMessage Transport(ServiceGateHost host, HttpMethod method, string hub, string token,
        string? site, string? origin, string? body = null, string? accept = null)
    {
        var request = host.Request(method, $"{hub}?id={Uri.EscapeDataString(token)}", site, origin);
        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
        }

        if (accept != null)
        {
            request.Headers.TryAddWithoutValidation("Accept", accept);
        }

        return request;
    }

    [TestMethod]
    public async Task This_devices_window_keeps_negotiation_and_every_fallback()
    {
        await using var host = await ServiceGateHost.StartAsync([], origins: new ServiceCorsOrigins(RuntimeMode.Dev));

        foreach (var hub in ServiceGateHost.Hubs)
        // A current engine labels its requests same-origin; an older one sends none.
        foreach (var site in new[] {"same-origin", null})
        {
            var because = $"{hub}, Sec-Fetch-Site {site ?? "(absent)"}";

            // Negotiated, then upgraded to a WebSocket on that connection.
            var socketToken = await NegotiateAsync(host, hub, site, host.Origin);
            using (var socket = await host.OpenSocketAsync($"{hub}?id={Uri.EscapeDataString(socketToken)}", host.Origin))
            {
                await AssertConnected(socket, because + ": WebSocket after negotiate");
            }

            // Long polling: sends are POSTs, receives are GETs — the first of which only
            // starts the connection and comes back empty.
            var token = await NegotiateAsync(host, hub, site, host.Origin);
            var started = await host.SendAsync(Transport(host, HttpMethod.Get, hub, token, site, host.Origin));
            Assert.AreEqual(HttpStatusCode.OK, started.StatusCode, because);
            var handshake = await host.SendAsync(Transport(host, HttpMethod.Post, hub, token, site, host.Origin, Handshake));
            Assert.AreEqual(HttpStatusCode.OK, handshake.StatusCode, because);
            var answer = await host.SendAsync(Transport(host, HttpMethod.Get, hub, token, site, host.Origin));
            Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode, because);
            StringAssert.Contains(await answer.Content.ReadAsStringAsync(), "{}\u001e", because);
            var invoked = await host.SendAsync(Transport(host, HttpMethod.Post, hub, token, site, host.Origin, PingInvocation));
            Assert.AreEqual(HttpStatusCode.OK, invoked.StatusCode, because);
            var result = await host.SendAsync(Transport(host, HttpMethod.Get, hub, token, site, host.Origin));
            StringAssert.Contains(await result.Content.ReadAsStringAsync(), "\"result\":\"pong\"", because);

            // Server-sent events: a GET that streams, sends as POSTs.
            var streamToken = await NegotiateAsync(host, hub, site, host.Origin);
            using var handler = new SocketsHttpHandler {UseProxy = false, UseCookies = false};
            using var client = new HttpClient(handler);
            using var stream = await client.SendAsync(
                Transport(host, HttpMethod.Get, hub, streamToken, site, host.Origin, accept: "text/event-stream"),
                HttpCompletionOption.ResponseHeadersRead);
            Assert.AreEqual(HttpStatusCode.OK, stream.StatusCode, because);
            Assert.AreEqual("text/event-stream", stream.Content.Headers.ContentType?.MediaType, because);
            var sent = await host.SendAsync(Transport(host, HttpMethod.Post, hub, streamToken, site, host.Origin, Handshake));
            Assert.AreEqual(HttpStatusCode.OK, sent.StatusCode, because);
            using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string? line;
            do
            {
                line = await reader.ReadLineAsync(timeout.Token);
            } while (line != null && !line.StartsWith("data:", StringComparison.Ordinal));

            Assert.AreEqual("data: {}\u001e", line, because);
        }
    }

    [TestMethod]
    public async Task A_foreign_page_gets_no_hub_connection_by_any_transport()
    {
        await using var host = await ServiceGateHost.StartAsync([], origins: new ServiceCorsOrigins(RuntimeMode.Dev));

        foreach (var hub in ServiceGateHost.Hubs)
        foreach (var origin in new[] {ServiceGateHost.RelayOrigin, Attacker, "null"})
        {
            // Negotiation is a POST: refused whether the engine labels it or not. It is the
            // only way to a connection id, which every fallback transport requires — and its
            // answer is not the page's to read either, which CORS already sees to.
            foreach (var site in new[] {"same-site", "cross-site", null})
            {
                var negotiate = await host.SendAsync(HttpMethod.Post, $"{hub}/negotiate?negotiateVersion=1", site, origin);
                Assert.AreEqual(HttpStatusCode.Forbidden, negotiate.StatusCode, $"{hub} negotiate from {origin}, {site}");
                StringAssert.DoesNotMatch(await negotiate.Content.ReadAsStringAsync(),
                    new System.Text.RegularExpressions.Regex("connectionToken"));
            }

            // Even holding the id of this device's own connection — which it has no way to
            // learn — it could neither send on it nor read what it receives.
            var token = await NegotiateAsync(host, hub, "same-origin", host.Origin);
            foreach (var site in new[] {"same-site", null})
            {
                var send = await host.SendAsync(Transport(host, HttpMethod.Post, hub, token, site, origin, Handshake));
                Assert.AreEqual(HttpStatusCode.Forbidden, send.StatusCode, $"{hub} send from {origin}, {site}");

                using var socket = await host.OpenSocketAsync($"{hub}?id={Uri.EscapeDataString(token)}", origin);
                AssertRefused(socket, $"{hub} WebSocket with an id from {origin}");
            }

            // A receive is a GET, which changes nothing and so is not refused — and its answer
            // carries no CORS permission, so the browser keeps it from the page. (The first
            // poll on a connection answers at once, which keeps this from waiting.)
            var poll = await host.SendAsync(Transport(host, HttpMethod.Get, hub, token, "same-site", origin));
            Assert.AreEqual(HttpStatusCode.OK, poll.StatusCode, $"{hub} poll from {origin}");
            Assert.IsFalse(poll.Headers.Contains("Access-Control-Allow-Origin"), $"{hub} poll from {origin}");
        }
    }
}
