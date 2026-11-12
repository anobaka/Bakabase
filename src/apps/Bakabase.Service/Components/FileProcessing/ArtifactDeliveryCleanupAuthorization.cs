using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>A server-created receipt, never a client-supplied list of files or owners.</summary>
public sealed record ArtifactDeliveryCleanupConsent(string Id, DateTimeOffset ConfirmedAt, int TaskId,
    int PlanRevision, int CompletedRunId, bool DeliveryAccepted, IReadOnlyList<int> OwnerRunIds,
    IReadOnlyList<ArtifactAcceptedPublication> Publications, bool SkipFileVerification = false, bool DeleteDeliveredFiles = false);
public sealed record ArtifactAcceptedPublication(string ArtifactId, string OperationId, string Fingerprint,
    string RootDirectory);

internal sealed record ArtifactCleanupRetirementDependency(string ArtifactId, string OperationId,
    string Fingerprint, string OriginalRoot, string SelectionFingerprint, string PublicationId);

/// <summary>
/// An explicit, fixed deletion confirmation may override old retention preferences. Accepting
/// delivery replaces only the requirement to find final bytes at their old location: lineage,
/// registered inputs, other consumers and filesystem fences still apply. Skipping byte checks
/// requires its separate explicit flag and replaces content hashes with request-local file identities.
/// </summary>
internal sealed class ArtifactDeliveryCleanupAuthorization(
    ArtifactDeliveryCleanupConsent consent,
    Func<CancellationToken, Task<bool>> isCurrent,
    IArtifactOperationStore store,
    IReadOnlyList<int>? previewSupersededRuns = null,
    Func<string, CancellationToken, Task<bool>>? previewActiveConsumers = null,
    Func<ArtifactRecord, ArtifactRecord, CancellationToken, Task<bool>>? boundaryCoverage = null,
    Func<CancellationToken, Task<IReadOnlyList<string>>>? releasableViews = null)
{
    private readonly Dictionary<string, IReadOnlyList<ArtifactCleanupRetirementDependency>> retirementDependencies = new(StringComparer.Ordinal);
    internal IReadOnlyDictionary<string, IReadOnlyList<ArtifactCleanupRetirementDependency>> RetirementDependencies => retirementDependencies;
    internal void RestoreRetirementDependencies(IReadOnlyDictionary<string, IReadOnlyList<ArtifactCleanupRetirementDependency>>? captured)
    {
        retirementDependencies.Clear();
        foreach (var pair in captured ?? new Dictionary<string, IReadOnlyList<ArtifactCleanupRetirementDependency>>())
            retirementDependencies.Add(pair.Key, pair.Value.ToArray());
    }
    internal void RecordRetirementDependencies(string artifactId, IReadOnlyList<ArtifactCleanupRetirementDependency> prerequisites) =>
        retirementDependencies[artifactId] = prerequisites.ToArray();
    internal IReadOnlyList<ArtifactCleanupRetirementDependency> DependenciesFor(string artifactId) =>
        retirementDependencies.GetValueOrDefault(artifactId) ?? [];
    internal ArtifactDeliveryCleanupConsent Consent { get; } = consent;
    // These exceptions describe a read-only preview only. Execution constructs fresh
    // authorization without them after the fixed old runs were durably cancelled.
    internal IReadOnlyList<int> PreviewSupersededRuns { get; } = previewSupersededRuns ?? [];
    internal Task<bool> HasActiveConsumersAsync(string operationId, CancellationToken ct) =>
        previewActiveConsumers?.Invoke(operationId, ct) ?? store.HasActiveConsumersAsync(operationId, ct);

    internal async Task<IReadOnlyList<string>> ReleasableViewIdsAsync(CancellationToken ct) =>
        releasableViews != null && await isCurrent(ct) ? await releasableViews(ct) : [];

    internal async Task<bool> HasOtherRegisteredFilesAsync(IReadOnlyList<string> files, string sourceId, CancellationToken ct) =>
        await store.HasOtherRegisteredFilesAsync(files, sourceId, await ReleasableViewIdsAsync(ct), ct);

    internal async Task<bool> MatchesAsync(ArtifactRecord publication, CancellationToken ct)
    {
        var expected = Consent.Publications.SingleOrDefault(p => p.ArtifactId == publication.Id);
        if (expected == null || publication.Kind != "publishedFiles" ||
            publication.OperationId != expected.OperationId || publication.Fingerprint != expected.Fingerprint ||
            publication.Manifest.Fingerprint != expected.Fingerprint || publication.RootDirectory != expected.RootDirectory ||
            publication.State is ArtifactLocationState.Quarantined or ArtifactLocationState.Retired ||
            !await isCurrent(ct)) return false;
        var operation = await store.GetOperationAsync(publication.OperationId, ct);
        return operation is {Kind: "files.publish", State: ArtifactOperationState.Completed} &&
               operation.OutputArtifactId == publication.Id;
    }

    // A user may have manually reorganized the delivered files, so a historical per-file
    // replacement chain is not always complete. Only the fixed, accepted deletion request
    // with verification explicitly skipped may substitute for that proof. This does not
    // grant ownership or release references; callers must still enforce those separately.
    internal async Task<bool> SkipsReplacementProofAsync(ArtifactRecord input,
        IReadOnlyList<string> publicationIds, CancellationToken ct) =>
        Consent.DeliveryAccepted && Consent.SkipFileVerification && publicationIds.Count > 0 &&
        publicationIds.All(id => Consent.Publications.Any(p => p.ArtifactId == id)) &&
        await CoversAsync(input, ct);

    internal async Task<bool> CoversAsync(ArtifactRecord input, CancellationToken ct)
    {
        if (!input.Managed || input.Kind is not ("download" or "processedFiles")) return false;
        foreach (var expected in Consent.Publications)
        {
            var output = await store.GetArtifactAsync(expected.ArtifactId, ct);
            if (output == null || !await MatchesAsync(output, ct)) return false;
        }
        // Explicitly accepting the delivered files and skipping verification avoids content
        // scans. Immutable artifact, ownership and active-consumer checks remain; executors
        // bind selected private files to fresh request-local metadata before unlinking.
        if (Consent.SkipFileVerification) return true;
        if ((await ArtifactFileCoverage.EvaluateAsync(store, input,
                Consent.Publications.Select(p => p.ArtifactId).ToArray(), ct)).Complete) return true;
        // A registered step boundary is a sibling snapshot, not a fabricated ancestor.
        // Its executor ledger must prove every boundary file reaches an accepted output.
        if (boundaryCoverage != null)
            foreach (var expected in Consent.Publications)
                if (await store.GetArtifactAsync(expected.ArtifactId, ct) is { } output &&
                    await boundaryCoverage(input, output, ct)) return true;
        return false;
    }
}
