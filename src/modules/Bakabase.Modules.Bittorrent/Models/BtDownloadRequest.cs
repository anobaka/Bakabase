namespace Bakabase.Modules.Bittorrent.Models;

/// <summary>Internal capability request; ownership and idempotency are assigned by trusted callers.</summary>
public sealed record BtDownloadRequest
{
    public string? Name { get; init; }
    public string? MagnetUri { get; init; }
    public byte[]? Metadata { get; init; }
    public string? TorrentUrl { get; init; }
    public required string OutputDirectory { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromHours(24);
    public string? IdempotencyKey { get; init; }
    public string SourceKind { get; init; } = "manual";
    public string? SourceId { get; init; }
    public int? WorkflowRunId { get; init; }
    public int? WorkflowDefinitionId { get; init; }
    public int? DownloadTaskId { get; init; }
    public int? DownloadResultId { get; init; }
}
