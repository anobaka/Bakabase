namespace Bakabase.Modules.Workflow.Abstractions.Models.Domain;

public record WorkflowDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string TriggerKind { get; set; } = null!;
    public string? TriggerFilterJson { get; set; }
    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastError { get; set; }

    public List<WorkflowActivity> Activities { get; set; } = [];
}
