namespace Bakabase.Modules.Downloader.Models.Tasks;

/// <summary>A protocol-independent task snapshot. Provider execution state remains authoritative.</summary>
public sealed record DownloadTask
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Provider { get; init; }
    public required string ProviderTaskId { get; init; }
    public string? ParentId { get; init; }
    public int? SiteTaskId { get; init; }
    public int? ThirdPartyId { get; init; }
    public string? Key { get; init; }
    public required string Name { get; init; }
    public required string OutputDirectory { get; init; }
    public IReadOnlyList<string> Files { get; init; } = [];
    public required string Status { get; init; }
    public string? Stage { get; init; }
    public double? Progress { get; init; }
    public long? CompletedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public double? SpeedBytesPerSecond { get; init; }
    public string? Error { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string OriginKind { get; init; } = "manual";
    public string? OriginId { get; init; }
    public int? WorkflowRunId { get; init; }
    public int? WorkflowDefinitionId { get; init; }
    public int? DownloadResultId { get; init; }
    public IReadOnlyList<string> AvailableActions { get; init; } = [];
    public DownloadTaskExecution? Execution { get; init; }
}

public sealed record DownloadTaskExecution
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

public sealed record DownloadTaskActionResult
{
    public required string Id { get; init; }
    public bool Success { get; init; }
    public int Code { get; init; }
    public string? Error { get; init; }
    public DownloadTask? Task { get; init; }
}
