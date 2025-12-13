using System.Net;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Airav.Models;
using Bootstrap.Extensions;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Airav;

public class AiravClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    private const string ChineseSimplifiedUrl = "https://cn.airav.wiki";
    private const string ChineseTraditionalUrl = "https://www.airav.wiki";
    private const string JapaneseUrl = "https://jp.airav.wiki";

    public async Task<AiravVideoDetail?> SearchAndParseVideo(string number, string language = "zh_cn", string? appointUrl = null)
    {
        try
        {
            var processedNumber = number.ToUpper();
            if (Regex.IsMatch(processedNumber, @"^N\d{4}$"))
            {
                processedNumber = processedNumber.ToLower();
            }

            var baseUrl = GetBaseUrlByLanguage(language);
            string realUrl;
            string? searchUrl = null;

            if (!string.IsNullOrEmpty(appointUrl))
            {
                realUrl = appointUrl;
            }
            else
            {
                searchUrl = $"{baseUrl}/?search={processedNumber}";
                var searchHtml = await HttpClient.GetStringAsync(searchUrl);
                var searchCq = new CQ(searchHtml);

                var videoLinks = searchCq["div.coverImageBox img.img-fluid.video-item.coverImage"]
                    .Where(img => img.GetAttribute("alt")?.Contains(processedNumber) == true && 
                                 !img.GetAttribute("alt")?.Contains("克破") == true)
                    .Select(img => img.ParentNode?.ParentNode?.GetAttribute("href"))
                    .Where(href => !string.IsNullOrEmpty(href))
                    .ToList();

                if (!videoLinks.Any())
                {
                    return null;
                }

                realUrl = $"{baseUrl}{videoLinks.First()}";
            }

            var detailHtml = await HttpClient.GetStringAsync(realUrl);
            var detailCq = new CQ(detailHtml);

            var webNumber = GetWebNumber(detailCq);
            var title = GetTitle(detailCq);
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }

            title = title.Replace(webNumber, "").Trim();

            var detail = new AiravVideoDetail
            {
                Number = processedNumber,
                Title = title,
                OriginalTitle = title,
                Actor = GetActor(detailCq),
                Studio = GetStudio(detailCq),
                Release = GetRelease(detailCq),
                Tag = GetTag(detailCq),
                CoverUrl = GetCover(detailCq),
                Outline = await GetOutline(detailCq, language, realUrl),
                Website = realUrl,
                Source = "airav",
                SearchUrl = searchUrl
            };

            detail.Year = GetYear(detail.Release);
            detail.Mosaic = GetMosaic(detail.Tag);

            return detail;
        }
        catch
        {
            return null;
        }
    }

    private static string GetBaseUrlByLanguage(string language)
    {
        return language switch
        {
            "zh_cn" => ChineseSimplifiedUrl,
            "zh_tw" => ChineseTraditionalUrl,
            "jp" => JapaneseUrl,
            _ => ChineseTraditionalUrl
        };
    }

    private static string GetWebNumber(CQ html)
    {
        var result = html["h5.d-none.d-md-block.text-primary.mb-3"].Text().Trim();
        return result;
    }

    private static string GetTitle(CQ html)
    {
        var result = html["h5.d-none.d-md-block"].Text().Trim();
        return result;
    }

    private static string GetActor(CQ html)
    {
        var actors = html["li.videoAvstarListItem a"].Select(a => a.Cq().Text()).ToArray();
        return string.Join(", ", actors);
    }

    private static string GetStudio(CQ html)
    {
        var result = html["a[href*='video_factory']"].Text().Trim();
        return result;
    }

    private static string GetRelease(CQ html)
    {
        var listItems = html["ul.list-unstyled.pl-2 li"].Select(li => li.Cq().Text()).ToArray();
        return listItems.LastOrDefault()?.Trim() ?? "";
    }

    private static string GetYear(string releaseDate)
    {
        var yearMatch = Regex.Match(releaseDate, @"\d{4}");
        return yearMatch.Success ? yearMatch.Value : releaseDate;
    }

    private static string GetTag(CQ html)
    {
            var tags = html["div.tagBtnMargin a"].Select(a => a.Cq().Text()).ToArray();
        return string.Join(", ", tags);
    }

    private static string GetCover(CQ html)
    {
        var coverUrl = html["div.videoPlayerMobile.d-none div img"].Attr("src");
        return coverUrl?.Trim() ?? "";
    }

    private async Task<string> GetOutline(CQ html, string language, string realUrl)
    {
        try
        {
            if (language == "zh_cn")
            {
                var traditionalUrl = realUrl.Replace("cn.airav.wiki", "www.airav.wiki").Replace("zh_CN", "zh_TW");
                var traditionalHtml = await HttpClient.GetStringAsync(traditionalUrl);
                html = new CQ(traditionalHtml);
            }

            var outline = html["div.synopsis p"].Text().Trim();
            return outline;
        }
        catch
        {
            return "";
        }
    }

    private static string GetMosaic(string tag)
    {
        var lowerTag = tag.ToLower();
        if (lowerTag.Contains("无码") || lowerTag.Contains("無修正") || lowerTag.Contains("無码") || lowerTag.Contains("uncensored"))
        {
            return "无码";
        }
        return "有码";
    }
}