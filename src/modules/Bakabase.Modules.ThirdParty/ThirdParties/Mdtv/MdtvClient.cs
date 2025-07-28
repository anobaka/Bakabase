using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Mdtv.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Mdtv;

public class MdtvClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<MdtvVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            var baseUrl = "https://www.mdpjzip.xyz";
            string? realUrl = appointUrl;
            string searchUrl = $"{baseUrl}/index.php/vodsearch/-------------.html";

            if (string.IsNullOrWhiteSpace(realUrl))
            {
                // simple search post replaced by get for offline simulation
                var htmlSearch = await HttpClient.GetStringAsync($"{searchUrl}?wd={Uri.EscapeDataString(number)}");
                var docSearch = new CQ(htmlSearch);
                // pick first
                var a = docSearch.Select("h4.post-title a").FirstOrDefault();
                if (a != null)
                {
                    var href = a.GetAttribute("href") ?? "";
                    realUrl = baseUrl + href;
                }
            }

            if (string.IsNullOrWhiteSpace(realUrl)) return null;

            var html = await HttpClient.GetStringAsync(realUrl);
            var doc = new CQ(html);

            var title = doc.Select("h4.post-title a").Attr("title") ?? doc.Select("h4.post-title a").Text();
            if (string.IsNullOrWhiteSpace(title)) return null;

            var series = doc.Select("div.category").First().Text();
            var tag = string.Join(",", doc.Select("div.category a[href*='/class/']").Select(a => a.Cq().Text().Trim()));
            var actor = string.Join(",", doc.Select("div.category").Eq(2).Find("a").Select(a => a.Cq().Text().Trim()));
            var cover = doc.Select("div.blog-single div a img").Attr("src") ?? "";
            if (!string.IsNullOrWhiteSpace(cover) && !cover.StartsWith("http", StringComparison.OrdinalIgnoreCase)) cover = baseUrl + cover;
            var releaseMatch = Regex.Match(cover, @"/(\d{4})(\d{2})(\d{2})-");
            var release = releaseMatch.Success ? $"{releaseMatch.Groups[1].Value}-{releaseMatch.Groups[2].Value}-{releaseMatch.Groups[3].Value}" : "";
            var year = Regex.Match(release ?? string.Empty, @"\d{4}").Value;

            var detail = new MdtvVideoDetail
            {
                Number = number,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag,
                Release = release,
                Year = year,
                Runtime = "",
                Series = series,
                Studio = series, // studio inferred from label list in python; approximate
                Publisher = series,
                CoverUrl = cover,
                PosterUrl = "",
                Website = realUrl,
                Source = "mdtv",
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


