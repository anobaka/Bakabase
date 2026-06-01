namespace Bakabase.Modules.Workflow.Abstractions.Models.Input;

public record WorkflowRunSearchInputModel
{
    public int? WorkflowDefinitionId { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 30;
}
