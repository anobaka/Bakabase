using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Faleno.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Faleno;

public class FalenoClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<FalenoVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            var lowered = number.ToLowerInvariant();
            var loweredNoDash = lowered.Replace("-", "");
            var loweredSpaced = lowered.Replace("-", " ");

            // Resolve detail URL
            var candidateUrls = new List<string>();
            if (!string.IsNullOrWhiteSpace(appointUrl))
            {
                candidateUrls.Add(appointUrl);
            }
            else
            {
                if (number.ToUpperInvariant().StartsWith("FLN"))
                {
                    candidateUrls.Add($"https://faleno.jp/top/works/{loweredNoDash}/");
                    candidateUrls.Add($"https://faleno.jp/top/works/{lowered}/");
                    candidateUrls.Add($"https://falenogroup.com/works/{lowered}/");
                    candidateUrls.Add($"https://falenogroup.com/works/{loweredNoDash}/");
                }

                // Search pages
                candidateUrls.Add($"https://faleno.jp/top/?s={Uri.EscapeDataString(loweredSpaced)}");
                candidateUrls.Add($"https://falenogroup.com/top/?s={Uri.EscapeDataString(loweredSpaced)}");
            }

            string? realUrl = null;
            string posterUrl = "";

            foreach (var url in candidateUrls)
            {
                string html;
                try
                {
                    html = await HttpClient.GetStringAsync(url);
                }
                catch
                {
                    continue;
                }

                // If this is a search page, try to extract the first matching result
                if (url.Contains("/top/?s=") || url.Contains("/top/?s=") || url.Contains("/top/?s="))
                {
                    var cq = new CQ(html);
                    var firstAnchor = cq.Select("div.text_name a").FirstOrDefault();
                    var firstPoster = cq.Select("div.text_name").Parent().Find("a img").FirstOrDefault();
                    if (firstAnchor != null)
                    {
                        var href = firstAnchor.GetAttribute("href") ?? "";
                        if (!string.IsNullOrWhiteSpace(href))
                        {
                            if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                var baseDomain = url.Contains("faleno.jp") ? "https://faleno.jp" : "https://falenogroup.com";
                                href = baseDomain.TrimEnd('/') + href;
                            }
                            realUrl = href;
                            posterUrl = firstPoster?.GetAttribute("src") ?? "";
                            break;
                        }
                    }
                }
                else
                {
                    // Already a detail page
                    realUrl = url;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(realUrl))
            {
                return null;
            }

            // Load detail page
            var detailHtml = await HttpClient.GetStringAsync(realUrl);
            var doc = new CQ(detailHtml);

            string GetTextAfterLabel(string labelContains)
            {
                var label = doc.Select($"span:contains('{labelContains}')").FirstOrDefault();
                if (label?.ParentNode == null) return "";
                // Try immediate following content
                var parent = label.ParentNode.Cq();
                var text = parent.Text().Trim();
                // Remove the label itself
                text = Regex.Replace(text, @"\s+", " ").Trim();
                if (text.Contains(label.InnerText))
                {
                    text = text.Replace(label.InnerText, "").Trim();
                }
                return text;
            }

            string title = doc.Select("h1").Text().Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            // Actor list
            var actorSpans = doc.Select("div.box_works01_list span:contains('出演女優')").Parent().Find("p").Text();
            var actor = actorSpans?.Trim() ?? "";

            // Outline
            var outline = doc.Select("div.box_works01_text p").Text().Trim();

            // Runtime
            var runtimeText = GetTextAfterLabel("収録時間");
            var runtimeMatch = Regex.Match(runtimeText ?? "", @"(\d+)");
            var runtime = runtimeMatch.Success ? runtimeMatch.Groups[1].Value : "";

            // Series
            var series = doc.Select("span:contains('系列')").Parent().Text().Replace("系列", "").Trim();

            // Director (导演/導演/監督)
            string director = "";
            foreach (var key in new[] { "导演", "導演", "監督" })
            {
                director = GetTextAfterLabel(key);
                if (!string.IsNullOrWhiteSpace(director)) break;
            }

            // Publisher/Studio
            var studio = doc.Select("span:contains('メーカー')").Parent().Text().Replace("メーカー", "").Trim();
            if (string.IsNullOrWhiteSpace(studio)) studio = "FALENO";
            var publisher = studio;

            // Release
            var release = doc.Select(".view_timer span:contains('配信開始日')").Parent().Find("p").Text().Trim().Replace("/", "-");
            var yearMatch = Regex.Match(release ?? "", @"(\d{4})");
            var year = yearMatch.Success ? yearMatch.Groups[1].Value : "";

            // Tags
            var tags = string.Join(",", doc.Select("a.genre").Select(a => a.Cq().Text().Trim()).Where(t => !string.IsNullOrWhiteSpace(t)));

            // Cover and Poster
            var cover = doc.Select("a.pop_sample img").Attr("src") ?? "";
            cover = cover.Replace("?output-quality=60", "");
            var poster = posterUrl;
            if (string.IsNullOrWhiteSpace(poster) && !string.IsNullOrWhiteSpace(cover))
            {
                poster = cover.Replace("_1200.jpg", "_2125.jpg").Replace("_tsp.jpg", "_actor.jpg").Replace("1200_re", "2125").Replace("_1200-1", "_2125-1");
            }

            // Extrafanart and trailer
            var extrafanart = doc.Select("a.pop_img").Select(a => a.GetAttribute("href")).Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
            var trailer = doc.Select("a.pop_sample").Attr("href") ?? "";

            var searchUrl = $"https://faleno.jp/top/?s={Uri.EscapeDataString(loweredSpaced)}";
            var detail = new FalenoVideoDetail
            {
                Number = number,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tags,
                Release = release,
                Year = year,
                Runtime = runtime,
                Series = series,
                Studio = studio,
                Publisher = publisher,
                CoverUrl = cover,
                PosterUrl = poster,
                Website = realUrl,
                Source = "faleno",
                Mosaic = "有码",
                SearchUrl = searchUrl
            };

            return detail;
        }
        catch
        {
            return null;
        }
    }
}


