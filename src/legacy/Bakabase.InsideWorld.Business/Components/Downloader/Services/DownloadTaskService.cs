using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
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
        private readonly IGuiAdapter _guiAdapter;
        private readonly IWorkflowEventBus _workflowBus;

        public DownloadTaskService(IServiceProvider serviceProvider, BakabaseLocalizer localizer,
            IGuiAdapter guiAdapter, IWorkflowEventBus workflowBus) : base(
            serviceProvider)
        {
            _localizer = localizer;
            _guiAdapter = guiAdapter;
            _workflowBus = workflowBus;
        }

        public async Task<DownloadTask> GetDto(int id)
        {
            var task = await GetByKey(id);
            return ToDto(new[] {task})[0];
        }

        private DownloadTask[] ToDto(IEnumerable<DownloadTaskDbModel> tasks)
        {
            return tasks.Select(task => task.ToDomainModel(DownloaderManager)!).ToArray();
        }

        protected async Task OnChange(int taskId, object value, Func<DownloadTask, object> getter,
            Action<DownloadTask, object> setter)
        {
            try
            {
                var task = (await GetByKey(taskId)).ToDomainModel(DownloaderManager)!;
                if (getter(task) != value)
                {
                    setter(task, value);
                    // Logger.LogInformation(
                    //     $"Use new value: {value} to update download task to: {JsonConvert.SerializeObject(task)}");
                    var dbModel = task.ToDbModel()!;
                    await Update(dbModel);
                    await UiHub.Clients.All.GetIncrementalData(nameof(DownloadTask), task);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    $"An error occurred during handling task change events: {ex.Message}. Current service instance: {GetHashCode()}.");
            }
        }

        public async Task<BaseResponse> Start(Expression<Func<DownloadTaskDbModel, bool>>? exp = null,
            DownloadTaskActionOnConflict actionOnConflict = DownloadTaskActionOnConflict.Ignore)
        {
            var tasks = await GetAll(exp);
            var badStatusTasks = tasks.Where(a => a.Status is DownloadTaskDbModelStatus.Disabled or DownloadTaskDbModelStatus.Failed)
                .ToArray();
            foreach (var badStatusTask in badStatusTasks)
            {
                badStatusTask.Status = DownloadTaskDbModelStatus.InProgress;
            }

            await UpdateRange(badStatusTasks);
            var rsp = await TryStartAllTasks(DownloadTaskStartMode.ManualStart, tasks.Select(a => a.Id).ToArray(),
                actionOnConflict);

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
                    switch (downloader.StoppedBy!.Value)
                    {
                        case DownloaderStopBy.ManuallyStop:
                            newStatus = DownloadTaskDbModelStatus.Disabled;
                            break;
                        case DownloaderStopBy.AppendToTheQueue:
                        case DownloaderStopBy.Defer:
                            newStatus = DownloadTaskDbModelStatus.InProgress;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

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

                // A Defer keeps the task eligible (InProgress) but, unlike other requeues, must kick
                // the scheduler so the next task is picked immediately rather than waiting for the
                // periodic trigger.
                var deferred = downloader is
                    { Status: DownloaderStatus.Stopped, StoppedBy: DownloaderStopBy.Defer };
                if (newStatus is DownloadTaskDbModelStatus.Complete or DownloadTaskDbModelStatus.Failed
                        or DownloadTaskDbModelStatus.Disabled || deferred)
                {
                    await TryStartAllTasks(DownloadTaskStartMode.AutoStart, null, DownloadTaskActionOnConflict.Ignore);
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
                ToDto(new[] {task}).FirstOrDefault()!);
        }

        // True when an ExHentai task will end up downloading images (so under torrent-priority it should
        // run after torrent-bearing tasks): it has been probed and has no torrent, or it opts out of
        // torrents (PreferTorrent off), in which case we honor that and download its images.
        private bool IsExHentaiImageOnly(DownloadTask task) =>
            DownloaderManager.IsKnownNoTorrent(task.Id) ||
            !task.GetTypedOptions<ExHentaiTaskOptions>().PreferTorrent;

        public async Task<BaseResponse> TryStartAllTasks(DownloadTaskStartMode mode, int[]? ids,
            DownloadTaskActionOnConflict actionOnConflict)
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

            var prioritizeExTorrent = GetRequiredService<IBOptionsManager<ExHentaiOptions>>().Value
                .PrioritizeTasksWithTorrent;
            var filteredTasks = targetTasks.GroupBy(a => a.ThirdPartyId)
                .Select(g =>
                {
                    // ExHentai torrent-priority: probe torrent-preferring, un-probed tasks first. Tasks
                    // that will end up downloading images — already probed with no torrent, or opting
                    // out of torrents — sink to the back, so they only start once there is nothing
                    // torrent-bearing left. Stable FIFO (by id) within each tier.
                    if (prioritizeExTorrent && g.Key == ThirdPartyId.ExHentai)
                    {
                        return g.OrderBy(t => IsExHentaiImageOnly(t) ? 1 : 0)
                            .ThenBy(t => t.Id)
                            .First();
                    }

                    return g.FirstOrDefault()!;
                })
                .ToArray();
            var startedTasks = new List<DownloadTask>();

            foreach (var tt in filteredTasks)
            {
                var rsp = await DownloaderManager.Start(tt,
                    actionOnConflict == DownloadTaskActionOnConflict.StopOthers);

                if (rsp.Code != (int) ResponseCode.Success)
                {
                    if (rsp.Code == (int) ResponseCode.Conflict)
                    {
                        if (actionOnConflict == DownloadTaskActionOnConflict.Ignore)
                        {
                            continue;
                        }
                    }

                    return rsp;
                }

                startedTasks.Add(tt);
            }

            // set other tasks status
            var pendingTasks = targetTasks.Except(startedTasks).ToList();
            foreach (var ot in pendingTasks)
            {
                var dd = DownloaderManager[ot.Id];
                dd?.ResetStatus();
            }

            return BaseResponseBuilder.Ok;
        }

        public async Task OnNameAcquired(int taskId, string name) =>
            await OnChange(taskId, name, t => t.Name, (t, s) => { t.Name = (string) s; });

        public async Task OnCheckpointReached(int taskId, string checkpoint) =>
            await OnChange(taskId, checkpoint, t => t.Checkpoint, (t, s) => { t.Checkpoint = (string) s; });

        public async Task OnProgress(int taskId, decimal progress) => await OnChange(taskId, progress, t => t.Progress,
            (t, s) => { t.Progress = (decimal) s; });

        public async Task OnCurrentChanged(int taskId) =>
            await UiHub.Clients.All.GetIncrementalData(nameof(DownloadTask), await GetDto(taskId));

        public async Task OnCheckpointChanged(int taskId, string checkpoint) => await OnChange(taskId, checkpoint,
            t => t.Checkpoint,
            (t, s) => { t.Checkpoint = s?.ToString(); });

        public async Task<DownloadTask[]> GetAllDto()
        {
            var tasks = await GetAll();
            return ToDto(tasks);
        }

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
            PushAllDataToUi();
            return rsp;
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