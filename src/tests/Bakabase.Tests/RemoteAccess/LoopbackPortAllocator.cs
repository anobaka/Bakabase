using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// A free loopback port for a test host to bind, at or after a preferred one.
/// </summary>
/// <remarks>
/// Walks up from <c>preferred</c> rather than asking the OS for an ephemeral port, so each
/// test class keeps to its own neighbourhood and a failure names a port that means
/// something. The desktop app's relays choose their ports themselves
/// (<c>RemoteConsoleManager</c>); this is only for the hosts the tests start.
/// </remarks>
internal static class LoopbackPortAllocator
{
    /// <summary>How far to walk before giving up. Far enough to clear a crowd of stale listeners.</summary>
    public const int MaxAttempts = 32;

    /// <summary>The first free port at or after <paramref name="preferred"/>.</summary>
    public static int Allocate(int preferred)
    {
        for (var offset = 0; offset < MaxAttempts; offset++)
        {
            var port = preferred + offset;

            if (port <= IPEndPoint.MaxPort && CanBind(port))
            {
                return port;
            }
        }

        throw new IOException($"No free loopback port between {preferred} and {preferred + MaxAttempts - 1}.");
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
