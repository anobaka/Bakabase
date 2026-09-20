using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;

namespace Bakabase.Modules.Federation.Transport;

public interface INodeTransport
{
    Task<HttpResponseMessage> SendAsync(string nodeId, HttpMethod method, string relativePath, object? body = null,
        CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> SendAsync(PeerSessionSnapshot session, HttpMethod method, string relativePath,
        object? body = null, CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? headers = null);
}

public sealed class NodeTransport(FederationStateStore store, IPeerSessionFactory sessions,
    FederationHttpClient http, TimeProvider timeProvider, GrantLeaseRegistry leases) : INodeTransport
{
    private static readonly HashSet<string> ForwardedHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Range", "If-Range", "If-None-Match", "If-Modified-Since", "Accept" };

    public async Task<HttpResponseMessage> SendAsync(string nodeId, HttpMethod method, string relativePath,
        object? body = null, CancellationToken cancellationToken = default) =>
        await SendAsync(await sessions.GetAsync(nodeId, cancellationToken), method, relativePath, body, cancellationToken);

    public async Task<HttpResponseMessage> SendAsync(PeerSessionSnapshot session, HttpMethod method,
        string relativePath, object? body = null, CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        // A query pins its target, but cannot keep using a credential the user removed
        // after opening that query. Never replace its target with a newer active peer.
        var state = await store.ReadAsync(cancellationToken);
        if (!state.Peers.TryGetValue(session.NodeId, out var peer) || !peer.Enabled ||
            !state.OutboundGrants.TryGetValue(session.NodeId, out var current) || current != session.Credentials ||
            peer.Address != session.BaseAddress)
            throw new FederationAccessException("NodeSessionChanged", 409, "The node connection changed. Start a new query or playback session.");
        using var request = FederationHttpClient.CreateRequest(session.BaseAddress, method, relativePath, body);
        if (!request.RequestUri!.AbsolutePath.StartsWith("/federation/v1/export/", StringComparison.Ordinal))
            throw new FederationAccessException("InvalidNodeRoute", 403, "Node credentials only authorize explicit export routes.");
        if (headers != null)
            foreach (var (name, value) in headers)
            {
                if (!ForwardedHeaders.Contains(name))
                    throw new FederationAccessException("InvalidNodeHeader", 400, "The requested header is not part of the media protocol.");
                request.Headers.TryAddWithoutValidation(name, value);
            }
        await FederationHttpClient.SignAsync(request, current, timeProvider.GetUtcNow() + session.ClockOffset,
            cancellationToken);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            leases.GetCancellationToken(GrantLeaseRegistry.OutboundKey(session.GrantId)));
        try
        {
            var response = await http.SendAsync(request, linked.Token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                response.Dispose();
                throw new FederationAccessException("NodeRedirectRefused", 502, "The node redirected a signed request. Verify its address.");
            }
            // ResponseHeadersRead completes before the body is consumed. Keep the
            // direct grant lease alive until the caller disposes the response.
            response.Content = new NodeResponseContent(response.Content, linked);
            return response;
        }
        catch { linked.Dispose(); throw; }
    }
}
