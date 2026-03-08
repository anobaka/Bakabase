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

            var works = await response.Content.ReadFromJsonAsync<List<DLsitePlayWorkDetail>>(cancellationToken: ct);
            if (works != null)
            {
                allWorks.AddRange(works);
            }
        }

        return allWorks;
    }

    private static void SetPlayApiHeaders(HttpRequestMessage request, string cookie)
    {
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("User-Agent", IThirdPartyHttpClientOptions.DefaultUserAgent);
        request.Headers.Add("Referer", "https://play.dlsite.com/");
    }

    #endregion
}
