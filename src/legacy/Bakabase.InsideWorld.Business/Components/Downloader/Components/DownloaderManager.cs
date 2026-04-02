using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components
{
    public sealed class DownloaderManager
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<int, IDownloader> _downloaders = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _downloadBTaskCompletionSources = new();
        private readonly IStringLocalizer<SharedResource> _localizer;
        private readonly IDownloaderLocalizer _downloaderLocalizer;
        private readonly IDownloaderFactory _downloaderFactory;
        private readonly BTaskManager _bTaskManager;

        private readonly ILogger<DownloaderManager> _logger;

        public IDictionary<int, IDownloader> Downloaders => new Dictionary<int, IDownloader>(_downloaders);

        public DownloaderManager(IServiceProvider serviceProvider, IStringLocalizer<SharedResource> localizer,
            ILogger<DownloaderManager> logger, IDownloaderLocalizer downloaderLocalizer,
            IDownloaderFactory downloaderFactory, BTaskManager bTaskManager)
        {
            _serviceProvider = serviceProvider;
            _localizer = localizer;
            _logger = logger;
            _downloaderLocalizer = downloaderLocalizer;
            _downloaderFactory = downloaderFactory;
            _bTaskManager = bTaskManager;

            OnStatusChanged += (taskId, downloader) =>
                GetNewScopeRequiredService<DownloadTaskService>().OnStatusChanged(taskId, downloader, null);
            OnStatusChanged += (taskId, downloader) =>
            {
                if (downloader.Status is DownloaderStatus.Complete or DownloaderStatus.Failed
                    or DownloaderStatus.Stopped)
                {
                    CompleteBTask(taskId);
                }

                return Task.CompletedTask;
            };
            OnNameAcquired += (taskId, name) =>
                GetNewScopeRequiredService<DownloadTaskService>().OnNameAcquired(taskId, name);
            OnProgress += (taskId, progress) =>
                GetNewScopeRequiredService<DownloadTaskService>().OnProgress(taskId, progress);
            OnProgress += (taskId, progress) => UpdateBTaskProgress(taskId, progress);
            OnCurrentChanged += (taskId) =>
                GetNewScopeRequiredService<DownloadTaskService>().OnCurrentChanged(taskId);
            OnCurrentChanged += (taskId) => UpdateBTaskProcess(taskId);
            OnCheckpointReached += (taskId, checkpoint) =>
                GetNewScopeRequiredService<DownloadTaskService>().OnCheckpointReached(taskId, checkpoint);
            return;

            T GetNewScopeRequiredService<T>() =>
                _serviceProvider.CreateAsyncScope().ServiceProvider.GetRequiredService<T>();
        }

        public event Func<int, IDownloader, Task> OnStatusChanged;
        public event Func<int, string, Task> OnNameAcquired;
        public event Func<int, decimal, Task> OnProgress;
        public event Func<int, Task> OnCurrentChanged;
        public event Func<int, string, Task> OnCheckpointReached;

        public IDownloader? this[int taskId] => _downloaders.GetValueOrDefault(taskId);

        public async Task Stop(int taskId, DownloaderStopBy stopBy)
        {
            var downloader = this[taskId];
            if (downloader is { Status: DownloaderStatus.Downloading })
            {
                _logger.LogInformation($"[TaskId:{taskId}]Trying to stop...");
                await downloader.Stop(stopBy);
                _logger.LogInformation($"[TaskId:{taskId}]Downloader has been stopped.");
            }
        }

        private async Task<BaseResponse> _tryStart(DownloadTask task, bool stopConflicts)
        {
            var helper = _downloaderFactory.GetHelper(task.ThirdPartyId, task.Type);
            await helper.ValidateOptionsAsync();

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
                        $"{Environment.NewLine}{string.Join(Environment.NewLine, occupiedTasks.Select(a => a.Name ?? a.Key))}"];
                    var fullMessage = _downloaderLocalizer["FailedToStart", task.ThirdPartyId, task.Name ?? task.Key,
                        message];
                    return BaseResponseBuilder.Build(ResponseCode.Conflict, fullMessage);
                }
            }

            if (!_downloaders.TryGetValue(task.Id, out var downloader))
            {
                downloader = _downloaderFactory.GetDownloader(task.ThirdPartyId, task.Type);
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

            await EnsureBTaskExists(task);

            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> Start(DownloadTask task, bool stopConflicts)
        {
            return await _tryStart(task, stopConflicts);
        }

        private static string GetBTaskId(int downloadTaskId) => $"DownloadTask:{downloadTaskId}";

        private async Task EnsureBTaskExists(DownloadTask task)
        {
            var btaskId = GetBTaskId(task.Id);
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_downloadBTaskCompletionSources.TryAdd(task.Id, tcs))
            {
                // BTask already exists for this download
                return;
            }

            try
            {
                await _bTaskManager.Enqueue(new BTaskHandlerBuilder
                {
                    Id = btaskId,
                    GetName = () => task.DisplayName,
                    IsPersistent = false,
                    Type = BTaskType.Download,
                    ResourceType = BTaskResourceType.Any,
                    StartNow = true,
                    DuplicateIdHandling = BTaskDuplicateIdHandling.Ignore,
                    Run = async args =>
                    {
                        try
                        {
                            await tcs.Task.WaitAsync(args.CancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            tcs.TrySetCanceled();
                            throw;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _downloadBTaskCompletionSources.TryRemove(task.Id, out _);
                _logger.LogError(ex, $"Failed to create BTask for download task {task.Id}");
            }
        }

        private void CompleteBTask(int downloadTaskId)
        {
            if (_downloadBTaskCompletionSources.TryRemove(downloadTaskId, out var tcs))
            {
                tcs.TrySetResult();
            }
        }

        private async Task UpdateBTaskProgress(int downloadTaskId, decimal progress)
        {
            var btaskId = GetBTaskId(downloadTaskId);
            var handler = _bTaskManager.Tasks.FirstOrDefault(t => t.Id == btaskId);
            if (handler != null)
            {
                await handler.UpdateTask(t => t.Percentage = (int)progress);
            }
        }

        private async Task UpdateBTaskProcess(int downloadTaskId)
        {
            var btaskId = GetBTaskId(downloadTaskId);
            var handler = _bTaskManager.Tasks.FirstOrDefault(t => t.Id == btaskId);
            if (handler != null)
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<DownloadTaskService>();
                var task = await service.GetDto(downloadTaskId);
                await handler.UpdateTask(t => t.Process = task.Current);
            }
        }
    }
}