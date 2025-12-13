using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Prestige.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Prestige;

public class PrestigeClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<PrestigeVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            var realUrl = string.IsNullOrEmpty(appointUrl) ? string.Empty : appointUrl.Replace("goods", "api/product");
            string searchUrl = "";
            if (string.IsNullOrEmpty(realUrl))
            {
                searchUrl = $"https://www.prestige-av.com/api/search?isEnabledQuery=true&searchText={Uri.EscapeDataString(number)}&isEnableAggregation=false&release=false&reservation=false&soldOut=false&from=0&aggregationTermsSize=0&size=20";
                var json = await HttpClient.GetStringAsync(searchUrl);
                var productApi = ExtractProductApi(json, number);
                if (string.IsNullOrEmpty(productApi)) return null;
                realUrl = productApi;
            }

            var pageDataJson = await HttpClient.GetStringAsync(realUrl);
            var doc = JsonDocument.Parse(pageDataJson).RootElement;
            var title = doc.GetProperty("title").GetString() ?? string.Empty;
            if (string.IsNullOrEmpty(title)) return null;

            string outline = doc.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? string.Empty : string.Empty;
            var actor = string.Join(",", doc.GetProperty("actress").EnumerateArray().Select(a => a.GetProperty("name").GetString()!.Replace(" ", "")));
            var poster = TryPath(doc, ["thumbnail", "path"]);
            poster = string.IsNullOrEmpty(poster) || poster.Contains("noimage") ? string.Empty : "https://www.prestige-av.com/api/media/" + poster;
            var cover = TryPath(doc, ["packageImage", "path"]);
            cover = string.IsNullOrEmpty(cover) ? string.Empty : "https://www.prestige-av.com/api/media/" + cover;
            var release = TryArrayPath(doc, ["sku", "salesStartAt"], 0);
            if (!string.IsNullOrEmpty(release)) release = release[..10];
            var year = Regex.Match(release ?? string.Empty, @"\d{4}").Value;
            var runtime = doc.TryGetProperty("playTime", out var pt) ? pt.GetInt32().ToString() : string.Empty;
            string series = TryPath(doc, ["series", "name"]);
            var tag = string.Join(",", doc.GetProperty("genre").EnumerateArray().Select(g => g.GetProperty("name").GetString()));
            string director = TryArrayPath(doc, ["directors", "name"], 0);
            string studio = TryPath(doc, ["maker", "name"]);
            string publisher = TryPath(doc, ["label", "name"]);
            var extrafanart = doc.GetProperty("media").EnumerateArray().Select(m => "https://www.prestige-av.com/api/media/" + m.GetProperty("path").GetString()).ToArray();
            var trailer = TryPath(doc, ["movie", "path"]);
            trailer = string.IsNullOrEmpty(trailer) ? string.Empty : "https://www.prestige-av.com/api/media/" + trailer;

            return new PrestigeVideoDetail
            {
                Number = number,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag,
                Release = release,
                Year = year,
                Runtime = runtime,
                Series = series,
                Studio = studio,
                Publisher = publisher,
                Source = "prestige",
                CoverUrl = cover,
                PosterUrl = poster,
                Website = realUrl.Replace("api/product", "goods"),
                Mosaic = "有码",
                SearchUrl = searchUrl
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractProductApi(string json, string number)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var hit in doc.RootElement.GetProperty("hits").GetProperty("hits").EnumerateArray())
            {
                var src = hit.GetProperty("_source");
                var deliveryItemId = src.GetProperty("deliveryItemId").GetString() ?? string.Empty;
                if (deliveryItemId.EndsWith(number.ToUpper()))
                {
                    var uuid = src.GetProperty("productUuid").GetString();
                    if (!string.IsNullOrEmpty(uuid))
                    {
                        return "https://www.prestige-av.com/api/product/" + uuid;
                    }
                }
            }
        }
        catch { }
        return string.Empty;
    }

    private static string TryPath(JsonElement el, string[] path)
    {
        try
        {
            foreach (var key in path)
            {
                el = el.GetProperty(key);
            }
            return el.GetString() ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private static string TryArrayPath(JsonElement el, string[] path, int index)
    {
        try
        {
            el = el.GetProperty(path[0]);
            var arr = el.EnumerateArray().ElementAt(index);
            return arr.GetProperty(path[1]).GetString() ?? string.Empty;
        }
        catch { return string.Empty; }
    }
}


