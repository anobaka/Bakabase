using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Business.Workflow;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Office.Excel;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using DownloadTask = Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.DownloadTask;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Services
{
    /// <summary>
    /// todo: extract interface
    /// </summary>
    public class DownloadTaskService : ResourceService<BakabaseDbContext, DownloadTaskDbModel, int>
    {
        protected DownloaderManager DownloaderManager => GetRequiredService<DownloaderManager>();

        protected DownloadRecordService DownloadRecordService => GetRequiredService<DownloadRecordService>();

        protected IHubContext<WebGuiHub, IWebGuiClient> UiHub =>
            GetRequiredService<IHubContext<WebGuiHub, IWebGuiClient>>();

        private BakabaseLocalizer _localizer;
        private readonly IWorkflowEventBus _workflowBus;

        public DownloadTaskService(IServiceProvider serviceProvider, BakabaseLocalizer localizer,
            IWorkflowEventBus workflowBus) : base(
            serviceProvider)
        {
            _localizer = localizer;
            _workflowBus = workflowBus;
        }

        public async Task<DownloadTask> GetDto(int id)
        {
            var task = await GetByKey(id);
            return (await ToDto(new[] {task}))[0];
        }

        private async Task<DownloadTask[]> ToDto(IEnumerable<DownloadTaskDbModel> tasks)
        {
            var dtos = tasks.Select(task => task.ToDomainModel(DownloaderManager)!).ToArray();
            if (dtos.Length == 0)
            {
                return dtos;
            }

            // Multiple tasks may share one output directory. Query only files explicitly
            // attributed to each task, with one grouped query for the whole list.
            var ids = dtos.Select(x => x.Id).ToArray();
            var sizes = await GetRequiredService<BakabaseDbContext>().DownloadTaskFiles.AsNoTracking()
                .Where(x => ids.Contains(x.DownloadTaskId))
                .GroupBy(x => x.DownloadTaskId)
                .Select(g => new {TaskId = g.Key, Size = g.Sum(x => x.Size)})
                .ToDictionaryAsync(x => x.TaskId, x => x.Size);
            foreach (var dto in dtos)
            {
                if (sizes.TryGetValue(dto.Id, out var bytes))
                {
                    dto.DownloadedBytes = bytes;
                }
            }

            return dtos;
        }

        protected async Task OnChange(int taskId, object value, Func<DownloadTask, object> getter,
            Action<DownloadTask, object> setter)
        {
            try
            {
                var task = (await GetByKey(taskId)).ToDomainModel(DownloaderManager)!;
                // Equals, not !=: both sides are `object`, so != compares references. Boxed
                // decimals and strings rebuilt from the database are never reference-equal,
                // which made this guard always true — every progress tick wrote to the
                // database and broadcast over SignalR even when the value had not moved.
                if (!Equals(getter(task), value))
                {
                    setter(task, value);
                    // Logger.LogInformation(
                    //     $"Use new value: {value} to update download task to: {JsonConvert.SerializeObject(task)}");
                    var dbModel = task.ToDbModel()!;
                    await Update(dbModel);
                    await UiHub.Clients.All.GetIncrementalData(nameof(DownloadTask),
                        (await ToDto(new[] {dbModel}))[0]);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    $"An error occurred during handling task change events: {ex.Message}. Current service instance: {GetHashCode()}.");
            }
        }

        /// <param name="targeted">
        /// True when the user picked these tasks by hand rather than starting everything. Picking a
        /// task out is an explicit "run this one", so the pre-check's skip is suppressed for it — it
        /// is the way back for someone who deleted a downloaded file and wants it fetched again.
        /// </param>
        public async Task<BaseResponse> Start(Expression<Func<DownloadTaskDbModel, bool>>? exp = null,
            DownloadTaskActionOnConflict actionOnConflict = DownloadTaskActionOnConflict.Ignore,
            bool targeted = false)
            => await StartCore(exp, actionOnConflict, targeted, taskActionLockHeld: false);

        private async Task<BaseResponse> StartCore(Expression<Func<DownloadTaskDbModel, bool>>? exp,
            DownloadTaskActionOnConflict actionOnConflict, bool targeted, bool taskActionLockHeld)
        {
            var tasks = await GetAll(exp);
            var badStatusTasks = tasks.Where(a => a.Status is DownloadTaskDbModelStatus.Disabled or DownloadTaskDbModelStatus.Failed)
                .ToArray();
            foreach (var badStatusTask in badStatusTasks)
            {
                async Task<bool> EnableTask()
                {
                    await base.UpdateByKey(badStatusTask.Id, current =>
                    {
                        if (current.Status is DownloadTaskDbModelStatus.Disabled or DownloadTaskDbModelStatus.Failed)
                            current.Status = DownloadTaskDbModelStatus.InProgress;
                    });
                    return true;
                }
                if (taskActionLockHeld) await EnableTask();
                else await DownloaderManager.WithTaskActionLock(badStatusTask.Id, EnableTask);
            }

            var rsp = await TryStartAllTasksCore(DownloadTaskStartMode.ManualStart, tasks.Select(a => a.Id).ToArray(),
                actionOnConflict, skipSatisfiedTasks: !targeted, taskActionLockHeld);

            PushAllDataToUi();

            return rsp;
        }

        public async Task Stop(Expression<Func<DownloadTaskDbModel, bool>>? exp = null)
        {
            var tasks = await GetAll(exp);
            var notDisabledTasks = tasks.Where(a => a.Status != DownloadTaskDbModelStatus.Disabled).ToArray();
            foreach (var t in notDisabledTasks)
            {
                t.Status = DownloadTaskDbModelStatus.Disabled;
            }

            await UpdateRange(notDisabledTasks);
            var notDisabledTaskIds = notDisabledTasks.Select(a => a.Id).ToArray();
            var activeIds = notDisabledTaskIds.Where(a => DownloaderManager[a]?.Status == DownloaderStatus.Downloading)
                .ToList();
            foreach (var a in activeIds)
            {
                await DownloaderManager.Stop(a, DownloaderStopBy.ManuallyStop);
            }

            PushAllDataToUi();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="taskId"></param>
        /// <param name="downloader"></param>
        /// <param name="extraData">todo: strong-typed</param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public async Task OnStatusChanged(int taskId, IDownloader downloader, object? extraData)
        {
            DownloadTaskDbModelStatus? newStatus = null;
            switch (downloader.Status)
            {
                case DownloaderStatus.JustCreated:
                case DownloaderStatus.Starting:
                case DownloaderStatus.Downloading:
                case DownloaderStatus.Stopping:
                    break;
                case DownloaderStatus.Stopped:
                {
                    // A missing reason used to throw here, and this handler is the only thing that
                    // moves the queue on — so one unexplained stop froze every remaining task of that
                    // source. Treat it as a requeue, the safest reading: the task stays eligible.
                    newStatus = downloader.StoppedBy switch
                    {
                        DownloaderStopBy.ManuallyStop => DownloadTaskDbModelStatus.Disabled,
                        _ => DownloadTaskDbModelStatus.InProgress
                    };

                    break;
                }
                case DownloaderStatus.Complete:
                    newStatus = DownloadTaskDbModelStatus.Complete;
                    break;
                case DownloaderStatus.Failed:
                    newStatus = DownloadTaskDbModelStatus.Failed;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var task = await GetByKey(taskId);
            if (task == null)
            {
                // The task row was deleted while its downloader was still winding down. Nothing left
                // to persist, but the queue still has to be told to move on — this used to throw and
                // take the rest of the handler (and with it the whole source's queue) down with it.
                RequestSchedulingPass();
                return;
            }

            if (newStatus.HasValue)
            {
                task.Status = newStatus.Value;
                task.DownloadStatusUpdateDt = DateTime.Now;
                task.Message = downloader.Message;

                if (newStatus == DownloadTaskDbModelStatus.Complete)
                {
                    if (downloader.Checkpoint.IsNotEmpty())
                    {
                        task.Checkpoint = downloader.Checkpoint;
                    }
                }

                await base.Update(task);

                // Permanently remember that this (ThirdPartyId, Key) has been downloaded.
                // Kept in a dedicated table so it survives deletion of the task itself.
                // Best-effort: a recording failure must never break the completion flow.
                if (newStatus == DownloadTaskDbModelStatus.Complete)
                {
                    try
                    {
                        await DownloadRecordService.Record(task.ThirdPartyId, task.Key);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex,
                            $"Failed to persist download record for task {task.Id} ({task.ThirdPartyId}/{task.Key})");
                    }
                }

                // Any downloader reaching a resting state frees its source's single slot, so the queue
                // has to be looked at again. This used to name only some of those states — a stop
                // that merely requeued the task left the queue idle with nothing scheduled to notice
                // — and it ran the next pass inline, nesting a pass inside the completion that
                // triggered it. Both are now the pump's job.
                if (downloader.Status is DownloaderStatus.Complete or DownloaderStatus.Failed
                    or DownloaderStatus.Stopped)
                {
                    RequestSchedulingPass();
                }

                // Fire the workflow trigger after persistence. Same pattern as
                // SubscriptionService: notifications run on their own track; workflows are
                // additive and only fire for definitions whose filter matches this payload.
                if (newStatus == DownloadTaskDbModelStatus.Complete)
                {
                    await _workflowBus.PublishAsync(
                        DownloaderWorkflowKinds.TriggerCompleted,
                        new DownloaderCompletedPayload
                        {
                            TaskId = task.Id,
                            ThirdPartyId = (int) task.ThirdPartyId,
                            Type = task.Type,
                            Key = task.Key ?? "",
                            Name = task.Name,
                            DownloadPath = task.DownloadPath,
                        });
                }
            }

            await UiHub.Clients.All.GetIncrementalData(nameof(DownloadTask),
                (await ToDto(new[] {task})).FirstOrDefault()!);
        }

        /// <summary>
        /// Stamps a task as probed-and-torrentless. Persisted on the task so a later run can skip the
        /// probe entirely, unlike the manager's in-memory verdict which is dropped once a task ends.
        /// </summary>
        public Task RecordNoTorrentVerdict(int taskId, DateTime checkedAt) =>
            RecordTorrentVerdict(taskId, checkedAt, found: false);

        /// <summary>
        /// Stamps a task as probed-and-torrent-bearing, so the UI can say so and a later probe is not
        /// the only way to find out again.
        /// </summary>
        public Task RecordTorrentFoundVerdict(int taskId, DateTime checkedAt) =>
            RecordTorrentVerdict(taskId, checkedAt, found: true);

        /// <summary>
        /// Stamps a task's torrent as downloaded. This — and not
        /// <see cref="RecordTorrentFoundVerdict"/> — is what the scheduler's pre-check reads to skip
        /// a finished task, so it may only ever be written after the file is actually on disk.
        /// </summary>
        public Task RecordTorrentDownloadedVerdict(int taskId, DateTime downloadedAt) =>
            UpdateExHentaiOptions(taskId, options =>
            {
                if (options.TorrentDownloadedAt == downloadedAt)
                {
                    return false;
                }

                options.TorrentDownloadedAt = downloadedAt;
                return true;
            });

        private Task RecordTorrentVerdict(int taskId, DateTime checkedAt, bool found) =>
            UpdateExHentaiOptions(taskId, options =>
            {
                // The two verdicts are mutually exclusive: a gallery that now has a torrent must not
                // keep a no-torrent stamp that would send it to the back of the queue and skip its
                // probe.
                var noTorrentCheckedAt = found ? null : (DateTime?) checkedAt;
                var torrentFoundAt = found ? (DateTime?) checkedAt : null;

                // A gallery that turns out to have no torrent cannot also have downloaded one, and a
                // stale stamp here would make the pre-check skip it forever.
                var torrentDownloadedAt = found ? options.TorrentDownloadedAt : null;

                if (options.NoTorrentCheckedAt == noTorrentCheckedAt &&
                    options.TorrentFoundAt == torrentFoundAt &&
                    options.TorrentDownloadedAt == torrentDownloadedAt)
                {
                    return false;
                }

                options.NoTorrentCheckedAt = noTorrentCheckedAt;
                options.TorrentFoundAt = torrentFoundAt;
                options.TorrentDownloadedAt = torrentDownloadedAt;
                return true;
            });

        /// <summary>
        /// Reads a task's ExHentai options, lets <paramref name="mutate"/> change them, and persists
        /// only if it reports a real change — these run on the download path, where a redundant write
        /// also costs a database round trip and a UI broadcast.
        /// </summary>
        private async Task UpdateExHentaiOptions(int taskId, Func<ExHentaiTaskOptions, bool> mutate)
        {
            var task = await GetByKey(taskId);
            if (task == null)
            {
                return;
            }

            var domain = task.ToDomainModel(DownloaderManager)!;
            var options = domain.GetTypedOptions<ExHentaiTaskOptions>();

            if (!mutate(options))
            {
                return;
            }

            domain.SetTypedOptions(options);
            task.Options = domain.Options;

            await Update(task);
            InvalidatePrecheck();
        }

        /// <param name="skipSatisfiedTasks">
        /// Whether a pre-check may complete a task outright instead of running it. False only when
        /// the user hand-picked the tasks, where "start this" has to mean it.
        /// </param>
        public async Task<BaseResponse> TryStartAllTasks(DownloadTaskStartMode mode, int[]? ids,
            DownloadTaskActionOnConflict actionOnConflict, bool skipSatisfiedTasks = true)
            => await TryStartAllTasksCore(mode, ids, actionOnConflict, skipSatisfiedTasks, taskActionLockHeld: false);

        private async Task<BaseResponse> TryStartAllTasksCore(DownloadTaskStartMode mode, int[]? ids,
            DownloadTaskActionOnConflict actionOnConflict, bool skipSatisfiedTasks, bool taskActionLockHeld)
        {
            var tasks = (await (ids == null ? GetAll() : GetByKeys(ids))).ToDictionary(a => a.ToDomainModel(DownloaderManager),
                a => a);
            var targetTasks = tasks.Keys
                .Where(a =>
                {
                    return mode switch
                    {
                        DownloadTaskStartMode.AutoStart => a.AvailableActions.Contains(DownloadTaskAction
                            .StartAutomatically),
                        DownloadTaskStartMode.ManualStart => a.CanStart,
                        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
                    };
                }).ToArray();

            // One bulk pass answers the source-specific questions for the whole queue. Doing it per
            // task meant paying a start/stop lifecycle — and usually a network round-trip — just to
            // discover a task had nothing to do, which is the entire cost of re-running a large,
            // mostly-finished queue.
            var verdicts = await GetRequiredService<DownloadTaskPrecheckRunner>().EvaluateAsync(targetTasks);

            var satisfied = skipSatisfiedTasks
                ? targetTasks
                    .Where(t => verdicts.TryGetValue(t.Id, out var v) &&
                                v.Outcome == DownloadTaskPrecheckOutcome.AlreadySatisfied)
                    .ToArray()
                : [];

            if (satisfied.Length > 0)
            {
                var completed = await CompleteWithoutDownloading(satisfied);
                targetTasks = targetTasks.Except(completed).ToArray();
            }

            var filteredTasks = targetTasks.GroupBy(a => a.ThirdPartyId)
                .Select(g =>
                    // Deferred tasks sink to the back so they only start once there is nothing the
                    // source considers more valuable left (for ExHentai: tasks that may still yield a
                    // torrent). Stable FIFO (by id) within each tier.
                    g.OrderBy(t => verdicts.TryGetValue(t.Id, out var v) &&
                                   v.Outcome == DownloadTaskPrecheckOutcome.Defer
                        ? 1
                        : 0)
                        .ThenBy(t => t.Id)
                        .First())
                .ToArray();
            var startedTasks = new List<DownloadTask>();
            BaseResponse? firstFailure = null;

            foreach (var tt in filteredTasks)
            {
                var rsp = taskActionLockHeld
                    ? await DownloaderManager.StartUnderTaskActionLock(tt,
                        actionOnConflict == DownloadTaskActionOnConflict.StopOthers)
                    : await DownloaderManager.Start(tt,
                        actionOnConflict == DownloadTaskActionOnConflict.StopOthers,
                        startAutomatically: mode == DownloadTaskStartMode.AutoStart);

                if (rsp.Code != (int) ResponseCode.Success)
                {
                    if (rsp.Code == (int) ResponseCode.Conflict)
                    {
                        if (actionOnConflict == DownloadTaskActionOnConflict.Ignore)
                        {
                            continue;
                        }

                        return rsp;
                    }

                    // A task that cannot even start — expired cookie, missing download path,
                    // any other rejected configuration — used to abort the whole pass without
                    // writing anything down. Nothing was persisted and no downloader was ever
                    // created, so the task list looked exactly as it did before the click.
                    // Record the reason on the task instead and carry on, so the failure is
                    // visible and the queue is seen moving to the next task.
                    if (tasks.TryGetValue(tt, out var dbModel))
                    {
                        await MarkAsFailedToStart(dbModel, rsp.Message, taskActionLockHeld);
                    }

                    firstFailure ??= rsp;
                    continue;
                }

                startedTasks.Add(tt);
            }

            // set other tasks status
            var pendingTasks = targetTasks.Except(startedTasks).ToList();
            foreach (var ot in pendingTasks)
            {
                Task<bool> ResetIdleDownloader()
                {
                    var dd = DownloaderManager[ot.Id];
                    if (dd?.IsOccupyingDownloadTaskSource() == false) dd.ResetStatus();
                    return Task.FromResult(true);
                }
                if (taskActionLockHeld) await ResetIdleDownloader();
                else await DownloaderManager.WithTaskActionLock(ot.Id, ResetIdleDownloader);
            }

            // Surfaced so a manual start still reports why it could not run; an automatic
            // pass discards it, having already recorded the failure on the task itself.
            return firstFailure ?? BaseResponseBuilder.Ok;
        }

        /// <summary>
        /// Completes tasks a pre-check proved have nothing left to download, without ever creating a
        /// downloader for them.
        ///
        /// The outcome is the same one the downloader would have reached — it starts, sees the file
        /// already on disk and returns — so the same things have to happen: the download record, the
        /// completion workflow trigger. What is skipped is the cost of getting there: a downloader, a
        /// background task, a status round-trip and a rate-limited network request per task. That is
        /// what made re-running a large finished queue take hours.
        ///
        /// Rechecked under each task's action gate so a queued pre-check cannot complete a task
        /// whose download mode was just changed. The UI is refreshed once for the batch.
        /// </summary>
        private async Task<DownloadTask[]> CompleteWithoutDownloading(IReadOnlyList<DownloadTask> satisfied)
        {
            var now = DateTime.Now;
            var updated = new List<DownloadTaskDbModel>(satisfied.Count);

            foreach (var task in satisfied)
            {
                await DownloaderManager.WithTaskActionLock(task.Id, async () =>
                {
                    var current = await GetRequiredService<BakabaseDbContext>().DownloadTasks.AsNoTracking()
                        .SingleOrDefaultAsync(t => t.Id == task.Id);
                    if (current == null || current.Options != task.Options || current.Checkpoint != task.Checkpoint ||
                        current.Key != task.Key || current.Type != task.Type ||
                        DownloaderManager[current.Id]?.IsOccupyingDownloadTaskSource() == true)
                        return false;
                    var currentDto = current.ToDomainModel(DownloaderManager)!;
                    var verdicts = await GetRequiredService<DownloadTaskPrecheckRunner>().EvaluateAsync([currentDto]);
                    if (!verdicts.TryGetValue(task.Id, out var verdict) ||
                        verdict.Outcome != DownloadTaskPrecheckOutcome.AlreadySatisfied)
                        return false;
                    var saved = await base.UpdateByKey(task.Id, t =>
                    {
                        t.Status = DownloadTaskDbModelStatus.Complete;
                        t.DownloadStatusUpdateDt = now;
                        t.Progress = 100;
                        t.Message = null;
                    });
                    if (saved.Data != null) updated.Add(saved.Data);
                    DownloaderManager.Forget(task.Id);
                    return true;
                });
            }

            if (updated.Count == 0)
            {
                return [];
            }

            Logger.LogInformation(
                "Completing {Count} download task(s) without starting them: a pre-check found nothing left to download",
                updated.Count);

            foreach (var dbModel in updated)
            {
                // Best-effort, exactly as on the normal completion path: a recording or trigger
                // failure must not stop the rest of the batch from being completed.
                try
                {
                    await DownloadRecordService.Record(dbModel.ThirdPartyId, dbModel.Key);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex,
                        $"Failed to persist download record for task {dbModel.Id} ({dbModel.ThirdPartyId}/{dbModel.Key})");
                }

                try
                {
                    await _workflowBus.PublishAsync(
                        DownloaderWorkflowKinds.TriggerCompleted,
                        new DownloaderCompletedPayload
                        {
                            TaskId = dbModel.Id,
                            ThirdPartyId = (int) dbModel.ThirdPartyId,
                            Type = dbModel.Type,
                            Key = dbModel.Key ?? "",
                            Name = dbModel.Name,
                            DownloadPath = dbModel.DownloadPath,
                        });
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Failed to publish the completion event for task {dbModel.Id}");
                }
            }

            PushAllDataToUi();
            var completedIds = updated.Select(t => t.Id).ToHashSet();
            return satisfied.Where(t => completedIds.Contains(t.Id)).ToArray();
        }

        /// <summary>
        /// Persists a start-time rejection (invalid cookie, bad configuration, ...) onto the task
        /// and pushes it, so the reason reaches the UI even when no downloader was ever created.
        /// </summary>
        private async Task MarkAsFailedToStart(DownloadTaskDbModel task, string? message, bool taskActionLockHeld)
        {
            async Task<bool> PersistFailure()
            {
                var current = await GetByKey(task.Id);
                if (current == null || current.Options != task.Options ||
                    DownloaderManager[task.Id]?.IsOccupyingDownloadTaskSource() == true) return false;
                var saved = await base.UpdateByKey(task.Id, t =>
                {
                    t.Status = DownloadTaskDbModelStatus.Failed;
                    t.Message = message;
                    t.DownloadStatusUpdateDt = DateTime.Now;
                });
                DownloaderManager.Forget(task.Id);
                if (saved.Data != null)
                    await UiHub.Clients.All.GetIncrementalData(nameof(DownloadTask),
                        (await ToDto(new[] {saved.Data})).FirstOrDefault()!);
                return true;
            }
            if (taskActionLockHeld) await PersistFailure();
            else await DownloaderManager.WithTaskActionLock(task.Id, PersistFailure);
        }

        public async Task OnNameAcquired(int taskId, string name) =>
            await OnChange(taskId, name, t => t.Name, (t, s) => { t.Name = (string) s; });

        public async Task OnCheckpointReached(int taskId, string checkpoint) =>
            await OnChange(taskId, checkpoint, t => t.Checkpoint, (t, s) => { t.Checkpoint = (string) s; });

        public async Task OnProgress(int taskId, decimal progress) => await OnChange(taskId, progress, t => t.Progress,
            (t, s) => { t.Progress = (decimal) s; });

        public async Task OnCurrentChanged(int taskId) =>
            await UiHub.Clients.All.GetIncrementalData(nameof(DownloadTask), await GetDto(taskId));

        /// <summary>Push transient speed without persisting a progress value every second.</summary>
        public async Task OnDownloadSpeedChanged(int taskId)
        {
            var task = await GetByKey(taskId);
            if (task != null)
            {
                await UiHub.Clients.All.GetIncrementalData(nameof(DownloadTask),
                    (await ToDto(new[] {task}))[0]);
            }
        }

        /// <summary>
        /// Upsert the size of one task-owned output file. A retry, resumed download, or overwrite
        /// updates that path instead of counting its bytes twice.
        /// </summary>
        public async Task RecordDownloadedFile(int taskId, string path, long size)
        {
            var fullPath = Path.GetFullPath(path);
            // Windows paths are case-insensitive, while SQLite's composite key is not.
            // The key is only used for accounting; no filesystem access uses its casing.
            if (OperatingSystem.IsWindows())
            {
                fullPath = fullPath.ToUpperInvariant();
            }

            var db = GetRequiredService<BakabaseDbContext>();
            var existing = await db.DownloadTaskFiles.FindAsync(taskId, fullPath);
            if (existing?.Size == size)
            {
                return;
            }

            if (existing == null)
            {
                db.DownloadTaskFiles.Add(new DownloadTaskFileDbModel
                {
                    DownloadTaskId = taskId, Path = fullPath, Size = size
                });
            }
            else
            {
                existing.Size = size;
            }

            await db.SaveChangesAsync();
            await UiHub.Clients.All.GetIncrementalData(nameof(DownloadTask), await GetDto(taskId));
        }

        public async Task OnCheckpointChanged(int taskId, string checkpoint) => await OnChange(taskId, checkpoint,
            t => t.Checkpoint,
            (t, s) => { t.Checkpoint = s?.ToString(); });

        public async Task<DownloadTask[]> GetAllDto()
        {
            var tasks = await GetAll();
            return await ToDto(tasks);
        }

        /// <summary>
        /// Tell, for each of <paramref name="keys"/>, whether the item is in the download list
        /// right now (and under which task ids) and when it was last downloaded. Keys the
        /// backend has never heard of are omitted from the result.
        /// </summary>
        public async Task<List<DownloadTaskKeyStatus>> QueryKeyStatuses(ThirdPartyId thirdPartyId,
            IReadOnlyCollection<string>? keys)
        {
            if (keys == null || keys.Count == 0)
            {
                return [];
            }

            var keySet = keys.Where(k => !string.IsNullOrEmpty(k)).ToHashSet();
            if (keySet.Count == 0)
            {
                return [];
            }

            var downloadedAtMap = (await DownloadRecordService.Query(thirdPartyId, keySet))
                .ToDictionary(r => r.Key, r => r.DownloadedAt);
            var tasksByKey = (await GetAll(t => t.ThirdPartyId == thirdPartyId))
                .Where(t => keySet.Contains(t.Key))
                .GroupBy(t => t.Key)
                .ToDictionary(g => g.Key, g => g.ToArray());

            var result = new List<DownloadTaskKeyStatus>();
            foreach (var key in keySet)
            {
                var hasRecord = downloadedAtMap.TryGetValue(key, out var downloadedAt);
                tasksByKey.TryGetValue(key, out var tasks);
                if (!hasRecord && (tasks == null || tasks.Length == 0))
                {
                    continue;
                }

                result.Add(new DownloadTaskKeyStatus
                {
                    Key = key,
                    TaskIds = tasks?.Select(t => t.Id).ToArray() ?? [],
                    Status = tasks is {Length: > 0}
                        ? tasks.MinBy(t => StatusActivenessOrder(t.Status))!.Status
                        : null,
                    DownloadedAt = hasRecord ? downloadedAt : null
                });
            }

            return result;
        }

        /// <summary>
        /// Ranks statuses so that the one the user most likely cares about wins when a key has
        /// several tasks: something still running, then something that needs attention, then
        /// what is already done.
        /// </summary>
        private static int StatusActivenessOrder(DownloadTaskDbModelStatus status) => status switch
        {
            DownloadTaskDbModelStatus.InProgress => 0,
            DownloadTaskDbModelStatus.Failed => 1,
            DownloadTaskDbModelStatus.Disabled => 2,
            DownloadTaskDbModelStatus.Complete => 3,
            _ => 4
        };

        // public async Task<BaseResponse> Start(int id)
        // {
        //     var task = await GetByKey(id);
        //     if (task.Status != DownloadTaskStatus.InProgress)
        //     {
        //         await base.UpdateByKey(id, t => t.Status = DownloadTaskStatus.InProgress);
        //         PushAllDataToUi();
        //     }
        //
        //     var rsp = await DownloaderManager.Start(task);
        //     var inQueue = rsp.Code is (int) ResponseCode.Conflict;
        //     if (inQueue || rsp.Code == (int) ResponseCode.Success)
        //     {
        //         PushAllDataToUi();
        //         return rsp;
        //     }
        //
        //     return rsp;
        // }
        //
        // public async Task Stop(int id)
        // {
        //     if (DownloaderManager.Downloaders.TryGetValue(id, out var downloader))
        //     {
        //         if (downloader.Status == DownloaderStatus.Downloading)
        //         {
        //             await DownloaderManager.Stop(id);
        //             return;
        //         }
        //     }
        //
        //     await UpdateByKey(id, a => a.Status = DownloadTaskStatus.Disabled);
        //     PushAllDataToUi();
        // }

        protected void PushAllDataToUi()
        {
            Task.Run(async () =>
            {
                await using var scope = ServiceProvider.CreateAsyncScope();
                var tasks = await scope.ServiceProvider.GetRequiredService<DownloadTaskService>().GetAllDto();
                var uiHub = scope.ServiceProvider.GetRequiredService<IHubContext<WebGuiHub, IWebGuiClient>>();
                await uiHub.Clients.All.GetData(nameof(DownloadTask), tasks);
            });
        }

        public async Task<SingletonResponse<DownloadTaskDbModel>> StopAndUpdateByKey(int id, Action<DownloadTaskDbModel> modify)
        {
            await DownloaderManager.Stop(id, DownloaderStopBy.ManuallyStop);
            var rsp = await base.UpdateByKey(id, modify);
            // The task's options (incl. PreferTorrent) may have changed, so drop any stale torrent
            // verdict and let it be re-probed on the next run.
            DownloaderManager.ClearNoTorrent(id);
            InvalidatePrecheck();
            PushAllDataToUi();
            return rsp;
        }

        /// <summary>
        /// Switches one ExHentai task to images and starts it explicitly. List/watched range
        /// checkpoints are reset so galleries previously completed through torrents are revisited;
        /// their existing image results and files remain available for reuse.
        /// </summary>
        public Task<BaseResponse> DirectDownload(int id,
            DownloadTaskActionOnConflict actionOnConflict = DownloadTaskActionOnConflict.NotSet) =>
            DownloaderManager.WithTaskActionLock(id, async () =>
            {
                var task = await GetByKey(id);
                if (task == null)
                    return BaseResponseBuilder.Build(ResponseCode.NotFound, $"Download task {id} was not found.");
                if (task.ThirdPartyId != ThirdPartyId.ExHentai ||
                    !Enum.IsDefined(typeof(ExHentaiDownloadTaskType), task.Type))
                    return BaseResponseBuilder.BuildBadRequest("Direct image download requires an ExHentai task.");

                JsonObject options;
                try
                {
                    options = string.IsNullOrWhiteSpace(task.Options)
                        ? new JsonObject()
                        : JsonNode.Parse(task.Options) as JsonObject
                          ?? throw new JsonException("Task options must be a JSON object.");
                }
                catch (JsonException e)
                {
                    return BaseResponseBuilder.BuildBadRequest($"Could not read task options: {e.Message}");
                }

                // Keep it out of the automatic queue while the old run drains and its last
                // checkpoint/status callbacks persist. No new run may reuse its cached options.
                await base.UpdateByKey(id, t => t.Status = DownloadTaskDbModelStatus.Disabled);
                await DownloaderManager.StopAndWait(id, DownloaderStopBy.ManuallyStop);

                // Re-read after draining, so the old run's final metadata is retained as well.
                task = await GetByKey(id);
                if (task == null)
                    return BaseResponseBuilder.Build(ResponseCode.NotFound, $"Download task {id} was not found.");
                options = string.IsNullOrWhiteSpace(task.Options)
                    ? new JsonObject()
                    : JsonNode.Parse(task.Options) as JsonObject ?? options;
                foreach (var key in options.Select(x => x.Key)
                             .Where(x => string.Equals(x, "preferTorrent", StringComparison.OrdinalIgnoreCase)).ToArray())
                    options.Remove(key);
                options["preferTorrent"] = false;
                await base.UpdateByKey(id, t =>
                {
                    t.Options = options.ToJsonString();
                    if (t.Type != (int) ExHentaiDownloadTaskType.SingleWork) t.Checkpoint = null;
                });
                DownloaderManager.ClearNoTorrent(id);
                InvalidatePrecheck();
                return await StartCore(t => t.Id == id, actionOnConflict, targeted: true, taskActionLockHeld: true);
            });

        /// <summary>
        /// Asks for another look at the queue, without waiting for it. Never call
        /// <see cref="TryStartAllTasks"/> directly from a downloader event: that runs the next
        /// scheduling pass inside the completion of the previous task, which nests without bound
        /// when a queue drains quickly.
        /// </summary>
        private void RequestSchedulingPass()
        {
            try
            {
                GetRequiredService<DownloadQueuePump>().Request();
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Failed to request a download scheduling pass");
            }
        }

        /// <summary>
        /// Drops every cached pre-check snapshot. Anything that changes what a pre-check reads — task
        /// options, the set of tasks, a written verdict — has to call this, or the queue keeps acting
        /// on an answer that is no longer true.
        /// </summary>
        private void InvalidatePrecheck()
        {
            try
            {
                GetRequiredService<DownloadTaskPrecheckRunner>().Invalidate();
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Failed to invalidate the download pre-check cache");
            }
        }

        public async Task<ListResponse<DownloadTask>> AddRange(IEnumerable<DownloadTask> resources)
        {
            var arr = resources.ToArray();
            var dbModels = arr.Select(r => r.ToDbModel()!).ToArray();
            var rsp = await base.AddRange(dbModels);
            for (var i = 0; i < arr.Length; i++)
            {
                arr[i].Id = rsp.Data[i].Id;
            }

            // Permanently remember that these (ThirdPartyId, Key) items have been requested for
            // download. Recorded at creation so the warning shows immediately (even while the task
            // is still queued / downloading); the timestamp is refreshed when the task completes.
            // Best-effort: a recording failure must never break task creation.
            foreach (var t in arr)
            {
                try
                {
                    await DownloadRecordService.Record(t.ThirdPartyId, t.Key);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex,
                        $"Failed to persist download record for task {t.Id} ({t.ThirdPartyId}/{t.Key})");
                }
            }

            InvalidatePrecheck();
            PushAllDataToUi();
            return new ListResponse<DownloadTask>(arr);
        }

        public async Task<BaseResponse> Delete(DownloadTaskDeleteInputModel model)
        {
            var ids = new List<int>(model.Ids ?? []);
            if (model.ThirdPartyId.HasValue)
            {
                var allIdsInThirdParty =
                    (await GetAll(x => x.ThirdPartyId == model.ThirdPartyId.Value)).Select(x => x.Id);
                ids = ids.Intersect(allIdsInThirdParty).ToList();
            }

            await Stop(t => ids.Contains(t.Id));
            await RemoveByKeys(ids);

            InvalidatePrecheck();
            PushAllDataToUi();
            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> ClearCheckpoints(Expression<Func<DownloadTaskDbModel, bool>>? exp = null)
        {
            var tasks = await GetAll(exp);
            foreach (var t in tasks)
            {
                t.Checkpoint = null;
            }
            await UpdateRange(tasks);
            PushAllDataToUi();
            return BaseResponseBuilder.Ok;
        }

        public async Task<byte[]> Export()
        {
            var tasks = await GetAllDto();
            var lines = new List<SimpleColumn[]>
            {
                new[] {nameof(DownloadTask.Key), nameof(DownloadTask.DisplayName), nameof(DownloadTask.Status)}
                    .Select(c => new SimpleColumn(c)).ToArray()
            };
            foreach (var task in tasks)
            {
                lines.Add(new[] {task.Key, task.DisplayName, task.Status.ToString()}.Select(c => new SimpleColumn(c))
                    .ToArray());
            }

            var bytes = ExcelUtils.CreateExcel(new ExcelData(lines));
            return bytes;
        }
    }
}
