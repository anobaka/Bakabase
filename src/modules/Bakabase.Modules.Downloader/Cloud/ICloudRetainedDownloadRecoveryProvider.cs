namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Explicit opt-in for continuing the original download with its retained files and
/// pinned remote receipt. Inspection must be read-only: no transfer, payload, account writes,
/// directory creation or synthesized checkpoints. The caller proves exclusive operation ownership.</summary>
public interface ICloudRetainedDownloadRecoveryProvider
{
    bool CanRevalidateRetainedDownloadFailure(string code);
    /// <summary>Permits an explicit resume after a user stop when the provider can
    /// inspect the original local checkpoint and pinned remote manifest.</summary>
    bool SupportsExplicitStoppedCheckpointResume => false;

    /// <summary>Recognizes a structural inspection that found incomplete local files.
    /// Only an explicit download action may use this to consider retained recovery.</summary>
    bool CanResumeIncompleteDownload(string failureCode, string? inspectionProblem) => false;

    Task<CloudReadiness> InspectRetainedDownloadAsync(CloudDownloadRequest request, bool verifyRemote,
        CancellationToken ct);
}
