using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Dahlia.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Dahlia;

public class DahliaClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    private const string DefaultBaseUrl = "https://dahlia-av.jp";

    public async Task<DahliaVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            var processedNumber = number.ToLower();
            string realUrl;

            if (!string.IsNullOrEmpty(appointUrl))
            {
                realUrl = appointUrl;
            }
            else
            {
                var urlNumber = processedNumber.Replace("-", "");
                realUrl = $"{DefaultBaseUrl}/works/{urlNumber}/";
            }

            var detailHtml = await HttpClient.GetStringAsync(realUrl);
            var detailCq = new CQ(detailHtml);

            var title = GetTitle(detailCq);
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }

            var detail = new DahliaVideoDetail
            {
                Number = number,
                Title = title,
                OriginalTitle = title,
                Actor = GetActor(detailCq),
                Studio = GetStudio(detailCq),
                Publisher = GetPublisher(detailCq),
                Release = GetRelease(detailCq),
                Runtime = GetRuntime(detailCq),
                Series = GetSeries(detailCq),
                Director = GetDirector(detailCq),
                CoverUrl = GetCover(detailCq),
                Outline = GetOutline(detailCq),
                ExtraFanart = GetExtraFanart(detailCq),
                Trailer = GetTrailer(detailCq),
                Website = realUrl,
                Source = "dahlia",
                SearchUrl = realUrl
            };

            detail.Year = GetYear(detail.Release);
            detail.Mosaic = "有码"; // Default mosaic setting as per Python implementation

            return detail;
        }
        catch
        {
            return null;
        }
    }

    private static string GetTitle(CQ html)
    {
        var result = html["h1"].Text().Trim();
        return result;
    }

    private static string GetActor(CQ html)
    {
        var actors = html["div.box_works01_list.clearfix span:contains('出演女優') + p"]
            .Select(p => p.Cq().Text().Trim())
            .Where(text => !string.IsNullOrEmpty(text))
            .ToList();

        return string.Join(", ", actors);
    }

    private static string GetOutline(CQ html)
    {
        var outline = html["div.box_works01_text p"].Text().Trim();
        return outline;
    }

    private static string GetRuntime(CQ html)
    {
        var runtimeElements = html["span:contains('収録時間')"];
        foreach (var element in runtimeElements)
        {
            var nextElementText = element.Cq().Next().Text();
            var match = Regex.Match(nextElementText ?? string.Empty, @"\d+");
            if (match.Success)
            {
                return match.Value;
            }
        }
        return "";
    }

    private static string GetSeries(CQ html)
    {
        var seriesElements = html["span:contains('系列')"];
        foreach (var element in seriesElements)
        {
            var nextElementText = element.Cq().Next().Text();
            return (nextElementText ?? string.Empty).Trim();
        }
        return "";
    }

    private static string GetDirector(CQ html)
    {
        var directorElements = html["span:contains('導演'), span:contains('監督')"];
        foreach (var element in directorElements)
        {
            var nextElementText = element.Cq().Next().Text();
            return (nextElementText ?? string.Empty).Trim();
        }
        return "";
    }

    private static string GetStudio(CQ html)
    {
        return "DAHLIA"; // Default studio as per Python implementation
    }

    private static string GetPublisher(CQ html)
    {
        var publisherElements = html["span:contains('メーカー')"];
        foreach (var element in publisherElements)
        {
            var nextElement = element.NextSibling ?? element.NextElementSibling;
            if (nextElement != null)
            {
                return (nextElement.InnerText ?? nextElement.TextContent).Trim();
            }
        }
        return "DAHLIA"; // Default as per Python implementation
    }

    private static string GetRelease(CQ html)
    {
        var releaseElements = html["div.view_timer span:contains('配信開始日') + p"];
        var result = releaseElements.Text().Trim();
        return result?.Replace("/", "-") ?? "";
    }

    private static string GetYear(string release)
    {
        var match = Regex.Match(release, @"\d{4}");
        return match.Success ? match.Value : "";
    }

    private static string GetCover(CQ html)
    {
        var coverUrl = html["a.pop_sample img"].Attr("src");
        return coverUrl?.Replace("?output-quality=60", "") ?? "";
    }

    private static string[] GetExtraFanart(CQ html)
    {
        var fanartUrls = html["a.pop_img"]
            .Select(a => a.Cq().Attr("href"))
            .Where(href => !string.IsNullOrEmpty(href))
            .ToArray();

        return fanartUrls;
    }

    private static string GetTrailer(CQ html)
    {
        var trailerUrl = html["a.pop_sample"].Attr("href");
        return trailerUrl ?? "";
    }
}