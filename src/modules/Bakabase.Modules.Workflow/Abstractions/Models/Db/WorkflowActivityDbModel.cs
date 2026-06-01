using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Abstractions.Models.Db;

/// <summary>
/// A configured step inside a <see cref="WorkflowDefinitionDbModel"/>. References an
/// activity implementation by <see cref="Kind"/> and stores its private config.
/// </summary>
public record WorkflowActivityDbModel
{
    [Key] public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }

    /// <summary>0-indexed position in the chain.</summary>
    public int Order { get; set; }

    public string Kind { get; set; } = null!;

    /// <summary>Opaque, parsed by the activity implementation.</summary>
    public string ConfigJson { get; set; } = "{}";

    /// <summary>Per-step policy on item-level failure.</summary>
    public WorkflowActivityErrorBehavior OnItemError { get; set; } = WorkflowActivityErrorBehavior.Fail;
}
