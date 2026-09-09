using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Workflow.Activities.Actions;

/// <summary>
/// Asks the path marks to sync.
/// <para>
/// What a chain does after putting files somewhere: the marks over that folder are what turn it
/// into resources and properties, and without a nudge they wait for the next scheduled scan. It
/// runs once for the whole run rather than once per item — a hundred files landing in one folder
/// is one thing to re-read, not a hundred.
/// </para>
/// </summary>
public class PathMarkEnqueueSyncActivity : IWorkflowActivity
{
    public string Kind => "action.pathmark.enqueueSync";
    public string DisplayName => "Sync path marks";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public string Group => WorkflowActivityGroups.Fs;

    /// <summary>Anything. A sync is about the library, not about the item that prompted it.</summary>
    public Type? AcceptedItemInterface => null;

    public WorkflowItemTypeBehavior OutputBehavior => WorkflowItemTypeBehavior.Passthrough;

    public record Config
    {
        /// <summary>
        /// Which marks to sync. Empty syncs whatever is pending, which is the usual case: the
        /// chain does not know which marks cover the folder it just wrote to.
        /// </summary>
        public int[]? MarkIds { get; init; }
    }

    /// <summary>
    /// Guards against one sync per item. Cleared per run by keying on the run, so the next run
    /// asks again.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, byte> _syncedRuns = new();

    public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item,
        CancellationToken ct)
    {
        if (!_syncedRuns.TryAdd(ctx.RunId, 0)) return WorkflowItemOutcome.KeepItem;

        var markIds = ctx.GetConfig<Config>()?.MarkIds ?? [];

        await using var scope = ctx.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IPathMarkSyncService>().EnqueueSync(markIds);

        return WorkflowItemOutcome.KeepItem;
    }
}
