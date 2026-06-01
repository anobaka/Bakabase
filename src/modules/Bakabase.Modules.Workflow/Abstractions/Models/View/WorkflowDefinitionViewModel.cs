using Bakabase.Modules.Workflow.Abstractions.Models.Domain;

namespace Bakabase.Modules.Workflow.Abstractions.Models.View;

public record WorkflowDefinitionViewModel
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

    public List<WorkflowActivityViewModel> Activities { get; set; } = [];

    public static WorkflowDefinitionViewModel From(WorkflowDefinition d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        TriggerKind = d.TriggerKind,
        TriggerFilterJson = d.TriggerFilterJson,
        Enabled = d.Enabled,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
        LastRunAt = d.LastRunAt,
        LastError = d.LastError,
        Activities = d.Activities.Select(WorkflowActivityViewModel.From).ToList(),
    };
}
