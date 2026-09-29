using System.Net;
using System.Net.Sockets;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <summary>
/// The addresses a proxy on this computer hands out for names it has taken over: 198.18.0.0/15.
/// </summary>
/// <remarks>
/// <para>
/// Clash and Mihomo in fake-IP or TUN mode, Surge, sing-box and Shadowrocket answer every name
/// the system asks them about with an address of their own from this range, and hand whatever
/// then connects there to the proxy. A LAN name such as <c>nas.local</c> is lost that way: the
/// proxy cannot resolve it either, and the connection fails as if nothing were there — while
/// the device's IP address, which the proxy leaves alone, connects at once.
/// </para>
/// <para>
/// The range is reserved for benchmarking (RFC 2544) and never a real LAN address, so meeting
/// it is proof enough that a proxy took the name over. What that means depends on the name
/// (<see cref="ProxyResolvesItself"/>):
/// </para>
/// <list type="bullet">
/// <item>
/// A name only the LAN knows — a <c>.local</c> name, a computer's single-label name — or such an
/// address typed as it is: the proxy has no way to the device, so nothing is sent there (it
/// would go to the proxy, and from there possibly to a server on the internet), and the one
/// thing worth telling the user is that the proxy is in the way.
/// </item>
/// <item>
/// Any other name — a domain, a DDNS name, a reverse proxy's: the proxy resolves it with its own
/// DNS and connects on the user's behalf, as it does for every other program, so its address is
/// dialled as the resolver gave it. Only when that fails is the proxy named as what stood in
/// the way.
/// </item>
/// </list>
/// <para>
/// No other range is treated this way.
/// </para>
/// </remarks>
public static class ProxyFakeAddresses
{
    /// <summary>Whether <paramref name="address"/> is in 198.18.0.0/15, IPv4-mapped or not.</summary>
    public static bool Contains(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();

        return bytes[0] == 198 && (bytes[1] & 0xFE) == 18;
    }

    /// <summary>
    /// Whether a proxy that took <paramref name="host"/> over can still reach it on the user's
    /// behalf: a domain name, which the proxy resolves with its own DNS. Not an IP address, a
    /// <c>.local</c> name (only the LAN's mDNS responders answer it) or a single label (a
    /// computer's name, which only the LAN's resolvers know).
    /// </summary>
    public static bool ProxyResolvesItself(string host)
    {
        var name = host.Trim('[', ']').TrimEnd('.');

        return name.Length > 0 && !IPAddress.TryParse(name, out _) && name.Contains('.') &&
               !name.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc cref="Screen(string, IReadOnlyList{IPAddress}, out IPAddress?)"/>
    public static IReadOnlyList<IPAddress> Screen(string host, IReadOnlyList<IPAddress> resolved) =>
        Screen(host, resolved, out _);

    /// <summary>Where to connect for <paramref name="host"/>, given what it resolved to.</summary>
    /// <remarks>
    /// <para>
    /// When the resolver's first answer is a proxy's — how a proxy answers every name it takes
    /// over — the name is the proxy's now: a <see cref="ProxyFakeAddressException"/>, nothing
    /// dialled, where the proxy cannot reach it (<see cref="ProxyResolvesItself"/>); the
    /// resolver's answer as it is, the proxy's address included, where it can.
    /// </para>
    /// <para>
    /// Otherwise the resolver's answer in its order, without a proxy's addresses behind the real
    /// ones: they would only lead into the proxy.
    /// </para>
    /// </remarks>
    /// <param name="host">The name resolved, or an address typed as it is.</param>
    /// <param name="resolved">What it resolved to, in the resolver's order.</param>
    /// <param name="throughProxy">
    /// The proxy's address, when what is returned goes through the proxy: a failure to connect
    /// there is then reported as the proxy's (<see cref="ProxyFakeAddressException"/>). Null otherwise.
    /// </param>
    public static IReadOnlyList<IPAddress> Screen(string host, IReadOnlyList<IPAddress> resolved,
        out IPAddress? throughProxy)
    {
        throughProxy = null;

        if (resolved.Count > 0 && Contains(resolved[0]))
        {
            if (!ProxyResolvesItself(host))
            {
                throw new ProxyFakeAddressException(host, resolved[0]);
            }

            throughProxy = resolved[0];
            return resolved;
        }

        return resolved.Any(Contains) ? resolved.Where(a => !Contains(a)).ToList() : resolved;
    }

    /// <summary>
    /// The <see cref="ProxyFakeAddressException"/> behind <paramref name="exception"/>, however
    /// deep: an HTTP client wraps what its connect step threw.
    /// </summary>
    public static ProxyFakeAddressException? Find(Exception? exception)
    {
        for (var depth = 0; exception != null && depth < 16; depth++)
        {
            switch (exception)
            {
                case ProxyFakeAddressException found:
                    return found;
                case AggregateException aggregate:
                    foreach (var inner in aggregate.InnerExceptions)
                    {
                        if (Find(inner) is { } nested)
                        {
                            return nested;
                        }
                    }

                    return null;
            }

            exception = exception.InnerException;
        }

        return null;
    }
}

/// <summary>
/// Thrown when the address is a proxy's own (see <see cref="ProxyFakeAddresses"/>): instead of
/// connecting, for a name only the LAN knows that a proxy on this computer took over, or such an
/// address typed as it is — nothing was sent there; and once connecting through the proxy
/// failed, for a domain it took over (<see cref="ProxyResolvesItself"/>).
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="SocketException"/> (host unreachable), so whatever reads socket failures as
/// "could not connect" still does; the callers that tell the user why look for this one.
/// </para>
/// <para>
/// Also thrown once the addresses the LAN gave for a <c>.local</c> name all failed where the
/// system resolver, which is what a proxy answers for, gave a proxy's address first
/// (<see cref="LanHostResolution.ProxyAddress"/>): with a proxy taking over the name, the proxy is
/// still what stands in the way. <see cref="ConnectError"/> is then how the last of those
/// attempts failed — a <see cref="SocketException"/> takes no inner exception.
/// </para>
/// </remarks>
public sealed class ProxyFakeAddressException(string host, IPAddress address, Exception? connectError = null)
    : SocketException((int) SocketError.HostUnreachable, Describe(host, address, connectError))
{
    /// <summary>The name or address that was to be connected to.</summary>
    public string Host { get; } = host;

    /// <summary>The proxy's address it led to.</summary>
    public IPAddress Address { get; } = address;

    /// <summary>
    /// How connecting failed, when it was tried: through the proxy for a domain, at the addresses
    /// the LAN gave instead for a <c>.local</c> name.
    /// </summary>
    public Exception? ConnectError { get; } = connectError;

    /// <summary>
    /// Whether the proxy could have reached <see cref="Host"/> itself — a domain
    /// (<see cref="ProxyFakeAddresses.ProxyResolvesItself"/>), where the fix is that domain set to
    /// bypass the proxy — rather than a name only the LAN knows, or an address.
    /// </summary>
    public bool ProxyResolvesItself => ProxyFakeAddresses.ProxyResolvesItself(Host);

    private static string Describe(string host, IPAddress address, Exception? connectError)
    {
        var tried = connectError == null ? "" : $": {connectError.Message}";

        if (IPAddress.TryParse(host.Trim('[', ']'), out _))
        {
            return connectError == null
                ? $"{address} is an address a proxy on this computer hands out (198.18.0.0/15), not a device's; " +
                  "nothing was sent to it"
                : $"{address}, an address a proxy on this computer hands out (198.18.0.0/15), could not be " +
                  $"reached through the proxy{tried}";
        }

        if (ProxyFakeAddresses.ProxyResolvesItself(host))
        {
            return $"{host} resolved to {address}, an address a proxy on this computer hands out (198.18.0.0/15), " +
                   $"and could not be reached through the proxy{tried}";
        }

        return $"{host} resolved to {address}, an address a proxy on this computer hands out (198.18.0.0/15); " +
               "nothing was sent to it" +
               (connectError == null ? "" : $". The addresses the LAN gave for it did not answer either{tried}");
    }
}
