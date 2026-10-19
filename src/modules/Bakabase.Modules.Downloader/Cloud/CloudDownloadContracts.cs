namespace Bakabase.Modules.Downloader.Cloud;

public sealed record CloudSource
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    public string ProviderId { get; init; } = "unknown";
    public string Kind { get; init; } = "share";
    public string? AccessCode { get; init; }
    public string? ArchivePassword { get; init; }
    public string? ExtractionPlanJson { get; init; }
    public string? GroupId { get; init; }
}

public sealed record CloudReadiness(bool Available, string? Problem = null);
public sealed record CloudRemoteEntry(string ProviderId, string AccountId, string Path, string? ManifestJson = null);
public sealed record CloudTransferRequest(string OperationId, CloudSource Source, string AccountId, string RemoteDirectory);
public sealed record CloudTransferReceipt(string OperationId, CloudRemoteEntry Entry);
public sealed record CloudDownloadRequest(string OperationId, CloudSource Source, string? AccountId,
    CloudRemoteEntry? RemoteEntry, string OutputDirectory);
public sealed record CloudDownloadProgress(double? Percentage, long? CompletedBytes = null,
    long? TotalBytes = null, string? Stage = null);
public sealed record CloudDownloadResult(IReadOnlyList<string> Files);
public sealed class CloudProviderException(string message, bool mayHaveSideEffects = false) : InvalidOperationException(message)
{
    public bool MayHaveSideEffects { get; } = mayHaveSideEffects;
}

/// <summary>Provider identity describes the service; implementations own their optional execution tool.</summary>
public interface ICloudDownloadProvider
{
    string ProviderId { get; }
    bool SupportsTransfer => false;
    Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct);
    Task<CloudTransferReceipt> TransferAsync(CloudTransferRequest request, CancellationToken ct) =>
        throw new NotSupportedException("This provider does not support share transfers.");
    Task<CloudTransferReceipt?> ReconcileTransferAsync(CloudTransferRequest request, CancellationToken ct) =>
        Task.FromResult<CloudTransferReceipt?>(null);
    Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request,
        Func<CloudDownloadProgress, Task> progress, CancellationToken ct);
}
