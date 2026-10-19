namespace Bakabase.Modules.Bittorrent.Models;

/// <summary>A transfer snapshot. Source payloads and network credentials are never part of history responses.</summary>
public sealed record BtDownloadJob
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public BtDownloadStatus Status { get; init; }
    public string Stage { get; init; } = "Queued";
    public int Percentage { get; init; }
    public long? CompletedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public double? SpeedBytesPerSecond { get; init; }
    public int? Peers { get; init; }
    public required string OutputDirectory { get; init; }
    public IReadOnlyList<string> Files { get; init; } = [];
    public string? Error { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string SourceKind { get; init; } = "manual";
    public string? SourceId { get; init; }
    public int? WorkflowRunId { get; init; }
    public int? WorkflowDefinitionId { get; init; }
    public int? DownloadTaskId { get; init; }
    public int? DownloadResultId { get; init; }
    public bool IsLinked { get; init; }

    public bool CanStop => Status is BtDownloadStatus.Queued or BtDownloadStatus.Running;
    public bool CanResume => Status == BtDownloadStatus.Stopped;
    public bool CanRetry => Status == BtDownloadStatus.Failed;
    public bool CanDelete => !IsLinked && SourceKind == "manual" && SourceId == null && WorkflowRunId == null &&
                             DownloadTaskId == null && DownloadResultId == null &&
                             Status is BtDownloadStatus.Stopped or BtDownloadStatus.Completed or BtDownloadStatus.Failed;
}
