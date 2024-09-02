using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Naming;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.ThirdParty.ThirdParties.Pixiv;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using NPOI.HSSF.Util;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Implementations
{
    /// <summary>
    /// https://www.pixiv.net/ranking.php?mode=monthly&date=20230228&p=4&format=json
    /// </summary>
    public class PixivRankingDownloader : AbstractPixivDownloader
    {
        public override PixivDownloadTaskType TaskType => PixivDownloadTaskType.Ranking;
        public PixivRankingDownloader(IServiceProvider serviceProvider, ISpecialTextService specialTextService,
            IBOptions<PixivOptions> options, PixivClient client) : base(serviceProvider, specialTextService, options,
            client)
        {
        }

        protected override async Task StartCore(DownloadTask task, CancellationToken ct)
        {
            // Illustrations in ranking will be changed on every day and there is not so many of them, so we do not use any checkpoint.
            var page = 0;
            var doneCount = 0;
            while (true)
            {
                var uriBuilder = new UriBuilder(task.Key);
                var query = HttpUtility.ParseQueryString(uriBuilder.Query);
                if (page == 0)
                {
                    page = int.TryParse(query["p"], out var p) ? p : 1;
                }

                query["p"] = page.ToString();
                if (query["format"].IsNullOrEmpty())
                {
                    query["format"] = "json";
                }

                uriBuilder.Query = query.ToString()!;
                var uri = uriBuilder.Uri;

                var rankingRsp = await Client.GetRankingData(uri.ToString());

                if (int.TryParse(rankingRsp.Next, out page))
                {
                    foreach (var c in rankingRsp.Contents)
                    {
                        var id = c.Illust_id;
                        var namingContext = c.ToNamingContext();
                        await DownloadSingleWork(id, task.DownloadPath, namingContext, ct);
                        await OnProgressInternal((decimal) ++doneCount * 100 / rankingRsp.Rank_total);
                    }
                }
                else
                {
                    break;
                }
            }
        }
    }
}