using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bootstrap.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai
{
    public class ExHentaiListDownloader(
        IServiceProvider serviceProvider,
        IStringLocalizer<SharedResource> localizer,
        ExHentaiClient client,
        ITextVocabularyService textVocabularyService,
        IHostEnvironment env)
        : AbstractExHentaiDownloader(serviceProvider, localizer, client,
            textVocabularyService, env)
    {
        public override ExHentaiDownloadTaskType EnumTaskType => ExHentaiDownloadTaskType.List;

        protected override async Task StartCore(DownloadTask task, ExHentaiTaskOptions options, CancellationToken ct)
        {
            var checkpointContext = new RangeCheckpointContext(task.Checkpoint);
            var results = GetRequiredService<DownloadResultService>();
            var preferOriginal = GetRequiredService<IBOptionsManager<ExHentaiOptions>>().Value.PreferOriginalImages;
            var missingSources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var gallery in (await results.GetByTaskAsync(task.Id, ct)).GroupBy(x => x.SourceKey))
            {
                var latest = gallery.MaxBy(x => x.Id)!;
                var available = await CanReuseResultAsync(results, latest, options.PreferTorrent, preferOriginal, ct);
                if (available && latest.Kind == DownloadResultKind.TorrentMetadata)
                {
                    // Valid managed metadata can repair the user copy without a network request,
                    // but a list checkpoint must first let that gallery reach its producer.
                    var userCopy = ExHentaiDownloadResultHelper.GetTorrentDownloadPath(latest);
                    available = userCopy != null &&
                        await DownloadResultService.ReadReusableTorrentMetadataAsync(latest, ct, userCopy) != null;
                }
                if (!available)
                    missingSources.Add(gallery.Key);
            }
            var traversingCompletedRange = false;

            var doneCount = 0;
            var taskIsDone = false;

            var nextUrl = task.Key;
            var totalCount = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var result = await Client.ParseList(nextUrl, ct);

                if (result.Resources?.Any() == true && result.ResultCount == 0)
                {
                    result.ResultCount = totalCount + result.Resources.Count;
                }

                totalCount = result.ResultCount;
                var unitWorkProgress = totalCount == 0 ? 0 : 100m / totalCount;
                if (result.Resources?.Any() == true)
                {
                    var workIndex = doneCount;
                    // handle resources
                    foreach (var r in result.Resources)
                    {
                        var action = checkpointContext.Analyze(r.Id.ToString());
                        // A completed range must not conceal a lost recorded result. Traverse
                        // that range to recover missing works, preserving intentional skips for
                        // works without an unavailable result.
                        if (action == RangeCheckpointContext.AnalyzeResult.AllTaskIsDone && missingSources.Count > 0)
                            traversingCompletedRange = true;
                        if (traversingCompletedRange) action = RangeCheckpointContext.AnalyzeResult.Skip;
                        if (missingSources.Contains(ExHentaiDownloadResultHelper.NormalizeSourceKey(r.Url)))
                            action = RangeCheckpointContext.AnalyzeResult.Download;

                        Current = $"[{doneCount + 1}/{totalCount}]{r.RawName ?? r.Name}";
                        await OnCurrentChangedInternal();

                        var betterName = r.RawName ?? r.Name;

                        switch (action)
                        {
                            case RangeCheckpointContext.AnalyzeResult.AllTaskIsDone:
                                doneCount = totalCount;
                                await OnProgressInternal(100);
                                taskIsDone = true;
                                break;
                            case RangeCheckpointContext.AnalyzeResult.Skip:
                                Current = $"[{workIndex + 1}/{totalCount}]{betterName}";
                                await OnCurrentChangedInternal();
                                break;
                            case RangeCheckpointContext.AnalyzeResult.Download:
                                // A follow-up workflow may move files while this list continues.
                                // Give each gallery its own root, even when the naming convention
                                // is flat or two galleries share the same title.
                                var workPath = ExHentaiDownloadResultHelper.GetWorkDirectory(task.DownloadPath,
                                    r.Url, options.DownloadResultWorkflowId);
                                await DownloadSingleWork(task.Id, r.Url, null, workPath, async name =>
                                {
                                    betterName = name;
                                    Current = $"[{doneCount + 1}/{totalCount}]{betterName}";
                                    await OnCurrentChangedInternal();
                                }, async current =>
                                {
                                    Current = $"[{doneCount + 1}/{totalCount}][{current}]{betterName}";
                                    await OnCurrentChangedInternal();
                                }, async p =>
                                {
                                    var newProgress = unitWorkProgress * (doneCount + p / 100m);
                                    await OnProgressInternal(newProgress);
                                }, null, ct, options.PreferTorrent,
                                    resultWorkflowId: options.DownloadResultWorkflowId);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        doneCount++;

                        await OnCheckpointChangedInternal(checkpointContext.BuildCheckpoint(r.Id.ToString()));

                        var newProgress = (decimal) doneCount / totalCount * 100;
                        await OnProgressInternal(newProgress);
                    }

                    // exit
                    if (result.NextListUrl.IsNullOrEmpty() || taskIsDone)
                    {
                        await OnCheckpointChangedInternal(checkpointContext.BuildCheckpointOnComplete());
                        break;
                    }
                    else
                    {
                        nextUrl = result.NextListUrl;
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
