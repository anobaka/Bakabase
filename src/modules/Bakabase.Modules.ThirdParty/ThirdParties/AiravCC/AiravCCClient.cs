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
        var result = html["div.video-title.my-3 h1"].First().Text().Trim();
        return result;
    }

    private static string GetActor(CQ html)
    {
        var links = FindLinksNearLabel(html, "女優", "女优");
        return string.Join(", ", links);
    }

    private static string GetStudio(CQ html)
    {
        var links = FindLinksNearLabel(html, "廠商", "厂商");
        return links.FirstOrDefault() ?? "";
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
        var links = FindLinksNearLabel(html, "標籤", "标籤", "标签");
        return string.Join(", ", links);
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
        var links = FindLinksNearLabel(html, "系列");
        return links.FirstOrDefault() ?? "";
    }

    /// <summary>
    /// Finds an element whose own text nodes (not descendants) contain one of the label texts,
    /// then returns text from &lt;a&gt; elements within that element's scope.
    /// This avoids the problem where <c>*:contains('label')</c> matches &lt;body&gt; and grabs
    /// every link on the page.
    /// </summary>
    private static List<string> FindLinksNearLabel(CQ html, params string[] labelTexts)
    {
        var selectorParts = labelTexts.Select(l => $"*:contains('{l}')");
        var candidates = html[string.Join(", ", selectorParts)];

        foreach (var el in candidates)
        {
            // Skip <a> elements - they are links themselves, not labels
            if (el.NodeName.Equals("A", StringComparison.OrdinalIgnoreCase))
                continue;

            var cq = el.Cq();

            // Check if this element's own text nodes (not descendant text) contain the label
            var hasDirectLabel = cq.Contents()
                .Any(child => child.NodeType == NodeType.TEXT_NODE &&
                              labelTexts.Any(l => child.NodeValue?.Contains(l) == true));

            if (!hasDirectLabel)
                continue;

            // Found the label element. Get links from this element.
            var links = cq.Find("a")
                .Select(a => a.Cq().Text().Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct()
                .ToList();

            if (links.Count > 0)
                return links;

            // If no child links, try the parent element
            links = cq.Parent().Find("a")
                .Select(a => a.Cq().Text().Trim())
                .Where(t => !string.IsNullOrEmpty(t) && labelTexts.All(l => !t.Contains(l)))
                .Distinct()
                .ToList();

            if (links.Count > 0)
                return links;
        }

        return [];
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