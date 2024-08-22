using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions
{
    public interface IDownloader : IDisposable
    {
        ThirdPartyId ThirdPartyId { get; }
        DownloaderStatus Status { get; }
        string Current { get; }
        Task Stop(DownloaderStopBy stopBy);
        DownloaderStopBy? StoppedBy { get; set; }
        Task Start(DownloadTask task);
        string Message { get; }
        int FailureTimes { get; }
        string Checkpoint { get; }

        void ResetStatus();

        event Func<Task> OnStatusChanged;
        event Func<string, Task> OnNameAcquired;
        event Func<decimal, Task> OnProgress;
        event Func<Task> OnCurrentChanged;
        event Func<string, Task> OnCheckpointChanged;
    }
}