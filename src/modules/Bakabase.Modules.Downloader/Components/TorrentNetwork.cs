using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Bakabase.Modules.Downloader.Models;
using MonoTorrent;
using MonoTorrent.Connections;
using MonoTorrent.Connections.Peer;
using MonoTorrent.Connections.Tracker;
using MonoTorrent.Trackers;
using ReusableTasks;

namespace Bakabase.Modules.Downloader.Components;

/// <summary>Every BitTorrent network channel shares this explicit, immutable routing policy.</summary>
internal sealed class TorrentNetwork
{
    private readonly TorrentNetworkOptions _options;
    public bool UsesProxy => _options.ProxyUri != null;

    public TorrentNetwork(TorrentNetworkOptions options)
    {
        if (options.ProxyUri is { } proxy && (!proxy.IsAbsoluteUri || proxy.Scheme != "socks5" ||
            string.IsNullOrWhiteSpace(proxy.Host) || proxy.Port is < 1 or > 65535 ||
            !string.IsNullOrEmpty(proxy.UserInfo)))
            throw new ArgumentException("BT downloads require a SOCKS5 proxy with a host and port; configure credentials separately.");
        if (options.Credentials is { } credentials && (Encoding.UTF8.GetByteCount(credentials.UserName) is < 1 or > 255 ||
            Encoding.UTF8.GetByteCount(credentials.Password) > 255))
            throw new ArgumentException("SOCKS5 credentials must use a username of 1–255 bytes and a password of at most 255 bytes.");
        _options = options with
        {
            Credentials = options.Credentials is { } c ? new NetworkCredential(c.UserName, c.Password, c.Domain) : null
        };
    }

    public HttpClient CreateHttpClient(AddressFamily family = AddressFamily.Unspecified)
    {
        var handler = new SocketsHttpHandler
        {
            // Set this even when using our SOCKS connector. It rules out the OS proxy and
            // environment variables for direct transfers, redirects and web seed requests.
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(30),
            ConnectCallback = async (context, ct) => new NetworkStream(
                await ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, family, ct), true)
        };
        return new HttpClient(handler) {Timeout = TimeSpan.FromSeconds(30)};
    }

    public Factories CreateFactories()
    {
        var factories = Factories.Default.WithHttpClientCreator(CreateHttpClient);
        // MonoTorrent's default tracker delegates close over their original Factories instance;
        // overriding only WithHttpClientCreator would leave HTTP trackers on the system proxy.
        Factories.TrackerCreator tracker = uri => new Tracker(
            new HttpTrackerConnection(uri, CreateHttpClient, AddressFamily.InterNetwork),
            new HttpTrackerConnection(uri, CreateHttpClient, AddressFamily.InterNetworkV6));
        factories = factories.WithTrackerCreator("http", tracker).WithTrackerCreator("https", tracker);
        if (UsesProxy)
        {
            var connector = new SocksConnector(this);
            factories = factories.WithSocketConnectorCreator(() => connector)
                .WithPeerConnectionCreator("ipv4", uri => new SocketPeerConnection(uri, connector))
                .WithPeerConnectionCreator("ipv6", uri => new SocketPeerConnection(uri, connector))
                // Passing null restores the default creator; return null instead to disable it.
                .WithTrackerCreator("udp", _ => null!);
        }
        return factories;
    }

    internal async Task<Socket> ConnectAsync(string host, int port, AddressFamily family, CancellationToken ct)
    {
        var socket = UsesProxy || family == AddressFamily.Unspecified
            ? new Socket(SocketType.Stream, ProtocolType.Tcp)
            : new Socket(family, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.NoDelay = true;
            if (_options.ProxyUri is not { } proxy)
            {
                await socket.ConnectAsync(host, port, ct);
                return socket;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await socket.ConnectAsync(proxy.DnsSafeHost, proxy.Port, timeout.Token);
            using var stream = new NetworkStream(socket, false);
            var method = _options.Credentials == null ? (byte)0 : (byte)2;
            await stream.WriteAsync(new byte[] {5, 1, method}, timeout.Token);
            var response = new byte[2];
            await stream.ReadExactlyAsync(response, timeout.Token);
            if (response[0] != 5 || response[1] != method)
                throw new IOException("The SOCKS5 proxy rejected the authentication method.");
            if (_options.Credentials is { } credentials)
            {
                var user = Encoding.UTF8.GetBytes(credentials.UserName);
                var password = Encoding.UTF8.GetBytes(credentials.Password);
                var authentication = new byte[3 + user.Length + password.Length];
                authentication[0] = 1;
                authentication[1] = (byte)user.Length;
                user.CopyTo(authentication, 2);
                authentication[2 + user.Length] = (byte)password.Length;
                password.CopyTo(authentication, 3 + user.Length);
                await stream.WriteAsync(authentication, timeout.Token);
                await stream.ReadExactlyAsync(response, timeout.Token);
                if (response[0] != 1 || response[1] != 0)
                    throw new IOException("The SOCKS5 proxy rejected the credentials.");
            }

            // Domain names are resolved by the proxy, never by the destination-side client.
            byte type;
            byte[] address;
            if (IPAddress.TryParse(host.Trim('[', ']'), out var ip))
            {
                type = ip.AddressFamily == AddressFamily.InterNetwork ? (byte)1 : (byte)4;
                address = ip.GetAddressBytes();
            }
            else
            {
                type = 3;
                var domain = Encoding.ASCII.GetBytes(new System.Globalization.IdnMapping().GetAscii(host));
                if (domain.Length is < 1 or > 255) throw new IOException("The destination hostname is too long for SOCKS5.");
                address = new byte[domain.Length + 1];
                address[0] = (byte)domain.Length;
                domain.CopyTo(address, 1);
            }
            var request = new byte[6 + address.Length];
            request[0] = 5;
            request[1] = 1; // CONNECT only; UDP is deliberately disabled for proxied transfers.
            request[3] = type;
            address.CopyTo(request, 4);
            request[^2] = (byte)(port >> 8);
            request[^1] = (byte)port;
            await stream.WriteAsync(request, timeout.Token);
            var header = new byte[4];
            await stream.ReadExactlyAsync(header, timeout.Token);
            if (header[0] != 5 || header[1] != 0 || header[2] != 0)
                throw new IOException("The SOCKS5 proxy could not connect to the destination.");
            var addressLength = header[3] switch {1 => 4, 4 => 16, 3 => 0, _ => -1};
            if (addressLength < 0) throw new IOException("The SOCKS5 proxy returned an invalid address.");
            if (header[3] == 3)
            {
                var length = new byte[1];
                await stream.ReadExactlyAsync(length, timeout.Token);
                addressLength = length[0];
            }
            await stream.ReadExactlyAsync(new byte[addressLength + 2], timeout.Token);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private sealed class SocksConnector(TorrentNetwork network) : ISocketConnector
    {
        public async ReusableTask<Socket> ConnectAsync(Uri uri, CancellationToken token) =>
            await network.ConnectAsync(uri.Host, uri.Port, AddressFamily.Unspecified, token);
    }
}
