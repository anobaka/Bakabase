namespace Bakabase.Remoting.Components.Connection;

/// <summary>
/// Whether an address is this client's own forwarding listener.
/// </summary>
/// <remarks>
/// <para>
/// It is the likeliest wrong address a user can type, because it is the one in front of
/// them: the connect page is served from it, and its host and port are sitting in the
/// window's own URL bar.
/// </para>
/// <para>
/// Nothing downstream catches it, and both ways it lands are bad. With no server attached
/// the listener answers the connect page, so the handshake reads HTML and reports "not a
/// Bakabase server" — true of the bytes and useless as a diagnosis, since the one thing
/// the user needs told is that they gave it this client's address. With a server already
/// attached it is worse: the forwarder relays <c>/remote-access/server-info</c> upstream,
/// so the handshake <i>succeeds</i> and hands back the real server's identity. Pairing
/// against that would point this client at itself, and every request afterwards would
/// loop back through the forwarder until the process ran out of something.
/// </para>
/// <para>
/// Loopback and this process's own ports only. A server on the same machine is a
/// legitimate and expected target — running the all-in-one and the client side by side is
/// a case this build is built for — and it listens on a different port; these listeners
/// are bound to 127.0.0.1 alone, so no other address reaches them.
/// </para>
/// <para>
/// The thin client has exactly one such port. The desktop app has several and they come
/// and go: its own server's, and one per relay it runs for a server it manages. Any of
/// them handed back to the pairing flow would pair the app with itself, so the set is
/// read at the moment of the check rather than fixed at construction.
/// </para>
/// </remarks>
public sealed class ClientSelfAddress
{
    private readonly Func<IEnumerable<int>> _ports;

    /// <summary>A process with a single listener of its own — the thin client.</summary>
    public ClientSelfAddress(int port)
    {
        Port = port;
        _ports = () => [port];
    }

    /// <summary>
    /// A process whose own listeners change while it runs.
    /// </summary>
    /// <param name="ports">Read on every check; must be cheap and must not throw.</param>
    public ClientSelfAddress(Func<IEnumerable<int>> ports)
    {
        _ports = ports;
        Port = ports().FirstOrDefault();
    }

    /// <summary>The first of this process's ports at construction. The thin client's only one.</summary>
    public int Port { get; }

    /// <summary>Every port this process listens on right now.</summary>
    public IReadOnlyCollection<int> Ports => _ports().Where(p => p > 0).Distinct().ToArray();

    /// <summary>
    /// Whether <paramref name="address"/> is one of this process's own listeners.
    /// </summary>
    /// <remarks>
    /// <see cref="Uri.IsLoopback"/> rather than a list of names: it already covers
    /// <c>localhost</c>, <c>[::1]</c> and the whole of 127.0.0.0/8, all of which reach a
    /// listener bound to loopback. <see cref="Forwarding.LoopbackOriginGuard"/> spells the
    /// names out instead because it reads a raw <c>Host</c> header, where a hostname an
    /// attacker chose must never be resolved — here the address is one the user typed
    /// into this client's own page, and resolving it is the point.
    /// </remarks>
    public bool Matches(Uri address) => address.IsLoopback && _ports().Contains(address.Port);
}
