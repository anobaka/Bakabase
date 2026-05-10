using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Av;
using Bakabase.Modules.ThirdParty.ThirdParties.Airav.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Airav;

public class AiravClient(
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    IAvSourceOptionsProvider avOptionsProvider)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<AiravVideoDetail?> SearchAndParseVideo(string number, string language = "zh_cn", string? appointUrl = null)
    {
        try
        {
            var config = avOptionsProvider.Resolve("airav");
            if (!config.Enabled) return null;

            var processedNumber = Regex.IsMatch(number, @"^N\d{4}$", RegexOptions.IgnoreCase)
                ? number.ToLowerInvariant()
                : number.ToUpperInvariant();

            var baseUrl = (config.BaseUrl ?? "https://airav.io").TrimEnd('/');
            var langPrefix = LanguagePrefix(language);
            string realUrl;
            string? searchUrl = null;

            if (!string.IsNullOrEmpty(appointUrl))
            {
                realUrl = appointUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? appointUrl
                    : baseUrl + (appointUrl.StartsWith("/") ? appointUrl : "/" + appointUrl);
            }
            else
            {
                searchUrl = $"{baseUrl}{langPrefix}/search_result?kw={Uri.EscapeDataString(processedNumber)}";
                using var searchRequest = AvHttpRequestBuilder.BuildGet(searchUrl, config);
                using var searchResponse = await HttpClient.SendAsync(searchRequest);
                searchResponse.EnsureSuccessStatusCode();
                var searchHtml = await searchResponse.Content.ReadAsStringAsync();

                var detailHref = FindDetailHref(new CQ(searchHtml), processedNumber);
                if (string.IsNullOrEmpty(detailHref)) return null;

                realUrl = detailHref.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? detailHref
                    : baseUrl + (detailHref.StartsWith("/") ? detailHref : "/" + detailHref);
            }

            using var detailRequest = AvHttpRequestBuilder.BuildGet(realUrl, config);
            using var detailResponse = await HttpClient.SendAsync(detailRequest);
            detailResponse.EnsureSuccessStatusCode();
            var detailHtml = await detailResponse.Content.ReadAsStringAsync();
            var doc = new CQ(detailHtml);

            var rawTitle = doc["h1"].First().Text().Trim();
            if (string.IsNullOrEmpty(rawTitle)) return null;

            // Match the number anywhere at the start of the title (case-insensitive) and strip it.
            var numberInTitle = ExtractLeadingNumber(rawTitle, processedNumber);
            var title = string.IsNullOrEmpty(numberInTitle)
                ? rawTitle
                : rawTitle.Substring(numberInTitle.Length).Trim();

            var actor = string.Join(",",
                doc["ul.list-group li.my-2 a[href*='/actor']"].Select(a => a.Cq().Text().Trim())
                    .Where(s => !string.IsNullOrEmpty(s)));

            var tag = string.Join(",",
                doc["ul.list-group li.my-2 a[href*='/tag']"].Select(a => a.Cq().Text().Trim())
                    .Where(s => !string.IsNullOrEmpty(s)));

            var cover = doc["meta[property='og:image']"].Attr("content") ?? string.Empty;
            var outline = doc["meta[property='og:description']"].Attr("content") ?? string.Empty;

            return new AiravVideoDetail
            {
                Number = string.IsNullOrEmpty(numberInTitle) ? processedNumber : numberInTitle,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag,
                Outline = outline,
                CoverUrl = cover,
                Website = realUrl,
                Source = "airav",
                Mosaic = GetMosaic(tag),
                SearchUrl = searchUrl,
            };
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Airav parse failed for {Number}", number);
            return null;
        }
    }

    /// <summary>
    /// airav.io URL layout: empty → Traditional Chinese (default), /cn → Simplified, /jp → Japanese.
    /// English isn't supported by the site, so en falls back to Traditional.
    /// </summary>
    private static string LanguagePrefix(string language) => language switch
    {
        "zh_cn" => "/cn",
        "ja" => "/jp",
        _ => string.Empty,
    };

    private static string FindDetailHref(CQ cq, string number)
    {
        var upper = number.ToUpperInvariant();
        foreach (var card in cq["div.oneVideo"])
        {
            var ccq = card.Cq();
            var heading = ccq.Find("div.oneVideo-body h5").Text().Trim();
            if (string.IsNullOrEmpty(heading)) continue;
            if (!heading.ToUpperInvariant().Contains(upper)) continue;
            var href = ccq.Find("div.oneVideo-top a[href*='/video']").Attr("href");
            if (!string.IsNullOrEmpty(href)) return href;
        }
        return string.Empty;
    }

    private static string ExtractLeadingNumber(string title, string fallback)
    {
        var trimmed = title.TrimStart();
        if (trimmed.StartsWith(fallback, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Substring(0, fallback.Length);
        }
        var m = Regex.Match(trimmed, @"^[A-Za-z]{2,10}-?\d{2,8}");
        return m.Success ? m.Value : string.Empty;
    }

    private static string GetMosaic(string tag)
    {
        var lower = tag.ToLowerInvariant();
        if (lower.Contains("无码") || lower.Contains("無修正") || lower.Contains("無码") || lower.Contains("uncensored"))
        {
            return "无码";
        }
        return "有码";
    }
}
