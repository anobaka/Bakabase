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
        Task Start(DownloadTask task);
        string? Message { get; }
        int FailureTimes { get; }
        string? Checkpoint { get; }

        void ResetStatus();

        event Func<Task>? OnStatusChanged;
        event Func<string, Task>? OnNameAcquired;
        event Func<decimal, Task>? OnProgress;
        event Func<Task>? OnCurrentChanged;
        event Func<string, Task>? OnCheckpointChanged;
    }
}