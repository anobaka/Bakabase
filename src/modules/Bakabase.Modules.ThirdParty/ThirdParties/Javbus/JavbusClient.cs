using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.Av;
using Bakabase.Modules.ThirdParty.ThirdParties.Javbus.Models;
using Microsoft.Extensions.Logging;
using CsQuery;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Javbus;

public class JavbusClient(
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    IAvSourceOptionsProvider avOptionsProvider)
    : BakabaseHttpClient(httpClientFactory, loggerFactory), IAvClient
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    string IAvClient.SourceId => AvSourceIds.Javbus;

    async Task<IAvDetail?> IAvClient.SearchAndParseVideo(string number, string? appointUrl, string? language) =>
        await SearchAndParseVideo(number, appointUrl: appointUrl);

    /// <summary>
    /// Fetches a code's cover and its full magnet list.
    ///
    /// Unlike <see cref="SearchAndParseVideo"/> this does not swallow failures:
    /// batch callers show one row per code, and "not indexed" has to read
    /// differently from "the site refused us".
    /// </summary>
    /// <returns>null when the source is disabled or the page carries no cover (code not indexed).</returns>
    public async Task<JavbusMagnetSearchResult?> SearchMagnets(string number, CancellationToken ct = default)
    {
        var config = avOptionsProvider.Resolve(AvSourceIds.Javbus);
        if (!config.Enabled) return null;

        var baseUrl = (config.BaseUrl ?? AvSourceDefaults.DefaultBaseUrls[AvSourceIds.Javbus]).TrimEnd('/');
        var detailUrl = $"{baseUrl}/{Uri.EscapeDataString(number)}";

        using var request = AvHttpRequestBuilder.BuildGet(detailUrl, config);
        using var response = await HttpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(html)) return null;

        var cq = new CQ(html);
        var coverUrl = GetCover(cq, baseUrl);
        // Javbus answers unknown codes with a soft 200 landing page, so a
        // missing cover is how "not indexed" actually looks.
        if (string.IsNullOrEmpty(coverUrl)) return null;

        var magnets = await FetchMagnets(html, baseUrl, detailUrl, config, ct);

        return new JavbusMagnetSearchResult
        {
            Number = number,
            DetailUrl = detailUrl,
            Title = cq["h3"].First().Text().Trim(),
            CoverUrl = coverUrl,
            Magnets = magnets
        };
    }

    /// <summary>
    /// Downloads a cover. Javbus hotlink-protects its image host, so the
    /// referer and the source's cookie/user-agent have to come along.
    /// </summary>
    public async Task<byte[]> DownloadCover(string coverUrl, string refererUrl, CancellationToken ct = default)
    {
        var config = avOptionsProvider.Resolve(AvSourceIds.Javbus);
        using var request = AvHttpRequestBuilder.BuildGet(coverUrl, config);
        request.Headers.Referrer = new Uri(refererUrl);
        using var response = await HttpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<List<JavbusMagnet>> FetchMagnets(string detailHtml, string baseUrl, string detailUrl,
        AvSourceResolvedConfig config, CancellationToken ct)
    {
        // The magnet table is loaded over ajax, keyed by three variables the
        // detail page only declares in an inline script.
        var gid = Regex.Match(detailHtml, @"var\s+gid\s*=\s*(\d+)").Groups[1].Value;
        var img = Regex.Match(detailHtml, @"var\s+img\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;
        var uc = Regex.Match(detailHtml, @"var\s+uc\s*=\s*(\d+)").Groups[1].Value;
        if (string.IsNullOrEmpty(gid) || string.IsNullOrEmpty(img))
        {
            return [];
        }

        var query =
            $"lang=zh&gid={gid}&img={Uri.EscapeDataString(img)}&uc={(string.IsNullOrEmpty(uc) ? "0" : uc)}&floor={Random.Shared.Next(1, 1000)}";
        using var request = AvHttpRequestBuilder.BuildGet($"{baseUrl}/ajax/uncledatoolsbyajax.php?{query}", config);
        request.Headers.Referrer = new Uri(detailUrl);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

        using var response = await HttpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return ParseMagnets(await response.Content.ReadAsStringAsync(ct));
    }

    internal static List<JavbusMagnet> ParseMagnets(string? html)
    {
        var magnets = new List<JavbusMagnet>();
        if (string.IsNullOrWhiteSpace(html))
        {
            return magnets;
        }

        // The endpoint answers with bare <tr> rows; they only survive parsing
        // when wrapped in a table.
        var cq = new CQ($"<table>{html}</table>");
        foreach (var row in cq["tr"].Select(r => r.Cq()))
        {
            var link = row.Find("a[href^='magnet:']").First();
            var href = link.Attr("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var cells = row.Find("td");
            // Every cell wraps the same magnet in its own <a>; the first one
            // carries the release name, then size, then date.
            var size = cells.Length > 1 ? cells[1].Cq().Text().Trim() : null;
            var name = Regex.Replace(link.Text().Trim(), @"\s+", " ");

            magnets.Add(new JavbusMagnet
            {
                Name = name,
                Size = size,
                SizeInBytes = JavbusMagnetSelector.ParseSize(size),
                Date = cells.Length > 2 ? cells[2].Cq().Text().Trim() : null,
                Link = href,
                Tag = JavbusMagnetSelector.DetectTag(name)
            });
        }

        return magnets;
    }

    public async Task<JavbusVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null, string? baseUrl = null, string? mosaic = null)
    {
        try
        {
            var config = avOptionsProvider.Resolve(AvSourceIds.Javbus);
            if (!config.Enabled) return null;

            var javbusUrl = baseUrl ?? config.BaseUrl ?? "https://www.javbus.com";
            var realUrl = appointUrl;

            if (string.IsNullOrEmpty(realUrl))
            {
                // Default try direct detail url
                realUrl = $"{javbusUrl}/{number}";
            }

            using var request = AvHttpRequestBuilder.BuildGet(realUrl, config);
            using var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var cq = new CQ(html);
            var title = cq["h3"].First().Text().Trim();
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }

            var webNumber = cq["span.header:contains('識別碼:')"].Parent().Find("span").Eq(1).Text().Trim();
            if (!string.IsNullOrEmpty(webNumber))
            {
                number = webNumber;
            }
            title = title.Replace(number, "").Trim();

            var actor = string.Join(",", cq["div.star-name a"].Select(a => a.InnerText).Select(x => x.Trim()));
            var coverUrl = GetCover(cq, javbusUrl);
            var posterUrl = GetPoster(coverUrl);
            var release = cq["span.header:contains('發行日期:')"].Parent().Contents().Filter(n => n.NodeType == NodeType.TEXT_NODE).Text().Trim();
            var yearMatch = Regex.Match(release ?? string.Empty, @"\d{4}");
            var year = yearMatch.Success ? yearMatch.Value : release?.Substring(0, Math.Min(4, release.Length));
            var runtime = ExtractRuntime(cq);
            var studio = FirstText(cq, "a[href*='/studio/']");
            var publisher = FirstText(cq, "a[href*='/label/']");
            if (string.IsNullOrEmpty(publisher)) publisher = studio;
            var director = FirstText(cq, "a[href*='/director/']");
            var series = FirstText(cq, "a[href*='/series/']");
            var tag = string.Join(",", cq["span.genre label a[href*='/genre/']"].Select(a => a.InnerText).Select(x => x.Trim()));
            var activeTab = cq["li.active a"].Text();
            var mosaicText = activeTab.Contains("有碼") ? "有码" : "无码";
            var extraFanart = cq["#sample-waterfall a"].Select(a => a.GetAttribute("href")).ToArray();

            // If uncensored and not一本道, prefer no poster unless specific cases
            if (mosaicText == "无码")
            {
                var isKmh = number.Contains("KMHRS");
                if (!isKmh)
                {
                    posterUrl = string.Empty;
                }
                else if (extraFanart.Length > 0)
                {
                    posterUrl = extraFanart[0];
                }
            }

            return new JavbusVideoDetail
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
                Source = AvSourceIds.Javbus,
                CoverUrl = coverUrl,
                PosterUrl = posterUrl,
                Website = realUrl,
                Mosaic = mosaicText,
                SearchUrl = realUrl
            };
        }
        catch
        {
            return null;
        }
    }

    private static string FirstText(CQ cq, string selector)
    {
        return cq[selector].First().Text().Trim();
    }

    private static string ExtractRuntime(CQ cq)
    {
        var text = cq["span.header:contains('長度:')"].Parent().Contents().Filter(n => n.NodeType == NodeType.TEXT_NODE).Text();
        var m = Regex.Match(text ?? string.Empty, @"\d+");
        return m.Success ? m.Value : string.Empty;
    }

    private static string GetCover(CQ cq, string baseUrl)
    {
        var href = cq["a.bigImage"].Attr("href") ?? string.Empty;
        if (string.IsNullOrEmpty(href)) return string.Empty;
        if (!href.StartsWith("http")) return baseUrl + href;
        return href;
    }

    private static string GetPoster(string coverUrl)
    {
        if (string.IsNullOrEmpty(coverUrl)) return string.Empty;
        if (coverUrl.Contains("/pics/"))
        {
            return coverUrl.Replace("/cover/", "/thumb/").Replace("_b.jpg", ".jpg");
        }
        if (coverUrl.Contains("/imgs/"))
        {
            return coverUrl.Replace("/cover/", "/thumbs/").Replace("_b.jpg", ".jpg");
        }
        return string.Empty;
    }
}


