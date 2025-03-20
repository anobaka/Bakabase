using System.Collections.Concurrent;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;

namespace Bakabase.Abstractions.Components.Tasks;

public class BTaskDescriptor
{
    private readonly Func<string> _getName;
    private readonly Func<string?>? _getDescription;
    private readonly Func<string?>? _getMessageOnInterruption;
    private readonly Func<string, Task>? _onProcessChange;
    private readonly Func<int, Task>? _onPercentageChange;
    private readonly Func<BTaskStatus, Task>? _onStatusChange;
    private readonly CancellationToken? _externalCt;
    private BTaskPauseToken? _pt;
    private CancellationTokenSource? _cts;

    public string Id { get; init; }
    public string Key { get; }
    private readonly Func<BTaskArgs, Task> _run;
    public DateTime CreatedAt { get; } = DateTime.Now;
    public object?[]? Args { get; }
    public string Name => _getName();
    public string? Description => _getDescription?.Invoke();
    public string? MessageOnInterruption => _getMessageOnInterruption?.Invoke();
    public HashSet<string>? ConflictKeys { get; init; }
    public BTaskLevel Level { get; }
    public readonly ConcurrentQueue<BTaskEvent<int>> PercentageEvents = [];
    public readonly ConcurrentQueue<BTaskEvent<string>> ProcessEvents = [];
    public string? Error { get; private set; }
    public string? StackTrace { get; private set; }
    public TimeSpan? Interval { get; set; }
    private BTaskStatus _status = BTaskStatus.NotStarted;
    public DateTime? StartedAt { get; private set; }
    public int Percentage { get; private set; }
    public DateTime? LastFinishedAt { get; private set; }

    public TimeSpan? EstimateRemainingTime
    {
        get
        {
            if (PercentageEvents.IsEmpty || !StartedAt.HasValue)
            {
                return null;
            }

            var lastEvent = PercentageEvents.Last();
            if (lastEvent.Event == 0)
            {
                return null;
            }

            var elapsed = StartedAt.Value - lastEvent.DateTime;

            return elapsed / lastEvent.Event * (100 - lastEvent.Event);
        }
    }

    public BTaskStatus Status
    {
        private set
        {
            _status = value;
            _onStatusChange?.Invoke(value);
        }
        get => _status;
    }

    public BTaskDescriptor(string key,
        Func<BTaskArgs, Task> run,
        object?[]? args,
        string id,
        Func<string> getName,
        Func<string?>? getDescription = null,
        Func<string?>? getMessageOnInterruption = null,
        CancellationToken? ct = null,
        BTaskLevel level = BTaskLevel.Default,
        Func<BTaskStatus, Task>? onStatusChange = null,
        Func<string, Task>? onProcessChange = null,
        Func<int, Task>? onPercentageChange = null,
        HashSet<string>? conflictKeys = null,
        TimeSpan? interval = null,
        bool isPersistent = false)
    {
        Key = key;
        _run = run;
        Args = args;
        Id = id;
        _externalCt = ct;
        _onStatusChange = onStatusChange;
        _getName = getName;
        _getDescription = getDescription;
        _getMessageOnInterruption = getMessageOnInterruption;
        ConflictKeys = conflictKeys;
        Level = level;
        _onPercentageChange = onPercentageChange;
        _onProcessChange = onProcessChange;
        Interval = interval;
    }

    public async Task Pause()
    {
        if (_pt != null && Status == BTaskStatus.Running)
        {
            await _pt.Pause();
        }
    }

    public Task Resume()
    {
        if (_pt != null)
        {
            _pt.Resume();
        }

        return Task.CompletedTask;
    }

    public async Task TryStartAutomatically()
    {
        if (Interval.HasValue && (!LastFinishedAt.HasValue || DateTime.Now - Interval.Value > LastFinishedAt.Value) &&
            Status is BTaskStatus.Completed or BTaskStatus.Error or BTaskStatus.NotStarted)
        {
            await Start();
        }
    }

    public async Task Start()
    {
        await Stop();

        _cts = new CancellationTokenSource();
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

        _pt = new BTaskPauseToken(ct);

        await Task.Run(async () =>
        {
            _cts = new CancellationTokenSource();
            Status = BTaskStatus.Running;
            Percentage = 0;
            Error = null;
            StackTrace = null;
            StartedAt = DateTime.Now;
            try
            {
                await _run(new BTaskArgs(Args, _pt, ct, _onProcessChange, _onPercentageChange));
                Status = BTaskStatus.Completed;
            }
            catch (Exception e)
            {
                if (e is OperationCanceledException oce && oce.CancellationToken == _cts.Token)
                {
                    Status = BTaskStatus.Stopped;
                }
                else
                {
                    Error = e.Message;
                    StackTrace = e.StackTrace;
                    Status = BTaskStatus.Error;
                }
            }
            finally
            {
                LastFinishedAt = DateTime.Now;
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