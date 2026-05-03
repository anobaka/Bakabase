using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Helpers;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite.Models;
using Bootstrap.Extensions;
using CsQuery;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Bakabase.Modules.ThirdParty.ThirdParties.DLsite;

/// <summary>
/// Thrown when DLsite returns 401/403, indicating an authentication issue.
/// </summary>
public class DLsiteAuthException(string message) : Exception(message);

/// <summary>
/// Thrown when a download URL has expired and needs to be re-resolved.
/// </summary>
public class DLsiteDownloadLinkExpiredException(string message) : Exception(message);

public class DLsiteClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.DLsite;

    /// <summary>
    /// Detail page URL — the category segment must match the work id prefix, otherwise
    /// DLsite issues a 30x redirect that the handler does not auto-follow
    /// (<see cref="DLsiteHttpMessageHandler{TDLsiteOptions}"/> sets AllowAutoRedirect=false
    /// for the manual download flow).
    /// </summary>
    private const string WorkDetailUrlTemplate = "https://www.dlsite.com/{0}/work/=/product_id/{1}.html";

    private const string InfoJsonUrlTemplate = "https://www.dlsite.com/{0}/product/info/ajax?product_id={1}&cdn_cache_min=1";

    /// <summary>
    /// Cookie that bypasses DLsite's age-verification interlude for R-18 works. Without
    /// it, R-18 detail pages render an "are you 18+?" interstitial that has none of the
    /// expected selectors (<c>#work_name</c>, <c>.product-slider-data</c>), producing
    /// an empty <see cref="DLsiteProductDetail"/>.
    /// </summary>
    private const string AgeBypassCookie = "adultchecked=1; locale=ja-jp";

    private const int MaxRedirectHops = 5;

    // Play API endpoints
    private const string PlayApiContentCount = "https://play.dlsite.com/api/v3/content/count";
    private const string PlayApiContentSales = "https://play.dlsite.com/api/v3/content/sales";
    private const string PlayApiContentWorks = "https://play.dlsite.com/api/v3/content/works";
    private const int PlayApiBatchSize = 100;

    // Separate HttpClient for Play API (no auto cookie injection from handler)
    private HttpClient? _playHttpClient;
    private HttpClient PlayHttpClient => _playHttpClient ??= httpClientFactory.CreateClient(InternalOptions.HttpClientNames.Default);

    /// <summary>
    /// Maps a DLsite work id prefix to the category segment used in its URL.
    /// </summary>
    /// <remarks>
    /// Hitting the wrong category yields a 30x redirect (which the production handler
    /// does not auto-follow, so the response body is empty) or in some cases a
    /// non-canonical page that lacks the expected selectors. Routing up front avoids
    /// both failure modes.
    /// </remarks>
    public static string GetCategoryByWorkId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length < 2) return "home";
        var prefix = id[..2].ToUpperInvariant();
        return prefix switch
        {
            "RJ" => "maniax",   // R-18 doujin
            "VJ" => "pro",      // Commercial games
            "BJ" => "books",    // Commercial books
            _ => "home"
        };
    }

    /// <summary>
    /// Fetches and parses a DLsite work detail page.
    /// </summary>
    /// <param name="id">Work id, e.g. RJ01248749 / BJxxxxxx / VJxxxxxx.</param>
    /// <returns>
    /// Parsed detail, or <c>null</c> when the work doesn't exist, the page has been
    /// redirected away from a canonical detail URL, or the page rendered without the
    /// expected selectors (e.g. served behind an unbypassed age gate).
    /// </returns>
    public async Task<DLsiteProductDetail?> ParseWorkDetailById(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var category = GetCategoryByWorkId(id);
        var url = string.Format(WorkDetailUrlTemplate, category, id);

        var rsp = await SendDetailRequestAsync(url);
        try
        {
            for (var hops = 0; hops < MaxRedirectHops && IsRedirect(rsp); hops++)
            {
                var location = rsp.Headers.Location;
                if (location == null) return null;
                var nextUrl = location.IsAbsoluteUri
                    ? location.AbsoluteUri
                    : new Uri(new Uri(url), location).AbsoluteUri;
                // Anything that leaves the canonical detail URL shape (e.g. landing on
                // a category index or search page) means the work id doesn't exist
                // under any category.
                if (!nextUrl.Contains("/work/=/product_id/", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                rsp.Dispose();
                url = nextUrl;
                rsp = await SendDetailRequestAsync(url);
            }

            if (rsp.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            rsp.EnsureSuccessStatusCode();

            var html = await rsp.Content.ReadAsStringAsync();
            var cq = new CQ(html);

            var name = cq["#work_name"].Text().Trim();
            if (string.IsNullOrEmpty(name))
            {
                // No #work_name on the response means we either hit an age-verification
                // interlude or an unrelated page. Returning null lets callers retry
                // rather than caching an empty shell.
                return null;
            }

            var detail = new DLsiteProductDetail
            {
                Name = name,
                Introduction = cq[".work_parts_container"].Html(),
            };

            if (detail.Introduction.IsNotEmpty())
            {
                detail.Introduction = StringHelpers.MinifyHtml(WebUtility.HtmlDecode(detail.Introduction));
            }

            var coverUrls = cq[".product-slider-data"].Children().Select(x => x.Cq().Data<string>("src"))
                .Where(x => x.IsNotEmpty()).ToList();
            if (coverUrls.Any())
            {
                detail.CoverUrls = coverUrls.Select(c => c.AddSchemaSafely()).ToArray();
            }

            var properties = cq["#work_maker>tbody>tr,#work_outline>tbody>tr"].Select(t => t.Cq())
                .ToDictionary(t => t.Children("th").Text().Trim(),
                    t =>
                    {
                        var list = t.Children("td").Children();
                        if (list == null)
                        {
                            return null;
                        }

                        var data = new List<object>();
                        foreach (var item in list)
                        {
                            if (!item.ClassName.Contains("btn_follow"))
                            {
                                var itemCq = item.Cq();
                                var links = itemCq.Children("a");
                                if (links?.Length > 0)
                                {
                                    foreach (var link in links)
                                    {
                                        data.Add(link.Cq().Text().Trim());
                                    }
                                }
                                else
                                {
                                    data.Add(itemCq.Text().Trim());
                                }
                            }
                        }

                        return data.Select(d => d.ToString()!).Where(x => x.IsNotEmpty()).ToList();
                    }).Where(x => x.Value?.Any() == true).ToDictionary(d => d.Key, d => d.Value!);
            detail.PropertiesOnTheRightSideOfCover = properties;

            var infoUrl = string.Format(InfoJsonUrlTemplate, category, id);
            try
            {
                using var infoReq = new HttpRequestMessage(HttpMethod.Get, infoUrl);
                infoReq.Headers.Add("Cookie", AgeBypassCookie);
                using var infoRsp = await HttpClient.SendAsync(infoReq);
                if (infoRsp.IsSuccessStatusCode)
                {
                    var productInfoStr = await infoRsp.Content.ReadAsStringAsync();
                    var productInfo = JsonConvert.DeserializeObject<Dictionary<string, DLsiteProductInfo>>(productInfoStr);
                    var rating = productInfo?.GetValueOrDefault(id)?.RateAverage2Dp;
                    if (rating.HasValue)
                    {
                        detail.Rating = rating.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                // Rating is best-effort; swallow so a missing/changed ajax endpoint
                // doesn't fail the whole parse.
                Logger.LogWarning(ex, "Failed to fetch DLsite rating for {WorkId}", id);
            }

            return detail;
        }
        finally
        {
            rsp.Dispose();
        }
    }

    /// <summary>
    /// Sends a GET to a DLsite public page with the age-bypass cookie set inline.
    /// Setting the Cookie header here pre-empts the handler's automatic cookie
    /// injection — acceptable for public detail/info pages, since user auth cookies
    /// only matter for the play.dlsite.com Play API.
    /// </summary>
    private async Task<HttpResponseMessage> SendDetailRequestAsync(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Cookie", AgeBypassCookie);
        return await HttpClient.SendAsync(req);
    }

    #region Play API

    /// <summary>
    /// Get the total count of purchased works from DLsite Play.
    /// </summary>
    public async Task<int> GetPurchaseCountAsync(string cookie, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{PlayApiContentCount}?last=0");
        SetPlayApiHeaders(request, cookie);

        var response = await PlayHttpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<DLsitePlayContentCount>(cancellationToken: ct);
        return data?.User ?? 0;
    }

    /// <summary>
    /// Get all purchased work IDs and their sales dates.
    /// </summary>
    public async Task<List<DLsitePlaySaleItem>> GetPurchaseSalesAsync(string cookie, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{PlayApiContentSales}?last=0");
        SetPlayApiHeaders(request, cookie);

        var response = await PlayHttpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<List<DLsitePlaySaleItem>>(cancellationToken: ct);
        return items ?? [];
    }

    /// <summary>
    /// Get work details for a batch of work IDs (max 100 per request).
    /// </summary>
    public async Task<List<DLsitePlayWorkDetail>> GetPurchaseWorksAsync(string cookie, List<string> workIds, CancellationToken ct = default)
    {
        var allWorks = new List<DLsitePlayWorkDetail>();

        for (var i = 0; i < workIds.Count; i += PlayApiBatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = workIds.Skip(i).Take(PlayApiBatchSize).ToList();
            using var request = new HttpRequestMessage(HttpMethod.Post, PlayApiContentWorks);
            SetPlayApiHeaders(request, cookie);
            request.Content = JsonContent.Create(batch);

            var response = await PlayHttpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<DLsitePlayWorksResponse>(cancellationToken: ct);
            if (result?.Works != null)
            {
                allWorks.AddRange(result.Works);
            }
        }

        return allWorks;
    }

    private static void SetPlayApiHeaders(HttpRequestMessage request, string cookie)
    {
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("User-Agent", IThirdPartyHttpClientOptions.DefaultUserAgent);
        request.Headers.Add("Referer", "https://play.dlsite.com/");
        request.Headers.Add("Origin", "https://play.dlsite.com");
    }

    #endregion

    #region Download API

    private static readonly Regex DrmKeyPattern = new(@"\b[A-Z0-9-]{16,}\b", RegexOptions.Compiled);

    /// <summary>
    /// Sends a single GET request using the DLsite named HttpClient (with handler-managed
    /// throttling, proxy, and cookie container). Account key and cookie are passed via
    /// HttpRequestMessage.Options so the handler uses the correct per-account container.
    /// AllowAutoRedirect is disabled in DLsiteHttpMessageHandler.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        string cookie,
        string accountKey,
        string url,
        CancellationToken ct,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        Action<HttpRequestMessage>? configureRequest = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(ThirdPartyRequestOptions.AccountKey, accountKey);
        request.Options.Set(ThirdPartyRequestOptions.Cookie, cookie);
        configureRequest?.Invoke(request);

        return await HttpClient.SendAsync(request, completionOption, ct);
    }

    private static string ResolveLocation(HttpResponseMessage response, string currentUrl)
    {
        var location = response.Headers.Location
                       ?? throw new Exception($"Expected Location header in redirect from {currentUrl}");
        return location.IsAbsoluteUri
            ? location.AbsoluteUri
            : new Uri(new Uri(currentUrl), location).AbsoluteUri;
    }

    private static bool IsRedirect(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Redirect
            or HttpStatusCode.MovedPermanently
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// Resolves download info for a work: DRM key (if any), file names, and download URLs.
    /// Flow:
    ///   1. GET play API → 302
    ///   2. Follow redirect:
    ///     2.1 200 (HTML page): parse file names/URLs from HTML, resolve each file URL (302) to final download URL.
    ///         If redirect URL contains "/serial/", also extract DRM key.
    ///     2.2 302 (direct download): single file, extract filename from Location URL.
    /// </summary>
    public async Task<DLsiteDownloadInfo> ResolveDownloadAsync(string cookie, string workId, string accountKey = "default", CancellationToken ct = default)
    {
        var playUrl = $"https://play.dlsite.com/api/v3/download?workno={workId}";

        // Step 1: Hit the play download API (returns 302)
        using var a = await SendAsync(cookie, accountKey, playUrl, ct);
        if (a.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new DLsiteAuthException(
                $"Authentication failed ({a.StatusCode}). Please check if your DLsite cookie is correct.");
        }

        if (!IsRedirect(a))
        {
            throw new Exception($"Expected redirect from play API for {workId}, got {a.StatusCode}");
        }

        var redirectUrl = ResolveLocation(a, playUrl);
        var isSerialPage = redirectUrl.Contains("/serial/", StringComparison.OrdinalIgnoreCase);

        // Step 2: Follow the redirect
        using var c = await SendAsync(cookie, accountKey, redirectUrl, ct);

        // Case 2.2: Second response is also a redirect → direct single-file download
        if (IsRedirect(c))
        {
            var downloadUrl = ResolveLocation(c, redirectUrl);
            return new DLsiteDownloadInfo
            {
                Links =
                [
                    new DLsiteDownloadLink
                    {
                        Url = downloadUrl,
                        FileName = SanitizeFileName(ExtractFileNameFromUrl(downloadUrl) ?? $"{workId}.zip", $"{workId}.zip")
                    }
                ]
            };
        }

        // Case 2.1: HTML page with file list
        c.EnsureSuccessStatusCode();
        var html = await c.Content.ReadAsStringAsync(ct);
        var cq = new CQ(html);

        var info = new DLsiteDownloadInfo();

        // Extract DRM key if this is a serial page
        if (isSerialPage)
        {
            info.DrmKey = cq["strong"]
                .FirstOrDefault(x => DrmKeyPattern.IsMatch(x.InnerText))?.InnerText;
        }

        // Extract file names and URLs from HTML
        var fileNames = cq["td.work_content>p>strong"].Select(x => x.InnerText).ToArray();
        var fileUrls = cq["td.work_dl>div.work_download>a"].Select(x => x.GetAttribute("href")).ToArray();

        // Resolve each file URL (they are 302 redirects to actual download URLs)
        for (var i = 0; i < fileUrls.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            var fileUrl = fileUrls[i];
            if (fileUrl.StartsWith("//")) fileUrl = $"https:{fileUrl}";

            using var d = await SendAsync(cookie, accountKey, fileUrl, ct, HttpCompletionOption.ResponseHeadersRead);
            if (!IsRedirect(d))
            {
                throw new Exception($"Expected redirect for file download URL: {fileUrl}, got {d.StatusCode}");
            }

            var actualUrl = ResolveLocation(d, fileUrl);

            var fallbackName = $"{workId}_part{i + 1}";
            var rawFileName = i < fileNames.Length && !string.IsNullOrEmpty(fileNames[i])
                ? fileNames[i]
                : ExtractFileNameFromUrl(actualUrl) ?? fallbackName;

            info.Links.Add(new DLsiteDownloadLink
            {
                FileName = SanitizeFileName(rawFileName, fallbackName),
                Url = actualUrl
            });
        }

        return info;
    }

    /// <summary>
    /// Downloads a file with resume support using HTTP Range requests.
    /// Follows redirects manually to carry cookies across domains.
    /// </summary>
    public async Task DownloadFileAsync(
        string cookie,
        string url,
        string destinationPath,
        string accountKey = "default",
        Action<long, long>? onProgress = null,
        CancellationToken ct = default)
    {
        var tempPath = destinationPath + ".downloading";
        long existingLength = 0;
        var resumeFromFinalFile = false;

        // Check if final file already exists (previous complete download)
        if (File.Exists(destinationPath))
        {
            existingLength = new FileInfo(destinationPath).Length;
            resumeFromFinalFile = true;
        }
        else if (File.Exists(tempPath))
        {
            existingLength = new FileInfo(tempPath).Length;
        }

        // Follow redirects to reach the actual download endpoint
        var currentUrl = url;
        HttpResponseMessage response;
        while (true)
        {
            response = await SendAsync(cookie, accountKey, currentUrl, ct,
                HttpCompletionOption.ResponseHeadersRead,
                request =>
                {
                    if (existingLength > 0)
                    {
                        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);
                    }
                });

            if (IsRedirect(response))
            {
                currentUrl = ResolveLocation(response, currentUrl);
                response.Dispose();
                continue;
            }

            break;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // File is already fully downloaded
                if (File.Exists(tempPath) && !resumeFromFinalFile)
                {
                    File.Move(tempPath, destinationPath, true);
                }

                return;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // If this is a download.dlsite.com URL, likely the link has expired
                if (currentUrl.Contains("download.dlsite.com", StringComparison.OrdinalIgnoreCase))
                {
                    throw new DLsiteDownloadLinkExpiredException(
                        $"Download link expired ({response.StatusCode}): {currentUrl}");
                }

                throw new DLsiteAuthException(
                    $"Authentication failed ({response.StatusCode}). Please check if your DLsite cookie is correct.");
            }

            response.EnsureSuccessStatusCode();

            var totalLength = response.Content.Headers.ContentLength ?? 0;
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                totalLength += existingLength;
            }
            else
            {
                existingLength = 0;
                resumeFromFinalFile = false;
            }

            // If resuming from the final file, copy it to temp first for append
            if (resumeFromFinalFile && existingLength > 0)
            {
                File.Copy(destinationPath, tempPath, true);
            }

            var mode = existingLength > 0 ? FileMode.Append : FileMode.Create;
            await using var fileStream = new FileStream(tempPath, mode, FileAccess.Write, FileShare.None);
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);

            var buffer = new byte[81920];
            long totalRead = existingLength;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;
                onProgress?.Invoke(totalRead, totalLength);
            }

            await fileStream.FlushAsync(ct);
            fileStream.Close();
        }

        File.Move(tempPath, destinationPath, true);
    }

    /// <summary>
    /// Ensures a filename is valid for the file system.
    /// Handles cases where HTML parsing yields a URL instead of a filename.
    /// </summary>
    private static string SanitizeFileName(string name, string fallback)
    {
        // If the name looks like a URL, try to extract the filename from it
        if (name.Contains("://"))
        {
            name = ExtractFileNameFromUrl(name) ?? fallback;
        }

        // Strip any directory separators (e.g. from URL-encoded paths)
        var lastSep = name.LastIndexOfAny(['/', '\\']);
        if (lastSep >= 0 && lastSep < name.Length - 1)
        {
            name = name[(lastSep + 1)..];
        }

        // Remove invalid filename characters
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    /// <summary>
    /// Extracts filename from a DLsite download URL.
    /// URL pattern: .../file/RJ405582.zip/_/...
    /// </summary>
    private static string? ExtractFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split('/');
            var fileIndex = Array.IndexOf(segments, "file");
            if (fileIndex >= 0 && fileIndex + 1 < segments.Length)
            {
                var name = Uri.UnescapeDataString(segments[fileIndex + 1]);
                if (!string.IsNullOrEmpty(name) && name.Contains('.'))
                {
                    return name;
                }
            }

            // Fallback: last segment with an extension
            var lastWithExt = segments.LastOrDefault(s => s.Contains('.') && !s.StartsWith('?'));
            return lastWithExt != null ? Uri.UnescapeDataString(lastWithExt) : null;
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
