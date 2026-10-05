using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;
using CsQuery;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

/// <summary>Pure first-page parsing, shared by the live reader and offline fixtures.</summary>
public static class SoulPlusPostParser
{
    public static bool IsSupportedUrl(string reference) =>
        Uri.TryCreate(reference, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
        uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        new[] {"soulplus.net", "soul-plus.net", "south-plus.net", "north-plus.net", "spring-plus.net",
            "summer-plus.net", "snow-plus.net", "white-plus.net", "level-plus.net"}
            .Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));

    public static string FirstPageUrl(string url)
    {
        var builder = new UriBuilder(url) {Fragment = ""};
        var query = builder.Query.TrimStart('?');
        query = Regex.Replace(query, @"(^|&)page=\d+(&|$)", "$1", RegexOptions.IgnoreCase).Trim('&');
        query = Regex.Replace(query, @"-page-\d+", "-page-1", RegexOptions.IgnoreCase);
        builder.Path = Regex.Replace(builder.Path, @"-page-\d+", "-page-1", RegexOptions.IgnoreCase);
        builder.Query = query;
        return builder.Uri.AbsoluteUri;
    }

    public static SoulPlusPost Parse(string html, string url)
    {
        if (html.Contains("此帖被管理员关闭，暂时不能浏览") || html.Contains("用户被禁言,该主题自动屏蔽"))
            throw new InvalidOperationException("Post is deleted or unavailable.");
        var cq = new CQ(html);
        var title = cq["#subject_tpc"].Text()?.Trim();
        if (string.IsNullOrEmpty(title) || cq["#read_tpc"].Length == 0)
            throw new InvalidOperationException("The post page could not be read. Check the account login and post URL.");
        var post = new SoulPlusPost {Title = title, Html = html, SourceUrl = url, LockedContents = []};

        foreach (var element in cq[".tpc_content>.f14:not(#read_tpc)"])
        {
            var content = element.Cq();
            var container = Container(content);
            post.Comments.Add(new SoulPlusPostComment
            {
                Id = Blank(content.Attr("id")) ?? Blank(container.Attr("id")),
                Floor = Floor(content),
                Author = Blank(container.Find("a[href*='showuid'],a[href*='uid='],.author").First().Text()),
                PostedAt = ParseTime(container.Find(".tiptop").Text()),
                Html = WebUtility.HtmlDecode(content.Html())
            });
        }

        // Inspect every individual button. Bought blocks elsewhere must not hide unbought blocks.
        var index = 0;
        foreach (var element in cq["input[value=\"愿意购买,我买,我付钱\"]"])
        {
            var button = element.Cq();
            var priceText = button.Prev().Text();
            if (string.IsNullOrWhiteSpace(priceText)) priceText = button.Parent().Text();
            var priceMatch = Regex.Match(priceText ?? "", @"(?<price>\d+(?:\.\d+)?)\s*SP(?:币|幣)?", RegexOptions.IgnoreCase);
            decimal? price = decimal.TryParse(priceMatch.Groups["price"].Value, NumberStyles.Number,
                CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            string? purchaseUrl = null;
            foreach (Match quoted in Regex.Matches(button.Attr("onclick") ?? "", "['\"](?<url>[^'\"]+)['\"]"))
            {
                if (!Uri.TryCreate(new Uri(url), WebUtility.HtmlDecode(quoted.Groups["url"].Value), out var candidate) ||
                    candidate.Scheme != new Uri(url).Scheme || candidate.Host != new Uri(url).Host ||
                    candidate.Port != new Uri(url).Port || !string.IsNullOrEmpty(candidate.UserInfo) ||
                    !candidate.AbsolutePath.EndsWith("job.php", StringComparison.OrdinalIgnoreCase) ||
                    !candidate.Query.Contains("buy", StringComparison.OrdinalIgnoreCase)) continue;
                purchaseUrl = candidate.AbsoluteUri;
                break;
            }
            var floor = Floor(button);
            post.LockedContents.Add(new SoulPlusPostLockedContent
            {
                Id = purchaseUrl ?? $"{floor ?? "unknown"}:lock:{index++}", Floor = floor,
                Url = purchaseUrl, Price = price, IsBought = false
            });
        }

        foreach (var element in cq[".s3.f12.fn"])
        {
            var marker = element.Cq();
            if (marker.Parents(".tpc_content").Length == 0) continue;
            var text = marker.Text();
            if (!(text.Contains("购买") || text.Contains("购买者") || text.Contains("出售"))) continue;
            var floor = Floor(marker);
            post.LockedContents.Add(new SoulPlusPostLockedContent
            {
                Id = $"{floor ?? "unknown"}:bought:{index++}", Floor = floor, IsBought = true,
                ContentHtml = marker.Parent().Html()
            });
        }

        // Only an identified account panel is trusted: SP amounts in author cards or posts are not the reader's balance.
        foreach (var element in cq["#user-info,#user_info,#user-login,#user_login,#login-info,#login_info,#user-nav,#user_nav,#user_credit"])
        {
            var panel = element.Cq();
            if (panel.Parents(".tpc_content").Length > 0 || panel.Find(".tpc_content").Length > 0) continue;
            var match = Regex.Match(panel.Text(), @"(?:SP(?:币|幣)?|金币|金幣)\s*[:：]\s*(?<balance>\d[\d,]*)(?![\d.])", RegexOptions.IgnoreCase);
            if (match.Success && decimal.TryParse(match.Groups["balance"].Value, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out var balance))
            {
                post.Balance = balance;
                break;
            }
        }
        return post;
    }

    private static CQ Container(CQ element)
    {
        var table = element.Parents("table").First();
        return table.Length > 0 ? table : element.Parents(".tpc_content").Parent();
    }

    private static string? Floor(CQ element)
    {
        if (element.Attr("id") == "read_tpc" || element.Parents("#read_tpc").Length > 0) return "0";
        var container = Container(element);
        var match = Regex.Match(container.Find(".tiptop").Text() ?? "", @"(?:B(?<floor>\d+)F|(?<floor>\d+)\s*楼)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["floor"].Value : null;
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        var match = Regex.Match(value ?? "", @"\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}(?::\d{2})?");
        return DateTime.TryParse(match.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), TimeSpan.FromHours(8)) : null;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
