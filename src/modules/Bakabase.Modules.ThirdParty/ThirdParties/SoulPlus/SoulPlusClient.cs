using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;
using CsQuery;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using System.Security.Policy;
using System.Text.RegularExpressions;
using static Bakabase.Abstractions.Components.Configuration.InternalOptions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

public class SoulPlusClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    public async Task<SoulPlusPost> GetPostAsync(string link)
    {
        var html = await HttpClient.GetStringAsync(link);
        var cq = new CQ(html);
        var topicContent = cq["#read_tpc"].Text();

        var post = new SoulPlusPost
        {
            Title = cq["#subject_tpc"].Text(),
            Html = ""
        };

        var bought = cq[".s3.f12.fn"];
        if (bought.Any())
        {
            // var quotes = new StringBuilder();
            var tpcContentElements = new HashSet<CQ>();
            foreach (var b in bought)
            {
                var tpcContentElement = b.Cq().Parents(".tpc_content")!;
                if (tpcContentElement.Find("#read_tpc").Length == 0 &&
                    tpcContentElements.Add(tpcContentElement))
                {

                    post.LockedContents ??= [];
                    post.LockedContents.Add(new SoulPlusPostLockedContent
                    {
                        IsBought = true,
                        ContentHtml = tpcContentElement.Html()
                    });
                }
                // var quote = b.Cq().Parent().Next("blockquote")!;
                // var text = quote.Html();
                //
                // text = WebUtility.HtmlDecode(text);
                //
                // text = string.Join(Environment.NewLine, text.Split("<br />"));
                // var passwordLines = text.Split('\n').Select(a => a.Trim()).Where(a =>
                //     passwordPrefixes.Any(c => a.StartsWith(c, StringComparison.OrdinalIgnoreCase))).Select(c =>
                //     passwordPrefixes.Aggregate(c, (current, p) => current.Replace(p, string.Empty))
                //         .Trim(':', '：')).ToArray();
                // if (passwordLines.Any())
                // {
                //     title = passwordLines.Aggregate(title, (current, p) => current + $"[{p}]");
                // }
                //
                // quotes.AppendLine(text);
            }

            // sb.AppendLine(title).AppendLine(quotes.ToString());
        }
        else
        {
            var buyButton = cq["input[value=\"愿意购买,我买,我付钱\"]"];
            if (buyButton.Any())
            {
                var priceText = buyButton.Prev().Text();
                var price = int.Parse(Regex.Match(priceText, @" (?<p>\d+) SP").Groups["p"].Value);

                post.LockedContents ??= [];
                post.LockedContents.Add(new SoulPlusPostLockedContent
                {
                    Url = new Uri(new Uri(link),
                        Regex.Match(buyButton.Attr("onclick"), $"'.*'").Value.Trim('\'')).ToString(),
                    IsBought = false,
                    Price = price
                });
            }
        }

        return post;
    }

    public async Task BuyLockedContent(string url)
    {
        await HttpClient.GetStringAsync(url);
    }

    protected override string HttpClientName => HttpClientNames.SoulPlus;
}