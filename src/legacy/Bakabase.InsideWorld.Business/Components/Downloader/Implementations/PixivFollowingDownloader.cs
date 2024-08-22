using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Checkpoint;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv.Models;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Implementations
{
    /// <summary>
    /// https://www.pixiv.net/bookmark_new_illust.php?p=82
    /// </summary>
    public class PixivFollowingDownloader : AbstractPixivDownloader
    {
        public override PixivDownloadTaskType TaskType => PixivDownloadTaskType.Following;

        private static readonly HashSet<string> UrlKeywords = new()
            {"bookmark_new_illust_r18.php", "bookmark_new_illust.php"};

        public PixivFollowingDownloader(IServiceProvider serviceProvider, ISpecialTextService specialTextService,
            IBOptions<PixivOptions> options, PixivClient client) : base(serviceProvider, specialTextService, options,
            client)
        {
        }

        protected override async Task StartCore(DownloadTask task, CancellationToken ct)
        {
            if (UrlKeywords.Any(a => task.Key.Contains(a) && !task.Key.Contains($"novel/{a}")))
            {
                var inputUri = new Uri(task.Key);
                var qs = inputUri.ParseQueryString();
                var page = int.TryParse(qs["p"], out var p) ? p : 1;
                var doneCount = 0;
                var doneIds = new List<string>();
                var total = 0;

                var checkpointContext = new RangeCheckpointContext(task.Checkpoint);

                while (true)
                {
                    qs["p"] = page.ToString();
                    qs["version"] = Guid.NewGuid().ToString("N");
                    var uri = new UriBuilder(PixivApiUrls.FollowingIllustrations) {Query = qs.ToString()!};
                    var url = uri.ToString();

                    var illustrations = await Client.FollowLatestIllust(url);
                    var ids = illustrations?.Page?.Ids ?? new List<string>();
                    var idIntersections = ids.Intersect(doneIds).ToArray();
                    var hasMore = !idIntersections.Any();
                    var targetIds = ids.Except(idIntersections).ToArray();
                    total += targetIds.Length;

                    foreach (var id in targetIds)
                    {
                        var action = checkpointContext.Analyze(id);
                        switch (action)
                        {
                            case RangeCheckpointContext.AnalyzeResult.AllTaskIsDone:
                                await OnCheckpointChangedInternal(checkpointContext.BuildCheckpointOnComplete());
                                return;
                            case RangeCheckpointContext.AnalyzeResult.Skip:
                                break;
                            case RangeCheckpointContext.AnalyzeResult.Download:
                            {
                                var illustration = illustrations?.Thumbnails?.Illust?.FirstOrDefault(a => a.Id == id);
                                if (illustration == null)
                                {
                                    throw new Exception($"Illustration info with id:{id} is not found in {url}");
                                }

                                await DownloadSingleWork(id, task.DownloadPath, illustration.ToNamingContext(), ct);
                                break;
                            }
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                        await OnCheckpointChangedInternal(checkpointContext.BuildCheckpoint(id));
                        await OnProgressInternal(++doneCount * 100m / total);
                    }

                    doneIds.AddRange(targetIds);
                    if (hasMore)
                    {
                        page++;
                    }
                    else
                    {
                        await OnCheckpointChangedInternal(checkpointContext.BuildCheckpointOnComplete());
                        return;
                    }
                }

            }

            throw new Exception($"Unsupported url: {task.Key}");
        }
    }
}