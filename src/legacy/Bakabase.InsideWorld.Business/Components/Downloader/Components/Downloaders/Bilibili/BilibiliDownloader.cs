using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models.Constants;
using Bootstrap.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Bilibili
{
    public class BilibiliDownloader : AbstractDownloader<BilibiliDownloadTaskType>
    {
        private IStringLocalizer<SharedResource> _localizer;
        private readonly IHostEnvironment _env;
        private readonly BilibiliClient _client;
        private readonly ISpecialTextService _specialTextService;

        public override ThirdPartyId ThirdPartyId => ThirdPartyId.Bilibili;
        public override BilibiliDownloadTaskType EnumTaskType => BilibiliDownloadTaskType.Favorites;

        private readonly LuxService _luxService;

        public BilibiliDownloader(IStringLocalizer<SharedResource> localizer,
            BilibiliClient client, ISpecialTextService specialTextService,
            IHostEnvironment env, IServiceProvider serviceProvider, LuxService luxService) : base(serviceProvider)
        {
            _localizer = localizer;
            _client = client;
            _specialTextService = specialTextService;
            _env = env;
            _luxService = luxService;
        }

        private string LuxBin => BuildLuxBinPath(_env);

        public static string BuildLuxBinPath(IHostEnvironment env) =>
            Path.Combine(env.ContentRootPath, "libs", "lux.exe");


        private decimal _getGlobalProgress(decimal currentPartProgress, int currentPartNo, int partCount,
            int currentPostIndex, int postCount)
        {
            var postUnitProgress = postCount == 0 ? 0 : 100 / (decimal) postCount;
            var partUnitProgress = postUnitProgress / partCount;

            var globalProgress = postUnitProgress * currentPostIndex + partUnitProgress * (currentPartNo - 1) +
                                 partUnitProgress * currentPartProgress / 100;
            return Math.Floor(globalProgress * 100) / 100;
        }

        protected override async Task StartCore(DownloadTask task, CancellationToken ct)
        {
            //var p = 0m;
            //var unit = 100m / 23;
            //while (p < 100)
            //{
            //    p += unit;
            //    p = Math.Min(100, p);
            //    await Task.Delay(1000, ct);
            //    await OnProgressInternal(p);
            //}
            //throw new Exception("Test exception");

            var options = await GetDownloaderOptionsAsync();
            var cookie = options.Cookie;
            var favorites = await _client.GetFavorites();
            var targetFavorites = favorites.FirstOrDefault(a => a.Id == long.Parse(task.Key));
            if (targetFavorites == null)
            {
                throw new Exception(_localizer[SharedResource.Downloader_BilibiliFavoritesDoesNotExist, task.Key,
                    task.Name]);
            }

            await OnNameAcquiredInternal(targetFavorites.Title);

            var page = task.StartPage ?? 1;
            var totalCount = targetFavorites.MediaCount;
            var doneCount = 0;
            var globalProgress = 0m;

            var tempVideoDirectory = Path.Combine(task.DownloadPath, "temp", targetFavorites.Id.ToString());
            if (Directory.Exists(tempVideoDirectory))
            {
                Directory.Delete(tempVideoDirectory, true);
            }

            Directory.CreateDirectory(tempVideoDirectory);

            await OnProgressInternal(globalProgress);

            var checkpointContext = new RangeCheckpointContext(task.Checkpoint);

            //var downloadedRanges = RangeCheckpointBuilder.GetRanges(task.Checkpoint);

            //var currentDownloadedRangeStartId = 0;
            //var currentDownloadedRangeEndId = 0;

            // var firstPostId = 0;

            while (true)
            {
                var favoriteItems = new List<FavoriteItem>();
                var posts = await _client.GetPostsInFavorites(targetFavorites.Id, page, ct);

                //if (posts.Medias?.Any() == true && firstPostId == 0)
                //{
                //    firstPostId = posts.Medias[0].Id;
                //}

                favoriteItems.AddRange(posts.Medias);

                var taskIsDone = false;

                foreach (var post in favoriteItems)
                {
                    var postIndex = doneCount;

                    var action = checkpointContext.Analyze(post.Id.ToString());
                    NextCheckpoint = checkpointContext.BuildCheckpoint(post.Id.ToString());
                    switch (action)
                    {
                        case RangeCheckpointContext.AnalyzeResult.AllTaskIsDone:
                            doneCount = totalCount;
                            await OnProgressInternal(100);
                            taskIsDone = true;
                            break;
                        case RangeCheckpointContext.AnalyzeResult.Skip:
                            Current = $"[{postIndex + 1}/{totalCount}][{post.Upper?.Name}]{post.Title}";
                            await OnCurrentChangedInternal();
                            break;
                        case RangeCheckpointContext.AnalyzeResult.Download:
                        {
                            if (post.Title != "已失效视频")
                            {
                                try
                                {
                                    // 上面的请求可以获取page总数，但还需要page名称，所以需要深层请求
                                    var postInfo = await _client.GetPostInfo(post.Id, ct);

                                    // Download Cover
                                    var coverExt = Path.GetExtension(postInfo.Pic);
                                    var coverBytes = await _client.HttpClient.GetByteArrayAsync(postInfo.Pic, ct);

                                    for (var i = 1; i <= postInfo.Pages.Count; i++)
                                    {
                                        var part = postInfo.Pages[i - 1];
                                        // 上面的请求可以获取page总数和名称，但没有清晰度信息，所以需要深层请求
                                        var videoSource = await _client.GetVideoSource(postInfo.Aid, part.Cid, ct);
                                        var bestQuality = videoSource.SupportFormats.MaxBy(t => t.Quality);

                                        var videoValues = new Dictionary<BilibiliNamingFields, object?>
                                        {
                                            {BilibiliNamingFields.UploaderId, post.Upper?.Mid},
                                            {BilibiliNamingFields.UploaderName, post.Upper?.Name},
                                            {BilibiliNamingFields.AId, post.Id},
                                            {BilibiliNamingFields.BvId, post.BvId},
                                            {BilibiliNamingFields.PostTitle, post.Title},
                                            {BilibiliNamingFields.CId, part.Cid},
                                            {BilibiliNamingFields.PartNo, i},
                                            {BilibiliNamingFields.PartName, part.Part},
                                            {BilibiliNamingFields.QualityName, bestQuality?.Description},
                                            {BilibiliNamingFields.Extension, ".mp4"},
                                        };

                                        Current =
                                            $"[{postIndex + 1}/{totalCount}][{post.Upper?.Name}]{post.Title} - P{i} - {bestQuality?.Description}";
                                        await OnCurrentChangedInternal();

                                        var wrappers =
                                            (await _specialTextService.GetAll(a => a.Type == SpecialTextType.Wrapper))
                                            .Where(a => a.Value1.IsNotEmpty() && a.Value2.IsNotEmpty())
                                            .GroupBy(a => a.Value1)
                                            .ToDictionary(a => a.Key, a => a.FirstOrDefault()!.Value2!);

                                        var keyFilename = await BuildDownloadFilename(videoValues);

                                        var keyFilenameWithoutExt = Path.GetFileNameWithoutExtension(keyFilename);
                                        var keyFileFullname = Path.Combine(task.DownloadPath, keyFilename);
                                        var keyFileDirectory = Path.GetDirectoryName(keyFileFullname)!.Trim();

                                        if (File.Exists(keyFileFullname))
                                        {
                                            var newGlobalProgress = _getGlobalProgress(100, i,
                                                postInfo.Pages.Count, postIndex, totalCount);
                                            if (globalProgress != newGlobalProgress)
                                            {
                                                globalProgress = newGlobalProgress;
                                                await OnProgressInternal(globalProgress);
                                            }
                                        }
                                        else
                                        {
                                            var tempFilenameWithoutExtension =
                                                DateTime.Now.ToString(
                                                    $"yyyyMMdd-HHmmssfff-{Guid.NewGuid().ToString("N")[..6]}");

                                            var urlForLux = BiliBiliApiUrls.PostPartPage
                                                .Replace("{aid}", postInfo.Aid.ToString())
                                                .Replace("{part}", i.ToString());

                                            var luxRet = await _luxService.Download(urlForLux, cookie, true, 1,
                                                tempVideoDirectory, tempFilenameWithoutExtension,
                                                async p =>
                                                {
                                                    var newGlobalProgress = _getGlobalProgress(p, i,
                                                        postInfo.Pages.Count, postIndex, totalCount);
                                                    if (globalProgress != newGlobalProgress)
                                                    {
                                                        globalProgress = newGlobalProgress;
                                                        await OnProgressInternal(globalProgress);
                                                    }
                                                }, null);
                                            
                                            if (luxRet.ExitCode != 0)
                                            {
                                                var message =
                                                    new StringBuilder($"An error occurred during using lux")
                                                        .AppendLine()
                                                        .AppendLine($"[ExitCode]{luxRet.ExitCode}")
                                                        .AppendLine($"[Command]{luxRet.Command}")
                                                        .AppendLine($"[Output]{luxRet.Output}")
                                                        .AppendLine($"[Error]{luxRet.Error}");
                                                throw new Exception(message.ToString());
                                            }

                                            var targetFiles = Directory.GetFiles(tempVideoDirectory).Where(t =>
                                                    Path.GetFileNameWithoutExtension(t) == tempFilenameWithoutExtension)
                                                .ToArray();

                                            Directory.CreateDirectory(keyFileDirectory);

                                            foreach (var tf in targetFiles)
                                            {
                                                var dest = Path.Combine(keyFileDirectory,
                                                    $"{keyFilenameWithoutExt}{Path.GetExtension(tf)}");
                                                if (File.Exists(dest))
                                                {
                                                    File.Delete(tf);
                                                }
                                                else
                                                {
                                                    File.Move(tf, dest, false);
                                                }
                                            }

                                            var coverFullname =
                                                Path.Combine(keyFileDirectory, $"{keyFilenameWithoutExt}{coverExt}");
                                            if (!File.Exists(coverFullname))
                                            {
                                                await File.WriteAllBytesAsync(coverFullname, coverBytes, ct);
                                            }
                                        }
                                    }
                                }
                                catch
                                {
                                    throw;
                                }
                            }
                        }
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    doneCount++;

                    await OnCheckpointChangedInternal(checkpointContext.BuildCheckpoint(post.Id.ToString()));

                    var newProgress = (decimal) doneCount / totalCount * 100;
                    await OnProgressInternal(newProgress);
                }

                page++;
                if (!posts.HasMore || taskIsDone)
                {
                    await OnCheckpointChangedInternal(checkpointContext.BuildCheckpointOnComplete());

                    break;
                }
            }
        }

        public override void Dispose()
        {
        }
    }
}