using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.Service.Components.RemoteAccess;

/// <summary>Which of <see cref="LoopbackCrossSiteGuard"/>'s rules refuses a request.</summary>
public enum LoopbackCrossSiteRefusal
{
    None = 0,

    /// <summary>
    /// A WebSocket handshake whose <c>Origin</c> is neither this Service's own page nor one
    /// it trusts. Judged by <c>Origin</c> alone: Chromium, and so WebView2, sends no fetch
    /// metadata on a handshake.
    /// </summary>
    WebSocketFromForeignOrigin = 1,

    /// <summary>
    /// The browser says a page on another site sent this (<c>Sec-Fetch-Site</c>), it can
    /// change something or puts this UI in a frame, and the page is not a trusted one.
    /// </summary>
    AnotherSite = 2,

    /// <summary>
    /// A request that can change something, from an engine that sends no fetch metadata,
    /// naming a page in <c>Origin</c> that is neither this Service's own nor trusted.
    /// </summary>
    ForeignOriginWithoutFetchMetadata = 3
}

/// <summary>
/// Refuses what a page on another site makes this computer's browser send to its own
/// Service.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RemoteAccessMiddleware"/> trusts every loopback caller, because a loopback
/// caller is the person sitting here. The browser on this computer is not always acting
/// for them, though: any page it has open can send requests to <c>localhost</c>. And since
/// the desktop app shows other servers through relays on <c>127.0.0.1</c>, one of those
/// pages is another server's own UI, running that server's JavaScript one port away. A
/// "simple" request — a form post, a <c>text/plain</c> fetch, an image — goes out without
/// a CORS preflight. The page never gets to read the answer, but a request that deletes
/// something or launches a program did not need its answer read.
/// </para>
/// <para>
/// A WebSocket handshake is judged first, and by <c>Origin</c>: it is a GET that opens a
/// two-way channel no CORS check ever looks at — the UI hub pushes every options object
/// down it, third-party cookies and API keys among them — and Chromium, and so WebView2,
/// sends no fetch metadata on it, only <c>Host</c>, <c>Upgrade</c> and <c>Origin</c>. A
/// handshake is refused when its <c>Origin</c> is present and is neither this request's
/// own origin — the scheme and <c>Host</c> it was sent to, which is how this device's window
/// reaches its hub whether it was opened on <c>localhost</c>, <c>127.0.0.1</c> or a name an
/// alias or local proxy gives it — nor a page this Service trusts. <c>null</c>, which a sandboxed or
/// opaque page sends, is foreign. Both the HTTP/1.1 upgrade and HTTP/2's extended
/// <c>CONNECT</c> count, though the Service only listens on plain HTTP, where browsers
/// never speak HTTP/2.
/// </para>
/// <para>
/// Everything else is judged by fetch metadata, which tells the requests apart without
/// guessing: the browser labels each one with <c>Sec-Fetch-Site</c>, and a request from a
/// page on another origin of this computer says <c>same-site</c> (ports do not make a site)
/// while one from anywhere else says <c>cross-site</c>. Such a request is refused when it
/// can change something — any method but GET, HEAD and OPTIONS — unless the page it came
/// from is one the CORS policy already lets in (<see cref="ServiceCorsOrigins"/>: the
/// userscript's sites, this Service's own endpoints, and <c>yarn dev</c>'s server in a
/// development build) or a browser extension, which is how the userscript manager sends
/// the userscript's requests. For an engine that sends no fetch metadata, a request that
/// can change something is refused on the same terms when its <c>Origin</c> names a page
/// that is neither this Service's own nor trusted: browsers name the page on every such
/// request, so a present, foreign <c>Origin</c> is a page, never a player or a script.
/// </para>
/// <para>
/// A load into a frame — <c>Sec-Fetch-Dest</c> <c>iframe</c>, <c>frame</c>, <c>embed</c>,
/// <c>object</c> or <c>fencedframe</c> — is refused the same way, whatever its method. A
/// frame changes nothing by being loaded, but it puts this device's own UI inside the
/// other page, which can hide it under a button of its own and collect the user's clicks
/// on it; everything the framed UI then sends is same-origin, so nothing here would ever
/// see a foreign page again. A frame's navigation carries no <c>Origin</c>, so for it the
/// page that framed it is read from <c>Referer</c>, which a page can shorten or drop but
/// never point at another site. <see cref="FrameAncestorsPolicy"/> tells the browser the
/// same thing, for engines that send no fetch metadata.
/// </para>
/// <para>
/// Everything else passes as before. A GET changes nothing on its own, so top-level
/// navigations (<c>Sec-Fetch-Dest: document</c>) still work — that is how a relay window
/// switches back to this device. The exception, a GET whose action runs on this machine
/// (opening a folder, launching a player, starting a program), is decided after routing by
/// <see cref="LoopbackCrossSiteUserMachineFilter"/>, the only place that can see which
/// action a request reaches. A request with neither fetch metadata nor an <c>Origin</c>
/// comes from a native player, a script or an older engine's navigation, not from a page
/// this can judge, and keeps the old behaviour — the same rule the relay's own guard
/// applies to a missing <c>Origin</c>.
/// </para>
/// <para>
/// Runs on every loopback request, the federation interfaces included. Their own gate is
/// stricter already — <c>/federation/local</c> demands this Service's own origin — so for
/// them this only repeats a refusal: a peer's request carries no fetch metadata, and this
/// device's own UI is same-origin, or <c>yarn dev</c>, which CORS trusts.
/// </para>
/// </remarks>
public sealed class LoopbackCrossSiteGuard(RequestDelegate next, ILogger<LoopbackCrossSiteGuard> logger)
{
    public const string SecFetchSiteHeader = "Sec-Fetch-Site";

    private const string SecFetchModeHeader = "Sec-Fetch-Mode";

    private const string SecFetchDestHeader = "Sec-Fetch-Dest";

    /// <summary>
    /// The <c>Sec-Fetch-Dest</c> of a load that puts the response inside another page.
    /// </summary>
    private static readonly string[] FrameDestinations = ["iframe", "frame", "embed", "object", "fencedframe"];

    /// <summary>
    /// Origins a browser extension's own requests carry. A web page cannot claim one — the
    /// browser writes <c>Origin</c> itself — and an extension the user installed and let
    /// reach this host can do far more than send it a POST.
    /// </summary>
    private static readonly string[] BrowserExtensionSchemes =
        ["chrome-extension", "moz-extension", "safari-web-extension", "ms-browser-extension"];

    internal const string RefusalMessage =
        "This request came from a page on another site, and Bakabase only lets its own pages change anything on this computer.";

    public async Task InvokeAsync(HttpContext context)
    {
        var refusal = Judge(context);

        if (refusal != LoopbackCrossSiteRefusal.None)
        {
            logger.LogDebug(
                "Refused {Method} {Path} ({Rule}, dest {Dest}): loopback request from {Site} page {Origin}",
                context.Request.Method, context.Request.Path, refusal,
                context.Request.Headers[SecFetchDestHeader].ToString(),
                context.Request.Headers[SecFetchSiteHeader].ToString(), PageOrigin(context.Request));

            context.Response.StatusCode = (int) HttpStatusCode.Forbidden;
            context.Response.ContentType = "application/json";
            context.Response.Headers["X-Bakabase-Remote-Access"] = RemoteAccessDenialReason.HostOnly.ToString();
            await context.Response.WriteAsync(
                JsonConvert.SerializeObject(BaseResponseBuilder.Build(ResponseCode.Unauthorized, RefusalMessage)));
            return;
        }

        await next(context);
    }

    /// <summary>
    /// Which rule, if any, refuses <paramref name="context"/>'s request before routing. Only
    /// ever a loopback request: a caller on another machine is
    /// <see cref="RemoteAccessMiddleware"/>'s to judge.
    /// </summary>
    internal static LoopbackCrossSiteRefusal Judge(HttpContext context)
    {
        if (!IsLoopback(context))
        {
            return LoopbackCrossSiteRefusal.None;
        }

        var request = context.Request;

        // First, and whatever else the request says: no browser labels a handshake with
        // fetch metadata that could be read instead.
        if (IsWebSocketHandshake(context) && HasForeignOrigin(context))
        {
            return LoopbackCrossSiteRefusal.WebSocketFromForeignOrigin;
        }

        if ((IsStateChanging(context) || IsFrameLoad(request)) && IsFromAnotherSite(request) &&
            !IsTrustedPage(context))
        {
            return LoopbackCrossSiteRefusal.AnotherSite;
        }

        if (IsStateChanging(context) && !SendsFetchMetadata(request) && HasForeignOrigin(context))
        {
            return LoopbackCrossSiteRefusal.ForeignOriginWithoutFetchMetadata;
        }

        return LoopbackCrossSiteRefusal.None;
    }

    /// <summary>
    /// Whether this request reached the loopback interface from a page this Service does not
    /// trust: one the browser says is on another site, or — from an engine that sends no
    /// fetch metadata — one whose <c>Origin</c> is neither this Service's own nor trusted.
    /// Says nothing about whether it may proceed — that depends on what it would do, which
    /// the middleware and the filter each judge.
    /// </summary>
    public static bool IsFromUntrustedPage(HttpContext context)
    {
        if (!IsLoopback(context))
        {
            return false;
        }

        return IsFromAnotherSite(context.Request)
            ? !IsTrustedPage(context)
            : !SendsFetchMetadata(context.Request) && HasForeignOrigin(context);
    }

    /// <summary>
    /// Anything that can change state without a preflight having asked first: every method
    /// but the three that are safe by definition, and a WebSocket handshake.
    /// </summary>
    internal static bool IsStateChanging(HttpContext context)
    {
        var request = context.Request;

        return !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) ||
                 HttpMethods.IsOptions(request.Method)) ||
               IsWebSocketHandshake(context);
    }

    /// <summary>A load into a frame, embed or object of another page.</summary>
    internal static bool IsFrameLoad(HttpRequest request) =>
        FrameDestinations.Contains(request.Headers[SecFetchDestHeader].ToString(), StringComparer.OrdinalIgnoreCase);

    private static bool IsFromAnotherSite(HttpRequest request)
    {
        var site = request.Headers[SecFetchSiteHeader].ToString();
        return site.Equals("same-site", StringComparison.OrdinalIgnoreCase) ||
               site.Equals("cross-site", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SendsFetchMetadata(HttpRequest request) =>
        !string.IsNullOrWhiteSpace(request.Headers[SecFetchSiteHeader].ToString());

    /// <summary>
    /// An HTTP/1.1 upgrade to <c>websocket</c>, or HTTP/2's extended <c>CONNECT</c> for one.
    /// </summary>
    /// <remarks>
    /// Read from the request itself, not from <c>HttpContext.WebSockets</c>: the WebSocket
    /// feature is only installed further down, inside the hub's own branch. Any mention of
    /// <c>websocket</c> counts, whether or not the server would go on to upgrade it —
    /// treating a near-handshake as one only ever refuses more.
    /// </remarks>
    internal static bool IsWebSocketHandshake(HttpContext context)
    {
        var request = context.Request;

        if (request.Headers[SecFetchModeHeader].ToString().Equals("websocket", StringComparison.OrdinalIgnoreCase) ||
            request.Headers.Upgrade.Any(v => v?.Contains("websocket", StringComparison.OrdinalIgnoreCase) == true))
        {
            return true;
        }

        return context.Features.Get<IHttpExtendedConnectFeature>() is {IsExtendedConnect: true} connect &&
               string.Equals(connect.Protocol, "websocket", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The same test <see cref="RemoteAccessMiddleware"/> applies, including treating a
    /// missing peer address — an in-process call — as local.
    /// </summary>
    private static bool IsLoopback(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp == null)
        {
            return true;
        }

        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        return IPAddress.IsLoopback(remoteIp);
    }

    /// <summary>
    /// Whether the request names, in <c>Origin</c>, a page that is neither the one it was
    /// sent from its own origin nor a trusted one. False when it names none: that is a
    /// native caller, or a navigation.
    /// </summary>
    private static bool HasForeignOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();

        return !string.IsNullOrEmpty(origin) && !IsOwnOrigin(context.Request, origin) &&
               !IsTrustedOrigin(context, origin);
    }

    /// <summary>
    /// Whether <paramref name="origin"/> is exactly the origin this request was sent to — its
    /// scheme and <c>Host</c>. Covers the window on <c>localhost</c> and on <c>127.0.0.1</c>
    /// alike, whichever port it is on, and the same UI reached under any other name.
    /// </summary>
    /// <remarks>
    /// Any name counts, not only a loopback one: a hosts-file alias, or a reverse proxy or
    /// tunnel on this machine (nginx, Caddy, frp), shows this very UI under its own name, and
    /// over plain HTTP its browser sends no fetch metadata — so a loopback-names-only rule
    /// would refuse every save and every hub connection it makes. What this guard tells
    /// apart is a page on a <em>different</em> origin, which is what a relay page and every
    /// other site are. It does not defend against DNS rebinding, where a hostile name is its
    /// own origin by construction; that needs a <c>Host</c> allow-list covering reads as
    /// well as writes, and is out of this guard's scope.
    /// </remarks>
    private static bool IsOwnOrigin(HttpRequest request, string origin)
    {
        if (!request.Host.HasValue || !Uri.TryCreate(origin, UriKind.Absolute, out var page) ||
            !string.IsNullOrEmpty(page.UserInfo) || page.PathAndQuery != "/" || page.Fragment.Length > 0)
        {
            return false;
        }

        var host = request.Host.Host;
        var port = request.Host.Port ?? (request.IsHttps ? 443 : 80);

        return page.Scheme.Equals(request.Scheme, StringComparison.OrdinalIgnoreCase) &&
               page.Host.Equals(host, StringComparison.OrdinalIgnoreCase) &&
               page.Port == port;
    }

    /// <remarks>
    /// A missing or <c>null</c> origin is not trusted: it cannot be shown to be on the
    /// list. Browsers name the page behind every state-changing request; the requests that
    /// go without are navigations and image-style loads — exactly the GETs the filter
    /// refuses when their action runs here — and pages that asked to stay anonymous.
    /// </remarks>
    private static bool IsTrustedPage(HttpContext context) => IsTrustedOrigin(context, PageOrigin(context.Request));

    private static bool IsTrustedOrigin(HttpContext context, string? origin)
    {
        if (string.IsNullOrEmpty(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (BrowserExtensionSchemes.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        // Read per request: the endpoints are replaced with the addresses Kestrel actually
        // bound once the host has started.
        var apiEndpoints = context.RequestServices?.GetService<AppContext>()?.ApiEndpoints;
        var origins = context.RequestServices?.GetService<ServiceCorsOrigins>() ?? ServiceCorsOrigins.ForThisBuild;
        return origins.Build(apiEndpoints).IsOriginAllowed(origin);
    }

    /// <summary>
    /// The page a request came from: its <c>Origin</c>, or — for a frame's navigation,
    /// which never names one — the origin of its <c>Referer</c>.
    /// </summary>
    private static string? PageOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) || !IsFrameLoad(request))
        {
            return origin;
        }

        return Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)
            ? referer.GetLeftPart(UriPartial.Authority)
            : null;
    }
}
