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

    /// <summary>
    /// Shipped with Bakabase rather than made by the user. Built-in definitions are seeded by name
    /// and are read-only in the editor — the way to change one is to copy it, which leaves the
    /// original able to gain steps in a later release without overwriting anyone's edits.
    /// </summary>
    public bool IsBuiltin { get; set; }
}
