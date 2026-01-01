using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.AiravCC.Models;
using Bootstrap.Extensions;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.AiravCC;

public class AiravCCClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    private const string DefaultBaseUrl = "https://airav.io";

    public async Task<AiravCCVideoDetail?> SearchAndParseVideo(string number, string language = "zh_cn", string? appointUrl = null, string? baseUrl = null)
    {
        try
        {
            var processedNumber = number.ToUpper();
            if (Regex.IsMatch(processedNumber, @"^N\d{4}$"))
            {
                processedNumber = processedNumber.ToLower();
            }

            var actualBaseUrl = baseUrl ?? DefaultBaseUrl;
            if (language == "zh_cn")
            {
                actualBaseUrl += "/cn";
            }

            string realUrl;
            string? searchUrl = null;

            if (!string.IsNullOrEmpty(appointUrl))
            {
                realUrl = appointUrl;
            }
            else
            {
                searchUrl = $"{actualBaseUrl}/search_result?kw={processedNumber}";
                var searchHtml = await HttpClient.GetStringAsync(searchUrl);
                var searchCq = new CQ(searchHtml);

                var videoLinks = searchCq["div.col.oneVideo a[href]"]
                    .Select(a => a.GetAttribute("href"))
                    .Where(href => !string.IsNullOrEmpty(href))
                    .ToList();

                if (!videoLinks.Any())
                {
                    return null;
                }

                if (videoLinks.Count == 1)
                {
                    realUrl = videoLinks.First();
                }
                else
                {
                    realUrl = GetRealUrlFromResults(searchCq, processedNumber);
                    if (string.IsNullOrEmpty(realUrl))
                    {
                        return null;
                    }
                }

                if (realUrl.StartsWith("/"))
                {
                    realUrl = new Uri(new Uri(actualBaseUrl), realUrl).ToString();
                }
            }

            var detailHtml = await HttpClient.GetStringAsync(realUrl);
            var detailCq = new CQ(detailHtml);

            var title = GetTitle(detailCq);
            if (string.IsNullOrEmpty(title) || title.Contains("克破"))
            {
                return null;
            }

            var webNumber = GetWebNumber(detailCq);
            var webNumberBrackets = $"[{webNumber}]";
            title = title.Replace(webNumberBrackets, "").Trim();

            var outline = GetOutline(detailCq);
            if (outline.Contains("克破"))
            {
                return null;
            }

            var detail = new AiravCCVideoDetail
            {
                Number = !string.IsNullOrEmpty(processedNumber) ? processedNumber : webNumber,
                Title = CleanTitle(title),
                OriginalTitle = CleanTitle(title),
                Actor = GetActor(detailCq),
                Studio = GetStudio(detailCq),
                Release = GetRelease(detailCq),
                Tag = GetTag(detailCq),
                CoverUrl = GetCover(detailCq, actualBaseUrl),
                Outline = CleanOutline(outline),
                Series = GetSeries(detailCq),
                Website = realUrl,
                Source = "airav_cc",
                SearchUrl = searchUrl
            };

            detail.Year = GetYear(detail.Release);
            detail.PosterUrl = detail.CoverUrl?.Replace("big_pic", "small_pic");
            detail.Mosaic = GetMosaic(detail.Tag);

            return detail;
        }
        catch
        {
            return null;
        }
    }

    private static string GetRealUrlFromResults(CQ searchHtml, string number)
    {
        var videoItems = searchHtml["div.col.oneVideo"];
        foreach (var item in videoItems)
        {
            var itemCq = item.Cq();
            var href = itemCq.Find("a").Attr("href");
            var title = itemCq.Find("h5").Text();
            
            if (!string.IsNullOrEmpty(href) && !string.IsNullOrEmpty(title) && 
                title.Contains(number.ToUpper()) && 
                !title.Contains("克破") && !title.Contains("无码破解") && !title.Contains("無碼破解"))
            {
                return href;
            }
        }
        return "";
    }

    private static string GetWebNumber(CQ html)
    {
        var elements = html["*:contains('番號'), *:contains('番号')"];
        foreach (var element in elements)
        {
            var spans = element.Cq().Find("span");
            if (spans.Any())
            {
                return spans.Text().Trim();
            }
        }
        return "";
    }

    private static string GetTitle(CQ html)
    {
        var result = html["div.video-title.my-3 h1"].Text().Trim();
        return result;
    }

    private static string GetActor(CQ html)
    {
        var elements = html["*:contains('女優'), *:contains('女优')"];
        var actors = new List<string>();
        
        foreach (var element in elements)
        {
            var links = element.Cq().Find("a");
            actors.AddRange(links.Select(a => a.Cq().Text().Trim()).Where(text => !string.IsNullOrEmpty(text)));
        }
        
        return string.Join(", ", actors.Distinct());
    }

    private static string GetStudio(CQ html)
    {
        var elements = html["*:contains('廠商'), *:contains('厂商')"];
        foreach (var element in elements)
        {
            var linkText = element.Cq().Find("a").Text();
            if (!string.IsNullOrWhiteSpace(linkText))
            {
                return linkText.Trim();
            }
        }
        return "";
    }

    private static string GetRelease(CQ html)
    {
        var element = html["i.fa.fa-clock.me-2"].Parent();
        if (element != null)
        {
            var text = element.Text();
            var match = Regex.Match(text, @"\d{4}-\d{2}-\d{2}");
            return match.Success ? match.Value : "";
        }
        return "";
    }

    private static string GetYear(string releaseDate)
    {
        var match = Regex.Match(releaseDate, @"\d{4}");
        return match.Success ? match.Value : releaseDate;
    }

    private static string GetTag(CQ html)
    {
        var elements = html["*:contains('標籤'), *:contains('标籤')"];
        var tags = new List<string>();
        
        foreach (var element in elements)
        {
            var links = element.Cq().Find("a");
            tags.AddRange(links.Select(a => a.Cq().Text().Trim()).Where(text => !string.IsNullOrEmpty(text)));
        }
        
        return string.Join(", ", tags.Distinct());
    }

    private static string GetCover(CQ html, string baseUrl)
    {
        var scriptElements = html["script[type='application/ld+json']"];
        foreach (var script in scriptElements)
        {
            try
            {
                var jsonContent = script.InnerText;
                var jsonDoc = JsonDocument.Parse(jsonContent);
                
                if (jsonDoc.RootElement.TryGetProperty("thumbnailUrl", out var thumbnailElement) &&
                    thumbnailElement.ValueKind == JsonValueKind.Array &&
                    thumbnailElement.GetArrayLength() > 0)
                {
                    var coverUrl = thumbnailElement[0].GetString();
                    if (!string.IsNullOrEmpty(coverUrl))
                    {
                        if (coverUrl.StartsWith("/"))
                        {
                            return new Uri(new Uri(baseUrl), coverUrl).ToString();
                        }
                        return coverUrl;
                    }
                }
            }
            catch
            {
                continue;
            }
        }
        return "";
    }

    private static string GetOutline(CQ html)
    {
        var result = html["div.video-info p"].Text().Trim();
        return result;
    }

    private static string GetSeries(CQ html)
    {
        var elements = html["*:contains('系列')"];
        foreach (var element in elements)
        {
            var linkText = element.Cq().Find("a").Text();
            if (!string.IsNullOrWhiteSpace(linkText))
            {
                return linkText.Trim();
            }
        }
        return "";
    }

    private static string CleanTitle(string title)
    {
        var titleReplaceList = new[] { "第一集", "第二集", " - 上", " - 下", " 上集", " 下集", " -上", " -下" };
        foreach (var replacement in titleReplaceList)
        {
            title = title.Replace(replacement, "").Trim();
        }
        return title;
    }

    private static string CleanOutline(string outline)
    {
        if (string.IsNullOrEmpty(outline))
            return "";
            
        outline = Regex.Replace(outline, @"[\n\t]", "");
        var parts = outline.Split(new[] { "*根据分发" }, StringSplitOptions.None);
        return parts[0].Trim();
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