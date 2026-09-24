using System.Net;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Models.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.Tests.RemoteAccess.Service;

/// <summary>
/// Every page a request to this Service can name, crossed with every kind of request the
/// guard tells apart, with and without the fetch metadata a browser adds — judged by the
/// guard's own decision, as a development and as a packaged build.
/// </summary>
/// <remarks>
/// <para>
/// The case that made this matrix: Chromium, and so WebView2, sends no <c>Sec-Fetch-*</c>
/// header on a WebSocket handshake — only <c>Host</c>, <c>Upgrade</c> and <c>Origin</c>. A
/// guard that recognised another site's page by <c>Sec-Fetch-Site</c> alone let a relay
/// page — another server's JavaScript, one port away — open this device's UI hub and read
/// every options object it pushes. So every handshake row below is also sent the way
/// Chromium sends it: without metadata.
/// </para>
/// <para>
/// The oracle is written from the rules as the design states them, not from the guard's
/// code: a handshake is refused when it names a page that is neither the origin it was sent
/// to nor trusted; a write, when the browser says another site sent it and the page is not
/// trusted, or when no metadata came and it names a foreign page; nothing else is refused
/// before routing.
/// </para>
/// </remarks>
[TestClass]
public class LoopbackCrossSiteGuardMatrixTests
{
    private const int Port = 34567;

    /// <summary>The Service's endpoints as the host publishes them: 0.0.0.0 read back as localhost.</summary>
    private static readonly string ApiEndpoint = $"http://localhost:{Port}";

    public enum Kind
    {
        Get,
        Post,
        WebSocket,
        Http2WebSocket
    }

    /// <summary>A page a request can come from, and whether a build trusts it.</summary>
    private sealed record Page(string Name, string? Origin, bool TrustedInDev, bool TrustedInRelease);

    private static readonly Page[] Pages =
    [
        new("own window on localhost", $"http://localhost:{Port}", true, true),
        new("own window on 127.0.0.1", $"http://127.0.0.1:{Port}", false, false),
        new("relay, same host other port", "http://127.0.0.1:34650", false, false),
        new("relay on localhost", "http://localhost:34650", false, false),
        new("website", "https://attacker.example", false, false),
        new("opaque page", "null", false, false),
        new("no origin", null, false, false),
        new("userscript site", "https://exhentai.org", true, true),
        new("browser extension", "chrome-extension://dhdgffkkebhmkfjojejmpbldmpobfkfo", true, true),
        new("yarn dev", "http://localhost:3000", true, false),
        new("two origins in one header", $"http://localhost:{Port}, https://attacker.example", false, false)
    ];

    private static readonly string[] Hosts = [$"localhost:{Port}", $"127.0.0.1:{Port}"];

    /// <summary>
    /// What a browser honestly puts in <c>Sec-Fetch-Site</c> for <paramref name="origin"/>
    /// calling <paramref name="host"/>: sites ignore ports, and <c>localhost</c> and
    /// <c>127.0.0.1</c> are different sites. An unnamed page is some other site's.
    /// </summary>
    private static string HonestSite(string? origin, string host)
    {
        if (origin == null || !Uri.TryCreate(origin, UriKind.Absolute, out var page) || page.Scheme is not ("http" or "https"))
        {
            return "cross-site";
        }

        if (origin == $"http://{host}")
        {
            return "same-origin";
        }

        var hostName = host[..host.LastIndexOf(':')];
        return page.Scheme == "http" && page.Host == hostName ? "same-site" : "cross-site";
    }

    private static HttpContext Request(Kind kind, string host, string? origin, string? site, RuntimeMode runtime,
        IPAddress? peer = null)
    {
        var services = new ServiceCollection()
            .AddSingleton(new AppContext {ApiEndpoints = [ApiEndpoint], ApiEndpoint = ApiEndpoint})
            .AddSingleton(new ServiceCorsOrigins(runtime))
            .BuildServiceProvider();

        var context = new DefaultHttpContext {RequestServices = services};
        context.Connection.RemoteIpAddress = peer ?? IPAddress.Loopback;
        context.Request.Scheme = "http";
        context.Request.Host = HostString.FromUriComponent(host);
        context.Request.Path = "/hub/ui";

        switch (kind)
        {
            case Kind.Get:
                context.Request.Method = HttpMethods.Get;
                break;
            case Kind.Post:
                context.Request.Method = HttpMethods.Post;
                break;
            case Kind.WebSocket:
                // What Chromium 153 sends, captured off the wire: no fetch metadata.
                context.Request.Method = HttpMethods.Get;
                context.Request.Headers.Connection = "Upgrade";
                context.Request.Headers.Upgrade = "websocket";
                context.Request.Headers.SecWebSocketVersion = "13";
                context.Request.Headers.SecWebSocketKey = "dGhlIHNhbXBsZSBub25jZQ==";
                break;
            case Kind.Http2WebSocket:
                context.Request.Method = HttpMethods.Connect;
                context.Features.Set<IHttpExtendedConnectFeature>(new ExtendedConnect("websocket"));
                break;
        }

        if (origin != null)
        {
            context.Request.Headers.Origin = origin;
        }

        if (site != null)
        {
            context.Request.Headers[LoopbackCrossSiteGuard.SecFetchSiteHeader] = site;
        }

        return context;
    }

    private sealed class ExtendedConnect(string protocol) : IHttpExtendedConnectFeature
    {
        public bool IsExtendedConnect => true;
        public string? Protocol => protocol;
        public ValueTask<Stream> AcceptAsync() => throw new NotSupportedException();
    }

    [TestMethod]
    [DataRow(RuntimeMode.Dev)]
    [DataRow(RuntimeMode.WinForms)]
    [DataRow(RuntimeMode.MacOS)]
    public void The_full_matrix(RuntimeMode runtime)
    {
        var checkedRows = 0;

        foreach (var host in Hosts)
        foreach (var page in Pages)
        foreach (var kind in Enum.GetValues<Kind>())
        foreach (var withMetadata in new[] {false, true})
        {
            var site = withMetadata ? HonestSite(page.Origin, host) : null;
            var label = $"{runtime} {kind} to {host} from {page.Name} ({page.Origin ?? "no Origin"}), " +
                        $"Sec-Fetch-Site: {site ?? "(absent)"}";

            var trusted = runtime == RuntimeMode.Dev ? page.TrustedInDev : page.TrustedInRelease;
            var own = page.Origin == $"http://{host}";
            var foreign = page.Origin != null && !own && !trusted;
            var anotherSite = site is "same-site" or "cross-site";
            var handshake = kind is Kind.WebSocket or Kind.Http2WebSocket;
            var canChangeState = kind != Kind.Get;

            var expected = handshake && foreign
                ? LoopbackCrossSiteRefusal.WebSocketFromForeignOrigin
                : canChangeState && anotherSite && !trusted
                    ? LoopbackCrossSiteRefusal.AnotherSite
                    : canChangeState && site == null && foreign
                        ? LoopbackCrossSiteRefusal.ForeignOriginWithoutFetchMetadata
                        : LoopbackCrossSiteRefusal.None;

            Assert.AreEqual(expected, LoopbackCrossSiteGuard.Judge(Request(kind, host, page.Origin, site, runtime)),
                label);
            checkedRows++;
        }

        Assert.AreEqual(Hosts.Length * Pages.Length * 4 * 2, checkedRows);
    }

    [TestMethod]
    public void Chromiums_handshake_from_a_relay_page_is_refused_in_both_forms_of_this_devices_address()
    {
        // The reported case, spelled out: ws://127.0.0.1:<port>/hub/ui and
        // ws://localhost:<port>/hub/ui opened by the page on 127.0.0.1:34650.
        foreach (var host in Hosts)
        foreach (var runtime in new[] {RuntimeMode.Dev, RuntimeMode.WinForms})
        {
            Assert.AreEqual(LoopbackCrossSiteRefusal.WebSocketFromForeignOrigin,
                LoopbackCrossSiteGuard.Judge(Request(Kind.WebSocket, host, "http://127.0.0.1:34650", null, runtime)),
                $"{runtime} {host}");
        }
    }

    [TestMethod]
    public void This_devices_window_keeps_its_hub_whichever_address_it_was_opened_on()
    {
        foreach (var host in Hosts)
        foreach (var runtime in new[] {RuntimeMode.Dev, RuntimeMode.WinForms, RuntimeMode.Docker})
        {
            Assert.AreEqual(LoopbackCrossSiteRefusal.None,
                LoopbackCrossSiteGuard.Judge(Request(Kind.WebSocket, host, $"http://{host}", null, runtime)),
                $"{runtime} {host}");
        }

        // IPv6 loopback, as a browser writes it.
        Assert.AreEqual(LoopbackCrossSiteRefusal.None,
            LoopbackCrossSiteGuard.Judge(Request(Kind.WebSocket, $"[::1]:{Port}", $"http://[::1]:{Port}", null,
                RuntimeMode.WinForms)));
    }

    [TestMethod]
    public void This_ui_under_another_name_is_still_its_own_page()
    {
        // A hosts-file alias, or a reverse proxy or tunnel on this machine (nginx, Caddy,
        // frp), shows this UI under its own name. Over plain HTTP the browser sends no fetch
        // metadata, so the page's saves and its hub must pass on Origin == Host alone, or the
        // UI cannot write or connect at all. The owner chose this over refusing DNS-rebound
        // names here, which needs a Host allow-list covering reads too.
        foreach (var name in new[] {"bakabase.lan", "nas-proxy.example"})
        foreach (var kind in new[] {Kind.WebSocket, Kind.Http2WebSocket, Kind.Post})
        {
            Assert.AreEqual(LoopbackCrossSiteRefusal.None,
                LoopbackCrossSiteGuard.Judge(Request(kind, $"{name}:{Port}", $"http://{name}:{Port}", null,
                    RuntimeMode.WinForms)), $"{name} {kind}");
        }

        // The same name on another port, or another name, is still a different page.
        Assert.AreEqual(LoopbackCrossSiteRefusal.WebSocketFromForeignOrigin,
            LoopbackCrossSiteGuard.Judge(Request(Kind.WebSocket, $"bakabase.lan:{Port}", $"http://bakabase.lan:{Port + 1}",
                null, RuntimeMode.WinForms)));
        Assert.AreEqual(LoopbackCrossSiteRefusal.ForeignOriginWithoutFetchMetadata,
            LoopbackCrossSiteGuard.Judge(Request(Kind.Post, $"bakabase.lan:{Port}", $"http://other.lan:{Port}", null,
                RuntimeMode.WinForms)));
    }

    [TestMethod]
    public void An_origin_is_its_own_only_when_it_is_exactly_the_address_the_request_went_to()
    {
        foreach (var near in new[]
                 {
                     $"https://localhost:{Port}", $"http://localhost:{Port + 1}", "http://localhost",
                     $"http://user@localhost:{Port}", $"http://localhost:{Port}/path", $"ws://localhost:{Port}",
                     $"http://localhost.:{Port}", $"http://localhost:{Port}?q"
                 })
        {
            Assert.AreEqual(LoopbackCrossSiteRefusal.WebSocketFromForeignOrigin,
                LoopbackCrossSiteGuard.Judge(Request(Kind.WebSocket, $"localhost:{Port}", near, null, RuntimeMode.WinForms)),
                near);
        }

        // Case does not decide anything, and neither does spelling out the default port.
        Assert.AreEqual(LoopbackCrossSiteRefusal.None,
            LoopbackCrossSiteGuard.Judge(Request(Kind.WebSocket, $"LOCALHOST:{Port}", $"HTTP://localhost:{Port}", null,
                RuntimeMode.WinForms)));
        Assert.AreEqual(LoopbackCrossSiteRefusal.None,
            LoopbackCrossSiteGuard.Judge(Request(Kind.WebSocket, "localhost", "http://localhost:80", null,
                RuntimeMode.WinForms)));
    }

    [TestMethod]
    public void Callers_on_other_machines_are_not_this_guards_to_judge()
    {
        // RemoteAccessMiddleware and the authorization filter decide those; this guard
        // neither loosens nor duplicates them.
        foreach (var kind in Enum.GetValues<Kind>())
        {
            Assert.AreEqual(LoopbackCrossSiteRefusal.None,
                LoopbackCrossSiteGuard.Judge(Request(kind, $"192.168.1.5:{Port}", "https://attacker.example",
                    "cross-site", RuntimeMode.WinForms, IPAddress.Parse("192.168.1.20"))), kind.ToString());
        }

        // An IPv4 loopback peer reported as IPv6-mapped is still loopback.
        Assert.AreEqual(LoopbackCrossSiteRefusal.WebSocketFromForeignOrigin,
            LoopbackCrossSiteGuard.Judge(Request(Kind.WebSocket, $"127.0.0.1:{Port}", "https://attacker.example", null,
                RuntimeMode.WinForms, IPAddress.Loopback.MapToIPv6())));
    }

    [TestMethod]
    public void A_near_handshake_is_treated_as_one()
    {
        // Upgrade names websocket but Connection does not ask for it: Kestrel would not
        // upgrade it, and refusing it anyway costs nothing.
        var context = Request(Kind.Get, $"localhost:{Port}", "https://attacker.example", null, RuntimeMode.WinForms);
        context.Request.Headers.Upgrade = "WebSocket, h2c";
        Assert.AreEqual(LoopbackCrossSiteRefusal.WebSocketFromForeignOrigin, LoopbackCrossSiteGuard.Judge(context));

        // An engine that labels its handshake says so in Sec-Fetch-Mode.
        var labelled = Request(Kind.Get, $"localhost:{Port}", "https://attacker.example", null, RuntimeMode.WinForms);
        labelled.Request.Headers["Sec-Fetch-Mode"] = "websocket";
        Assert.AreEqual(LoopbackCrossSiteRefusal.WebSocketFromForeignOrigin, LoopbackCrossSiteGuard.Judge(labelled));

        // Another extended CONNECT protocol is not a WebSocket — but still a CONNECT, which
        // can change state like any other method.
        var tunnel = Request(Kind.Get, $"localhost:{Port}", "https://attacker.example", null, RuntimeMode.WinForms);
        tunnel.Request.Method = HttpMethods.Connect;
        tunnel.Features.Set<IHttpExtendedConnectFeature>(new ExtendedConnect("webtransport"));
        Assert.AreEqual(LoopbackCrossSiteRefusal.ForeignOriginWithoutFetchMetadata, LoopbackCrossSiteGuard.Judge(tunnel));
    }

    [TestMethod]
    public void The_after_routing_check_sees_a_foreign_page_the_same_way()
    {
        // LoopbackCrossSiteUserMachineFilter asks this about a GET whose action runs on
        // this machine: labelled by the browser, or — with no metadata — named in Origin.
        bool Untrusted(string? origin, string? site) =>
            LoopbackCrossSiteGuard.IsFromUntrustedPage(Request(Kind.Get, $"localhost:{Port}", origin, site,
                RuntimeMode.WinForms));

        Assert.IsTrue(Untrusted("http://127.0.0.1:34650", "cross-site"));
        Assert.IsTrue(Untrusted("http://127.0.0.1:34650", null));
        Assert.IsTrue(Untrusted("null", null));
        Assert.IsTrue(Untrusted(null, "same-site"), "an image tag on another site names no page");

        Assert.IsFalse(Untrusted(null, null), "a player or a script");
        Assert.IsFalse(Untrusted($"http://localhost:{Port}", null), "this device's window, older engine");
        Assert.IsFalse(Untrusted($"http://localhost:{Port}", "same-origin"));
        Assert.IsFalse(Untrusted(null, "none"), "typed into the address bar");
        Assert.IsFalse(Untrusted("https://exhentai.org", "cross-site"), "the userscript");
        Assert.IsFalse(Untrusted("https://exhentai.org", null), "the userscript, older engine");
    }
}
