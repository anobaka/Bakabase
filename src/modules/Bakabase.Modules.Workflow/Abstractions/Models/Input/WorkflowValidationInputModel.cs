namespace Bakabase.Modules.Workflow.Abstractions.Models.Input;

public record WorkflowValidationInputModel
{
    public string TriggerKind { get; set; } = "";
    public string? TriggerFilterJson { get; set; }
    public List<WorkflowActivityInputModel> Activities { get; set; } = [];
}
