using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Cloud;
using Microsoft.AspNetCore.WebUtilities;

namespace Bakabase.Service.Components.CloudDownloads.WebProviders;

/// <summary>Anonymous public SharePoint folder resolver. It never mirrors, syncs, deletes, or
/// signs in. The public page's scoped drive token stays in memory and is not forwarded to CDN hosts.
/// </summary>
public sealed class SharePointCloudProvider : ResolvedShareCloudProvider
{
    public SharePointCloudProvider(AppService app, IHttpDownloader downloader, IHttpClientFactory clients) : base(app, downloader, clients) { }
    internal SharePointCloudProvider(string appData, IHttpDownloader downloader, IHttpClientFactory clients) : base(() => appData, downloader, clients) { }
    public override string ProviderId => "oneDrive";
    protected override string ClientName => PublicShareDownloadClients.SharePoint;
    // Only transport failures before any output are eligible. The common recovery guard pins
    // the original operation and the resolver rechecks every original without downloading it.
    public override bool CanRevalidateDownloadFailure(string exactCode) => exactCode == "PUBLIC_SHARE_NETWORK_ERROR";

    /// <summary>Public sharing metadata only: one page and one root lookup, without redemption,
    /// OAuth, recursive enumeration, obtaining file URLs or opening an acquisition operation.</summary>
    internal static async Task ProbePublicRootAsync(string url, HttpClient client, CancellationToken ct)
    {
        var context = await ReadPublicContext(url, client, ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, context.RootMetadata());
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new CloudProviderException("SHAREPOINT_PUBLIC_ACCESS_REQUIRED");
        using var document = JsonDocument.Parse(await ReadBounded(response.Content, 2 * 1024 * 1024, ct));
        RequiredString(document.RootElement, "id");
        FolderCount(document.RootElement);
    }

    internal override void ValidateSource(CloudSource source)
    {
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || !uri.IdnHost.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(uri.AbsolutePath, @"^/:f:/[gs]/[^?#]+$"))
            throw new ArgumentException("Choose a public SharePoint folder sharing URL. Personal OneDrive and login-only links require another method.");
        // A parsed access code is not proof that the page requires one. Attempt anonymous public
        // access first; only the actual public-token response decides whether login/manual access
        // is required. No code is submitted and no password or authentication flow is executed.
    }

    internal override async Task<PublicShareResolution> ResolveAsync(CloudSource source, HttpClient client, CancellationToken ct)
    {
        ValidateSource(source);
        var context = await ReadPublicContext(source.Url, client, ct);
        using var rootResponse = await GetJson(client, context.RootMetadata(), ct);
        var root = rootResponse.RootElement;
        var rootId = RequiredString(root, "id");
        var expectedRoot = FolderCount(root);
        var files = new List<PublicShareFile>();
        var links = new Dictionary<string, Uri>(StringComparer.Ordinal);
        var seenIds = new HashSet<string>(StringComparer.Ordinal) {rootId};
        await Visit(rootId, "", expectedRoot, 0);
        ValidateManifest(files);
        return new(files, async (file, refresh, token) =>
        {
            if (!refresh && links.TryGetValue(file.Id, out var existing)) return existing;
            // Temporary download URLs and page tokens both expire. Refresh from the original
            // public share, then verify the same exact original file before handing it to HTTP.
            if (refresh) context = await ReadPublicContext(source.Url, client, token);
            using var response = await GetJson(client, context.Item(file.Id), token);
            var item = response.RootElement;
            var prefix = file.Path[..Math.Max(0, file.Path.LastIndexOf('/') + 1)];
            SameOriginal(file, ReadFile(item, prefix));
            return DownloadUrl(item);
        });

        async Task Visit(string id, string prefix, long? expected, int depth)
        {
            if (depth > 32) throw new CloudProviderException("SharePoint folder recursion limit exceeded; the manifest is incomplete.");
            var firstPage = context.Children(id);
            Uri? page = firstPage;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            long count = 0;
            while (page != null)
            {
                if (visited.Count >= 10001 || !visited.Add(page.AbsoluteUri))
                    throw new CloudProviderException("SharePoint repeated or exceeded its listing pages; the manifest is incomplete.");
                // A nextLink carries the public drive token. It must remain on this exact
                // collection and origin; never send it to another host or arbitrary API route.
                if (page.Scheme != firstPage.Scheme || page.Authority != firstPage.Authority || page.AbsolutePath != firstPage.AbsolutePath)
                    throw new CloudProviderException("SharePoint returned an unsafe pagination URL.");
                using var response = await GetJson(client, page, ct);
                var data = response.RootElement;
                if (!data.TryGetProperty("value", out var items) || items.ValueKind != JsonValueKind.Array)
                    throw new CloudProviderException("SharePoint did not provide a complete public-folder listing; login may be required.");
                if (data.TryGetProperty("@odata.count", out _))
                {
                    var declared = Size(data, "@odata.count");
                    if (expected != null && expected != declared) throw new CloudProviderException("SharePoint folder contents changed during discovery.");
                    expected = declared;
                }
                foreach (var item in items.EnumerateArray())
                {
                    var itemId = RequiredString(item, "id");
                    if (!seenIds.Add(itemId) || seenIds.Count > 100000) throw new CloudProviderException("SharePoint returned duplicate or excessive entries.");
                    if (item.TryGetProperty("folder", out var folder) && folder.ValueKind == JsonValueKind.Object)
                        await Visit(itemId, prefix + SafeSegment(RequiredString(item, "name")) + "/", FolderCount(item), depth + 1);
                    else if (item.TryGetProperty("file", out var file) && file.ValueKind == JsonValueKind.Object)
                    {
                        var original = ReadFile(item, prefix);
                        files.Add(original);
                        links[itemId] = DownloadUrl(item);
                    }
                    else throw new CloudProviderException("A SharePoint entry is unavailable or unsupported; the manifest is incomplete.");
                    count++;
                }
                var next = Text(data, "@odata.nextLink");
                if (string.IsNullOrEmpty(next)) page = null;
                else if (items.GetArrayLength() == 0 || !Uri.TryCreate(next, UriKind.Absolute, out page))
                    throw new CloudProviderException("SharePoint pagination made no progress or returned an invalid cursor.");
            }
            if (expected != null && count != expected) throw new CloudProviderException("SharePoint returned fewer entries than its folder metadata declared.");
        }
    }

    private static long? FolderCount(JsonElement item)
    {
        if (!item.TryGetProperty("folder", out var folder) || folder.ValueKind != JsonValueKind.Object)
            throw new CloudProviderException("The SharePoint link does not expose a public folder; login or manual access may be required.");
        return folder.TryGetProperty("childCount", out _) ? Size(folder, "childCount") : null;
    }

    private static PublicShareFile ReadFile(JsonElement item, string prefix)
    {
        if (!item.TryGetProperty("file", out var file) || file.ValueKind != JsonValueKind.Object)
            throw new CloudProviderException("SharePoint did not return original file metadata.");
        string? sha256 = null;
        if (file.TryGetProperty("hashes", out var hashes)) sha256 = Text(hashes, "sha256Hash");
        return new(RequiredString(item, "id"), prefix + SafeSegment(RequiredString(item, "name")), Size(item, "size"),
            Text(item, "eTag") ?? Text(item, "cTag") ?? Text(item, "lastModifiedDateTime"), sha256);
    }

    private static Uri DownloadUrl(JsonElement item)
    {
        var value = Text(item, "@content.downloadUrlNoAuth") ?? Text(item, "@microsoft.graph.downloadUrl") ?? Text(item, "@content.downloadUrl");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !PublicShareDownloadClients.IsProviderHost("oneDrive", uri))
            throw new CloudProviderException("SharePoint did not expose a supported public original-file URL. Login or manual download may be required.");
        return uri;
    }

    private static async Task<PublicContext> ReadPublicContext(string shareUrl, HttpClient client, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var response = await client.GetAsync(shareUrl, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode) throw new CloudProviderException("SHAREPOINT_PUBLIC_ACCESS_REQUIRED: check that this folder is shared publicly without login or a password.");
        var final = response.RequestMessage?.RequestUri ?? new Uri(shareUrl);
        var page = Encoding.UTF8.GetString(await ReadBounded(response.Content, 8 * 1024 * 1024, deadline.Token));
        var drive = Field(".driveUrl").TrimEnd('/');
        var token = Field(".driveAccessToken").TrimStart('?');
        var library = Field("listUrl").TrimEnd('/');
        var query = QueryHelpers.ParseQuery(final.Query);
        var path = query.TryGetValue("id", out var selected) ? selected.ToString() : "";
        if (!Uri.TryCreate(drive, UriKind.Absolute, out var driveUri) || driveUri.Scheme != "https" ||
            driveUri.Authority != final.Authority || driveUri.UserInfo.Length != 0 ||
            !Regex.IsMatch(driveUri.AbsolutePath, @"/_api/v2\.[01]/drives/[^/]+$", RegexOptions.IgnoreCase) ||
            !path.StartsWith(library + "/", StringComparison.OrdinalIgnoreCase) || token.Contains('#'))
            throw new CloudProviderException("The public SharePoint page uses an unsupported folder format. Login or manual access may be required.");
        var relative = path[(library.Length + 1)..];
        if (relative.Length == 0 || relative.Split('/').Any(p => p is "" or "." or ".." || p.Contains('\\') || p.Contains('\0')))
            throw new CloudProviderException("SharePoint returned an unsafe shared-folder path.");
        return new(drive, token, relative);

        string Field(string name)
        {
            var match = Regex.Match(page, "\\\"" + Regex.Escape(name) + "\\\"\\s*:\\s*(\\\"(?:[^\\\"\\\\]|\\\\.)*\\\")", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            try
            {
                if (match.Success && JsonSerializer.Deserialize<string>(match.Groups[1].Value) is {Length: > 0 and <= 16384} value) return value;
            }
            catch (JsonException) { }
            throw new CloudProviderException("SHAREPOINT_PUBLIC_ACCESS_REQUIRED: the page does not expose a public-folder token. It may require login, a password, or manual download.");
        }
    }

    private sealed record PublicContext(string Drive, string Token, string Folder)
    {
        private string Query => "?" + Token;
        private string Id(string id) => Uri.EscapeDataString(id);
        public Uri RootMetadata() => new(Drive + "/root:/" + string.Join('/', Folder.Split('/').Select(Uri.EscapeDataString)) + Query);
        public Uri Children(string id) => new(Drive + "/items/" + Id(id) + "/children" + Query);
        public Uri Item(string id) => new(Drive + "/items/" + Id(id) + Query);
    }
}
