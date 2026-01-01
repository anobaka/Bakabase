using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Fantastica.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Fantastica;

public class FantasticaClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<FantasticaVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            string? realUrl = appointUrl;
            string poster = "";
            bool imageDownload = false;
            string? searchUrl = null;

            if (string.IsNullOrWhiteSpace(realUrl))
            {
                searchUrl = $"http://fantastica-vr.com/items/search?q={Uri.EscapeDataString(number)}";
                var htmlSearch = await HttpClient.GetStringAsync(searchUrl);
                var docSearch = new CQ(htmlSearch);

                // find first matching item in search list
                var items = docSearch.Select("section.item_search.item_list.clearfix div ul li a");
                foreach (var a in items)
                {
                    var href = a.GetAttribute("href") ?? "";
                    var img = a.Cq().Find("img").FirstOrDefault();
                    var src = img?.GetAttribute("src") ?? "";
                    if (href.Contains(number.Replace("-", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        realUrl = href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : $"http://fantastica-vr.com{href}";
                        poster = src;
                        break;
                    }
                }

                imageDownload = true; // align with python logic
            }

            if (string.IsNullOrWhiteSpace(realUrl))
            {
                return null;
            }

            var htmlDetail = await HttpClient.GetStringAsync(realUrl);
            var doc = new CQ(htmlDetail);

            string title = doc.Select("div.title-area h2").Text().Trim();
            if (string.IsNullOrWhiteSpace(title)) return null;

            // Outline
            var outline = doc.Select("p.explain").Text().Trim();

            // Actor
            var actorNodes = doc.Select("th:contains('出演者')").Parent().Find("td").Text();
            var actor = string.Join(",", Regex.Split(actorNodes ?? "", @"\s+").Where(s => !string.IsNullOrWhiteSpace(s)));

            // Cover
            var cover = doc.Select("div.vr_wrapper.clearfix div.img img").Attr("src") ?? "";
            if (cover == "https://assets.fantastica-vr.com/assets/common/img/dummy_large_white.jpg") cover = "";

            // Release
            var releaseRaw = doc.Select("th:contains('発売日')").Parent().Find("td").Text().Trim();
            var release = releaseRaw.Replace("年", "-").Replace("月", "-").Replace("日", "");
            var yearMatch = Regex.Match(release ?? "", @"(\d{4})");
            var year = yearMatch.Success ? yearMatch.Groups[1].Value : "";

            // Runtime
            var runtimeRaw = doc.Select("th:contains('収録時間')").Parent().Find("td").Text();
            var runtimeMatch = Regex.Match(runtimeRaw ?? "", @"(\d+)");
            var runtime = runtimeMatch.Success ? runtimeMatch.Groups[1].Value : "";

            // Series
            var series = doc.Select("span:contains('シリーズ')").Parent().Text().Replace("シリーズ", "").Trim();

            // Tags
            var tags = string.Join(",", doc.Select("th:contains('ジャンル')").Parent().Find("a").Select(a => a.Cq().Text().Trim()).Where(t => !string.IsNullOrWhiteSpace(t)));

            // Extrafanart
            var extrafanart = doc.Select("div.vr_images.clearfix div.vr_image a").Select(a => a.GetAttribute("href")).Where(h => !string.IsNullOrWhiteSpace(h)).ToList();

            // Poster fallback: choose first landscape image
            if (string.IsNullOrWhiteSpace(poster) && extrafanart.Count > 0)
            {
                // No real size check without downloading; keep first as heuristic
                poster = extrafanart[0];
                imageDownload = true;
            }

            var detail = new FantasticaVideoDetail
            {
                Number = number,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tags,
                Release = release,
                Year = year,
                Runtime = runtime,
                Series = series,
                Studio = "ファンタスティカ",
                Publisher = "ファンタスティカ",
                CoverUrl = cover,
                PosterUrl = poster,
                Website = realUrl,
                Source = "fantastica",
                Mosaic = "有码",
                SearchUrl = searchUrl
            };

            return detail;
        }
        catch
        {
            return null;
        }
    }
}


