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
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;

namespace Bakabase.Service.Components.Federation;

/// <param name="connector">How to connect to what answered; a registered one is a test's network.</param>
public sealed class FederationNodeDiscovery(IServerDiscovery discovery, INodeIdentityProvider identity,
    DualStackConnector? connector = null)
    : INodePeerDiscovery
{
    public async Task<IReadOnlyList<NodeDiscoveryCandidate>> DiscoverAsync(CancellationToken ct)
    {
        var local = await identity.GetAsync(ct);
        var servers = await discovery.DiscoverAsync(TimeSpan.FromSeconds(2), ct);
        using var client = new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseProxy = false,
                ConnectCallback = (connector ?? DualStackConnector.Default).ConnectCallback
            })
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
                // Never this node, answering from one of this machine's own addresses. Another
                // machine under this node's id is listed: a copy of this data directory, which
                // connecting to then says (SameIdentity) — the one way the user learns of it.
                return info is { ProtocolVersion: 1 } && (info.NodeId != local.NodeId || !server.IsThisMachine)
                    ? new NodeDiscoveryCandidate(info.NodeId, info.Name, server.BaseAddress,
                        ServerSelfDescriptionWords.KindOf(info.Kind) ?? server.Kind,
                        ServerSelfDescriptionWords.PlatformOf(info.Platform) ?? server.Platform)
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
}
