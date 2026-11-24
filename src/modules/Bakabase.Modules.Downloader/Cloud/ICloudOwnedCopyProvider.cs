namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Proof of a provider-created, operation-exclusive share copy. A path alone is never proof of ownership.</summary>
public sealed record CloudOwnedCopyReceipt(string AccountId, string AccountVersion, string ContainerId, string ManifestFingerprint);
public sealed record CloudVerifiedLocalDownload(int DownloadTaskId, int DownloadResultId, string Directory,
    IReadOnlyList<string> Files, string Fingerprint)
{
    public IReadOnlyList<CloudVerifiedLocalFile> VerifiedFiles { get; init; } = [];
}
public sealed record CloudVerifiedLocalFile(string Path, long Length, string Sha256)
{
    public DateTime? LastWriteTimeUtc { get; init; }
    public string? ContentGeneration { get; init; }
}
public sealed record CloudOwnedCopyRelease(string Status, string? ReasonCode = null);
public sealed record CloudDownloadOwnedCopyView(int DownloadTaskId, string Status, string? ReasonCode, DateTime? UpdatedAt, bool CanRetry);

/// <summary>Optional account policy for temporary public-share copies. Both operations must be idempotent across
/// restarts. Release must recheck account consent and ownership, and reconcile a previously submitted release.</summary>
public interface ICloudOwnedCopyProvider
{
    /// <summary>Read only the configured account policy before claiming an operation. Prepare and release recheck consent.</summary>
    Task<bool> IsOwnedCopyEnabledAsync(CloudDownloadRequest request, CancellationToken ct);
    Task<CloudOwnedCopyReceipt?> PrepareOwnedCopyAsync(CloudDownloadRequest request, CancellationToken ct);
    Task<CloudOwnedCopyRelease> ReleaseOwnedCopyAsync(CloudDownloadRequest request, CloudOwnedCopyReceipt copy,
        CloudVerifiedLocalDownload local, CancellationToken ct);
}
