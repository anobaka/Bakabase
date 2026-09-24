using System.Net;
using System.Text;
using System.Text.Json;

namespace Bakabase.Remoting.Components.Forwarding;

/// <summary>
/// Holds back the relay's own calls to the server — the context probe, the reads a play or
/// open action needs, the history it records — until the address is confirmed to answer as
/// that server, exactly as the forwarder holds back the browser's.
/// </summary>
/// <remarks>
/// <para>
/// In front of the signing handler, so nothing is signed for an address that is not the
/// server's. A refusal is a response rather than an exception, shaped like the forwarder's
/// (503 with <c>X-Bakabase-Client</c>): every caller already reads a non-success status as
/// "the server did not answer", which is the truth as far as it is concerned.
/// </para>
/// <para>
/// A request that fails to connect at all raises suspicion, as a forwarding failure does:
/// the server went away, and whoever answers next at its address has to be asked who it is.
/// One refused by the connect step (<see cref="UpstreamConnections"/>) does not: that was
/// the check itself answering.
/// </para>
/// </remarks>
public sealed class UpstreamIdentityHandler(UpstreamIdentity identity) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var check = await identity.EnsureAsync(cancellationToken);

        if (check is not {IsConfirmed: true} || !SameAuthority(request.RequestUri, check.Address))
        {
            return Refuse(request, check);
        }

        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException e) when (Refusal(e) is { } refused)
        {
            // The connect step found the address answering as someone else a moment after
            // this request was let through: refused like any other, with nothing sent.
            return Refuse(request, refused.Check);
        }
        catch (HttpRequestException)
        {
            identity.Suspect();
            throw;
        }
    }

    /// <summary>The connect step's refusal behind <paramref name="e"/>, if that is what it was.</summary>
    internal static UpstreamIdentityRefusedException? Refusal(Exception? e)
    {
        for (; e != null; e = e.InnerException)
        {
            if (e is UpstreamIdentityRefusedException refused)
            {
                return refused;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the request goes to the address that was confirmed. An address changed under
    /// a request already built is refused; the next one is built, and checked, afresh.
    /// </summary>
    private static bool SameAuthority(Uri? requestUri, string confirmed) =>
        requestUri != null && Uri.TryCreate(confirmed, UriKind.Absolute, out var address) &&
        Uri.Compare(requestUri, address, UriComponents.SchemeAndServer, UriFormat.Unescaped,
            StringComparison.OrdinalIgnoreCase) == 0;

    private static HttpResponseMessage Refuse(HttpRequestMessage request, UpstreamIdentityCheck? check)
    {
        var failure = check == null
            ? ClientForwardingFailure.NotConnected
            : check.IsMismatch
                ? ClientForwardingFailure.WrongServer
                : ClientForwardingFailure.ServerUnreachable;

        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            RequestMessage = request,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                code = (int) HttpStatusCode.ServiceUnavailable,
                message = check?.Describe(null) ?? "This computer no longer manages this server."
            }), Encoding.UTF8, "application/json")
        };

        response.Headers.TryAddWithoutValidation(UpstreamForwarder.FailureHeader, failure.ToString());

        return response;
    }
}
