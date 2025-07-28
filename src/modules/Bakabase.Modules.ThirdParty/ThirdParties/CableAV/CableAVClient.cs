using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.CableAV.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.CableAV;

public class CableAVClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    private const string DefaultBaseUrl = "https://cableav.tv";

    public async Task<CableAVVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null, string? baseUrl = null)
    {
        try
        {
            var actualBaseUrl = baseUrl ?? DefaultBaseUrl;
            var processedNumber = number.Trim();
            string realUrl = string.Empty;

            if (!string.IsNullOrEmpty(appointUrl))
            {
                realUrl = appointUrl;
            }
            else
            {
                // Get number list (similar to get_number_list in Python)
                var numberList = GetNumberList(processedNumber);
                
                bool found = false;
                foreach (var searchNumber in numberList)
                {
                    var searchUrl = $"{actualBaseUrl}/?s={searchNumber}";
                    Logger.LogInformation("Searching CableAV with URL: {Url}", searchUrl);

                    var searchHtml = await HttpClient.GetStringAsync(searchUrl);
                    var searchCq = new CQ(searchHtml);

                    var (searchFound, foundNumber, foundTitle, foundUrl) = GetRealUrlFromResults(searchCq, numberList);
                    if (searchFound && !string.IsNullOrEmpty(foundUrl))
                    {
                        realUrl = foundUrl;
                        processedNumber = foundNumber;
                        found = true;
                        break;
                    }
                }
                
                if (!found)
                {
                    Logger.LogInformation("No matching search results found for: {Number}", number);
                    return null;
                }
            }

            Logger.LogInformation("Fetching CableAV detail from URL: {Url}", realUrl);
            var detailHtml = await HttpClient.GetStringAsync(realUrl);
            var detailCq = new CQ(detailHtml);

            var (detailNumber, title, actor, coverUrl, tag) = GetDetailInfo(detailCq, processedNumber);

            if (string.IsNullOrEmpty(title))
            {
                Logger.LogInformation("Could not extract title for: {Number}", number);
                return null;
            }

            var detail = new CableAVVideoDetail
            {
                Number = detailNumber,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Outline = "",
                Tag = tag,
                Release = "",
                Year = "",
                Runtime = "",
                Series = "",
                Country = "CN",
                Director = "",
                Studio = "",
                Publisher = "",
                Source = "cableav",
                CoverUrl = coverUrl,
                Website = realUrl,
                Mosaic = "国产",
                ActorPhoto = GetActorPhoto(actor)
            };

            Logger.LogInformation("Successfully extracted CableAV data for: {Number}", detailNumber);
            return detail;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error searching and parsing CableAV video: {Number}", number);
            return null;
        }
    }

    private static List<string> GetNumberList(string number)
    {
        // Simple implementation - can be enhanced based on requirements
        var numberList = new List<string> { number };
        
        // Add variations if needed (uppercase, lowercase, etc.)
        if (number != number.ToUpper())
            numberList.Add(number.ToUpper());
        if (number != number.ToLower())
            numberList.Add(number.ToLower());

        return numberList;
    }

    private static (bool found, string number, string title, string url) GetRealUrlFromResults(CQ html, List<string> numberList)
    {
        // Equivalent to Python XPath: //h3[contains(@class,"title")]//a[@href and @title]
        var items = html["h3[class*='title'] a[href][title]"];

        foreach (var item in items)
        {
            var itemCq = item.Cq();
            var href = itemCq.Attr("href");
            var title = itemCq.Text();

            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(href))
            {
                foreach (var n in numberList)
                {
                    // Remove non-alphanumeric characters and compare (similar to Python regex)
                    var tempN = Regex.Replace(n, @"[\W_]", "").ToUpper();
                    var tempTitle = Regex.Replace(title, @"[\W_]", "").ToUpper();

                    if (tempTitle.Contains(tempN))
                    {
                        return (true, n, title, href);
                    }
                }
            }
        }

        return (false, "", "", "");
    }

    private static (string number, string title, string actor, string coverUrl, string tag) GetDetailInfo(CQ html, string number)
    {
        // Extract title from entry content paragraphs
        // Python XPath: //div[@class="entry-content "]/p/text()
        var titleElements = html["div.entry-content p"];
        var title = "";
        if (titleElements.Any())
        {
            var firstParagraph = titleElements.First().Text();
            title = (firstParagraph ?? string.Empty).Replace(number + " ", "").Trim();
        }
        
        if (string.IsNullOrEmpty(title))
            title = number;

        // Extract actor using get_extra_info equivalent (simplified - may need enhancement)
        var actor = ExtractActorFromTitle(title);

        // Extract tag from categories
        // Python XPath: //header//div[@class="categories-wrap"]/a/text()
        var tagElements = html["header div.categories-wrap a"];
        var tag = "";
        if (tagElements.Any())
        {
            tag = tagElements.First().Text().Trim();
            // Convert to simplified Chinese (simplified equivalent of zhconv.convert)
            tag = ConvertToSimplifiedChinese(tag);
        }

        // Extract cover URL from Open Graph meta tag
        // Python XPath: //meta[@property="og:image"]/@content
        var coverUrl = html["meta[property='og:image']"].Attr("content") ?? "";

        return (number, title, actor, coverUrl, tag);
    }

    private static string ExtractActorFromTitle(string title)
    {
        // Simplified actor extraction - this would need enhancement based on requirements
        // This is a placeholder for the get_extra_info functionality from Python
        return "";
    }

    private static string ConvertToSimplifiedChinese(string text)
    {
        // Placeholder for zhconv.convert functionality
        // Would need actual Traditional to Simplified Chinese conversion library
        return text;
    }

    private static Dictionary<string, string> GetActorPhoto(string actor)
    {
        var actorPhoto = new Dictionary<string, string>();
        
        if (!string.IsNullOrEmpty(actor))
        {
            var actors = actor.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var act in actors)
            {
                actorPhoto[act.Trim()] = "";
            }
        }

        return actorPhoto;
    }
}