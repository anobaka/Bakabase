using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Jobs;
using Bakabase.Infrastructures.Components.Logging;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Business.Services.Bootstraps;
using Bakabase.InsideWorld.Models.Constants;
using Quartz;

namespace Bakabase.InsideWorld.Business.Components.Jobs
{
    [DisallowConcurrentExecution]
    public class SubscriptionSyncingJob : SimpleJob
    {
        private SubscriptionService SubscriptionService => GetRequiredService<SubscriptionService>();
        private SystemPropertyService SystemPropertyService => GetRequiredService<SystemPropertyService>();
        private SqliteLogService LogService => GetRequiredService<SqliteLogService>();

        public override async Task Execute()
        {
            if (!TimeSpan.TryParse(
                    (await SystemPropertyService.GetByKey(SystemPropertyKey.SubscriptionSyncInterval, false))?.Value,
                    out var syncInterval))
            {
                syncInterval = TimeSpan.FromHours(6);
            }

            var earliestDt = DateTime.Now.Add(-syncInterval);
            var subscriptions = await SubscriptionService.GetAll();
            if (subscriptions.All(a => a.Progresses.All(b => b.LatestSyncDt > earliestDt)))
            {
                return;
            }

            await SubscriptionService.StartSync(earliestDt);
        }
    }
}