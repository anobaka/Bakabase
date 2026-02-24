using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai
{
    public class ExHentaiSingleWorkDownloader(
        IServiceProvider serviceProvider,
        IStringLocalizer<SharedResource> localizer,
        ExHentaiClient client,
        ISpecialTextService specialTextService,
        IHostEnvironment env)
        : AbstractExHentaiDownloader(serviceProvider, localizer,
            client, specialTextService, env)
    {
        public override ExHentaiDownloadTaskType EnumTaskType => ExHentaiDownloadTaskType.SingleWork;

        protected override async Task StartCore(DownloadTask task, ExHentaiTaskOptions options, CancellationToken ct)
        {
            await DownloadSingleWork(task.Key, task.Checkpoint, task.DownloadPath, OnNameAcquiredInternal,
                async current =>
                {
                    Current = current;
                    await OnCurrentChangedInternal();
                }, OnProgressInternal, OnCheckpointChangedInternal, ct, options.PreferTorrent);
        }
    }
}
