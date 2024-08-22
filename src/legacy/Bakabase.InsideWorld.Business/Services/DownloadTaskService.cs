using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Downloader;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NPOI.SS.Formula.Functions;

namespace Bakabase.InsideWorld.Business.Services
{
    public class DownloadTaskService : ResourceService<InsideWorldDbContext, DownloadTask, int>
    {
        protected DownloaderManager DownloaderManager => GetRequiredService<DownloaderManager>();

        protected IHubContext<WebGuiHub, IWebGuiClient> UiHub =>
            GetRequiredService<IHubContext<WebGuiHub, IWebGuiClient>>();

        private InsideWorldLocalizer _localizer;

        public DownloadTaskService(IServiceProvider serviceProvider, InsideWorldLocalizer localizer) : base(
            serviceProvider)
        {
            _localizer = localizer;
        }

        public async Task<DownloadTaskDto> GetDto(int id)
        {
            var task = await GetByKey(id);
            return ToDto(new[] {task})[0];
        }

        private DownloadTaskDto[] ToDto(IEnumerable<DownloadTask> tasks)
        {
            return tasks.Select(task => task.ToDto(DownloaderManager)).ToArray();
        }

        protected async Task OnChange(int taskId, object value, Func<DownloadTask, object> getter,
            Action<DownloadTask, object> setter)
        {
            try
            {
                var task = await GetByKey(taskId);
                if (getter(task) != value)
                {
                    setter(task, value);
                    // Logger.LogInformation(
                    //     $"Use new value: {value} to update download task to: {JsonConvert.SerializeObject(task)}");
                    await Update(task);
                    await UiHub.Clients.All.GetIncrementalData(nameof(DownloadTask),
                        ToDto(new[] {task}).FirstOrDefault()!);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    $"An error occurred during handling task change events: {ex.Message}. Current service instance: {this.GetHashCode()}.");
            }
        }

        public async Task<BaseResponse> Start(Expression<Func<DownloadTask, bool>>? exp = null, DownloadTaskActionOnConflict actionOnConflict = DownloadTaskActionOnConflict.Ignore)
        {
            var tasks = await GetAll(exp);
            var badStatusTasks = tasks.Where(a => a.Status is DownloadTaskStatus.Disabled or DownloadTaskStatus.Failed).ToArray();
            foreach (var badStatusTask in badStatusTasks)
            {
                badStatusTask.Status = DownloadTaskStatus.InProgress;
            }

            await UpdateRange(badStatusTasks);
            var rsp = await TryStartAllTasks(DownloadTaskStartMode.ManualStart, tasks.Select(a => a.Id).ToArray(), actionOnConflict);

            PushAllDataToUi();

            return rsp;
        }

        public async Task Stop(Expression<Func<DownloadTask, bool>>? exp = null)
        {
            var tasks = await GetAll(exp);
            var notDisabledTasks = tasks.Where(a => a.Status != DownloadTaskStatus.Disabled).ToArray();
            foreach (var t in notDisabledTasks)
            {
                t.Status = DownloadTaskStatus.Disabled;
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
            DownloadTaskStatus? newStatus = null;
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
                            newStatus = DownloadTaskStatus.Disabled;
                            break;
                        case DownloaderStopBy.AppendToTheQueue:
                            newStatus = DownloadTaskStatus.InProgress;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    break;
                }
                case DownloaderStatus.Complete:
                    newStatus = DownloadTaskStatus.Complete;
                    break;
                case DownloaderStatus.Failed:
                    newStatus = DownloadTaskStatus.Failed;
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

                if (newStatus == DownloadTaskStatus.Complete)
                {
                    if (downloader.Checkpoint.IsNotEmpty())
                    {
                        task.Checkpoint = downloader.Checkpoint;
                    }
                }

                await base.Update(task);
                if (newStatus is DownloadTaskStatus.Complete or DownloadTaskStatus.Failed
                    or DownloadTaskStatus.Disabled)
                {
                    await TryStartAllTasks(DownloadTaskStartMode.AutoStart, null, DownloadTaskActionOnConflict.Ignore);
                }
            }

            await UiHub.Clients.All.GetIncrementalData(nameof(DownloadTask),
                ToDto(new[] {task}).FirstOrDefault()!);
        }

        public async Task<BaseResponse> TryStartAllTasks(DownloadTaskStartMode mode, int[]? ids, DownloadTaskActionOnConflict actionOnConflict)
        {
            var tasks = (await (ids == null ? GetAll() : GetByKeys(ids))).ToDictionary(a => a.ToDto(DownloaderManager),
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

            var filteredTasks = targetTasks.GroupBy(a => a.ThirdPartyId).Select(a => a.FirstOrDefault()!).ToArray();
            var startedTasks = new List<DownloadTaskDto>();

            foreach (var tt in filteredTasks)
            {
                var rsp = await DownloaderManager.Start(tasks[tt],
                    actionOnConflict == DownloadTaskActionOnConflict.StopOthers);

                if (rsp.Code != (int) ResponseCode.Success)
                {
                    if (rsp.Code == (int)ResponseCode.Conflict)
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

        public async Task<DownloadTaskDto[]> GetAllDto()
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

        public async Task<SingletonResponse<DownloadTask>> StopAndUpdateByKey(int id, Action<DownloadTask> modify)
        {
            await DownloaderManager.Stop(id, DownloaderStopBy.ManuallyStop);
            var rsp = await base.UpdateByKey(id, modify);
            PushAllDataToUi();
            return rsp;
        }

        public override Task<ListResponse<DownloadTask>> AddRange(IEnumerable<DownloadTask> resources)
        {
            var rsp = base.AddRange(resources);
            PushAllDataToUi();
            return rsp;
        }

        public override Task<BaseResponse> RemoveByKey(int key)
        {
            var rsp = base.RemoveByKey(key);
            PushAllDataToUi();
            return rsp;
        }
    }
}