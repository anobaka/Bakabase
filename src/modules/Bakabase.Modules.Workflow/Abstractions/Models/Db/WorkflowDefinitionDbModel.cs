using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Workflow.Abstractions.Models.Db;

public record WorkflowDefinitionDbModel
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = null!;

    /// <summary>Trigger kind, e.g. <c>subscription.updated</c>.</summary>
    public string TriggerKind { get; set; } = null!;

    /// <summary>Opaque per-trigger filter JSON; null = match all events of this kind.</summary>
    public string? TriggerFilterJson { get; set; }

    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastError { get; set; }
}
