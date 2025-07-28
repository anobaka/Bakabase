using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Lulubar.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Lulubar;

public class LulubarClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<LulubarVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            string? realUrl = appointUrl;
            string poster = "";
            if (string.IsNullOrWhiteSpace(realUrl))
            {
                var searchUrl = $"https://lulubar.co/video/bysearch?search={Uri.EscapeDataString(number)}&page=1";
                var htmlSearch = await HttpClient.GetStringAsync(searchUrl);
                var docSearch = new CQ(htmlSearch);
                var items = docSearch.Select("a.imgBoxW");
                foreach (var a in items)
                {
                    var href = a.GetAttribute("href") ?? "";
                    var alt = a.Cq().Find("img").Attr("alt") ?? "";
                    var src = a.Cq().Find("img").Attr("src") ?? "";
                    if (!string.IsNullOrWhiteSpace(href) && alt.StartsWith(number.ToLower(), StringComparison.OrdinalIgnoreCase))
                    {
                        realUrl = "https://lulubar.co" + href;
                        poster = src.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? src : ("https://lulubar.co" + src);
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(realUrl)) return null;

            var html = await HttpClient.GetStringAsync(realUrl);
            var doc = new CQ(html);

            // Title/Number from <title>
            var titleTag = doc.Select("title").Text();
            string title = "", webNumber = number;
            try
            {
                var split = titleTag.Split('|');
                if (split.Length >= 1)
                {
                    var left = split[0].Trim();
                    var parts = left.Split(' ');
                    if (parts.Length >= 2)
                    {
                        var num = parts[0].Trim();
                        title = left.Replace(num, "").Trim();
                        webNumber = num;
                        if (string.IsNullOrWhiteSpace(title)) title = webNumber;
                    }
                }
            }
            catch { }
            if (string.IsNullOrWhiteSpace(title)) return null;

            var outline = doc.Select("p.video_container_info").Text().Trim();
            var actor = string.Join(",", doc.Select("div.tag_box a[title='女优']").Select(a => a.Cq().Text().Trim()));
            var actorPhoto = actor.Split(',', StringSplitOptions.RemoveEmptyEntries).ToDictionary(a => a, _ => "");
            var studio = doc.Select("div.tag_box a[title='片商']").Text().Trim();
            var cover = doc.Select("a.notVipAd.imgBoxW img").Attr("src") ?? "";
            if (!string.IsNullOrWhiteSpace(cover) && !cover.StartsWith("http", StringComparison.OrdinalIgnoreCase)) cover = "https://lulubar.co" + cover;
            var tag = string.Join(",", doc.Select("div.tag_box a.tag[href*='bytagdetail']").Select(a => a.Cq().Text().Trim()));
            var release = doc.Select("a[title*='上架日']").Attr("title")?.Replace("上架日", "").Trim() ?? "";
            var year = Regex.Match(release ?? string.Empty, @"\d{4}").Value;
            var extrafanart = doc.Select("#stills div img").Select(img => "https://lulubar.co" + (img.GetAttribute("src") ?? "")).ToList();

            // Mosaic
            var tagBoxText = string.Join(",", doc.Select("div.tag_box a.tag").Select(a => a.Cq().Text()));
            var mosaic = tagBoxText.Contains("有码") ? "有码" : (tagBoxText.Contains("无码") ? "无码" : (tagBoxText.Contains("国产") ? "国产" : ""));

            var detail = new LulubarVideoDetail
            {
                Number = webNumber,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag,
                Release = release,
                Year = year,
                Runtime = "",
                Series = "",
                Studio = studio,
                Publisher = "",
                CoverUrl = cover,
                PosterUrl = poster,
                Website = realUrl,
                Source = "lulubar",
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


