namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>A resumable operation needs a stable chain until its existing consumers finish.</summary>
public interface IWorkflowCheckpointedActivity { }

/// <summary>A step that preserves artifact requirements while an upstream materializer plans ahead.</summary>
public interface IWorkflowArtifactTransparentActivity : IWorkflowCheckpointedActivity
{
    bool PreservesArtifactDemand(string configJson) => true;
}

/// <summary>
/// A typed consumer can satisfy its demand from verified durable artifacts before an upstream
/// materializer runs. Planning is optional and read-only with respect to the input files.
/// Unknown steps are barriers: no assumption is made about their side effects or item transforms.
/// </summary>
public interface IWorkflowArtifactDemandActivity : IWorkflowCheckpointedActivity
{
    Task<object> TrySatisfyAsync(WorkflowExecutionContext context, object item, string configJson,
        CancellationToken ct);
}

public sealed record WorkflowPlannedActivity(string Kind, string ConfigJson);

public static class WorkflowArtifactPlanner
{
    public static async Task<object> SatisfyDownstreamAsync(WorkflowExecutionContext context, object item,
        CancellationToken ct)
    {
        if (context.Services.GetService(typeof(IWorkflowActivityRegistry)) is not IWorkflowActivityRegistry registry)
            return item;
        for (var index = 0; index < context.FollowingActivities.Count; index++)
        {
            var step = context.FollowingActivities[index];
            var activity = registry.Get(step.Kind);
            if (activity is IWorkflowArtifactDemandActivity demand)
                return await demand.TrySatisfyAsync(context.ForPlannedActivity(step.ConfigJson,
                    context.FollowingActivities.Skip(index + 1).ToList()), item, step.ConfigJson, ct);
            if (activity is not IWorkflowArtifactTransparentActivity transparent ||
                !transparent.PreservesArtifactDemand(step.ConfigJson)) break;
        }
        return item;
    }
}
