using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NPOI.OpenXmlFormats.Spreadsheet;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Downloader.DownloaderOptionsValidator;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Implementations;
using Bakabase.InsideWorld.Business.Resources;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader
{
    public sealed class DownloaderManager
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<int, IDownloader> _downloaders = new();
        private readonly IStringLocalizer<SharedResource> _localizer;
        private readonly InsideWorldLocalizer _insideWorldLocalizer;

        private readonly Dictionary<ThirdPartyId, IDownloaderOptionsValidator> _validators;

        /// <summary>
        /// todo: Types of downloaders should be scanned automatically
        /// </summary>
        private Dictionary<ThirdPartyId, Dictionary<int, Type>> _downloaderTypes = new()
        {
            [ThirdPartyId.Bilibili] = new Dictionary<int, Type>
            {
                [(int) BilibiliDownloadTaskType.Favorites] = SpecificTypeUtils<BilibiliDownloader>.Type
            },
            [ThirdPartyId.ExHentai] = new Dictionary<int, Type>
            {
                [(int) ExHentaiDownloadTaskType.SingleWork] = SpecificTypeUtils<ExHentaiSingleWorkDownloader>.Type,
                [(int) ExHentaiDownloadTaskType.List] = SpecificTypeUtils<ExHentaiListDownloader>.Type,
                [(int) ExHentaiDownloadTaskType.Watched] = SpecificTypeUtils<ExHentaiWatchedDownloader>.Type,
            },
            [ThirdPartyId.Pixiv] = new Dictionary<int, Type>
            {
                [(int) PixivDownloadTaskType.Search] = SpecificTypeUtils<PixivSearchDownloader>.Type,
                [(int) PixivDownloadTaskType.Ranking] = SpecificTypeUtils<PixivRankingDownloader>.Type,
                [(int) PixivDownloadTaskType.Following] = SpecificTypeUtils<PixivFollowingDownloader>.Type,
            }
        };

        private T GetNewScopeRequiredService<T>() =>
            _serviceProvider.CreateAsyncScope().ServiceProvider.GetRequiredService<T>();

        private readonly ILogger<DownloaderManager> _logger;

        public IDictionary<int, IDownloader> Downloaders => new Dictionary<int, IDownloader>(_downloaders);

        public DownloaderManager(IServiceProvider serviceProvider, IStringLocalizer<SharedResource> localizer,
            IEnumerable<IDownloaderOptionsValidator> validators, ILogger<DownloaderManager> logger, InsideWorldLocalizer insideWorldLocalizer)
        {
            _serviceProvider = serviceProvider;
            _localizer = localizer;
            _logger = logger;
            _insideWorldLocalizer = insideWorldLocalizer;
            _validators = validators.ToDictionary(a => a.ThirdPartyId, a => a);

            OnStatusChanged += (taskId, downloader) =>
                GetNewScopeRequiredService<DownloadTaskService>().OnStatusChanged(taskId, downloader, null);
            OnNameAcquired += (taskId, name) =>
                GetNewScopeRequiredService<DownloadTaskService>().OnNameAcquired(taskId, name);
            OnProgress += (taskId, progress) =>
                GetNewScopeRequiredService<DownloadTaskService>().OnProgress(taskId, progress);
            OnCurrentChanged += (taskId) =>
                GetNewScopeRequiredService<DownloadTaskService>().OnCurrentChanged(taskId);
            OnCheckpointReached += (taskId, checkpoint) =>
                GetNewScopeRequiredService<DownloadTaskService>().OnCheckpointReached(taskId, checkpoint);
        }

        [NotNull] public event Func<int, IDownloader, Task> OnStatusChanged;
        [NotNull] public event Func<int, string, Task> OnNameAcquired;
        [NotNull] public event Func<int, decimal, Task> OnProgress;
        [NotNull] public event Func<int, Task> OnCurrentChanged;
        [NotNull] public event Func<int, string, Task> OnCheckpointReached;

        public IDownloader? this[int taskId] => _downloaders.GetValueOrDefault(taskId);

        public async Task Stop(int taskId, DownloaderStopBy stopBy)
        {
            var downloader = this[taskId];
            if (downloader is {Status: DownloaderStatus.Downloading})
            {
                _logger.LogInformation($"[TaskId:{taskId}]Trying to stop...");
                await downloader.Stop(stopBy);
                _logger.LogInformation($"[TaskId:{taskId}]Downloader has been stopped.");
            }
        }

        private async Task<BaseResponse> _tryStart(DownloadTask task, bool stopConflicts)
        {
            if (!_validators.TryGetValue(task.ThirdPartyId, out var optionsValidator))
            {
                return BaseResponseBuilder.BuildBadRequest(
                    _localizer[SharedResource.Downloader_DownloaderOptionsValidatorNotRegistered,
                        (int) task.ThirdPartyId,
                        task.ThirdPartyId]);
            }

            var validation = await optionsValidator.Validate();
            if (validation.Code != 0)
            {
                return BaseResponseBuilder.BuildBadRequest(validation.Message);
            }

            var activeConflictDownloaders = _downloaders.Where(a => a.Key != task.Id)
                .Where(a => a.Value.ThirdPartyId == task.ThirdPartyId && a.Value.IsOccupyingDownloadTaskSource())
                .ToDictionary(a => a.Key, a => a.Value);

            if (activeConflictDownloaders.Any())
            {
                if (stopConflicts)
                {
                    foreach (var (key, dd) in activeConflictDownloaders)
                    {
                        await dd.Stop(DownloaderStopBy.AppendToTheQueue);
                    }
                }
                else
                {
                    await using var scope = _serviceProvider.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<DownloadTaskService>();
                    var occupiedTasks = await service.GetByKeys(activeConflictDownloaders.Keys);
                    var message = _localizer[SharedResource.Downloader_DownloaderCountExceeded, task.ThirdPartyId,
                        $"{Environment.NewLine}{string.Join(Environment.NewLine, occupiedTasks.Select(a => a.DisplayName))}"];
                    var fullMessage = _insideWorldLocalizer.Downloader_FailedToStart(task.DisplayName, message);
                    return BaseResponseBuilder.Build(ResponseCode.Conflict, fullMessage);
                }
            }

            if (!_downloaders.TryGetValue(task.Id, out var downloader))
            {
                Type? type = null;
                if (_downloaderTypes.TryGetValue(task.ThirdPartyId, out var types))
                {
                    types.TryGetValue(task.Type, out type);
                }

                if (type == null)
                {
                    return BaseResponseBuilder.BuildBadRequest(
                        _localizer[SharedResource.Downloader_DownloaderIsNotFound, (int) task.ThirdPartyId, task.Type]);
                }

                downloader = (_serviceProvider.GetRequiredService(type) as IDownloader)!;
                downloader.OnStatusChanged += () => OnStatusChanged(task.Id, downloader);
                downloader.OnNameAcquired += name => OnNameAcquired(task.Id, name);
                downloader.OnProgress += progress => OnProgress(task.Id, progress);
                downloader.OnCurrentChanged += () => OnCurrentChanged(task.Id);
                downloader.OnCheckpointChanged += checkpoint => OnCheckpointReached(task.Id, checkpoint);

                _downloaders[task.Id] = downloader;
            }

            if (downloader.Status is DownloaderStatus.Downloading or DownloaderStatus.Starting)
            {
                return BaseResponseBuilder.Ok;
            }

            await downloader.Start(task);

            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> Start(DownloadTask task, bool stopConflicts)
        {
            return await _tryStart(task, stopConflicts);
        }
    }
}