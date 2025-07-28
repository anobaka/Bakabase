using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Mmtv.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Mmtv;

public class MmtvClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<MmtvVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            var baseUrl = "https://www.7mmtv.sx"; // default domain used in python config
            string? realUrl = appointUrl;

            if (string.IsNullOrWhiteSpace(realUrl))
            {
                var searchKeyword = number;
                var m = Regex.Match(number, @"FC2-?\d+");
                if (m.Success)
                {
                    searchKeyword = Regex.Match(number, @"\d{3,}").Value;
                }
                var searchUrl = $"{baseUrl}/zh/searchform_search/all/index.html?search_keyword={Uri.EscapeDataString(searchKeyword)}&search_type=searchall&op=search";
                var htmlSearch = await HttpClient.GetStringAsync(searchUrl);
                var docSearch = new CQ(htmlSearch);
                var cand = docSearch.Select("figure.video-preview a");
                foreach (var a in cand)
                {
                    var href = a.GetAttribute("href") ?? "";
                    var imgAlt = a.Cq().Find("img").Attr("alt") ?? "";
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        realUrl = href;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(realUrl)) return null;

            var html = await HttpClient.GetStringAsync(realUrl);
            var doc = new CQ(html);

            // number and release/runtime from info bar
            var infoSpans = doc.Select("div.d-flex.mb-4 span").ToList();
            var webNumber = infoSpans.Count > 0 ? infoSpans[0].Cq().Text() : number;
            if (webNumber.StartsWith("FC2-PPV ")) webNumber = webNumber.Replace("FC2-PPV ", "FC2-");
            var release = infoSpans.Count > 1 ? Regex.Match(infoSpans[1].Cq().Text(), @"\d{4}-\d{2}-\d{2}").Value : "";
            var runtime = "";
            if (infoSpans.Count > 2)
            {
                var s = infoSpans[2].Cq().Text();
                if (s.Contains(":"))
                {
                    var parts = s.Split(':');
                    if (parts.Length == 3) runtime = (int.Parse(parts[0]) * 60 + int.Parse(parts[1])).ToString();
                    else if (parts.Length == 2) runtime = int.Parse(parts[0]).ToString();
                }
                else
                {
                    var rm = Regex.Match(s, @"(\d+)(分|min)");
                    if (rm.Success) runtime = rm.Groups[1].Value;
                }
            }

            var title = doc.Select("h1.fullvideo-title").Text().Trim();
            var webNumForTitle = webNumber;
            title = title.Replace(webNumForTitle, "").Trim();
            if (string.IsNullOrWhiteSpace(title)) return null;

            var actor = string.Join(",", doc.Select("div.fullvideo-idol span a").Select(a => Regex.Replace(a.Cq().Text().Trim(), "（.+）", string.Empty).Split(' ')[0]).Where(x => !string.IsNullOrWhiteSpace(x)));
            var cover = Regex.Match(html, "class=\\\"player-cover\\\" ><a><img src=\\\"([^\\\"]+)").Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(cover) && !cover.StartsWith("http", StringComparison.OrdinalIgnoreCase)) cover = baseUrl.Replace(".sx", ".tv") + cover;
            var (outline, originalplot) = ("", "");
            var intro = doc.Select("div.video-introduction-images-text p").Select(p => p.Cq().Text()).ToList();
            if (intro.Count > 0)
            {
                outline = intro.Last();
                originalplot = intro.First();
            }
            var year = Regex.Match(release ?? string.Empty, @"\d{4}").Value;
            var director = doc.Select("div.col-auto.flex-shrink-1.flex-grow-1 a[href*='director']").Text().Trim();
            var studio = doc.Select("div.col-auto.flex-shrink-1.flex-grow-1 a[href*='makersr']").Text().Trim();
            var publisher = doc.Select("div.col-auto.flex-shrink-1.flex-grow-1 a[href*='issuer']").Text().Trim();
            var tag = string.Join(",", doc.Select("div.d-flex.flex-wrap.categories a").Select(a => a.Cq().Text().Trim()));
            var extrafanart = new List<string>();
            extrafanart.AddRange(doc.Select("span img.lazyload").Select(i => i.GetAttribute("data-src") ?? ""));
            var hidden = doc.Select("div.fullvideo script[language='javascript']").Text();
            if (!string.IsNullOrWhiteSpace(hidden))
            {
                extrafanart.AddRange(Regex.Matches(hidden, @"https?://.+?\.jpe?g").Select(m => m.Value));
            }
            var breadcrumb = doc.Select("ol.breadcrumb").Text();
            var mosaic = breadcrumb.Contains("無碼AV") || breadcrumb.Contains("國產影片") || webNumber.ToUpperInvariant().StartsWith("FC2") ? "无码" : "有码";

            var detail = new MmtvVideoDetail
            {
                Number = webNumber,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag,
                Release = release,
                Year = year,
                Runtime = runtime,
                Series = "",
                Studio = studio,
                Publisher = publisher,
                CoverUrl = cover,
                PosterUrl = "",
                Website = realUrl,
                Source = "7mmtv",
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


