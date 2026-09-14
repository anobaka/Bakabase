namespace Bakabase.Modules.Workflow.Abstractions.Models.Input;

public record WorkflowDefinitionCreationInputModel
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? DescriptionKey { get; set; }
    public string TriggerKind { get; set; } = null!;
    public string? TriggerFilterJson { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Activities in chain order (index = order).</summary>
    public List<WorkflowActivityInputModel> Activities { get; set; } = [];
}
