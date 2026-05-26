using System.Collections.Concurrent;
using System.Diagnostics;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NPOI.SS.Formula.Functions;

namespace Bakabase.Abstractions.Components.Tasks;

public class BTaskHandler
{
    public string Id => Task.Id;
    public readonly BTask Task;

    private readonly Func<Task>? _onChange;
    private readonly Func<BTaskStatus, BTask, Task>? _onStatusChange;
    private readonly Func<BTask, Task>? _onPercentageChanged;
    private readonly CancellationToken? _externalCt;
    private readonly IServiceProvider _rootServiceProvider;
    private CancellationTokenSource? _cts;
    private PauseTokenSource? _pts;
    private Timer? _heartbeatTimer;

    /// <summary>
    /// How often the heartbeat fires a change notification while the task body
    /// is active. The point is to keep frontend timers (elapsed / remaining)
    /// from drifting when percentage plateaus — the timing simulator only
    /// resets its baseline when raw elapsed/remaining changes, and those are
    /// derived from <see cref="Sw"/> which advances continuously.
    /// </summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long Stop() waits before logging a warning that the task body has
    /// ignored the cancellation request. Not enforced — tasks that don't
    /// cooperate are left to finish on their own; this just surfaces the bug
    /// so authors know to insert a yield point.
    /// </summary>
    private static readonly TimeSpan StopGraceTimeout = TimeSpan.FromSeconds(10);

    private readonly Func<BTaskArgs, Task> _run;
    public readonly ConcurrentQueue<BTaskEvent<int>> PercentageEvents = [];
    public readonly ConcurrentQueue<BTaskEvent<string?>> ProcessEvents = [];
    private TimeSpan _elapsedOnLastPercentageChange = TimeSpan.Zero;
    public Stopwatch Sw { get; } = new();
    private ILogger _logger;

    public DateTime? NextTimeStartAt
    {
        get
        {
            if (!Task.Interval.HasValue)
            {
                return null;
            }

            if (!Task.Status.IsFinished() || !Task.IsPersistent)
            {
                return null;
            }

            if (Task.LastFinishedAt.HasValue)
            {
                return Task.LastFinishedAt.Value + Task.Interval.Value;
            }

            return DateTime.Now;
        }
    }

    public TimeSpan? EstimateRemainingTime
    {
        get
        {
            if (PercentageEvents.IsEmpty || _elapsedOnLastPercentageChange == TimeSpan.Zero)
            {
                return null;
            }

            if (!Task.Status.CanBeStopped())
            {
                return null;
            }

            var lastEvent = PercentageEvents.Last();
            if (lastEvent.Event is 0 or 100)
            {
                return null;
            }

            // Extrapolate from the live stopwatch so the estimate keeps refining
            // every heartbeat instead of freezing at the last percentage step.
            var elapsed = Sw.Elapsed > _elapsedOnLastPercentageChange
                ? Sw.Elapsed
                : _elapsedOnLastPercentageChange;
            return elapsed / lastEvent.Event * (100 - lastEvent.Event);
        }
    }

    public BTaskHandler(Func<BTaskArgs, Task> run, BTask task,
        IServiceProvider rootServiceProvider,
        Func<BTaskStatus, BTask, Task>? onStatusChange = null,
        Func<BTask, Task>? onPercentageChanged = null,
        Func<Task>? onChange = null,
        CancellationToken? ct = null)
    {
        Task = task;
        _run = run;
        _externalCt = ct;
        _onChange = onChange;
        _rootServiceProvider = rootServiceProvider;
        _onStatusChange = onStatusChange;
        _onPercentageChanged = onPercentageChanged;
        var loggerName = $"{GetType().FullName}:{task.Id}:{task.Name}";
        _logger = rootServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(loggerName);
    }

    public async Task UpdateTask(Action<BTask> update)
    {
        var prevProcess = Task.Process;
        var prevPercentage = Task.Percentage;
        var prevStatus = Task.Status;
        update(Task);

        if (prevProcess != Task.Process)
        {
            ProcessEvents.Enqueue(new BTaskEvent<string?>(Task.Process));
        }

        if (prevPercentage != Task.Percentage)
        {
            _elapsedOnLastPercentageChange = Sw.Elapsed;
            PercentageEvents.Enqueue(new BTaskEvent<int>(Task.Percentage));
            if (_onPercentageChanged != null)
            {
                await _onPercentageChanged(Task);
            }
        }

        if (prevStatus != Task.Status)
        {
            if (_onStatusChange != null)
            {
                await _onStatusChange(prevStatus, Task);
            }
            
            _logger.LogInformation($"status changed from [{prevStatus}] to [{Task.Status}]");
        }

        if (_onChange != null)
        {
            await _onChange();
        }
    }

    public async Task Pause()
    {
        if (_pts != null && Task.Status == BTaskStatus.Running)
        {
            // Surface "Pausing" the moment the click is handled. OnPause only
            // fires once the task body reaches PauseToken.WaitWhilePausedAsync,
            // and a long CPU/IO step between yield points was previously
            // indistinguishable from a pending pause request.
            await UpdateTask(t => t.Status = BTaskStatus.Pausing);
            _pts.Pause();
        }
    }

    public async Task Resume()
    {
        if (_pts != null && Task.Status is BTaskStatus.Paused or BTaskStatus.Pausing)
        {
            await UpdateTask(t => t.Status = BTaskStatus.Resuming);
            _pts.Resume();
        }
    }

    public async Task TryStartAutomatically()
    {
        // Check if waiting for retry
        if (Task.NextRetryAt.HasValue && DateTime.Now < Task.NextRetryAt.Value)
        {
            return;
        }

        if (Task.Status == BTaskStatus.NotStarted || (Task.Interval.HasValue && (!Task.LastFinishedAt.HasValue ||
                                                          DateTime.Now - Task.Interval.Value >
                                                          Task.LastFinishedAt.Value) &&
                                                      Task.Status is BTaskStatus.Completed or BTaskStatus.Error))
        {
            await Start();
        }
    }

    public async Task Start()
    {
        await Stop();

        Sw.Restart();
        _cts = new CancellationTokenSource();
        _cts.Token.Register(() => { Sw.Stop(); });
        CancellationToken ct;
        if (_externalCt.HasValue)
        {
            var mixedCts = CancellationTokenSource.CreateLinkedTokenSource(_externalCt.Value, _cts.Token);
            ct = mixedCts.Token;
        }
        else
        {
            ct = _cts.Token;
        }

        _pts = new PauseTokenSource();
        _pts.OnPause += async (_) =>
        {
            await UpdateTask(t => { t.Status = BTaskStatus.Paused; });
            Sw.Stop();
        };
        _pts.OnResume += async (_) =>
        {
            await UpdateTask(t => { t.Status = BTaskStatus.Running; });
            Sw.Start();
        };

        await UpdateTask(t =>
        {
            t.ClearError();
            t.Status = BTaskStatus.Running;
            t.Percentage = 0;
            t.StartedAt = DateTime.Now;
            t.NextRetryAt = null; // Clear retry time when starting
        });

        StartHeartbeat();

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await _run(new BTaskArgs(_pts.Token, ct, Task, UpdateTask, _rootServiceProvider));
                await UpdateTask(t =>
                {
                    t.ClearError();
                    t.Percentage = 100;
                    t.Status = BTaskStatus.Completed;
                });
            }
            catch (Exception e)
            {
                if (e is OperationCanceledException && _cts!.IsCancellationRequested)
                {
                    await UpdateTask(t => t.Status = BTaskStatus.Cancelled);
                }
                else
                {
                    _logger.LogError(e, "Task failed");

                    // Check if retry is available
                    if (Task.RetryPolicy != null && Task.RetryCount < Task.RetryPolicy.MaxRetries)
                    {
                        var delay = Task.RetryPolicy.GetDelayForRetry(Task.RetryCount);
                        await UpdateTask(t =>
                        {
                            t.SetError((e as BTaskException)?.BriefMessage, e.BuildFullInformationText());
                            t.RetryCount++;
                            t.NextRetryAt = DateTime.Now + delay;
                            t.Status = BTaskStatus.NotStarted; // Will be picked up by daemon
                        });
                        _logger.LogInformation($"Task will retry in {delay.TotalSeconds:F1}s (attempt {Task.RetryCount}/{Task.RetryPolicy.MaxRetries})");
                    }
                    else
                    {
                        await UpdateTask(t =>
                        {
                            t.SetError((e as BTaskException)?.BriefMessage, e.BuildFullInformationText());
                            t.Status = BTaskStatus.Error;
                        });
                    }
                }
            }
            finally
            {
                StopHeartbeat();
                Sw.Stop();
                await UpdateTask(t => t.LastFinishedAt = DateTime.Now);
            }
        }, ct);
    }

    private void StartHeartbeat()
    {
        StopHeartbeat();
        _heartbeatTimer = new Timer(static state =>
        {
            var self = (BTaskHandler) state!;
            if (self._onChange == null) return;
            if (!self.Task.Status.IsAdvancing())
            {
                return;
            }
            _ = self._onChange();
        }, this, HeartbeatInterval, HeartbeatInterval);
    }

    private void StopHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    public async Task Stop()
    {
        if (_cts == null) return;

        // Reflect the intent in the UI immediately. The actual transition to
        // Cancelled happens in the task's catch block once it observes the
        // CancellationToken — until then the user sees "Cancelling" instead
        // of "Running" with a misleading success toast.
        if (Task.Status.CanBeStopped())
        {
            await UpdateTask(t => t.Status = BTaskStatus.Cancelling);
        }

        // If a task body holds the thread between yield points it will ignore
        // the cancellation until the next checkpoint. Log a warning after the
        // grace window so the author knows where to insert a yield — we never
        // force-kill since .NET has no safe API for that.
        var stuckTaskId = Task.Id;
        var stuckTaskName = Task.Name;
        _ = System.Threading.Tasks.Task.Delay(StopGraceTimeout).ContinueWith(_ =>
        {
            if (Task.Status == BTaskStatus.Cancelling)
            {
                _logger.LogWarning(
                    "Task [{TaskId}] {TaskName} did not observe cancellation within {Timeout}s — " +
                    "the task body likely needs a yield point (await args.YieldAsync()) inside its tight loop",
                    stuckTaskId, stuckTaskName, StopGraceTimeout.TotalSeconds);
            }
        });

        await _cts.CancelAsync();
    }
}