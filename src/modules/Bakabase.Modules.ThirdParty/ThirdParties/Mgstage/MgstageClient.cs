using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Mgstage.Models;
using Microsoft.Extensions.Logging;
using CsQuery;
using System.Text;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Mgstage;

public class MgstageClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<MgstageVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null, string? shortNumber = null)
    {
        try
        {
            number = number?.ToUpper() ?? string.Empty;
            shortNumber = shortNumber?.ToUpper();

            var candidateUrls = new List<string>();
            if (!string.IsNullOrEmpty(appointUrl))
            {
                candidateUrls.Add(appointUrl);
            }
            else
            {
                candidateUrls.Add($"https://www.mgstage.com/product/product_detail/{number}/");
                if (!string.IsNullOrEmpty(shortNumber) && shortNumber != number)
                {
                    candidateUrls.Add($"https://www.mgstage.com/product/product_detail/{shortNumber}/");
                }
            }

            string? realUrl = null;
            CQ? detail = null;
            foreach (var url in candidateUrls)
            {
                var html = await GetStringWithEncoding(url);
                if (string.IsNullOrWhiteSpace(html))
                {
                    continue;
                }
                var cq = new CQ(html);
                var title = GetTitle(cq);
                if (!string.IsNullOrEmpty(title))
                {
                    realUrl = url;
                    detail = cq;
                    break;
                }
            }
            if (detail == null || string.IsNullOrEmpty(realUrl))
            {
                return null;
            }

            var actor = GetActor(detail);
            var titleText = GetTitle(detail);
            var coverUrl = GetCover(detail);
            var posterUrl = GetCoverSmall(coverUrl);
            var outline = GetOutline(detail);
            var release = GetRelease(detail).Replace("/", "-");
            var tag = GetTag(detail);
            var year = ExtractYear(release);
            var runtime = GetRuntime(detail);
            var series = GetSeries(detail);
            var studio = GetStudio(detail);
            var publisher = GetPublisher(detail);

            return new MgstageVideoDetail
            {
                Number = number,
                Title = titleText,
                OriginalTitle = titleText,
                Actor = actor,
                Tag = tag,
                Release = release,
                Year = year,
                Runtime = runtime,
                Series = series,
                Studio = studio,
                Publisher = publisher,
                Source = "mgstage",
                CoverUrl = coverUrl,
                PosterUrl = posterUrl,
                Website = realUrl,
                Mosaic = "有码",
                SearchUrl = realUrl
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> GetStringWithEncoding(string url)
    {
        try
        {
            var bytes = await HttpClient.GetByteArrayAsync(url);
            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                var euc = Encoding.GetEncoding("euc-jp");
                return euc.GetString(bytes);
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetTitle(CQ html)
    {
        return html["#center_column h1"].Text().Replace("/", ",").Trim();
    }

    private static string GetActor(CQ html)
    {
        var td = FindTd(html, "出演");
        var text = string.Join(",", td.Find("a").Select(a => a.InnerText))
            .Replace("\\n", "").Replace(" ", "").Replace("/", ",");
        if (!string.IsNullOrEmpty(text)) return text;
        var plain = td.Text().Replace("\\n", "").Replace(" ", "").Replace("/", ",");
        return plain;
    }

    private static string GetStudio(CQ html)
    {
        var td = FindTd(html, "メーカー：");
        var v = td.Find("a").Text();
        return string.IsNullOrEmpty(v) ? td.Text() : v;
    }

    private static string GetPublisher(CQ html)
    {
        var td = FindTd(html, "レーベル：");
        var v = td.Find("a").Text();
        return string.IsNullOrEmpty(v) ? td.Text() : v;
    }

    private static string GetRuntime(CQ html)
    {
        var td = FindTd(html, "収録時間：");
        var v = string.IsNullOrEmpty(td.Find("a").Text()) ? td.Text() : td.Find("a").Text();
        return Regex.Replace(v ?? string.Empty, @"\D+", "");
    }

    private static string GetSeries(CQ html)
    {
        var td = FindTd(html, "シリーズ：");
        var v = td.Find("a").Text();
        return string.IsNullOrEmpty(v) ? td.Text() : v;
    }

    private static string GetNum(CQ html)
    {
        var td = FindTd(html, "品番：");
        var v = td.Find("a").Text();
        if (string.IsNullOrEmpty(v)) v = td.Text();
        return v?.Trim().ToUpper() ?? string.Empty;
    }

    private static string GetRelease(CQ html)
    {
        var td = FindTd(html, "配信開始日：");
        var v = string.IsNullOrEmpty(td.Find("a").Text()) ? td.Text() : td.Find("a").Text();
        return v?.Trim() ?? string.Empty;
    }

    private static string GetTag(CQ html)
    {
        var td = FindTd(html, "ジャンル：");
        var tags = string.Join(",", td.Find("a").Select(a => a.InnerText));
        if (!string.IsNullOrEmpty(tags)) return tags;
        return td.Text();
    }

    private static string GetCover(CQ html)
    {
        return html["a#EnlargeImage"].Attr("href") ?? string.Empty;
    }

    private static string GetCoverSmall(string coverUrl)
    {
        return string.IsNullOrEmpty(coverUrl) ? string.Empty : coverUrl.Replace("/pb_", "/pf_");
    }

    private static string GetOutline(CQ html)
    {
        var p = html["#introduction dd p"].First().Text();
        if (!string.IsNullOrEmpty(p)) return p.Trim();
        var all = html["#introduction dd"].Text();
        return all?.Replace(" ", "").Trim() ?? string.Empty;
    }

    private static string ExtractYear(string release)
    {
        var m = Regex.Match(release ?? string.Empty, @"\d{4}");
        return m.Success ? m.Value : string.Empty;
    }

    private static CQ FindTd(CQ html, string thContains)
    {
        foreach (var th in html["th"])
        {
            var thText = th.Cq().Text();
            if (thText.Contains(thContains))
            {
                var td = th.NextElementSibling;
                if (td != null)
                {
                    return td.Cq();
                }
            }
        }
        return new CQ("<td></td>");
    }
}


