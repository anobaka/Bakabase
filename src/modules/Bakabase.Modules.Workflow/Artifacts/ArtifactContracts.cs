namespace Bakabase.Modules.Workflow.Artifacts;

public enum ArtifactLocationState
{
    Available = 1, Quarantined = 2, Missing = 3, Modified = 4, StorageUnavailable = 5,
    /// <summary>Intentionally removed after verified delivery. The immutable manifest remains
    /// inspectable, but locating files again must never revive this artifact or its operation.</summary>
    Retired = 6
}
public enum ArtifactOperationState { Planned = 1, Running = 2, Completed = 3, Failed = 4, Unknown = 5 }
public enum ArtifactClaimStatus { Acquired = 1, Busy = 2, Completed = 3, NeedsReconciliation = 4, StorageUnavailable = 5 }

public sealed record ArtifactManifestEntry(string RelativePath, long Length, string Sha256);
// Directories records intentional empty leaves, not a discovery rule for sibling content.
// Null/empty preserves the historical exact-file fingerprint and legacy replay semantics.
public sealed record ArtifactManifest(IReadOnlyList<ArtifactManifestEntry> Entries, string Fingerprint,
    IReadOnlyList<string>? Directories = null);
public sealed record ArtifactValidation(ArtifactLocationState State, string? Message = null)
{
    public bool Available => State == ArtifactLocationState.Available;
}

/// <summary>Keys describe semantic inputs, never workflow run identities or plaintext credentials.</summary>
public sealed record ArtifactOperationRequest(string OperationKey, string IntentKey, string Kind,
    string InputFingerprint, bool Restartable = true, bool AllowRetry = true);
public sealed record ArtifactOperationLease(string OperationId, string Token);
public sealed record ArtifactOperationClaim(ArtifactClaimStatus Status, string OperationId,
    ArtifactOperationLease? Lease = null, ArtifactRecord? Artifact = null,
    // Set only by the successful generation-changing CAS, never by a caller's prior read.
    [property: System.Text.Json.Serialization.JsonIgnore] bool StartedNewAttempt = false);

/// <summary>Register an exact, already published file set. Registration never adopts sibling files.</summary>
public sealed record ArtifactRegistration(string RootDirectory, IReadOnlyList<string> Files,
    string Kind = "files", bool Managed = false, IReadOnlyList<string>? ParentArtifactIds = null,
    string? MetadataJson = null, string? ExpectedManifestFingerprint = null,
    IReadOnlyList<string>? Directories = null,
    // Trusted in-process adapter metadata, evaluated against the exact manifest captured by the store.
    [property: System.Text.Json.Serialization.JsonIgnore]
    Func<ArtifactManifest, string?>? CreateMetadata = null)
{
    // Previous complete native-receipt verification from this same request, never a client hint.
    [System.Text.Json.Serialization.JsonIgnore] public ArtifactManifest? VerifiedDownloadFiles { get; init; }
}

public sealed record ArtifactRecord(string Id, string OperationId, string Kind, string RootDirectory,
    IReadOnlyList<string> Files, ArtifactManifest Manifest, string Fingerprint, ArtifactLocationState State,
    bool Managed, IReadOnlyList<string> ParentArtifactIds, string? MetadataJson);

/// <summary>An exclusive maintenance operation may admit waiting registrations at a durable checkpoint.
/// A successful handoff invalidates the caller's prior reference check. Other maintenance remains excluded.</summary>
public interface IArtifactMaintenanceHandoff : IAsyncDisposable
{
    Task<bool> YieldToWaitingMutationsAsync(CancellationToken ct);
}

/// <summary>
/// Opt-in operation persistence for activities that can describe and validate reusable outputs.
/// Operation execution is independent of its workflow consumers; this is not a cache for all nodes.
/// </summary>
public interface IArtifactOperationStore
{
    /// <summary>Excludes new claims, registrations, consumer attachment and relocations until reclamation finishes.</summary>
    Task<IAsyncDisposable> AcquireMaintenanceAsync(CancellationToken ct = default) =>
        throw new NotSupportedException("This artifact store does not support workspace maintenance.");
    /// <summary>Excludes mutations affecting these individually registered publication files.</summary>
    Task<IAsyncDisposable> AcquirePublishedFilesMaintenanceAsync(string artifactId, string root, string fingerprint,
        IReadOnlyList<string> files, CancellationToken ct = default) => AcquireMaintenanceAsync(ct);
    /// <summary>Includes current manifests and roots fenced by an in-progress quarantine/restore.</summary>
    Task<bool> HasRegisteredFilesAsync(IReadOnlyList<string> files, CancellationToken ct = default) =>
        throw new NotSupportedException("This artifact store does not support workspace maintenance.");
    /// <summary>Checks exact file references while excluding the one source artifact being permanently retired.</summary>
    Task<bool> HasOtherRegisteredFilesAsync(IReadOnlyList<string> files, string excludedArtifactId, CancellationToken ct = default) =>
        throw new NotSupportedException("This artifact store does not support permanent source cleanup.");
    /// <summary>Excludes additional server-proven read-only views of the same owner's files.
    /// The caller must recheck their ownership and consumers under the maintenance fence.</summary>
    Task<bool> HasOtherRegisteredFilesAsync(IReadOnlyList<string> files, string excludedArtifactId,
        IReadOnlyList<string> excludedViewIds, CancellationToken ct = default) =>
        HasOtherRegisteredFilesAsync(files, excludedArtifactId, ct);
    Task<ArtifactRecord?> ResolveAsync(string intentKey, CancellationToken ct = default);
    Task<ArtifactRecord?> ResolveByIdAsync(string artifactId, CancellationToken ct = default);
    Task<ArtifactRecord?> GetArtifactAsync(string artifactId, CancellationToken ct = default);
    /// <summary>Resolves the registered directory without enumerating the artifact's files.</summary>
    async Task<string?> GetArtifactDirectoryAsync(string artifactId, CancellationToken ct = default) =>
        (await GetArtifactAsync(artifactId, ct))?.RootDirectory;
    Task<ArtifactOperationDbModel?> GetOperationAsync(string operationId, CancellationToken ct = default);
    Task<ArtifactOperationClaim> ClaimAsync(ArtifactOperationRequest request, string ownerId,
        TimeSpan leaseDuration, CancellationToken ct = default);
    /// <summary>Resume only this existing attempt. A changed/failed generation must never become a retry.</summary>
    Task<ArtifactOperationClaim> ClaimSameAttemptAsync(ArtifactOperationRequest request, int expectedAttempt,
        string ownerId, TimeSpan leaseDuration, CancellationToken ct = default) =>
        throw new NotSupportedException("This artifact store does not support an attempt-bound continuation.");
    /// <summary>Reopen a provider-verified, restartable failed attempt owned by the same consumer. Never allocate another generation.</summary>
    Task<ArtifactOperationClaim> ReopenFailedAttemptAsync(ArtifactOperationRequest request, int expectedAttempt,
        string expectedError, string ownerId, TimeSpan leaseDuration, CancellationToken ct = default) =>
        throw new NotSupportedException("This artifact store does not support reopening a verified failed checkpoint.");
    Task<bool> RenewAsync(ArtifactOperationLease lease, TimeSpan leaseDuration, CancellationToken ct = default);
    Task<ArtifactRecord> CompleteAsync(ArtifactOperationLease lease, ArtifactRegistration registration,
        CancellationToken ct = default);
    Task<bool> FailAsync(ArtifactOperationLease lease, string error, bool outcomeUnknown = false,
        CancellationToken ct = default);
    Task<ArtifactRecord> AdoptAsync(ArtifactOperationRequest request, ArtifactRegistration registration,
        string ownerId, CancellationToken ct = default);
    Task<bool> AttachConsumerAsync(string operationId, string consumerKey, TimeSpan leaseDuration,
        CancellationToken ct = default);
    /// <summary>Extends an existing live, unreleased consumer without waiting for maintenance.
    /// Must never create a consumer or revive one that cleanup already considers inactive.</summary>
    Task<bool> RenewExistingConsumerAsync(string operationId, string consumerKey, TimeSpan leaseDuration,
        CancellationToken ct = default);
    /// <summary>Keep a consumer alive while its durable owner is active; currently supports workflow runs.</summary>
    Task<bool> PinConsumerAsync(string operationId, string consumerKey, string ownerKind, string ownerId,
        CancellationToken ct = default);
    Task ReleaseConsumerAsync(string operationId, string consumerKey, CancellationToken ct = default);
    Task<bool> HasActiveConsumersAsync(string operationId, CancellationToken ct = default);
    /// <summary>Atomically refuses active consumers and blocks new consumers before relocation.</summary>
    Task<bool> TryQuarantineAsync(string artifactId, CancellationToken ct = default);
    /// <summary>Fence a verified independent processing boundary. Only the named workflow's
    /// pins on the distinct original download may survive. Other stores retain their normal fence.</summary>
    Task<bool> TryQuarantineProcessingBoundaryAsync(string artifactId, string fingerprint,
        string downloadId, int retainedByRunId, CancellationToken ct) => TryQuarantineAsync(artifactId, ct);
    /// <summary>Update a quarantined artifact's location; restoring Available verifies every hash first.</summary>
    Task SetLocationAsync(string artifactId, string rootDirectory, ArtifactLocationState state,
        CancellationToken ct = default);
}
