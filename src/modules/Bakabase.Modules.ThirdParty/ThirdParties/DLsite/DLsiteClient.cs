using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

public class DLsiteClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.DLsite;

    /// <summary>
    /// We can always use a random category (/books part) to get any detail page by id.
    /// </summary>
    private const string WorkDetailUrlTemplate = "https://www.dlsite.com/books/work/=/product_id/{0}.html";

    private const string InfoJsonUrlTemplate = "https://www.dlsite.com/books/product/info/ajax?product_id={0}&cdn_cache_min=1";

    // Play API endpoints
    private const string PlayApiContentCount = "https://play.dlsite.com/api/v3/content/count";
    private const string PlayApiContentSales = "https://play.dlsite.com/api/v3/content/sales";
    private const string PlayApiContentWorks = "https://play.dlsite.com/api/v3/content/works";
    private const int PlayApiBatchSize = 100;

    // Separate HttpClient for Play API (no auto cookie injection from handler)
    private HttpClient? _playHttpClient;
    private HttpClient PlayHttpClient => _playHttpClient ??= httpClientFactory.CreateClient(InternalOptions.HttpClientNames.Default);

    /// <summary>
    ///
    /// </summary>
    /// <param name="id">Something like RJxxxxxxx</param>
    /// <returns></returns>
    public async Task<DLsiteProductDetail?> ParseWorkDetailById(string id)
    {
        var url = string.Format(WorkDetailUrlTemplate, id);
        var rsp = await HttpClient.GetAsync(url);
        if (rsp.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var html = await rsp.Content.ReadAsStringAsync();
        var cq = new CQ(html);

        var detail = new DLsiteProductDetail
        {
            Name = cq["#work_name"].Text().Trim(),
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

        var productInfoStr = await HttpClient.GetStringAsync(string.Format(InfoJsonUrlTemplate, id));
        var productInfo = JsonConvert.DeserializeObject<Dictionary<string, DLsiteProductInfo>>(productInfoStr);
        var rating = productInfo?.GetValueOrDefault(id)?.RateAverage2Dp;
        if (rating.HasValue)
        {
            detail.Rating = rating.Value;
        }

        return detail;
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

    // HttpClient with auto-redirect disabled for manual redirect following with cookie forwarding
    private HttpClient? _noRedirectHttpClient;
    private HttpClient NoRedirectHttpClient => _noRedirectHttpClient ??= new HttpClient(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All
    });

    private const int MaxRedirects = 10;

    /// <summary>
    /// Sends a GET request following redirects manually, carrying cookie on every hop.
    /// Returns the final response (which may be HTML or binary).
    /// </summary>
    private async Task<HttpResponseMessage> SendWithCookieRedirectAsync(
        string url,
        string cookie,
        CancellationToken ct,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        Action<HttpRequestMessage>? configureRequest = null)
    {
        var currentUrl = url;
        // Accumulate cookies from Set-Cookie headers during redirect chain
        var cookieJar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in cookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eqIdx = part.IndexOf('=');
            if (eqIdx > 0)
            {
                cookieJar[part[..eqIdx].Trim()] = part[(eqIdx + 1)..].Trim();
            }
        }

        for (var i = 0; i < MaxRedirects; i++)
        {
            var cookieHeader = string.Join("; ", cookieJar.Select(kv => $"{kv.Key}={kv.Value}"));
            var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
            request.Headers.Add("Cookie", cookieHeader);
            request.Headers.Add("User-Agent", IThirdPartyHttpClientOptions.DefaultUserAgent);
            request.Headers.Add("Referer", "https://play.dlsite.com/");
            configureRequest?.Invoke(request);

            var response = await NoRedirectHttpClient.SendAsync(request, completionOption, ct);

            if (response.StatusCode is HttpStatusCode.Redirect
                or HttpStatusCode.MovedPermanently
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                if (location == null)
                {
                    throw new Exception($"Redirect response from {currentUrl} has no Location header");
                }

                // Merge Set-Cookie headers into the cookie jar
                if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                {
                    foreach (var sc in setCookies)
                    {
                        // Set-Cookie value format: name=value; path=...; domain=...
                        var cookiePart = sc.Split(';', 2)[0].Trim();
                        var eqIdx = cookiePart.IndexOf('=');
                        if (eqIdx > 0)
                        {
                            cookieJar[cookiePart[..eqIdx].Trim()] = cookiePart[(eqIdx + 1)..].Trim();
                        }
                    }
                }

                response.Dispose();
                currentUrl = location.IsAbsoluteUri
                    ? location.AbsoluteUri
                    : new Uri(new Uri(currentUrl), location).AbsoluteUri;
                continue;
            }

            return response;
        }

        throw new Exception($"Too many redirects (>{MaxRedirects}) starting from {url}");
    }

    /// <summary>
    /// Resolves download links for a work. Handles two DLsite flows:
    /// 1. Direct download: redirect chain ends at a binary file (application/octet-stream)
    /// 2. Serial page: redirect chain ends at an HTML page with split download links and optional DRM key
    /// </summary>
    public async Task<DLsiteDownloadInfo> GetDownloadInfoAsync(string cookie, string workId, CancellationToken ct = default)
    {
        // Use the Play download API with manual redirects so cookies are forwarded
        var url = $"https://play.dlsite.com/api/v3/download?workno={workId}";

        using var response = await SendWithCookieRedirectAsync(url, cookie, ct);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

        // Flow 1: Final response is a binary file (direct download)
        if (contentType.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("application/zip", StringComparison.OrdinalIgnoreCase))
        {
            var finalUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? url;
            var fileName = GetFileNameFromResponse(response, workId);
            return new DLsiteDownloadInfo
            {
                Links = [new DLsiteDownloadLink { Url = finalUrl, FileName = fileName }]
            };
        }

        // Flow 2: HTML page (serial/split download page)
        var html = await response.Content.ReadAsStringAsync(ct);
        return ParseSerialDownloadPage(html, workId);
    }

    /// <summary>
    /// Fetches the DRM key for a work from the serial download page (via Play API with DRM info).
    /// Returns null if the work has no DRM key, or the key string if found.
    /// Returns empty string if the page was reached but no key was present.
    /// </summary>
    public async Task<string?> GetDrmKeyAsync(string cookie, string workId, CancellationToken ct = default)
    {
        var url = $"https://play.dlsite.com/api/v3/download?workno={workId}";

        using var response = await SendWithCookieRedirectAsync(url, cookie, ct);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

        // Direct download - no DRM key
        if (!contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var html = await response.Content.ReadAsStringAsync(ct);
        var cq = new CQ(html);

        // DRM key is in: .table_inframe_box_fix table td > strong.color_02
        var keyElement = cq[".table_inframe_box_fix strong.color_02"];
        var key = keyElement.Text().Trim();

        return string.IsNullOrEmpty(key) ? string.Empty : key;
    }

    /// <summary>
    /// Parses the serial download HTML page for download links and DRM key.
    /// </summary>
    private DLsiteDownloadInfo ParseSerialDownloadPage(string html, string workId)
    {
        var cq = new CQ(html);
        var info = new DLsiteDownloadInfo();

        // Parse DRM key: .table_inframe_box_fix table td > strong.color_02
        var keyElement = cq[".table_inframe_box_fix strong.color_02"];
        var key = keyElement.Text().Trim();
        if (!string.IsNullOrEmpty(key))
        {
            info.DrmKey = key;
        }

        // Parse download links from #download_division_file table
        var rows = cq["#download_division_file table tr"].Select(r => r.Cq()).ToList();
        foreach (var row in rows)
        {
            // Skip header row (has th elements)
            if (row.Children("th").Length > 0) continue;

            var fileNameElement = row.Find("td.work_content strong");
            var linkElement = row.Find("td.work_dl a.btn_dl");

            if (linkElement.Length == 0) continue;

            var href = linkElement.Attr("href");
            if (string.IsNullOrEmpty(href)) continue;

            var fileName = fileNameElement.Text().Trim();
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = $"{workId}_part{info.Links.Count + 1}";
            }

            info.Links.Add(new DLsiteDownloadLink
            {
                Url = href.StartsWith("//") ? $"https:{href}" : href,
                FileName = fileName
            });
        }

        return info;
    }

    /// <summary>
    /// Extracts filename from Content-Disposition header or URL.
    /// </summary>
    private static string GetFileNameFromResponse(HttpResponseMessage response, string workId)
    {
        var contentDisposition = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        if (!string.IsNullOrEmpty(contentDisposition))
        {
            return contentDisposition;
        }

        var uri = response.RequestMessage?.RequestUri;
        if (uri != null)
        {
            var pathFileName = Path.GetFileName(uri.AbsolutePath);
            if (!string.IsNullOrEmpty(pathFileName) && pathFileName.Contains('.'))
            {
                return pathFileName;
            }
        }

        return $"{workId}.zip";
    }

    /// <summary>
    /// Downloads a file with resume support using HTTP Range requests.
    /// Uses manual redirect following to carry cookies across domains.
    /// </summary>
    public async Task DownloadFileAsync(
        string cookie,
        string url,
        string destinationPath,
        Action<long, long>? onProgress = null,
        CancellationToken ct = default)
    {
        var tempPath = destinationPath + ".downloading";
        long existingLength = 0;

        if (File.Exists(tempPath))
        {
            existingLength = new FileInfo(tempPath).Length;
        }

        using var response = await SendWithCookieRedirectAsync(url, cookie, ct,
            HttpCompletionOption.ResponseHeadersRead,
            request =>
            {
                if (existingLength > 0)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);
                }
            });

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // File already fully downloaded
            if (File.Exists(tempPath))
            {
                File.Move(tempPath, destinationPath, true);
            }
            return;
        }

        response.EnsureSuccessStatusCode();

        var totalLength = response.Content.Headers.ContentLength ?? 0;
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            totalLength += existingLength;
        }
        else
        {
            // Server doesn't support range requests, start from beginning
            existingLength = 0;
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

        File.Move(tempPath, destinationPath, true);
    }

    #endregion
}
