using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bootstrap.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai
{
    public abstract class AbstractExHentaiDownloader : AbstractDownloader<ExHentaiDownloadTaskType>
    {
        protected readonly IStringLocalizer<SharedResource> Localizer;
        protected readonly ExHentaiClient Client;
        protected readonly ISpecialTextService SpecialTextService;
        protected readonly IHostEnvironment Env;
        
        protected AbstractExHentaiDownloader(IServiceProvider serviceProvider,
            IStringLocalizer<SharedResource> localizer,
            ExHentaiClient client, ISpecialTextService specialTextService,
            IHostEnvironment env) : base(serviceProvider)
        {
            Localizer = localizer;
            Client = client;
            SpecialTextService = specialTextService;
            Env = env;
        }

        public override ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;


        protected async Task DownloadSingleWork(string url, string checkpoint, string downloadPath,
            Func<string, Task> onNameAcquired,
            Func<string, Task> onCurrentChanged,
            Func<decimal, Task> onProgress,
            Func<string, Task> onCheckpointChanged,
            CancellationToken ct)
        {
            // Parse detail with torrent info to check if torrents are available
            var detail = await Client.ParseDetail(url, true);
            if (detail == null)
            {
                throw new Exception($"Got empty response from: {url}");
            }

            var betterName = detail.RawName.IsNullOrEmpty() ? detail.Name : detail.RawName;
            if (onNameAcquired != null)
            {
                await onNameAcquired(betterName);
            }

            // Check if torrents are available and download torrent instead of images
            if (detail.Torrents?.Any() == true)
            {
                // Select the best torrent (largest size, most recent)
                var bestTorrent = detail.Torrents
                    .OrderByDescending(t => t.Size)
                    .ThenByDescending(t => t.UpdatedAt)
                    .First();

                var path = Path.Combine(downloadPath, $"{betterName.RemoveInvalidFilePathChars()}.torrent");

                if (onCurrentChanged != null)
                {
                    await onCurrentChanged("正在下载种子文件...");
                }

                await Client.DownloadTorrent(bestTorrent.DownloadUrl, path);

                if (onProgress != null)
                {
                    await onProgress(100);
                }

                if (onCheckpointChanged != null)
                {
                    await onCheckpointChanged("completed");
                }

                return;
            }

            var baseNameSegmentsValues = new Dictionary<ExHentaiNamingFields, object?>
            {
                {ExHentaiNamingFields.RawName, detail.RawName},
                {ExHentaiNamingFields.Name, detail.Name},
                {ExHentaiNamingFields.Category, detail.Category},
            };

            var wrappers = await SpecialTextService.GetAllWrappers();

            //var limit = await _client.GetImageLimits();
            //if (limit.Rest <= imageTitleAndPageUrls.Length)
            //{
            //    throw new Exception(
            //        $"Image limits reached, {limit.Current}/{limit.Limit}, needs {imageTitleAndPageUrls.Length}");
            //}

            var checkpointContext = new RangeCheckpointContext(checkpoint);

            var doneCount = 0;
            var taskIsDone = false;

            for (var page = 0; page < detail.PageCount; page++)
            {
                var imageTitleAndPageUrls = await Client.GetImageTitleAndPageUrlsFromDetailUrl(detail.Url, page);

                var taskDataList = new List<(string filename, string pageUrl)>();
                var options = await GetDownloaderOptionsAsync();

                foreach (var (title, pageUrl) in imageTitleAndPageUrls)
                {
                    var action = checkpointContext.Analyze(title);
                    switch (action)
                    {
                        case RangeCheckpointContext.AnalyzeResult.AllTaskIsDone:
                            if (onProgress != null)
                            {
                                await onProgress(100);
                            }

                            taskIsDone = true;
                            break;
                        case RangeCheckpointContext.AnalyzeResult.Skip:
                            doneCount++;
                            break;
                        case RangeCheckpointContext.AnalyzeResult.Download:
                            var extension = Path.GetExtension(title);

                            var fullNameSegmentsValues = new Dictionary<ExHentaiNamingFields, object?>(baseNameSegmentsValues)
                            {
                                [ExHentaiNamingFields.PageTitle] = Path.GetFileNameWithoutExtension(title),
                                [ExHentaiNamingFields.Extension] = extension
                            };

                            var keyFilename = await BuildDownloadFilename(fullNameSegmentsValues);
                            var keyFullname = Path.Combine(downloadPath, keyFilename);

                            if (!File.Exists(keyFullname))
                            {
                                taskDataList.Add((keyFullname, pageUrl));
                            }
                            else
                            {
                                doneCount++;
                            }

                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                if (onProgress != null)
                {
                    await onProgress(doneCount * 100m / detail.FileCount);
                }

                if (onCurrentChanged != null)
                {
                    await onCurrentChanged($"{doneCount}/{detail.FileCount}");
                }

                // Avoid large mount of tasks being created.
                var threads = options.MaxConcurrency;
                var sm = new SemaphoreSlim(threads, threads);
                var tasks = new ConcurrentBag<Task>();

                // There is no need to save checkpoint during downloading files, because no extra request will be sent.
                // Although, the progress and current should be changed.
                var doneStates = new ConcurrentDictionary<string, bool>();

                var tmpCount = doneCount;
                var maxDoneCount = tmpCount + taskDataList.Count;

                async Task CurrentChanged()
                {
                    if (onCurrentChanged != null)
                    {
                        var d = tmpCount + doneStates.Count(a => a.Value);
                        var s = Math.Min(maxDoneCount, d + 1);
                        var e = Math.Min(maxDoneCount, d + tasks.Count(a => !a.IsCompleted));
                        var c = s == e ? s.ToString() : $"{s}-{e}";
                        await onCurrentChanged($"{c}/{detail.FileCount}");
                    }
                }

                foreach (var (fullname, pageUrl) in taskDataList)
                {
                    var dir = Path.GetDirectoryName(fullname)!;
                    Directory.CreateDirectory(dir);

                    const int continuousFailedTaskSampleCount = 10;
                    var last10Tasks = tasks.TakeLast(continuousFailedTaskSampleCount).ToArray();
                    if (last10Tasks.Length == continuousFailedTaskSampleCount &&
                        last10Tasks.All(x => !x.IsCompletedSuccessfully))
                    {
                        throw last10Tasks.Last().Exception!;
                    }

                    await sm.WaitAsync(ct);
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await CurrentChanged();

                            const int maxTryTimes = 10;
                            var tryTimes = 0;
                            byte[] data;
                            string? contentType = null;
                            while (true)
                            {
                                try
                                {
                                    var r = await Client.DownloadImage(pageUrl);
                                    data = r.Data;
                                    contentType = r.ContentType;
                                    break;
                                }
                                catch (Exception)
                                {
                                    tryTimes++;
                                    if (tryTimes >= maxTryTimes)
                                    {
                                        throw;
                                    }
                                }
                            }

                            string MapContentTypeToExtension(string? ict)
                            {
                                return ict?.ToLowerInvariant() switch
                                {
                                    "image/jpeg" => ".jpg",
                                    "image/jpg" => ".jpg",
                                    "image/png" => ".png",
                                    "image/webp" => ".webp",
                                    "image/gif" => ".gif",
                                    "image/bmp" => ".bmp",
                                    "image/tiff" => ".tiff",
                                    _ => string.Empty
                                };
                            }

                            var targetExt = Path.GetExtension(fullname);
                            var actualExt = MapContentTypeToExtension(contentType);

                            var wrotePath = fullname;
                            var wrote = false;

                            if (actualExt.IsNullOrEmpty() || string.Equals(actualExt, targetExt, StringComparison.OrdinalIgnoreCase))
                            {
                                await File.WriteAllBytesAsync(fullname, data, ct);
                                wrote = true;
                            }
                            else
                            {
                                // Try convert to target format indicated by title
                                try
                                {
                                    using var image = Image.Load(data);
                                    await image.SaveAsync(fullname, ct);
                                    wrote = true;
                                }
                                catch
                                {
                                    // ignore conversion failure
                                }

                                if (!wrote)
                                {
                                    // Fallback: save using actual format extension
                                    var dirName = Path.GetDirectoryName(fullname)!;
                                    var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fullname);
                                    var actualFullName = Path.Combine(dirName, fileNameWithoutExt + actualExt);
                                    await File.WriteAllBytesAsync(actualFullName, data, ct);
                                    wrote = true;
                                    wrotePath = actualFullName;
                                }
                            }

                            doneStates[wrotePath] = true;
                            if (onProgress != null)
                            {
                                await onProgress((tmpCount + doneStates.Count(a => a.Value)) * 100m / detail.FileCount);
                            }

                            await CurrentChanged();
                        }
                        finally
                        {
                            sm.Release();
                        }
                    }, ct));
                }

                await Task.WhenAll(tasks);

                doneCount += taskDataList.Count;

                if (onProgress != null)
                {
                    await onProgress(doneCount * 100m / detail.FileCount);
                }

                if (onCheckpointChanged != null)
                {
                    var newCheckpoint = taskIsDone
                        ? checkpointContext.BuildCheckpointOnComplete()
                        : checkpointContext.BuildCheckpoint(imageTitleAndPageUrls.Last().Title);
                    await onCheckpointChanged(newCheckpoint);
                }

                if (taskIsDone)
                {
                    break;
                }
            }
        }
    }
}