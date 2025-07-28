using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsox.Models;
using Bootstrap.Extensions;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Avsox;

public class AvsoxClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<AvsoxVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null, string? baseUrl = null)
    {
        try
        {
            var actualBaseUrl = baseUrl ?? "https://avsox.click"; // Default fallback
            string realUrl;
            string posterUrl = "";
            bool imageDownload = false;

            if (!string.IsNullOrEmpty(appointUrl))
            {
                realUrl = appointUrl;
            }
            else
            {
                var searchUrl = $"{actualBaseUrl}/cn/search/{number}";
                var searchHtml = await HttpClient.GetStringAsync(searchUrl);
                var searchCq = new CQ(searchHtml);

                var (foundUrl, count) = GetRealUrlFromResults(number, searchCq, actualBaseUrl);
                if (string.IsNullOrEmpty(foundUrl))
                {
                    return null;
                }

                realUrl = foundUrl;
                posterUrl = GetPosterFromResults(searchCq, count);
                if (!string.IsNullOrEmpty(posterUrl))
                {
                    imageDownload = true;
                }
            }

            var detailHtml = await HttpClient.GetStringAsync(realUrl);
            var detailCq = new CQ(detailHtml);

            var webNumber = GetWebNumber(detailCq);
            var title = GetTitle(detailCq);
            if (!string.IsNullOrEmpty(webNumber))
            {
                title = title.Replace($"{webNumber} ", "").Trim();
            }

            if (string.IsNullOrEmpty(title))
            {
                return null;
            }

            var detail = new AvsoxVideoDetail
            {
                Number = number,
                Title = title,
                OriginalTitle = title,
                Actor = GetActor(detailCq),
                Studio = GetStudio(detailCq),
                Release = GetRelease(detailCq),
                Tag = GetTag(detailCq),
                CoverUrl = GetCover(detailCq),
                PosterUrl = posterUrl,
                Runtime = GetRuntime(detailCq),
                Series = GetSeries(detailCq),
                Website = realUrl,
                Source = "avsox",
                Mosaic = "无码",
                ImageDownload = imageDownload,
                ImageCut = "center"
            };

            detail.Year = GetYear(detail.Release);
            detail.Publisher = detail.Studio; // Publisher same as studio in avsox

            return detail;
        }
        catch
        {
            return null;
        }
    }

    private static (string url, int count) GetRealUrlFromResults(string number, CQ html, string baseUrl)
    {
        var urlList = html["#waterfall div a"].Select(a => a.Cq().Attr("href")).Where(href => !string.IsNullOrEmpty(href)).ToArray();
        
        for (int i = 1; i <= urlList.Length; i++)
        {
            var numberGet = html[$"#waterfall div:nth-child({i}) a div.photo-info span date:first-child"].Text().Trim();
            
            if (string.Equals(number.ToUpper().Replace("-PPV", ""), numberGet.ToUpper().Replace("-PPV", ""), StringComparison.OrdinalIgnoreCase))
            {
                var url = urlList[i - 1];
                if (url.StartsWith("//"))
                {
                    url = "https:" + url;
                }
                else if (url.StartsWith("/"))
                {
                    url = baseUrl + url;
                }
                return (url, i);
            }
        }

        return ("", 0);
    }

    private static string GetPosterFromResults(CQ html, int count)
    {
        if (count <= 0) return "";
        
        var poster = html[$"#waterfall div:nth-child({count}) a div.photo-frame img"].Attr("src");
        return poster ?? "";
    }

    private static string GetActor(CQ html)
    {
        var actors = html["#avatar-waterfall a span"]
            .Select(span => span.Cq().Text().Trim())
            .Where(text => !string.IsNullOrEmpty(text))
            .ToList();

        return string.Join(", ", actors);
    }

    private static string GetWebNumber(CQ html)
    {
        return html["div.col-md-3.info p span[style*='color:#CC0000']"].Text();
    }

    private static string GetTitle(CQ html)
    {
        return html["div.container h3"].Text();
    }

    private static string GetCover(CQ html)
    {
        return html["a.bigImage"].Attr("href") ?? "";
    }

    private static string GetTag(CQ html)
    {
        var tags = html["span.genre a"]
            .Select(a => a.Cq().Text().Trim())
            .Where(text => !string.IsNullOrEmpty(text))
            .ToList();

        return string.Join(", ", tags);
    }

    private static string GetRelease(CQ html)
    {
        var elements = html["span:contains('发行时间:'), span:contains('發行日期:'), span:contains('発売日:')"];
        
        foreach (var element in elements)
        {
            var parent = element.ParentNode;
            if (parent != null)
            {
                var textNodes = parent.ChildNodes.Where(n => n.NodeType == NodeType.TEXT_NODE);
                var releaseText = string.Join("", textNodes.Select(n => n.NodeValue)).Trim();
                if (!string.IsNullOrEmpty(releaseText))
                {
                    return releaseText;
                }
            }
        }

        return "";
    }

    private static string GetYear(string release)
    {
        if (string.IsNullOrEmpty(release) || release.Length < 4)
            return release;
        
        return release.Substring(0, 4);
    }

    private static string GetRuntime(CQ html)
    {
        var elements = html["span:contains('长度:'), span:contains('長度:'), span:contains('収録時間:')"];
        
        foreach (var element in elements)
        {
            var parent = element.ParentNode;
            if (parent != null)
            {
                var textNodes = parent.ChildNodes.Where(n => n.NodeType == NodeType.TEXT_NODE);
                var runtimeText = string.Join("", textNodes.Select(n => n.NodeValue));
                var match = Regex.Match(runtimeText, @"(\d+)");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }

        return "";
    }

    private static string GetSeries(CQ html)
    {
        return html["p a[href*='/series/']"].Text().Trim();
    }

    private static string GetStudio(CQ html)
    {
        return html["p a[href*='/studio/']"].Text().Trim();
    }
}