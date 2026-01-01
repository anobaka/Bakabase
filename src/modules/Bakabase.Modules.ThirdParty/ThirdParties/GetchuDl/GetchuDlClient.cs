using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.GetchuDl.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.GetchuDl;

public class GetchuDlClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<GetchuDlVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            var cookies = new Dictionary<string, string> { { "adult_check_flag", "1" } };
            string? realUrl = appointUrl;

            if (string.IsNullOrWhiteSpace(realUrl) && (number.Contains("DLID", StringComparison.OrdinalIgnoreCase) || number.Contains("ITEM", StringComparison.OrdinalIgnoreCase) || number.Contains("GETCHU", StringComparison.OrdinalIgnoreCase)))
            {
                var id = Regex.Match(number, @"\d+").Value;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    realUrl = $"https://dl.getchu.com/i/item{id}";
                }
            }

            string searchUrl = "";
            if (string.IsNullOrWhiteSpace(realUrl))
            {
                // Fallback simple search (encoding nuances in python omitted here)
                searchUrl = $"https://dl.getchu.com/search/search_list.php?dojin=1&search_category_id=&search_keyword={Uri.EscapeDataString(number)}&btnWordSearch=%B8%A1%BA%F7&action=search&set_category_flag=1";
                var htmlSearch = await HttpClient.GetStringAsync(searchUrl);
                var docSearch = new CQ(htmlSearch);
                var rows = docSearch.Select("table tr td[valign='top']:not([align]) div a");
                foreach (var a in rows)
                {
                    var href = a.GetAttribute("href") ?? "";
                    var tmpTitle = a.Cq().Text();
                    if (!string.IsNullOrWhiteSpace(href) && href.Contains("/item") && tmpTitle.StartsWith(number))
                    {
                        realUrl = href;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(realUrl)) return null;

            var html = await HttpClient.GetStringAsync(realUrl);
            var doc = new CQ(html);

            var title = doc.Select("meta[property='og:title']").Attr("content") ?? "";
            if (string.IsNullOrWhiteSpace(title)) return null;

            var cover = doc.Select("meta[property='og:image']").Attr("content") ?? "";
            var studio = doc.Select("td:contains('サークル')").Next().Text().Trim();
            var release = doc.Select("td:contains('配信開始日')").Next().Text().Trim().Replace("/", "-");
            var year = Regex.Match(release ?? string.Empty, @"\d{4}").Value;
            var author = doc.Select("td:contains('作者')").Next().Text().Trim();
            var runtimeRaw = doc.Select("td:contains('画像数&ページ数')").Next().Text();
            var runtimeMatch = Regex.Match(runtimeRaw ?? string.Empty, @"(\d+)");
            var runtime = runtimeMatch.Success ? runtimeMatch.Groups[1].Value : "";
            var tag = string.Join(",", doc.Select("td:contains('趣向')").Next().Find("a").Select(a => a.Cq().Text().Trim()));
            var outline = doc.Select("td:contains('作品内容')").Next().Text().Trim();
            var extrafanart = doc.Select("a.highslide[href*='/data/item_img/']").Select(a => "https://dl.getchu.com" + (a.GetAttribute("href") ?? "")).ToList();

            var idNum = Regex.Match(realUrl, @"\d+").Value;
            var finalNumber = string.IsNullOrWhiteSpace(idNum) ? number : $"DLID-{idNum}";

            var detail = new GetchuDlVideoDetail
            {
                Number = finalNumber,
                Title = title,
                OriginalTitle = title,
                Actor = "",
                Tag = tag,
                Release = release,
                Year = year,
                Runtime = runtime,
                Series = "",
                Studio = studio,
                Publisher = "",
                CoverUrl = cover,
                PosterUrl = cover,
                Website = realUrl,
                Source = "dl_getchu",
                Mosaic = "同人",
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


