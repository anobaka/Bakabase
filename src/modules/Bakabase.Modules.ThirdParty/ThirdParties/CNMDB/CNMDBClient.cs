using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.CNMDB.Models;
using Bootstrap.Extensions;
using CsQuery;
using Microsoft.Extensions.Logging;
using System.Web;

namespace Bakabase.Modules.ThirdParty.ThirdParties.CNMDB;

public class CNMDBClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    private const string BaseUrl = "https://cnmdb.net";

    public async Task<CNMDBVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null, string? filePath = null, string? appointNumber = null)
    {
        try
        {
            string realUrl = "";
            string coverUrl = "";
            string title = "";
            string actor = "";
            string studio = "";
            string series = "";

            if (!string.IsNullOrEmpty(appointUrl))
            {
                realUrl = appointUrl;
                var html = await HttpClient.GetStringAsync(realUrl);
                var cq = new CQ(html);
                var (success, parsedNumber, parsedTitle, parsedActor, parsedUrl, parsedCover, parsedStudio, parsedSeries) = GetDetailInfo(cq, realUrl);
                
                if (!success)
                {
                    return null;
                }

                number = parsedNumber;
                title = parsedTitle;
                actor = parsedActor;
                realUrl = parsedUrl;
                coverUrl = parsedCover;
                studio = parsedStudio;
                series = parsedSeries;
            }
            else
            {
                // Process number list (simplified version of get_number_list)
                var numberList = GetNumberList(number, appointNumber, filePath);

                bool found = false;
                
                // Try direct number search first
                foreach (var searchNumber in numberList)
                {
                    var directUrl = $"{BaseUrl}/{searchNumber}";
                    
                    try
                    {
                        var html = await HttpClient.GetStringAsync(directUrl);
                        var cq = new CQ(html);
                        var (success, parsedNumber, parsedTitle, parsedActor, parsedUrl, parsedCover, parsedStudio, parsedSeries) = GetDetailInfo(cq, directUrl);
                        
                        if (success)
                        {
                            number = parsedNumber;
                            title = parsedTitle;
                            actor = parsedActor;
                            realUrl = parsedUrl;
                            coverUrl = parsedCover;
                            studio = parsedStudio;
                            series = parsedSeries;
                            found = true;
                            break;
                        }
                    }
                    catch
                    {
                        // Continue to next number or search method
                    }
                }

                if (!found && !string.IsNullOrEmpty(filePath))
                {
                    // Fallback to filename-based search
                    var filenameList = Regex.Split(filePath, @"[\.,，]");
                    
                    foreach (var searchTerm in filenameList)
                    {
                        if (searchTerm.Length < 5 || searchTerm.Contains("传媒") || searchTerm.Contains("麻豆"))
                        {
                            continue;
                        }

                        var searchUrl = $"{BaseUrl}/s0?q={HttpUtility.UrlEncode(searchTerm)}";
                        
                        try
                        {
                            var html = await HttpClient.GetStringAsync(searchUrl);
                            var cq = new CQ(html);
                            var (success, parsedNumber, parsedTitle, parsedActor, parsedUrl, parsedCover, parsedStudio, parsedSeries) = GetSearchInfo(cq, numberList);
                            
                            if (success)
                            {
                                number = parsedNumber;
                                title = parsedTitle;
                                actor = parsedActor;
                                realUrl = parsedUrl;
                                coverUrl = parsedCover;
                                studio = parsedStudio;
                                series = parsedSeries;
                                found = true;
                                break;
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }

                if (!found)
                {
                    return null;
                }
            }

            var actorPhoto = GetActorPhoto(actor);

            var detail = new CNMDBVideoDetail
            {
                Number = number,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Outline = "",
                OriginalPlot = "",
                Tag = "",
                Release = "",
                Year = "",
                Runtime = "",
                Score = "",
                Series = series,
                Country = "CN",
                Director = "",
                Studio = studio,
                Publisher = studio,
                Source = "cnmdb",
                Website = realUrl,
                ActorPhoto = actorPhoto,
                CoverUrl = coverUrl,
                PosterUrl = "",
                ExtraFanart = new List<string>(),
                Trailer = "",
                ImageDownload = false,
                ImageCut = "no",
                Mosaic = "国产",
                Wanted = ""
            };

            return detail;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error parsing CNMDB video: {Message}", ex.Message);
            return null;
        }
    }

    private static List<string> GetNumberList(string number, string? appointNumber, string? filePath)
    {
        var numberList = new List<string>();
        
        if (!string.IsNullOrEmpty(appointNumber))
        {
            numberList.Add(appointNumber);
        }
        
        if (!string.IsNullOrEmpty(number))
        {
            numberList.Add(number);
        }

        return numberList;
    }

    private static (bool success, string number, string title, string actor, string realUrl, string cover, string studio, string series) GetDetailInfo(CQ html, string realUrl)
    {
        try
        {
            var number = HttpUtility.UrlDecode(realUrl.Split('/').Last());
            var itemList = html["ol.breadcrumb"]?.Text()?.Split(' ')?.Where(s => !string.IsNullOrWhiteSpace(s))?.ToArray();
            
            if (itemList == null || itemList.Length == 0)
            {
                return (false, "", "", "", "", "", "", "");
            }

            var title = itemList.Last().Trim();
            var studio = itemList.Length > 1 && itemList[1].Contains("麻豆") ? "麻豆" : (itemList.Length > 2 ? itemList[itemList.Length - 2].Trim() : "");
            
            var (parsedTitle, parsedNumber, actor, series) = GetActorTitle(title, number, studio);
            
            if (itemList.Length > 2 && itemList[itemList.Length - 2].Contains("系列"))
            {
                series = itemList[itemList.Length - 2].Trim();
            }

            var cover = html["div.post-image-inner img"].Attr("src") ?? "";

            return (true, parsedNumber, parsedTitle, actor, realUrl, cover, studio, series);
        }
        catch
        {
            return (false, "", "", "", "", "", "", "");
        }
    }

    private static (bool success, string number, string title, string actor, string realUrl, string cover, string studio, string series) GetSearchInfo(CQ html, List<string> numberList)
    {
        try
        {
            var itemList = html["div.post-item"];
            
            foreach (var item in itemList)
            {
                var itemCq = new CQ(item);
                var titleElements = itemCq["h3 a"];
                
                if (!titleElements.Any()) continue;
                
                var titleText = titleElements.First().Text();
                
                foreach (var searchNumber in numberList)
                {
                    if (titleText.ToUpper().Contains(searchNumber.ToUpper()))
                    {
                        var realUrl = itemCq["h3 a"].Attr("href") ?? "";
                        var cover = itemCq["div.post-item-image a div img"].Attr("src") ?? "";
                        var studioUrl = itemCq["a"].Attr("href") ?? "";
                        var studio = itemCq["a span"].Text() ?? "";
                        
                        if (studioUrl.Contains("麻豆"))
                        {
                            studio = "麻豆";
                        }

                        var (parsedTitle, parsedNumber, actor, series) = GetActorTitle(titleText, searchNumber, studio);
                        
                        return (true, parsedNumber, parsedTitle, actor, realUrl, cover, studio, series);
                    }
                }
            }

            return (false, "", "", "", "", "", "", "");
        }
        catch
        {
            return (false, "", "", "", "", "", "", "");
        }
    }

    private static (string title, string number, string actor, string series) GetActorTitle(string title, string number, string studio)
    {
        var tempList = Regex.Split(title.Replace("/", "."), @"[\., ]");
        var actorList = new List<string>();
        var newTitle = "";
        var series = "";

        for (int i = 0; i < tempList.Length; i++)
        {
            if (tempList[i].ToUpper().Contains(number.ToUpper()))
            {
                number = tempList[i];
                continue;
            }

            if (tempList[i].Contains("系列"))
            {
                series = tempList[i];
                continue;
            }

            if (i < 2 && (tempList[i].Contains("传媒") || tempList[i].Contains(studio)))
            {
                continue;
            }

            if (i > 2 && (tempList[i] == studio || tempList[i].Contains("麻豆") || tempList[i].Contains("出品") || tempList[i].Contains("传媒")))
            {
                break;
            }

            if (i < 3 && tempList[i].Length <= 4 && actorList.Count < 1)
            {
                actorList.Add(tempList[i]);
                continue;
            }

            if (tempList[i].Length <= 3 && tempList[i].Length > 1)
            {
                actorList.Add(tempList[i]);
                continue;
            }

            newTitle += "." + tempList[i];
        }

        title = !string.IsNullOrEmpty(newTitle) ? newTitle : title;
        title = title.Trim('.');

        return (title, number, string.Join(",", actorList), series);
    }

    private static Dictionary<string, string> GetActorPhoto(string actor)
    {
        var actorDict = new Dictionary<string, string>();
        
        if (string.IsNullOrEmpty(actor)) return actorDict;
        
        var actors = actor.Split(',');
        foreach (var actorName in actors)
        {
            if (!string.IsNullOrWhiteSpace(actorName))
            {
                actorDict[actorName.Trim()] = "";
            }
        }

        return actorDict;
    }
}