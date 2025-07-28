using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Jobs;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Microsoft.Extensions.DependencyInjection;
using Org.BouncyCastle.Utilities;
using Quartz;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components
{
    [DisallowConcurrentExecution]
    public class DownloaderTriggerJob : SimpleJob
    {
        private DownloadTaskService DownloadTaskService => GetRequiredService<DownloadTaskService>();

        public override async Task Execute(AsyncServiceScope scope)
        {
            await DownloadTaskService.TryStartAllTasks(DownloadTaskStartMode.AutoStart, null, DownloadTaskActionOnConflict.Ignore);
        }
    }
}