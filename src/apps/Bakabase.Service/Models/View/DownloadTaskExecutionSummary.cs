namespace Bakabase.Service.Models.View;

/// <summary>Projection of source results and their independently owned executions.</summary>
public sealed record DownloadTaskExecutionSummary
{
    public int DownloadTaskId { get; init; }
    public string Status { get; init; } = "preparing";
    public int TotalCount { get; init; }
    public int CompletedCount { get; init; }
    public int RunningCount { get; init; }
    public int QueuedCount { get; init; }
    public int StoppedCount { get; init; }
    public int FailedCount { get; init; }
    public int WaitingCount { get; init; }
    public bool SourceCompleted { get; init; }
    public double? Progress { get; init; }
    public double? SpeedBytesPerSecond { get; init; }
}
