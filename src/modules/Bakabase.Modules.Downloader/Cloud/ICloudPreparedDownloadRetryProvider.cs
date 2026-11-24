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

    /// <summary>Rechecks the original prepared source and downloads missing payload without preparing it again.</summary>
    Task<CloudDownloadResult> RetryPreparedDownloadAsync(CloudDownloadRequest request,
        Func<CloudDownloadProgress, Task> progress, CancellationToken ct);
}
