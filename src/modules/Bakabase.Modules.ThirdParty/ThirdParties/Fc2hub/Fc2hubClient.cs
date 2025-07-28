using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Fc2hub.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Fc2hub;

public class Fc2hubClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<Fc2hubVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            var rootUrl = "https://javten.com"; // default from python
            var normalized = number.ToUpperInvariant()
                .Replace("FC2PPV", "")
                .Replace("FC2-PPV-", "")
                .Replace("FC2-", "")
                .Replace("-", "")
                .Trim();

            string? realUrl = appointUrl;
            if (string.IsNullOrWhiteSpace(realUrl))
            {
                var searchUrl = $"{rootUrl}/search?kw={normalized}";
                var htmlSearch = await HttpClient.GetStringAsync(searchUrl);
                var docSearch = new CQ(htmlSearch);
                var candidates = docSearch.Select("link[href*='id" + normalized + "']");
                foreach (var c in candidates)
                {
                    var href = c.GetAttribute("href") ?? "";
                    if (string.IsNullOrWhiteSpace(href)) continue;
                    if (href.Contains("/tw/") || href.Contains("/ko/") || href.Contains("/en/")) continue;
                    realUrl = href;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(realUrl)) return null;

            var htmlDetail = await HttpClient.GetStringAsync(realUrl);
            var doc = new CQ(htmlDetail);

            string title = doc.Select("h1").Skip(1).FirstOrDefault()?.Cq().Text().Trim() ?? ""; // title is 2nd h1
            if (string.IsNullOrWhiteSpace(title)) return null;

            var cover = doc.Select("a[data-fancybox='gallery']").Attr("href") ?? "";
            if (cover.StartsWith("//")) cover = "https:" + cover;

            var extrafanart = doc.Select("div[style='padding: 0'] a").Select(a => a.GetAttribute("href") ?? "").Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.StartsWith("//") ? ("https:" + u) : u).ToList();

            var studio = doc.Select("div.col-8").Text().Trim();
            var tag = string.Join(",", doc.Select("p.card-text a[href*='/tag/']").Select(a => a.Cq().Text().Trim()));
            var outline = string.Join("", doc.Select("div.col.des").Text()).Replace("\n", "").Trim();

            var mosaic = (tag.Contains("無修正") || title.Contains("無修正")) ? "无码" : "有码";

            var detail = new Fc2hubVideoDetail
            {
                Number = $"FC2-{normalized}",
                Title = title,
                OriginalTitle = title,
                Actor = studio,
                Tag = tag,
                Release = "",
                Year = "",
                Runtime = "",
                Series = "FC2系列",
                Studio = studio,
                Publisher = studio,
                CoverUrl = cover,
                PosterUrl = "",
                Website = realUrl,
                Source = "fc2hub",
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


