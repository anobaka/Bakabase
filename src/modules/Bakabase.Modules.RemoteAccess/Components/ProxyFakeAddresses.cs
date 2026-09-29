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
/// it is proof enough, and the one thing worth telling the user is that the proxy is in the
/// way. Nothing is sent there: it would go to the proxy, and from there possibly to a server
/// on the internet. No other range is treated this way.
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
    /// What <paramref name="host"/> resolved to, in the resolver's order, without the fake
    /// addresses — or, when the resolver's first answer is one, a
    /// <see cref="ProxyFakeAddressException"/>: the proxy answered the name before anything
    /// real did, which is how it answers every name it takes over.
    /// </summary>
    public static IReadOnlyList<IPAddress> Screen(string host, IReadOnlyList<IPAddress> resolved)
    {
        if (resolved.Count > 0 && Contains(resolved[0]))
        {
            throw new ProxyFakeAddressException(host, resolved[0]);
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
/// Thrown instead of connecting when the address is a proxy's own (see
/// <see cref="ProxyFakeAddresses"/>): a name a proxy on this computer took over, or such an
/// address typed as it is. Nothing was sent.
/// </summary>
/// <remarks>
/// A <see cref="SocketException"/> (host unreachable), so whatever reads socket failures as
/// "could not connect" still does; the callers that tell the user why look for this one.
/// </remarks>
public sealed class ProxyFakeAddressException(string host, IPAddress address)
    : SocketException((int) SocketError.HostUnreachable,
        IPAddress.TryParse(host, out _)
            ? $"{address} is an address a proxy on this computer hands out (198.18.0.0/15), not a device's; " +
              "nothing was sent to it"
            : $"{host} resolved to {address}, an address a proxy on this computer hands out (198.18.0.0/15); " +
              "nothing was sent to it")
{
    /// <summary>The name or address that was to be connected to.</summary>
    public string Host { get; } = host;

    /// <summary>The proxy's address it led to.</summary>
    public IPAddress Address { get; } = address;
}
