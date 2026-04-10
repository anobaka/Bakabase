using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;
using CsQuery;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Helpers;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using HttpCloak;
using static Bakabase.Abstractions.Components.Configuration.InternalOptions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

public class SoulPlusClient(
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    IBOptions<ISoulPlusOptions> options)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    public async Task<SoulPlusPost> GetPostAsync(string link, CancellationToken ct)
    {
        var html = await GetHtml(link, ct);

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

    protected Task<string> GetHtml(string url, CancellationToken ct)
    {
        if (options.Value.Cookie.IsNullOrEmpty())
        {
            throw new Exception("Cookie is not set");
        }

        ct.ThrowIfCancellationRequested();

        var preset = options.Value.TlsPreset ?? TlsPresetHelper.DefaultPreset;
        var userAgent = options.Value.UserAgent ?? IThirdPartyHttpClientOptions.DefaultUserAgent;

        using var session = new Session(preset: preset);
        var headers = new Dictionary<string, string>
        {
            { "User-Agent", userAgent },
            { "Cookie", options.Value.Cookie }
        };

        var response = session.Get(url, headers: headers);
        if (response.StatusCode != 200)
        {
            throw new Exception(
                $"Request to {url} failed with status code: {response.StatusCode}");
        }

        return Task.FromResult(response.Text);
    }

    public async Task BuyLockedContent(string url, CancellationToken ct)
    {
        await GetHtml(url, ct);
    }

    protected override string HttpClientName => HttpClientNames.SoulPlus;
}
