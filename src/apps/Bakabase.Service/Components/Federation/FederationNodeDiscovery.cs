using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;

namespace Bakabase.Service.Components.Federation;

public sealed class FederationNodeDiscovery(IServerDiscovery discovery, INodeIdentityProvider identity)
    : INodePeerDiscovery
{
    public async Task<IReadOnlyList<NodeDiscoveryCandidate>> DiscoverAsync(CancellationToken ct)
    {
        var local = await identity.GetAsync(ct);
        var servers = await discovery.DiscoverAsync(TimeSpan.FromSeconds(2), ct);
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
            { Timeout = TimeSpan.FromSeconds(2), MaxResponseContentBufferSize = 64 * 1024 };
        using var concurrency = new SemaphoreSlim(4);
        var candidates = await Task.WhenAll(servers.Take(64).Select(async server =>
        {
            await concurrency.WaitAsync(ct);
            try
            {
                // Discovery is only a hint; use the new protocol's actual identity, not the old ServerId.
                using var response = await client.GetAsync(server.BaseAddress.TrimEnd('/') + "/federation/v1/info", ct);
                if (!response.IsSuccessStatusCode) return null;
                var info = JsonSerializer.Deserialize<NodeInfo>(await response.Content.ReadAsStringAsync(ct), FederationJson.Options);
                // A node sharing only its definitions answers info too (§7.4), and says so; a hint, like the rest.
                return info is { ProtocolVersion: 1 } && IsPlausibleIdentity(info) && info.NodeId != local.NodeId
                    ? new NodeDiscoveryCandidate(info.NodeId, info.Name, server.BaseAddress,
                        ServerSelfDescriptionWords.KindOf(info.Kind) ?? server.Kind,
                        ServerSelfDescriptionWords.PlatformOf(info.Platform) ?? server.Platform,
                        info.DataSyncContractVersion is >= 0 and <= 1_000_000 ? info.DataSyncContractVersion : null,
                        info.SharesDefinitions)
                    : null;
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                return null;
            }
            finally { concurrency.Release(); }
        }));
        return candidates.Where(x => x != null).Cast<NodeDiscoveryCandidate>().DistinctBy(x => x.NodeId).ToArray();
    }

    /// <summary>
    /// Anyone on the network can answer info, and what it says is listed to the Devices page and to data sync's peer
    /// candidates, which paired devices read too (§12). So an identity a session would refuse (the checks of
    /// <c>PeerSessionFactory.ValidateInfo</c>) is not listed at all.
    /// </summary>
    private static bool IsPlausibleIdentity(NodeInfo info) => NodeRequestSignature.IsIdentifier(info.NodeId) &&
        !string.IsNullOrWhiteSpace(info.Name) && info.Name.Length <= 128 && !info.Name.Any(char.IsControl);
}
