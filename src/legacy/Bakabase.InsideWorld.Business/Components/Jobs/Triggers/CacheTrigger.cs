// using System.Threading;
// using System.Threading.Tasks;
// using Bakabase.Abstractions.Models.Domain.Constants;
// using Bakabase.Abstractions.Services;
// using Bakabase.Infrastructures.Components.Jobs;
// using Bakabase.InsideWorld.Business.Components.Tasks;
// using Bakabase.Modules.Enhancer.Abstractions.Services;
// using Bootstrap.Components.Miscellaneous.ResponseBuilders;
// using Microsoft.Extensions.DependencyInjection;
// using Quartz;
//
// namespace Bakabase.InsideWorld.Business.Components.Jobs.Triggers;
//
// [DisallowConcurrentExecution]
// public class CacheTrigger : SimpleJob
// {
//     private BackgroundTaskHelper BackgroundTaskHelper => GetRequiredService<BackgroundTaskHelper>();
//
//     public override async Task Execute(AsyncServiceScope scope)
//     {
//         BackgroundTaskHelper.RunInNewScope<IResourceService>(BackgroundTaskName.PrepareCache.ToString(),
//             async (service, task) =>
//             {
//                 await service.PrepareCache(p => task.Percentage = p, task.Cts.Token);
//
//                 return BaseResponseBuilder.Ok;
//             });
//     }
// }