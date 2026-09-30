using Bakabase.Abstractions.Models.Db;

namespace Bakabase.Abstractions.Models.View;

public record ResourceMoveBatchDetailViewModel
{
    public string BatchId { get; set; } = null!;
    public string TaskId { get; set; } = null!;
    public string? Origin { get; set; }
    public string? SourceTabId { get; set; }
    public string? SourceTabName { get; set; }
    public string DestDir { get; set; } = null!;
    public string? DestinationId { get; set; }
    public string? DestinationName { get; set; }
    public string Status { get; set; } = null!;
    public string ConflictPolicy { get; set; } = "inherit";
    public int Percentage { get; set; }
    public bool CancelRequested { get; set; }
    public bool CanCancel { get; set; }
    public bool CanRetry { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int[] ResourceIds { get; set; } = [];
    public int[] LockedResourceIds { get; set; } = [];
    public string[] ReservedPaths { get; set; } = [];
    public ResourceMoveBatchCounts Counts { get; set; } = new();
    public List<ResourceMoveRecordDbModel> Records { get; set; } = [];
}

public record ResourceMoveBatchCounts
{
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Cancelled { get; set; }
    public int Skipped { get; set; }
    public int Waiting { get; set; }
}
