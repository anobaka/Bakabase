using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Fantia
{
    public class FantiaFollowingDownloader : AbstractFantiaDownloader
    {
        public override FantiaDownloadTaskType EnumTaskType => FantiaDownloadTaskType.Following;

        public FantiaFollowingDownloader(IServiceProvider serviceProvider, ISpecialTextService specialTextService) : base(serviceProvider, specialTextService)
        {
        }

        protected override async Task DownloadFromFanclub(DownloadTaskDbModel task, string downloadPath, string namingConvention, CancellationToken ct)
        {
            throw new NotImplementedException("Fanclub download not supported in Following downloader");
        }

        protected override async Task DownloadFromFollowing(DownloadTaskDbModel task, string downloadPath, string namingConvention, CancellationToken ct)
        {
            // Basic framework implementation
            // TODO: Implement actual Fantia API integration and download logic
            
            Logger.LogInformation("Starting Fantia following download task: {TaskId}", task.Id);
            
            try
            {
                // Placeholder for download logic
                await Task.Delay(1000, ct); // Simulate work
                
                // Example of how naming fields would be used:
                var namingContext = new Dictionary<FantiaNamingFields, object?>
                {
                    [FantiaNamingFields.PostId] = "sample_post_id",
                    [FantiaNamingFields.PostTitle] = "Sample Following Post",
                    [FantiaNamingFields.PublishDate] = DateTime.Now.ToString("yyyy-MM-dd"),
                    [FantiaNamingFields.FanclubId] = "sample_fanclub",
                    [FantiaNamingFields.FanclubName] = "Sample Fanclub",
                    [FantiaNamingFields.FileNo] = 0,
                    [FantiaNamingFields.Extension] = ".png"
                };
                
                var fileName = await BuildDownloadFilename(namingContext);
                Logger.LogDebug("Generated filename: {FileName}", fileName);
                
                Logger.LogInformation("Fantia following download task completed: {TaskId}", task.Id);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in Fantia following downloader for task: {TaskId}", task.Id);
                throw;
            }
        }

        protected override async Task DownloadSinglePost(DownloadTaskDbModel task, string downloadPath, string namingConvention, CancellationToken ct)
        {
            throw new NotImplementedException("Single post download not supported in Following downloader");
        }

        protected override async Task DownloadByPlan(DownloadTaskDbModel task, string downloadPath, string namingConvention, CancellationToken ct)
        {
            throw new NotImplementedException("Plan download not supported in Following downloader");
        }
    }
}