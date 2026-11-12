using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;
using Bakabase.Modules.ThirdParty.Helpers;
using CsQuery;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

/// <summary>Pure first-page parsing, shared by the live reader and offline fixtures.</summary>
public static partial class SoulPlusPostParser
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
        var cq = new CQ(html);
        var main = cq["#read_tpc"].First();
        // Replies can be hidden for a banned commenter while the original post is readable.
        // Site scripts and quoted examples also contain these messages. Only a visible notice
        // replacing the original post (or an otherwise notice-only page) proves it is blocked.
        if (IsUnavailableNotice(main.Length > 0 ? main : cq["body"]))
            throw new InvalidOperationException("Post is deleted or unavailable.");
        var title = cq["#subject_tpc"].Text()?.Trim();
        if (string.IsNullOrEmpty(title) || main.Length == 0)
        {
            var notice = main.Length == 0 ? ReadPageNotice(cq) : null;
            throw new InvalidOperationException(notice == null
                ? "The post page could not be read. Check the account login and post URL."
                : $"SoulPlus：{notice}");
        }
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
                Html = HtmlContentSnapshot.Capture(content)
            });
        }

        // Content-block identity is distinct from a purchase endpoint. Include bought containers
        // in the ordinal so unlocking one block does not rename later blocks in the same floor.
        var blockIds = new Dictionary<IDomObject, string>();
        var floorOrdinals = new Dictionary<string, int>();
        foreach (var element in cq["h6.quote"])
        {
            var floor = Floor(element.Cq()) ?? "unknown";
            var ordinal = floorOrdinals.GetValueOrDefault(floor);
            floorOrdinals[floor] = ordinal + 1;
            blockIds[element] = $"{floor}:block:{ordinal}";
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
            var container = button.Parents("h6.quote").First();
            var blockId = container.Length > 0 ? blockIds.GetValueOrDefault(container[0]) : null;
            post.LockedContents.Add(new SoulPlusPostLockedContent
            {
                Id = purchaseUrl ?? $"{floor ?? "unknown"}:lock:{index++}", Floor = floor,
                BlockId = blockId ?? $"{floor ?? "unknown"}:unmapped:{index++}",
                PurchaseTargetId = PurchaseTarget(purchaseUrl), ContextText = PurposeContext(container),
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
                BlockId = marker.Parents("h6.quote").First().Length > 0
                    ? blockIds.GetValueOrDefault(marker.Parents("h6.quote").First()[0]) : null,
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
        post.EvidenceFingerprint = CanonicalSnapshotFingerprint(url, post.Title,
            HtmlContentSnapshot.Capture(main), post.Comments);
        post.ReaderEvidence = BuildReaderEvidence(cq, post, blockIds);
        return post;
    }

    private static string? PurchaseTarget(string? url)
    {
        if (url == null) return null;
        // Preserve the public snapshot identity: new structured evidence has its own canonical key.
        var stable = Regex.Replace(url, @"([?&]verify=)[^&#]*", "$1<nonce>", RegexOptions.IgnoreCase);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stable)));
    }

    private static string LegacyStableEvidence(string html)
    {
        // Historical algorithm, retained only to authenticate saved snapshots before proving
        // equivalence using their complete source. Never use it for fresh editorial evidence.
        var text = Regex.Replace(html, @"(?<prefix>job\.php\?action=buytopic[^'""<>]*?(?:&amp;|&)verify=)[^&'""<>\s]*", "${prefix}<nonce>", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"已有\s*\d+\s*人购买", "已有<人数>人购买");
        return text;
    }

    private static string? PurposeContext(CQ container)
    {
        if (container.Length == 0 || container[0].ParentNode == null) return null;
        var siblings = container[0].ParentNode.ChildNodes.ToList();
        var index = siblings.IndexOf(container[0]);
        var parts = new List<string>();
        for (var i = index - 1; i >= 0; i--)
        {
            var node = siblings[i];
            if (node.NodeType == NodeType.COMMENT_NODE) continue;
            if (node.NodeType == NodeType.ELEMENT_NODE)
            {
                var name = node.NodeName.ToLowerInvariant();
                if (name == "h6" || node.Cq().Find("input[value=\"愿意购买,我买,我付钱\"],.s3.f12.fn").Length > 0) break;
                if (name is "script" or "style" or "blockquote" or "q") break;
                if (name == "br") { if (parts.Any(p => !string.IsNullOrWhiteSpace(p))) break; else continue; }
                var text = VisibleText(node.Cq());
                if (text.Length > 0) parts.Insert(0, text);
                if (name is "p" or "div" or "h1" or "h2" or "h3" or "h4" or "h5") break;
            }
            else if (node.NodeType == NodeType.TEXT_NODE) parts.Insert(0, node.NodeValue ?? "");
            if (parts.Sum(p => p.Length) > 240) break;
        }
        var context = Regex.Replace(string.Concat(parts), @"\s+", " ").Trim();
        if (context.Length > 240) context = context[^240..];
        return Blank(context);
    }

    private static bool IsUnavailableNotice(CQ region)
    {
        var text = Regex.Replace(VisibleText(region), @"\s+", "").Trim('。', '.', '!', '！');
        return text is "此帖被管理员关闭，暂时不能浏览" or "用户被禁言,该主题自动屏蔽";
    }

    private static string? ReadPageNotice(CQ page)
    {
        // PHPWind returns these error pages with HTTP 200. Only SoulPlus's dedicated
        // notice table is trusted; page titles, navigation and arbitrary body text are not.
        foreach (var element in page["#main > div.t > table"])
        {
            var table = element.Cq();
            var heading = VisibleText(table.Find("td.h").First());
            // The heading can include the current mirror's brand, e.g. "南+ South Plus 提示信息".
            if (heading != "提示信息" && !heading.EndsWith(" 提示信息", StringComparison.Ordinal)) continue;
            var text = VisibleText(table.Find("tr.f_one > td > center").First());
            if (text.Length == 0) continue;
            const int maximumLength = 512;
            if (text.Length <= maximumLength) return text;
            var length = char.IsHighSurrogate(text[maximumLength - 1]) ? maximumLength - 1 : maximumLength;
            return text[..length].TrimEnd() + "…";
        }
        return null;
    }

    private static string VisibleText(CQ region)
    {
        if (region.Length == 0) return "";
        foreach (var element in region.Concat(region.Parents()))
        {
            if (element.GetAttribute("hidden") != null ||
                string.Equals(element.GetAttribute("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase) ||
                IsHiddenStyle(element.GetAttribute("style"))) return "";
        }
        // Work on a copy: the original HTML and comments remain available to the content reader.
        var visible = CQ.CreateFragment("<div></div>");
        visible.Html(region.Html() ?? "");
        visible.Find("script,style,template,noscript,textarea,pre,code,blockquote,q,.quote,.blockquote,[hidden],[aria-hidden=true]")
            .Remove();
        foreach (var element in visible.Find("[style]"))
        {
            if (IsHiddenStyle(element.GetAttribute("style"))) element.Cq().Remove();
        }
        return Regex.Replace(visible.Text() ?? "", @"\s+", " ").Trim();
    }

    private static bool IsHiddenStyle(string? style) => Regex.IsMatch(style ?? "",
        @"(?:^|;)\s*(?:display\s*:\s*none|visibility\s*:\s*hidden)\s*(?:!important\s*)?(?:;|$)",
        RegexOptions.IgnoreCase);

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
