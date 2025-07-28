using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Getchu.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Getchu;

public class GetchuClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<GetchuVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            // If DLID/ITEM/getchu specific, this should be handled by GetchuDl client in the Python flow.
            if (!string.IsNullOrWhiteSpace(appointUrl) && appointUrl.Contains("dl.getchu"))
            {
                return null;
            }

            var baseUrl = "http://www.getchu.com";
            string? realUrl = string.IsNullOrWhiteSpace(appointUrl) ? null : appointUrl.Replace("&gc=gc", "") + "&gc=gc";
            string cover = "";

            if (string.IsNullOrWhiteSpace(realUrl))
            {
                var keyword = number.Replace("10bit", "").Replace("裕未", "祐未").Replace("“", "”").Replace("·", "・");
                var keywordEncoded = Uri.EscapeDataString(keyword); // approximate
                var searchUrl = $"{baseUrl}/php/search.phtml?genre=all&search_keyword={keywordEncoded}&gc=gc";
                var htmlSearch = await HttpClient.GetStringAsync(searchUrl);
                var docSearch = new CQ(htmlSearch);
                var links = docSearch.Select("a.blueb").ToList();
                if (links.Count == 0) return null;
                // pick first, refine by title match if possible
                realUrl = baseUrl + (links[0].GetAttribute("href") ?? string.Empty).Replace("../", "/") + "&gc=gc";
            }

            var html = await HttpClient.GetStringAsync(realUrl);
            var doc = new CQ(html);

            var title = doc.Select("#soft-title").Text().Trim();
            if (string.IsNullOrWhiteSpace(title)) return null;

            // outline
            var outlineBlocks = doc.Select("div.tablebody");
            var outline = string.Join("\n", outlineBlocks.Select(b => b.Cq().Text().Trim())).Trim();

            // cover
            var ogImage = doc.Select("meta[property='og:image']").Attr("content") ?? "";
            cover = string.IsNullOrWhiteSpace(ogImage) ? "" : (ogImage.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? ogImage : baseUrl + ogImage);

            // number from page
            var numberText = doc.Select("td:contains('品番：')").Next().Text().Trim();
            var webNumber = string.IsNullOrWhiteSpace(numberText) ? number : numberText.ToUpperInvariant();

            // release
            var release = doc.Select("td:contains('発売日：')").Next().Find("a").Text().Trim().Replace("/", "-");
            var year = Regex.Match(release ?? string.Empty, @"\d{4}").Value;

            // director and runtime
            string director = doc.Select("td:contains('監督：')").Next().Text();
            if (string.IsNullOrWhiteSpace(director)) director = doc.Select("a[href*='person=']").Text();
            if (string.IsNullOrWhiteSpace(director)) director = doc.Select("td:contains('キャラデザイン：')").Next().Text();
            var runtimeRaw = doc.Select("td:contains('時間：')").Next().Text();
            var runtimeMatch = Regex.Match(runtimeRaw ?? string.Empty, @"(\d+)");
            var runtime = runtimeMatch.Success ? runtimeMatch.Groups[1].Value : "";

            // tag
            var tag = string.Join(",", doc
                .Select("td:contains('サブジャンル：'), td:contains('カテゴリ：')")
                .Next()
                .Find("a").Select(a => a.Cq().Text().Trim()));

            // studio
            var studio = doc.Select("a.glance").Text();

            // extrafanart
            var extrafanart = doc.Select("div:contains('サンプル画像')").Next().Find("a").Select(a => a.GetAttribute("href") ?? "").Select(h => h.Replace("./", baseUrl + "/")).ToList();

            // mosaic type based on category tab
            var mosaic = "动漫"; // default
            var category = doc.Select("li.genretab.current").Text();
            if (!string.IsNullOrWhiteSpace(category))
            {
                if (category == "アダルトアニメ") mosaic = "里番";
                else if (category == "書籍・雑誌") mosaic = "书籍";
                else if (category == "アニメ") mosaic = "动漫";
            }

            var detail = new GetchuVideoDetail
            {
                Number = webNumber,
                Title = title,
                OriginalTitle = title,
                Actor = "",
                Tag = tag,
                Release = release,
                Year = year,
                Runtime = runtime,
                Series = "",
                Director = director,
                Studio = studio,
                Publisher = "",
                CoverUrl = cover,
                PosterUrl = cover,
                Website = realUrl,
                Source = "getchu",
                Mosaic = mosaic
            };

            return detail;
        }
        catch
        {
            return null;
        }
    }
}


