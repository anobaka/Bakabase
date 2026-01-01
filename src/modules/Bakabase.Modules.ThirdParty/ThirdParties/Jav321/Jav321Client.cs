using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Jav321.Models;
using Microsoft.Extensions.Logging;
using CsQuery;
using System.Text.RegularExpressions;
using System.Net.Http;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Jav321;

public class Jav321Client(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<Jav321VideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            var url = appointUrl ?? "https://www.jav321.com/search";
            string response;
            if (appointUrl == null)
            {
                var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("sn", number) });
                response = await HttpClient.PostAsync(url, content).Result.Content.ReadAsStringAsync();
            }
            else
            {
                response = await HttpClient.GetStringAsync(url);
            }

            if (response.Contains("AVが見つかりませんでした")) return null;
            var detailPage = new CQ(response);
            var website = "https:" + detailPage["a:contains('简体中文')"].Attr("href");

            var actor = ExtractActor(response);
            var title = Regex.Match(response, @"<h3>(.+) <small>").Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(title)) return null;
            var coverUrl = GetCover(detailPage);
            var posterUrl = detailPage["img.img-responsive"].Attr("src") ?? string.Empty;
            if (string.IsNullOrEmpty(coverUrl)) coverUrl = posterUrl;
            var release = Regex.Match(response, @"<b>配信開始日</b>: (\d+-\d+-\d+)").Groups[1].Value;
            var year = Regex.Match(release ?? string.Empty, @"\d{4}").Value;
            var runtime = Regex.Match(response, @"<b>収録時間</b>: (\d+) ").Groups[1].Value;
            var webNumMatch = Regex.Match(response, @"<b>品番</b>: (\S+)<br>");
            var webNumber = webNumMatch.Success ? webNumMatch.Groups[1].Value.Trim().ToUpper() : number;
            var outline = detailPage["div.col-md-9 div[itemprop] + div"].Text();
            var tags = Regex.Matches(response, @"<a href=""/genre/\S+"">(\S+)</a>").Select(m => m.Groups[1].Value);
            var score = ExtractScore(response);
            var studio = detailPage["a[href*='/company/']"].Text();
            var series = detailPage["a[href*='/series/']"].Text();
            var extraFanart = detailPage["div.col-md-3 img.img-responsive"].Select(img => img.GetAttribute("src")).ToArray();
            var mosaic = "有码";

            return new Jav321VideoDetail
            {
                Number = webNumber,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = string.Join(",", tags),
                Release = release,
                Year = year,
                Runtime = runtime,
                Series = series,
                Studio = studio,
                Publisher = studio,
                Source = "jav321",
                CoverUrl = coverUrl,
                PosterUrl = posterUrl,
                Website = website,
                Mosaic = mosaic,
                SearchUrl = url
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GetCover(CQ detailPage)
    {
        var cover = detailPage["div.col-md-3 p a img.img-responsive"].Attr("src") ?? string.Empty;
        if (string.IsNullOrEmpty(cover))
        {
            cover = detailPage["#vjs_sample_player"].Attr("poster") ?? string.Empty;
        }
        return cover;
    }

    private static string ExtractActor(string response)
    {
        var m1 = Regex.Matches(response, @"<a href=""/star/\S+"">(\S+)</a> &nbsp;");
        if (m1.Count > 0) return string.Join(",", m1.Select(m => m.Groups[1].Value));
        var m2 = Regex.Matches(response, @"<a href=""/heyzo_star/\S+"">(\S+)</a> &nbsp;");
        if (m2.Count > 0) return string.Join(",", m2.Select(m => m.Groups[1].Value));
        var m3 = Regex.Match(response, @"<b>出演者</b>: ([^<]+) &nbsp; <br>");
        return m3.Success ? m3.Groups[1].Value : string.Empty;
    }

    private static string ExtractScore(string response)
    {
        var m = Regex.Match(response, @"<b>平均評価</b>: <img data-original=""/img/(\d+).gif"" />");
        if (m.Success) return (int.Parse(m.Groups[1].Value) / 10.0).ToString();
        var m2 = Regex.Match(response, @"<b>平均評価</b>: ([^<]+)<br>");
        return m2.Success ? m2.Groups[1].Value : string.Empty;
    }
}


