using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions
{
    public abstract class AbstractDownloader : IDownloader
    {
        public int FailureTimes { get; protected set; }
        public void ResetStatus()
        {
            Status = DownloaderStatus.JustCreated;
        }

        public event Func<Task> OnStatusChanged;
        public event Func<string, Task> OnNameAcquired;
        public event Func<decimal, Task> OnProgress;
        public event Func<Task> OnCurrentChanged;
        public event Func<string, Task> OnCheckpointChanged;

        public abstract ThirdPartyId ThirdPartyId { get; }
        public string Current { get; protected set; }
        public string Message { get; protected set; }
        private DownloaderStatus _status = DownloaderStatus.JustCreated;
        protected readonly ILogger Logger;

        protected IServiceProvider ServiceProvider;
        public string Checkpoint { get; protected set; }

        protected T GetRequiredService<T>() => ServiceProvider.GetRequiredService<T>();
        protected DownloaderManager DownloaderManager => GetRequiredService<DownloaderManager>();

        protected AbstractDownloader(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            Logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
        }

        protected async Task OnCheckpointChangedInternal(string checkpoint)
        {
            if (OnCheckpointChanged != null)
            {
                await OnCheckpointChanged(checkpoint);
            }
        }

        protected async Task OnProgressInternal(decimal progress)
        {
            if (OnProgress != null)
            {
                await OnProgress(progress);
            }
        }

        protected async Task OnCurrentChangedInternal()
        {
            if (OnCurrentChanged != null)
            {
                await OnCurrentChanged();
            }
        }

        protected async Task OnNameAcquiredInternal(string name)
        {
            if (OnNameAcquired != null)
            {
                await OnNameAcquired(name);
            }
        }

        public DownloaderStatus Status
        {
            get => _status;
            protected set
            {
                _status = value;
                OnStatusChanged?.Invoke();
            }
        }

        public async Task Stop()
        {
            Status = DownloaderStatus.Stopping;
            Cts?.Cancel();
            await StopCore();
            Status = DownloaderStatus.Stopped;
        }

        protected virtual Task StopCore()
        {
            return Task.CompletedTask;
        }

        protected CancellationTokenSource Cts;

        public async Task Start(DownloadTask task)
        {
            if (Status is DownloaderStatus.Stopped or DownloaderStatus.JustCreated or DownloaderStatus.Failed or DownloaderStatus.Complete)
            {
                Status = DownloaderStatus.Starting;
                Cts?.Cancel();
                Cts = new CancellationTokenSource();

                Message = null;
                if (OnProgress != null)
                {
                    await OnProgress(0);
                }

                Status = DownloaderStatus.Downloading;

                var token = Cts.Token;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await StartCore(task, token);
                    }
                    catch (OperationCanceledException oce)
                    {
                        if (oce.CancellationToken == token)
                        {
                            Status = DownloaderStatus.Stopped;
                        }
                    }
                    catch (Exception e)
                    {
                        FailureTimes++;
                        Message = e.BuildFullInformationText();
                        Status = DownloaderStatus.Failed;
                    }
                    finally
                    {
                        Cts.Cancel();
                    }

                    if (Status == DownloaderStatus.Downloading)
                    {
                        Status = DownloaderStatus.Complete;
                        FailureTimes = 0;
                        if (OnProgress != null)
                        {
                            await OnProgress(100m);
                        }
                    }
                }, token);
            }
        }

        protected abstract Task StartCore(DownloadTask task, CancellationToken ct);

        public virtual void Dispose()
        {

        }
    }
}