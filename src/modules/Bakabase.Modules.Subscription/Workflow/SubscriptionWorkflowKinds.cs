using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Modules.Subscription.Workflow;

/// <summary>
/// All workflow trigger / activity kind strings owned by the Subscription module.
/// Concrete <see cref="IWorkflowTrigger"/> / <see cref="IWorkflowActivity"/> implementations
/// reference these constants instead of hard-coding strings.
/// </summary>
public static class SubscriptionWorkflowKinds
{
    public const string Module = "subscription";

    public static readonly string TriggerUpdated = WorkflowTriggerKinds.Build(Module, "updated");
}
