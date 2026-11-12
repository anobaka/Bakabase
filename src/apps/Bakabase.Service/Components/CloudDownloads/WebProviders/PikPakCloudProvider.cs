using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Cloud;
using Microsoft.AspNetCore.WebUtilities;

namespace Bakabase.Service.Components.CloudDownloads.WebProviders;

/// <summary>Independent implementation of the anonymous PikPak public-share HTTP contract.
/// API fields/public web-client parameters were checked against current OpenList's PikPakShare
/// driver and official public web client on 2026-10-27. No OpenList implementation is embedded or executed.
/// </summary>
public sealed class PikPakCloudProvider : ResolvedShareCloudProvider
{
    public PikPakCloudProvider(AppService app, IHttpDownloader downloader, IHttpClientFactory clients) : base(app, downloader, clients) { }
    internal PikPakCloudProvider(string appData, IHttpDownloader downloader, IHttpClientFactory clients) : base(() => appData, downloader, clients) { }
    public override string ProviderId => "pikpak";
    protected override string ClientName => PublicShareDownloadClients.PikPak;
    public override bool CanRevalidateDownloadFailure(string exactCode) => exactCode is
        "PIKPAK_ORIGINAL_FILE_UNAVAILABLE" or "PIKPAK_PUBLIC_ACCESS_REQUIRED" or
        "PIKPAK_VERIFICATION_REQUIRED" or "PIKPAK_ACCESS_CODE_REQUIRED";

    internal override void ValidateSource(CloudSource source)
    {
        if (!PikPakShareLocator.TryParse(source.Url, out _))
            throw new ArgumentException("Choose a public PikPak /s/ share URL.");
    }

    internal override async Task<PublicShareResolution> ResolveAsync(CloudSource source, HttpClient client, CancellationToken ct)
    {
        ValidateSource(source);
        var uri = new Uri(source.Url);
        PikPakShareLocator.TryParse(source.Url, out var locator);
        var shareId = locator!.ShareId;
        var query = QueryHelpers.ParseQuery(uri.Query);
        var passCode = source.AccessCode ?? (query.TryGetValue("pwd", out var pwd) ? pwd.ToString() :
            query.TryGetValue("pass_code", out var code) ? code.ToString() : "");
        var api = new ShareApi(client, shareId);
        await api.Initialize(ct);
        if (passCode.Length != 0) await api.Unlock(passCode, ct);
        var files = new List<PublicShareFile>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var allIds = new HashSet<string>(StringComparer.Ordinal);
        await Visit("", "", 0, 0);
        ValidateManifest(files);
        return new(files, async (file, refresh, token) =>
        {
            using var response = await api.Request("/drive/v1/share/file_info", new()
            {
                ["share_id"] = shareId, ["file_id"] = file.Id, ["pass_code_token"] = api.PassToken
            }, token);
            if (response.RootElement.TryGetProperty("share_status", out _)) api.CheckShareStatus(response.RootElement);
            if (!response.RootElement.TryGetProperty("file_info", out var data) || data.ValueKind != JsonValueKind.Object)
                throw new CloudProviderException("PikPak did not return original file metadata.");
            SameOriginal(file, ReadFile(data, file.Path[..Math.Max(0, file.Path.LastIndexOf('/') + 1)]));
            // medias/link may be a lower-resolution transcode, even when marked default/origin.
            // Only the original web_content_link satisfies an acquisition manifest.
            var original = Text(data, "web_content_link");
            if (!Uri.TryCreate(original, UriKind.Absolute, out var download) || !PublicShareDownloadClients.IsProviderHost("pikpak", download))
                throw new CloudProviderException("PIKPAK_ORIGINAL_FILE_UNAVAILABLE: PikPak lists this file but did not return a downloadable original-file URL. Retrieve it with the official client and associate the local files, or save it to your own drive and select an rclone account path. Configuring an account alone does not import this public share.");
            return download;
        });

        async Task Visit(string parent, string prefix, int depth, int selectionIndex)
        {
            if (depth > 32 || !visited.Add(parent) || visited.Count > 100000)
                throw new CloudProviderException("PikPak returned a folder cycle or exceeded the recursive listing limit.");
            var seenPages = new HashSet<string>(StringComparer.Ordinal);
            var pageToken = "";
            var count = 0L;
            var selected = selectionIndex < locator.NodeIds.Count ? locator.NodeIds[selectionIndex] : null;
            var foundSelection = false;
            long? expected = null;
            for (var page = 0; page < 10001; page++)
            {
                if (!seenPages.Add(pageToken)) throw new CloudProviderException("PikPak repeated a listing page; the manifest is incomplete.");
                using var response = await api.Request("/drive/v1/share/detail", new()
                {
                    ["share_id"] = shareId, ["parent_id"] = parent, ["pass_code_token"] = api.PassToken,
                    ["page_token"] = pageToken, ["limit"] = "100", ["with_audit"] = "true", ["thumbnail_size"] = "SIZE_LARGE"
                }, ct);
                var data = response.RootElement;
                api.CheckShareStatus(data);
                if (!data.TryGetProperty("files", out var batch) || batch.ValueKind != JsonValueKind.Array ||
                    !data.TryGetProperty("next_page_token", out var next) || next.ValueKind != JsonValueKind.String)
                    throw new CloudProviderException("PikPak did not return a complete, paginated file listing.");
                if (data.TryGetProperty("file_count", out _))
                {
                    var total = Size(data, "file_count");
                    if (expected != null && expected != total) throw new CloudProviderException("PikPak folder contents changed during listing.");
                    expected = total;
                }
                foreach (var item in batch.EnumerateArray())
                {
                    var id = RequiredString(item, "id");
                    if (!allIds.Add(id) || allIds.Count > 100000) throw new CloudProviderException("PikPak returned duplicate or excessive entries.");
                    count++;
                    if (selected != null && selected != id) continue;
                    foundSelection = true;
                    var phase = Text(item, "phase");
                    if (phase != null && phase != "PHASE_TYPE_COMPLETE") throw new CloudProviderException("A PikPak original file is not ready; the share cannot yet be downloaded completely.");
                    switch (RequiredString(item, "kind"))
                    {
                        case "drive#folder":
                            var folder = SafeSegment(RequiredString(item, "name"));
                            await Visit(id, selected != null ? "" : prefix + folder + "/", depth + 1,
                                selected != null ? selectionIndex + 1 : selectionIndex);
                            break;
                        case "drive#file":
                            if (selected != null && selectionIndex + 1 != locator.NodeIds.Count)
                                throw new CloudProviderException("The selected PikPak path continues beyond a file; choose the original share again.");
                            files.Add(ReadFile(item, prefix)); break;
                        default: throw new CloudProviderException("PikPak returned an unsupported entry; the manifest is incomplete.");
                    }
                }
                pageToken = next.GetString()!;
                if (pageToken.Length == 0)
                {
                    if (expected != null && count != expected) throw new CloudProviderException("PikPak folder listing did not match its declared item count.");
                    if (selected != null && !foundSelection)
                        throw new CloudProviderException("The selected PikPak item is not present in this share path; choose the original share again.");
                    return;
                }
                if (batch.GetArrayLength() == 0 || pageToken.Length > 8192) throw new CloudProviderException("PikPak pagination made no progress.");
            }
            throw new CloudProviderException("PikPak pagination limit exceeded; the manifest is incomplete.");
        }
    }

    private static PublicShareFile ReadFile(JsonElement data, string prefix)
    {
        if (RequiredString(data, "kind") != "drive#file") throw new CloudProviderException("PikPak did not provide an original file.");
        var version = Text(data, "modified_time") ?? "";
        var hash = Text(data, "hash") ?? "";
        return new(RequiredString(data, "id"), prefix + SafeSegment(RequiredString(data, "name")), Size(data, "size"), version + ":" + hash);
    }

    private sealed class ShareApi(HttpClient client, string shareId)
    {
        // Public web-client protocol values, not user secrets or account credentials.
        private const string ClientId = "YUMx5nI8ZU8Ap8pm";
        private readonly string _deviceId = Guid.NewGuid().ToString("N");
        private string _captcha = "";
        public string PassToken { get; private set; } = "";

        public Task Initialize(CancellationToken ct) => Captcha("GET:/drive/v1/share:batch_file_info", ct);

        public async Task Unlock(string password, CancellationToken ct)
        {
            using var result = await Request("/drive/v1/share", new()
            { ["share_id"] = shareId, ["pass_code"] = password, ["thumbnail_size"] = "SIZE_LARGE", ["limit"] = "100" }, ct);
            CheckShareStatus(result.RootElement);
            PassToken = Text(result.RootElement, "pass_code_token") ?? "";
            if (PassToken.Length == 0) throw new CloudProviderException("PIKPAK_ACCESS_CODE_REQUIRED: check the public share's access code in the source information.");
        }

        public void CheckShareStatus(JsonElement data)
        {
            var status = Text(data, "share_status");
            if (status == "OK") return;
            if (status is "PASS_CODE_EMPTY" or "PASS_CODE_ERROR")
                throw new CloudProviderException("PIKPAK_ACCESS_CODE_REQUIRED: this share needs a valid access code; update the source information and retry.");
            throw new CloudProviderException("The PikPak public share is expired, restricted or unavailable; no complete original-file manifest was produced.");
        }

        public async Task<JsonDocument> Request(string path, Dictionary<string, string?> query, CancellationToken ct)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, QueryHelpers.AddQueryString("https://api-drive.mypikpak.net" + path, query));
                Headers(request);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromMinutes(2));
                using var transport = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                JsonDocument response;
                try { response = JsonDocument.Parse(await ReadBounded(transport.Content, 32 * 1024 * 1024, deadline.Token)); }
                catch (JsonException) { throw new CloudProviderException("PikPak did not return a supported public-share response; verification or a later retry may be required."); }
                var error = response.RootElement.TryGetProperty("error_code", out var code) && code.ValueKind == JsonValueKind.Number ? code.GetInt64() : 0;
                if (transport.IsSuccessStatusCode && error == 0 && string.IsNullOrEmpty(Text(response.RootElement, "error")))
                {
                    // Public shares also issue an access token in detail responses. Without it,
                    // file_info can return metadata but omit the authorized original-file URL.
                    if (Text(response.RootElement, "pass_code_token") is {Length: > 0} token) PassToken = token;
                    return response;
                }
                response.Dispose();
                if (error == 9 && attempt == 0) { await Captcha("GET:" + path, ct); continue; }
                throw new CloudProviderException("PIKPAK_PUBLIC_ACCESS_REQUIRED: PikPak rejected anonymous share access or requires verification. Use its official client or retry later.");
            }
            throw new CloudProviderException("PikPak public access could not be refreshed.");
        }

        private void Headers(HttpRequestMessage request)
        {
            request.Headers.TryAddWithoutValidation("X-Client-ID", ClientId);
            request.Headers.TryAddWithoutValidation("X-Device-ID", _deviceId);
            request.Headers.TryAddWithoutValidation("X-Captcha-Token", _captcha);
        }

        private async Task Captcha(string action, CancellationToken ct)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            var signature = ClientId + "2.0.0mypikpak.com" + _deviceId + timestamp;
            // Public web protocol salts. The algorithm is implemented here independently:
            // each stage hashes the preceding stage followed by that protocol salt.
            foreach (var salt in new[] {"C9qPpZLN8ucRTaTiUMWYS9cQvWOE", "+r6CQVxjzJV6LCV", "F", "pFJRC",
                         "9WXYIDGrwTCz2OiVlgZa90qpECPD6olt", "/750aCr4lm/Sly/c", "RB+DT/gZCrbV", "",
                         "CyLsf7hdkIRxRm215hl", "7xHvLi2tOYP0Y92b", "ZGTXXxu8E/MIWaEDB+Sm/", "1UI3",
                         "E7fP5Pfijd+7K+t6Tg/NhuLq0eEUVChpJSkrKxpO", "ihtqpG6FMt65+Xk+tWUH2", "NhXXU9rg4XXdzo7u5o"})
                signature = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(signature + salt))).ToLowerInvariant();
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://user.mypikpak.net/v1/shield/captcha/init")
            {
                Content = JsonContent.Create(new
                {
                    action, captcha_token = _captcha, client_id = ClientId, device_id = _deviceId,
                    meta = new {client_version = "2.0.0", package_name = "mypikpak.com", user_id = "", timestamp, captcha_sign = "1." + signature}
                })
            };
            Headers(request);
            using var response = await JsonResponse(client, request, ct);
            _captcha = Text(response.RootElement, "captcha_token") ?? "";
            if (_captcha.Length == 0 || !string.IsNullOrEmpty(Text(response.RootElement, "url")))
                throw new CloudProviderException("PIKPAK_VERIFICATION_REQUIRED: anonymous access requires interactive verification. Open the share in the official client or try another source.");
        }
    }
}
