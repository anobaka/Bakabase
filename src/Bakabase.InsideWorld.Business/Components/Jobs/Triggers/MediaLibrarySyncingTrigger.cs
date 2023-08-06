using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Jobs;
using Bakabase.Infrastructures.Components.Logging;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Logging.LogService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Bakabase.InsideWorld.Business.Components.Jobs.Triggers
{
    [DisallowConcurrentExecution]
    public class MediaLibrarySyncingTrigger : SimpleJob
    {
        protected MediaLibraryService MediaLibraryService => GetRequiredService<MediaLibraryService>();
        protected LogService LogService => GetRequiredService<LogService>();

        private InsideWorldOptionsManagerPool InsideWorldOptionsManager => GetRequiredService<InsideWorldOptionsManagerPool>();

        public override async Task Execute(AsyncServiceScope scope)
        {
            if (MediaLibraryService.SyncTaskInformation?.Status != BackgroundTaskStatus.Running)
            {
                var options = InsideWorldOptionsManager.Resource;
                if (options.Value.LastSyncDt.Add(InsideWorldDefaultOptions.DefaultLibrarySyncInterval) < DateTime.Now)
                {
                    MediaLibraryService.SyncInBackgroundTask();
                }
            }
        }
    }
}