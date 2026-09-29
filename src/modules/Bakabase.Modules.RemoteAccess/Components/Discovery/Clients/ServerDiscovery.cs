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
/// <para>
/// Once per side, though: an answer from this machine and one from another carrying the same
/// id are two installations — a data directory copied to another computer takes its identity
/// along — and this machine's own answer must never hide the copy's. See <see cref="Keep"/>.
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
    /// one this network cannot route to.
    /// </remarks>
    public static IReadOnlyList<DiscoveredServer> Merge(IReadOnlyList<DiscoveredServer> probed,
        IReadOnlyList<DiscoveredServer> browsed)
    {
        var found = new Dictionary<(string, bool), DiscoveredServer>();

        foreach (var server in probed.Concat(browsed))
        {
            Keep(found, server);
        }

        return found.Values
            .OrderByDescending(s => s.IsThisMachine)
            .ThenBy(s => s.ServerName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Keeps the first answer an install gave from each side: from this machine, or from
    /// another one.
    /// </summary>
    /// <remarks>
    /// One install answering on several interfaces, or on both channels, is one entry. The
    /// sides are never merged: an install is its identity, and the one place a copy of this
    /// install on another computer shows is its answer under this install's own id — which
    /// this machine's own answer, loopback or not, would otherwise hide.
    /// </remarks>
    internal static void Keep(Dictionary<(string, bool), DiscoveredServer> found, DiscoveredServer server) =>
        found.TryAdd((server.ServerId, server.IsThisMachine), server);
}
