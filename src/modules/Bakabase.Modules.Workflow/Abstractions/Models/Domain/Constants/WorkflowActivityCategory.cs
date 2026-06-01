namespace Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

/// <summary>
/// Bucketing for the editor UI — Filter shrinks the item list, Action has side effects,
/// Transform mutates items. The framework treats them identically (everyone implements
/// IWorkflowActivity.ProcessItemAsync); category is metadata only.
/// </summary>
public enum WorkflowActivityCategory
{
    Filter = 1,
    Action = 2,
    Transform = 3,
}
