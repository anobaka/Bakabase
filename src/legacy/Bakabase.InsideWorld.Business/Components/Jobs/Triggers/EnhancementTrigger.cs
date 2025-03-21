// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Text;
// using System.Threading;
// using System.Threading.Tasks;
// using Bakabase.Abstractions.Models.Domain.Constants;
// using Bakabase.Infrastructures.Components.Jobs;
// using Bakabase.InsideWorld.Business.Components.Tasks;
// using Bakabase.InsideWorld.Business.Services;
// using Bakabase.Modules.Enhancer.Abstractions.Services;
// using Bootstrap.Components.Miscellaneous.ResponseBuilders;
// using Microsoft.Extensions.DependencyInjection;
// using Quartz;
//
// namespace Bakabase.InsideWorld.Business.Components.Jobs.Triggers
// {
//     [DisallowConcurrentExecution]
//     internal class EnhancementTrigger : SimpleJob
//     {
//         private BackgroundTaskHelper Bth => GetRequiredService<BackgroundTaskHelper>();
//
//         public override async Task Execute(AsyncServiceScope scope)
//         {
//             Bth.RunInNewScope<IEnhancerService>(BackgroundTaskName.Enhance.ToString(), async (service, task) =>
//             {
//                 await service.EnhanceAll(p =>
//                 {
//                     task.Percentage = p;
//                     return Task.CompletedTask;
//                 }, task.Cts.Token);
//
//                 return BaseResponseBuilder.Ok;
//             }, BackgroundTaskLevel.Default);
//         }
//     }
// }