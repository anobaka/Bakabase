namespace Bakabase.Modules.Workflow.Abstractions.Models.Input;

public record WorkflowDefinitionUpdateInputModel
{
    public string? Name { get; set; }
    public string? TriggerFilterJson { get; set; }
    public bool? Enabled { get; set; }

    /// <summary>When non-null, replace the entire activity chain with this list.</summary>
    public List<WorkflowActivityInputModel>? Activities { get; set; }
}
