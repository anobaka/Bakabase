using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Modules.Workflow.Components;

public sealed class WorkflowActivityRuntimeRegistry(IEnumerable<IWorkflowActivityRuntimeProvider> providers,
    IWorkflowActivityRegistry? activities = null)
{
    public async ValueTask<WorkflowActivityRuntimeSnapshot> ReadAsync(WorkflowActivityRuntimeContext context,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var provider = providers.FirstOrDefault(p => p.CanHandle(context.ActivityKind));
        if (provider != null) return await provider.ReadAsync(context, ct);
        // Activities already own their names; an unfamiliar capability needs no orchestration
        // special case. A generic running workflow does not prove a capability acquired a slot.
        var activity = activities?.Get(context.ActivityKind);
        var live = context.CurrentProgress;
        return new()
        {
            Capability = activity == null ? null : new(activity.Kind, activity.DisplayName),
            Stage = live?.Stage,
            Progress = live?.StageProgress is >= 0 and <= 100 ? live.StageProgress : null,
            ProgressScope = live?.StageProgress is >= 0 and <= 100 ? "activity" : null
        };
    }
}
