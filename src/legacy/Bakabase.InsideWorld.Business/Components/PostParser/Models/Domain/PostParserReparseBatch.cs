using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

/// <summary>The complete failed-task selection confirmed by the user, independent of list pagination.</summary>
public sealed record ReParseFailedPostParserTasksInput
{
    public List<PostParserReparseSelection> Tasks { get; set; } = [];
}

public sealed record PostParserReparseSelection
{
    public int TaskId { get; set; }
    public int Revision { get; set; }
    public int? WorkflowRunId { get; set; }
}

public sealed record PostParserReparseBatchResult
{
    public int RequestedCount { get; set; }
    public int QueuedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public List<PostParserReparseBatchItem> Items { get; set; } = [];
}

public sealed record PostParserReparseBatchItem(int TaskId, string Status, int? WorkflowRunId = null,
    string? Reason = null);
