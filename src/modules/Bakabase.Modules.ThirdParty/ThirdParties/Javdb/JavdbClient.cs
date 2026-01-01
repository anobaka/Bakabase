using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Javdb.Models;
using Microsoft.Extensions.Logging;
using CsQuery;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Javdb;

public class JavdbClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<JavdbVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null, string? baseUrl = null)
    {
        try
        {
            var site = baseUrl ?? "https://javdb.com";
            string realUrl = appointUrl ?? "";
            string? searchUrl = null;

            if (string.IsNullOrWhiteSpace(realUrl))
            {
                searchUrl = $"{site}/search?q={Uri.EscapeDataString(number)}&locale=zh";
                var searchHtml = await HttpClient.GetStringAsync(searchUrl);
                var searchCq = new CQ(searchHtml);

                realUrl = GetRealUrl(searchCq, number);
                if (string.IsNullOrEmpty(realUrl))
                {
                    return null;
                }

                if (realUrl.StartsWith("/v/"))
                {
                    realUrl = $"{site}{realUrl}?locale=zh";
                }
            }

            var detailHtml = await HttpClient.GetStringAsync(realUrl);
            if (detailHtml.Contains("Cloudflare") || detailHtml.Contains("owner of this website has banned"))
            {
                return null;
            }

            var detailCq = new CQ(detailHtml);

            var (title, originalTitle) = GetTitle(detailCq, "zh_cn");
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var actor = GetActor(detailCq);
            var numberWeb = GetNumber(detailCq, number);
            var coverUrl = GetCover(detailCq);
            var posterUrl = string.IsNullOrEmpty(coverUrl) ? "" : coverUrl.Replace("/covers/", "/thumbs/");
            var tag = GetTag(detailCq);
            var release = GetRelease(detailCq);
            var year = GetYear(release);
            var runtime = GetRuntime(detailCq);
            var series = GetSeries(detailCq);
            var director = GetDirector(detailCq);
            var studio = GetStudio(detailCq);
            var publisher = GetPublisher(detailCq);

            return new JavdbVideoDetail
            {
                Number = numberWeb,
                Title = CleanupTitle(title, numberWeb),
                OriginalTitle = CleanupTitle(originalTitle, numberWeb),
                Actor = actor,
                Tag = tag,
                Release = release,
                Year = year,
                Runtime = runtime,
                Series = series,
                Studio = studio,
                Publisher = publisher,
                Source = "javdb",
                CoverUrl = coverUrl,
                PosterUrl = posterUrl,
                Website = NormalizeWebsite(realUrl, site),
                Mosaic = (title.Contains("無修正") || title.Contains("無碼") || title.Contains("无码")) ? "无码" : "有码",
                SearchUrl = searchUrl
            };
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeWebsite(string realUrl, string site)
    {
        return realUrl.Replace("?locale=zh", "").Replace("https://javdb.com", site);
    }

    private static string CleanupTitle(string text, string number)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var cleaned = text
            .Replace("中文字幕", "")
            .Replace("無碼", "")
            .Replace("\\n", "")
            .Replace("_", "-")
            .Replace(number.ToUpper(), "")
            .Replace(number, "")
            .Replace("--", "-")
            .Trim();
        var removeParts = new[] {"第一集", "第二集", " - 上", " - 下", " 上集", " 下集", " -上", " -下"};
        foreach (var part in removeParts) cleaned = cleaned.Replace(part, "").Trim();
        return cleaned;
    }

    private static (string title, string originalTitle) GetTitle(CQ html, string orgLanguage)
    {
        var title = html["h2.title.is-4 strong.current-title"].Text();
        var original = html["h2.title.is-4 span.origin-title"].Text();
        if (!string.IsNullOrEmpty(original) && orgLanguage == "jp")
        {
            title = original;
        }
        if (string.IsNullOrEmpty(original)) original = title;
        return (title.Trim(), original.Trim());
    }

    private static string GetActor(CQ html)
    {
        var actors = html["div.panel-block strong:contains('演員:'), div.panel-block strong:contains('Actor(s):')"]
            .Parent()
            .Find("span.value a");
        return string.Join(",", actors.Select(a => a.InnerText));
    }

    private static string GetNumber(CQ html, string number)
    {
        var result = html["a.button.is-white.copy-to-clipboard"].Attr("data-clipboard-text");
        return string.IsNullOrEmpty(result) ? number : result;
    }

    private static string GetCover(CQ html)
    {
        return html["img.video-cover"].Attr("src") ?? "";
    }

    private static string GetTag(CQ html)
    {
        var t1 = html["div.panel-block strong:contains('類別:')"].Parent().Find("span.value a").Select(a => a.InnerText);
        var t2 = html["div.panel-block strong:contains('Tags:')"].Parent().Find("span.value a").Select(a => a.InnerText);
        return string.Join(",", t1.Concat(t2)).Replace(" ,", ",").Replace("  ", " ").Trim();
    }

    private static string GetRelease(CQ html)
    {
        var r1 = html["div.panel-block strong:contains('日期:')"].Parent().Find("span").Text();
        var r2 = html["div.panel-block strong:contains('Released Date:')"].Parent().Find("span").Text();
        return string.IsNullOrWhiteSpace(r1) ? r2 : r1;
    }

    private static string GetYear(string release)
    {
        var m = Regex.Match(release ?? string.Empty, @"\d{4}");
        return m.Success ? m.Value : release;
    }

    private static string GetRuntime(CQ html)
    {
        var r1 = html["div.panel-block strong:contains('時長')"].Parent().Find("span").Text();
        var r2 = html["div.panel-block strong:contains('Duration:')"].Parent().Find("span").Text();
        var text = string.IsNullOrWhiteSpace(r1) ? r2 : r1;
        return Regex.Replace(text ?? string.Empty, @"\D+", "").Trim();
    }

    private static string GetSeries(CQ html)
    {
        var s1 = html["div.panel-block strong:contains('系列:')"].Parent().Find("span a").Text();
        var s2 = html["div.panel-block strong:contains('Series:')"].Parent().Find("span a").Text();
        return string.IsNullOrWhiteSpace(s1) ? s2 : s1;
    }

    private static string GetDirector(CQ html)
    {
        var d1 = html["div.panel-block strong:contains('導演:')"].Parent().Find("span a").Text();
        var d2 = html["div.panel-block strong:contains('Director:')"].Parent().Find("span a").Text();
        return string.IsNullOrWhiteSpace(d1) ? d2 : d1;
    }

    private static string GetStudio(CQ html)
    {
        var s1 = html["div.panel-block strong:contains('片商:')"].Parent().Find("span a").Text();
        var s2 = html["div.panel-block strong:contains('Maker:')"].Parent().Find("span a").Text();
        return string.IsNullOrWhiteSpace(s1) ? s2 : s1;
    }

    private static string GetPublisher(CQ html)
    {
        var p1 = html["div.panel-block strong:contains('發行:')"].Parent().Find("span a").Text();
        var p2 = html["div.panel-block strong:contains('Publisher:')"].Parent().Find("span a").Text();
        return string.IsNullOrWhiteSpace(p1) ? p2 : p1;
    }

    private static string GetRealUrl(CQ searchHtml, string number)
    {
        var items = searchHtml["a.box"];
        foreach (var item in items)
        {
            var itemCq = item.Cq();
            var href = itemCq.Attr("href") ?? string.Empty;
            var title = itemCq.Find("div.video-title strong").Text();
            var meta = itemCq.Find("div.meta").Text();

            var normalizedQuery = number.ToUpper().Replace(".", "").Replace("-", "").Replace(" ", "");
            var normalizedTarget = (title + meta).ToUpper().Replace("-", "").Replace(".", "").Replace(" ", "");
            if (normalizedTarget.Contains(normalizedQuery))
            {
                return href;
            }
        }
        return string.Empty;
    }
}


