using Bakabase.Modules.Workflow.Abstractions.Models.Domain;

namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// Opt-in safe inspection projection. Describe only the outputs relevant to this activity;
/// the runner never serializes arbitrary items to obtain node metadata. This is not a replay
/// snapshot, a cache key or proof that an artifact still exists.
/// </summary>
public interface IWorkflowOutputMetadata
{
    IReadOnlyList<WorkflowOutputBlock> GetWorkflowOutputMetadata(string activityKind);
}
