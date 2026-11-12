using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.TeraBox;

/// <summary>Official CLI 1.1.0 discards a download rejection's response body. Only
/// after its exact HTTP-403 failure, inspect the same native-issued grant once.
/// This is never a download fallback: successful bodies are limited to one byte,
/// errors to 4 KiB, redirects are disabled, and nothing sensitive is persisted.</summary>
internal sealed class TeraBoxDownloadFailureProbe
{
    internal const string AccessDenied = "TERABOX_DOWNLOAD_ACCESS_DENIED";
    internal const string IdentityRejected = "TERABOX_DOWNLOAD_IDENTITY_REJECTED";
    private readonly Func<HttpClient> _client;
    internal TeraBoxDownloadFailureProbe(BakabaseWebProxy proxy) => _client = () => new(CreateHandler(proxy)) {Timeout = Timeout.InfiniteTimeSpan};
    internal TeraBoxDownloadFailureProbe(HttpMessageHandler handler) => _client = () => new(handler, false) {Timeout = Timeout.InfiniteTimeSpan};
    internal static SocketsHttpHandler CreateHandler(BakabaseWebProxy proxy) => new()
    {
        Proxy = proxy.ForDownloadTool(DownloadToolProxyTargets.TeraBoxCli, ThirdPartyId.TeraBox),
        UseProxy = true, UseCookies = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(10),
        MaxResponseDrainSize = 0, ResponseDrainTimeout = TimeSpan.Zero
    };

    internal static bool IsNativeDownloadDenied(IReadOnlyList<string> arguments, string error) =>
        RequestFile(arguments) != null && error.Split('\n', 2)[0].TrimEnd('\r') == "Error: download failed: HTTP 403";

    private static (string Id, string Output)? RequestFile(IReadOnlyList<string> arguments)
    {
        if (arguments.Count is not (5 or 7) || arguments[0] != "share-download" ||
            !TeraBoxShareLocator.TryParse(arguments[1], out _) || !Path.IsPathFullyQualified(arguments[2])) return null;
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 3; i < arguments.Count; i += 2)
            if (arguments[i] is not ("--fsid" or "--pwd") || !flags.TryAdd(arguments[i], arguments[i + 1])) return null;
        return flags.TryGetValue("--fsid", out var id) && ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? (number.ToString(CultureInfo.InvariantCulture), arguments[2]) : null;
    }

    internal async Task<string> ClassifyAsync(string config, IReadOnlyList<string> arguments,
        string? payloadDirectory, string output, CancellationToken ct)
    {
        try
        {
            var request = RequestFile(arguments);
            if (request == null || !EmptyPayload(request.Value.Output, payloadDirectory) ||
                !TryGrant(output, request.Value.Id, Path.GetFileName(request.Value.Output), out var grant)) return AccessDenied;
            TeraBoxStorage.NoLinks(config);
            if (Path.GetFileName(config) != "config.json" || Path.GetFileName(Path.GetDirectoryName(config)) != "terabox" ||
                !File.Exists(config) || new FileInfo(config).Length > 1024 * 1024) return AccessDenied;
            using var profile = JsonDocument.Parse(await File.ReadAllTextAsync(config, ct));
            if (!profile.RootElement.TryGetProperty("auth", out var auth)) return AccessDenied;
            var token = TeraBoxJson.Text(auth, "access_token");
            if (string.IsNullOrEmpty(token) || token.Length > 4096 || token.Any(char.IsControl)) return AccessDenied;
            if (!EmptyPayload(request.Value.Output, payloadDirectory)) return AccessDenied;
            // Reproduce the failing native request, not another API or token variant.
            // The CLI uses singular access_token here; its metadata calls use plural.
            var uri = new Uri(grant!.OriginalString + "&access_token=" + Uri.EscapeDataString(token));
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
            lifetime.CancelAfter(TimeSpan.FromSeconds(15));
            using var client = _client();
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.UserAgent.ParseAdd("terabox-storage/1.0");
            message.Headers.Range = new RangeHeaderValue(0, 0);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, lifetime.Token);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(lifetime.Token);
                await stream.ReadAsync(new byte[1], lifetime.Token);
                return AccessDenied; // A diagnostic success cannot complete the failed download.
            }
            if (response.StatusCode != HttpStatusCode.Forbidden) return AccessDenied;
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not ("text/plain" or "application/json") && mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) != true ||
                response.Content.Headers.ContentLength > 4096) return AccessDenied;
            await using var body = await response.Content.ReadAsStreamAsync(lifetime.Token);
            var buffer = new byte[4096]; var length = 0;
            while (length < buffer.Length)
            {
                var received = await body.ReadAsync(buffer.AsMemory(length), lifetime.Token);
                if (received == 0) break;
                length += received;
            }
            if (length == buffer.Length) return AccessDenied; // Truncation cannot prove an error code.
            return ClassifyForbiddenBody(buffer.AsMemory(0, length));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        { return AccessDenied; } // Optional diagnosis must never attach URL-, token- or response-bearing exceptions.
    }

    private static bool EmptyPayload(string output, string? directory)
    {
        if (directory == null || !Path.IsPathFullyQualified(directory) ||
            Path.GetDirectoryName(Path.GetFullPath(output)) != Path.GetFullPath(directory)) return false;
        TeraBoxStorage.NoLinks(output);
        return !File.Exists(output) && !Directory.Exists(output);
    }

    internal static string ClassifyForbiddenBody(ReadOnlyMemory<byte> body)
    {
        if (body.Length >= 4096) return AccessDenied;
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                   json.RootElement.TryGetProperty("error_code", out var code) && code.ValueKind == JsonValueKind.Number &&
                   code.TryGetInt32(out var number) && number == 31045 ? IdentityRejected : AccessDenied;
        }
        catch (JsonException) { return AccessDenied; }
    }

    internal static bool TryGrant(string output, string id, string filename, out Uri? grant)
    {
        grant = null;
        if (output.Length > 32 * 1024 * 1024) return false;
        var remaining = output.AsSpan().TrimStart(); var count = 0; JsonElement? response = null;
        var sawParameters = false;
        while (remaining.StartsWith("[DEBUG] ", StringComparison.Ordinal))
        {
            if (++count > 16) return false;
            var newline = remaining.IndexOf('\n');
            if (newline < 0 || newline > 8 * 1024 * 1024) return false;
            var line = remaining[..newline].TrimEnd('\r');
            if (!TeraBoxCliProcess.KnownDiagnostic("share-download", line)) return false;
            const string parameters = "[DEBUG] share/download URL params (no token): ";
            const string receipt = "[DEBUG] share/download response: ";
            if (line.StartsWith(parameters, StringComparison.Ordinal))
            {
                if (sawParameters || !line.Contains(("&fid_list=[" + id + "]&sekey=").AsSpan(), StringComparison.Ordinal)) return false;
                sawParameters = true;
            }
            if (line.StartsWith(receipt, StringComparison.Ordinal))
            {
                if (response != null) return false;
                using var json = JsonDocument.Parse(line[receipt.Length..].ToString()); response = json.RootElement.Clone();
            }
            remaining = remaining[(newline + 1)..].TrimStart();
        }
        if (!remaining.IsEmpty || !sawParameters || response is not { } data ||
            TeraBoxJson.Number(data, "errno") != 0) return false;
        string? link = null;
        if (TeraBoxJson.Text(data, "dlink") is { } single && data.TryGetProperty("file_info", out var info) &&
            TeraBoxJson.Text(info, "filename") == filename && TeraBoxJson.Number(info, "size") != null) link = single;
        else if (data.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() == 1)
        {
            var file = list[0];
            if (TeraBoxJson.Id(file, "fs_id") == id && TeraBoxJson.Text(file, "server_filename") == filename &&
                TeraBoxJson.Number(file, "size") != null) link = TeraBoxJson.Text(file, "dlink");
        }
        if (link == null || link.Length > 16384 || link.Any(char.IsControl) || !Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.Host != "d.terabox.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            !Regex.IsMatch(uri.AbsolutePath, @"\A/file/[0-9a-fA-F]{32}\z", RegexOptions.CultureInvariant)) return false;
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = entry.Split('=', 2); var name = Uri.UnescapeDataString(pair[0]);
            if (pair.Length != 2 || name.Equals("access_token", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("access_tokens", StringComparison.OrdinalIgnoreCase) ||
                !query.TryAdd(name, Uri.UnescapeDataString(pair[1]))) return false;
        }
        if (!query.TryGetValue("fid", out var fid) || !Regex.IsMatch(fid, @"\A[0-9]+-[0-9]+-[0-9]+\z") ||
            !ulong.TryParse(fid.Split('-')[2], NumberStyles.None, CultureInfo.InvariantCulture, out var fsid) ||
            fsid.ToString(CultureInfo.InvariantCulture) != id || !query.TryGetValue("sign", out var sign) || sign.Length == 0) return false;
        grant = uri;
        return true;
    }
}
