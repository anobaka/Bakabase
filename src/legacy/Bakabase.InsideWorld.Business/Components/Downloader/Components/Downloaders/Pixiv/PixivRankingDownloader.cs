using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.ThirdParty.ThirdParties.Pixiv;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Pixiv
{
    /// <summary>
    /// https://www.pixiv.net/ranking.php?mode=monthly&date=20230228&p=4&format=json
    /// </summary>
    public class PixivRankingDownloader(
        IServiceProvider serviceProvider,
        ISpecialTextService specialTextService,
        PixivClient client)
        : AbstractPixivDownloader(serviceProvider, specialTextService,
            client)
    {
        public override PixivDownloadTaskType EnumTaskType => PixivDownloadTaskType.Ranking;

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