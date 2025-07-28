using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Fc2ppvdb.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Fc2ppvdb;

public class Fc2ppvdbClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<Fc2ppvdbVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            var normalized = number.ToUpperInvariant()
                .Replace("FC2PPV", "")
                .Replace("FC2-PPV-", "")
                .Replace("FC2-", "")
                .Replace("-", "")
                .Trim();

            var url = $"https://fc2ppvdb.com/articles/{normalized}";
            string html;
            try { html = await HttpClient.GetStringAsync(url); } catch { return null; }
            var doc = new CQ(html);

            var title = doc.Select("h2 a").Text().Trim();
            if (string.IsNullOrWhiteSpace(title)) return null;

            var cover = doc.Select($"img[alt*='{normalized}']").Attr("src") ?? "";
            if (string.IsNullOrWhiteSpace(cover) || !cover.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;

            var release = doc.Select("div:contains('販売日：') span").Text().Trim();
            var year = string.IsNullOrWhiteSpace(release) ? "" : release.Substring(0, Math.Min(4, release.Length));
            var actor = string.Join(",", doc.Select("div:contains('女優：') span a").Select(a => a.Cq().Text().Trim()));
            var tag = string.Join(",", doc.Select("div:contains('タグ：') span a").Select(a => a.Cq().Text().Trim()));
            var studio = doc.Select("div:contains('販売者：') span a").Text().Trim();
            var mosaicStr = doc.Select("div:contains('モザイク：') span").Text().Trim();
            var mosaic = mosaicStr == "無" ? "无码" : "有码";
            var trailer = doc.Select("a:contains('サンプル動画')").Attr("href") ?? "";
            var videoTime = doc.Select("div:contains('収録時間：') span").Text().Trim();

            var detail = new Fc2ppvdbVideoDetail
            {
                Number = $"FC2-{normalized}",
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag.Replace("無修正,", "").Replace("無修正", "").Trim(','),
                Release = release,
                Year = year,
                Runtime = videoTime,
                Series = "FC2系列",
                Studio = studio,
                Publisher = studio,
                CoverUrl = cover,
                PosterUrl = cover,
                Website = url,
                Source = "fc2",
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


