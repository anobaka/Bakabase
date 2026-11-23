namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Opt-in retry after a previous whole-file checksum mismatch. Only the provider owns
/// the proof for replacing a partial checkpoint and reusing already verified members.</summary>
public interface ICloudDownloadChecksumRetryProvider
{
    Task<CloudDownloadResult> RetryChecksumMismatchAsync(CloudDownloadRequest request, DateTime failureRecordedAt,
        Func<CloudDownloadProgress, Task> progress, CancellationToken ct);
}
