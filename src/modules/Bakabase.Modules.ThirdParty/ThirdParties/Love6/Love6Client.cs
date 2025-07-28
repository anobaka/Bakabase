using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Love6.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Love6;

public class Love6Client(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<Love6VideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            string? realUrl = appointUrl;
            string poster = "";
            if (string.IsNullOrWhiteSpace(realUrl))
            {
                var searchUrl = $"https://love6.tv/search/all/?search_text={Uri.EscapeDataString(number)}";
                var htmlSearch = await HttpClient.GetStringAsync(searchUrl);
                var docSearch = new CQ(htmlSearch);
                var items = docSearch.Select("div.col-sm-2.search_item a");
                foreach (var a in items)
                {
                    var href = a.GetAttribute("href") ?? "";
                    var tmpTitle = a.Cq().Find("div.album_text").Text();
                    var img = a.Cq().Find("div.search_img img").Attr("src") ?? "";
                    if (!string.IsNullOrWhiteSpace(tmpTitle) && !string.IsNullOrWhiteSpace(href))
                    {
                        realUrl = "https://love6.tv" + href;
                        poster = img;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(realUrl)) return null;

            var html = await HttpClient.GetStringAsync(realUrl);
            var doc = new CQ(html);

            // number field on page
            var webNumber = doc.Select("div.video_description span:contains('番號')").Text().Replace("番號 : ", "").Trim();

            // actor
            var actor = string.Join(",", doc.Select("div.video_description a[href*='performer']").Select(a => a.Cq().Text().Trim()))
                .Trim(',');
            var actorPhoto = actor.Split(',', StringSplitOptions.RemoveEmptyEntries).ToDictionary(a => a, _ => "");

            // title post-processed
            var title = doc.Select("h1.fullvideo-title").Text().Trim();
            title = title.Replace(webNumber, "").Trim();
            foreach (var key in actorPhoto.Keys) title = title.Replace(key, "").Trim();
            foreach (Match m in Regex.Matches(webNumber, @"\d+")) title = title.Replace(m.Value, "").Trim();
            if (string.IsNullOrWhiteSpace(title)) return null;

            var outline = doc.Select("div.kv_description").Text().Trim();
            var cover = doc.Select("div.kv_img").Css("background-image"); // CsQuery can't eval computed; fallback regex on html
            var coverMatch = Regex.Match(html, "background-image: url\\('([^']+)" );
            cover = coverMatch.Success ? coverMatch.Groups[1].Value : "";

            var tag = string.Join(",", doc.Select("div.kv_tag a").Select(a => a.Cq().Text().Trim()));
            var release = doc.Select("div.video_description span:contains('發行時間')").Text().Replace("發行時間: ", "").Trim();
            var year = Regex.Match(release ?? string.Empty, @"\d{4}").Value;

            var mosaic = ""; // no explicit mapping kept to empty as python

            var detail = new Love6VideoDetail
            {
                Number = string.IsNullOrWhiteSpace(webNumber) ? number : webNumber,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag,
                Release = release,
                Year = year,
                Runtime = "",
                Series = "",
                Studio = "",
                Publisher = "",
                CoverUrl = cover,
                PosterUrl = poster,
                Website = realUrl,
                Source = "love6",
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


