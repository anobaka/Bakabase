using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Exceptions;
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
            // Guarded because the scheduler resets every task it did not start, on every pass. Firing
            // a status change unconditionally there meant a database read and a SignalR broadcast per
            // idle task per pass — for a value that had not moved.
            if (_status == DownloaderStatus.JustCreated)
            {
                return;
            }

            Current = null;
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
        private readonly DownloadTimeEstimator _timeEstimator = new();
        public double? EstimatedRemainingSeconds => Status == DownloaderStatus.Downloading
            ? _timeEstimator.EstimateRemainingSeconds()
            : null;
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

            // Sanitize complete components after interpolation: a gallery title or a template
            // literal can leave trailing dots/spaces that Windows strips when creating a directory.
            // Reuse the same safe path for existing-file checks and writes, keeping template folders.
            return string.Join(Path.DirectorySeparatorChar,
                name.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Select(segment =>
                {
                    var safeName = FileNameSanitizer.Sanitize(segment);
                    return safeName.Length == 0 ? "_" : safeName;
                }));
        }

        protected async Task OnCheckpointChangedInternal(string checkpoint)
        {
            _latestCheckpoint = checkpoint;
            if (OnCheckpointChanged != null)
            {
                await OnCheckpointChanged(checkpoint);
            }
        }

        protected async Task OnProgressInternal(decimal progress)
        {
            Touch();
            if (Status == DownloaderStatus.Downloading)
            {
                _timeEstimator.Report(progress);
            }
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
                if (_status != value)
                {
                    _timeEstimator.Reset();
                }
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
            // What was noted so far (the status handler persists it); kept for the run that resumes.
            Message = BuildNoticesMessage();
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
            _latestCheckpoint = null;
            _transientRetries = 0;
            if (string.IsNullOrEmpty(task.Checkpoint))
            {
                // From the beginning: every item noted before will be seen (and noted) again. A task resuming
                // from its checkpoint after a failure or a stop keeps its notes instead — the items behind the
                // checkpoint are not seen again, and a completed run has already handed its notes over.
                ClearNotices();
            }

            OnStarting();
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
                    await RunWithTransientRetriesAsync(task, token);
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
                            Message = BuildNoticesMessage();
                            Status = DownloaderStatus.Stopped;
                        }
                    }
                    else
                    {
                        FailureTimes++;
                        Message = BuildFailureMessage(oce);
                        Current = null;
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
                    // The step that was in flight is over either way; a failed row showing "downloading
                    // torrent file" next to its error reads as still running.
                    Current = null;
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
                    // Before the status flips: the status handler is what persists Message. Null when
                    // nothing was noted, so a clean run never shows a stale note.
                    Message = BuildNoticesMessage();
                    ClearNotices();
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
                    Message = BuildNoticesMessage();
                    Status = DownloaderStatus.Stopped;
                }
            });

            return true;
        }

        private string BuildFailureMessage(Exception e)
        {
            var message = (_transientRetries > 0
                              ? $"An error occurred during downloading files (automatic retries after network errors: {_transientRetries}). "
                              : "An error occurred during downloading files. ") +
                          $"You can use expected checkpoint to skip current file: {NextCheckpoint}\n{e.BuildFullInformationText()}";
            // A reason the user can act on (often localized) goes first: the task row shows the first line.
            if (FindUserActionable(e) is { } actionable && !string.IsNullOrWhiteSpace(actionable.Message))
            {
                message = $"{actionable.Message}\n{message}";
            }

            return BuildNoticesMessage() is { } notices ? $"{message}\n\n{notices}" : message;
        }

        /// <summary>The outermost <see cref="IUserActionableException"/> in <paramref name="e"/>'s chain.</summary>
        private static Exception? FindUserActionable(Exception e)
        {
            for (var current = e; current != null; current = current.InnerException)
            {
                if (current is IUserActionableException)
                {
                    return current;
                }
            }

            return null;
        }

        #region Notices

        /// <summary>At most this many notices are listed in <see cref="Message"/>; the rest are counted.</summary>
        public const int MaxListedNotices = 100;

        private readonly List<string> _notices = [];
        private readonly HashSet<string> _noticeSet = new(StringComparer.Ordinal);

        /// <summary>
        /// Something the user should know about a run that still succeeds (e.g. an item skipped on purpose).
        /// De-duplicated; kept across the automatic re-runs after transient failures and across starts that resume
        /// from the task's checkpoint after a failure or a stop; handed over (and dropped) when the task completes.
        /// Kept in memory only: notes of a run interrupted by an app restart are in the log, not in the next
        /// run's list.
        /// </summary>
        /// <remarks>
        /// The notices become <see cref="Message"/> when the task completes (and are appended to the failure text
        /// when it fails), as plain text: a summary line, one <c>- </c> line per notice (at most
        /// <see cref="MaxListedNotices"/>), an optional truncation line and an optional footer
        /// (<see cref="GetNoticesFooter"/>). The UI shows the first line and the whole text on demand.
        /// </remarks>
        protected void AddNotice(string notice)
        {
            lock (_notices)
            {
                if (_noticeSet.Add(notice))
                {
                    _notices.Add(notice);
                }
            }
        }

        private void ClearNotices()
        {
            lock (_notices)
            {
                _notices.Clear();
                _noticeSet.Clear();
            }
        }

        /// <summary>Optional last line under the notices (e.g. "skipped items are not retried").</summary>
        protected virtual string? GetNoticesFooter() => null;

        private string? BuildNoticesMessage()
        {
            lock (_notices)
            {
                if (_notices.Count == 0)
                {
                    return null;
                }

                var localizer = GetRequiredService<IDownloaderLocalizer>();
                var lines = new List<string> {localizer.DownloadNoticesSummary(_notices.Count)};
                lines.AddRange(_notices.Take(MaxListedNotices).Select(n => "- " + n));
                if (_notices.Count > MaxListedNotices)
                {
                    lines.Add(localizer.DownloadNoticesTruncated(_notices.Count - MaxListedNotices));
                }

                if (GetNoticesFooter() is { } footer)
                {
                    lines.Add(footer);
                }

                return string.Join('\n', lines);
            }
        }

        #endregion

        /// <summary>
        /// Called by <see cref="Start"/> before the first run, after the downloader's own per-run state was reset:
        /// the place to reset state a derived downloader keeps across the automatic re-runs of one start.
        /// </summary>
        protected virtual void OnStarting()
        {
        }

        /// <summary>
        /// How long to wait before each automatic re-run of a task that a transient network failure
        /// interrupted; the number of entries is the number of re-runs.
        /// </summary>
        /// <remarks>
        /// Individual requests already retry briefly on their own. This covers what they cannot: an
        /// outage that outlasts them, and every request that has no retry of its own (list pages,
        /// torrent pages, API calls). Kept well inside the queue watchdog's stall threshold, and
        /// short enough that a real outage still reaches Failed — and the slower task-level auto
        /// retry — within a few minutes.
        /// </remarks>
        protected virtual IReadOnlyList<TimeSpan> TransientFailureRetryDelays { get; } =
            [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90)];

        /// <summary>
        /// The wait before a re-run. A seam for tests; it must observe <paramref name="ct"/> so that
        /// stopping a waiting task takes effect at once.
        /// </summary>
        protected virtual Task DelayBeforeRetryAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);

        /// <summary>One automatic re-run: wait <see cref="Delay"/>, then run again.</summary>
        /// <param name="Retry">This re-run's number, shown to the user.</param>
        /// <param name="MaxRetries">How many re-runs of this kind there can be, shown to the user.</param>
        protected sealed record TransientRetry(TimeSpan Delay, int Retry, int MaxRetries);

        /// <summary>
        /// Whether, and after how long, to run the task again after the transient failure <paramref name="e"/>.
        /// </summary>
        /// <param name="e">The failure; <see cref="TransientNetworkError.IsTransient"/> already holds for it.</param>
        /// <param name="attempt">The 1-based number of the re-run being considered, counting every re-run of this
        /// start.</param>
        /// <returns>The re-run, or null to fail now. The default follows
        /// <see cref="TransientFailureRetryDelays"/>.</returns>
        protected virtual TransientRetry? GetTransientRetry(Exception e, int attempt)
        {
            var delays = TransientFailureRetryDelays;
            return attempt <= delays.Count ? new TransientRetry(delays[attempt - 1], attempt, delays.Count) : null;
        }

        /// <summary>The step shown while waiting for <paramref name="retry"/>; refreshed while the wait lasts.</summary>
        protected virtual string DescribeTransientRetryWait(Exception e, TransientRetry retry, TimeSpan remaining) =>
            GetRequiredService<IDownloaderLocalizer>().TransientNetworkErrorRetrying(
                (int) Math.Ceiling(Math.Max(0, remaining.TotalSeconds)), retry.Retry, retry.MaxRetries);

        /// <summary>
        /// How often the waiting step is rewritten during a long wait. Each rewrite is also a sign of life for the
        /// queue watchdog, so this must stay well under its stall threshold.
        /// </summary>
        protected virtual TimeSpan TransientRetryStepRefreshInterval => TimeSpan.FromMinutes(1);

        private string? _latestCheckpoint;
        private int _transientRetries;

        /// <summary>
        /// Runs <see cref="StartCore"/>, and runs it again after a transient network failure instead
        /// of failing the task outright.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Before this, a single dropped connection anywhere in a download — one TLS handshake cut
        /// short while fetching one image of a gallery — failed the whole task, and the only way back
        /// was the task-level auto retry a minute later or the user pressing restart. A re-run is
        /// cheap because every downloader resumes: finished files are skipped and list downloaders
        /// continue from their checkpoint, which is carried forward from the failed run because the
        /// <paramref name="task"/> snapshot still holds the one the run started with.
        /// </para>
        /// <para>
        /// The task stays Downloading throughout, so stopping it works as usual (the wait observes
        /// the token), <see cref="FailureTimes"/> — which drives the task-level retry schedule —
        /// only counts a failure that survived every re-run, and the row shows what is happening
        /// instead of a failure that is about to heal itself.
        /// </para>
        /// </remarks>
        private async Task RunWithTransientRetriesAsync(DownloadTask task, CancellationToken token)
        {
            for (var attempt = 1;; attempt++)
            {
                TransientRetry? retry = null;
                Exception failure;
                // Each run gets its own token so that whatever a failed run left in flight — ExHentai
                // gives up on a gallery while sibling image downloads are still running — is
                // cancelled before the next run starts, instead of racing it.
                using (var runCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    try
                    {
                        await StartCore(task, runCts.Token);
                        return;
                    }
                    catch (Exception e) when (IsRetryableFailure(e, token) &&
                                              (retry = GetTransientRetry(e, attempt)) != null)
                    {
                        await runCts.CancelAsync();
                        failure = e;
                        _transientRetries = attempt;
                        Logger.LogWarning(e,
                            "A transient network error interrupted download task {TaskId}; running it again in {Delay} ({Retry}/{MaxRetries})",
                            task.Id, retry.Delay, retry.Retry, retry.MaxRetries);
                    }
                }

                // The estimate would only grow while nothing moves, then start from a stale baseline.
                _timeEstimator.Reset();
                // Also a sign of life for the queue watchdog, which would otherwise read a long wait
                // as a stalled download.
                // Only reached through the catch above, which set both.
                var plan = retry!;
                Current = DescribeTransientRetryWait(failure, plan, plan.Delay);
                await OnCurrentChangedInternal();

                try
                {
                    await WaitBeforeRetryAsync(failure, plan, token);
                }
                catch (OperationCanceledException)
                {
                    // A stop can complete — clearing Current on its way to Stopped — between the
                    // failure and the retry step above being written. Nothing else clears Current on
                    // a stopped downloader, so the idle row would keep promising a retry.
                    Current = null;
                    await OnCurrentChangedInternal();
                    throw;
                }

                Current = null;
                await OnCurrentChangedInternal();
                // Do not start another run for a task stopped while that step was being pushed.
                token.ThrowIfCancellationRequested();
                if (_latestCheckpoint != null)
                {
                    task.Checkpoint = _latestCheckpoint;
                }
            }
        }

        /// <summary>
        /// <see cref="DelayBeforeRetryAsync"/>, while rewriting the waiting step every
        /// <see cref="TransientRetryStepRefreshInterval"/> with the time left: a wait of several minutes must not
        /// look like a stalled task, to the user or to the queue watchdog.
        /// </summary>
        private async Task WaitBeforeRetryAsync(Exception failure, TransientRetry retry, CancellationToken token)
        {
            using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var refresh = RefreshRetryStepAsync(failure, retry, refreshCts.Token);
            try
            {
                await DelayBeforeRetryAsync(retry.Delay, token);
            }
            finally
            {
                await refreshCts.CancelAsync();
                try
                {
                    await refresh;
                }
                catch (OperationCanceledException)
                {
                    // Stopped with the wait.
                }
            }
        }

        private async Task RefreshRetryStepAsync(Exception failure, TransientRetry retry, CancellationToken ct)
        {
            var interval = TransientRetryStepRefreshInterval;
            if (interval <= TimeSpan.Zero)
            {
                return;
            }

            var startedAt = Environment.TickCount64;
            while (true)
            {
                await Task.Delay(interval, ct);
                var remaining = retry.Delay - TimeSpan.FromMilliseconds(Environment.TickCount64 - startedAt);
                if (remaining <= TimeSpan.Zero)
                {
                    return;
                }

                Current = DescribeTransientRetryWait(failure, retry, remaining);
                await OnCurrentChangedInternal();
            }
        }

        private bool IsRetryableFailure(Exception e, CancellationToken token) =>
            // A stop that raced the failure wins: nothing should start again behind the user's back.
            Status == DownloaderStatus.Downloading &&
            e is not DownloadDeferredException &&
            TransientNetworkError.IsTransient(e, token);

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
