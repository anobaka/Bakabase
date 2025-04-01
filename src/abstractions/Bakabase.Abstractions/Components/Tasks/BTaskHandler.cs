using System.Collections.Concurrent;
using System.Diagnostics;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bootstrap.Components.Tasks;
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

    private readonly Func<BTaskArgs, Task> _run;
    public readonly ConcurrentQueue<BTaskEvent<int>> PercentageEvents = [];
    public readonly ConcurrentQueue<BTaskEvent<string?>> ProcessEvents = [];
    private TimeSpan _elapsedOnLastPercentageChange = TimeSpan.Zero;
    public Stopwatch Sw { get; } = new();

    public DateTime? NextTimeStartAt
    {
        get
        {
            if (!Task.Interval.HasValue)
            {
                return null;
            }

            if ((Task.Status != BTaskStatus.Completed && Task.Status != BTaskStatus.Error &&
                 Task.Status != BTaskStatus.Stopped) ||
                !Task.IsPersistent)
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

            if (Task.Status != BTaskStatus.Paused && Task.Status != BTaskStatus.Running)
            {
                return null;
            }

            var lastEvent = PercentageEvents.Last();
            if (lastEvent.Event is 0 or 100)
            {
                return null;
            }

            return _elapsedOnLastPercentageChange / lastEvent.Event * (100 - lastEvent.Event);
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
        }

        if (_onChange != null)
        {
            await _onChange();
        }
    }

    public void Pause()
    {
        if (_pts != null && Task.Status == BTaskStatus.Running)
        {
            _pts.Pause();
        }
    }

    public void Resume()
    {
        _pts?.Resume();
    }

    public async Task TryStartAutomatically()
    {
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

        _pts = new PauseTokenSource(ct);
        _pts.OnWaitPauseStart += async () =>
        {
            await UpdateTask(t =>
            {
                Task.Status = BTaskStatus.Paused;
            });
            Sw.Stop();
        };
        _pts.OnWaitPauseEnd += async () =>
        {
            await UpdateTask(t =>
            {
                Task.Status = BTaskStatus.Running;
            });
            Sw.Start();
        };

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await UpdateTask(t =>
            {
                t.Status = BTaskStatus.Running;
                t.Percentage = 0;
                t.Error = null;
                t.StartedAt = DateTime.Now;
            });
            try
            {
                await _run(new BTaskArgs(_pts.Token, ct, Task, UpdateTask, _rootServiceProvider));
                await UpdateTask(t =>
                {
                    t.Percentage = 100;
                    t.Status = BTaskStatus.Completed;
                });
            }
            catch (Exception e)
            {
                if (e is OperationCanceledException oce && oce.CancellationToken == _cts.Token)
                {
                    await UpdateTask(t => t.Status = BTaskStatus.Stopped);
                }
                else
                {
                    await UpdateTask(t =>
                    {
                        Task.Error = string.Join('\n', e.Message, e.StackTrace);
                        Task.Status = BTaskStatus.Error;
                    });
                }
            }
            finally
            {
                Sw.Stop();
                await UpdateTask(t => t.LastFinishedAt = DateTime.Now);
            }
        }, ct);
    }

    public async Task Stop()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
        }
    }
}