namespace Bakabase.Modules.Downloader.Cloud;

public sealed record CloudSource
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    /// <summary>Original locator when a reviewed resolver expanded it. Url remains the execution identity.</summary>
    public string? OriginalUrl { get; init; }
    public List<string> ResolutionChain { get; init; } = [];
    public string ProviderId { get; init; } = "unknown";
    public string? MethodId { get; init; }
    /// <summary>Server-defined artifact scope for engines whose receipts cannot reuse a legacy method's outputs.</summary>
    public string? MethodScope { get; init; }
    public string? AccountId { get; init; }
    /// <summary>Configuration generation, never credentials. Pins account-backed reuse to the configured identity.</summary>
    public string? AccountVersion { get; init; }
    public string Kind { get; init; } = "share";
    public string? AccessCode { get; init; }
    /// <summary>Append-only, task-local corrections. Original locators and archive passwords remain unchanged.</summary>
    public List<CloudAccessCodeRevision>? AccessCodeRevisions { get; init; }
    public string? ArchivePassword { get; init; }
    public string? ExtractionPlanJson { get; init; }
    public string? GroupId { get; init; }
    /// <summary>Optional provider version/ETag; absent means reuse verified, previously acquired content.</summary>
    public string? Version { get; init; }
}

public sealed record CloudAccessCodeRevision
{
    public required string CorrectionId { get; init; }
    public required string ExpectedSourceIdentity { get; init; }
    public string? PreviousAccessCode { get; init; }
    /// <summary>Reviewed pre-write failure which authorized this revision, never a raw provider diagnostic.</summary>
    public string? PreviousFailureCode { get; init; }
    public string? NewAccessCode { get; init; }
    public required string Basis { get; init; }
    public string? EvidenceReference { get; init; }
    public string? Evidence { get; init; }
    public DateTime AppliedAt { get; init; }
}

public sealed record CloudReadiness(bool Available, string? Problem = null)
{
    /// <summary>The provider verified that the selected input fits this account after other files are released.</summary>
    public bool CanWaitForCapacity { get; init; }
}
public sealed record CloudRemoteEntry(string ProviderId, string AccountId, string Path, string? ManifestJson = null)
{
    /// <summary>A provider-issued reference, not proof by itself; download must verify its durable transfer receipt.</summary>
    public CloudOwnedRemoteContainer? OwnedContainer { get; init; }
}
public sealed record CloudOwnedRemoteContainer(string TransferOperationId, string Path);
public sealed record CloudTransferRequest(string OperationId, CloudSource Source, string AccountId, string RemoteDirectory);
public sealed record CloudTransferReceipt(string OperationId, CloudRemoteEntry Entry);
public sealed record CloudDownloadRequest(string OperationId, CloudSource Source, string? AccountId,
    CloudRemoteEntry? RemoteEntry, string OutputDirectory)
{
    /// <summary>Provider keys proven reusable during admission. Only this invocation can supply them.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string>? ReadOnlyProviderFileKeys { get; init; }
    /// <summary>Execution-only provider map identity. The workflow and storage reservation retain OperationId.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ProviderLayoutId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string LayoutOperationId => ProviderLayoutId ?? OperationId;
    [System.Text.Json.Serialization.JsonIgnore]
    public CloudDownloadReuseSource? ReuseSource { get; init; }
    /// <summary>Frozen caller placement for owned transport staging; null uses destination storage.</summary>
    public string? WorkspaceDirectory { get; init; }
    /// <summary>Reviewed physical placement revisions. OutputDirectory remains the frozen semantic input.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<CloudDownloadDestinationRevision>? DestinationRevisions { get; init; }
}
public sealed record CloudDownloadProgress(double? Percentage, long? CompletedBytes = null,
    long? TotalBytes = null, string? Stage = null);
public sealed record CloudDownloadResult(IReadOnlyList<string> Files)
{
    /// <summary>Optional content root after proving ownership of a transport wrapper. Physical files and checkpoints stay put.</summary>
    public string? PayloadRootDirectory { get; init; }
    /// <summary>Retire superseded transport scratch only after the site commits the exact local-files fingerprint.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Action<string>? OnLocalFilesCommitted { get; init; }
}
public sealed record CloudTransferDiagnostic(string Code, int? ProviderCode, int[] ItemCodes, int? FileCount, int? FileLimit);

public sealed class CloudProviderException(string message, bool mayHaveSideEffects = false) : InvalidOperationException(message)
{
    public bool MayHaveSideEffects { get; } = mayHaveSideEffects;
    /// <summary>Issued only at a checked capacity boundary before submission, never inferred from a remote error message.</summary>
    public bool CanWaitForCapacity { get; init; }
    public CloudVerificationLocation? VerificationLocation { get; init; }
    public CloudTransferDiagnostic? TransferDiagnostic { get; init; }
}

/// <summary>Provider identity describes the service; implementations own their optional execution tool.</summary>
public interface ICloudDownloadProvider
{
    string ProviderId { get; }
    /// <summary>Null is the legacy/default implementation. Explicit engines share the same site identity.</summary>
    string? MethodId => null;
    bool SupportsTransfer => false;
    /// <summary>True only for classified access failures known to occur before any remote write.</summary>
    bool CanCorrectAccessCodeAfterFailure(string code) => false;
    Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct);
    Task<CloudTransferReceipt> TransferAsync(CloudTransferRequest request, CancellationToken ct) =>
        throw new NotSupportedException("This provider does not support share transfers.");
    Task<CloudTransferReceipt?> ReconcileTransferAsync(CloudTransferRequest request, CancellationToken ct) =>
        Task.FromResult<CloudTransferReceipt?>(null);
    Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request,
        Func<CloudDownloadProgress, Task> progress, CancellationToken ct);
}
