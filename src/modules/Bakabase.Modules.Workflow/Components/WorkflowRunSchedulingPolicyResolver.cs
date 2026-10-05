using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Modules.Workflow.Components;

public sealed class WorkflowRunSchedulingPolicyResolver(IEnumerable<IWorkflowRunSchedulingPolicy> policies)
{
    private IWorkflowRunSchedulingPolicy? Find(string triggerKind) =>
        policies.FirstOrDefault(policy => policy.AppliesTo(triggerKind));

    public bool ResumeOnStartup(string triggerKind) => Find(triggerKind)?.ResumeOnStartup ?? true;

    public BTaskHandlerBuilder Configure(BTaskHandlerBuilder builder, int definitionId, string triggerKind) =>
        Find(triggerKind)?.Configure(builder) ?? builder.ConflictsWith($"workflow.definition.{definitionId}");
}
