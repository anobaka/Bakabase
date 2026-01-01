using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Iqqtv.Models;
using Microsoft.Extensions.Logging;
using CsQuery;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Iqqtv;

public class IqqtvClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<IqqtvVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null, string language = "zh_cn", string? baseUrl = null)
    {
        try
        {
            if (!Regex.IsMatch(number ?? string.Empty, @"^n\d{4}$"))
            {
                number = number?.ToUpper();
            }

            var site = (baseUrl ?? "https://iqq5.xyz").TrimEnd('/');
            var prefix = language switch { "zh_cn" => "/cn", "zh_tw" => "", _ => "/jp" };
            var iqqtvUrl = site + prefix + "/";

            string realUrl;
            string searchUrl = "";
            if (string.IsNullOrEmpty(appointUrl))
            {
                searchUrl = iqqtvUrl + "search.php?kw=" + Uri.EscapeDataString(number ?? string.Empty);
                var searchHtml = await HttpClient.GetStringAsync(searchUrl);
                var searchCq = new CQ(searchHtml);
                var tmp = GetRealUrl(searchCq, number ?? string.Empty);
                if (string.IsNullOrEmpty(tmp)) return null;
                realUrl = iqqtvUrl + tmp.Replace("/cn/", "/").Replace("/jp/", "/").Replace("&cat=19", "").TrimStart('/');
            }
            else
            {
                realUrl = iqqtvUrl + Regex.Replace(appointUrl, @".*player", "player");
            }

            var html = await HttpClient.GetStringAsync(realUrl);
            var cq = new CQ(html);

            var title = cq["#videoInfo h1.h4.b"].Text().Trim();
            if (string.IsNullOrEmpty(title)) return null;
            var webNumber = GetWebNumberFromTitle(title, number ?? string.Empty);
            title = title.Replace($" {webNumber}", "").Trim();
            var actor = string.Join(",", cq["#videoInfo p span:nth-child(2) a"].Select(a => a.InnerText));
            var cover = cq["meta[property='og:image']"].Attr("content") ?? string.Empty;
            var outline = GetOutline(cq);
            var release = cq["div.date"].Text().Replace("/", "-").Trim();
            var year = Regex.Match(release ?? string.Empty, @"\d{4}").Value;
            var tag = string.Join(",", cq[".tag-info a[href*='tag']"].Select(a => a.InnerText));
            var mosaic = GetMosaic(tag);
            var studio = cq["a[href*='fac'] div[itemprop]"].Text().Trim();
            var series = cq["a[href*='series']"].Text().Trim();
            var extrafanart = cq[".cover img[data-src]"].Select(img => img.GetAttribute("data-src")).ToArray();

            return new IqqtvVideoDetail
            {
                Number = webNumber,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag.Replace("无码片", "").Replace("無碼片", "").Replace("無修正", ""),
                Release = release,
                Year = year,
                Runtime = "",
                Series = series,
                Studio = studio,
                Publisher = studio,
                Source = "iqqtv",
                CoverUrl = cover,
                PosterUrl = string.Empty,
                Website = realUrl,
                Mosaic = mosaic,
                SearchUrl = searchUrl
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GetRealUrl(CQ html, string number)
    {
        foreach (var span in html["span.title"])
        {
            var a = span.Cq().Find("a");
            var href = a.Attr("href") ?? string.Empty;
            var title = a.Attr("title") ?? string.Empty;
            if (title.ToUpper().Contains(number.ToUpper()) && !IsBroken(title))
            {
                return href;
            }
        }
        return string.Empty;
    }

    private static bool IsBroken(string title)
    {
        var list = new[] { "克破", "无码破解", "無碼破解", "无码流出", "無碼流出" };
        return list.Any(x => title.Contains(x));
    }

    private static string GetWebNumberFromTitle(string title, string number)
    {
        var parts = title.Split(' ');
        var result = parts.Length > 1 ? parts[^1] : number.ToUpper();
        return result.Replace("_1pondo_", string.Empty).Replace("1pondo_", string.Empty)
            .Replace("caribbeancom-", string.Empty).Replace("caribbeancom", string.Empty)
            .Replace("-PPV", string.Empty).Trim(' ', '_', '-');
    }

    private static string GetOutline(CQ html)
    {
        var p = html["p:contains('简介'), p:contains('簡介')"].Text();
        if (string.IsNullOrEmpty(p)) return string.Empty;
        var trimmed = Regex.Replace(p, @"[\n\t]|(简|簡)介：", "");
        return trimmed.Split("*根据分发", StringSplitOptions.RemoveEmptyEntries)[0].Trim();
    }

    private static string GetMosaic(string tag)
    {
        return (tag.Contains("无码") || tag.Contains("無碼") || tag.Contains("無修正")) ? "无码" : "有码";
    }
}


