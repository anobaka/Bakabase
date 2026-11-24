namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Retries the original payload of an already verified provider preparation. Only a
/// user-authorized retry of the same native task can invoke this capability.</summary>
public interface ICloudPreparedDownloadRetryProvider
{
    /// <summary>Additional failure codes eligible only when the saved preparation and local
    /// download journal can be proved. They never authorize a fresh preparation.</summary>
    bool CanRetryPreparedDownloadFailure(string failureCode) => false;

    /// <summary>Read-only local handling of the original journal and execution location.</summary>
    Task<CloudReadiness> CheckPreparedDownloadRetryAsync(CloudDownloadRequest request, CancellationToken ct);

    /// <summary>The provider can inspect durable payload checkpoints after a manual stop.</summary>
    bool SupportsPausedPreparedDownload => false;

    /// <summary>A manual pause with a provider-owned, committed payload checkpoint. Does not contact the provider.</summary>
    Task<CloudReadiness> CheckPausedPreparedDownloadAsync(CloudDownloadRequest request, CancellationToken ct) =>
        Task.FromResult(new CloudReadiness(false));

    /// <summary>This failure may be recovered solely by handing off an already sealed original.</summary>
    bool CanHandoffCompletedPreparedDownloadFailure(string failureCode) => false;

    Task<CloudReadiness> CheckCompletedPreparedDownloadAsync(CloudDownloadRequest request, CancellationToken ct) =>
        Task.FromResult(new CloudReadiness(false));

    /// <summary>Verifies and hands off the local payload without beginning or resuming a transfer.</summary>
    Task<CloudDownloadResult> HandoffCompletedPreparedDownloadAsync(CloudDownloadRequest request,
        Func<CloudDownloadProgress, Task> progress, CancellationToken ct) =>
        Task.FromException<CloudDownloadResult>(new InvalidOperationException("RECOVERY_DOWNLOAD_REVIEW_REQUIRED"));

    /// <summary>Rechecks the original prepared source and downloads missing payload without preparing it again.</summary>
    Task<CloudDownloadResult> RetryPreparedDownloadAsync(CloudDownloadRequest request,
        Func<CloudDownloadProgress, Task> progress, CancellationToken ct);
}
