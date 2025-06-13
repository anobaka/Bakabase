using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Jobs;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.DependencyInjection;
using Org.BouncyCastle.Utilities;
using Quartz;

namespace Bakabase.InsideWorld.Business.Components.Downloader
{
    [DisallowConcurrentExecution]
    public class DownloaderTrigger : SimpleJob
    {
        protected DownloadTaskService DownloadTaskService => GetRequiredService<DownloadTaskService>();

        public override async Task Execute(AsyncServiceScope scope)
        {
            await DownloadTaskService.TryStartAllTasks(DownloadTaskStartMode.AutoStart, null, DownloadTaskActionOnConflict.Ignore);
        }
    }
}