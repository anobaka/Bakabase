using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Javbus.Models;
using Microsoft.Extensions.Logging;
using CsQuery;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Javbus;

public class JavbusClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<JavbusVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null, string? baseUrl = null, string? mosaic = null)
    {
        try
        {
            var javbusUrl = baseUrl ?? "https://www.javbus.com";
            var realUrl = appointUrl;

            if (string.IsNullOrEmpty(realUrl))
            {
                // Default try direct detail url
                realUrl = $"{javbusUrl}/{number}";
            }

            var html = await HttpClient.GetStringAsync(realUrl);
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var cq = new CQ(html);
            var title = cq["h3"].Text().Trim();
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }

            var webNumber = cq["span.header:contains('識別碼:')"].Parent().Find("span").Eq(1).Text().Trim();
            if (!string.IsNullOrEmpty(webNumber))
            {
                number = webNumber;
            }
            title = title.Replace(number, "").Trim();

            var actor = string.Join(",", cq["div.star-name a"].Select(a => a.InnerText).Select(x => x.Trim()));
            var coverUrl = GetCover(cq, javbusUrl);
            var posterUrl = GetPoster(coverUrl);
            var release = cq["span.header:contains('發行日期:')"].Parent().Contents().Filter(n => n.NodeType == NodeType.TEXT_NODE).Text().Trim();
            var yearMatch = Regex.Match(release ?? string.Empty, @"\d{4}");
            var year = yearMatch.Success ? yearMatch.Value : release?.Substring(0, Math.Min(4, release.Length));
            var runtime = ExtractRuntime(cq);
            var studio = FirstText(cq, "a[href*='/studio/']");
            var publisher = FirstText(cq, "a[href*='/label/']");
            if (string.IsNullOrEmpty(publisher)) publisher = studio;
            var director = FirstText(cq, "a[href*='/director/']");
            var series = FirstText(cq, "a[href*='/series/']");
            var tag = string.Join(",", cq["span.genre label a[href*='/genre/']"].Select(a => a.InnerText).Select(x => x.Trim()));
            var activeTab = cq["li.active a"].Text();
            var mosaicText = activeTab.Contains("有碼") ? "有码" : "无码";
            var extraFanart = cq["#sample-waterfall a"].Select(a => a.GetAttribute("href")).ToArray();

            // If uncensored and not一本道, prefer no poster unless specific cases
            if (mosaicText == "无码")
            {
                var isKmh = number.Contains("KMHRS");
                if (!isKmh)
                {
                    posterUrl = string.Empty;
                }
                else if (extraFanart.Length > 0)
                {
                    posterUrl = extraFanart[0];
                }
            }

            return new JavbusVideoDetail
            {
                Number = number,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag,
                Release = release,
                Year = year,
                Runtime = runtime,
                Series = series,
                Studio = studio,
                Publisher = publisher,
                Source = "javbus",
                CoverUrl = coverUrl,
                PosterUrl = posterUrl,
                Website = realUrl,
                Mosaic = mosaicText
            };
        }
        catch
        {
            return null;
        }
    }

    private static string FirstText(CQ cq, string selector)
    {
        return cq[selector].First().Text().Trim();
    }

    private static string ExtractRuntime(CQ cq)
    {
        var text = cq["span.header:contains('長度:')"].Parent().Contents().Filter(n => n.NodeType == NodeType.TEXT_NODE).Text();
        var m = Regex.Match(text ?? string.Empty, @"\d+");
        return m.Success ? m.Value : string.Empty;
    }

    private static string GetCover(CQ cq, string baseUrl)
    {
        var href = cq["a.bigImage"].Attr("href") ?? string.Empty;
        if (string.IsNullOrEmpty(href)) return string.Empty;
        if (!href.StartsWith("http")) return baseUrl + href;
        return href;
    }

    private static string GetPoster(string coverUrl)
    {
        if (string.IsNullOrEmpty(coverUrl)) return string.Empty;
        if (coverUrl.Contains("/pics/"))
        {
            return coverUrl.Replace("/cover/", "/thumb/").Replace("_b.jpg", ".jpg");
        }
        if (coverUrl.Contains("/imgs/"))
        {
            return coverUrl.Replace("/cover/", "/thumbs/").Replace("_b.jpg", ".jpg");
        }
        return string.Empty;
    }
}


