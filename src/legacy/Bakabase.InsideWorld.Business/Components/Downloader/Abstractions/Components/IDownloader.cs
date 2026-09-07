using System;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components
{
    public interface IDownloader : IDisposable
    {
        ThirdPartyId ThirdPartyId { get; }
        int TaskType { get; }
        DownloaderStatus Status { get; }
        string? Current { get; }
        Task Stop(DownloaderStopBy stopBy);
        DownloaderStopBy? StoppedBy { get; set; }

        /// <returns>
        /// True when this call actually started the download. False when the downloader was already
        /// occupied and the request was a no-op — the caller must not report the task as started,
        /// or a wedged downloader silently swallows every attempt to run its task.
        /// </returns>
        Task<bool> Start(DownloadTask task);

        string? Message { get; }
        int FailureTimes { get; }
        string? Checkpoint { get; }

        /// <summary>
        /// When this downloader last showed a sign of life (status change, progress, step change).
        /// The queue watchdog uses it to tell a slow download from a wedged one.
        /// </summary>
        DateTime LastActivityAt { get; }

        void ResetStatus();

        event Func<Task>? OnStatusChanged;
        event Func<string, Task>? OnNameAcquired;
        event Func<decimal, Task>? OnProgress;
        event Func<Task>? OnCurrentChanged;
        event Func<string, Task>? OnCheckpointChanged;
    }
}