using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Fanbox
{
    public abstract class AbstractFanboxDownloader : AbstractDownloader<FanboxDownloadTaskType>
    {
        private readonly ISpecialTextService _specialTextService;
        public override ThirdPartyId ThirdPartyId => ThirdPartyId.Fanbox;

        protected AbstractFanboxDownloader(IServiceProvider serviceProvider, ISpecialTextService specialTextService) : base(serviceProvider)
        {
            _specialTextService = specialTextService;
        }

        protected override async Task StartCore(DownloadTask task, CancellationToken ct)
        {
            Logger.LogInformation("Starting Fanbox download task: {TaskId}, Type: {TaskType}", task.Id, EnumTaskType);
            
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
                    case FanboxDownloadTaskType.Creator:
                        await DownloadFromCreator(task, downloadPath, namingConvention, ct);
                        break;
                    case FanboxDownloadTaskType.Following:
                        await DownloadFromFollowing(task, downloadPath, namingConvention, ct);
                        break;
                    case FanboxDownloadTaskType.SinglePost:
                        await DownloadSinglePost(task, downloadPath, namingConvention, ct);
                        break;
                    default:
                        throw new NotSupportedException($"Task type {EnumTaskType} is not supported");
                }

                Logger.LogInformation("Fanbox download task completed: {TaskId}", task.Id);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in Fanbox downloader for task: {TaskId}", task.Id);
                throw;
            }
        }

        protected abstract Task DownloadFromCreator(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct);
        protected abstract Task DownloadFromFollowing(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct);
        protected abstract Task DownloadSinglePost(DownloadTask task, string downloadPath, string namingConvention, CancellationToken ct);


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