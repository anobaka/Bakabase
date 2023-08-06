using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Jobs;
using Bakabase.InsideWorld.Business.Services;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Bakabase.InsideWorld.Business.Components.Jobs.Triggers
{
    [DisallowConcurrentExecution]
    internal class EnhancementTrigger : SimpleJob
    {
        protected EnhancementRecordService EnhancementRecordService => GetRequiredService<EnhancementRecordService>();

        public override async Task Execute(AsyncServiceScope scope)
        {
            await EnhancementRecordService.StartEnhancingInBackgroundTask();
        }
    }
}