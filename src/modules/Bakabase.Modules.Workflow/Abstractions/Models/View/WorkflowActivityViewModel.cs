using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Abstractions.Models.View;

public record WorkflowActivityViewModel
{
    public int Id { get; set; }
    public int Order { get; set; }
    public string Kind { get; set; } = null!;
    public string ConfigJson { get; set; } = "{}";
    public WorkflowActivityErrorBehavior OnItemError { get; set; }

    public static WorkflowActivityViewModel From(WorkflowActivity a) => new()
    {
        Id = a.Id,
        Order = a.Order,
        Kind = a.Kind,
        ConfigJson = a.ConfigJson,
        OnItemError = a.OnItemError,
    };
}
