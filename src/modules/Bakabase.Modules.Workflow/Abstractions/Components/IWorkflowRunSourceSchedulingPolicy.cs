using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;

namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>Add an owner's execution limit without replacing a trigger's scheduling policy.
/// Sources identify the caller; they are not authorization or file ownership evidence.</summary>
public interface IWorkflowRunSourceSchedulingPolicy
{
    bool AppliesTo(string triggerKind, IReadOnlyList<WorkflowSourceRef> sources);
    BTaskHandlerBuilder Configure(BTaskHandlerBuilder builder);
    BTaskHandlerBuilder Configure(BTaskHandlerBuilder builder, string? planSnapshotJson, int? currentStepIndex) =>
        Configure(builder);
}
