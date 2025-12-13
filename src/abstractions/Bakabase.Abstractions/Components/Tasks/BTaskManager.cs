using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.View;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NPOI.SS.Formula.Functions;

namespace Bakabase.Abstractions.Components.Tasks;

public class BTaskManager : IAsyncDisposable
{
    private readonly IBakabaseLocalizer _localizer;
    private readonly AspNetCoreOptionsManager<TaskOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, BTaskHandler> _taskMap = [];
    private readonly IBTaskEventHandler _eventHandler;
    private readonly IDisposable _optionsChangeHandler;
    private readonly ILogger<BTaskManager> _logger;
    private readonly CancellationTokenSource _daemonCts = new();
    private Task? _daemonTask;

    public BTaskManager(IBakabaseLocalizer localizer, AspNetCoreOptionsManager<TaskOptions> options,
        IServiceProvider serviceProvider,
        IBTaskEventHandler eventHandler, ILogger<BTaskManager> logger)
    {
        _localizer = localizer;
        _options = options;
        _serviceProvider = serviceProvider;
        _eventHandler = eventHandler;
        _logger = logger;

        _optionsChangeHandler = _options.OnChange(o =>
        {
            var dmMap = o.Tasks?.ToDictionary(x => x.Id, x => x);
            foreach (var t in _taskMap.Values)
            {
                t.Task.Interval = dmMap?.GetValueOrDefault(t.Id)?.Interval ?? t.Task.Interval;
                t.Task.EnableAfter = dmMap?.GetValueOrDefault(t.Id)?.EnableAfter ?? t.Task.EnableAfter;
            }

            _ = OnAllTasksChange();
        });
    }

    public async Task Initialize()
    {
        await Daemon();
    }

    private bool _isRunning;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                _ = _eventHandler.OnTaskManagerStatusChange(_isRunning);
            }
        }
    }

    private void UpdateManagerStatus()
    {
        IsRunning = _taskMap.Values.Any(x => x.Task.Status is BTaskStatus.Paused or BTaskStatus.Running);
    }

    /// <summary>
    /// You don't need to start task manually, the daemon will do it for you automatically.
    /// </summary>
    /// <param name="taskBuilder"></param>
    /// <exception cref="Exception"></exception>
    public async Task Enqueue(BTaskHandlerBuilder taskBuilder)
    {
        var handler = _buildHandler(taskBuilder);

        if (!_taskMap.TryAdd(handler.Id, handler))
        {
            switch (taskBuilder.DuplicateIdHandling)
            {
                case BTaskDuplicateIdHandling.Reject:
                    throw new Exception(_localizer.BTask_FailedToRunTaskDueToIdExisting(handler.Id, handler.Task.Name));
                case BTaskDuplicateIdHandling.Ignore:
                    return;
                case BTaskDuplicateIdHandling.Replace:
                {
                    var currentTask = _taskMap[handler.Id];
                    if (currentTask.Task.Status is BTaskStatus.Error or BTaskStatus.Completed or BTaskStatus.Cancelled)
                    {
                        await Clean(taskBuilder.Id);
                        await Enqueue(taskBuilder);
                    }
                    else
                    {
                        throw new Exception(_localizer.BTask_CanNotReplaceAnActiveTask(handler.Id, handler.Task.Name));
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        _ = OnAllTasksChange();
    }

    private BTaskHandler _buildHandler(BTaskHandlerBuilder builder)
    {
        var dbModel = _options.Value.Tasks?.FirstOrDefault(x => x.Id == builder.Id);

        var enableAfter = dbModel?.EnableAfter;
        if (!enableAfter.HasValue)
        {
            if (builder is { StartNow: false, Interval: not null })
            {
                enableAfter = DateTime.Now.Add(builder.Interval.Value);
            }
        }

        var task = builder.ToBTask(enableAfter, dbModel?.Interval);

        return new BTaskHandler(builder.Run, task, _serviceProvider,
            builder.OnStatusChange,
            builder.OnPercentageChanged,
            async () => await OnTaskChange(builder.Id),
            builder.CancellationToken);
    }

    private async Task OnTaskChange(string id)
    {
        var task = GetTaskViewModel(id);
        if (task != null)
        {
            await _eventHandler.OnTaskChange(task);
            UpdateManagerStatus();
        }
    }

    private async Task OnAllTasksChange()
    {
        var tasks = GetTasksViewModel();
        await _eventHandler.OnAllTasksChange(tasks);
        UpdateManagerStatus();
    }

    /// <summary>
    /// Start or resume a task
    /// </summary>
    /// <param name="id"></param>
    /// <param name="newTaskBuilder"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public async Task Start(string id, Func<BTaskHandlerBuilder>? newTaskBuilder = null)
    {
        if (!_taskMap.TryGetValue(id, out var d))
        {
            if (newTaskBuilder != null)
            {
                await Enqueue(newTaskBuilder());
                await Start(id, null);
                return;
            }

            throw new Exception(_localizer.BTask_FailedToRunTaskDueToUnknownTaskId(id));
        }

        switch (d.Task.Status)
        {
            case BTaskStatus.Running:
            {
                break;
            }
            case BTaskStatus.Paused:
            {
                d.Resume();
                break;
            }
            case BTaskStatus.NotStarted:
            case BTaskStatus.Error:
            case BTaskStatus.Completed:
            case BTaskStatus.Cancelled:
            {
                var (blockers, _) = GetDependencyStatus(d);
                if (!_getConflictTasks(d).Any() && blockers.Length == 0)
                {
                    await d.Start();
                }

                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public async Task Stop(string id)
    {
        if (_taskMap.TryGetValue(id, out var task))
        {
            await task.Stop();
        }
    }

    private BTaskHandler[] _getConflictTasks(BTaskHandler d)
    {
        return _taskMap.Values
            .Where(x =>
                x != d &&
                d.Task.ConflictKeys != null && x.Task.ConflictKeys != null &&
                d.Task.ConflictKeys.Intersect(x.Task.ConflictKeys).Any() &&
                x.Task.Status is BTaskStatus.Running or BTaskStatus.Paused)
            .ToArray();
    }

    /// <summary>
    /// Returns dependency status for a task.
    /// </summary>
    /// <param name="d">The task to check</param>
    /// <returns>
    /// blockers: Tasks that are blocking this task from starting
    /// shouldFail: Whether this task should be marked as failed due to dependency failures
    /// </returns>
    private (BTaskHandler[] blockers, bool shouldFail) GetDependencyStatus(BTaskHandler d)
    {
        if (d.Task.DependsOn == null || d.Task.DependsOn.Count == 0)
            return ([], false);

        var blockers = new List<BTaskHandler>();
        var hasFailedDep = false;

        foreach (var depId in d.Task.DependsOn)
        {
            if (_taskMap.TryGetValue(depId, out var depTask))
            {
                if (depTask.Task.Status == BTaskStatus.Error)
                {
                    hasFailedDep = true;
                    // If policy is Skip, don't add to blockers
                    if (d.Task.DependencyFailurePolicy == BTaskDependencyFailurePolicy.Skip)
                        continue;
                }

                if (depTask.Task.Status != BTaskStatus.Completed)
                    blockers.Add(depTask);
            }
            else
            {
                _logger.LogWarning($"Task [{d.Id}] depends on [{depId}] which is not registered");
            }
        }

        var shouldFail = hasFailedDep && d.Task.DependencyFailurePolicy == BTaskDependencyFailurePolicy.Fail;
        return (blockers.ToArray(), shouldFail);
    }

    private Task Daemon()
    {
        _daemonTask = Task.Run(async () =>
        {
            while (!_daemonCts.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var activeTasks = _taskMap.Values
                        .Where(x => (x.NextTimeStartAt < now || x.Task.Status is BTaskStatus.NotStarted) &&
                                    (!x.Task.EnableAfter.HasValue || x.Task.EnableAfter <= now) &&
                                    (!x.Task.NextRetryAt.HasValue || x.Task.NextRetryAt <= now))
                        .OrderBy(x => x.Task.LastFinishedAt).ThenBy(x => x.Task.CreatedAt).ToArray();

                    foreach (var at in activeTasks)
                    {
                        var (blockers, shouldFail) = GetDependencyStatus(at);

                        if (shouldFail)
                        {
                            // Mark task as failed due to dependency failure
                            await at.UpdateTask(t =>
                            {
                                t.SetError("Dependency failed", "One or more dependency tasks have failed");
                                t.Status = BTaskStatus.Error;
                            });
                            continue;
                        }

                        if (!_getConflictTasks(at).Any() && blockers.Length == 0)
                        {
                            await at.TryStartAutomatically();
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "An error occurred during daemon");
                }

                try
                {
                    await Task.Delay(1000, _daemonCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });
        return Task.CompletedTask;
    }

    public void Pause(string id)
    {
        if (_taskMap.TryGetValue(id, out var t))
        {
            t.Pause();
        }
    }

    public void PauseAll()
    {
        foreach (var t in _taskMap.Values)
        {
            t.Pause();
        }
    }

    public void Resume(string id)
    {
        if (_taskMap.TryGetValue(id, out var t))
        {
            t.Resume();
        }
    }

    public async Task Clean(string id)
    {
        _taskMap.TryRemove(id, out _);
        await OnAllTasksChange();
    }

    public async Task CleanInactive()
    {
        var tasks = _taskMap.Values.ToList();
        foreach (var t in tasks.Where(x => x is
                 {
                     Task:
                     {
                         IsPersistent: false, Status: BTaskStatus.Completed or BTaskStatus.Error or BTaskStatus.Cancelled
                     }
                 }))
        {
            _taskMap.TryRemove(t.Id, out _);
        }

        await _eventHandler.OnAllTasksChange(GetTasksViewModel());
    }

    public List<BTaskEvent<int>> GetPercentageEvents(string id) =>
        _taskMap.GetValueOrDefault(id)?.PercentageEvents.ToList() ?? [];

    public List<BTaskEvent<string?>> GetProcessEvents(string id) =>
        _taskMap.GetValueOrDefault(id)?.ProcessEvents.ToList() ?? [];

    public bool IsPending(string id) => _taskMap.TryGetValue(id, out var bth) &&
                                        bth.Task.Status is BTaskStatus.Running or BTaskStatus.Paused
                                            or BTaskStatus.NotStarted;

    private BTaskViewModel BuildTaskViewModel(BTaskHandler handler)
    {
        string? reasonForUnableToStart = null;
        if (handler.Task.Status is BTaskStatus.Completed or BTaskStatus.Error or BTaskStatus.NotStarted)
        {
            var conflictTasks = _getConflictTasks(handler);
            var (dependencyBlockers, _) = GetDependencyStatus(handler);
            if (conflictTasks.Any())
            {
                reasonForUnableToStart =
                    _localizer.BTask_FailedToRunTaskDueToConflict(handler.Task.Name,
                        conflictTasks.Select(c => c.Task.Name).ToArray());
            }
            else if (dependencyBlockers.Any())
            {
                reasonForUnableToStart =
                    _localizer.BTask_FailedToRunTaskDueToDependency(handler.Task.Name,
                        dependencyBlockers.Select(d => d.Task.Name).ToArray());
            }
        }

        return new BTaskViewModel(handler, reasonForUnableToStart);
    }

    public List<BTaskHandler> Tasks => _taskMap.Values.ToList();
    public List<BTaskViewModel> GetTasksViewModel() => _taskMap.Values.Select(BuildTaskViewModel).ToList();

    public BTaskViewModel? GetTaskViewModel(string id) =>
        _taskMap.TryGetValue(id, out var bt) ? BuildTaskViewModel(bt) : null;

    public async ValueTask DisposeAsync()
    {
        _optionsChangeHandler.Dispose();

        // 1. Stop daemon
        await _daemonCts.CancelAsync();
        if (_daemonTask != null)
        {
            try
            {
                await _daemonTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
        }
        _daemonCts.Dispose();

        // 2. Stop non-Critical tasks
        var nonCriticalTasks = _taskMap.Values
            .Where(t => t.Task.Level != BTaskLevel.Critical &&
                        t.Task.Status is BTaskStatus.Running or BTaskStatus.Paused)
            .ToList();

        foreach (var task in nonCriticalTasks)
        {
            _logger.LogInformation($"Stopping non-critical task: {task.Id}");
            await task.Stop();
        }

        // 3. Wait for Critical tasks to complete
        var criticalTasks = _taskMap.Values
            .Where(t => t.Task.Level == BTaskLevel.Critical &&
                        t.Task.Status is BTaskStatus.Running or BTaskStatus.Paused)
            .ToList();

        if (criticalTasks.Count > 0)
        {
            _logger.LogInformation($"Waiting for {criticalTasks.Count} critical task(s) to complete...");

            // Wait for all Critical tasks to complete
            while (criticalTasks.Any(t => t.Task.Status is BTaskStatus.Running or BTaskStatus.Paused))
            {
                await Task.Delay(100);
            }

            _logger.LogInformation("All critical tasks completed");
        }
    }
}