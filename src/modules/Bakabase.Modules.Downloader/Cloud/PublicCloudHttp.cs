using System.Net;
using System.Net.Sockets;

namespace Bakabase.Modules.Downloader.Cloud;

public sealed class CloudSourceNetworkException : HttpRequestException
{
    public const string RequirementCode = "SOURCE_NETWORK_REQUIRES_PROXY";
    public CloudSourceNetworkException() : base(
        RequirementCode + ": The source cannot be reached through public DNS. Configure an explicit proxy in network settings, then try again.") { }
}

/// <summary>Network boundary for public share metadata and files, without ambient proxy credentials.</summary>
public static class PublicCloudHttp
{
    public static void EnsureSafeUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("https" or "http") || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.AbsoluteUri.Length > 8192 ||
            string.IsNullOrWhiteSpace(uri.IdnHost) || uri.IdnHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.IdnHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.IdnHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            !uri.IdnHost.Contains('.') && !IPAddress.TryParse(uri.IdnHost, out _) ||
            IPAddress.TryParse(uri.IdnHost, out var ip) && !IsPublicAddress(ip))
            throw new ArgumentException("The download source must be a public HTTP(S) address without credentials or a custom port.");
    }

    public static async Task<IPAddress[]> ResolvePublicAddressesAsync(string host, CancellationToken ct)
    {
        IPAddress[] addresses;
        try { addresses = await Dns.GetHostAddressesAsync(host, ct); }
        catch (SocketException) { throw new CloudSourceNetworkException(); }
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new CloudSourceNetworkException();
        return addresses;
    }

    public static async Task EnsurePublicHostAsync(Uri uri, CancellationToken ct)
    {
        EnsureSafeUri(uri);
        await ResolvePublicAddressesAsync(uri.IdnHost, ct);
    }

    /// <summary>Resolve once per connection and connect to that checked address, preventing DNS rebinding.</summary>
    public static HttpMessageHandler CreateHandler(bool preserveRepresentation = false) => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        AutomaticDecompression = preserveRepresentation ? DecompressionMethods.None : DecompressionMethods.GZip | DecompressionMethods.Deflate,
        ActivityHeadersPropagator = preserveRepresentation ? null : System.Diagnostics.DistributedContextPropagator.Current,
        ConnectCallback = async (context, ct) =>
        {
            EnsureSafeUri(context.InitialRequestMessage.RequestUri!);
            var addresses = await ResolvePublicAddressesAsync(context.DnsEndPoint.Host, ct);
            Exception? last = null;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    socket.Dispose();
                    last = error;
                }
                catch { socket.Dispose(); throw; }
            }
            throw new HttpRequestException("The public download source could not be reached.", last);
        }
    };

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        var b = address.GetAddressBytes();
        if (b.Length == 4)
            return b[0] is not (0 or 10 or 127) && b[0] < 224 &&
                   !(b[0] == 100 && b[1] is >= 64 and <= 127) &&
                   !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] is >= 16 and <= 31) &&
                   !(b[0] == 192 && (b[1] is 0 or 168 || b[1] == 88 && b[2] == 99)) &&
                   !(b[0] == 198 && b[1] is 18 or 19) &&
                   !(b[0] == 198 && b[1] == 51 && b[2] == 100) &&
                   !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        return b.Length == 16 && (b[0] & 0xe0) == 0x20 &&
               !(b[0] == 0x20 && b[1] == 0x02) &&
               !(b[0] == 0x20 && b[1] == 0x01 &&
                 (b[2] == 0x0d && b[3] == 0xb8 || b[2] == 0 && b[3] < 0x20));
    }
}
