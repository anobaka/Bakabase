namespace Bakabase.Modules.HttpDownloads.Models;

/// <summary>Internal capability request; callers assign ownership and idempotency.</summary>
public sealed record HttpDownloadRequest
{
    public required string Url { get; init; }
    public required string OutputDirectory { get; init; }
    public string? FileName { get; init; }
    public string? Name { get; init; }
    public string? HttpClientName { get; init; }
    public int ParallelConnections { get; init; } = 4;
    public int MaxRetries { get; init; } = 3;
    public long MaximumBytesPerSecond { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromHours(4);
    public string? IdempotencyKey { get; init; }
    public string SourceKind { get; init; } = "manual";
    public string? SourceId { get; init; }
    public int? WorkflowRunId { get; init; }
    public int? WorkflowDefinitionId { get; init; }
    public int? DownloadTaskId { get; init; }
    public int? DownloadResultId { get; init; }
}
