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
            var detail = await Client.ParseDetail(url, false);
            if (detail == null)
            {
                throw new Exception($"Got empty response from: {url}");
            }

            var betterName = detail.RawName.IsNullOrEmpty() ? detail.Name : detail.RawName;
            if (onNameAcquired != null)
            {
                await onNameAcquired(betterName);
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
                            while (true)
                            {
                                try
                                {
                                    data = await Client.DownloadImage(pageUrl);
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

                            await File.WriteAllBytesAsync(fullname, data, ct);

                            doneStates[fullname] = true;
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