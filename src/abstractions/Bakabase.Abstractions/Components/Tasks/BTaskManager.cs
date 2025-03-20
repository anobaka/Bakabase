using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.View;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Abstractions.Components.Tasks;

public class BTaskManager : IAsyncDisposable
{
    private readonly IBakabaseLocalizer _localizer;
    private readonly IBOptions<TaskOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, BTaskDescriptor> _taskMap = [];
    private readonly IBTaskEventHandler _eventHandler;

    public BTaskManager(IBakabaseLocalizer localizer, IBOptions<TaskOptions> options, IServiceProvider serviceProvider,
        IBTaskEventHandler eventHandler)
    {
        _localizer = localizer;
        _options = options;
        _serviceProvider = serviceProvider;
        _eventHandler = eventHandler;
    }

    public async Task Initialize()
    {
        var predefinedTasks = _serviceProvider.GetRequiredService<IEnumerable<IPredefinedBTask>>();
        foreach (var pt in predefinedTasks)
        {
            Enqueue(pt.DescriptorBuilder);
        }

        await Daemon();
    }

    public void Enqueue(BTaskDescriptorBuilder descriptorBuilder)
    {
        var descriptor = _buildDescriptor(descriptorBuilder);

        if (!_taskMap.TryAdd(descriptor.Id, descriptor))
        {
            throw new Exception(_localizer.BTask_FailedToRunTaskDueToIdExisting(descriptor.Name));
        }
    }

    private BTaskDescriptor _buildDescriptor(BTaskDescriptorBuilder builder)
    {
        var getName = builder.GetName ?? (Func<string>) (() => builder.Key);

        async Task OnTaskStatusChange(BTaskStatus status)
        {
            await OnTaskChange(builder.Id);
            if (builder.OnStatusChange != null)
            {
                await builder.OnStatusChange(status);
            }
        }

        async Task OnTaskPercentageChange(int percentage)
        {
            await OnTaskChange(builder.Id);
            if (builder.OnPercentageChange != null)
            {
                await builder.OnPercentageChange(percentage);
            }
        }

        async Task OnTaskProcessChange(string process)
        {
            await OnTaskChange(builder.Id);
            if (builder.OnProcessChange != null)
            {
                await builder.OnProcessChange(process);
            }
        }

        var dbModel = _options.Value.Tasks?.FirstOrDefault(x => x.Id == builder.Id);

        return new BTaskDescriptor(builder.Key,
            builder.Run,
            builder.Args,
            builder.Id,
            getName,
            builder.GetDescription,
            builder.GetMessageOnInterruption,
            builder.CancellationToken,
            builder.Level,
            OnTaskStatusChange,
            OnTaskProcessChange,
            OnTaskPercentageChange,
            builder.ConflictKeys,
            dbModel?.Interval,
            builder.IsPersistent
        );
    }

    private async Task OnTaskChange(string id)
    {
        var task = GetTaskViewModel(id);
        if (task != null)
        {
            await _eventHandler.OnTaskChange(task);
        }
    }

    /// <summary>
    /// Start or resume a task
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public async Task Start(string id)
    {
        if (!_taskMap.TryGetValue(id, out var d))
        {
            throw new Exception(_localizer.BTask_FailedToRunTaskDueToUnknownTaskKey(id));
        }

        switch (d.Status)
        {
            case BTaskStatus.Running:
            {
                break;
            }
            case BTaskStatus.Paused:
            {
                await d.Resume();
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

    private BTaskDescriptor[] _getConflictTasks(BTaskDescriptor d)
    {
        return _taskMap.Values
            .Where(x =>
                x != d && (d.ConflictKeys?.Contains(x.Key) == true || x.ConflictKeys?.Contains(d.Key) == true) &&
                x.Status is BTaskStatus.Running or BTaskStatus.Paused)
            .ToArray();
    }


    private Task Daemon()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                var activeTasks = _taskMap.Values.Where(x => x.Interval.HasValue || x.Status is BTaskStatus.NotStarted)
                    .OrderBy(x => x.LastFinishedAt)
                    .ThenBy(x => x.CreatedAt).ToArray();
                foreach (var at in activeTasks)
                {
                    if (!_getConflictTasks(at).Any())
                    {
                        await at.TryStartAutomatically();
                    }
                }

                await Task.Delay(1000);
            }
        });
        return Task.CompletedTask;
    }

    public async Task Pause(string id)
    {
        if (_taskMap.TryGetValue(id, out var t))
        {
            await t.Pause();
        }
    }

    public async Task PauseAll()
    {
        await Task.WhenAll(_taskMap.Values.Select(async t => await t.Pause()));
    }

    public async Task Remove(string id)
    {
        _taskMap.TryRemove(id, out _);
        await _eventHandler.OnAllTasksChange(GetTasksViewModel());
    }

    public async Task RemoveInactive()
    {
        var tasks = _taskMap.Values.ToList();
        foreach (var t in tasks.Where(x =>
                     x.Status is BTaskStatus.Completed or BTaskStatus.Error or BTaskStatus.Stopped))
        {
            _taskMap.TryRemove(t.Id, out _);
        }

        await _eventHandler.OnAllTasksChange(GetTasksViewModel());
    }

    public List<BTaskEvent<int>> GetPercentageEvents(string id) =>
        _taskMap.GetValueOrDefault(id)?.PercentageEvents.ToList() ?? [];

    public List<BTaskEvent<string>> GetProcessEvents(string id) =>
        _taskMap.GetValueOrDefault(id)?.ProcessEvents.ToList() ?? [];

    private BTaskViewModel BuildTaskViewModel(BTaskDescriptor d)
    {
        string? reasonForUnableToStart = null;
        if (d.Status is BTaskStatus.Completed or BTaskStatus.Error or BTaskStatus.NotStarted)
        {
            var conflictTasks = _getConflictTasks(d);
            if (conflictTasks.Any())
            {
                reasonForUnableToStart =
                    _localizer.BTask_FailedToRunTaskDueToConflict(d.Name,
                        conflictTasks.Select(c => c.Name).ToArray());
            }
        }

        return new BTaskViewModel(d, _options.Value.Tasks?.FirstOrDefault(x => x.Id == d.Id), reasonForUnableToStart);
    }

    public List<BTaskViewModel> GetTasksViewModel() => _taskMap.Values.Select(BuildTaskViewModel).ToList();

    public BTaskViewModel? GetTaskViewModel(string id) =>
        _taskMap.TryGetValue(id, out var bt) ? BuildTaskViewModel(bt) : null;

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}