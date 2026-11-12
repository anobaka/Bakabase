using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

/// <summary>Lightweight identities selected with the same rules as the task list, across all pages.</summary>
public sealed record PostParserTaskSelectionSnapshot(int TaskId, int Revision, int? WorkflowRunId);

public sealed record PostParserTaskFilteredSelection(int MatchedCount, bool LimitExceeded,
    IReadOnlyList<PostParserTaskSelectionSnapshot> Tasks);
