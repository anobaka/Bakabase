using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Xcity.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Xcity;

public class XcityClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<XcityVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            var realUrl = appointUrl;
            string searchUrl = "";
            if (string.IsNullOrWhiteSpace(realUrl))
            {
                searchUrl = "https://xcity.jp/result_published/?q=" + Uri.EscapeDataString(number.Replace("-", string.Empty));
                var searchHtml = await HttpClient.GetStringAsync(searchUrl);
                if (searchHtml.Contains("該当する作品はみつかりませんでした"))
                {
                    return null;
                }

                var searchDoc = new CQ(searchHtml);
                var firstHref = searchDoc.Select("table.resultList tr td a").FirstOrDefault()?.GetAttribute("href");
                if (string.IsNullOrWhiteSpace(firstHref)) return null;
                realUrl = "https://xcity.jp" + firstHref;
            }

            var html = await HttpClient.GetStringAsync(realUrl);
            var doc = new CQ(html);

            string TextOf(string selector) => doc.Select(selector).Text().Trim();

            var title = TextOf("#program_detail_title");
            if (string.IsNullOrWhiteSpace(title)) return null;
            var webNumber = TextOf("#hinban");
            if (!string.IsNullOrWhiteSpace(webNumber))
            {
                title = title.Replace(" " + webNumber, string.Empty).Trim();
            }

            // actor
            var actor = string.Join(",", doc.Select("li.credit-links a").Select(a => a.Cq().Text().Trim()));

            // cover (full)
            var cover = doc.Select("div.photo p a").Attr("href") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(cover) && cover.StartsWith("//")) cover = "https:" + cover;

            // outline
            var outline = TextOf("p.lead");

            // release and year
            var releaseText = doc.Select("span.koumoku:contains('発売日')").Parent().Text();
            string release = string.Empty;
            if (!string.IsNullOrWhiteSpace(releaseText))
            {
                var m = Regex.Match(releaseText, @"\d{4}/\d{1,2}/\d{1,2}");
                if (m.Success) release = m.Value.Replace("/", "-");
            }
            var year = Regex.Match(release ?? string.Empty, @"\d{4}").Success ? Regex.Match(release ?? string.Empty, @"\d{4}").Value : (release?.Length >= 4 ? release[..4] : string.Empty);

            // tag
            var tag = string.Join(",", doc.Select("a.genre").Select(a => a.Cq().Text().Trim()));

            // studio & publisher
            var studio = TextOf("#program_detail_maker_name");
            var publisher = TextOf("#program_detail_label_name");

            // runtime
            var runtimeText = doc.Select("span.koumoku:contains('収録時間')").Parent().Text();
            var mr = Regex.Match(runtimeText ?? string.Empty, @"(\d+)");
            var runtime = mr.Success ? mr.Groups[1].Value : string.Empty;

            // director
            var director = TextOf("#program_detail_director");

            // extrafanart
            var extraFanart = doc.Select("a.thumb").Select(a => a.GetAttribute("href") ?? string.Empty)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Select(h =>
                {
                    var u = h;
                    if (u.StartsWith("//")) u = "https:" + u;
                    return u.Replace("//faws.xcity.jp/scene/small/", "https://faws.xcity.jp/");
                })
                .ToList();

            // poster small
            var poster = doc.Select("img.packageThumb").Attr("src") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(poster) && poster.StartsWith("//")) poster = "https:" + poster;
            poster = poster.Replace("package/medium/", string.Empty);

            var detail = new XcityVideoDetail
            {
                Number = number,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag,
                Release = release,
                Year = year,
                Runtime = runtime,
                Series = doc.Select("a[href*='series'] span").Text(),
                Director = director,
                Studio = studio,
                Publisher = publisher,
                Source = "xcity",
                CoverUrl = cover,
                PosterUrl = poster,
                Website = realUrl,
                Mosaic = "有码",
                SearchUrl = searchUrl
            };

            return detail;
        }
        catch
        {
            return null;
        }
    }
}


