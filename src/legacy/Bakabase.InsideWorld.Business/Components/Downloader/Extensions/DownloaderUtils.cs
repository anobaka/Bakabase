using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using System;
using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Extensions
{
    public class DownloaderUtils
    {
        public static readonly Dictionary<DownloadTaskDtoStatus, DownloadTaskAction[]> AvailableActions = new()
        {
            {
                DownloadTaskDtoStatus.Idle,
                [
                    DownloadTaskAction.StartManually, DownloadTaskAction.Disable, 
                    DownloadTaskAction.StartAutomatically
                ]
            },
            {DownloadTaskDtoStatus.InQueue, [DownloadTaskAction.Disable, DownloadTaskAction.StartManually] },
            {DownloadTaskDtoStatus.Starting, [] },
            {DownloadTaskDtoStatus.Downloading, [DownloadTaskAction.Disable] },
            {DownloadTaskDtoStatus.Stopping, [] },
            {DownloadTaskDtoStatus.Complete, [DownloadTaskAction.Restart, DownloadTaskAction.Disable] },
            {DownloadTaskDtoStatus.Failed, [DownloadTaskAction.Restart, DownloadTaskAction.Disable] },
            {DownloadTaskDtoStatus.Disabled, [DownloadTaskAction.StartManually] },
        };

        public static readonly Dictionary<int, TimeSpan> IntervalsOnContinuousFailures = new()
        {
            {1, TimeSpan.FromMinutes(1)},
            {2, TimeSpan.FromMinutes(5)},
            {3, TimeSpan.FromMinutes(20)},
            {4, TimeSpan.FromMinutes(60)}
        };
    }
}