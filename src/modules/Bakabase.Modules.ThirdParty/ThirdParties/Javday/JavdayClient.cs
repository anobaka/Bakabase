using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Javday.Models;
using Microsoft.Extensions.Logging;
using CsQuery;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Javday;

public class JavdayClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<JavdayVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null, string? baseUrl = null)
    {
        try
        {
            var site = baseUrl ?? "https://javday.tv";
            var realUrl = appointUrl;
            string? html = null;
            if (string.IsNullOrEmpty(realUrl))
            {
                var testUrl = site + $"/videos/{number}/";
                html = await HttpClient.GetStringAsync(testUrl);
                if (string.IsNullOrEmpty(html) || html.Contains("你似乎來到了沒有視頻存在的荒原"))
                {
                    return null;
                }
                realUrl = testUrl;
            }
            else
            {
                html = await HttpClient.GetStringAsync(realUrl);
            }

            var cq = new CQ(html);
            var title = cq["h1.h4.b"].Text().Trim();
            if (string.IsNullOrEmpty(title)) return null;
            var series = cq["#videoInfo div div p:nth-child(3) span:nth-child(2) a"].Text().Trim();
            var tags = cq["#videoInfo div div p:nth-child(1) span:nth-child(2) a"].Select(a => a.InnerText);
            var tag = string.Join(",", tags);
            var actor = string.Join(",", cq["#videoInfo div div p:nth-child(1) span:nth-child(2) a"].Select(a => a.InnerText));
            var cover = cq["head meta"].Eq(7).Attr("content") ?? string.Empty;
            if (!string.IsNullOrEmpty(cover) && !cover.StartsWith("http")) cover = site + cover;
            var studio = GetStudio(series, tag);

            return new JavdayVideoDetail
            {
                Number = number,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag,
                Release = "",
                Year = "",
                Runtime = "",
                Series = series,
                Studio = studio,
                Publisher = studio,
                Source = "javday",
                CoverUrl = cover,
                PosterUrl = string.Empty,
                Website = realUrl,
                Mosaic = "国产"
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GetStudio(string series, string tagCsv)
    {
        // Heuristic: studio may be one of the tags or the series title
        var labels = (series + "," + tagCsv).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return labels.FirstOrDefault() ?? string.Empty;
    }
}


