using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders
{
    public abstract class AbstractDownloader<TEnumTaskType> : IDownloader where TEnumTaskType : struct
    {
        public int FailureTimes { get; protected set; }

        public void ResetStatus()
        {
            Status = DownloaderStatus.JustCreated;
        }

        public event Func<Task>? OnStatusChanged;
        public event Func<string, Task>? OnNameAcquired;
        public event Func<decimal, Task>? OnProgress;
        public event Func<Task>? OnCurrentChanged;
        public event Func<string, Task>? OnCheckpointChanged;

        public abstract ThirdPartyId ThirdPartyId { get; }
        public int TaskType => Convert.ToInt32(EnumTaskType);
        public abstract TEnumTaskType EnumTaskType { get; }
        public string? Current { get; protected set; }
        public string? Message { get; protected set; }
        private DownloaderStatus _status = DownloaderStatus.JustCreated;
        protected readonly ILogger Logger;
        private readonly IDownloaderFactory _downloaderFactory;
        private readonly ISpecialTextService _specialTextService;

        protected IServiceProvider ServiceProvider;
        public string? Checkpoint { get; protected set; }
        public string? NextCheckpoint { get; protected set; }

        protected T GetRequiredService<T>() => ServiceProvider.GetRequiredService<T>();
        protected DownloaderManager DownloaderManager => GetRequiredService<DownloaderManager>();
        protected CancellationTokenSource? Cts;

        protected IDownloaderHelper Helper => _downloaderFactory.GetHelper(ThirdPartyId, TaskType);
        protected readonly DownloaderDefinition Definition;

        protected AbstractDownloader(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            Logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
            _downloaderFactory = serviceProvider.GetRequiredService<IDownloaderFactory>();
            _specialTextService = serviceProvider.GetRequiredService<ISpecialTextService>();
            Definition = DownloaderInternals.DownloaderTypeDefinitionMap[GetType()];
        }

        /// <summary>
        /// Get unified downloader options for this platform
        /// </summary>
        protected async Task<DownloaderOptions> GetDownloaderOptionsAsync() => await Helper.GetOptionsAsync();

        /// <summary>
        /// Get the default naming convention for this downloader from DownloaderAttribute
        /// </summary>
        /// <returns>Default naming convention pattern</returns>
        protected string GetDefaultNamingConvention() => Definition.DefaultConvention;

        /// <summary>
        /// Build download filename using naming convention and values (internal implementation)
        /// </summary>
        /// <param name="values">Dictionary of field values to replace</param>
        /// <returns>Formatted filename</returns>
        protected async Task<string> BuildDownloadFilename<TEnumNamingField>(
            IDictionary<TEnumNamingField, object?> values) where TEnumNamingField : Enum
        {
            // Get field and replacers mapping from the downloader source naming definitions
            var fieldAndReplacements = Definition.NamingFields.ToDictionary(f => f.Key, f => $"{{{f.Key}}}");
            var options = await GetDownloaderOptionsAsync();
            var namingConvention = options.NamingConvention ?? GetDefaultNamingConvention();

            var startIndex = 0;
            var name = namingConvention;
            var strValues = values.ToDictionary(d => d.Key!.ToString(), d => d.Value?.ToString());

            while (true)
            {
                var (key, index) = fieldAndReplacements.ToDictionary(a => a.Key,
                        a => name.IndexOf(a.Value, startIndex, StringComparison.OrdinalIgnoreCase))
                    .Where(a => a.Value > -1).OrderBy(a => a.Value).FirstOrDefault();

                if (key.IsNotEmpty())
                {
                    var replacement = strValues.TryGetValue(key, out var value)
                        ? value?.RemoveInvalidFileNameChars()
                        : null;
                    var replacerLength = fieldAndReplacements[key].Length;
                    var replacementLength = replacement?.Length ?? 0;

                    name = $"{name[..index]}{replacement}{name[(index + replacerLength)..]}";
                    startIndex = index + replacementLength;
                }
                else
                {
                    break;
                }
            }

            var wrappers = await _specialTextService.GetAllWrappers();

            // Remove empty wrappers
            if (wrappers.Any())
            {
                foreach (var wrapper in wrappers)
                {
                    if (wrapper.Key.IsNotEmpty() && wrapper.Value.IsNotEmpty())
                    {
                        name = Regex.Replace(name, $"{Regex.Escape(wrapper.Key)}[\\s]*{Regex.Escape(wrapper.Value)}",
                            string.Empty);
                    }
                }
            }

            return name;
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

        public async Task Stop(DownloaderStopBy stopBy)
        {
            StoppedBy = stopBy;
            Status = DownloaderStatus.Stopping;
            if (Cts != null)
            {
                await Cts.CancelAsync();
            }

            await StopCore();
            Status = DownloaderStatus.Stopped;
        }

        public DownloaderStopBy? StoppedBy { get; set; }

        protected virtual Task StopCore()
        {
            return Task.CompletedTask;
        }

        public async Task Start(DownloadTask task)
        {
            if (Status is DownloaderStatus.Stopped or DownloaderStatus.JustCreated or DownloaderStatus.Failed
                or DownloaderStatus.Complete)
            {
                StoppedBy = null;
                Status = DownloaderStatus.Starting;
                if (Cts != null)
                {
                    await Cts.CancelAsync();
                }

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
                        Message =
                            $"An error occurred during downloading files. You can use expected checkpoint to skip current file: {NextCheckpoint}\n{e.BuildFullInformationText()}";
                        Status = DownloaderStatus.Failed;
                    }
                    finally
                    {
                        await Cts.CancelAsync();
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

    public abstract class AbstractDownloader<TEnumTaskType, TOptions>(IServiceProvider serviceProvider)
        : AbstractDownloader<TEnumTaskType>(serviceProvider)
        where TEnumTaskType : struct
        where TOptions : class, new()
    {
        protected sealed override Task StartCore(DownloadTask task, CancellationToken ct)
        {
            var options = task.GetTypedOptions<TOptions>();
            return StartCore(task, options, ct);
        }

        protected abstract Task StartCore(DownloadTask task, TOptions options, CancellationToken ct);
    }
}