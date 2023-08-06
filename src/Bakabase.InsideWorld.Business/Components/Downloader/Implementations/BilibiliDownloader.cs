using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Downloader.Checkpoint;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Naming;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models.Constants;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using CliWrap;
using CsQuery.ExtensionMethods;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SQLitePCL;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Implementations
{
    public class BilibiliDownloader : AbstractDownloader
    {
        private IStringLocalizer<SharedResource> _localizer;
        private readonly IHostEnvironment _env;
        private readonly BilibiliClient _client;
        private readonly InsideWorldOptionsManagerPool _optionsManager;
        private readonly SpecialTextService _specialTextService;

        public override ThirdPartyId ThirdPartyId => ThirdPartyId.Bilibili;

        public BilibiliDownloader(IStringLocalizer<SharedResource> localizer,
            BilibiliClient client, InsideWorldOptionsManagerPool optionsManager, SpecialTextService specialTextService,
            IHostEnvironment env, IServiceProvider serviceProvider) : base(serviceProvider)
        {
            _localizer = localizer;
            _client = client;
            _optionsManager = optionsManager;
            _specialTextService = specialTextService;
            _env = env;
        }

        private string LuxBin => BuildLuxBinPath(_env);

        public static string BuildLuxBinPath(IHostEnvironment env) =>
            Path.Combine(env.ContentRootPath, "libs", "lux.exe");

        public const string DefaultNamingConvention =
            $"[{{{BilibiliNamingFields.UploaderName}}}]{{{BilibiliNamingFields.PostTitle}}}/[P{{{BilibiliNamingFields.PartNo}}}][{{{BilibiliNamingFields.QualityName}}}]{{{BilibiliNamingFields.PartName}}}{{{BilibiliNamingFields.Extension}}}";

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

            var options = _optionsManager.Bilibili;
            var cookie = options.Value.Cookie;
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

                                        var videoValues = new Dictionary<string, object>
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
                                            .ToDictionary(a => a.Key, a => a.FirstOrDefault()!.Value2);

                                        var keyFilename = DownloaderUtils.BuildDownloadFilename<BilibiliNamingFields>(
                                                options.Value.Downloader.NamingConvention.IsNotEmpty()
                                                    ? options.Value.Downloader.NamingConvention
                                                    : DefaultNamingConvention, videoValues, wrappers)
                                            .RemoveInvalidFilePathChars();

                                        var keyFilenameWithoutExt = Path.GetFileNameWithoutExtension(keyFilename);
                                        var keyFileFullname = Path.Combine(task.DownloadPath, keyFilename);
                                        var keyFileDirectory = Path.GetDirectoryName(keyFileFullname)!;

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

                                            var urlForLux = BiliBiliApiConstants.PostPartPage
                                                .Replace("{aid}", postInfo.Aid.ToString())
                                                .Replace("{part}", i.ToString());
                                            var arguments = new[]
                                            {
                                                "-c", cookie,
                                                "-C",
                                                "-n", 1.ToString(),
                                                "-o", tempVideoDirectory,
                                                "-O", tempFilenameWithoutExtension,
                                                // "-o", dirFullname,
                                                // "-O", filenameWithoutExtension,
                                                urlForLux
                                            };
                                            var regex = new Regex(@"(\d+(\.\d+)?)%");
                                            var cmdErrorSb = new StringBuilder();
                                            var error2Sb = new StringBuilder();
                                            var cmd = Cli.Wrap(LuxBin)
                                                .WithValidation(CommandResultValidation.None)
                                                .WithArguments(arguments, true)
                                                .WithStandardOutputPipe(
                                                    PipeTarget.ToDelegate(str =>
                                                    {
                                                        if (cmdErrorSb.Length > 0)
                                                        {
                                                            cmdErrorSb.Append(Environment.NewLine).Append(str);
                                                        }
                                                        else
                                                        {
                                                            if (str.Contains("error:"))
                                                            {
                                                                cmdErrorSb.Append(str);
                                                            }
                                                        }
                                                    }, Encoding.UTF8)
                                                )
                                                .WithStandardErrorPipe(new CustomCliWrapPipeTarget(async str =>
                                                {
                                                    var match = regex.Match(str);
                                                    if (match.Success)
                                                    {
                                                        var partProgress = decimal.Parse(match.Groups[1].Value);
                                                        var newGlobalProgress = _getGlobalProgress(partProgress, i,
                                                            postInfo.Pages.Count, postIndex, totalCount);
                                                        if (globalProgress != newGlobalProgress)
                                                        {
                                                            globalProgress = newGlobalProgress;
                                                            await OnProgressInternal(globalProgress);
                                                        }
                                                    }

                                                    error2Sb.Append(str);
                                                }, Encoding.UTF8));
                                            // It will take several seconds to cancel the CancellationToken if ct was passed here.
                                            var r = await cmd.ExecuteAsync();
                                            if (r.ExitCode != 0)
                                            {
                                                throw new Exception(
                                                    $"Lux error code: {r.ExitCode}, output: {Environment.NewLine}{cmdErrorSb}");
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