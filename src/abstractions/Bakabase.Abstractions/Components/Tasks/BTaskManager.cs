using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
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
    public void Enqueue(BTaskHandlerBuilder taskBuilder)
    {
        var handler = _buildHandler(taskBuilder);

        if (!_taskMap.TryAdd(handler.Id, handler))
        {
            throw new Exception(_localizer.BTask_FailedToRunTaskDueToIdExisting(handler.Task.Name));
        }

        _ = OnAllTasksChange();
    }

    /// <summary>
    /// You don't need to start task manually, the daemon will do it for you automatically.
    /// </summary>
    /// <param name="taskBuilder"></param>
    /// <exception cref="Exception"></exception>
    public void EnqueueSafely(BTaskHandlerBuilder taskBuilder)
    {
        var handler = _buildHandler(taskBuilder);

        if (_taskMap.TryAdd(handler.Id, handler))
        {
            _ = OnAllTasksChange();
        }
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

        var task = new BTask(builder.Id, builder.GetName, builder.GetDescription, builder.GetMessageOnInterruption,
            builder.ConflictKeys, builder.Level, builder.IsPersistent, builder.Type, builder.ResourceType, builder.ResourceKeys)
        {
            EnableAfter = enableAfter,
            Interval = dbModel?.Interval ?? builder.Interval
        };

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
                Enqueue(newTaskBuilder());
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
            case BTaskStatus.Stopped:
            {
                if (!_getConflictTasks(d).Any())
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


    private Task Daemon()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var now = DateTime.Now;
                    var activeTasks = _taskMap.Values
                        .Where(x => (x.NextTimeStartAt < now || x.Task.Status is BTaskStatus.NotStarted) &&
                                    (!x.Task.EnableAfter.HasValue || x.Task.EnableAfter <= now))
                        .OrderBy(x => x.Task.LastFinishedAt).ThenBy(x => x.Task.CreatedAt).ToArray();
                    foreach (var at in activeTasks)
                    {
                        if (!_getConflictTasks(at).Any())
                        {
                            await at.TryStartAutomatically();
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "An error occurred during daemon");
                }

                await Task.Delay(1000);
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
                         IsPersistent: false, Status: BTaskStatus.Completed or BTaskStatus.Error or BTaskStatus.Stopped
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
            if (conflictTasks.Any())
            {
                reasonForUnableToStart =
                    _localizer.BTask_FailedToRunTaskDueToConflict(handler.Task.Name,
                        conflictTasks.Select(c => c.Task.Name).ToArray());
            }
        }

        return new BTaskViewModel(handler, reasonForUnableToStart);
    }

    public List<BTaskHandler> Tasks => _taskMap.Values.ToList();
    public List<BTaskViewModel> GetTasksViewModel() => _taskMap.Values.Select(BuildTaskViewModel).ToList();

    public BTaskViewModel? GetTaskViewModel(string id) =>
        _taskMap.TryGetValue(id, out var bt) ? BuildTaskViewModel(bt) : null;

    public ValueTask DisposeAsync()
    {
        _optionsChangeHandler.Dispose();
        return ValueTask.CompletedTask;
    }
}