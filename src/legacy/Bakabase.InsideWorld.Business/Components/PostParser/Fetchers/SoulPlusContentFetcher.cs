using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;
using Bootstrap.Components.Configuration.Abstractions;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;

public class SoulPlusContentFetcher(
    IBOptions<SoulPlusOptions> options,
    SoulPlusClient spClient,
    ILogger<SoulPlusContentFetcher> logger)
    : IPostContentFetcher
{
    public PostParserSource Source => PostParserSource.SoulPlus;

    public async Task<PostContent> FetchAsync(string link, CancellationToken ct)
    {
        var post = await spClient.GetPostAsync(link, ct);
        if (post.LockedContents != null)
        {
            var boughtSomething = false;
            foreach (var lc in post.LockedContents)
            {
                if (lc.IsBought)
                {
                    continue;
                }

                if (lc.Price > options.Value.AutoBuyThreshold)
                {
                    throw new Exception(
                        $"Failed due to price {lc.Price} is larger than auto buy threshold {options.Value.AutoBuyThreshold}");
                }

                await spClient.BuyLockedContent(lc.Url!, ct);
                boughtSomething = true;
            }

            if (boughtSomething)
            {
                post = await spClient.GetPostAsync(link, ct);
            }
        }

        var html = post.Html;
        var cq = new CQ(html);
        var mainContent = WebUtility.HtmlDecode(cq["#read_tpc"].Html());
        var commentContents = cq[".tpc_content>.f14:not(#read_tpc)"]
            .Select(c => WebUtility.HtmlDecode(c.Cq().Html()))
            .ToList();

        return new PostContent
        {
            Title = post.Title,
            MainHtml = mainContent,
            CommentHtmlList = commentContents,
        };
    }
}
