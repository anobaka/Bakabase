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
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components
{
    public sealed class DownloaderManager : ITransientTorrentVerdictCache
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<int, IDownloader> _downloaders = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _downloadBTaskCompletionSources = new();

        /// <summary>
        /// In-memory verdicts for ExHentai torrent-priority: task ids known (during the current run)
        /// to have no torrent. Used by the scheduler to deprioritize them and by the downloader to
        /// stop deferring them. Transient by design — it is rebuilt by re-probing after a restart.
        /// </summary>
        private readonly ConcurrentDictionary<int, byte> _noTorrentTaskIds = new();
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

        }

        /// <summary>
        /// Fans a status change out to everything that cares. Each concern is isolated: these used to
        /// be multicast event handlers, where the first one to throw — a service that could not be
        /// resolved during shutdown, a task row deleted mid-download — aborted the rest of the chain.
        /// One of those later handlers is the *only* thing that advances the download queue, so a
        /// single swallowed exception left every remaining task of that source stuck forever.
        /// Releasing the BTask comes first: it must happen even if persisting the change fails.
        /// </summary>
        private async Task HandleStatusChanged(int taskId, IDownloader downloader)
        {
            var status = downloader.Status;

            if (status is DownloaderStatus.Complete or DownloaderStatus.Failed or DownloaderStatus.Stopped)
            {
                Guard(() => CompleteBTask(taskId), "release the background task");
            }

            // Drop the torrent-priority verdict once the task truly ends (success / failure /
            // manual stop) so a later restart re-probes it. A Defer / AppendToTheQueue stop is a
            // requeue, not an end, so the verdict must survive it — otherwise we would re-defer
            // forever.
            if (status is DownloaderStatus.Complete or DownloaderStatus.Failed ||
                (status == DownloaderStatus.Stopped && downloader.StoppedBy == DownloaderStopBy.ManuallyStop))
            {
                Guard(() => ClearNoTorrent(taskId), "clear the torrent verdict");
            }

            await GuardAsync(
                () => GetNewScopeRequiredService<DownloadTaskService>().OnStatusChanged(taskId, downloader, null),
                "persist the status change");
        }

        private Task HandleNameAcquired(int taskId, string name) => GuardAsync(
            () => GetNewScopeRequiredService<DownloadTaskService>().OnNameAcquired(taskId, name),
            "persist the acquired name");

        private async Task HandleProgress(int taskId, decimal progress)
        {
            await GuardAsync(() => GetNewScopeRequiredService<DownloadTaskService>().OnProgress(taskId, progress),
                "persist progress");
            await GuardAsync(() => UpdateBTaskProgress(taskId, progress), "update background task progress");
        }

        private async Task HandleCurrentChanged(int taskId)
        {
            await GuardAsync(() => GetNewScopeRequiredService<DownloadTaskService>().OnCurrentChanged(taskId),
                "push the current step");
            await GuardAsync(() => UpdateBTaskProcess(taskId), "update background task process");
        }

        private Task HandleCheckpointReached(int taskId, string checkpoint) => GuardAsync(
            () => GetNewScopeRequiredService<DownloadTaskService>().OnCheckpointReached(taskId, checkpoint),
            "persist the checkpoint");

        private T GetNewScopeRequiredService<T>() where T : notnull =>
            _serviceProvider.CreateAsyncScope().ServiceProvider.GetRequiredService<T>();

        private void Guard(Action action, string what)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to {What} while handling a downloader event", what);
            }
        }

        private async Task GuardAsync(Func<Task> action, string what)
        {
            try
            {
                await action();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to {What} while handling a downloader event", what);
            }
        }

        public IDownloader? this[int taskId] => _downloaders.GetValueOrDefault(taskId);

        /// <summary>Record that a task has been probed and has no torrent (ExHentai torrent-priority).</summary>
        public void MarkNoTorrent(int taskId) => _noTorrentTaskIds[taskId] = 0;

        /// <summary>
        /// Records the no-torrent verdict in memory and on the task itself, so it outlives both the
        /// current run and a restart. Best-effort: failing to persist must not break the download.
        /// </summary>
        public async Task MarkNoTorrentAsync(int taskId)
        {
            MarkNoTorrent(taskId);

            try
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<DownloadTaskService>();
                await service.RecordNoTorrentVerdict(taskId, DateTime.Now);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to persist the no-torrent verdict for task {TaskId}", taskId);
            }
        }

        /// <summary>
        /// Records that a torrent was found for this task. Best-effort, like its negative counterpart:
        /// this is what the task list reads to say whether a gallery has a torrent at all, but failing
        /// to write it must never break the download that just succeeded.
        /// </summary>
        public async Task MarkTorrentFoundAsync(int taskId)
        {
            ClearNoTorrent(taskId);

            try
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<DownloadTaskService>();
                await service.RecordTorrentFoundVerdict(taskId, DateTime.Now);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to persist the torrent-found verdict for task {TaskId}", taskId);
            }
        }

        /// <summary>Whether a task is already known (this run) to have no torrent.</summary>
        public bool IsKnownNoTorrent(int taskId) => _noTorrentTaskIds.ContainsKey(taskId);

        /// <summary>Forget a task's no-torrent verdict so it is re-probed next time it runs.</summary>
        public void ClearNoTorrent(int taskId) => _noTorrentTaskIds.TryRemove(taskId, out _);

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
            var validation = await helper.ValidateOptionsAsync();
            if (!validation.IsSuccess())
            {
                return validation;
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
                        $"{Environment.NewLine}{string.Join(Environment.NewLine, occupiedTasks.Select(a => a.Name ?? a.Key))}"];
                    var fullMessage = _downloaderLocalizer["FailedToStart", task.ThirdPartyId, task.Name ?? task.Key,
                        message];
                    return BaseResponseBuilder.Build(ResponseCode.Conflict, fullMessage);
                }
            }

            if (!_downloaders.TryGetValue(task.Id, out var downloader))
            {
                downloader = _downloaderFactory.GetDownloader(task.ThirdPartyId, task.Type);
                downloader.OnStatusChanged += () => HandleStatusChanged(task.Id, downloader);
                downloader.OnNameAcquired += name => HandleNameAcquired(task.Id, name);
                downloader.OnProgress += progress => HandleProgress(task.Id, progress);
                downloader.OnCurrentChanged += () => HandleCurrentChanged(task.Id);
                downloader.OnCheckpointChanged += checkpoint => HandleCheckpointReached(task.Id, checkpoint);

                _downloaders[task.Id] = downloader;
            }

            if (downloader.Status is DownloaderStatus.Downloading or DownloaderStatus.Starting)
            {
                return BaseResponseBuilder.Ok;
            }

            if (!await downloader.Start(task))
            {
                // Start refuses while the downloader is mid-transition (Stopping). Reporting Ok here
                // would have the scheduler count this task as started and stop looking for work, so
                // report a conflict instead — the caller already knows to move on to the next task.
                _logger.LogInformation(
                    "[TaskId:{TaskId}] Start was refused because the downloader is {Status}", task.Id,
                    downloader.Status);
                return BaseResponseBuilder.Build(ResponseCode.Conflict,
                    $"The downloader of task {task.DisplayName} is {downloader.Status}.");
            }

            await EnsureBTaskExists(task);

            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> Start(DownloadTask task, bool stopConflicts)
        {
            return await _tryStart(task, stopConflicts);
        }

        /// <summary>
        /// Force-releases downloaders that claim to be busy but have shown no sign of life for
        /// <paramref name="stallThreshold"/>. A downloader stuck in Downloading / Starting / Stopping
        /// occupies its source's only slot and makes every later start attempt a silent no-op, so
        /// without this the queue stays frozen until the user starts everything by hand.
        /// </summary>
        /// <returns>The ids of the tasks that were released.</returns>
        public async Task<IReadOnlyList<int>> ReleaseStalledDownloaders(TimeSpan stallThreshold)
        {
            var now = DateTime.Now;
            var stalled = _downloaders
                .Where(a => a.Value.IsOccupyingDownloadTaskSource() &&
                            now - a.Value.LastActivityAt > stallThreshold)
                .ToArray();

            var released = new List<int>();

            foreach (var (taskId, downloader) in stalled)
            {
                _logger.LogWarning(
                    "[TaskId:{TaskId}] No activity since {LastActivityAt} while {Status}; releasing it so the queue can move on",
                    taskId, downloader.LastActivityAt, downloader.Status);

                try
                {
                    // AppendToTheQueue, not ManuallyStop: the user did not ask for this, so the task
                    // must stay eligible and get picked up again rather than looking disabled.
                    await downloader.Stop(DownloaderStopBy.AppendToTheQueue);
                    released.Add(taskId);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "[TaskId:{TaskId}] Failed to release a stalled downloader", taskId);
                }
                finally
                {
                    // Whatever happened above, the background task must not outlive the download it
                    // was mirroring, or the app refuses to consider itself idle.
                    CompleteBTask(taskId);
                }
            }

            return released;
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
                await _bTaskManager.Enqueue(BTaskBuilder.Create(btaskId)
                    .Named(() => task.DisplayName)
                    .OfType(BTaskType.Download)
                    .StartImmediately()
                    .IgnoreIfExists()
                    .Run(async args =>
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
                    }));
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