// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Text;
// using System.Threading.Tasks;
// using Bakabase.Infrastructures.Components.Jobs;
// using Bakabase.InsideWorld.Business.Components.Downloader;
// using Bakabase.InsideWorld.Business.Components.Jobs.Triggers;
// using Bootstrap.Components.Notification.Abstractions;
// using Bootstrap.Components.Notification.Abstractions.Services;
//
// namespace Bakabase.InsideWorld.Business.Components.Jobs
// {
//     public class InsideWorldJobManager : SimpleJobManager
//     {
//         public InsideWorldJobManager(IServiceProvider serviceProvider) : base(serviceProvider)
//         {
//         }
//
//         protected override async Task ScheduleJobs()
//         {
//             // await ScheduleJob<MediaLibrarySyncingTrigger>(TimeSpan.FromMinutes(1));
//             // await ScheduleJob<NfoGeneratingTrigger>(TimeSpan.FromMinutes(1));
//             await ScheduleJob<EnhancementTrigger>(TimeSpan.FromMinutes(1));
//             await ScheduleJob<FileMovingTrigger>(TimeSpan.FromMinutes(1));
//             await ScheduleJob<DownloaderTrigger>(TimeSpan.FromMinutes(1));
//             await ScheduleJob<CacheTrigger>(TimeSpan.FromMinutes(1));
//         }
//     }
// }