using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;
using CsQuery;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using System.Diagnostics;
using System.Net;
using System;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using static Bakabase.Abstractions.Components.Configuration.InternalOptions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

public class SoulPlusClient(
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    IBOptions<ThirdPartyOptions> thirdPartyOptions,
    IBOptions<SoulPlusOptions> options)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    public async Task<SoulPlusPost> GetPostAsync(string link)
    {
        var html = await GetHtml(link);

        if (html.Contains("此帖被管理员关闭，暂时不能浏览") || html.Contains("用户被禁言,该主题自动屏蔽"))
        {
            throw new Exception("Post is deleted");
        }

        var cq = new CQ(html);
        var title = cq["#subject_tpc"].Text();

        var post = new SoulPlusPost
        {
            Title = title,
            Html = html,
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
            }
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

    protected async Task<string> GetHtml(string url)
    {
        if (thirdPartyOptions.Value.CurlExecutable.IsNullOrEmpty())
        {
            throw new Exception("Curl executable is not set");
        }

        if (options.Value.Cookie.IsNullOrEmpty())
        {
            throw new Exception("Cookie is not set");
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = thirdPartyOptions.Value.CurlExecutable,
                Arguments = $"""
                             --cookie "{options.Value.Cookie}" -H "User-Agent:Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36 Edg/137.0.0.0" {url}
                             """,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        return await process.StandardOutput.ReadToEndAsync();

    }

    public async Task BuyLockedContent(string url)
    {
        await GetHtml(url);
    }

    protected override string HttpClientName => HttpClientNames.SoulPlus;
}