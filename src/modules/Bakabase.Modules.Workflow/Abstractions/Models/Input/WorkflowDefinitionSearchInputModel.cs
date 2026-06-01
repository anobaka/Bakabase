namespace Bakabase.Modules.Workflow.Abstractions.Models.Input;

public record WorkflowDefinitionSearchInputModel
{
    public string? TriggerKind { get; set; }
    public bool? EnabledOnly { get; set; }
}
