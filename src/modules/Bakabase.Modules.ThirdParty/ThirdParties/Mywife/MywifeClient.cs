using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Mywife.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Mywife;

public class MywifeClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<MywifeVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            // Try derive key
            var keyMatch = Regex.Match(number, @"(No\.)?(\d{3,})", RegexOptions.IgnoreCase);
            var key = keyMatch.Success ? keyMatch.Groups[2].Value : "";
            if (string.IsNullOrWhiteSpace(key)) return null;

            string? realUrl = appointUrl;
            if (string.IsNullOrWhiteSpace(realUrl))
            {
                realUrl = $"https://mywife.cc/teigaku/model/no/{key}";
            }

            var html = await HttpClient.GetStringAsync(realUrl);
            var doc = new CQ(html);

            // Title and number from head title
            var headTitle = doc.Select("head title").Text();
            var numTitleMatch = Regex.Match(headTitle, @"(No\.\d*)(.*)");
            var webNum = numTitleMatch.Success ? numTitleMatch.Groups[1].Value : $"No.{key}";
            var title = numTitleMatch.Success ? numTitleMatch.Groups[2].Value.Trim() : "";
            if (string.IsNullOrWhiteSpace(title)) return null;

            var outline = doc.Select("div.modelsamplephototop").Text().Trim();
            var actor = doc.Select("div.modelwaku0 img").Attr("alt") ?? "";
            var cover = doc.Select("#video").Attr("poster") ?? "";
            if (!string.IsNullOrWhiteSpace(cover) && !cover.StartsWith("http", StringComparison.OrdinalIgnoreCase)) cover = "https:" + cover;
            var trailer = doc.Select("#video").Attr("src") ?? "";
            var extrafanart = doc.Select("div.modelsample_photowaku img").Select(i => i.GetAttribute("src") ?? "").ToList();

            var detail = new MywifeVideoDetail
            {
                Number = $"Mywife {webNum}",
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = "",
                Release = "",
                Year = "",
                Runtime = "",
                Series = "",
                Studio = "舞ワイフ",
                Publisher = "舞ワイフ",
                CoverUrl = cover,
                PosterUrl = cover.Replace("topview.jpg", "thumb.jpg"),
                Website = realUrl,
                Source = "mywife",
                Mosaic = "有码"
            };

            return detail;
        }
        catch
        {
            return null;
        }
    }
}


