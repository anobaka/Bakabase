using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bootstrap.Components.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Extensions
{
    public static class DownloaderExtensions
    {
        public static bool IsOccupyingDownloadTaskSource(this IDownloader downloader) =>
            downloader.Status is DownloaderStatus.Downloading or DownloaderStatus.Starting
                or DownloaderStatus.Stopping;

        public static IServiceCollection AddDownloaders(this IServiceCollection services)
        {
            foreach (var downloaderDefinition in DownloaderInternals.Definitions)
            {
                services.AddScoped(downloaderDefinition.DownloaderType);
                services.AddSingleton(downloaderDefinition.HelperType);
            }

            services.RegisterAllRegisteredTypeAs<IDownloader>();
            services.RegisterAllRegisteredTypeAs<IDownloaderHelper>();

            services.AddScoped<DownloadTaskService>();
            services.AddSingleton<DownloaderManager>();
            services.AddTransient<IDownloaderLocalizer, DownloaderLocalizer>();
            services.AddSingleton<IDownloaderFactory, DownloaderFactory>();

            return services;
        }

        public static DownloadTask? ToDomainModel(this DownloadTaskDbModel? task, DownloaderManager downloaderManager)
        {
            if (task == null)
            {
                return null;
            }

            var downloader = downloaderManager[task.Id];

            var allDownloaders = downloaderManager.Downloaders;

            DownloadTaskStatus status;
            if (downloader == null)
            {
                status = task.Status switch
                {
                    DownloadTaskDbModelStatus.InProgress => allDownloaders.Values.Any(a =>
                        a.ThirdPartyId == task.ThirdPartyId && a.IsOccupyingDownloadTaskSource())
                        ? DownloadTaskStatus.InQueue
                        : DownloadTaskStatus.Idle,
                    DownloadTaskDbModelStatus.Disabled => DownloadTaskStatus.Disabled,
                    DownloadTaskDbModelStatus.Complete => DownloadTaskStatus.Complete,
                    DownloadTaskDbModelStatus.Failed => DownloadTaskStatus.Failed,
                    _ => throw new ArgumentOutOfRangeException()
                };
            }
            else
            {
                if (task.Status == DownloadTaskDbModelStatus.Disabled)
                {
                    status = DownloadTaskStatus.Disabled;
                }
                else
                {
                    status = downloader.Status switch
                    {
                        DownloaderStatus.JustCreated => allDownloaders.Any(a =>
                            a.Key != task.Id && a.Value.ThirdPartyId == task.ThirdPartyId &&
                            a.Value.IsOccupyingDownloadTaskSource())
                            ? DownloadTaskStatus.InQueue
                            : DownloadTaskStatus.Idle,
                        DownloaderStatus.Starting => DownloadTaskStatus.Starting,
                        DownloaderStatus.Downloading => DownloadTaskStatus.Downloading,
                        DownloaderStatus.Complete => DownloadTaskStatus.Complete,
                        DownloaderStatus.Failed => DownloadTaskStatus.Failed,
                        DownloaderStatus.Stopping => DownloadTaskStatus.Stopping,
                        DownloaderStatus.Stopped => DownloadTaskStatus.Disabled,
                        _ => throw new ArgumentOutOfRangeException()
                    };
                    // Same as JustCreated
                    if (downloader is
                        { Status: DownloaderStatus.Stopped, StoppedBy: DownloaderStopBy.AppendToTheQueue })
                    {
                        status = allDownloaders.Any(a =>
                            a.Key != task.Id && a.Value.ThirdPartyId == task.ThirdPartyId &&
                            a.Value.IsOccupyingDownloadTaskSource())
                            ? DownloadTaskStatus.InQueue
                            : DownloadTaskStatus.Idle;
                    }
                }
            }

            var actions = DownloaderUtils.AvailableActions[status].ToHashSet();
            DateTime? nextStartDt = null;

            switch (status)
            {
                case DownloadTaskStatus.Idle:
                case DownloadTaskStatus.Complete:
                {
                    if (task.Interval.HasValue)
                    {
                        nextStartDt = task.DownloadStatusUpdateDt.AddSeconds(task.Interval.Value);
                    }

                    break;
                }
                case DownloadTaskStatus.Failed:
                {
                    if (downloader?.FailureTimes > 0 && task.AutoRetry)
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
                case DownloadTaskStatus.InQueue:
                case DownloadTaskStatus.Starting:
                case DownloadTaskStatus.Downloading:
                case DownloadTaskStatus.Disabled:
                case DownloadTaskStatus.Stopping:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (actions.Contains(DownloadTaskAction.Restart) || actions.Contains(DownloadTaskAction.StartManually))
            {
                if (status != DownloadTaskStatus.Disabled)
                {
                    if (nextStartDt.HasValue && DateTime.Now > nextStartDt.Value)
                    {
                        actions.Add(DownloadTaskAction.StartAutomatically);
                    }
                }
            }

            var dto = new DownloadTask
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
                AvailableActions = actions,
                AutoRetry = task.AutoRetry,
                CreatedAt = task.CreatedAt,
                Options = task.Options
            };

            return dto;
        }

        public static DownloadTaskDbModel? ToDbModel(this DownloadTask? task)
        {
            if (task == null)
            {
                return null;
            }

            return new DownloadTaskDbModel
            {
                Id = task.Id,
                Key = task.Key,
                Name = task.Name,
                ThirdPartyId = task.ThirdPartyId,
                Type = task.Type,
                Progress = task.Progress,
                DownloadStatusUpdateDt = task.DownloadStatusUpdateDt,
                Interval = task.Interval,
                StartPage = task.StartPage,
                EndPage = task.EndPage,
                Message = task.Message,
                Checkpoint = task.Checkpoint,
                Status = task.Status.ToDomainModel(),
                AutoRetry = task.AutoRetry,
                DownloadPath = task.DownloadPath,
                CreatedAt = task.CreatedAt,
                Options = task.Options
            };
        }

        public static DownloadTaskDbModelStatus ToDomainModel(this DownloadTaskStatus status) {
          return status switch {
            DownloadTaskStatus.Disabled => DownloadTaskDbModelStatus.Disabled,
            DownloadTaskStatus.Complete => DownloadTaskDbModelStatus.Complete,
            DownloadTaskStatus.Failed => DownloadTaskDbModelStatus.Failed,
            DownloadTaskStatus.Idle => DownloadTaskDbModelStatus.InProgress,
            DownloadTaskStatus.InQueue => DownloadTaskDbModelStatus.InProgress,
            DownloadTaskStatus.Starting => DownloadTaskDbModelStatus.InProgress,
            DownloadTaskStatus.Downloading => DownloadTaskDbModelStatus.InProgress,
            DownloadTaskStatus.Stopping => DownloadTaskDbModelStatus.InProgress,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
          };
        }
    }
}