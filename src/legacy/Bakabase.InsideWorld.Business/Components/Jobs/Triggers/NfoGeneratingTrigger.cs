// using System;
// using System.Collections.Generic;
// using System.IO;
// using System.Linq;
// using System.Text;
// using System.Threading;
// using System.Threading.Tasks;
// using Bakabase.Infrastructures.Components.Jobs;
// using Bakabase.Infrastructures.Components.Logging;
// using Bakabase.InsideWorld.Business.Components.Resource.Components;
// using Bakabase.InsideWorld.Business.Components.Tasks;
// using Bakabase.InsideWorld.Business.Configurations;
// using Bakabase.InsideWorld.Business.Services;
// using Bakabase.InsideWorld.Models.Models.Dtos;
// using Bakabase.InsideWorld.Models.RequestModels;
// using Bootstrap.Components.Miscellaneous.ResponseBuilders;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Options;
// using Quartz;
//
// namespace Bakabase.InsideWorld.Business.Components.Jobs.Triggers
// {
//     [DisallowConcurrentExecution]
//     internal class NfoGeneratingTrigger : SimpleJob
//     {
//         private ResourceService ResourceService => GetRequiredService<ResourceService>();
//         private InsideWorldOptionsManagerPool InsideWorldOptionsManager => GetRequiredService<InsideWorldOptionsManagerPool>();
//
//         public override async Task Execute(AsyncServiceScope scope)
//         {
//             var options = InsideWorldOptionsManager.Resource;
//             if (options.Value.LastNfoGenerationDt.Add(InsideWorldDefaultOptions.DefaultNfoGenerationInterval) <
//                 DateTime.Now)
//             {
//                 await ResourceService.TryToGenerateNfoInBackground();
//             }
//         }
//     }
// }