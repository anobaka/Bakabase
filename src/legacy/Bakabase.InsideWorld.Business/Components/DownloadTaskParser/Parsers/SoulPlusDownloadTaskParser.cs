using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Ai;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Bootstrap.Components.Configuration.Abstractions;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Parsers;

public class SoulPlusDownloadTaskParser(
    IBOptions<SoulPlusOptions> options,
    SoulPlusClient spClient,
    OllamaApiClientAccessor oaca)
    : AbstractDownloadTaskParser
{
    public override DownloadTaskParserSource Source => DownloadTaskParserSource.SoulPlus;

    public override async Task<DownloadTaskParseTask> Parse(string link, CancellationToken ct)
    {
        var post = await spClient.GetPostAsync(link);
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

                await spClient.BuyLockedContent(lc.Url!);
                boughtSomething = true;
            }

            if (boughtSomething)
            {
                post = await spClient.GetPostAsync(link);
            }
        }

        var task = new DownloadTaskParseTask
        {
            Link = link,
            ParsedAt = DateTime.Now,
            Source = DownloadTaskParserSource.SoulPlus
        };


        var aiClient = oaca.Client;
        var stream = aiClient.GenerateAsync(new GenerateRequest {Model = "", Prompt = "", System = ""});

        // var chatRsp = await aiClient.

        return task;
    }
}