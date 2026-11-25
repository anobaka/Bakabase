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

    /// <summary>Whether revalidation itself checks current provider and account readiness for the
    /// requested account and generation. Providers can do both under one account lease.</summary>
    bool RevalidationChecksReadiness => false;

    Task<CloudReadiness> RevalidateDownloadAsync(CloudDownloadRequest request, CancellationToken ct);
}

/// <summary>Read-only recheck for an already admitted original download waiting before its cloud copy is submitted.</summary>
public interface ICloudDownloadCapacityWaitProvider
{
    const string WaitStage = "WaitingForCloudStorageCapacity";

    Task<CloudReadiness> CheckWaitingCapacityAsync(CloudDownloadRequest request, CancellationToken ct);
}
