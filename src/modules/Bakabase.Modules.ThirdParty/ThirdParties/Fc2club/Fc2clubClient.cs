using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Fc2club.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Fc2club;

public class Fc2clubClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<Fc2clubVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            var normalized = number.ToUpperInvariant()
                .Replace("FC2PPV", "")
                .Replace("FC2-PPV-", "")
                .Replace("FC2-", "")
                .Replace("-", "")
                .Trim();

            var realUrl = string.IsNullOrWhiteSpace(appointUrl)
                ? $"https://fc2club.top/html/FC2-{normalized}.html"
                : appointUrl;

            string html;
            try
            {
                html = await HttpClient.GetStringAsync(realUrl);
            }
            catch
            {
                return null;
            }

            var doc = new CQ(html);

            string title = doc.Select("h3").Text().Trim();
            if (!string.IsNullOrEmpty(title))
            {
                title = title.Replace($"FC2-{normalized} ", "").Trim();
            }
            if (string.IsNullOrWhiteSpace(title)) return null;

            // Cover and extrafanart
            var images = doc.Select("img.responsive").Select(i => i.GetAttribute("src")).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            var extrafanart = images.Select(u => u.Replace("../uploadfile", "https://fc2club.top/uploadfile")).ToList();
            var cover = extrafanart.FirstOrDefault() ?? "";

            // Studio (seller)
            var studio = doc.Select("strong:contains('卖家信息')").Parent().Find("a").Text().Trim();
            studio = studio.Replace("本资源官网地址", "").Trim();

            // Score
            string score = "";
            var scoreNode = doc.Select("strong:contains('影片评分')").Parent().Text();
            var mScore = Regex.Match(scoreNode ?? "", @"(\d+)");
            if (mScore.Success) score = mScore.Groups[1].Value;

            // Actor
            var actorText = string.Join(",", doc.Select("strong:contains('女优名字')").Parent().Find("a").Select(a => a.Cq().Text().Trim()))
                .Trim(',');
            var actor = string.IsNullOrWhiteSpace(actorText) ? studio : actorText;

            // Tag
            var tag = string.Join(",", doc.Select("strong:contains('影片标签')").Parent().Find("a").Select(a => a.Cq().Text().Trim()));

            // Mosaic
            var mosaicText = doc.Select("h5:contains('资源参数')").Parent().Text();
            var mosaic = (mosaicText?.Contains("无码") ?? false) ? "无码" : "有码";

            var detail = new Fc2clubVideoDetail
            {
                Number = $"FC2-{normalized}",
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag,
                Release = "",
                Year = "",
                Runtime = "",
                Score = score,
                Series = "FC2系列",
                Studio = studio,
                Publisher = studio,
                CoverUrl = cover,
                PosterUrl = "",
                Website = realUrl,
                Source = "fc2club",
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


