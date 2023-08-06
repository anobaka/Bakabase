using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Downloader.Checkpoint;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Naming;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models;
using Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai;
using Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai.Models;
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
    public class ExHentaiSingleWorkDownloader : AbstractExHentaiDownloader
    {
        public ExHentaiSingleWorkDownloader(IServiceProvider serviceProvider,
            IStringLocalizer<SharedResource> localizer, ExHentaiClient client, SpecialTextService specialTextService,
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