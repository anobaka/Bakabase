using System;
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
    public Bakabase.Modules.PostParser.Models.Domain.PostContent? ContentSnapshot { get; set; }
    public Bakabase.Modules.PostParser.Models.Domain.PostAvailabilityAssessment? Availability { get; set; }
    public string? ParsingState { get; set; }
    public string? ParsingMessage { get; set; }
    public int AutoBuyThreshold { get; set; }
    public int MinimumRemainingCoins { get; set; }
    public PostParserPurchaseQuote? PurchaseQuote => ContentSnapshot == null ? null :
        PostParserPurchaseQuote.Create(ContentSnapshot, AutoBuyThreshold, MinimumRemainingCoins);
    public string? Text { get; set; }
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime? CreatedAt { get; set; }
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime? CompletedAt { get; set; }
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
