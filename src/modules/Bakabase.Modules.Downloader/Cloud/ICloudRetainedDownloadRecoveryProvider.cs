namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Explicit opt-in for continuing the original download with its retained files and
/// pinned remote receipt. Inspection must be read-only: no transfer, payload, account writes,
/// directory creation or synthesized checkpoints. The caller proves exclusive operation ownership.</summary>
public interface ICloudRetainedDownloadRecoveryProvider
{
    bool CanRevalidateRetainedDownloadFailure(string code);
    Task<CloudReadiness> InspectRetainedDownloadAsync(CloudDownloadRequest request, bool verifyRemote,
        CancellationToken ct);
}
