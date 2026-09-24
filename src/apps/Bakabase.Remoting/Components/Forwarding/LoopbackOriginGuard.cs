using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Bakabase.Remoting.Components.Forwarding;

/// <summary>Why the forwarding layer refused a request before looking at it — or what it does instead.</summary>
public enum LoopbackGuardVerdict
{
    Allowed = 0,

    /// <summary>
    /// The <c>Host</c> header names something other than this listener. Either a
    /// misconfiguration or DNS rebinding — a page on <c>evil.com</c> whose domain
    /// resolves to 127.0.0.1, which is how a website reaches a local port it was never
    /// meant to.
    /// </summary>
    ForeignHost = 1,

    /// <summary>
    /// A state-changing request from another origin. The browser will not let that page
    /// read the answer, but the request already happened, which is enough for a delete.
    /// Or a WebSocket handshake from another origin, whatever its method: that page
    /// <em>would</em> read the answer — a WebSocket is a channel no CORS check looks at —
    /// and a handshake carries no fetch metadata to judge it by instead.
    /// </summary>
    ForeignOrigin = 2,

    /// <summary>
    /// The browser itself says a page on another site made this request
    /// (<c>Sec-Fetch-Site: same-site</c> or <c>cross-site</c>), and it is not a window
    /// arriving with a switch ticket. Reads included: several of the actions a relay runs
    /// on this machine are GETs — play, open a folder, empty the recycle bin — so an
    /// <c>&lt;img&gt;</c> on any page would otherwise be enough to start one.
    /// </summary>
    ForeignSite = 3,

    /// <summary>
    /// Admitted, but not served as asked: the address carries a switch ticket. The ticket
    /// is spent, and the caller answers with a page that sends the window on to
    /// <see cref="LoopbackGuardDecision.ContinueTo"/> — the same address without it.
    /// </summary>
    DropTicket = 4
}

/// <summary>What the guard reads off a request. Every field is one the browser sets itself.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Host">The <c>Host</c> header (or HTTP/2 <c>:authority</c>).</param>
/// <param name="Origin">The <c>Origin</c> header, when there is one.</param>
/// <param name="FetchSite"><c>Sec-Fetch-Site</c>, when the engine sends fetch metadata.</param>
/// <param name="FetchMode"><c>Sec-Fetch-Mode</c>.</param>
/// <param name="FetchDest"><c>Sec-Fetch-Dest</c>.</param>
/// <param name="Target">
/// The request target as it arrived — path and query, still escaped. Only read for a
/// switch ticket, and only ever handed back with the ticket taken out.
/// </param>
/// <param name="WebSocket">
/// Whether this is a WebSocket handshake: an HTTP/1.1 <c>Upgrade: websocket</c>, or HTTP/2's
/// extended <c>CONNECT</c> for one. A <c>Sec-Fetch-Mode</c> of <c>websocket</c> counts too,
/// whatever this says.
/// </param>
public readonly record struct LoopbackGuardRequest(
    string Method,
    string? Host,
    string? Origin = null,
    string? FetchSite = null,
    string? FetchMode = null,
    string? FetchDest = null,
    string? Target = null,
    bool WebSocket = false)
{
    public static LoopbackGuardRequest From(HttpRequest request)
    {
        var headers = request.Headers;

        return new LoopbackGuardRequest(request.Method, request.Host.Value, headers.Origin.ToString(),
            headers["Sec-Fetch-Site"].ToString(), headers["Sec-Fetch-Mode"].ToString(),
            headers["Sec-Fetch-Dest"].ToString(), RawTarget(request), IsWebSocketHandshake(request));
    }

    /// <remarks>
    /// Read from the request rather than from <c>HttpContext.WebSockets</c>, which only knows
    /// once the upgrade has been offered. Any mention of <c>websocket</c> in <c>Upgrade</c>
    /// counts, whether or not the server would go on to upgrade it: treating a
    /// near-handshake as one only ever refuses more.
    /// </remarks>
    private static bool IsWebSocketHandshake(HttpRequest request)
    {
        if (request.Headers.Upgrade.Any(v => v?.Contains("websocket", StringComparison.OrdinalIgnoreCase) == true))
        {
            return true;
        }

        return request.HttpContext.Features.Get<IHttpExtendedConnectFeature>() is {IsExtendedConnect: true} connect &&
               string.Equals(connect.Protocol, "websocket", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The target exactly as the browser sent it, so the address a window is sent on to
    /// differs from the one it asked for by the ticket and nothing else — not by a
    /// decode-and-re-encode round trip that turns an escaped slash into a real one.
    /// </summary>
    private static string RawTarget(HttpRequest request)
    {
        var raw = request.HttpContext.Features.Get<IHttpRequestFeature>()?.RawTarget;

        // Origin-form is what a browser sends. Anything else — absolute-form, the
        // asterisk of an OPTIONS — is rebuilt from the parsed parts instead.
        return !string.IsNullOrEmpty(raw) && raw[0] == '/'
            ? raw
            : request.PathBase.Add(request.Path).ToUriComponent() + request.QueryString.ToUriComponent();
    }
}

/// <summary>The guard's answer about one request.</summary>
/// <param name="Verdict">What to do with the request.</param>
/// <param name="ContinueTo">
/// For <see cref="LoopbackGuardVerdict.DropTicket"/>: the target to send the window on to,
/// path and query in origin-form, with every other query parameter exactly as it arrived.
/// </param>
/// <param name="CarriedTicket">
/// Whether the address carried a switch ticket at all — so a refusal can say so in the
/// log without the log ever holding the ticket.
/// </param>
public readonly record struct LoopbackGuardDecision(
    LoopbackGuardVerdict Verdict,
    string? ContinueTo = null,
    bool CarriedTicket = false);

/// <summary>
/// The only thing standing between a local port and every page the user has open.
/// </summary>
/// <remarks>
/// <para>
/// The forwarding layer listens on loopback with no authentication of its own: anything
/// that reaches it gets the user's whole library, signed with the device key. Loopback
/// is not the protection people assume — any website can make the browser send requests
/// to <c>127.0.0.1</c>, and with a DNS record pointing at it, can do so under its own
/// hostname.
/// </para>
/// <para>
/// Three checks, because they stop different things. <c>Host</c> catches rebinding: the
/// browser sends the hostname from the URL bar, and an attacker's page carries the
/// attacker's hostname no matter where the DNS points. <c>Sec-Fetch-Site</c> catches
/// ordinary cross-site requests, which arrive with a truthful <c>Host</c> — and it
/// catches reads too, which matters because several of the actions this relay runs on
/// the user's machine are GETs. Note that every other port on this machine is
/// <em>same-site</em>, not cross-site: sites ignore ports, so this device's own window,
/// another relay and whatever else runs on 127.0.0.1 all count, and all are refused.
/// <c>Origin</c> covers state-changing requests from engines that send no fetch metadata.
/// </para>
/// <para>
/// And <c>Origin</c> alone decides a WebSocket handshake, which is a GET, so neither of the
/// other checks sees it: Chromium — and so WebView2 — sends no fetch metadata on one, only
/// <c>Host</c>, <c>Upgrade</c> and <c>Origin</c>, and no CORS check ever applies to what
/// the socket then carries. Left to the GET rules, any page in any browser on this machine
/// could open this relay's hub and read what the server pushes to its own UI, signed with
/// the device key. A handshake naming any page but this relay's own is refused; one naming
/// none is not a browser's, and is judged as before.
/// </para>
/// <para>
/// A request with no fetch metadata at all is judged as before. That is what a local
/// player pulling a stream, a script the user ran, and an older embedded engine all
/// look like, and none of them is the vector — a browser that sends the header on one
/// request sends it on all of them, WebSocket handshakes apart.
/// </para>
/// <para>
/// The one cross-site request that has to succeed is the window itself switching
/// servers: a top-level navigation from this device's own origin, or from another relay,
/// to this one. It carries a <see cref="RelayNavigationTokens">ticket</see>, which is
/// spent here, and the window is sent on to the same address without it — by a page
/// served from this origin rather than by a redirect. A redirect would not work: the
/// browser computes <c>Sec-Fetch-Site</c> across the whole redirect chain, relative to
/// the page that started the navigation, so the request after a 302 is exactly as
/// cross-site as the one before it, and carries no ticket. A navigation started by a
/// page on this origin is same-origin, and so is everything that page loads.
/// </para>
/// </remarks>
public sealed class LoopbackOriginGuard(int port, RelayNavigationTokens? tickets = null)
{
    /// <summary>
    /// Names that resolve to this machine and cannot be produced by rebinding — an
    /// attacker's page always carries its own hostname, whatever address it resolves to.
    /// </summary>
    private static readonly string[] LoopbackNames = ["127.0.0.1", "localhost", "[::1]"];

    /// <summary>The rules for a request that carries no fetch metadata and no ticket.</summary>
    public LoopbackGuardVerdict Evaluate(string? host, string? origin, string method) =>
        Evaluate(new LoopbackGuardRequest(method, host, origin)).Verdict;

    /// <summary>
    /// Judges <paramref name="request"/>. Not a pure function: a ticket it admits a
    /// window with is spent, and so is one on an address it would have served anyway.
    /// </summary>
    public LoopbackGuardDecision Evaluate(LoopbackGuardRequest request)
    {
        if (!IsOurs(request.Host))
        {
            // Before the ticket is even looked at: a rebound page must not be able to
            // spend one, let alone use it.
            return new LoopbackGuardDecision(LoopbackGuardVerdict.ForeignHost);
        }

        var target = TicketedTarget.Read(request.Target);

        if (IsWebSocketHandshake(request) && !IsAbsentOrOurs(request.Origin))
        {
            // Before fetch metadata, which Chromium never sends on a handshake, and before
            // any ticket: a ticket only ever admits a window's navigation, and is left for
            // the one it was minted for.
            return new LoopbackGuardDecision(LoopbackGuardVerdict.ForeignOrigin, CarriedTicket: target.Tickets.Count > 0);
        }

        if (IsFromAnotherSite(request.FetchSite))
        {
            // Exactly one ticket, on exactly the request a window switching servers makes,
            // and only then is the ticket touched — a subresource or a form post that
            // happens to carry one leaves it for the navigation it was minted for.
            if (target.Tickets.Count == 1 &&
                IsTopLevelNavigation(request) &&
                tickets?.TryConsume(target.Tickets[0], port) == true)
            {
                return new LoopbackGuardDecision(LoopbackGuardVerdict.DropTicket, target.WithoutTickets, true);
            }

            return new LoopbackGuardDecision(LoopbackGuardVerdict.ForeignSite, CarriedTicket: target.Tickets.Count > 0);
        }

        // Same-origin, a navigation the user started themselves, or no fetch metadata.
        if (!IsSafeMethod(request.Method) && !IsAbsentOrOurs(request.Origin))
        {
            return new LoopbackGuardDecision(LoopbackGuardVerdict.ForeignOrigin, CarriedTicket: target.Tickets.Count > 0);
        }

        if (target.Tickets.Count > 0 && HttpMethods.IsGet(request.Method))
        {
            // Admitted without the ticket's help — the shell pointing its own window here,
            // or an engine that sends no fetch metadata. The ticket is spent anyway and
            // taken out of the address, so it never reaches the server, the page or the
            // window's history.
            foreach (var ticket in target.Tickets)
            {
                tickets?.TryConsume(ticket, port);
            }

            return new LoopbackGuardDecision(LoopbackGuardVerdict.DropTicket, target.WithoutTickets, true);
        }

        return new LoopbackGuardDecision(LoopbackGuardVerdict.Allowed, CarriedTicket: target.Tickets.Count > 0);
    }

    /// <summary>
    /// Whether the browser says another site's page made this request.
    /// </summary>
    /// <remarks>
    /// Anything present that is not <c>same-origin</c> or <c>none</c> counts: a browser
    /// only ever sends the four values, so a fifth — or two headers joined into one — is
    /// something pretending to be one, and fails closed.
    /// </remarks>
    private static bool IsFromAnotherSite(string? fetchSite)
    {
        if (string.IsNullOrWhiteSpace(fetchSite))
        {
            return false;
        }

        var value = fetchSite.Trim();

        return !value.Equals("same-origin", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWebSocketHandshake(LoopbackGuardRequest request) =>
        request.WebSocket ||
        string.Equals(request.FetchMode?.Trim(), "websocket", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A window loading a document — not a frame, not a subresource, not a fetch, and not
    /// anything that writes. What a switch is, and all a ticket may ever buy.
    /// </summary>
    private static bool IsTopLevelNavigation(LoopbackGuardRequest request) =>
        HttpMethods.IsGet(request.Method) &&
        string.Equals(request.FetchMode?.Trim(), "navigate", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(request.FetchDest?.Trim(), "document", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="origin"/> names this relay's own page, in any of the forms the
    /// <c>Host</c> check accepts for this port.
    /// </summary>
    public bool IsOwnOrigin(string? origin) => !string.IsNullOrEmpty(origin) && IsAbsentOrOurs(origin);

    private bool IsAbsentOrOurs(string? origin)
    {
        // Absent is not the same as foreign. Browsers always send Origin on a
        // state-changing request; the callers that omit it are not browsers — a local
        // player fetching a stream, a script — and are not the vector this guards.
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var parsed) && IsOurs(parsed.Authority);
    }

    private bool IsOurs(string? authority)
    {
        if (string.IsNullOrEmpty(authority))
        {
            // HTTP/1.1 requires a Host header and HTTP/2 requires :authority. Something
            // that sends neither is not the embedded browser.
            return false;
        }

        var separator = authority.LastIndexOf(':');

        // IPv6 literals are bracketed, so a colon inside the brackets is not the port
        // separator.
        var closingBracket = authority.LastIndexOf(']');
        var name = separator > closingBracket && separator >= 0 ? authority[..separator] : authority;
        var portText = separator > closingBracket && separator >= 0 ? authority[(separator + 1)..] : null;

        if (!LoopbackNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // A port is mandatory: the listener never runs on 80 or 443, so an authority
        // without one did not mean this process.
        return int.TryParse(portText, out var parsedPort) && parsedPort == port;
    }

    private static bool IsSafeMethod(string method) =>
        string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase);

    /// <summary>A request target split into its switch tickets and everything else.</summary>
    /// <remarks>
    /// Works on the escaped text rather than a parsed query, so what is handed back is the
    /// original minus the ticket's own <c>name=value</c> pair — every other parameter
    /// keeps its order, its escaping and its duplicates. Names are compared decoded and
    /// ignoring case, the way ASP.NET reads a query: anything that could read a ticket
    /// out of the address is also taken out of it.
    /// </remarks>
    private readonly record struct TicketedTarget(string WithoutTickets, IReadOnlyList<string> Tickets)
    {
        public static TicketedTarget Read(string? target)
        {
            if (string.IsNullOrEmpty(target))
            {
                return new TicketedTarget("/", []);
            }

            var queryStart = target.IndexOf('?');

            if (queryStart < 0)
            {
                return new TicketedTarget(target, []);
            }

            var kept = new List<string>();
            var found = new List<string>();

            foreach (var pair in target[(queryStart + 1)..].Split('&'))
            {
                var equals = pair.IndexOf('=');
                var name = Decode(equals < 0 ? pair : pair[..equals]);

                if (string.Equals(name, RelayNavigationTokens.QueryName, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(equals < 0 ? "" : Decode(pair[(equals + 1)..]));
                }
                else
                {
                    kept.Add(pair);
                }
            }

            if (found.Count == 0)
            {
                return new TicketedTarget(target, found);
            }

            var path = target[..queryStart];
            var query = string.Join('&', kept);

            return new TicketedTarget(query.Length == 0 ? path : $"{path}?{query}", found);
        }

        private static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));
    }
}
