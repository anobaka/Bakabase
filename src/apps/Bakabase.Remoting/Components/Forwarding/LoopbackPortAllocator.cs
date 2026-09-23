using System.Net;
using System.Net.Sockets;

namespace Bakabase.Remoting.Components.Forwarding;

/// <summary>
/// Picks the loopback port the forwarding layer listens on.
/// </summary>
/// <remarks>
/// <para>
/// Stability is the whole point, which is why this does not just ask the OS for a free
/// port. The browser keys localStorage, IndexedDB and every cached asset to the origin,
/// and the origin includes the port — so a client that moved from 34600 to 34601 between
/// launches would look to the user like it had forgotten their settings. It tries the
/// same port first every time and only walks forward when something else has it.
/// </para>
/// <para>
/// Two things keep it still. <see cref="PreferredPort"/> sits clear of the all-in-one's
/// own window, so running both flavours on one machine does not decide the port by launch
/// order; <see cref="LoopbackPortMemory"/> then carries forward whatever was actually
/// bound, so an unrelated squatter moves the client once rather than every other launch.
/// </para>
/// </remarks>
public static class LoopbackPortAllocator
{
    /// <summary>
    /// Where the all-in-one's own listening window begins.
    /// </summary>
    /// <remarks>
    /// Not read by anything here — it is the number <see cref="PreferredPort"/> has to
    /// stay clear of, named so the reason is visible at the place that depends on it.
    /// The server walks up from here taking <c>AutoListeningPortCount</c> free ports
    /// (three by default, and the user can raise it in settings), binding them on
    /// <c>0.0.0.0</c>, which occupies the loopback address too.
    /// </remarks>
    public const int AllInOneWindowStart = 34567;

    /// <summary>
    /// Clear of the all-in-one's window, which is the whole point: a user is expected to
    /// run both flavours on one machine, and 34568 — the previous value — is the server's
    /// <i>second</i> port. Whichever started first won it, so the client bound 34568 or
    /// 34570 depending on launch order, and the browser treats those as two different
    /// origins. The gap below is far wider than any sane <c>AutoListeningPortCount</c>.
    /// </summary>
    public const int PreferredPort = 34600;

    /// <summary>How far to walk before giving up. Far enough to clear a crowd of stale listeners.</summary>
    public const int MaxAttempts = 32;

    /// <summary>
    /// The first free port at or after <paramref name="preferred"/>.
    /// </summary>
    /// <param name="isFree">
    /// Injected so the walk is testable without binding real sockets; defaults to
    /// actually trying to bind, which is the only answer that is not a guess.
    /// </param>
    public static int Allocate(int preferred = PreferredPort, Func<int, bool>? isFree = null)
    {
        isFree ??= CanBind;

        for (var offset = 0; offset < MaxAttempts; offset++)
        {
            var port = preferred + offset;

            if (port <= IPEndPoint.MaxPort && isFree(port))
            {
                return port;
            }
        }

        throw new IOException(
            $"No free loopback port between {preferred} and {preferred + MaxAttempts - 1}.");
    }

    private static bool CanBind(int port)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
