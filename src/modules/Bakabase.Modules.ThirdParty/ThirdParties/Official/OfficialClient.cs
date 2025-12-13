using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Official.Models;
using Microsoft.Extensions.Logging;
using CsQuery;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Official;

public class OfficialClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<OfficialVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null, string? officialBaseUrl = null)
    {
        try
        {
            if (string.IsNullOrEmpty(officialBaseUrl))
            {
                // Without a mapping from number prefix to official site, require caller to pass a base URL
                return null;
            }

            var realUrl = appointUrl;
            string poster = string.Empty;
            string? searchUrl = null;
            if (string.IsNullOrEmpty(realUrl))
            {
                searchUrl = officialBaseUrl.TrimEnd('/') + "/search/list?keyword=" + number.Replace("-", "");
                var searchHtml = await HttpClient.GetStringAsync(searchUrl);
                var (u, p) = GetRealUrl(new CQ(searchHtml), number);
                if (string.IsNullOrEmpty(u)) return null;
                realUrl = u;
                poster = p;
            }

            var html = await HttpClient.GetStringAsync(realUrl);
            var cq = new CQ(html);
            var title = cq["h2.p-workPage__title"].Text().Trim();
            if (string.IsNullOrEmpty(title)) return null;
            var outline = cq["p.p-workPage__text"].Text().Trim();
            var actor = string.Join(",", cq["a.c-tag.c-main-bg-hover.c-main-font.c-main-bd[href*='/actress/']"].Select(a => a.InnerText).Select(x => x.Trim()));
            var (publisher, studio) = GetPublisher(cq);
            var series = cq["div.th:contains('シリーズ')"].Parent().Find("a").Text().Trim();
            var runtime = cq["div.th:contains('収録時間')"].Parent().Find("div div p").Text().Replace("分", "").Trim();
            var trailer = cq["div.video video"].Attr("src") ?? string.Empty;
            var release = cq["div:contains('発売日')"].Parent().Find("div div a").Text().Replace("年", "-").Replace("月", "-").Replace("日", "").Trim();
            var year = Regex.Match(release ?? string.Empty, @"\d{4}").Value;
            var tag = string.Join(",", cq["div:contains('ジャンル')"].Parent().Find("div div a").Select(a => a.InnerText)).Replace(",Blu-ray（ブルーレイ）", "");
            var (coverUrl, extra) = GetCoverAndExtra(cq);

            return new OfficialVideoDetail
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
                Source = GetSourceName(officialBaseUrl),
                CoverUrl = coverUrl,
                PosterUrl = string.IsNullOrEmpty(poster) ? coverUrl : poster,
                Website = realUrl,
                Mosaic = "有码",
                SearchUrl = searchUrl
            };
        }
        catch
        {
            return null;
        }
    }

    private static (string url, string poster) GetRealUrl(CQ html, string number)
    {
        foreach (var a in html["a.img.hover"])
        {
            var href = a.GetAttribute("href") ?? string.Empty;
            var img = a.Cq().Find("img").Attr("data-src") ?? string.Empty;
            if (href.ToUpper().EndsWith(number.ToUpper().Replace("-", "")))
            {
                return (href, img);
            }
        }
        return (string.Empty, string.Empty);
    }

    private static (string publisher, string studio) GetPublisher(CQ html)
    {
        var desc = html["meta[name='description']"].Attr("content") ?? string.Empty;
        var m = Regex.Match(desc, "【公式】([^\\(]+)\\(([^\\)]+)\\)");
        var publisher = m.Success ? m.Groups[1].Value : string.Empty;
        var studio = m.Success ? m.Groups[2].Value : string.Empty;
        var label = html["div.th:contains('レーベル')"].Parent().Find("a").Text().Trim();
        if (!string.IsNullOrEmpty(label)) publisher = label;
        publisher = publisher.Replace("　", " ");
        return (publisher, studio);
    }

    private static (string cover, string[] extra) GetCoverAndExtra(CQ html)
    {
        var list = html["img.swiper-lazy"].Select(img => img.GetAttribute("data-src")).Where(x => !string.IsNullOrEmpty(x)).ToList();
        if (list.Count == 0) return (string.Empty, Array.Empty<string>());
        var cover = list[0]!;
        var extras = list.Skip(1).ToArray();
        return (cover, extras);
    }

    private static string GetSourceName(string? baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl)) return "official";
        var host = baseUrl.Replace("https://", "").Replace("http://", "");
        var parts = host.Split('.');
        return parts.Length >= 2 ? parts[^2] : host;
    }
}


