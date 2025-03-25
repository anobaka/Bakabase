﻿using System.Collections.Concurrent;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bootstrap.Components.Tasks;

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
    private CancellationTokenSource? _cts;
    private PauseTokenSource? _pts;

    public string Id { get; init; }
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
    public DateTime? EnableAfter { get; set; }
    private BTaskStatus _status = BTaskStatus.NotStarted;
    public DateTime? StartedAt { get; private set; }
    public int Percentage { get; private set; }
    public DateTime? LastFinishedAt { get; private set; }
    public bool IsPersistent { get; set; }

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

    public BTaskDescriptor(Func<BTaskArgs, Task> run,
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
        DateTime? enableAfter = null,
        bool isPersistent = false)
    {
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
        EnableAfter = enableAfter;
        IsPersistent = isPersistent;
    }

    public void Pause()
    {
        if (_pts != null && Status == BTaskStatus.Running)
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

        _pts = new PauseTokenSource(ct);
        _pts.OnWaitPauseStart += () =>
        {
            Status = BTaskStatus.Paused;
            return Task.CompletedTask;
        };
        _pts.OnWaitPauseEnd += () =>
        {
            Status = BTaskStatus.Running;
            return Task.CompletedTask;
        };

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
                await _run(new BTaskArgs(Args, _pts.Token, ct, _onProcessChange, _onPercentageChange));
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