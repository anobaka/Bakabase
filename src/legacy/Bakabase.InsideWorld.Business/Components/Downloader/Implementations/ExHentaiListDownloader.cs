using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Checkpoint;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.ThirdParty.ExHentai;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using NPOI.SS.Formula.Functions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Implementations
{
    public class ExHentaiListDownloader : AbstractExHentaiDownloader
    {
        public ExHentaiListDownloader(IServiceProvider serviceProvider, IStringLocalizer<SharedResource> localizer,
            ExHentaiClient client, ISpecialTextService specialTextService, IHostEnvironment env,
            IBOptionsManager<ExHentaiOptions> optionsManager) : base(serviceProvider, localizer, client,
            specialTextService, env, optionsManager)
        {
        }

        protected override async Task StartCore(DownloadTask task, CancellationToken ct)
        {
            var checkpointContext = new RangeCheckpointContext(task.Checkpoint);

            var doneCount = 0;
            var taskIsDone = false;

            var nextUrl = task.Key;
            var totalCount = 0;

            while (true)
            {
                var result = await Client.ParseList(nextUrl);

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
                                await DownloadSingleWork(r.Url, null, task.DownloadPath, async name =>
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
                                }, null, ct);
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