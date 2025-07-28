using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Giga.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Giga;

public class GigaClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<GigaVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            string? realUrl = appointUrl;
            if (string.IsNullOrWhiteSpace(realUrl))
            {
                var searchUrl = $"https://www.giga-web.jp/search/?keyword={Uri.EscapeDataString(number)}";
                var htmlSearch = await HttpClient.GetStringAsync(searchUrl);
                if (htmlSearch.Contains("/cookie_set.php"))
                {
                    // prime cookies then retry
                    try { await HttpClient.GetStringAsync("https://www.giga-web.jp/cookie_set.php"); } catch { }
                    htmlSearch = await HttpClient.GetStringAsync(searchUrl);
                }
                var docSearch = new CQ(htmlSearch);
                var boxes = docSearch.Select("div.search_sam_box");
                foreach (var box in boxes)
                {
                    var href = box.Cq().Find("a").Attr("href") ?? "";
                    var text = box.Cq().Text();
                    if (!string.IsNullOrWhiteSpace(href) && text.Contains($"（{number.ToUpper()}）"))
                    {
                        realUrl = "https://www.giga-web.jp" + href;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(realUrl)) return null;

            var html = await HttpClient.GetStringAsync(realUrl);
            var doc = new CQ(html);

            var title = doc.Select("h5").Text().Trim();
            if (string.IsNullOrWhiteSpace(title)) return null;

            var webNumber = doc.Select("span:contains('作品番号')").Parent().Find("dd").Text().Trim();
            var outline = doc.Select("#story_list2 ul li.story_window, #eye_list2 ul li.story_window").Select(li => li.Cq().Text().Replace("[BAD END]", "").Trim()).ToList();
            var outlineText = string.Join("\n", outline).Trim();

            var actor = string.Join(",", doc.Select("span.yaku a").Select(a => a.Cq().Text().Trim()));
            var poster = doc.Select("div.smh li ul li a img").Attr("src") ?? "";
            var cover = doc.Select("div.smh li ul li a").Attr("href") ?? "";
            if (string.IsNullOrWhiteSpace(poster))
            {
                var altPoster = doc.Select("div.smh li img").Attr("src") ?? "";
                if (!string.IsNullOrWhiteSpace(altPoster))
                {
                    poster = altPoster;
                    cover = altPoster.Replace("pac_s", "pac_l");
                }
            }
            cover = cover.Replace("http://", "https://");

            var release = doc.Select("dt:contains('リリース')").Next().Text().Trim().Replace("/", "-");
            var year = Regex.Match(release ?? string.Empty, @"\d{4}").Value;
            var runtime = Regex.Match(doc.Select("dt:contains('収録時間')").Next().Text(), @"(\d+)").Groups[1].Value;
            var score = Regex.Match(html, @"5点満点中 <b>(.+)<").Groups[1].Value;
            var tag = string.Join(",", doc.Select("#tag_main a").Select(a => a.Cq().Text().Trim()));
            var series = "";
            var director = doc.Select("dt:contains('監督')").Next().Text().Trim();
            var studio = "GIGA";
            var publisher = "GIGA";
            var extrafanart = doc.Select("div.gasatsu_images_pc div div a").Select(a => "https://www.giga-web.jp" + (a.GetAttribute("href") ?? "")).ToList();

            var detail = new GigaVideoDetail
            {
                Number = string.IsNullOrWhiteSpace(webNumber) ? number : webNumber,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag,
                Release = release,
                Year = year,
                Runtime = runtime,
                Series = series,
                Director = director,
                Studio = studio,
                Publisher = publisher,
                CoverUrl = cover,
                PosterUrl = poster,
                Website = realUrl,
                Source = "giga",
                Mosaic = "有码"
            };

            return detail;
        }
        catch
        {
            return null;
        }
    }
}


