using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Modules.Federation.Peers;

public static class FederationPeerServiceCollectionExtensions
{
    public static IServiceCollection AddFederationPeers(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<FederationStateStore>();
        services.TryAddSingleton<INodeIdentityProvider, NodeIdentityProvider>();
        services.TryAddSingleton<GrantLeaseRegistry>();
        services.TryAddSingleton<NodeNonceCache>();
        services.TryAddSingleton<NodeGrantService>();
        services.TryAddSingleton<INodeGrantService>(sp => sp.GetRequiredService<NodeGrantService>());
        services.TryAddSingleton<NodeGrantAuthenticator>();
        services.TryAddSingleton<NodePairingRateLimiter>();
        services.TryAddSingleton<FederationPeerService>();
        services.TryAddSingleton<NodePairingClient>();
        services.AddHttpClient<FederationHttpClient>(http => http.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Peers are addressed directly, like the desktop app's relays: a system or
                // environment proxy would otherwise receive LAN traffic and signed requests.
                UseProxy = false,
                AllowAutoRedirect = false,
                UseCookies = false,
                ConnectTimeout = TimeSpan.FromSeconds(2),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });
        services.TryAddSingleton<PeerSessionFactory>();
        services.TryAddSingleton<IPeerSessionFactory>(sp => sp.GetRequiredService<PeerSessionFactory>());
        services.TryAddSingleton<INodeTransport, NodeTransport>();
        return services;
    }
}
