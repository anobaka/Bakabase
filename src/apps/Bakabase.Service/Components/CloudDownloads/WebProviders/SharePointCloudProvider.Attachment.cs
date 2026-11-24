using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;
using Microsoft.AspNetCore.WebUtilities;

namespace Bakabase.Service.Components.CloudDownloads.WebProviders;

public sealed partial class SharePointCloudProvider
{
    internal static bool SupportsPublicLink(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && PublicShareDownloadClients.IsProviderHost("oneDrive", uri) &&
        uri.IdnHost.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase) &&
        (Regex.IsMatch(uri.AbsolutePath, @"\A/:f:/[gs]/[^?#]+\z") || IsAttachment(uri));

    internal static bool IsAttachment(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsAttachment(uri);

    private static bool IsAttachment(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            uri.Fragment.Length != 0 || uri.Query.Length > 4096 ||
            !Regex.IsMatch(uri.IdnHost, @"\A[a-z0-9][a-z0-9-]*-my\.sharepoint\.com\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(uri.AbsolutePath, @"\A/personal/[a-z0-9_.-]+/_layouts/[0-9]{1,3}/download\.aspx\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;
        var query = QueryHelpers.ParseQuery(uri.Query);
        return query.Count == 1 && query.TryGetValue("share", out var shares) && shares.Count == 1 &&
            shares[0] is {Length: > 0} share && Regex.IsMatch(share, @"\A[A-Za-z0-9_-]+\z");
    }

    internal static void RequireAttachmentRoute(Uri original, Uri current)
    {
        if (!IsAttachment(original) || current.Scheme != Uri.UriSchemeHttps || !current.IsDefaultPort ||
            current.UserInfo.Length != 0 || current.Fragment.Length != 0)
            throw new CloudProviderException("SHAREPOINT_ATTACHMENT_REDIRECT_UNSUPPORTED");
        if (current.IdnHost is "login.microsoftonline.com" or "login.live.com" ||
            current.IdnHost == original.IdnHost && Regex.IsMatch(current.AbsolutePath,
                @"/_layouts/[0-9]{1,3}/(?:authenticate|accessdenied)\.aspx\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new CloudProviderException("SHAREPOINT_PUBLIC_ACCESS_REQUIRED");
        // A different tenant is an independent origin. Only original-file CDN endpoints
        // already permitted by the public Microsoft transport may receive a cross-host request.
        if (current.IdnHost != original.IdnHost && current.IdnHost != "publiccdn.sharepointonline.com" &&
            !current.IdnHost.EndsWith(".files.1drv.com", StringComparison.OrdinalIgnoreCase))
            throw new CloudProviderException("SHAREPOINT_ATTACHMENT_REDIRECT_UNSUPPORTED");
    }

    private static async Task<PublicShareResolution> ResolveAttachmentAsync(Uri source, HttpClient client, CancellationToken ct)
    {
        var file = await ReadAttachmentAsync(source, client, ct);
        return new([file], async (expected, refresh, token) =>
        {
            if (refresh) SameOriginal(expected, await ReadAttachmentAsync(source, client, token));
            return source; // HTTP retries, checkpoints and restarts all begin at the durable sharing URL.
        }, (expected, response) => SameOriginal(expected, AttachmentFromHeaders(source, response)));
    }

    internal static async Task<PublicShareFile> ReadAttachmentAsync(Uri source, HttpClient client, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var head = new HttpRequestMessage(HttpMethod.Head, source);
        head.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        using var response = await client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        return AttachmentFromHeaders(source, response);
    }

    private static PublicShareFile AttachmentFromHeaders(Uri source, HttpResponseMessage response)
    {
        var endpoint = response.RequestMessage?.RequestUri ?? source;
        RequireAttachmentRoute(source, endpoint);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new CloudProviderException("SHAREPOINT_PUBLIC_ACCESS_REQUIRED");
        if (response.StatusCode == HttpStatusCode.Gone)
            throw new CloudProviderException(endpoint == source ? "SHAREPOINT_ATTACHMENT_REMOVED" : "SHAREPOINT_ATTACHMENT_UNAVAILABLE");
        if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            throw new CloudProviderException("PUBLIC_SHARE_NETWORK_ERROR");
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
            throw new CloudProviderException("SHAREPOINT_ATTACHMENT_UNAVAILABLE");

        var headers = response.Content.Headers;
        var size = response.StatusCode == HttpStatusCode.PartialContent ? headers.ContentRange?.Length : headers.ContentLength;
        var disposition = headers.ContentDisposition;
        var media = headers.ContentType?.MediaType;
        if (!string.Equals(disposition?.DispositionType, "attachment", StringComparison.OrdinalIgnoreCase) ||
            (disposition.FileNameStar ?? disposition.FileName?.Trim('"')) is not {Length: > 0} name ||
            size is not >= 0 || headers.ContentEncoding.Any(e => !e.Equals("identity", StringComparison.OrdinalIgnoreCase)) ||
            media == null || !media.StartsWith("application/", StringComparison.OrdinalIgnoreCase) ||
            media.EndsWith("xml", StringComparison.OrdinalIgnoreCase) || media.EndsWith("json", StringComparison.OrdinalIgnoreCase) ||
            media.Contains("html", StringComparison.OrdinalIgnoreCase))
            throw new CloudProviderException("SHAREPOINT_ATTACHMENT_RESPONSE_UNRECOGNIZED");

        var version = response.Headers.ETag is {IsWeak: false} tag ? tag.ToString() : headers.LastModified?.ToString("R");
        if (version == null) throw new CloudProviderException("SHAREPOINT_ATTACHMENT_VERSION_UNAVAILABLE");
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.AbsoluteUri)));
        return new(id, SafeSegment(name), size.Value, version);
    }
}
