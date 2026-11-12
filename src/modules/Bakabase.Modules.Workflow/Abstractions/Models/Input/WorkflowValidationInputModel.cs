namespace Bakabase.Modules.Workflow.Abstractions.Models.Input;

public record WorkflowValidationInputModel
{
    public List<WorkflowParameterDefinition> Parameters { get; set; } = [];
    [Newtonsoft.Json.JsonProperty(ItemConverterType = typeof(WorkflowJsonElementConverter))]
    public Dictionary<string, System.Text.Json.JsonElement>? ParameterValues { get; set; }

    public string TriggerKind { get; set; } = "";
    public string? TriggerFilterJson { get; set; }
    public List<WorkflowActivityInputModel> Activities { get; set; } = [];
}
