using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.DMM.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Dmm;

public class DmmClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<DMMVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            // For offline conversion, implement detail-page parsing if URL is provided.
            if (string.IsNullOrWhiteSpace(appointUrl))
            {
                return null;
            }

            var html = await HttpClient.GetStringAsync(appointUrl);
            var doc = new CQ(html);

            string GetText(CQ sel) => sel.Text().Trim();
            string GetNextCellText(string label)
            {
                var td = doc.Select($"td:contains('{label}')").FirstOrDefault()?.Cq();
                if (td == null || td.Length == 0)
                {
                    td = doc.Select($"th:contains('{label}')").FirstOrDefault()?.Cq();
                    if (td == null || td.Length == 0) return string.Empty;
                }
                var nxt = td.Parent().Find("td").FirstOrDefault()?.Cq();
                return nxt?.Text().Trim() ?? string.Empty;
            }

            // Title
            var title = GetText(doc.Select("h1#title"));
            if (string.IsNullOrWhiteSpace(title)) title = GetText(doc.Select("h1.item.fn.bold"));
            if (string.IsNullOrWhiteSpace(title)) return null;

            // Actor
            var actor = string.Join(",", doc.Select("#performer a").Select(a => a.Cq().Text().Trim()));
            if (string.IsNullOrWhiteSpace(actor))
            {
                actor = string.Join(",", doc.Select("td#fn-visibleActor div a").Select(a => a.Cq().Text().Trim()));
            }
            if (string.IsNullOrWhiteSpace(actor))
            {
                actor = string.Join(",", doc.Select("td:contains('出演者')").Parent().Find("td a").Select(a => a.Cq().Text().Trim()));
            }

            // Mosaic (tab)
            var mosaic = doc.Select("li.on a").Text().Trim() == "アニメ" ? "里番" : "有码";

            // Studio/Publisher
            var studio = GetNextCellText("メーカー");
            var publisher = GetNextCellText("レーベル");
            if (string.IsNullOrWhiteSpace(publisher)) publisher = studio;

            // Runtime
            string runtime = GetNextCellText("収録時間");
            var mRuntime = Regex.Match(runtime ?? string.Empty, @"(\d+)");
            runtime = mRuntime.Success ? mRuntime.Groups[1].Value : string.Empty;

            // Series
            var series = GetNextCellText("シリーズ");

            // Release
            var release = GetNextCellText("発売日");
            if (string.IsNullOrWhiteSpace(release)) release = GetNextCellText("配信開始日");
            if (!string.IsNullOrWhiteSpace(release))
            {
                release = release.Replace("/", "-");
                var rm = Regex.Match(release, @"(\d{4}-\d{1,2}-\d{1,2})");
                release = rm.Success ? rm.Groups[1].Value : string.Empty;
            }
            var year = string.IsNullOrWhiteSpace(release) ? string.Empty : Regex.Match(release, @"\d{4}").Value;

            // Tags
            string tag = string.Join(",", doc.Select("td:contains('ジャンル')").Parent().Find("td a").Select(a => a.Cq().Text().Trim()));
            if (string.IsNullOrWhiteSpace(tag))
            {
                tag = string.Join(",", doc.Select("div.info__item table tbody tr th:contains('ジャンル')").Parent().Find("td a").Select(a => a.Cq().Text().Trim()));
            }

            // Cover/Poster
            var cover = doc.Select("meta[property='og:image']").Attr("content") ?? string.Empty;
            string poster = string.Empty;
            if (!string.IsNullOrWhiteSpace(cover))
            {
                poster = cover.Replace("pl.jpg", "ps.jpg");
            }

            // ExtraFanart
            var extraFanart = doc.Select("#sample-image-block a").Select(a => a.GetAttribute("href") ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (extraFanart.Count == 0)
            {
                extraFanart = doc.Select("a[name='sample-image'] img").Select(img => img.GetAttribute("data-lazy") ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            }

            // Score
            var score = doc.Select("p.d-review__average strong").Text().Replace("\n", string.Empty).Replace("点", string.Empty).Trim();

            // Director
            var director = GetNextCellText("監督");

            var detail = new DMMVideoDetail
            {
                Number = number,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Outline = string.Empty,
                OriginalPlot = string.Empty,
                Tag = tag,
                Release = release,
                Year = year,
                Runtime = runtime,
                Score = score,
                Series = series,
                Director = director,
                Studio = studio,
                Publisher = publisher,
                Source = "dmm",
                Website = appointUrl,
                ActorPhoto = string.IsNullOrWhiteSpace(actor) ? new Dictionary<string, string>() : actor.Split(',').Distinct().ToDictionary(a => a, _ => string.Empty),
                CoverUrl = cover,
                PosterUrl = poster,
                ExtraFanart = extraFanart,
                Trailer = string.Empty,
                ImageDownload = false,
                ImageCut = "right",
                Mosaic = mosaic,
                Wanted = string.Empty,
                SearchUrl = appointUrl
            };

            return detail;
        }
        catch
        {
            return null;
        }
    }
}


