using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Implementations
{
    public class ExHentaiSingleWorkDownloader : AbstractExHentaiDownloader
    {
        public ExHentaiSingleWorkDownloader(IServiceProvider serviceProvider,
            IStringLocalizer<SharedResource> localizer, ExHentaiClient client, ISpecialTextService specialTextService,
            IHostEnvironment env, IBOptionsManager<ExHentaiOptions> optionsManager) : base(serviceProvider, localizer,
            client, specialTextService, env, optionsManager)
        {
        }

        protected override async Task StartCore(DownloadTask task, CancellationToken ct)
        {
            await DownloadSingleWork(task.Key, task.Checkpoint, task.DownloadPath, OnNameAcquiredInternal,
                async current =>
                {
                    Current = current;
                    await OnCurrentChangedInternal();
                }, OnProgressInternal, OnCheckpointChangedInternal, ct);
        }
    }
}