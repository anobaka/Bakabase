using System.Net;
using System.Net.Sockets;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.RemoteAccess.Components.Discovery;

/// <summary>
/// The dumb half of discovery: a client yells
/// <see cref="RemoteAccessProtocol.ProbeRequest"/> at
/// <see cref="RemoteAccessProtocol.ProbePort"/> (broadcast or unicast) and gets
/// the server descriptor back as JSON. Exists because mDNS dies on networks
/// that filter multicast, and doubles as a connectivity diagnostic.
/// </summary>
public sealed class UdpProbeResponder : IDisposable
{
    private readonly RemoteAccessServerDescriptor _descriptor;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();

    private Socket? _socket;

    public UdpProbeResponder(RemoteAccessServerDescriptor descriptor, ILogger logger)
    {
        _descriptor = descriptor;
        _logger = logger;
    }

    /// <summary>False when the port could not be bound; not fatal, mDNS still works.</summary>
    public bool Start()
    {
        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.EnableBroadcast = true;
            socket.Bind(new IPEndPoint(IPAddress.Any, RemoteAccessProtocol.ProbePort));

            _socket = socket;
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "UDP probe responder could not bind port {Port}; mDNS remains the only channel",
                RemoteAccessProtocol.ProbePort);
            _socket?.Dispose();
            _socket = null;
            return false;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[1024];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

        while (!ct.IsCancellationRequested && _socket is { } socket)
        {
            try
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remote, ct);

                if (!DiscoveryProtocol.IsProbeRequest(buffer.AsSpan(0, result.ReceivedBytes)))
                {
                    continue;
                }

                var response = DiscoveryProtocol.BuildProbeResponse(_descriptor);
                await socket.SendToAsync(response, SocketFlags.None, result.RemoteEndPoint, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "UDP probe loop error; continuing");

                // A socket stuck in an error state would otherwise spin this loop hot.
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _socket?.Dispose();
        _socket = null;
        _cts.Dispose();
    }
}
