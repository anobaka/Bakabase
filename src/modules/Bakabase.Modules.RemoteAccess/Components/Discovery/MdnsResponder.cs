using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.RemoteAccess.Components.Discovery;

/// <summary>
/// Owns the mDNS socket: joins the multicast group, answers queries matching
/// this server's advertisement, and announces on start / says goodbye on stop.
/// <para>
/// Coexists with the OS's own mDNS responder via address reuse — this is why
/// the advertisement uses its own hostname instead of the machine's. Everything
/// here fails soft: discovery is a convenience, and a socket error must never
/// take the application down with it.
/// </para>
/// </summary>
public sealed class MdnsResponder : IDisposable
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    private const int MdnsPort = 5353;

    /// <summary>mDNS forbids multicasting the same records more often than this.</summary>
    private static readonly TimeSpan MinResponseInterval = TimeSpan.FromSeconds(1);

    private readonly MdnsAdvertisement _advertisement;
    private readonly Func<IReadOnlyList<IPAddress>> _addressProvider;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();

    private Socket? _socket;
    private long _lastResponseTicks;

    public MdnsResponder(MdnsAdvertisement advertisement, Func<IReadOnlyList<IPAddress>> addressProvider,
        ILogger logger)
    {
        _advertisement = advertisement;
        _addressProvider = addressProvider;
        _logger = logger;
    }

    /// <summary>False when the socket could not be set up (port in use, no multicast).</summary>
    public bool Start()
    {
        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));

            // Join on every usable interface; the default join alone misses
            // queries on multi-homed machines.
            var joinedAny = false;
            foreach (var (address, _) in LocalNetworkAddresses.EnumerateIPv4(_logger))
            {
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                        new MulticastOption(MulticastAddress, address));
                    joinedAny = true;
                }
                catch (SocketException)
                {
                    // An interface that refuses multicast (VPN adapters commonly do)
                    // just doesn't get discovery.
                }
            }

            if (!joinedAny)
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                    new MulticastOption(MulticastAddress));
            }

            _socket = socket;
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "mDNS responder could not start; discovery falls back to the UDP probe");
            _socket?.Dispose();
            _socket = null;
            return false;
        }
    }

    /// <summary>The unsolicited "I'm here" mDNS suggests on startup: twice, a second apart.</summary>
    public async Task AnnounceAsync(CancellationToken ct)
    {
        Send(goodbye: false);
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        Send(goodbye: false);
    }

    public void SayGoodbye() => Send(goodbye: true);

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[9000];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

        while (!ct.IsCancellationRequested && _socket is { } socket)
        {
            try
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remote, ct);

                if (!MdnsMessage.TryParseQuestions(buffer.AsSpan(0, result.ReceivedBytes), out var questions) ||
                    !_advertisement.Answers(questions))
                {
                    continue;
                }

                var now = Environment.TickCount64;
                if (now - Interlocked.Read(ref _lastResponseTicks) < MinResponseInterval.TotalMilliseconds)
                {
                    continue;
                }

                Interlocked.Exchange(ref _lastResponseTicks, now);
                Send(goodbye: false);
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
                _logger.LogDebug(e, "mDNS receive loop error; continuing");

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

    private void Send(bool goodbye)
    {
        try
        {
            var addresses = _addressProvider();
            if (addresses.Count == 0 || _socket is not { } socket)
            {
                return;
            }

            var packet = MdnsMessage.BuildResponse(_advertisement.BuildRecords(addresses, goodbye));
            socket.SendTo(packet, new IPEndPoint(MulticastAddress, MdnsPort));
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "mDNS send failed");
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
