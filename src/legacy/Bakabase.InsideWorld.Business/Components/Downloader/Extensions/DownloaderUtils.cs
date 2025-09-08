using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using System;
using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Extensions
{
    public class DownloaderUtils
    {
        public static readonly Dictionary<DownloadTaskStatus, DownloadTaskAction[]> AvailableActions = new()
        {
            {
                DownloadTaskStatus.Idle,
                [
                    DownloadTaskAction.StartManually, DownloadTaskAction.Disable, 
                    DownloadTaskAction.StartAutomatically
                ]
            },
            {DownloadTaskStatus.InQueue, [DownloadTaskAction.Disable, DownloadTaskAction.StartManually] },
            {DownloadTaskStatus.Starting, [] },
            {DownloadTaskStatus.Downloading, [DownloadTaskAction.Disable] },
            {DownloadTaskStatus.Stopping, [] },
            {DownloadTaskStatus.Complete, [DownloadTaskAction.Restart, DownloadTaskAction.Disable] },
            {DownloadTaskStatus.Failed, [DownloadTaskAction.Restart, DownloadTaskAction.Disable] },
            {DownloadTaskStatus.Disabled, [DownloadTaskAction.StartManually] },
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