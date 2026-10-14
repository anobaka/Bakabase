using Bakabase.Modules.RemoteAccess.Components.Pairing;

namespace Bakabase.Remoting.Components.Connection;

/// <summary>
/// Signs every request going upstream, so nothing that talks to the server has to
/// remember to.
/// </summary>
/// <remarks>
/// A handler rather than a helper each caller invokes: the forwarding layer, the pairing
/// flow and the connection poller all reach the same server, and a signature that gets
/// added at one call site and forgotten at another is a bug that shows up as an
/// intermittent 401. Putting it in the pipeline makes forgetting impossible.
/// <para>
/// With no credentials it does nothing and the request goes out anonymous — which is
/// exactly right for a server that does not require pairing, and for the pairing
/// handshake itself, which by definition has nothing to sign with yet.
/// </para>
/// </remarks>
public sealed class DeviceSigningHandler(IClientCredentialProvider credentials, ServerClock clock)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var current = credentials.Current;

        if (current != null)
        {
            byte[] key;
            try
            {
                key = RemoteRequestSignature.FromBase64Url(current.Key);
            }
            catch (FormatException)
            {
                // A stored key that will not decode cannot sign anything. Going out
                // anonymous produces a refusal the user can act on ("pair again"),
                // where throwing here would surface as an unexplained client crash.
                return await base.SendAsync(request, cancellationToken);
            }

            await UpstreamRequestSigner.SignAsync(request, current.DeviceId, key, clock.NowUnixSeconds,
                ct: cancellationToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>The credentials in force, or null when this client has not paired.</summary>
public interface IClientCredentialProvider
{
    ClientCredentials? Current { get; }
}

/// <param name="Key">base64url of the HMAC key.</param>
public sealed record ClientCredentials(string DeviceId, string Key);
