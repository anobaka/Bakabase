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
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Extensions;

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
        private readonly ITextVocabularyService _textVocabularyService;

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
            _textVocabularyService = serviceProvider.GetRequiredService<ITextVocabularyService>();
            Definition = DownloaderInternals.DownloaderTypeDefinitionMap[GetType()];
        }

        /// <summary>
        /// Get unified downloader options for this platform
        /// </summary>
        protected async Task<DownloaderOptions> GetDownloaderOptionsAsync() => await Helper.GetOptionsAsync();

        protected string GetEffectiveNamingConvention(string? overrideConvention) =>
            string.IsNullOrWhiteSpace(overrideConvention)
                ? Definition.DefaultConvention
                : overrideConvention;

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
            var namingConvention = GetEffectiveNamingConvention(options.NamingConvention);

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

            var wrappers = (await _textVocabularyService.ResolveSet(WellKnownTextType.Wrapper)).ToPairMap();

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
            Touch();
            if (OnProgress != null)
            {
                await OnProgress(progress);
            }
        }

        protected async Task OnCurrentChangedInternal()
        {
            Touch();
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

        /// <inheritdoc />
        public DateTime LastActivityAt { get; private set; } = DateTime.Now;

        /// <summary>Records a sign of life for the queue watchdog.</summary>
        protected void Touch() => LastActivityAt = DateTime.Now;

        public DownloaderStatus Status
        {
            get => _status;
            protected set
            {
                _status = value;
                LastActivityAt = DateTime.Now;

                // Raised without awaiting because this is a property setter. That is exactly why the
                // result has to be observed: a handler that faults here used to vanish into an
                // unobserved Task, and since the *only* thing that advances the download queue is a
                // status-change handler, one swallowed exception left the queue permanently idle
                // with no trace in the log.
                var raised = OnStatusChanged?.Invoke();
                if (raised is { IsCompletedSuccessfully: false })
                {
                    _ = ObserveStatusChangedAsync(raised, value);
                }
            }
        }

        private async Task ObserveStatusChangedAsync(Task raised, DownloaderStatus status)
        {
            try
            {
                await raised;
            }
            catch (Exception e)
            {
                Logger.LogError(e, "A status-changed handler failed for status {Status}", status);
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

            // The step text belongs to work that is no longer running. Leaving it behind is what made
            // a parked task read as "stuck at downloading torrent file" forever: the row kept its last
            // step next to an idle badge, with nothing to say the step had been abandoned.
            Current = null;
            Status = DownloaderStatus.Stopped;
        }

        public DownloaderStopBy? StoppedBy { get; set; }

        protected virtual Task StopCore()
        {
            return Task.CompletedTask;
        }

        public async Task<bool> Start(DownloadTask task)
        {
            if (Status is not (DownloaderStatus.Stopped or DownloaderStatus.JustCreated or DownloaderStatus.Failed
                or DownloaderStatus.Complete))
            {
                return false;
            }

            StoppedBy = null;
            Status = DownloaderStatus.Starting;
            if (Cts != null)
            {
                await Cts.CancelAsync();
            }

            Cts = new CancellationTokenSource();

            Message = null;
            Current = null;
            if (OnProgress != null)
            {
                await OnProgress(0);
            }

            Status = DownloaderStatus.Downloading;

            var cts = Cts;
            var token = cts.Token;

            // Deliberately NOT Task.Run(..., token): a token already cancelled at schedule time makes
            // the delegate never run, so nothing would ever move the downloader out of Downloading and
            // the whole source's queue would be blocked by a task that never even began. The body
            // observes the token itself.
            _ = Task.Run(async () =>
            {
                try
                {
                    await StartCore(task, token);
                }
                catch (OperationCanceledException oce)
                {
                    // Any cancellation of our own token ends the run; comparing token identity missed
                    // the linked/derived tokens that HttpClient and SemaphoreSlim throw with, which
                    // left the downloader sitting in Downloading forever.
                    if (oce.CancellationToken == token || token.IsCancellationRequested)
                    {
                        if (Status is DownloaderStatus.Downloading)
                        {
                            // Never leave StoppedBy null on a Stopped downloader: consumers switch on
                            // it, and a null there threw straight through the status handler — which
                            // is the one thing that advances the queue.
                            StoppedBy ??= DownloaderStopBy.AppendToTheQueue;
                            Current = null;
                            Status = DownloaderStatus.Stopped;
                        }
                    }
                    else
                    {
                        FailureTimes++;
                        Message = BuildFailureMessage(oce);
                        Status = DownloaderStatus.Failed;
                    }
                }
                catch (DownloadDeferredException)
                {
                    // The task gave up its slot on purpose (e.g. no torrent under torrent-priority).
                    // Mark it Stopped/Defer so it stays eligible and the scheduler advances, without
                    // counting as a failure.
                    StoppedBy = DownloaderStopBy.Defer;
                    Status = DownloaderStatus.Stopped;
                }
                catch (Exception e)
                {
                    FailureTimes++;
                    Message = BuildFailureMessage(e);
                    Status = DownloaderStatus.Failed;
                }
                finally
                {
                    try
                    {
                        await cts.CancelAsync();
                    }
                    catch (ObjectDisposedException)
                    {
                        // A concurrent restart already replaced and disposed this source.
                    }
                }

                if (Status == DownloaderStatus.Downloading)
                {
                    // Drop the in-flight step text before flipping to a terminal state. The DTO reads
                    // Current straight off the downloader and the status change is what pushes it to the
                    // UI, so this has to happen first — otherwise a finished task keeps rendering its last
                    // step (e.g. "downloading torrent file") right next to a Complete badge.
                    Current = null;
                    Status = DownloaderStatus.Complete;
                    FailureTimes = 0;
                    if (OnProgress != null)
                    {
                        await OnProgress(100m);
                    }
                }
                else if (Status is DownloaderStatus.Starting or DownloaderStatus.Stopping)
                {
                    // The body finished while the downloader was mid-transition. Without this the
                    // downloader would keep claiming the source's single slot with no runner behind it.
                    Current = null;
                    StoppedBy ??= DownloaderStopBy.AppendToTheQueue;
                    Status = DownloaderStatus.Stopped;
                }
            });

            return true;
        }

        private string BuildFailureMessage(Exception e) =>
            $"An error occurred during downloading files. You can use expected checkpoint to skip current file: {NextCheckpoint}\n{e.BuildFullInformationText()}";

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