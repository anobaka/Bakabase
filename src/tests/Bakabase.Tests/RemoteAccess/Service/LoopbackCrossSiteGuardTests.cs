using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bootstrap.Models.Constants;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bakabase.Tests.Federation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Service;

/// <summary>
/// What a page on another site can make this computer's browser do to its own Service.
/// </summary>
/// <remarks>
/// <para>
/// The Service trusts loopback, and the desktop app now shows other servers' UIs from
/// relays on <c>127.0.0.1</c> — so the page one port away runs another server's
/// JavaScript, and any website can aim a form at <c>localhost</c>. These tests hold the
/// line between that and everything that must keep working: this device's own window,
/// <c>yarn dev</c>, the userscript, the hub, the federation interface, native players, and
/// a window switching back from a relay.
/// </para>
/// <para>
/// Requests are shaped the way a browser shapes them — <c>Sec-Fetch-Site</c>, <c>Origin</c>,
/// the navigation headers — and sent to a real listener running the Service's own gate
/// order.
/// </para>
/// </remarks>
[TestClass]
public class LoopbackCrossSiteGuardTests
{
    private const string Attacker = "https://attacker.example";

    private ServiceGateHost _host = null!;
    private FakeManagedServerService _managed = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _managed = new FakeManagedServerService();
        _host = await ServiceGateHost.StartAsync(
            [typeof(GuardProbeController), typeof(FederationServerController)],
            services => services.AddSingleton<IManagedServerService>(_managed));
    }

    [TestCleanup]
    public async Task Cleanup() => await _host.DisposeAsync();

    private static void AssertRefusedByGuard(HttpResponseMessage response, string because)
    {
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, because);
        Assert.IsTrue(response.Headers.TryGetValues("X-Bakabase-Remote-Access", out var reasons), because);
        Assert.AreEqual("HostOnly", reasons.Single(), because);

        // The shape every other remote-access refusal has, so the UI reads it the same way.
        using var body = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result);
        var code = body.RootElement.EnumerateObject()
            .Single(p => p.Name.Equals("code", StringComparison.OrdinalIgnoreCase)).Value.GetInt32();
        Assert.AreEqual(403, code, because);
    }

    private static async Task AssertReached(HttpResponseMessage response, string expectedData, string because)
    {
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, because);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), expectedData, because);
    }

    [TestMethod]
    public async Task A_state_changing_request_from_a_relay_page_is_refused()
    {
        // The case this exists for: another server's UI, one port away, sending a
        // request that needs no preflight.
        foreach (var method in new[] {HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete})
        {
            var response = await _host.SendAsync(method, "/test-probe/write", "same-site", ServiceGateHost.RelayOrigin);
            AssertRefusedByGuard(response, method.Method);
            StringAssert.DoesNotMatch(await response.Content.ReadAsStringAsync(),
                new System.Text.RegularExpressions.Regex("written"));
        }
    }

    [TestMethod]
    public async Task A_state_changing_request_from_a_website_is_refused_whatever_origin_it_names()
    {
        foreach (var origin in new[] {Attacker, "null", "http://localhost:3001", "https://exhentai.org.attacker.example", null})
        {
            var response = await _host.SendAsync(HttpMethod.Post, "/test-probe/write", "cross-site", origin);
            AssertRefusedByGuard(response, origin ?? "<no origin>");
        }
    }

    [TestMethod]
    public async Task Reads_and_navigations_from_another_site_still_pass()
    {
        await AssertReached(
            await _host.SendAsync(HttpMethod.Get, "/test-probe/read", "same-site", ServiceGateHost.RelayOrigin),
            "read", "a read");

        // A relay window switching back to this device: a top-level navigation, which
        // carries no Origin.
        var navigation = _host.Request(HttpMethod.Get, "/", "cross-site");
        navigation.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        navigation.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        var page = await _host.SendAsync(navigation);
        Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
        StringAssert.Contains(await page.Content.ReadAsStringAsync(), "<title>Bakabase</title>");

        // A preflight is the CORS middleware's to answer, and it declines a foreign origin
        // on its own; the guard stays out of its way.
        var preflight = _host.Request(HttpMethod.Options, "/test-probe/write", "cross-site", Attacker);
        preflight.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        var answer = await _host.SendAsync(preflight);
        Assert.IsFalse(answer.Headers.Contains("X-Bakabase-Remote-Access"));
        Assert.IsFalse(answer.Headers.Contains("Access-Control-Allow-Origin"));
    }

    /// <summary>
    /// A load of this Service's page into a frame on another page, shaped as a browser
    /// shapes it: a navigation with no <c>Origin</c>, naming the framing page in
    /// <c>Referer</c> (the default referrer policy sends its origin to another site).
    /// </summary>
    internal static HttpRequestMessage FrameLoad(ServiceGateHost host, string path, string dest, string site,
        string? framingPage, HttpMethod? method = null)
    {
        var request = host.Request(method ?? HttpMethod.Get, path, site);
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", dest is "embed" or "object" ? "no-cors" : "navigate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", dest);
        if (framingPage != null)
        {
            request.Headers.TryAddWithoutValidation("Referer", framingPage + "/");
        }

        return request;
    }

    internal static readonly string[] FrameDestinations = ["iframe", "frame", "embed", "object", "fencedframe"];

    [TestMethod]
    public async Task This_devices_ui_cannot_be_framed_by_a_relay_page_or_a_website()
    {
        // Clickjacking: the other page hides this device's own UI under a button of its
        // own. Everything the framed UI then sends is same-origin, so the frame's own load
        // is the one request that can be judged.
        foreach (var dest in FrameDestinations)
        {
            foreach (var (site, page) in new[]
                     {
                         ("same-site", ServiceGateHost.RelayOrigin), ("cross-site", Attacker),
                         ("same-site", "http://localhost:3001"), ("cross-site", (string?) null)
                     })
            {
                var because = $"{dest} from {page ?? "<no referrer>"}";
                AssertRefusedByGuard(await _host.SendAsync(FrameLoad(_host, "/", dest, site, page)), because);
                AssertRefusedByGuard(
                    await _host.SendAsync(FrameLoad(_host, "/test-probe/read", dest, site, page)), because);
            }
        }

        // Whatever the method: a form posted into a frame is as good a frame as any, and
        // this time the browser does name the page, in Origin.
        var form = FrameLoad(_host, "/", "iframe", "same-site", ServiceGateHost.RelayOrigin, HttpMethod.Post);
        form.Headers.TryAddWithoutValidation("Origin", ServiceGateHost.RelayOrigin);
        AssertRefusedByGuard(await _host.SendAsync(form), "a form posted into a frame");
    }

    [TestMethod]
    public async Task A_relay_window_switching_back_is_still_a_top_level_navigation_that_passes()
    {
        // The line the frame rule must not cross: the same GET, from the same relay page,
        // as the whole window rather than a frame inside it.
        var navigation = FrameLoad(_host, "/", "document", "same-site", ServiceGateHost.RelayOrigin);
        var page = await _host.SendAsync(navigation);
        Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
        StringAssert.Contains(await page.Content.ReadAsStringAsync(), "<title>Bakabase</title>");

        var fromWebsite = await _host.SendAsync(FrameLoad(_host, "/", "document", "cross-site", Attacker));
        Assert.AreEqual(HttpStatusCode.OK, fromWebsite.StatusCode, "a link on any website still opens the app");
    }

    [TestMethod]
    public async Task This_devices_own_pages_still_frame_their_own()
    {
        // The profiler page frames /profiler/results-index from the same origin.
        foreach (var dest in FrameDestinations)
        {
            await AssertReached(await _host.SendAsync(FrameLoad(_host, "/test-probe/read", dest, "same-origin",
                _host.Origin)), "read", dest);
        }

        // An engine that sends no fetch metadata is left to the framing headers.
        var legacy = _host.Request(HttpMethod.Get, "/test-probe/read");
        legacy.Headers.TryAddWithoutValidation("Referer", ServiceGateHost.RelayOrigin + "/");
        await AssertReached(await _host.SendAsync(legacy), "read", "no fetch metadata");
    }

    [TestMethod]
    public async Task Yarn_devs_profiler_page_frames_the_api_in_a_development_build()
    {
        if (!ServiceCorsOrigins.ForThisBuild.TrustsDevServer)
        {
            Assert.Inconclusive("Only a development build trusts the dev server.");
        }

        // localhost:3000 framing localhost:<port>: same-site, and named only by Referer.
        foreach (var site in new[] {"same-site", "cross-site"})
        {
            await AssertReached(
                await _host.SendAsync(FrameLoad(_host, "/test-probe/read", "iframe", site, ServiceGateHost.DevOrigin)),
                "read", site);
        }

        var shell = await _host.SendAsync(FrameLoad(_host, "/", "iframe", "same-site", ServiceGateHost.DevOrigin));
        Assert.AreEqual(HttpStatusCode.OK, shell.StatusCode);
        CollectionAssert.Contains(shell.Headers.GetValues(FrameAncestorsPolicy.ContentSecurityPolicyHeader).ToList(),
            $"frame-ancestors 'self' {ServiceGateHost.DevOrigin}");
        Assert.IsFalse(shell.Headers.Contains(FrameAncestorsPolicy.FrameOptionsHeader),
            "X-Frame-Options cannot name the dev server");
    }

    [TestMethod]
    public async Task A_get_that_runs_on_this_machine_is_refused_from_another_site()
    {
        AssertRefusedByGuard(
            await _host.SendAsync(HttpMethod.Get, "/test-probe/open", "same-site", ServiceGateHost.RelayOrigin),
            "fetch from a relay page");
        AssertRefusedByGuard(await _host.SendAsync(HttpMethod.Get, "/test-probe/open", "cross-site", Attacker),
            "fetch from a website");

        // An image tag or a link on any website: no Origin at all.
        var image = _host.Request(HttpMethod.Get, "/test-probe/open", "cross-site");
        image.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "no-cors");
        image.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "image");
        AssertRefusedByGuard(await _host.SendAsync(image), "an image tag");

        var link = _host.Request(HttpMethod.Get, "/test-probe/open", "cross-site");
        link.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        link.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        AssertRefusedByGuard(await _host.SendAsync(link), "a link");
    }

    [TestMethod]
    public async Task This_devices_own_pages_and_native_callers_are_unaffected()
    {
        await AssertReached(await _host.SendAsync(HttpMethod.Post, "/test-probe/write", "same-origin", _host.Origin),
            "written", "this device's own window");
        await AssertReached(await _host.SendAsync(HttpMethod.Get, "/test-probe/open", "same-origin", _host.Origin),
            "opened", "this device's own window opening a folder");

        // Typed into the address bar, or a bookmark.
        await AssertReached(await _host.SendAsync(HttpMethod.Get, "/test-probe/open", "none"), "opened", "typed URL");

        // A native player or a script sends neither fetch metadata nor an Origin; this is not
        // a page the guard can judge, and it keeps the old behaviour.
        await AssertReached(await _host.SendAsync(HttpMethod.Post, "/test-probe/write"), "written", "a script");
        await AssertReached(await _host.SendAsync(HttpMethod.Get, "/test-probe/open"), "opened", "a player");

        // This device's own window in an engine that sends no fetch metadata still names
        // itself in Origin, on whichever address it was opened.
        await AssertReached(await _host.SendAsync(HttpMethod.Post, "/test-probe/write", null, _host.Origin),
            "written", "own window, no fetch metadata");
        var onLocalhost = _host.Request(HttpMethod.Post, "/test-probe/write", null, $"http://localhost:{_host.Port}");
        onLocalhost.Headers.Host = $"localhost:{_host.Port}";
        await AssertReached(await _host.SendAsync(onLocalhost), "written", "own window on localhost, no fetch metadata");
    }

    [TestMethod]
    public async Task An_engine_without_fetch_metadata_is_judged_by_the_origin_it_names()
    {
        // WebKit before fetch metadata, or any engine that leaves it off: the page is still
        // named in Origin on every request that can change something.
        foreach (var origin in new[] {ServiceGateHost.RelayOrigin, Attacker, "null", "http://localhost:34650"})
        {
            foreach (var method in new[] {HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete})
            {
                AssertRefusedByGuard(await _host.SendAsync(method, "/test-probe/write", null, origin),
                    $"{method} from {origin}");
            }

            // And a read whose action runs on this machine, judged after routing.
            AssertRefusedByGuard(await _host.SendAsync(HttpMethod.Get, "/test-probe/open", null, origin),
                $"open from {origin}");

            // A plain read still passes: it changes nothing.
            await AssertReached(await _host.SendAsync(HttpMethod.Get, "/test-probe/read", null, origin), "read", origin);
        }

        // The pages it trusts are still trusted.
        foreach (var origin in ServiceCorsOrigins.UserscriptSites.Append("chrome-extension://dhdgffkkebhmkfjojejmpbldmpobfkfo"))
        {
            await AssertReached(await _host.SendAsync(HttpMethod.Post, "/test-probe/write", null, origin), "written", origin);
            await AssertReached(await _host.SendAsync(HttpMethod.Get, "/test-probe/open", null, origin), "opened", origin);
        }
    }

    [TestMethod]
    public async Task Yarn_dev_reaches_the_api_across_ports()
    {
        // localhost:3000 → localhost:<port> is same-site; → 127.0.0.1:<port> is cross-site.
        foreach (var site in new[] {"same-site", "cross-site"})
        {
            await AssertReached(
                await _host.SendAsync(HttpMethod.Post, "/test-probe/write", site, ServiceGateHost.DevOrigin),
                "written", site);
            await AssertReached(
                await _host.SendAsync(HttpMethod.Get, "/test-probe/open", site, ServiceGateHost.DevOrigin),
                "opened", site);
        }

        var preflight = _host.Request(HttpMethod.Options, "/test-probe/write", "same-site", ServiceGateHost.DevOrigin);
        preflight.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        var answer = await _host.SendAsync(preflight);
        Assert.IsTrue(answer.IsSuccessStatusCode);
        Assert.AreEqual(ServiceGateHost.DevOrigin, answer.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [TestMethod]
    public async Task The_userscript_keeps_working()
    {
        // Sent from the site's page (a content-script fetch) or by the userscript
        // manager itself (an extension origin); either way it must land.
        foreach (var origin in ServiceCorsOrigins.UserscriptSites.Concat([
                     "chrome-extension://dhdgffkkebhmkfjojejmpbldmpobfkfo",
                     "moz-extension://0c8d8b0e-8f7c-4b6a-9d8e-2f1c3b4a5d6e",
                     "safari-web-extension://4A1B2C3D-0000-1111-2222-333344445555"
                 ]))
        {
            await AssertReached(await _host.SendAsync(HttpMethod.Post, "/test-probe/write", "cross-site", origin),
                "written", origin);
        }

        // An extension with host permissions is usually labelled "none".
        await AssertReached(await _host.SendAsync(HttpMethod.Post, "/test-probe/write", "none",
            "chrome-extension://dhdgffkkebhmkfjojejmpbldmpobfkfo"), "written", "extension, none");
        await AssertReached(await _host.SendAsync(HttpMethod.Get, "/test-probe/read", "cross-site",
            "https://exhentai.org"), "read", "health check");
    }

    [TestMethod]
    public async Task The_guard_trusts_exactly_the_pages_cors_trusts()
    {
        // The guard asks the CORS policy's question from the same sources. Pin that they
        // agree, so an origin added to one is never refused by the other. Extension
        // origins are the one deliberate addition, covered above.
        var origins = new[]
        {
            ServiceGateHost.DevOrigin, $"http://localhost:{_host.Port}", "https://www.north-plus.net",
            "https://exhentai.org", ServiceGateHost.RelayOrigin, Attacker, "http://localhost:3001",
            "http://exhentai.org", "https://north-plus.net", "http://127.0.0.1:3000"
        };

        foreach (var origin in origins)
        {
            var preflight = _host.Request(HttpMethod.Options, "/test-probe/write", "cross-site", origin);
            preflight.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
            var corsAllows = (await _host.SendAsync(preflight)).Headers.Contains("Access-Control-Allow-Origin");

            var guardAllows = (await _host.SendAsync(HttpMethod.Post, "/test-probe/write", "cross-site", origin))
                .StatusCode != HttpStatusCode.Forbidden;

            Assert.AreEqual(corsAllows, guardAllows, origin);
        }
    }

    [TestMethod]
    public async Task Callers_on_other_machines_are_left_to_the_remote_access_gate()
    {
        // Out of scope by design: a LAN caller is judged by RemoteAccessMiddleware and the
        // authorization filter, which this guard neither loosens nor duplicates. In
        // Unrestricted mode (the container default) that gate lets it through, as before.
        _host.Remote.Mode = RemoteAccessMode.Unrestricted;
        var request = _host.Request(HttpMethod.Post, "/test-probe/write", "cross-site", Attacker);
        request.Headers.TryAddWithoutValidation(ServiceGateHost.RemoteIpHeader, "192.168.1.20");
        await AssertReached(await _host.SendAsync(request), "written", "LAN, Unrestricted");
    }

    [TestMethod]
    public async Task The_hub_negotiates_for_this_devices_own_page_and_yarn_dev_but_not_a_relay()
    {
        const string negotiate = "/hub/ui/negotiate?negotiateVersion=1";

        foreach (var (site, origin) in new[] {("same-origin", _host.Origin), ("same-site", ServiceGateHost.DevOrigin)})
        {
            var response = await _host.SendAsync(HttpMethod.Post, negotiate, site, origin);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, origin);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "connectionToken", origin);
        }

        AssertRefusedByGuard(
            await _host.SendAsync(HttpMethod.Post, negotiate, "same-site", ServiceGateHost.RelayOrigin), "relay");
    }

    [TestMethod]
    public async Task A_websocket_from_another_site_is_refused_while_this_devices_own_connects()
    {
        // A WebSocket handshake is a GET no CORS check ever looks at, so it is judged like
        // a write.
        async Task<ClientWebSocket> Connect(string site, string origin)
        {
            var socket = new ClientWebSocket();
            socket.Options.CollectHttpResponseDetails = true;
            socket.Options.SetRequestHeader("Origin", origin);
            socket.Options.SetRequestHeader("Sec-Fetch-Site", site);
            socket.Options.SetRequestHeader("Sec-Fetch-Mode", "websocket");
            socket.Options.Proxy = null;
            try
            {
                await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_host.Port}/hub/ui"), CancellationToken.None);
            }
            catch (WebSocketException)
            {
            }

            return socket;
        }

        using (var own = await Connect("same-origin", _host.Origin))
        {
            Assert.AreEqual(WebSocketState.Open, own.State, "this device's own window");
        }

        using (var dev = await Connect("same-site", ServiceGateHost.DevOrigin))
        {
            Assert.AreEqual(WebSocketState.Open, dev.State, "yarn dev");
        }

        using (var relay = await Connect("same-site", ServiceGateHost.RelayOrigin))
        {
            Assert.AreNotEqual(WebSocketState.Open, relay.State, "relay page");
            Assert.AreEqual(HttpStatusCode.Forbidden, relay.HttpStatusCode, "relay page");
        }
    }

    [TestMethod]
    public async Task The_federation_local_interface_still_answers_this_devices_own_page()
    {
        const string probe = "/federation/local/servers/probe";
        const string body = """{"address":"192.168.1.9"}""";

        var own = await _host.SendAsync(HttpMethod.Post, probe, "same-origin", _host.Origin, body);
        Assert.AreEqual(HttpStatusCode.OK, own.StatusCode, await own.Content.ReadAsStringAsync());

        // A script on this machine, with no browser around it.
        var script = await _host.SendAsync(HttpMethod.Post, probe, json: body);
        Assert.AreEqual(HttpStatusCode.OK, script.StatusCode);

        Assert.AreEqual(2, _managed.Calls.Count(c => c == "Probe 192.168.1.9"));
    }

    [TestMethod]
    public async Task The_federation_local_interface_still_answers_yarn_dev()
    {
        if (AppService.RuntimeMode != RuntimeMode.Dev)
        {
            Assert.Inconclusive("The federation interface trusts the dev server only in a development build.");
        }

        var response = await _host.SendAsync(HttpMethod.Post, "/federation/local/servers/probe", "same-site",
            ServiceGateHost.DevOrigin, """{"address":"192.168.1.9"}""");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task A_relay_page_cannot_drive_the_federation_local_interface()
    {
        // Refused by the federation gate first, which demands this Service's own origin;
        // the cross-site guard would refuse it next.
        foreach (var method in new[] {HttpMethod.Post, HttpMethod.Get})
        {
            var response = await _host.SendAsync(method,
                method == HttpMethod.Get ? "/federation/local/servers" : "/federation/local/servers/probe",
                "same-site", ServiceGateHost.RelayOrigin, method == HttpMethod.Get ? null : """{"address":"x"}""");
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, method.Method);
        }

        Assert.IsTrue(_managed.Calls.IsEmpty);
    }
}
