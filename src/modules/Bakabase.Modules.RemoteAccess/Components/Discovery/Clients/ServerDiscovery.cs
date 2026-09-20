namespace Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;

/// <summary>
/// Both ways of finding a server, run together.
/// </summary>
/// <remarks>
/// <para>
/// They fail on different networks, which is the whole reason there are two. A subnet
/// broadcast is dropped by plenty of access points and by most enterprise wireless;
/// multicast is dropped by some home routers and by nearly every VPN. Running one and
/// falling back to the other would cost the user the timeout of the first before they
/// saw anything, so both go out at once and whatever answers, answers.
/// </para>
/// <para>
/// A server that answers on both channels appears once: they carry the same facts, and
/// the id in them is the same id a paired device is paired to.
/// </para>
/// </remarks>
public sealed class ServerDiscovery(UdpProbeClient probe, MdnsBrowser mdns) : IServerDiscovery
{
    public async Task<IReadOnlyList<DiscoveredServer>> DiscoverAsync(TimeSpan timeout,
        CancellationToken ct = default)
    {
        var probed = probe.DiscoverAsync(timeout, ct);
        var browsed = mdns.DiscoverAsync(timeout, ct);

        await Task.WhenAll(probed, browsed);

        return Merge(await probed, await browsed);
    }

    /// <summary>
    /// One entry per server.
    /// </summary>
    /// <remarks>
    /// The probe's answers are preferred where both channels found the same server:
    /// its address is the one that a datagram actually came back over, while mDNS
    /// reports an address the server published, which on a multi-homed machine can be
    /// one this network cannot route to. A loopback answer still wins over either —
    /// only it can say "this is your own computer".
    /// </remarks>
    public static IReadOnlyList<DiscoveredServer> Merge(IReadOnlyList<DiscoveredServer> probed,
        IReadOnlyList<DiscoveredServer> browsed)
    {
        var found = new Dictionary<string, DiscoveredServer>(StringComparer.Ordinal);

        foreach (var server in probed.Concat(browsed))
        {
            if (!found.TryGetValue(server.ServerId, out var existing) ||
                (server.IsThisMachine && !existing.IsThisMachine))
            {
                found[server.ServerId] = server;
            }
        }

        return found.Values
            .OrderByDescending(s => s.IsThisMachine)
            .ThenBy(s => s.ServerName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
