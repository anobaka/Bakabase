namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Optional read-only proof that leaving an unsuccessful download will not hide an
/// uncertain remote write. The caller separately checks payloads, tasks, receipts and operation bindings.
/// Implementations must not create directories, authenticate, transfer, download or mutate journals.</summary>
public interface ICloudDownloadAbandonmentProvider
{
    Task<CloudReadiness> CheckCanAbandonAsync(CloudDownloadRequest request, CancellationToken ct);
}
