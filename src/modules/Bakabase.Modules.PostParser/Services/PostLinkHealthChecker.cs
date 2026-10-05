using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>Small metadata probes. Unsupported providers, authentication and ambiguous responses stay unknown.</summary>
public class PostLinkHealthChecker(IHttpClientFactory clients) : IPostLinkHealthChecker
{
    public const string HttpClientName = "PostParser.LinkHealth";
    private const int MaxResponseBytes = 256 * 1024;

    public async Task<PostLinkHealth> CheckAsync(string url, string? accessCode, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)) return Unknown("unsupportedUrl");
        var host = uri.IdnHost.ToLowerInvariant();
        var mega = host is "mega.nz" or "mega.co.nz";
        var baidu = host is "pan.baidu.com" or "yun.baidu.com";
        var oneDrive = host is "1drv.ms" or "onedrive.live.com";
        if (!mega && !baidu && !oneDrive) return Unknown("unsupportedProvider");
        if (baidu && !Regex.IsMatch(uri.AbsolutePath, @"^/s/[A-Za-z0-9_-]+$")) return Unknown("unsupportedShareUrl");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var client = clients.CreateClient(HttpClientName);
            if (mega) return await CheckMegaAsync(client, uri, timeout.Token);
            for (var redirects = 0; redirects < 4; redirects++)
            {
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if ((int)response.StatusCode is >= 300 and <= 399)
                {
                    if (response.Headers.Location is not { } location) return Unknown("redirectWithoutLocation");
                    var next = new Uri(uri, location);
                    var nextHost = next.IdnHost.ToLowerInvariant();
                    if (next.Scheme != "https" || !next.IsDefaultPort || !string.IsNullOrEmpty(next.UserInfo) ||
                        !(baidu && nextHost is "pan.baidu.com" or "yun.baidu.com" ||
                          oneDrive && nextHost is "1drv.ms" or "onedrive.live.com"))
                        return Unknown("authenticationOrUnsupportedRedirect");
                    uri = next;
                    continue;
                }
                // A server/proxy status by itself is not evidence that a public share was deleted.
                if (!response.IsSuccessStatusCode && response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Gone))
                    return Unknown("httpResponseNotConclusive");
                var body = await ReadBoundedAsync(response, timeout.Token);
                if (body == null) return Unknown("responseTooLarge");
                var visible = Regex.Replace(body, @"<(script|style)\b[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
                visible = WebUtility.HtmlDecode(Regex.Replace(visible, "<[^>]+>", " "));
                if (baidu && new[] {"分享的文件已经被取消", "分享的文件已被取消", "分享已取消", "分享的文件不存在", "分享已过期"}.Any(visible.Contains))
                    return Unavailable("providerReportsShareUnavailable");
                if (oneDrive && new[] {"This item might not exist or is no longer available", "This link has been removed", "此项目可能不存在或不再可用", "此链接已被删除"}.Any(visible.Contains))
                    return Unavailable("providerReportsShareUnavailable");
                return Unknown(baidu && (visible.Contains("提取码") || visible.Contains("访问码"))
                    ? "accessCodeOrInteractiveVerificationRequired" : "pageDidNotConfirmAvailability");
            }
            return Unknown("tooManyRedirects");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Unknown("checkTimedOut"); }
        catch (HttpRequestException) { return Unknown("networkError"); }
        catch (IOException) { return Unknown("networkError"); }
        catch (JsonException) { return Unknown("unrecognizedProviderResponse"); }
    }

    private static async Task<PostLinkHealth> CheckMegaAsync(HttpClient client, Uri uri, CancellationToken ct)
    {
        var match = Regex.Match(uri.AbsolutePath, @"^/file/(?<id>[A-Za-z0-9_-]{8})$");
        if (!match.Success) return Unknown("megaFolderOrUnsupportedShareUrl");
        // MEGA SDK's command g without the g=1 flag requests metadata only; it never downloads files.
        // https://github.com/meganz/sdk/blob/master/src/commands.cpp (CommandGetFile)
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://g.api.mega.co.nz/cs?id=0")
        {
            Content = JsonContent.Create(new[] {new {a = "g", p = match.Groups["id"].Value}})
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return Unknown("httpResponseNotConclusive");
        var body = await ReadBoundedAsync(response, ct);
        if (body == null) return Unknown("responseTooLarge");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 1) return Unknown("unrecognizedProviderResponse");
        var result = root[0];
        // API_ENOENT=-9 is a missing node. Access errors, quota and transient errors remain unknown.
        if (result.ValueKind == JsonValueKind.Number && result.TryGetInt32(out var error) && error == -9)
            return Unavailable("providerReportsShareUnavailable");
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("d", out _))
            return Unavailable("providerReportsShareBlocked");
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("e", out _))
            return Unknown("providerReportedAnError");
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("s", out var size) &&
            size.TryGetInt64(out var bytes) && bytes >= 0 && result.TryGetProperty("at", out var attributes) &&
            attributes.ValueKind == JsonValueKind.String)
            return Regex.IsMatch(uri.Fragment, @"^#[A-Za-z0-9_-]{43}$")
                ? new PostLinkHealth {Status = "available", Reason = "publicFileMetadataAvailable"}
                : Unknown("megaDecryptionKeyMissingOrUnrecognized");
        return Unknown("unrecognizedProviderResponse");
    }

    private static async Task<string?> ReadBoundedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > MaxResponseBytes) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static PostLinkHealth Unknown(string reason) => new() {Reason = reason};
    private static PostLinkHealth Unavailable(string reason) => new() {Status = "unavailable", Reason = reason};

    /// <summary>Pin each request to a public DNS answer, with no redirects, cookies or ambient credentials.</summary>
    public static HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            var address = addresses.FirstOrDefault(IsPublicAddress)
                ?? throw new HttpRequestException("The share host has no public address.");
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
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
                !(b[0] == 192 && b[1] is 0 or 168) && !(b[0] == 198 && b[1] is 18 or 19) &&
                !(b[0] == 198 && b[1] == 51 && b[2] == 100) && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        return b.Length == 16 && (b[0] & 0xe0) == 0x20 &&
            !(b[0] == 0x20 && b[1] == 0x02) &&
            !(b[0] == 0x20 && b[1] == 0x01 && (b[2] == 0x0d && b[3] == 0xb8 || b[2] == 0 && b[3] == 0));
    }
}
