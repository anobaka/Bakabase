using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.RemoteAccess.Components;
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
                // Resolving the name and connecting, together: a Windows computer name can take
                // seconds to resolve over LLMNR/NetBIOS. Kept under the 8 s every exchange runs
                // within (PublicAsync, the handshake, a query's steps), so an unreachable peer
                // is still reported as such.
                ConnectTimeout = TimeSpan.FromSeconds(5),
                // Connecting alone, once resolved: a name's IPv4 and IPv6 addresses are raced
                // rather than tried one after another, so one round trip is what is left to wait
                // for. An address — what most peers and discovered devices are stored as — needs
                // no lookup, so a peer that is switched off there is reported after these 2 s,
                // not after the lookup's allowance too, on every verification that finds it off.
                ConnectCallback = (context, ct) =>
                    DualStackConnector.Default.ConnectAsync(context.DnsEndPoint, TimeSpan.FromSeconds(2), ct),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });
        services.TryAddSingleton<PeerSessionFactory>();
        services.TryAddSingleton<IPeerSessionFactory>(sp => sp.GetRequiredService<PeerSessionFactory>());
        services.TryAddSingleton<INodeTransport, NodeTransport>();
        return services;
    }
}
