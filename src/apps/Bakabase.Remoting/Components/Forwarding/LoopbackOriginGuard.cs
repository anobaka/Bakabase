namespace Bakabase.Remoting.Components.Forwarding;

/// <summary>Why the forwarding layer refused a request before looking at it.</summary>
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
    /// </summary>
    ForeignOrigin = 2
}

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
/// Two checks, because they stop different things. <c>Host</c> catches rebinding: the
/// browser sends the hostname from the URL bar, and an attacker's page carries the
/// attacker's hostname no matter where the DNS points. <c>Origin</c> catches ordinary
/// cross-site requests, which do arrive with a truthful <c>Host</c> — the browser will
/// not hand the page the response, but a POST that deleted something did not need one.
/// </para>
/// </remarks>
public sealed class LoopbackOriginGuard(int port)
{
    /// <summary>
    /// Names that resolve to this machine and cannot be produced by rebinding — an
    /// attacker's page always carries its own hostname, whatever address it resolves to.
    /// </summary>
    private static readonly string[] LoopbackNames = ["127.0.0.1", "localhost", "[::1]"];

    public LoopbackGuardVerdict Evaluate(string? host, string? origin, string method)
    {
        if (!IsOurs(host))
        {
            return LoopbackGuardVerdict.ForeignHost;
        }

        // GET and HEAD change nothing, and a page that could read their answers would
        // have had to pass the check above first.
        if (IsSafeMethod(method))
        {
            return LoopbackGuardVerdict.Allowed;
        }

        // Absent is not the same as foreign. Browsers always send Origin on a
        // state-changing request; the callers that omit it are not browsers — a local
        // player fetching a stream, a script — and are not the vector this guards.
        if (string.IsNullOrEmpty(origin))
        {
            return LoopbackGuardVerdict.Allowed;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var parsed) && IsOurs(parsed.Authority)
            ? LoopbackGuardVerdict.Allowed
            : LoopbackGuardVerdict.ForeignOrigin;
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
}
