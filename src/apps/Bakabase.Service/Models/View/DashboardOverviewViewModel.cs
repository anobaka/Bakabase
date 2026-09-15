using System.Collections.Generic;

namespace Bakabase.Service.Models.View;

/// <summary>Bounded homepage data. Counts describe stored records, without scanning files or evaluating rules.</summary>
public record DashboardOverviewViewModel
{
    public int TotalResourceCount { get; init; }
    /// <summary>Resources with a nonempty local path; this does not probe current file availability.</summary>
    public int LocalResourceCount { get; init; }
    /// <summary>Resources without a local path, whether or not an acquisition source is known.</summary>
    public int PendingResourceCount { get; init; }
    public int CollectionCount { get; init; }
    public int MediaLibraryCount { get; init; }
    /// <summary>Added since Monday midnight in the server's local timezone, up to this snapshot.</summary>
    public int ThisWeekAddedCount { get; init; }
    /// <summary>At most six libraries, ordered by distinct existing resource count, including empty libraries.</summary>
    public List<DashboardMediaLibraryViewModel> MediaLibraries { get; init; } = [];
    public DashboardWorkflowsViewModel Workflows { get; init; } = new();
}

public record DashboardMediaLibraryViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int ResourceCount { get; init; }
}

public record DashboardWorkflowsViewModel
{
    /// <summary>Pending and running workflow executions, including executions owned by other modules.</summary>
    public int RunningCount { get; init; }
    public int WaitingCount { get; init; }
    /// <summary>Failed or interrupted executions completed in the last seven days; falls back to StartedAt for legacy rows.</summary>
    public int FailedRecentlyCount { get; init; }
}
