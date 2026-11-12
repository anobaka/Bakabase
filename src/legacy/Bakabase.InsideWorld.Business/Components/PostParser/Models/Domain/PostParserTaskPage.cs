using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

public sealed record PostParserTaskSearchInput
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Keyword { get; set; }
    public string? Status { get; set; }
    public string? ReasonCode { get; set; }
    public int? LocateTaskId { get; set; }
}

public sealed record PostParserTaskPage
{
    public List<PostParserTask> Items { get; set; } = [];
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int GlobalTotal { get; set; }
    public Dictionary<string, int> AttentionReasonCounts { get; set; } = [];
    public Dictionary<string, int> StatusCounts { get; set; } = [];
    public Dictionary<string, int> RunningStages { get; set; } = [];
    public int? RunningTaskId { get; set; }
    public int StartableCount { get; set; }
    public int PendingHandoffCount { get; set; }
}
