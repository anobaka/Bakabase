using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Hscangku.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Hscangku;

public class HscangkuClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<HscangkuVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            string? realUrl = appointUrl;
            var baseUrl = "http://hsck.net";

            if (string.IsNullOrWhiteSpace(realUrl))
            {
                var searchUrl = $"{baseUrl}/vodsearch/-------------.html?wd={Uri.EscapeDataString(number)}&submit=";
                string htmlSearch;
                try { htmlSearch = await HttpClient.GetStringAsync(searchUrl); } catch { return null; }
                var docSearch = new CQ(htmlSearch);
                var items = docSearch.Select("a.stui-vodlist__thumb.lazyload");
                foreach (var a in items)
                {
                    var href = a.GetAttribute("href") ?? string.Empty;
                    var tmpTitle = a.GetAttribute("title") ?? a.Cq().Attr("title") ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(href) && !string.IsNullOrWhiteSpace(tmpTitle))
                    {
                        realUrl = baseUrl + href;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(realUrl)) return null;

            var html = await HttpClient.GetStringAsync(realUrl);
            var doc = new CQ(html);

            var h3Title = doc.Select("h3.title").Text().Trim();
            var title = h3Title.Replace(number + " ", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(title)) title = h3Title;

            var cover = doc.Select("a[data-original][href*='detail']").Attr("data-original") ?? string.Empty;

            var detail = new HscangkuVideoDetail
            {
                Number = number,
                Title = string.IsNullOrWhiteSpace(title) ? number : title,
                OriginalTitle = string.IsNullOrWhiteSpace(title) ? number : title,
                Actor = "",
                Tag = string.Empty,
                Release = string.Empty,
                Year = string.Empty,
                Runtime = string.Empty,
                Series = string.Empty,
                Studio = string.Empty,
                Publisher = string.Empty,
                CoverUrl = cover,
                PosterUrl = string.Empty,
                Website = realUrl,
                Source = "hscangku",
                Mosaic = "国产"
            };

            return detail;
        }
        catch
        {
            return null;
        }
    }
}


