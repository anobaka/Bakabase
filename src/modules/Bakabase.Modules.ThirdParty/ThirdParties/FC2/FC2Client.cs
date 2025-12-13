using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.FC2.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.FC2;

public class FC2Client(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    private const string BaseUrl = "https://adult.contents.fc2.com";

    public async Task<FC2VideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            // Process number - remove FC2 prefixes and clean up
            var processedNumber = ProcessNumber(number);
            
            // Build URL
            string realUrl;
            if (!string.IsNullOrEmpty(appointUrl))
            {
                realUrl = appointUrl;
            }
            else
            {
                realUrl = $"{BaseUrl}/article/{processedNumber}/";
            }

            // Fetch HTML content
            var html = await HttpClient.GetStringAsync(realUrl);
            var cq = new CQ(html);

            // Check if video exists
            var title = GetTitle(cq);
            if (string.IsNullOrEmpty(title) || title.Contains("お探しの商品が見つかりません"))
            {
                Logger.LogWarning("FC2 video not found for number: {Number}", processedNumber);
                return null;
            }

            // Extract cover and extra fanart
            var (coverUrl, extraFanart) = GetCover(cq);
            if (string.IsNullOrEmpty(coverUrl) || !coverUrl.Contains("http"))
            {
                Logger.LogWarning("Failed to get cover for FC2 video: {Number}", processedNumber);
                return null;
            }

            // Extract all other data
            var posterUrl = GetCoverSmall(cq);
            var outline = GetOutline(cq);
            var tag = GetTag(cq);
            var release = GetRelease(cq);
            var studio = GetStudio(cq);
            var mosaic = GetMosaic(tag, title);
            
            // Clean up tags - remove 無修正
            tag = tag.Replace("無修正,", "").Replace("無修正", "").Trim(',');

            // Build actor photo dictionary
            var actorPhoto = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(studio))
            {
                actorPhoto[studio] = "";
            }

            var detail = new FC2VideoDetail
            {
                Number = "FC2-" + processedNumber,
                Title = title,
                OriginalTitle = title,
                Actor = studio, // Using seller as actor per Python script logic
                Outline = outline,
                OriginalPlot = outline,
                Tag = tag,
                Release = release,
                Year = GetYear(release),
                Runtime = "",
                Score = "",
                Series = "FC2系列",
                Director = "",
                Studio = studio,
                Publisher = studio,
                Source = "fc2",
                Website = realUrl,
                ActorPhoto = actorPhoto,
                CoverUrl = coverUrl,
                PosterUrl = posterUrl,
                ExtraFanart = extraFanart,
                Trailer = "",
                ImageDownload = false,
                ImageCut = "center",
                Mosaic = mosaic,
                Wanted = "",
                SearchUrl = realUrl
            };

            Logger.LogInformation("Successfully parsed FC2 video: {Number} - {Title}", processedNumber, title);
            return detail;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error parsing FC2 video {Number}: {Message}", number, ex.Message);
            return null;
        }
    }

    private static string ProcessNumber(string number)
    {
        return number.ToUpper()
            .Replace("FC2PPV", "")
            .Replace("FC2-PPV-", "")
            .Replace("FC2-", "")
            .Replace("-", "")
            .Trim();
    }

    private static string GetTitle(CQ html)
    {
        // XPath: //div[@data-section="userInfo"]//h3/span/../text()
        var titleElements = html["div[data-section='userInfo'] h3 span"].Parent();
        var title = titleElements.Text().Trim();
        
        // Remove the span content to get just the text nodes
        var span = html["div[data-section='userInfo'] h3 span"];
        var spanText = span.Text();
        title = title.Replace(spanText, "").Trim();
        
        return title;
    }

    private static (string coverUrl, List<string> extraFanart) GetCover(CQ html)
    {
        // XPath: //ul[@class="items_article_SampleImagesArea"]/li/a/@href
        var links = html["ul.items_article_SampleImagesArea li a"];
        var extraFanart = new List<string>();
        
        foreach (var link in links)
        {
            var href = link.Cq().Attr("href");
            if (!string.IsNullOrEmpty(href))
            {
                var fullUrl = href.StartsWith("//") ? "https:" + href : href;
                extraFanart.Add(fullUrl);
            }
        }

        var coverUrl = extraFanart.FirstOrDefault() ?? "";
        return (coverUrl, extraFanart);
    }

    private static string GetCoverSmall(CQ html)
    {
        // XPath: //div[@class="items_article_MainitemThumb"]/span/img/@src
        var src = html["div.items_article_MainitemThumb span img"].Attr("src");
        
        if (!string.IsNullOrEmpty(src) && !src.StartsWith("http"))
        {
            src = "https:" + src;
        }
        
        return src ?? "";
    }

    private static string GetRelease(CQ html)
    {
        // XPath: //div[@class="items_article_Releasedate"]/p/text()
        var releaseElement = html["div.items_article_Releasedate p"];
        var releaseText = releaseElement.Text();
        
        // Extract date pattern like "2023/01/15"
        var match = Regex.Match(releaseText, @"\d+/\d+/\d+");
        if (match.Success)
        {
            return match.Value.Replace("/", "-");
        }
        
        return "";
    }

    private static string GetStudio(CQ html)
    {
        // XPath: //div[@class="items_article_headerInfo"]/ul/li[last()]/a/text()
        var lastLi = html["div.items_article_headerInfo ul li"].Last();
        var text = lastLi?.Find("a").Text();
        return text?.Trim() ?? "";
    }

    private static string GetTag(CQ html)
    {
        // XPath: //a[@class="tag tagTag"]/text()
        var tags = html["a.tag.tagTag"];
        var tagList = new List<string>();
        
        foreach (var tag in tags)
        {
            var tagText = tag.InnerText?.Trim();
            if (!string.IsNullOrEmpty(tagText))
            {
                tagList.Add(tagText);
            }
        }
        
        return string.Join(",", tagList);
    }

    private static string GetOutline(CQ html)
    {
        // XPath: //meta[@name="description"]/@content
        return html["meta[name='description']"].Attr("content") ?? "";
    }

    private static string GetMosaic(string tag, string title)
    {
        // Check if "無修正" is in tag or title
        if (tag.Contains("無修正") || title.Contains("無修正"))
        {
            return "无码";
        }
        return "有码";
    }

    private static string GetYear(string releaseDate)
    {
        if (string.IsNullOrEmpty(releaseDate) || releaseDate.Length < 4)
        {
            return "";
        }
        
        return releaseDate.Substring(0, 4);
    }
}