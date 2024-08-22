using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Naming;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Cryptography;
using static Microsoft.Extensions.Logging.EventSource.LoggingEventSource;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv.Models;
using Bootstrap.Extensions;
using static Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv.Models.PixivSearchResponse;
using Bakabase.InsideWorld.Business.Components.Downloader.Checkpoint;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Implementations
{
    public class PixivSearchDownloader : AbstractPixivDownloader
    {
        public override PixivDownloadTaskType TaskType => PixivDownloadTaskType.Search;

        public PixivSearchDownloader(IServiceProvider serviceProvider, ISpecialTextService specialTextService,
            IBOptions<PixivOptions> options, PixivClient client) : base(serviceProvider, specialTextService, options,
            client)
        {
        }

        protected override async Task StartCore(DownloadTask task, CancellationToken ct)
        {
            // Categories: https://www.pixiv.net/tags/%E6%B1%8E%E7%94%A8%E5%9E%8B%E3%83%96%E3%83%AA/{(top)?/illustrations/manga/novels/artworks}?order=popular_male_d&mode=safe&wlt=3000&hlt=3000
            // User not supported: https://www.pixiv.net/search_user.php?nick=%E6%B1%8E%E7%94%A8%E5%9E%8B%E3%83%96%E3%83%AA&s_mode=s_usr

            // Ajax: https://www.pixiv.net/ajax/search/illustrations/%E6%B1%8E%E7%94%A8%E5%9E%8B%E3%83%96%E3%83%AA?word=%E6%B1%8E%E7%94%A8%E5%9E%8B%E3%83%96%E3%83%AA&order=date&mode=safe&p=1&s_mode=s_tag_full&type=illust_and_ugoira&lang=zh&version=1bb9c95cd9cbc108a16ddf9fea198f3210ac5053
            // parse url to request url
            var inputUri = new Uri(task.Key);
            var path = inputUri.AbsolutePath;
            var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToArray();
            // Search
            if (pathSegments.Any())
            {
                if (pathSegments[0] == "tags")
                {
                    if (pathSegments.Length > 1)
                    {
                        var keyword = pathSegments[1];
                        var type = pathSegments.Length > 2 ? pathSegments[2] : "top";

                        var encodedKeyword = WebUtility.UrlDecode(keyword) == keyword
                            ? WebUtility.UrlEncode(keyword)
                            : keyword;
                        var qsTemplate = inputUri.ParseQueryString();
                        qsTemplate["word"] = encodedKeyword;
                        var page = int.TryParse(qsTemplate["p"], out var p) ? p : 1;

                        var urlTemplate = $"https://www.pixiv.net/ajax/search/{type}/{encodedKeyword}";
                        var doneCount = 0;

                        var checkpointContext = new RangeCheckpointContext(task.Checkpoint);

                        while (true)
                        {
                            var qs = new NameValueCollection(qsTemplate)
                            {
                                ["p"] = page.ToString(),
                                ["version"] = Guid.NewGuid().ToString("N")
                            };

                            var uri = new UriBuilder(urlTemplate)
                            {
                                Query = qs.ToString()!
                            };

                            var url = uri.ToString();
                            var data = await Client.Search(url);
                            var targetListData = new List<ListData>
                                {
                                    data.Illust,
                                    data.IllustManga,
                                    data.Manga,
                                    // data.Novel is not supported now
                                }.Where(a => a?.Total > 0)
                                .ToList();
                            if (targetListData.Any())
                            {
                                var totalCount = targetListData.Sum(a => a.Total);
                                var allWorks = targetListData.SelectMany(a => a.Data).ToList();

                                foreach (var w in allWorks)
                                {
                                    var action = checkpointContext.Analyze(w.Id);
                                    switch (action)
                                    {
                                        case RangeCheckpointContext.AnalyzeResult.AllTaskIsDone:
                                            await OnCheckpointChangedInternal(checkpointContext
                                                .BuildCheckpointOnComplete());
                                            return;
                                        case RangeCheckpointContext.AnalyzeResult.Skip:
                                            break;
                                        case RangeCheckpointContext.AnalyzeResult.Download:
                                        {
                                            var nameContext = w.ToNamingContext();
                                            await DownloadSingleWork(w.Id, task.DownloadPath, nameContext, ct);
                                            break;
                                        }
                                        default:
                                            throw new ArgumentOutOfRangeException();
                                    }

                                    await OnCheckpointChangedInternal(checkpointContext.BuildCheckpoint(w.Id));
                                    await OnProgressInternal((++doneCount) * 100m / totalCount);
                                }

                                page++;
                            }
                            else
                            {
                                break;
                            }
                        }

                        await OnCheckpointChangedInternal(checkpointContext.BuildCheckpointOnComplete());
                        return;
                    }
                }
            }

            throw new Exception($"Unsupported url: {task.Key}");
        }
    }
}