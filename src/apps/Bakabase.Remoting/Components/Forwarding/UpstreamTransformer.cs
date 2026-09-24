using Bakabase.Remoting.Components.Connection;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Yarp.ReverseProxy.Forwarder;

namespace Bakabase.Remoting.Components.Forwarding;

/// <summary>
/// Turns a request the local browser made into one the server will accept.
/// </summary>
/// <remarks>
/// <para>
/// Signing happens here rather than in a message handler because the signature covers
/// the path and query, and a message handler runs after YARP has already decided them.
/// Signing earlier would sign a URL that no longer matches what goes on the wire.
/// </para>
/// <para>
/// Which makes the order below load-bearing, and it is the opposite of what it looks
/// like: YARP fills <see cref="HttpRequestMessage.RequestUri"/> in <em>after</em> the
/// transformer returns, and only if it is still null. So a transformer that signs
/// without building the address first is handed a request with no URI at all — and the
/// signer, correctly, refuses to sign one. That threw inside YARP, which reported it as
/// a failure to create the request, which the forwarder reported as "the server is not
/// answering": a client that had just paired successfully answered every single request
/// with a 503 naming a server that was fine.
/// </para>
/// <para>
/// Anything the caller sent as <c>Authorization</c> is dropped first. A page in the
/// user's browser can put whatever it likes in that header, and forwarding it would let
/// it choose which device the server thinks is calling.
/// </para>
/// <para>
/// <c>Cookie</c> goes too. Cookies are scoped to a host, not a port, so every listener on
/// 127.0.0.1 — this device's own server, every other relay, anything else the user runs
/// there — shares one jar, and whatever the browser attaches belongs to none of them in
/// particular. The server decides who is calling from the signature alone; the most a
/// forwarded cookie could do is hand one loopback origin's state to another machine.
/// </para>
/// <para>
/// A request the loopback guard admitted as coming from this relay's own page — the
/// server's own UI, shown here — tells the server so: its <c>Origin</c> becomes the
/// server's own. To a server on another machine that changes nothing; no gate there reads
/// <c>Origin</c>. A server on this machine, though — another install, a container on the
/// host network, a tunnel — takes the relay for a loopback caller, and judges a
/// WebSocket handshake by its <c>Origin</c>: named as the relay's port, its own UI's hub
/// would be refused as another site's page, and fall back to a slower transport. For the
/// same reason such a server's <c>/federation/local</c> interface, which demands its own
/// origin, answers its UI here as it answers its own window — no more than the device key
/// every forwarded request carries already allows. A page on any other origin never gets its
/// <c>Origin</c> rewritten; the guard refuses its handshakes and writes before they get here.
/// </para>
/// </remarks>
public sealed class UpstreamTransformer(IClientCredentialProvider credentials, ServerClock clock) : HttpTransformer
{
    private static readonly object FromRelayPageKey = new();

    /// <summary>
    /// Marks <paramref name="context"/>'s request as one the loopback guard admitted from this
    /// relay's own page, whose <c>Origin</c> is forwarded as the server's own.
    /// </summary>
    public static void MarkFromRelayPage(HttpContext context) => context.Items[FromRelayPageKey] = true;

    public override async ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest,
        string destinationPrefix, CancellationToken cancellationToken)
    {
        await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);

        if (httpContext.Items.ContainsKey(FromRelayPageKey) &&
            Uri.TryCreate(destinationPrefix, UriKind.Absolute, out var destination))
        {
            proxyRequest.Headers.Remove(HeaderNames.Origin);
            proxyRequest.Headers.TryAddWithoutValidation(HeaderNames.Origin,
                destination.GetLeftPart(UriPartial.Authority));
        }

        // The address YARP would have built itself, built here instead so there is
        // something to sign. Composed with YARP's own helper rather than by hand: it is
        // what decides how the prefix, the path and the query are joined and escaped,
        // and a second opinion on that is a signature over a URL the server never sees.
        proxyRequest.RequestUri ??= RequestUtilities.MakeDestinationAddress(
            destinationPrefix, httpContext.Request.Path, httpContext.Request.QueryString);

        // The upstream decides who we are from the signature alone; a Host of
        // 127.0.0.1 would just be a lie it has no use for.
        proxyRequest.Headers.Host = null;
        proxyRequest.Headers.Authorization = null;
        proxyRequest.Headers.Remove(HeaderNames.Cookie);

        var current = credentials.Current;
        if (current == null)
        {
            // Not paired. The request still goes: a server that does not require
            // pairing serves anonymous callers, and the pairing handshake itself has
            // nothing to sign with yet.
            return;
        }

        byte[] key;
        try
        {
            key = RemoteRequestSignature.FromBase64Url(current.Key);
        }
        catch (FormatException)
        {
            // A stored key that will not decode cannot sign. Going out anonymous
            // produces a refusal the user can act on rather than an unexplained crash
            // inside the proxy pipeline.
            return;
        }

        await UpstreamRequestSigner.SignAsync(proxyRequest, current.DeviceId, key, clock.NowUnixSeconds,
            ct: cancellationToken);
    }
}
