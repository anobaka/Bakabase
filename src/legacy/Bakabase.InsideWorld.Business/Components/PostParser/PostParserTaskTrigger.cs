using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.PostParser;

/// <summary>Explicit one-shot dispatch of all pending posts; input-driven automatic parsing is scoped by the service.</summary>
public class PostParserTaskTrigger
{
    public const string TaskId = "ParseAllPosts";

    private readonly BTaskManager _btm;
    private readonly BTaskHandlerBuilder _taskBuilder;
    private readonly SemaphoreSlim _startGate = new(1, 1);

    public PostParserTaskTrigger(BTaskManager btm, IBakabaseLocalizer localizer)
    {
        _btm = btm;
        _taskBuilder = BTaskBuilder.Create(TaskId, localizer.PostParser_ParseAll_TaskName)
            .Persistent()
            .ConflictsWith(TaskId)
            .IgnoreIfExists()
            .Run(async args =>
            {
                await args.YieldAsync();
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IPostParserTaskService>();
                await service.ParseAll(p => args.UpdateTask(x => x.Percentage = p),
                    p => args.UpdateTask(x => x.Process = p),
                    args.PauseToken,
                    args.CancellationToken);
            });
    }

    // Existing workflow runs are recovered by WorkflowRunRehydrator. An app restart or a
    // configuration change must not turn unrelated saved inputs into new executions.
    public Task Initialize() => Task.CompletedTask;

    public async Task Start()
    {
        await _startGate.WaitAsync();
        try
        {
            if (_btm.IsShuttingDown) return;
            var current = _btm.Tasks.FirstOrDefault(t => t.Id == TaskId);
            if (current != null && !current.Task.Status.IsFinished()) return;
            await _btm.Enqueue(_taskBuilder);
            current = _btm.Tasks.First(t => t.Id == TaskId);
            // Ignore a persisted interval from the old recurring dispatcher. This action
            // only runs when explicitly requested, while a completed handler can be restarted.
            await current.UpdateTask(t => { t.Interval = null; t.EnableAfter = null; });
            await _btm.Start(TaskId);
        }
        finally { _startGate.Release(); }
    }
}
