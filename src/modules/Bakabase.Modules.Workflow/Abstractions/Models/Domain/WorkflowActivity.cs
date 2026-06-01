using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Abstractions.Models.Domain;

/// <summary>
/// Domain projection of a configured step. Note the type lives alongside
/// <c>IWorkflowActivity</c> (the implementation interface) — they're different concepts:
/// the interface is the activity TYPE; this record is one configured instance referencing
/// such a type by <see cref="Kind"/>.
/// </summary>
public record WorkflowActivity
{
    public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public int Order { get; set; }
    public string Kind { get; set; } = null!;
    public string ConfigJson { get; set; } = "{}";
    public WorkflowActivityErrorBehavior OnItemError { get; set; } = WorkflowActivityErrorBehavior.Fail;
}
