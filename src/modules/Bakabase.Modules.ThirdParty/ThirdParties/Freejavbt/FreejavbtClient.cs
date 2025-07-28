using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Freejavbt.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Freejavbt;

public class FreejavbtClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<FreejavbtVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            var realUrl = string.IsNullOrWhiteSpace(appointUrl)
                ? $"https://freejavbt.com/{number}"
                : appointUrl.Replace("/zh/", "/").Replace("/en/", "/").Replace("/ja/", "/");

            string html;
            try { html = await HttpClient.GetStringAsync(realUrl); } catch { return null; }
            if (string.IsNullOrWhiteSpace(html)) return null;

            // Attempt to parse; if parsing fails due to weird chars, we still rely on CsQuery which is lenient
            var doc = new CQ(html);

            // Title and number from <title>
            string pageTitle = doc.Select("title").Text();
            string parsedTitle = "";
            string parsedNumber = number;
            try
            {
                var cleaned = pageTitle.Replace("| FREE JAV BT", "");
                var parts = cleaned.Split('|');
                if (parts.Length == 2)
                {
                    var num = parts[0].Trim();
                    var ttl = string.Join(" ", parts.Skip(1)).Trim();
                    ttl = ttl.Replace(num.ToUpperInvariant(), "").Replace(num, "").Replace("_", "-").Trim();
                    parsedNumber = num;
                    parsedTitle = ttl;
                }
                else
                {
                    var sp = cleaned.Split(' ');
                    if (sp.Length > 2)
                    {
                        var num = sp[0].Trim();
                        var ttl = string.Join(" ", sp.Skip(1)).Trim();
                        ttl = ttl.Replace(num.ToUpperInvariant(), "").Replace(num, "").Replace("_", "-").Trim();
                        parsedNumber = num;
                        parsedTitle = ttl;
                    }
                }
            }
            catch { /* ignore */ }

            if (string.IsNullOrWhiteSpace(parsedTitle) || !html.Contains("single-video-info col-12")) return null;

            // Actors
            var allActors = doc.Select("a.actress").Select(a => a.Cq().Text().Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            var actor = string.Join(",", allActors.Where(a => !string.IsNullOrWhiteSpace(a)));
            var allActor = string.Join(",", allActors);

            // Cover
            var cover = doc.Select("img.player-cover").Attr("src") ?? "";
            if (!string.IsNullOrWhiteSpace(cover) && !cover.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                cover = "https://7mmtv.tv" + cover; // site used in python post-processing for trailer domain; cover is absolute there
            }

            // Outline and originalplot
            var outlineNodes = doc.Select("div.video-introduction-images-text p").Select(p => p.Cq().Text()).ToList();
            var outline = outlineNodes.Count > 0 ? outlineNodes.Last().Trim() : "";
            var originalplot = outlineNodes.Count > 0 ? outlineNodes.First().Trim() : outline;

            // Release, runtime from info spans
            string release = Regex.Match(html, @"\d{4}-\d{2}-\d{2}").Value;
            string runtime = "";
            var timeText = doc.Select("div.d-flex.mb-4 span").Skip(2).FirstOrDefault()?.Cq().Text() ?? "";
            if (timeText.Contains(":"))
            {
                var parts = timeText.Split(':');
                if (parts.Length == 3) runtime = (int.Parse(parts[0]) * 60 + int.Parse(parts[1])).ToString();
                else if (parts.Length == 2) runtime = int.Parse(parts[0]).ToString();
            }
            else
            {
                var m = Regex.Match(timeText, @"(\d+)(分|min)");
                if (m.Success) runtime = m.Groups[1].Value;
            }

            var year = string.IsNullOrWhiteSpace(release) ? "" : Regex.Match(release, @"\d{4}").Value;

            // Director/Studio/Publisher/Tags
            string DirectorSelector(string key) => doc.Select($"div.col-auto.flex-shrink-1.flex-grow-1 a[href*='{key}']").FirstOrDefault()?.Cq().Text().Trim() ?? "";
            var director = DirectorSelector("director");
            var studio = DirectorSelector("makersr");
            var publisher = DirectorSelector("issuer");
            var tag = string.Join(",", doc.Select("div.d-flex.flex-wrap.categories a").Select(a => a.Cq().Text().Trim()));

            // Stills
            var extrafanart = new List<string>();
            extrafanart.AddRange(doc.Select("span img.lazyload").Select(i => i.GetAttribute("data-src") ?? ""));
            var hidden = doc.Select("div.fullvideo script[language='javascript']").Text();
            if (!string.IsNullOrWhiteSpace(hidden))
            {
                extrafanart.AddRange(Regex.Matches(hidden, @"https?://.+?\.jpe?g").Select(m => m.Value));
            }

            // Mosaic
            var breadcrumb = doc.Select("ol.breadcrumb").Text();
            var mosaic = breadcrumb.Contains("無碼AV") || breadcrumb.Contains("國產影片") || parsedNumber.ToUpperInvariant().StartsWith("FC2") ? "无码" : "有码";

            var detail = new FreejavbtVideoDetail
            {
                Number = parsedNumber,
                Title = parsedTitle,
                OriginalTitle = parsedTitle,
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
                Source = "freejavbt",
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


