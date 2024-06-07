using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Downloader.Checkpoint;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Naming;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili;
using Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using NPOI.SS.Formula.Functions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Implementations
{
    public abstract class AbstractExHentaiDownloader : AbstractDownloader
    {
        protected readonly IStringLocalizer<SharedResource> Localizer;
        protected readonly ExHentaiClient Client;
        protected readonly ISpecialTextService SpecialTextService;
        protected readonly IHostEnvironment Env;

        public const string DefaultNamingConvention =
            $"{{{ExHentaiNamingFields.RawName}}}/{{{ExHentaiNamingFields.PageTitle}}}{{{ExHentaiNamingFields.Extension}}}";

        protected readonly IBOptions<ExHentaiOptions> Options;

        protected AbstractExHentaiDownloader(IServiceProvider serviceProvider,
            IStringLocalizer<SharedResource> localizer,
            ExHentaiClient client, ISpecialTextService specialTextService,
            IHostEnvironment env, IBOptions<ExHentaiOptions> options) : base(serviceProvider)
        {
            Localizer = localizer;
            Client = client;
            SpecialTextService = specialTextService;
            Env = env;
            Options = options;
        }

        public override ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;


        protected async Task DownloadSingleWork(string url, string checkpoint, string downloadPath,
            Func<string, Task> onNameAcquired,
            Func<string, Task> onCurrentChanged,
            Func<decimal, Task> onProgress,
            Func<string, Task> onCheckpointChanged,
            CancellationToken ct)
        {
            var detail = await Client.ParseDetail(url);
            if (detail == null)
            {
                throw new Exception($"Got empty response from: {url}");
            }

            var betterName = detail.RawName.IsNullOrEmpty() ? detail.Name : detail.RawName;
            if (onNameAcquired != null)
            {
                await onNameAcquired(betterName);
            }

            var baseNameSegmentsValues = new Dictionary<string, object>
            {
                {ExHentaiNamingFields.RawName, detail.RawName},
                {ExHentaiNamingFields.Name, detail.Name},
                {ExHentaiNamingFields.Category, detail.Category},
            };

            var wrappers =
                (await SpecialTextService.GetAll(a => a.Type == SpecialTextType.Wrapper))
                .Where(a => a.Value1.IsNotEmpty() && a.Value2.IsNotEmpty())
                .GroupBy(a => a.Value1)
                .ToDictionary(a => a.Key, a => a.FirstOrDefault()!.Value2);

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

                            var fullNameSegmentsValues = new Dictionary<string, object>(baseNameSegmentsValues)
                            {
                                [ExHentaiNamingFields.PageTitle] = Path.GetFileNameWithoutExtension(title),
                                [ExHentaiNamingFields.Extension] = extension
                            };

                            var keyFilename = DownloaderUtils
                                .BuildDownloadFilename<ExHentaiNamingFields>(
                                    Options.Value.Downloader.NamingConvention.IsNotEmpty()
                                        ? Options.Value.Downloader.NamingConvention
                                        : DefaultNamingConvention, fullNameSegmentsValues, wrappers)
                                .RemoveInvalidFilePathChars();
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
                var sm = new SemaphoreSlim(Options.Value.Downloader.Threads, Options.Value.Downloader.Threads);
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