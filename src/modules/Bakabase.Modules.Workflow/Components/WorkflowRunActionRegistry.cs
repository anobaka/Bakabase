using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Modules.Workflow.Components;

/// <summary>
/// Collects capability-owned recovery actions for a run. This registry only
/// discovers actions; preview/apply remains with the provider that owns the
/// capability and its persisted evidence.
/// </summary>
public sealed class WorkflowRunActionRegistry(IEnumerable<IWorkflowRunActionProvider> providers)
{
    public async ValueTask<IReadOnlyList<WorkflowRunActionDescriptor>> DescribeAsync(
        WorkflowRunActionContext context, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var actions = new List<WorkflowRunActionDescriptor>();
        foreach (var provider in providers)
        {
            ct.ThrowIfCancellationRequested();
            if (!provider.CanHandle(context)) continue;
            var described = await provider.DescribeAsync(context, ct);
            if (described.Count != 0) actions.AddRange(described);
        }

        // A provider owns its action ids. Keep the first descriptor when a
        // duplicate is accidentally registered so clients never see an
        // ambiguous operation.
        return actions
            .Where(action => !string.IsNullOrWhiteSpace(action.Id) && !string.IsNullOrWhiteSpace(action.CapabilityId))
            .GroupBy(action => action.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }
}
