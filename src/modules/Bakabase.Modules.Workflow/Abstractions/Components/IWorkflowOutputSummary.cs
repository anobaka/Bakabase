namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// A compact inspection-only representation used when an item's complete JSON exceeds the output
/// preview budget. Execution and suspension snapshots always keep the complete original item.
/// </summary>
public interface IWorkflowOutputSummary
{
    object ToWorkflowOutputSummary();
}
