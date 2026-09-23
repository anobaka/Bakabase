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
/// Loopback and this port only. A server on the same machine is a legitimate and expected
/// target — running the all-in-one and the client side by side is a case this build is
/// built for — and it listens on a different port; this listener is bound to 127.0.0.1
/// alone, so no other address reaches it.
/// </para>
/// </remarks>
public sealed class ClientSelfAddress(int port)
{
    public int Port { get; } = port;

    /// <summary>
    /// Whether <paramref name="address"/> is this process's own listener.
    /// </summary>
    /// <remarks>
    /// <see cref="Uri.IsLoopback"/> rather than a list of names: it already covers
    /// <c>localhost</c>, <c>[::1]</c> and the whole of 127.0.0.0/8, all of which reach a
    /// listener bound to loopback. <see cref="Forwarding.LoopbackOriginGuard"/> spells the
    /// names out instead because it reads a raw <c>Host</c> header, where a hostname an
    /// attacker chose must never be resolved — here the address is one the user typed
    /// into this client's own page, and resolving it is the point.
    /// </remarks>
    public bool Matches(Uri address) => address.IsLoopback && address.Port == Port;
}
