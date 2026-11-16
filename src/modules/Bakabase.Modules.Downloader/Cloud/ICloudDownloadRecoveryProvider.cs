namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Optional, read-only recovery capability for failures known to precede payload writes.
/// The adapter must verify current original-file access and any retained manifest without transferring,
/// purchasing, modifying accounts, creating downloads or consuming file payloads.</summary>
public interface ICloudDownloadRecoveryProvider
{
    bool CanRevalidateDownloadFailure(string code);
    /// <summary>Local-only admission check, repeated under the native download action lock.
    /// Providers with private path-bound journals can veto even a zero-progress retry.</summary>
    Task<CloudReadiness> CheckLocalRecoveryAsync(CloudDownloadRequest request, CancellationToken ct) =>
        Task.FromResult(new CloudReadiness(true));
    Task<CloudReadiness> RevalidateDownloadAsync(CloudDownloadRequest request, CancellationToken ct);
}
