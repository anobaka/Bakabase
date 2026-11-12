using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;

namespace Bakabase.Modules.Workflow.Components;

public sealed class WorkflowRunSchedulingPolicyResolver(IEnumerable<IWorkflowRunSchedulingPolicy> policies,
    IEnumerable<IWorkflowRunSourceSchedulingPolicy>? sourcePolicies = null)
{
    private IWorkflowRunSchedulingPolicy? Find(string triggerKind) =>
        policies.FirstOrDefault(policy => policy.AppliesTo(triggerKind));

    public bool ResumeOnStartup(string triggerKind) => Find(triggerKind)?.ResumeOnStartup ?? true;

    public BTaskHandlerBuilder Configure(BTaskHandlerBuilder builder, int definitionId, string triggerKind,
        int? runId = null, IReadOnlyList<WorkflowSourceRef>? sources = null)
    {
        var configured = Find(triggerKind)?.Configure(builder) ?? builder.ConflictsWith(definitionId > 0
            ? $"workflow.definition.{definitionId}" : $"workflow.run.{runId ?? 0}");
        // BTask currently has one concurrency group. Never replace a source trigger's existing
        // group, and retain its conflict keys, dependencies and retry/shutdown behavior.
        if (configured.ConcurrencyGroup != null || sources is not {Count: > 0}) return configured;
        foreach (var policy in sourcePolicies ?? [])
        {
            if (!policy.AppliesTo(triggerKind, sources)) continue;
            configured = policy.Configure(configured);
            if (configured.ConcurrencyGroup != null) break;
        }
        return configured;
    }
}
