using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

public sealed record PostParserTaskDeletionSelection
{
    public int TaskId { get; set; }
    public int Revision { get; set; }
    public int? WorkflowRunId { get; set; }
}

public sealed record PostParserTaskDeletionPreview
{
    public PostParserTaskFilterScope Scope { get; set; } = new();
    public int Count { get; set; }
    public List<PostParserTaskDeletionSelection> Tasks { get; set; } = [];
}

/// <summary>A fixed cross-page selection. Confirmation never adds newly matching tasks.</summary>
public sealed record ConfirmPostParserTaskDeletionInput
{
    public PostParserTaskFilterScope Scope { get; set; } = null!;
    public List<PostParserTaskDeletionSelection> Tasks { get; set; } = [];
}

public sealed record PostParserTaskDeletionResult
{
    public int RequestedCount { get; set; }
    public int DeletedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<PostParserTaskDeletionOutcome> Items { get; set; } = [];
}

public sealed record PostParserTaskDeletionOutcome(int TaskId, string Outcome, string? Reason = null);
