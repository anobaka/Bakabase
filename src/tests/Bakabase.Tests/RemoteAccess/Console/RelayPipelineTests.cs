using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Forwarding;
using Bakabase.Remoting.Components.UserMachine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// A managed server's relay as it actually runs: the console's own composition, a real
/// listener, real requests, and a stand-in server behind it.
/// </summary>
/// <remarks>
/// <para>
/// The guard, the forwarder and the dispatcher have their own unit tests, but none of them
/// says whether they are wired in the right order, or whether the guard was armed with the
/// port the relay really bound. Those are exactly the mistakes that make a security check
/// silently protect nothing, so this drives the assembled thing over HTTP.
/// </para>
/// <para>
/// Most of it runs with the server attached, so what reaches it can be checked. The rest
/// runs "detached" — the entry taken out of the managed store under the running relay, as
/// forgetting it from another window does — which is the relay's no-server path: it must
/// neither sign with a key it no longer has nor forward unsigned.
/// </para>
/// </remarks>
[TestClass]
public class RelayPipelineTests
{
    private ConsoleHarness _console = null!;
    private FakeServer _desk = null!;
    private string _deviceId = null!;
    private int _port;

    [TestInitialize]
    public async Task Setup()
    {
        _console = await ConsoleHarness.StartAsync();
        _desk = await FakeServer.StartAsync("server-desk", "Desk", 46900);
        (_deviceId, _) = await _console.AddManagedAsync(_desk);
        _port = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_desk.ServerId, null))!.Url);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _console.DisposeAsync();
        await _desk.DisposeAsync();
    }

    private RelayNavigationTokens Tickets => _console.Get<RelayNavigationTokens>();

    private IServiceProvider Relay => _console.Manager.RelayServices(_desk.ServerId)!;

    /// <summary>The server forgotten under the running relay: the relay's no-server path.</summary>
    private Task DetachAsync() =>
        _console.Store.MutateAsync(data => data.Servers.RemoveAll(s => s.ServerId == _desk.ServerId));

    private List<FakeServer.Received> Arrived(string path) => _desk.Requests.Where(r => r.Path == path).ToList();

    /// <summary>
    /// Sends without letting HttpClient rewrite the Host header, which is the whole point
    /// of most of these.
    /// </summary>
    private async Task<HttpResponseMessage> Send(string path, string? host = null, string? origin = null,
        HttpMethod? method = null, string? body = null, string? accept = null, string? fetchMode = null,
        string? fetchSite = null, string? fetchDest = null, string? cookie = null)
    {
        // Redirects are the subject of some of these, so they are never followed. Cookies
        // are sent by hand when a test wants them, never from a jar.
        using var handler = new SocketsHttpHandler {AllowAutoRedirect = false, UseProxy = false, UseCookies = false};
        using var client = new HttpClient(handler);
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, $"http://127.0.0.1:{_port}{path}");

        if (accept != null) request.Headers.TryAddWithoutValidation("Accept", accept);
        if (fetchMode != null) request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", fetchMode);
        if (fetchSite != null) request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", fetchSite);
        if (fetchDest != null) request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", fetchDest);
        if (cookie != null) request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (origin != null) request.Headers.TryAddWithoutValidation("Origin", origin);

        request.Headers.Host = host ?? $"127.0.0.1:{_port}";

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(request);
        await response.Content.LoadIntoBufferAsync();

        return response;
    }

    private static string? FailureOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Bakabase-Client", out var values) ? values.Single() : null;

    private static void AssertRefused(HttpResponseMessage response, string? because = null)
    {
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, because);
        Assert.AreEqual(nameof(ClientForwardingFailure.ForeignCaller), FailureOf(response), because);
    }

    // ---- the guard ----

    [TestMethod]
    public async Task A_rebound_hostname_never_reaches_the_pipeline()
    {
        AssertRefused(await Send("/resource/search", host: $"evil.com:{_port}"));
        Assert.AreEqual(0, Arrived("/resource/search").Count);
    }

    [TestMethod]
    public async Task A_cross_site_write_never_reaches_the_pipeline()
    {
        AssertRefused(await Send("/resource/search", origin: "https://evil.com", method: HttpMethod.Post));
        Assert.AreEqual(0, Arrived("/resource/search").Count);
    }

    [TestMethod]
    public async Task The_guard_is_armed_with_the_port_the_relay_actually_bound()
    {
        // A guard built from a second copy of the port decision would either refuse
        // everything or protect nothing, and both look fine in a unit test.
        Assert.AreEqual(HttpStatusCode.OK, (await Send(ClientContextEndpoint.Path)).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await Send(ClientContextEndpoint.Path, host: $"127.0.0.1:{_port + 1}")).StatusCode);
    }

    // ---- fetch metadata, switch tickets and cookies ----

    [TestMethod]
    public async Task A_window_switching_in_lands_where_it_was_sent_without_its_ticket()
    {
        var ticket = Tickets.Mint(_port);
        var path = $"/resource/42?tab=files&{RelayNavigationTokens.QueryName}={ticket}&q=a%20b&tag=%E4%B8%AD";

        // Exactly what a window navigated from this device's own origin sends: every other
        // loopback port is the same site.
        var response = await Send(path, fetchSite: "same-site", fetchMode: "navigate", fetchDest: "document");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        Assert.AreEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.IsTrue(response.Headers.CacheControl?.NoStore == true);
        Assert.AreEqual("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.IsNull(FailureOf(response), "neither refused nor forwarded");
        Assert.AreEqual(0, Arrived("/resource/42").Count, "the ticketed request itself was forwarded");

        // Only the ticket is gone: the path, every other parameter and its escaping are
        // exactly what was asked for, on the host the guard just verified. One navigation,
        // to that address plus whatever #route the window was sent to: the browser keeps the
        // fragment from the request, and only the page can hand it on.
        var expected = $"http://127.0.0.1:{_port}/resource/42?tab=files&q=a%20b&tag=%E4%B8%AD";
        Assert.AreEqual(expected, await ConsoleHarness.ContinuationOfAsync(response));
        StringAssert.Contains(body, $"<script>location.replace({JsonSerializer.Serialize(expected)}+location.hash);</script>");
        Assert.IsFalse(body.Contains(ticket), "the ticket must not be written back out");

        // Spent: the same address a second time is just another site's navigation.
        AssertRefused(await Send(path, fetchSite: "same-site", fetchMode: "navigate", fetchDest: "document"));

        // The page starts the next navigation itself, so it arrives same-origin and reaches
        // the server — without the ticket.
        var landed = await Send("/resource/42?tab=files&q=a%20b&tag=%E4%B8%AD", fetchSite: "same-origin",
            fetchMode: "navigate", fetchDest: "document");
        Assert.AreEqual(HttpStatusCode.OK, landed.StatusCode);
        Assert.AreEqual("tab=files&q=a%20b&tag=%E4%B8%AD", Arrived("/resource/42").Single().Query);
    }

    [TestMethod]
    public async Task A_ticket_is_refused_on_anything_but_a_window_navigating()
    {
        var ticket = Tickets.Mint(_port);
        var path = $"/?{RelayNavigationTokens.QueryName}={ticket}";

        var image = await Send(path, fetchSite: "cross-site", fetchMode: "no-cors", fetchDest: "image");
        var frame = await Send(path, fetchSite: "cross-site", fetchMode: "navigate", fetchDest: "iframe");
        var post = await Send(path, method: HttpMethod.Post, origin: "https://evil.example", fetchSite: "cross-site",
            fetchMode: "navigate", fetchDest: "document");
        var other = await Send($"/?{RelayNavigationTokens.QueryName}={Tickets.Mint(_port + 1)}",
            fetchSite: "cross-site", fetchMode: "navigate", fetchDest: "document");

        foreach (var response in new[] {image, frame, post, other})
        {
            AssertRefused(response);
        }

        // None of the refusals spent the one minted for this relay.
        Assert.IsTrue(Tickets.TryConsume(ticket, _port));
    }

    [TestMethod]
    public async Task Another_sites_page_cannot_start_an_action_on_this_machine()
    {
        // An <img> pointed at these would, before fetch metadata was read, have opened a
        // file or started a stream on the user's machine: they are GETs, and a GET from
        // another site used to be let through as harmless.
        foreach (var path in new[] {"/tool/open?path=%2Fdata%2Fmedia%2Fa.mkv", "/file/play?fullname=%2Fdata%2Fa.mkv"})
        foreach (var site in new[] {"cross-site", "same-site"})
        {
            AssertRefused(await Send(path, fetchSite: site, fetchMode: "no-cors", fetchDest: "image"), $"{site} {path}");
        }

        Assert.AreEqual(0, Arrived("/file/play").Count);
    }

    [TestMethod]
    public async Task Callers_that_send_no_fetch_metadata_are_served_as_before()
    {
        // A local player pulling a stream, a script, an older engine. The action is
        // reached — and fails only because nothing maps the path on this machine.
        var open = await Send("/tool/open?path=%2Fdata%2Fmedia%2Fa.mkv");
        Assert.AreEqual(HttpStatusCode.NotFound, open.StatusCode);
        Assert.AreEqual(nameof(ClientForwardingFailure.PathNotMapped), FailureOf(open));

        // The stream is the server's, and reaches it.
        var stream = await Send("/file/play?fullname=%2Fdata%2Fa.mkv");
        Assert.AreEqual(HttpStatusCode.OK, stream.StatusCode);
        Assert.AreEqual(1, Arrived("/file/play").Count);

        // And so is the relay's own page, asking for the same thing.
        var sameOrigin = await Send("/tool/open?path=%2Fdata%2Fmedia%2Fa.mkv", fetchSite: "same-origin",
            fetchMode: "cors", fetchDest: "empty");
        Assert.AreEqual(nameof(ClientForwardingFailure.PathNotMapped), FailureOf(sameOrigin));
    }

    [TestMethod]
    public async Task Neither_cookies_nor_tickets_ever_reach_the_server()
    {
        // Cookies are per host, not per port, so the browser attaches whatever any loopback
        // origin set — this device's own server's, another relay's. None of it is this
        // relay's to hand to another machine.
        var get = await Send("/resource/search?page=2", cookie: "session=local-secret; other=1",
            fetchSite: "same-origin", fetchMode: "cors", fetchDest: "empty");
        var post = await Send("/resource/search", method: HttpMethod.Post, body: "{}",
            origin: $"http://127.0.0.1:{_port}", cookie: "session=local-secret");

        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, post.StatusCode);

        // A ticket on an address the relay would serve anyway is taken off, not passed on.
        var ticketed = await Send($"/?{RelayNavigationTokens.QueryName}={Tickets.Mint(_port)}",
            fetchSite: "none", fetchMode: "navigate", fetchDest: "document");
        Assert.AreEqual($"http://127.0.0.1:{_port}/", await ConsoleHarness.ContinuationOfAsync(ticketed));

        var arrived = Arrived("/resource/search");
        Assert.AreEqual(2, arrived.Count, string.Join("\n", arrived));
        Assert.IsFalse(_desk.Requests.Any(r => r.Query.Contains(RelayNavigationTokens.QueryName)),
            "a ticket reached the server");

        foreach (var request in arrived)
        {
            Assert.IsNull(request.Cookie, $"{request.Method} {request.Path}");

            // Still signed as this device: dropping the cookie took nothing else with it.
            Assert.AreEqual(_deviceId, request.DeviceId);
            Assert.IsTrue(request.SignatureValid);
        }
    }

    [TestMethod]
    public async Task A_websocket_from_another_page_never_reaches_the_pipeline()
    {
        // A handshake is a GET, and Chromium sends no fetch metadata on it, only Origin.
        // Detached, so that one the guard lets through is answered by the forwarder's
        // no-server refusal and the two outcomes cannot be mistaken for each other.
        await DetachAsync();

        async Task<System.Net.WebSockets.ClientWebSocket> Open(string? origin)
        {
            var socket = new System.Net.WebSockets.ClientWebSocket();
            socket.Options.CollectHttpResponseDetails = true;
            socket.Options.Proxy = null;
            if (origin != null)
            {
                socket.Options.SetRequestHeader("Origin", origin);
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/hub/ui"), timeout.Token);
            }
            catch (System.Net.WebSockets.WebSocketException)
            {
            }

            return socket;
        }

        foreach (var origin in new[]
                     {"https://evil.example", "null", $"http://127.0.0.1:{_port + 1}", "http://localhost:34567"})
        {
            using var socket = await Open(origin);
            Assert.AreEqual(HttpStatusCode.BadRequest, socket.HttpStatusCode, origin);
            Assert.AreEqual(nameof(ClientForwardingFailure.ForeignCaller),
                socket.HttpResponseHeaders!["X-Bakabase-Client"].Single(), origin);
        }

        // Its own page's handshake gets past the guard, to the forwarder — which has no
        // server to send it to any more.
        foreach (var origin in new[] {$"http://127.0.0.1:{_port}", $"http://localhost:{_port}", null})
        {
            using var socket = await Open(origin);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, socket.HttpStatusCode, origin ?? "(none)");
            Assert.AreEqual(nameof(ClientForwardingFailure.NotConnected),
                socket.HttpResponseHeaders!["X-Bakabase-Client"].Single(), origin ?? "(none)");
        }

        Assert.AreEqual(0, Arrived("/hub/ui").Count);
    }

    // ---- the transformer ----

    private sealed class NoCredentials : IClientCredentialProvider
    {
        public ClientCredentials? Current => null;
    }

    [TestMethod]
    public async Task Only_the_relays_own_page_is_forwarded_with_the_servers_origin()
    {
        var transformer = new UpstreamTransformer(new NoCredentials(), new ServerClock());

        async Task<string?> Forwarded(bool fromRelayPage, string? origin, string destination)
        {
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            context.Request.Method = "GET";
            context.Request.Path = "/hub/ui";
            if (origin != null)
            {
                context.Request.Headers.Origin = origin;
            }

            if (fromRelayPage)
            {
                UpstreamTransformer.MarkFromRelayPage(context);
            }

            using var proxyRequest = new HttpRequestMessage(HttpMethod.Get, (Uri?) null);
            await transformer.TransformRequestAsync(context, proxyRequest, destination, CancellationToken.None);

            return proxyRequest.Headers.TryGetValues("Origin", out var values) ? values.Single() : null;
        }

        // The guard marks what it admitted from the relay's own origin; the server is told
        // it is its own UI — a path in the server's address is not part of an origin.
        Assert.AreEqual("http://192.168.1.5:34567",
            await Forwarded(true, $"http://127.0.0.1:{_port}", "http://192.168.1.5:34567/"));
        Assert.AreEqual("https://nas.example",
            await Forwarded(true, $"http://localhost:{_port}", "https://nas.example/bakabase/"));

        // Anything else keeps whatever it named, or nothing.
        Assert.AreEqual("https://evil.example", await Forwarded(false, "https://evil.example", "http://192.168.1.5:34567/"));
        Assert.IsNull(await Forwarded(false, null, "http://192.168.1.5:34567/"));
    }

    [TestMethod]
    public async Task An_unpaired_relay_drops_cookies_too()
    {
        // The unsigned path returns early; the cookie has to be gone before it does.
        var transformer = new UpstreamTransformer(new NoCredentials(), new ServerClock());
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/resource/search";
        context.Request.Headers.Cookie = "session=local-secret";
        context.Request.Headers.Authorization = "Bearer chosen-by-the-page";
        context.Request.Headers["X-Kept"] = "1";

        using var proxyRequest = new HttpRequestMessage(HttpMethod.Get, (Uri?) null);
        await transformer.TransformRequestAsync(context, proxyRequest, "http://127.0.0.1:1/", CancellationToken.None);

        Assert.IsFalse(proxyRequest.Headers.Contains("Cookie"));
        Assert.IsNull(proxyRequest.Headers.Authorization);
        Assert.IsTrue(proxyRequest.Headers.Contains("X-Kept"), "only the cookie and credentials are dropped");
    }

    // ---- forwarding ----

    [TestMethod]
    public async Task A_relay_actually_reaches_its_server()
    {
        var response = await Send("/resource/search?page=2");

        // The failure this is here to catch reads as HTTP 503 with
        // X-Bakabase-Client: ServerUnreachable — the relay's own "it is not answering",
        // which is what a user sees in place of the server's library.
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, string.Join("\n", _console.Logs));
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "/resource/search");
        Assert.AreEqual("page=2", Arrived("/resource/search").Single().Query);
    }

    [TestMethod]
    public async Task What_reaches_the_server_is_signed_as_this_device()
    {
        await Send("/resource/search");

        // Without this the server answers every forwarded request as an unpaired stranger,
        // and the window looks connected while showing nothing.
        var arrived = Arrived("/resource/search").Single();
        Assert.AreEqual(_deviceId, arrived.DeviceId);
        Assert.IsTrue(arrived.SignatureValid);
    }

    [TestMethod]
    public async Task A_post_reaches_the_server_too()
    {
        // A body is where the signing path does its extra work — it buffers and hashes
        // one — so a GET passing says less than it looks like it does.
        var response = await Send("/resource/search", method: HttpMethod.Post, body: "{\"keyword\":\"x\"}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, string.Join("\n", _console.Logs));
        var arrived = Arrived("/resource/search").Single();
        Assert.AreEqual("POST", arrived.Method);
        Assert.IsTrue(arrived.SignatureValid);
    }

    [TestMethod]
    public async Task A_path_that_looks_like_a_file_is_forwarded_like_anything_else()
    {
        // MapFallback's default pattern excludes paths whose last segment has a dot. The
        // server's whole frontend arrives that way — /assets/index-<hash>.js and friends —
        // so taking the default would 404 every asset and leave the window blank.
        foreach (var path in new[]
                 {
                     "/favicon.ico",
                     "/assets/index-a1b2c3.js",
                     "/assets/index-a1b2c3.css",
                     "/tampermonkey/script/bakabase.user.js"
                 })
        {
            var response = await Send(path);

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, path);
            Assert.IsNull(FailureOf(response), path);
            Assert.AreEqual(1, Arrived(path).Count, path);
        }
    }

    [TestMethod]
    public async Task A_path_the_route_table_only_nearly_matches_still_goes_upstream()
    {
        // /player/playlist/{playlistId:int}/... has a constraint; a non-numeric segment is a
        // different route on the server and must not be swallowed here.
        var response = await Send("/player/playlist/latest/batch-play", method: HttpMethod.Post, body: "{}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, Arrived("/player/playlist/latest/batch-play").Count);
    }

    // ---- the context endpoint ----

    [TestMethod]
    public async Task The_context_endpoint_answers_locally()
    {
        var response = await Send(ClientContextEndpoint.Path);
        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        // Not the machine running the server. Saying otherwise would send the UI looking
        // for covers on listening ports that mean nothing here.
        Assert.IsFalse(data.GetProperty("isLocal").GetBoolean());
        Assert.AreEqual((int) ClientMode.PureClient, data.GetProperty("clientMode").GetInt32());
        Assert.IsTrue(data.GetProperty("cookieCaptureAvailable").GetBoolean());

        // Folded in from the server's own answer, asked signed.
        Assert.IsTrue(data.GetProperty("serverReachable").GetBoolean());
        Assert.IsTrue(data.GetProperty("paired").GetBoolean());
        Assert.AreEqual(_desk.ServerId, data.GetProperty("serverId").GetString());
    }

    [TestMethod]
    public async Task The_context_endpoint_answers_without_a_server_too()
    {
        // The UI has to render something while its server is gone, and a context call that
        // failed would leave it nothing.
        await DetachAsync();

        var response = await Send(ClientContextEndpoint.Path);
        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsFalse(data.GetProperty("serverReachable").GetBoolean());
        Assert.AreEqual((int) ClientMode.PureClient, data.GetProperty("clientMode").GetInt32());
        Assert.IsFalse(data.TryGetProperty("serverId", out _));
    }

    // ---- without a server ----

    [TestMethod]
    public async Task A_fetch_with_no_server_still_gets_the_error_the_frontend_reads()
    {
        // Only a navigation is redirected. The frontend's own calls are written against this
        // envelope, and handing them a redirect to an HTML page instead would turn a legible
        // "not connected" into a parse failure.
        await DetachAsync();

        var response = await Send("/resource/search", accept: "application/json");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.AreEqual(nameof(ClientForwardingFailure.NotConnected), FailureOf(response));
    }

    [TestMethod]
    public async Task A_page_request_that_is_not_a_navigation_is_not_redirected()
    {
        // Sec-Fetch-Mode is what the browser itself says it is doing, and it beats guessing
        // from Accept.
        await DetachAsync();

        var response = await Send("/resource/search", accept: "text/html", fetchMode: "cors");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [TestMethod]
    public async Task Everything_else_says_there_is_no_server()
    {
        await DetachAsync();
        var before = _desk.Requests.Count;

        var response = await Send("/resource/search");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.AreEqual(nameof(ClientForwardingFailure.NotConnected), FailureOf(response));

        // The same envelope shape the server's own gate uses, so the frontend has one error
        // format to understand rather than two.
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.IsTrue(body.TryGetProperty("message", out var message));

        // A relay loses its server only when this computer stopped managing it; there is no
        // client left to connect.
        StringAssert.Contains(message.GetString()!, "Devices and sharing");
        Assert.IsFalse(message.GetString()!.Contains("client", StringComparison.OrdinalIgnoreCase),
            message.GetString());
        Assert.AreEqual(before, _desk.Requests.Count, "the relay forwarded without a server");
    }

    // ---- actions on this machine ----

    [TestMethod]
    public void Every_route_the_relay_claims_has_a_handler()
    {
        // The dispatcher answers a declared route with no handler as "this relay is behind",
        // which is right while one is being written and wrong once they all are. Assembled
        // from the relay's real container, so a handler that was written but never
        // registered fails here rather than in front of a user.
        var dispatcher = Relay.GetRequiredService<UserMachineDispatcher>();
        var missing = UserMachineRoutes.All.Select(r => r.Key)
            .Except(dispatcher.ImplementedRoutes, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.AreEqual(0, missing.Length, string.Join(", ", missing));
    }

    [TestMethod]
    public async Task A_user_machine_route_is_intercepted_rather_than_forwarded()
    {
        // Playing a file is this machine's to do. Forwarding it would ask a server —
        // possibly somebody else's — to start a player on a screen nobody is watching. It
        // still fails here, with no server to ask what is playable, but it fails as this
        // machine's action: the forwarding header is absent, which is what tells the two
        // apart.
        await DetachAsync();

        var response = await Send("/resource/42/play");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.IsNull(FailureOf(response));
        Assert.AreEqual(0, Arrived("/resource/42/play").Count);
    }

    [TestMethod]
    public async Task Opening_a_server_path_with_no_mapping_says_which_path_and_why()
    {
        // Nothing can infer where /data/media is on this machine, so the answer names the
        // path and carries a header the frontend keys its "set this up" prompt off.
        // Guessing, or reporting a generic failure, would both leave the user stuck.
        var response = await Send("/tool/open?path=%2Fdata%2Fmedia%2Fa.mkv");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(nameof(ClientForwardingFailure.PathNotMapped), FailureOf(response));

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual("/data/media/a.mkv", body.GetProperty("serverPath").GetString());

        // Where the setting actually is in this window — the toast shows this text as it is.
        var message = body.GetProperty("message").GetString()!;
        StringAssert.Contains(message, "/data/media/a.mkv");
        StringAssert.Contains(message, "This computer → Path mapping");
        Assert.IsFalse(message.Contains("client", StringComparison.OrdinalIgnoreCase), message);
        Assert.AreEqual(0, Arrived("/tool/open").Count);
    }

    [TestMethod]
    public async Task Opening_a_link_refuses_anything_that_is_not_a_web_address()
    {
        // Reached the local handler rather than the forwarder, and stopped there.
        var response = await Send("/gui/url?url=file%3A%2F%2F%2Fetc%2Fpasswd");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsNull(FailureOf(response));
        Assert.AreEqual(0, Arrived("/gui/url").Count);
    }
}
