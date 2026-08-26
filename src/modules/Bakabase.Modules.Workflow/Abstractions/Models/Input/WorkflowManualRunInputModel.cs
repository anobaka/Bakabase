namespace Bakabase.Modules.Workflow.Abstractions.Models.Input;

public record WorkflowManualRunInputModel
{
    /// <summary>
    /// The payload for this run, as JSON, in whatever shape the definition's trigger asks for
    /// (<c>IWorkflowTrigger.BuildManualPayload</c>). Null for a trigger that reads its inputs
    /// from the definition itself.
    /// </summary>
    public string? ArgsJson { get; set; }
}
