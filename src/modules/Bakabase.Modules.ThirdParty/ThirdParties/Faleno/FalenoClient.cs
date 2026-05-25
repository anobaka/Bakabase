using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.ThirdParty.Helpers;
using Bakabase.Modules.ThirdParty.ThirdParties.Av;
using Bakabase.Modules.ThirdParty.ThirdParties.Faleno.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Faleno;

public class FalenoClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory), IAvClient
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    string IAvClient.SourceId => AvSourceIds.Faleno;

    async Task<IAvDetail?> IAvClient.SearchAndParseVideo(string number, string? appointUrl, string? language) =>
        await SearchAndParseVideo(number, appointUrl: appointUrl);

    public async Task<FalenoVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            var lowered = number.ToLowerInvariant();
            var loweredNoDash = lowered.Replace("-", "");

            // faleno.jp redirected its catalogue to falenogroup.com; the canonical detail page is
            //   https://falenogroup.com/works/<lower-number>/
            // 404 returns a "ページが見つかりませんでした" page that we detect and skip.
            var candidateUrls = new List<string>();
            if (!string.IsNullOrWhiteSpace(appointUrl))
            {
                candidateUrls.Add(appointUrl);
            }
            else
            {
                candidateUrls.Add($"https://falenogroup.com/works/{lowered}/");
                if (loweredNoDash != lowered)
                {
                    candidateUrls.Add($"https://falenogroup.com/works/{loweredNoDash}/");
                }
            }

            string? realUrl = null;
            string posterUrl = "";
            string? detailHtml = null;

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

                if (IsNotFoundPage(html)) continue;
                realUrl = url;
                detailHtml = html;
                break;
            }

            if (string.IsNullOrWhiteSpace(realUrl) || detailHtml == null)
            {
                return null;
            }

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

            string title = doc.Select("h1").First().Text().Trim();
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

            // Series — read each label parent's text separately so a duplicate
            // mobile/desktop render does not concat into "NameName".
            var series = doc.Select("span:contains('系列')").Parent()
                .Select(p => p.Cq().Text().Replace("系列", "").Trim())
                .JoinDistinctText();

            // Director (导演/導演/監督)
            string director = "";
            foreach (var key in new[] { "导演", "導演", "監督" })
            {
                director = GetTextAfterLabel(key);
                if (!string.IsNullOrWhiteSpace(director)) break;
            }

            // Publisher/Studio
            var studio = doc.Select("span:contains('メーカー')").Parent()
                .Select(p => p.Cq().Text().Replace("メーカー", "").Trim())
                .JoinDistinctText();
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

            var searchUrl = realUrl;
            var detail = new FalenoVideoDetail
            {
                Number = number,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Outline = outline,
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
                Source = AvSourceIds.Faleno,
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

    private static bool IsNotFoundPage(string html) =>
        !string.IsNullOrEmpty(html) && html.Contains("ページが見つかりませんでした");
}


