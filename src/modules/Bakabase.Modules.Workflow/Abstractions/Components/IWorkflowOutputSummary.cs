namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// An explicit, safe inspection-only representation. Never include credentials, signed URLs or
/// serialized execution objects. The complete item is never serialized as a display fallback.
/// Execution and suspension snapshots keep the complete original item independently.
/// </summary>
public interface IWorkflowOutputSummary
{
    object ToWorkflowOutputSummary();
}
