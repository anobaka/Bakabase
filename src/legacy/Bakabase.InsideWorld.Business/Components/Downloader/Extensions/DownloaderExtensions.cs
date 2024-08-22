﻿using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Downloader.Naming;
using Bootstrap.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Extensions
{
    public static class DownloaderExtensions
    {
        public static bool IsOccupyingDownloadTaskSource(this IDownloader downloader) =>
            downloader.Status is DownloaderStatus.Downloading or DownloaderStatus.Starting
                or DownloaderStatus.Stopping;

        public static DownloadTaskDto ToDto(this DownloadTask task, DownloaderManager downloaderManager)
        {
            if (task == null)
            {
                return null;
            }

            var downloader = downloaderManager[task.Id];

            var allDownloaders = downloaderManager.Downloaders;

            DownloadTaskDtoStatus status;
            if (downloader == null)
            {
                status = task.Status switch
                {
                    DownloadTaskStatus.InProgress => allDownloaders.Values.Any(a =>
                        a.ThirdPartyId == task.ThirdPartyId && a.IsOccupyingDownloadTaskSource())
                        ? DownloadTaskDtoStatus.InQueue
                        : DownloadTaskDtoStatus.Idle,
                    DownloadTaskStatus.Disabled => DownloadTaskDtoStatus.Disabled,
                    DownloadTaskStatus.Complete => DownloadTaskDtoStatus.Complete,
                    DownloadTaskStatus.Failed => DownloadTaskDtoStatus.Failed,
                    _ => throw new ArgumentOutOfRangeException()
                };
            }
            else
            {
                if (task.Status == DownloadTaskStatus.Disabled)
                {
                    status = DownloadTaskDtoStatus.Disabled;
                }
                else
                {
                    status = downloader.Status switch
                    {
                        DownloaderStatus.JustCreated => allDownloaders.Any(a =>
                            a.Key != task.Id && a.Value.ThirdPartyId == task.ThirdPartyId &&
                            a.Value.IsOccupyingDownloadTaskSource())
                            ? DownloadTaskDtoStatus.InQueue
                            : DownloadTaskDtoStatus.Idle,
                        DownloaderStatus.Starting => DownloadTaskDtoStatus.Starting,
                        DownloaderStatus.Downloading => DownloadTaskDtoStatus.Downloading,
                        DownloaderStatus.Complete => DownloadTaskDtoStatus.Complete,
                        DownloaderStatus.Failed => DownloadTaskDtoStatus.Failed,
                        DownloaderStatus.Stopping => DownloadTaskDtoStatus.Stopping,
                        DownloaderStatus.Stopped => DownloadTaskDtoStatus.Disabled,
                        _ => throw new ArgumentOutOfRangeException()
                    };
                    // Same as JustCreated
                    if (downloader is {Status: DownloaderStatus.Stopped, StoppedBy: DownloaderStopBy.AppendToTheQueue})
                    {
                        status = allDownloaders.Any(a =>
                            a.Key != task.Id && a.Value.ThirdPartyId == task.ThirdPartyId &&
                            a.Value.IsOccupyingDownloadTaskSource())
                            ? DownloadTaskDtoStatus.InQueue
                            : DownloadTaskDtoStatus.Idle;
                    }
                }
            }

            var actions = DownloaderUtils.AvailableActions[status].ToHashSet();
            DateTime? nextStartDt = null;

            switch (status)
            {
                case DownloadTaskDtoStatus.Idle:
                case DownloadTaskDtoStatus.Complete:
                {
                    if (task.Interval.HasValue)
                    {
                        nextStartDt = task.DownloadStatusUpdateDt.AddSeconds(task.Interval.Value);
                    }

                    break;
                }
                case DownloadTaskDtoStatus.Failed:
                {
                    if (downloader?.FailureTimes > 0)
                    {
                        if (!DownloaderUtils.IntervalsOnContinuousFailures.TryGetValue(downloader.FailureTimes,
                                out var ts))
                        {
                            ts = DownloaderUtils.IntervalsOnContinuousFailures.Values.Max();
                        }

                        nextStartDt = task.DownloadStatusUpdateDt.Add(ts);
                    }

                    break;
                }
                case DownloadTaskDtoStatus.InQueue:
                case DownloadTaskDtoStatus.Starting:
                case DownloadTaskDtoStatus.Downloading:
                case DownloadTaskDtoStatus.Disabled:
                case DownloadTaskDtoStatus.Stopping:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (actions.Contains(DownloadTaskAction.Restart) || actions.Contains(DownloadTaskAction.StartManually))
            {
                if (status != DownloadTaskDtoStatus.Disabled)
                {
                    if (nextStartDt.HasValue && DateTime.Now > nextStartDt.Value)
                    {
                        actions.Add(DownloadTaskAction.StartAutomatically);
                    }
                }
            }

            var dto = new DownloadTaskDto
            {
                Id = task.Id,
                DownloadStatusUpdateDt = task.DownloadStatusUpdateDt,
                StartPage = task.StartPage,
                EndPage = task.EndPage,
                Interval = task.Interval,
                Key = task.Key,
                Message = task.Message,
                Name = task.Name,
                Checkpoint = task.Checkpoint,
                Progress = task.Progress,
                ThirdPartyId = task.ThirdPartyId,
                Type = task.Type,
                Status = status,
                DownloadPath = task.DownloadPath,
                Current = downloader?.Current,
                FailureTimes = downloader?.FailureTimes ?? 0,
                NextStartDt = nextStartDt,
                AvailableActions = actions
            };

            return dto;
        }
    }
}