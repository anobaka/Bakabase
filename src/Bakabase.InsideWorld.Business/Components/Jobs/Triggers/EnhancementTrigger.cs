using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Jobs;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Bakabase.InsideWorld.Business.Components.Jobs.Triggers
{
    [DisallowConcurrentExecution]
    internal class EnhancementTrigger : SimpleJob
    {
        private IEnhancerService EnhancerService => GetRequiredService<IEnhancerService>();

        public override async Task Execute(AsyncServiceScope scope)
        {
            await EnhancerService.EnhanceAll();
        }
    }
}