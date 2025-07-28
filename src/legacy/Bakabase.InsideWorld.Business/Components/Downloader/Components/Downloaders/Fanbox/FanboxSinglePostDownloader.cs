using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Fanbox
{
    public class FanboxSinglePostDownloader : AbstractFanboxDownloader
    {
        public override FanboxDownloadTaskType EnumTaskType => FanboxDownloadTaskType.SinglePost;

        public FanboxSinglePostDownloader(IServiceProvider serviceProvider, ISpecialTextService specialTextService) : base(serviceProvider, specialTextService)
        {
        }

        protected override async Task DownloadFromCreator(DownloadTaskDbModel task, string downloadPath, string namingConvention, CancellationToken ct)
        {
            // Not applicable for single post downloader
            throw new NotSupportedException("Creator download not supported by single post downloader");
        }

        protected override async Task DownloadFromFollowing(DownloadTaskDbModel task, string downloadPath, string namingConvention, CancellationToken ct)
        {
            // Not applicable for single post downloader
            throw new NotSupportedException("Following download not supported by single post downloader");
        }

        protected override async Task DownloadSinglePost(DownloadTaskDbModel task, string downloadPath, string namingConvention, CancellationToken ct)
        {
            // Framework implementation for downloading a single specific post
            Logger.LogInformation("Downloading single post for task: {TaskId}", task.Id);
            
            await UpdateCurrent("Initializing single post download...");
            
            try
            {
                // TODO: Parse post ID from task.Key
                var postId = ExtractPostIdFromTask(task);
                
                await UpdateCurrent($"Fetching post details: {postId}");
                await ReportProgress(20);
                
                // TODO: Implement actual API call to fetch post details
                var post = await GetPostDetails(postId, ct);
                
                await UpdateCheckpoint($"single_post:{postId}");
                await UpdateCurrent($"Downloading files for post: {post.Title}");
                await ReportProgress(50);
                
                // TODO: Download all files associated with the post
                await DownloadPostFiles(post, downloadPath, namingConvention, ct);
                
                await UpdateCurrent("Single post download completed");
                await ReportProgress(100);
            }
            catch (OperationCanceledException)
            {
                Logger.LogInformation("Single post download cancelled for task: {TaskId}", task.Id);
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error downloading single post for task: {TaskId}", task.Id);
                throw;
            }
        }

        private string ExtractPostIdFromTask(DownloadTaskDbModel task)
        {
            // TODO: Parse post ID from task key/url
            // Example: "https://creator.fanbox.cc/posts/12345" -> "12345"
            return "sample_post_id";
        }

        private async Task<FanboxSinglePost> GetPostDetails(string postId, CancellationToken ct)
        {
            // TODO: Implement API call to fetch post details
            await Task.Delay(1000, ct); // Simulate API call
            
            return new FanboxSinglePost 
            { 
                Id = postId, 
                Title = "Sample Single Post",
                CreatorName = "Sample Creator",
                CreatorId = "sample_creator"
            };
        }

        private async Task DownloadPostFiles(FanboxSinglePost post, string downloadPath, string namingConvention, CancellationToken ct)
        {
            // TODO: Download all files for the specific post
            await Task.Delay(1000, ct); // Simulate download
            
            // Simulate multiple files in a post
            var fileCount = 3;
            for (int i = 0; i < fileCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                
                var namingContext = new Dictionary<FanboxNamingFields, object>
                {
                    [FanboxNamingFields.PostId] = post.Id,
                    [FanboxNamingFields.PostTitle] = post.Title,
                    [FanboxNamingFields.PublishDate] = DateTime.Now.ToString("yyyy-MM-dd"),
                    [FanboxNamingFields.CreatorId] = post.CreatorId,
                    [FanboxNamingFields.CreatorName] = post.CreatorName,
                    [FanboxNamingFields.FileNo] = i,
                    [FanboxNamingFields.Extension] = i == 0 ? ".jpg" : (i == 1 ? ".png" : ".gif")
                };
                
                var fileName = await BuildDownloadFilename(namingContext);
                Logger.LogDebug("Would save file {FileIndex} as: {FileName}", i, fileName);
                
                // Update progress for each file
                var progress = 50 + (decimal)(i + 1) / fileCount * 50;
                await ReportProgress(progress);
                
                await Task.Delay(200, ct); // Simulate file download time
            }
        }

        private class FanboxSinglePost
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string CreatorId { get; set; } = "";
            public string CreatorName { get; set; } = "";
        }
    }
}