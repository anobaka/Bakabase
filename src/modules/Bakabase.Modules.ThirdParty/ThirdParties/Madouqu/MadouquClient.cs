using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Madouqu.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Madouqu;

public class MadouquClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<MadouquVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            // Search on site (domain can vary; use default)
            string? realUrl = appointUrl;
            var searchBases = new[] { "https://madouqu.com" };
            if (string.IsNullOrWhiteSpace(realUrl))
            {
                foreach (var baseUrl in searchBases)
                {
                    var searchUrl = $"{baseUrl}/?s={Uri.EscapeDataString(number)}";
                    string html;
                    try { html = await HttpClient.GetStringAsync(searchUrl); } catch { continue; }
                    var doc = new CQ(html);
                    var posts = doc.Select("h4.post-title a");
                    foreach (var a in posts)
                    {
                        var href = a.GetAttribute("href") ?? "";
                        var tmpTitle = a.GetAttribute("title") ?? a.Cq().Text();
                        if (!string.IsNullOrWhiteSpace(href) && (tmpTitle.Contains(number, StringComparison.OrdinalIgnoreCase) || href.Contains(number, StringComparison.OrdinalIgnoreCase)))
                        {
                            realUrl = href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : (baseUrl + href);
                            break;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(realUrl)) break;
                }
            }

            if (string.IsNullOrWhiteSpace(realUrl)) return null;

            var htmlDetail = await HttpClient.GetStringAsync(realUrl);
            var docDetail = new CQ(htmlDetail);

            var title = docDetail.Select("div.cao_entry_header header h1").Text().Trim();
            if (string.IsNullOrWhiteSpace(title)) return null;

            // cover
            var cover = docDetail.Select("div.entry-content img").Attr("src") ?? "";
            // studio/category
            var studio = docDetail.Select("span.meta-category").Text().Trim();
            // release/year from <time datetime>
            var datetime = docDetail.Select("time[datetime]").Attr("datetime") ?? "";
            string release = "", year = "";
            if (!string.IsNullOrWhiteSpace(datetime))
            {
                release = datetime.Length >= 10 ? datetime.Substring(0, 10) : datetime;
                if (datetime.Length >= 4) year = datetime.Substring(0, 4);
            }
            // actor from heuristics omitted; keep empty to match future changes

            var detail = new MadouquVideoDetail
            {
                Number = title, // python may replace later; keep title as number when not parsed
                Title = title,
                OriginalTitle = title,
                Actor = "",
                Tag = "",
                Release = release,
                Year = year,
                Runtime = "",
                Series = "",
                Studio = studio,
                Publisher = studio,
                CoverUrl = cover,
                PosterUrl = "",
                Website = realUrl,
                Source = "madouqu",
                Mosaic = "国产"
            };

            return detail;
        }
        catch
        {
            return null;
        }
    }
}


