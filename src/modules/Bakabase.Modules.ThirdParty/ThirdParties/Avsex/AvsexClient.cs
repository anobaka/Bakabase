using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsex.Models;
using Bootstrap.Extensions;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Avsex;

public class AvsexClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    private const string DefaultBaseUrl = "https://gg5.co";

    public async Task<AvsexVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null, string? baseUrl = null)
    {
        try
        {
            var processedNumber = Regex.IsMatch(number, @"^n\d{4}$") ? number : number.ToUpper();
            var actualBaseUrl = baseUrl ?? DefaultBaseUrl;

            if (!string.IsNullOrEmpty(appointUrl))
            {
                if (appointUrl.Contains("http"))
                {
                    var urlMatch = Regex.Match(appointUrl, @"(.*//[^/]*)/");
                    if (urlMatch.Success)
                    {
                        actualBaseUrl = urlMatch.Groups[1].Value;
                    }
                }
                else
                {
                    var domainMatch = Regex.Match(appointUrl, @"([^/]*)/");
                    if (domainMatch.Success)
                    {
                        actualBaseUrl = "https://" + domainMatch.Groups[1].Value;
                    }
                }
            }

            string realUrl;
            string posterUrl = "";
            string? searchUrl = null;

            if (!string.IsNullOrEmpty(appointUrl))
            {
                realUrl = appointUrl;
            }
            else
            {
                searchUrl = $"{actualBaseUrl}/tw/search?query={processedNumber.ToLower()}";
                var searchHtml = await HttpClient.GetStringAsync(searchUrl);
                var searchCq = new CQ(searchHtml);

                var (foundUrl, foundPoster) = GetRealUrlFromResults(searchCq, processedNumber);
                if (string.IsNullOrEmpty(foundUrl))
                {
                    return null;
                }

                realUrl = foundUrl;
                posterUrl = foundPoster;
            }

            var detailHtml = await HttpClient.GetStringAsync(realUrl);
            var detailCq = new CQ(detailHtml);

            var title = GetTitle(detailCq);
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }

            var detail = new AvsexVideoDetail
            {
                Number = GetWebNumber(detailCq, processedNumber),
                Title = title,
                OriginalTitle = title,
                Actor = GetActor(detailCq),
                Studio = GetStudio(detailCq),
                Release = GetRelease(detailCq),
                Tag = GetTag(detailCq),
                CoverUrl = GetCover(detailCq),
                PosterUrl = posterUrl,
                Outline = GetOutline(detailCq),
                Runtime = GetRuntime(detailCq).ToString(),
                ExtraFanart = GetExtraFanart(detailCq),
                Website = Regex.Replace(realUrl, @"http[s]?://[^/]+", actualBaseUrl),
                Source = "avsex",
                SearchUrl = searchUrl
            };

            detail.Year = GetYear(detail.Release);
            detail.Mosaic = GetMosaic(detailCq, detail.Studio);

            return detail;
        }
        catch
        {
            return null;
        }
    }

    private static (string url, string poster) GetRealUrlFromResults(CQ html, string number)
    {
        var items = html["ul li a"];

        foreach (var item in items)
        {
            var itemCq = item.Cq();
            var title = itemCq.Find("div h4.truncate").Text();
            var url = itemCq.Attr("href") ?? string.Empty;
            var poster = itemCq.Find("div.relative.overflow-hidden.rounded-t-md img").Attr("src") ?? string.Empty;

            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(url))
            {
                if (title.ToUpper().StartsWith(number.ToUpper()) ||
                    (title.ToUpper().Contains($"{number.ToUpper()}-") && title.Length > 0 && char.IsDigit(title[0])))
                {
                    return (url, poster);
                }
            }
        }

        return ("", "");
    }

    private static string GetWebNumber(CQ html, string defaultNumber)
    {
        var result = html["h2 span.truncate"].Text().Trim();
        return !string.IsNullOrEmpty(result) ? result : defaultNumber;
    }

    private static string GetTitle(CQ html)
    {
        var result = html["span.truncate.p-2.text-primary.font-bold.dark\\:text-primary-200"].Text();
        
        var replaceList = new[]
        {
            "[VIP会员点播] ", "[VIP會員點播] ", "[VIP] ", 
            "★ (请到免费赠片区观赏)", "(破解版獨家中文)"
        };

        foreach (var replacement in replaceList)
        {
            result = result.Replace(replacement, "");
        }

        return result.Trim();
    }

    private static string GetActor(CQ html)
    {
        var actors = html["dd.flex.gap-2.flex-wrap a[href*='actor']"]
            .Select(a => a.Cq().Text().Trim())
            .Where(text => !string.IsNullOrEmpty(text))
            .ToList();

        return string.Join(", ", actors);
    }

    private static string GetOutline(CQ html)
    {
        var outline = html["h2:contains('劇情簡介') + p"].Text();

        var replaceList = new[]
        {
            "(中文字幕1280x720)", "(日本同步最新‧中文字幕1280x720)",
            "(日本同步最新‧中文字幕)", "(日本同步最新‧完整激薄版‧中文字幕1280x720)",
            "＊日本女優＊ 劇情做愛影片 ＊完整日本版＊", "＊日本女優＊ 剧情做爱影片 ＊完整日本版＊",
            "&nbsp;", "<br/>", "<p>", "</p>", "★ (请到免费赠片区观赏)"
        };

        foreach (var replacement in replaceList)
        {
            outline = outline.Replace(replacement, "");
        }

        return outline.Trim();
    }

    private static string GetStudio(CQ html)
    {
        var text = html["dt:contains('製作商') + dd"].Text();
        return text.Trim();
    }

    private static int GetRuntime(CQ html)
    {
        var text = html["dt:contains('片長') + dd"].Text().Trim();
        var numbers = Regex.Matches(text, @"\d+").Cast<Match>().Select(m => int.Parse(m.Value)).ToArray();
        if (numbers.Length >= 2)
        {
            return numbers[0] * 60 + numbers[1];
        }
        if (numbers.Length == 1)
        {
            return numbers[0];
        }
        return 0;
    }

    private static string GetRelease(CQ html)
    {
        var text = html["dt:contains('上架日') + dd"].Text();
        return text.Replace("/", "-").Trim();
    }

    private static string GetYear(string releaseDate)
    {
        var match = Regex.Match(releaseDate, @"\d{4}");
        return match.Success ? match.Value : releaseDate;
    }

    private static string GetTag(CQ html)
    {
        var tags = html["dt:contains('類別') ~ dd a"]
            .Select(a => a.Cq().Attr("title"))
            .Where(title => !string.IsNullOrEmpty(title))
            .ToList();

        return string.Join(", ", tags);
    }

    private static string GetCover(CQ html)
    {
        var coverUrl = html["div.relative.overflow-hidden.rounded-md img"].Attr("src");
        return coverUrl ?? "";
    }

    private static string[] GetExtraFanart(CQ html)
    {
        var fanartUrls = html["h2:contains('精彩劇照') + ul li div.relative.overflow-hidden.rounded-md img"]
            .Select(img => img.Cq().Attr("src"))
            .Where(src => !string.IsNullOrEmpty(src))
            .ToArray();
        return fanartUrls;
    }

    private static string GetMosaic(CQ html, string studio)
    {
        var title = html["h1.vv_title.col-12"].Text();
        var mosaic = (title.Contains("無碼") && !title.Contains("破解版")) ? "无码" : "有码";
        return studio.Contains("國產") ? "国产" : mosaic;
    }
}