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
/// <para>
/// A full mDNS querier — one asking from port 5353, as every browser does — is answered by
/// multicast, at most once a second. A one-shot querier — one asking from a port of its own, as
/// another Bakabase resolving a <c>.local</c> name does (<c>MdnsHostResolver</c>) — is answered
/// straight back, as RFC 6762 §6.7 requires: it may not hear the group at all, and a single
/// multicast answer lost on Wi-Fi would otherwise leave it with nothing for a second or more.
/// Those answers go only to senders on this machine's links and are limited on their own, apart
/// from the multicast ones.
/// </para>
/// </summary>
public sealed class MdnsResponder : IDisposable
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    private const int MdnsPort = 5353;

    /// <summary>mDNS forbids multicasting the same records more often than this.</summary>
    private static readonly TimeSpan MinResponseInterval = TimeSpan.FromSeconds(1);

    /// <summary>How many answers go straight back to one-shot queriers in a second, all of them together.</summary>
    public const int MaxUnicastAnswersPerSecond = 20;

    private readonly MdnsAdvertisement _advertisement;
    private readonly Func<IReadOnlyList<IPAddress>> _addressProvider;
    private readonly Func<IPAddress, bool> _isOnLink;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _gate = new();

    private Socket? _socket;
    private long? _lastMulticastAt;
    private long _unicastWindowStart;
    private int _unicastInWindow;

    /// <param name="advertisement">What this server advertises.</param>
    /// <param name="addressProvider">This machine's IPv4 addresses to publish, read at every answer.</param>
    /// <param name="logger">Where failures go, quietly.</param>
    /// <param name="isOnLink">
    /// Whether a sender is on one of this machine's links, which an answer straight back needs;
    /// defaults to <see cref="LocalNetworkAddresses.IsOnLink"/>.
    /// </param>
    public MdnsResponder(MdnsAdvertisement advertisement, Func<IReadOnlyList<IPAddress>> addressProvider,
        ILogger logger, Func<IPAddress, bool>? isOnLink = null)
    {
        _advertisement = advertisement;
        _addressProvider = addressProvider;
        _logger = logger;
        _isOnLink = isOnLink ?? LocalNetworkAddresses.IsOnLink;
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

    /// <summary>
    /// What to send for one datagram <paramref name="from"/> received: the answer and where it
    /// goes, or null when nothing is to be sent — not a query about this server's records, a
    /// multicast answer sent less than a second ago, a one-shot querier off this machine's links
    /// or past <see cref="MaxUnicastAnswersPerSecond"/>, or no address to publish.
    /// </summary>
    /// <param name="data">The datagram.</param>
    /// <param name="from">Where it came from.</param>
    /// <param name="nowMilliseconds">A monotonic clock's reading, in milliseconds.</param>
    public MdnsReply? Respond(ReadOnlySpan<byte> data, IPEndPoint from, long nowMilliseconds)
    {
        if (!MdnsMessage.TryParseQuery(data, out var id, out var questions))
        {
            return null;
        }

        var asked = questions.Select(q => (q.Name, q.Type)).ToList();

        if (!_advertisement.Answers(asked))
        {
            return null;
        }

        if (from.Port != MdnsPort)
        {
            // A one-shot querier (RFC 6762 §6.7): answered straight back, repeating its id and
            // questions — never by multicast, which it may not hear.
            if (!_isOnLink(from.Address) || !TakeUnicastTurn(nowMilliseconds))
            {
                return null;
            }

            var addresses = _addressProvider();

            if (addresses.Count == 0)
            {
                return null;
            }

            var (answers, additional) = MdnsAdvertisement.Select(_advertisement.BuildRecords(addresses), asked);

            return answers.Count == 0
                ? null
                : new MdnsReply(MdnsMessage.BuildLegacyUnicastResponse(id, questions, answers, additional), from);
        }

        lock (_gate)
        {
            if (_lastMulticastAt is { } last && nowMilliseconds - last < MinResponseInterval.TotalMilliseconds)
            {
                return null;
            }

            _lastMulticastAt = nowMilliseconds;
        }

        return BuildAnnouncement(goodbye: false) is { } packet
            ? new MdnsReply(packet, new IPEndPoint(MulticastAddress, MdnsPort))
            : null;
    }

    private bool TakeUnicastTurn(long now)
    {
        lock (_gate)
        {
            if (now - _unicastWindowStart >= 1000 || now < _unicastWindowStart)
            {
                _unicastWindowStart = now;
                _unicastInWindow = 0;
            }

            return ++_unicastInWindow <= MaxUnicastAnswersPerSecond;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[9000];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

        while (!ct.IsCancellationRequested && _socket is { } socket)
        {
            try
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remote, ct);

                if (result.RemoteEndPoint is not IPEndPoint from ||
                    Respond(buffer.AsSpan(0, result.ReceivedBytes), from, Environment.TickCount64) is not { } reply)
                {
                    continue;
                }

                socket.SendTo(reply.Packet, reply.To);
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

    /// <summary>The whole record set as one multicast response, or null with no address to publish.</summary>
    private byte[]? BuildAnnouncement(bool goodbye)
    {
        var addresses = _addressProvider();

        return addresses.Count == 0
            ? null
            : MdnsMessage.BuildResponse(_advertisement.BuildRecords(addresses, goodbye));
    }

    private void Send(bool goodbye)
    {
        try
        {
            if (_socket is not { } socket || BuildAnnouncement(goodbye) is not { } packet)
            {
                return;
            }

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

/// <summary>What <see cref="MdnsResponder"/> sends in answer to a query, and where.</summary>
/// <param name="Packet">The response.</param>
/// <param name="To">The multicast group, or the one-shot querier it goes straight back to.</param>
public sealed record MdnsReply(byte[] Packet, IPEndPoint To);
