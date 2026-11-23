namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Opt-in verification of existing files after a failed download or completion registration. Implementations may read pinned
/// remote metadata and existing local bytes only: no payload fetch, transfer, account mutation,
/// file mutation or fallback to the downloader. Missing or ambiguous proof must fail closed.</summary>
public interface ICloudExistingDownloadVerificationProvider
{
    /// <summary>Recognizes a provider's persisted machine code, never an arbitrary upstream message.
    /// Opting in requires complete existing-file proof; it cannot authorize a download retry if
    /// inspection or verification fails. Registration failures retain their separate common guard.</summary>
    bool CanVerifyExistingDownloadFailure(string code) => false;

    Task<CloudReadiness> InspectExistingDownloadAsync(CloudDownloadRequest request, CancellationToken ct);
    /// <summary>Inspect the frozen file set without reading payload bytes. Existing providers can report readiness without inventory.</summary>
    async Task<CloudExistingDownloadInventory> InspectExistingFileInventoryAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        var readiness = await InspectExistingDownloadAsync(request, ct);
        return new(readiness.Available, readiness.Problem);
    }

    Task<CloudExistingDownloadVerification> VerifyExistingDownloadAsync(CloudDownloadRequest request,
        CancellationToken ct);
}

public sealed record CloudExistingDownloadInventory(bool Available, string? Problem = null, int? FileCount = null, long? TotalBytes = null,
    CloudVerificationLocation? Location = null);

/// <summary>One-based positions in the provider's frozen file list and in that member's checksum list.</summary>
public sealed record CloudVerificationLocation(int FileNumber, int FileCount, CloudChecksumMismatch? ChecksumMismatch = null);
public sealed record CloudChecksumMismatch(int BlockCount, int MismatchedBlockCount, int FirstBlockNumber, long FirstBlockBytes);

/// <summary>Full content verified against the original remote identity, never inferred from progress/size.</summary>
public sealed record CloudVerifiedExistingFile(string Path, long Length, string Sha256);

public sealed record CloudExistingDownloadVerification(IReadOnlyList<CloudVerifiedExistingFile> Files,
    string? PayloadRootDirectory = null);
