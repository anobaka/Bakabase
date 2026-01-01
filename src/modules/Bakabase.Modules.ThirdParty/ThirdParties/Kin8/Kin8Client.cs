using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Kin8.Models;
using Microsoft.Extensions.Logging;
using CsQuery;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Kin8;

public class Kin8Client(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<Kin8VideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            string key;
            var realUrl = appointUrl ?? "";
            if (!string.IsNullOrEmpty(realUrl))
            {
                var m = Regex.Match(realUrl, @"(\d{3,})");
                key = m.Success ? m.Groups[1].Value : string.Empty;
                if (!string.IsNullOrEmpty(key))
                {
                    number = $"KIN8-{key}";
                }
            }
            else
            {
                var m = Regex.Match(number.ToUpper(), @"KIN8(TENGOKU)?-?(\d{3,})");
                key = m.Success ? m.Groups[2].Value : string.Empty;
                if (string.IsNullOrEmpty(key))
                {
                    return null;
                }
                number = $"KIN8-{key}";
                realUrl = $"https://www.kin8tengoku.com/moviepages/{key}/index.html";
            }

            var html = await HttpClient.GetStringAsync(realUrl);
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var cq = new CQ(html);
            var title = cq["p.sub_title"].Text();
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var outline = cq["#comment"].Text().Trim();
            var actor = string.Join(",", cq["div.icon a[href*='listpages/actor']"].Select(a => a.InnerText));
            var categories = cq["td.movie_table_td:contains('カテゴリー')"].Parent().Find("td div a").Select(a => a.InnerText);
            var tag = string.Join(",", categories);
            var release = cq["td.movie_table_td:contains('更新日')"].Parent().Find("td").Eq(1).Text();
            var year = Regex.Match(release ?? string.Empty, @"\d{4}").Value;
            var playTime = cq["td.movie_table_td:contains('再生時間')"].Parent().Find("td").Eq(1).Text();
            var runtime = ExtractRuntimeMinutes(playTime);

            var coverUrl = $"https://www.kin8tengoku.com/{key}/pht/1.jpg";
            var posterUrl = coverUrl;

            return new Kin8VideoDetail
            {
                Number = number,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag,
                Release = release,
                Year = string.IsNullOrEmpty(year) ? release : year,
                Runtime = runtime,
                Series = "",
                Studio = "kin8tengoku",
                Publisher = "kin8tengoku",
                Source = "kin8",
                CoverUrl = coverUrl,
                PosterUrl = posterUrl,
                Website = realUrl,
                Mosaic = "无码",
                SearchUrl = realUrl
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractRuntimeMinutes(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(":"))
        {
            var parts = value.Split(':');
            if (parts.Length == 3 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m))
            {
                return (h * 60 + m).ToString();
            }
            if (parts.Length <= 2 && int.TryParse(parts[0], out var mm))
            {
                return mm.ToString();
            }
        }
        return Regex.Replace(value, @"\D+", "").Trim();
    }
}


