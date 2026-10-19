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
internal sealed class TorrentNetwork : IDisposable
{
    private readonly TorrentNetworkOptions _options;
    private readonly IWebProxy? _systemProxy;
    private readonly CancellationTokenSource _routingFailure = new();
    private Exception? _configurationError;
    public bool UsesProxy => _options.UseSystemProxy || _options.ProxyUri != null;
    internal CancellationToken RoutingFailureToken => _routingFailure.Token;
    internal Exception? ConfigurationError => Volatile.Read(ref _configurationError);

    public TorrentNetwork(TorrentNetworkOptions options, IWebProxy? systemProxy = null)
    {
        if (options.UseSystemProxy && options.ProxyUri != null)
            throw new ArgumentException("Select either the system proxy or a custom BT proxy.");
        if (options.ProxyUri is { } proxy)
        {
            ValidateProxy(proxy);
            if (!string.IsNullOrEmpty(proxy.UserInfo))
                throw new ArgumentException("Configure BT proxy credentials separately from its address.");
            ValidateCredentials(proxy, options.Credentials);
        }
        _options = options with
        {
            Credentials = options.Credentials is { } c ? new NetworkCredential(c.UserName, c.Password, c.Domain) : null
        };
        // Resolving this only in system mode is essential: direct BT never consults OS/env routing.
        _systemProxy = options.UseSystemProxy ? systemProxy ?? HttpClient.DefaultProxy : null;
    }

    public HttpClient CreateHttpClient(AddressFamily family = AddressFamily.Unspecified)
    {
        var handler = new SocketsHttpHandler
        {
            // Routing is owned by the connector, including system proxy/bypass decisions.
            UseProxy = false,
            // System/PAC rules can differ by path on the same origin. Re-resolve each request
            // instead of reusing a direct connection/tunnel chosen for another URI.
            PooledConnectionLifetime = _options.UseSystemProxy ? TimeSpan.Zero : TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(30),
            ConnectCallback = async (context, ct) => new NetworkStream(
                await ConnectAsync(context.InitialRequestMessage.RequestUri!, family, ct), true)
        };
        return new HttpClient(handler) {Timeout = TimeSpan.FromSeconds(30)};
    }

    public Factories CreateFactories()
    {
        var factories = Factories.Default.WithHttpClientCreator(CreateHttpClient);
        // MonoTorrent's default tracker delegates close over their original Factories instance;
        // overriding only WithHttpClientCreator would leave HTTP trackers on the system proxy.
        Factories.TrackerCreator tracker = uri => new Tracker(
            new OutboundOnlyTrackerConnection(new HttpTrackerConnection(uri, CreateHttpClient, AddressFamily.InterNetwork)),
            new OutboundOnlyTrackerConnection(new HttpTrackerConnection(uri, CreateHttpClient, AddressFamily.InterNetworkV6)));
        factories = factories.WithTrackerCreator("http", tracker).WithTrackerCreator("https", tracker);
        if (UsesProxy)
        {
            var connector = new RoutedConnector(this);
            factories = factories.WithSocketConnectorCreator(() => connector)
                .WithPeerConnectionCreator("ipv4", uri => new SocketPeerConnection(uri, connector))
                .WithPeerConnectionCreator("ipv6", uri => new SocketPeerConnection(uri, connector))
                // Passing null restores the default creator; return null instead to disable it.
                .WithTrackerCreator("udp", _ => null!);
        }
        else
        {
            factories = factories.WithTrackerCreator("udp", uri => new Tracker(
                new OutboundOnlyTrackerConnection(new UdpTrackerConnection(uri, AddressFamily.InterNetwork)),
                new OutboundOnlyTrackerConnection(new UdpTrackerConnection(uri, AddressFamily.InterNetworkV6))));
        }
        return factories;
    }

    private sealed class OutboundOnlyTrackerConnection(ITrackerConnection inner) : ITrackerConnection
    {
        public bool CanScrape => inner.CanScrape;
        public Uri Uri => inner.Uri;

        public ReusableTask<AnnounceResponse> AnnounceAsync(AnnounceRequest request, CancellationToken token) =>
            inner.AnnounceAsync(request.WithReportedEndPointFunc(type =>
            {
                var endpoint = request.GetReportedAddress(type);
                // With no listeners MonoTorrent reports -1, which real trackers reject.
                // 6881 is only a protocol compatibility port for this outbound-only client;
                // it does not bind a socket, map a port or override the tracker-observed IP.
                return (endpoint.address, endpoint.port is > 0 and <= 65535 ? endpoint.port : 6881);
            }), token);

        public ReusableTask<ScrapeResponse> ScrapeAsync(ScrapeRequest request, CancellationToken token) =>
            inner.ScrapeAsync(request, token);
    }

    internal Task<Socket> ConnectAsync(string host, int port, AddressFamily family, CancellationToken ct) =>
        ConnectAsync(new UriBuilder(Uri.UriSchemeHttp, host, port).Uri, family, ct);

    internal async Task<Socket> ConnectAsync(Uri destination, AddressFamily family, CancellationToken ct)
    {
        // HTTP(S) requests retain their actual scheme. Peers have no HTTP scheme, so resolve
        // them as HTTP destinations (HTTP_PROXY/ALL_PROXY and NO_PROXY on environment-based hosts).
        if (destination.Scheme is not ("http" or "https"))
            destination = new UriBuilder(Uri.UriSchemeHttp, destination.Host, destination.Port).Uri;
        Uri? proxy;
        NetworkCredential? credentials;
        try
        {
            proxy = _options.ProxyUri;
            credentials = _options.Credentials;
            if (_systemProxy != null)
            {
                proxy = _systemProxy.IsBypassed(destination) ? null : _systemProxy.GetProxy(destination);
                if (proxy == destination) proxy = null;
                credentials = proxy == null ? null : _systemProxy.Credentials?.GetCredential(proxy, "Basic");
                if (proxy != null && credentials == null && !string.IsNullOrEmpty(proxy.UserInfo))
                {
                    var parts = proxy.UserInfo.Split(':', 2);
                    credentials = new NetworkCredential(Uri.UnescapeDataString(parts[0]),
                        parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
                }
            }
            if (proxy != null)
            {
                ValidateProxy(proxy);
                ValidateCredentials(proxy, credentials);
            }
        }
        catch (Exception ex)
        {
            // MonoTorrent otherwise retries peer/tracker configuration failures until timeout.
            // Cancel the owning transfer so an unsupported route is reported immediately.
            var error = ex is ArgumentException or NotSupportedException
                ? ex : new InvalidOperationException("The system BT proxy could not be resolved.", ex);
            FailRouting(error);
            throw;
        }

        var host = destination.DnsSafeHost;
        var port = destination.Port;
        var socket = proxy != null || family == AddressFamily.Unspecified
            ? new Socket(SocketType.Stream, ProtocolType.Tcp)
            : new Socket(family, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.NoDelay = true;
            if (proxy == null)
            {
                await socket.ConnectAsync(host, port, ct);
                return socket;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await socket.ConnectAsync(proxy.DnsSafeHost, proxy.Port, timeout.Token);
            using var stream = new NetworkStream(socket, false);
            if (proxy.Scheme == "http")
            {
                await ConnectHttpTunnelAsync(stream, destination, credentials, timeout.Token);
                return socket;
            }
            var method = credentials == null ? (byte)0 : (byte)2;
            await stream.WriteAsync(new byte[] {5, 1, method}, timeout.Token);
            var response = new byte[2];
            await stream.ReadExactlyAsync(response, timeout.Token);
            if (response[0] != 5 || response[1] != method)
                throw new ProxyAuthenticationException("The SOCKS5 proxy rejected the authentication method.");
            if (credentials != null)
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
                    throw new ProxyAuthenticationException("The SOCKS5 proxy rejected the credentials.");
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
        catch (Exception ex)
        {
            socket.Dispose();
            if (ex is ProxyAuthenticationException) FailRouting(ex);
            throw;
        }
    }

    private static void ValidateProxy(Uri proxy)
    {
        if (!proxy.IsAbsoluteUri || string.IsNullOrWhiteSpace(proxy.Host) || proxy.Port is < 1 or > 65535)
            throw new ArgumentException("The BT proxy requires a host and a valid port.");
        if (proxy.Scheme is not ("http" or "socks5"))
            throw new NotSupportedException("BT supports HTTP CONNECT and SOCKS5 proxies. The selected system proxy protocol is unsupported.");
    }

    private static void ValidateCredentials(Uri proxy, NetworkCredential? credentials)
    {
        if (credentials == null) return;
        if (proxy.Scheme == "socks5" && (Encoding.UTF8.GetByteCount(credentials.UserName) is < 1 or > 255 ||
            Encoding.UTF8.GetByteCount(credentials.Password) > 255))
            throw new ArgumentException("SOCKS5 credentials must use a username of 1–255 bytes and a password of at most 255 bytes.");
        if (proxy.Scheme == "http" && credentials.UserName.Contains(':'))
            throw new ArgumentException("HTTP proxy usernames cannot contain a colon.");
    }

    private static async Task ConnectHttpTunnelAsync(NetworkStream stream, Uri destination,
        NetworkCredential? credentials, CancellationToken ct)
    {
        var host = destination.IdnHost.Trim('[', ']');
        var authority = $"{(host.Contains(':') ? $"[{host}]" : host)}:{destination.Port}";
        var request = new StringBuilder($"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\n");
        if (credentials != null)
        {
            var user = string.IsNullOrEmpty(credentials.Domain)
                ? credentials.UserName : $"{credentials.Domain}\\{credentials.UserName}";
            request.Append("Proxy-Authorization: Basic ").Append(Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{user}:{credentials.Password}"))).Append("\r\n");
        }
        request.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request.ToString()), ct);
        // Do not buffer beyond the headers: those bytes belong to the tunneled peer/TLS stream.
        var header = new List<byte>();
        var next = new byte[1];
        while (header.Count < 16 * 1024)
        {
            await stream.ReadExactlyAsync(next, ct);
            header.Add(next[0]);
            if (header.Count < 4 || header[^4] != '\r' || header[^3] != '\n' || header[^2] != '\r' || header[^1] != '\n')
                continue;
            var status = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", 2)[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (status.Length < 2 || !status[0].StartsWith("HTTP/1.", StringComparison.Ordinal) ||
                !int.TryParse(status[1], out var code))
                throw new IOException("The HTTP proxy returned an invalid CONNECT response.");
            if (code == 407)
                throw new ProxyAuthenticationException("The HTTP proxy rejected authentication (status 407). Check the saved proxy credentials; only Basic authentication is supported.");
            if (code is < 200 or > 299)
                throw new IOException($"The HTTP proxy rejected the CONNECT tunnel (status {code}).");
            return;
        }
        throw new IOException("The HTTP proxy CONNECT response headers exceed 16 KiB.");
    }

    private void FailRouting(Exception error)
    {
        Interlocked.CompareExchange(ref _configurationError, error, null);
        try { _routingFailure.Cancel(); }
        catch (ObjectDisposedException) { /* A late tracker callback after the owner has already stopped. */ }
    }

    public void Dispose() => _routingFailure.Dispose();

    private sealed class ProxyAuthenticationException(string message) : IOException(message);

    private sealed class RoutedConnector(TorrentNetwork network) : ISocketConnector
    {
        public async ReusableTask<Socket> ConnectAsync(Uri uri, CancellationToken token) =>
            await network.ConnectAsync(uri, AddressFamily.Unspecified, token);
    }
}
