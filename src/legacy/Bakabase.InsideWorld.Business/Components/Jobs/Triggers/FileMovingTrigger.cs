// using System;
// using System.Collections.Generic;
// using System.IO;
// using System.Linq;
// using System.Text;
// using System.Threading;
// using System.Threading.Tasks;
// using Bakabase.Infrastructures.Components.Jobs;
// using Bakabase.InsideWorld.Business.Components.FileMover;
// using Bakabase.InsideWorld.Business.Components.Tasks;
// using Bakabase.InsideWorld.Business.Configurations;
// using Bakabase.InsideWorld.Business.Services;
// using Bakabase.InsideWorld.Models.Extensions;
// using Bootstrap.Components.Miscellaneous.ResponseBuilders;
// using Bootstrap.Components.Storage;
// using Bootstrap.Extensions;
// using Microsoft.Extensions.DependencyInjection;
// using Quartz;
//
// namespace Bakabase.InsideWorld.Business.Components.Jobs.Triggers
// {
//     [DisallowConcurrentExecution]
//     public class FileMovingTrigger : SimpleJob
//     {
//         private IFileMover FileMover => GetRequiredService<IFileMover>();
//
//         public override async Task Execute(AsyncServiceScope scope)
//         {
//             FileMover.TryStartMovingFiles();
//         }
//     }
// }