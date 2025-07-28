using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Cien
{
    public abstract class AbstractCienDownloader : AbstractDownloader<CienDownloadTaskType>
    {
        private readonly ISpecialTextService _specialTextService;
        public override ThirdPartyId ThirdPartyId => ThirdPartyId.Cien;

        protected AbstractCienDownloader(IServiceProvider serviceProvider, ISpecialTextService specialTextService) : base(serviceProvider)
        {
            _specialTextService = specialTextService;
        }

        protected override async Task StartCore(DownloadTaskDbModel task, CancellationToken ct)
        {
            Logger.LogInformation("Starting Cien download task: {TaskId}, Type: {TaskType}", task.Id, TaskType);
            
            try
            {
                // Get unified downloader options
                var options = await GetDownloaderOptionsAsync();
                
                // Get naming convention and download path
                var namingConvention = options.NamingConvention ?? GetDefaultNamingConvention();
                var downloadPath = options.DefaultPath ?? Path.GetTempPath();

                // Handle different task types
                switch (EnumTaskType)
                {
                    case CienDownloadTaskType.Creator:
                        await DownloadFromAuthor(task, downloadPath, namingConvention, ct);
                        break;
                    case CienDownloadTaskType.Following:
                        await DownloadFromFollowing(task, downloadPath, namingConvention, ct);
                        break;
                    case CienDownloadTaskType.SinglePost:
                        await DownloadSingleArticle(task, downloadPath, namingConvention, ct);
                        break;
                    default:
                        throw new NotSupportedException($"Task type {TaskType} is not supported");
                }

                Logger.LogInformation("Cien download task completed: {TaskId}", task.Id);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in Cien downloader for task: {TaskId}", task.Id);
                throw;
            }
        }

        protected abstract Task DownloadFromAuthor(DownloadTaskDbModel task, string downloadPath, string namingConvention, CancellationToken ct);
        protected abstract Task DownloadFromFollowing(DownloadTaskDbModel task, string downloadPath, string namingConvention, CancellationToken ct);
        protected abstract Task DownloadSingleArticle(DownloadTaskDbModel task, string downloadPath, string namingConvention, CancellationToken ct);
        protected abstract Task DownloadByCategory(DownloadTaskDbModel task, string downloadPath, string namingConvention, CancellationToken ct);

        protected string BuildFileName(Dictionary<string, object> namingContext, string namingConvention)
        {
            var fileName = namingConvention;
            foreach (var kvp in namingContext)
            {
                fileName = fileName.Replace($"{{{kvp.Key}}}", kvp.Value?.ToString() ?? "");
            }
            return fileName.RemoveInvalidFileNameChars()!;
        }

        protected async Task ReportProgress(decimal progress)
        {
            await OnProgressInternal(progress);
        }

        protected async Task UpdateCurrent(string current)
        {
            Current = current;
            await OnCurrentChangedInternal();
        }

        protected async Task UpdateCheckpoint(string checkpoint)
        {
            Checkpoint = checkpoint;
            NextCheckpoint = checkpoint;
            await OnCheckpointChangedInternal(checkpoint);
        }
    }
}