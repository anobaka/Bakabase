using System.Net;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite.Models;
using Bootstrap.Extensions;
using CsQuery;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

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
            detail.Introduction = WebUtility.HtmlDecode(detail.Introduction);
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
}