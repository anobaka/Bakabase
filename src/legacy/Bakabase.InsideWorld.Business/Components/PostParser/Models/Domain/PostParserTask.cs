using System.Collections.Generic;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

public record PostParserTask
{
    public int Id { get; set; }
    public PostParserSource Source { get; set; }
    public string Link { get; set; } = null!;
    public string? Title { get; set; }
    public string? Content { get; set; }
    public string? Text { get; set; }
    public int Revision { get; set; }
    public int? WorkflowDefinitionId { get; set; }
    public int? WorkflowRunId { get; set; }
    public WorkflowRunStatus? WorkflowStatus { get; set; }
    public List<PostParseTarget> Targets { get; set; } = [];
    [Newtonsoft.Json.JsonProperty(ItemConverterType = typeof(PostParserResultNodeConverter))]
    public Dictionary<PostParseTarget, JsonNode?>? Results { get; set; }
    public string? Error { get; set; }
    public bool IsDeleted { get; set; }
}
