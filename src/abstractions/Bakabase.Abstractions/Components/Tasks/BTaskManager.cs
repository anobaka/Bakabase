using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using Quartz;
using Quartz.Impl;

namespace Bakabase.Abstractions.Components.Tasks;

public class BTaskManager : IAsyncDisposable
{
    private readonly IBakabaseLocalizer _localizer;
    private readonly ConcurrentDictionary<string, IBTaskHandler> _taskHandlers;
    private IScheduler _scheduler = null!;
    private readonly ConcurrentDictionary<string, BTaskAttribute> _attributeMap;
    private readonly IBOptions<TaskOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, BTaskRunner> _runners = [];

    public BTaskManager(IBakabaseLocalizer localizer, IEnumerable<IBTaskHandler> taskHandlers,
        IBOptions<TaskOptions> options, IServiceProvider serviceProvider)
    {
        _localizer = localizer;

        var uniqueHandlers = taskHandlers.GroupBy(x => x.Key).Select(x => x.First()).ToArray();
        _taskHandlers =
            new ConcurrentDictionary<string, IBTaskHandler>(uniqueHandlers.ToDictionary(x => x.Key, x => x));
        _attributeMap = new ConcurrentDictionary<string, BTaskAttribute>(
            uniqueHandlers.ToDictionary(d => d.Key, d => d.GetType().GetCustomAttribute<BTaskAttribute>())!);

        _options = options;
        _serviceProvider = serviceProvider;
    }

    protected async Task Schedule(BTaskViewModel task, bool stopImmediately)
    {
        var jobKey = new JobKey(task.Key);
        await _scheduler.DeleteJob(jobKey);

        if (stopImmediately)
        {
            await _taskHandlers[task.Key].Stop();
        }

        var job = JobBuilder.Create(_taskHandlers[task.Key].GetType())
            .WithIdentity(jobKey)
            .SetJobData(new JobDataMap(
                new Dictionary<string, object> {{nameof(IServiceProvider), _serviceProvider}} as IDictionary))
            .Build();

        var tb = TriggerBuilder.Create()
            .WithSchedule(SimpleScheduleBuilder.Create().WithInterval(task.Interval).RepeatForever());
        if (task.EnableAfter.HasValue)
        {
            tb.StartAt(task.EnableAfter.Value);
        }

        var trigger = tb.Build();

        await _scheduler.ScheduleJob(job, trigger);
    }

    public async Task Start()
    {
        var factory = new StdSchedulerFactory();
        _scheduler = await factory.GetScheduler();

        var tasks = Tasks;
        foreach (var task in tasks)
        {
            await Schedule(task, true);
        }
    }

    public async Task StartTask<TTask>(params object[] args) where TTask : IBTaskHandler
    {
        var task = _taskHandlers.Values.FirstOrDefault(x => x.GetType() == typeof(TTask));
        if (task == null)
        {
            return;
        }

        await task.Start();
    }

    public async Task Run(BTaskRunInputModel model)
    {
        var runner =new BTaskRunner(model);

        if (model.StopPrevious)
        {
            if (_runners.TryGetValue(model.Key, out var r))
            {
                await r.Stop();
            }
        }

        if (!model.IgnoreConflicts && model.ConflictWithTaskKeys?.Any() == true)
        {
            var conflictTasks = _runners.Where(x =>
                model.ConflictWithTaskKeys.Contains(x.Key) && x.Value.Status == BTaskStatus.Running).ToList();
            if (conflictTasks.Any())
            {
                throw new Exception(
                    $"Can not initialize task [{runner.GetName}] due to conflict tasks are still running: {string.Join(',', conflictTasks.Select(c => c.Value.GetName))}");
            }
        }

        _runners[model.Key] = runner;
        await runner.Run();
    }

    public List<BTaskViewModel> Tasks
    {
        get
        {
            var dbModels = (_options.Value.Tasks ?? []).GroupBy(d => d.Key).ToDictionary(d => d.Key, d => d.First());
            var tasks = _taskHandlers.Values.Select(x => new BTaskViewModel
            {
                Description = _localizer.BTask_Description(x.Key),
                Name = _localizer.BTask_Name(x.Key),
                RiskOnInterruption = _localizer.BTask_RiskOnInterruption(x.Key),
                EnableAfter = dbModels.GetValueOrDefault(x.Key)?.EnableAfter,
                Error = x.Error,
                Interval = dbModels.GetValueOrDefault(x.Key)?.Interval ?? _attributeMap[x.Key].DefaultInterval,
                Key = x.Key,
                Percentage = x.Percentage,
                Status = x.Status
            }).ToList();
            return tasks;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _scheduler.Shutdown();
    }
}