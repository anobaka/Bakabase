using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Javlibrary.Models;
using Microsoft.Extensions.Logging;
using CsQuery;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Javlibrary;

public class JavlibraryClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<JavlibraryVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null, string language = "zh_cn", string? baseDomain = null)
    {
        try
        {
            var domain = baseDomain ?? "https://www.javlibrary.com";
            string langPath = language switch { "zh_cn" => "/cn", "zh_tw" => "/tw", _ => "/ja" };
            var searchUrl = $"{domain}{langPath}/vl_searchbyid.php?keyword={Uri.EscapeDataString(number)}";
            var realUrl = appointUrl;

            if (string.IsNullOrEmpty(realUrl))
            {
                var searchHtml = await HttpClient.GetStringAsync(searchUrl);
                if (searchHtml.Contains("Cloudflare")) return null;
                var real = GetRealUrl(new CQ(searchHtml), number, domain + langPath);
                if (string.IsNullOrEmpty(real)) return null;
                realUrl = real;
            }

            var detailHtml = await HttpClient.GetStringAsync(realUrl);
            if (detailHtml.Contains("Cloudflare")) return null;
            var cq = new CQ(detailHtml);

            var title = cq["#video_title h3 a"].Text().Trim();
            var webNumber = cq["#video_id .text"].Text().Trim();
            if (string.IsNullOrEmpty(title)) return null;
            title = title.Replace(webNumber + " ", "");

            var actor = string.Join(",", cq["#video_cast .text span.star a"].Select(a => a.InnerText));
            var cover = cq["#video_jacket_img"].Attr("src") ?? string.Empty;
            if (!string.IsNullOrEmpty(cover) && !cover.StartsWith("http")) cover = "https:" + cover;
            var tag = string.Join(",", cq["#video_genres .text span a"].Select(a => a.InnerText));
            var release = cq["#video_date .text"].Text().Trim();
            var year = Regex.Match(release ?? string.Empty, @"\d{4}").Success ? Regex.Match(release, @"\d{4}").Value : release;
            var studio = cq["#video_maker .text span a"].Text();
            var publisher = cq["#video_label .text span a"].Text();
            var runtime = cq["#video_length span.text"].Text();
            var score = cq["#video_review .score"].Text().Trim('(', ')');
            var director = cq["#video_director .text span a"].Text();
            var wanted = cq["a[href*='userswanted.php']"].Text();

            return new JavlibraryVideoDetail
            {
                Number = webNumber,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag,
                Release = release,
                Year = year,
                Runtime = runtime,
                Studio = studio,
                Publisher = publisher,
                Source = "javlibrary",
                CoverUrl = cover,
                PosterUrl = string.Empty,
                Website = realUrl,
                Mosaic = "有码"
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GetRealUrl(CQ html, string number, string domain2)
    {
        var newNumber = number.Trim().Replace("-", "").ToUpper() + " ";
        var anchors = html["#video_title h3 a"]; // main list
        foreach (var a in anchors)
        {
            var text = a.InnerText;
            if ((text ?? string.Empty).Replace("-", "").ToUpper().Contains(newNumber))
            {
                var href = a.GetAttribute("href") ?? string.Empty;
                if (!string.IsNullOrEmpty(href)) return domain2[..^3] + href;
            }
        }

        var anchors2 = html["a[href*='/?v=jav']"]; // alternative list
        foreach (var a in anchors2)
        {
            var title = a.GetAttribute("title") ?? string.Empty;
            if (title.Replace("-", "").ToUpper().Contains(newNumber) && !title.Contains("ブルーレイディスク"))
            {
                var href = a.GetAttribute("href") ?? string.Empty;
                if (!string.IsNullOrEmpty(href)) return domain2 + href[1..];
            }
        }
        return string.Empty;
    }
}

// Duplicate stub removed; implementation above provides the real client
