namespace Bakabase.Modules.HttpDownloads.Models;

/// <summary>A transfer snapshot. Source payloads and network credentials are never part of history responses.</summary>
public sealed record HttpDownloadJob
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public HttpDownloadStatus Status { get; init; }
    public string Stage { get; init; } = "Queued";
    public int? Percentage { get; init; }
    public long CompletedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public double? SpeedBytesPerSecond { get; init; }
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

    public bool CanStop => Status is HttpDownloadStatus.Queued or HttpDownloadStatus.Running;
    public bool CanResume => Status == HttpDownloadStatus.Stopped;
    public bool CanRetry => Status == HttpDownloadStatus.Failed;
    public bool CanDelete => !IsLinked && SourceKind == "manual" && SourceId == null && WorkflowRunId == null && WorkflowDefinitionId == null &&
                             DownloadTaskId == null && DownloadResultId == null &&
                             Status is HttpDownloadStatus.Stopped or HttpDownloadStatus.Completed or HttpDownloadStatus.Failed;
}
