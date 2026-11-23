using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.FileProcessing;

public sealed record ArtifactWorkspaceCleanupPreview(string ArtifactId, string OperationId, string ResultDirectory,
    bool Eligible, string? Reason, int FileCount, long Bytes, string? SelectionFingerprint = null,
    string? PublishedArtifactId = null)
{
    // Request-local presentation only. Never serialized as authority or reused by execution.
    internal IReadOnlyList<(string Path, long Bytes)>? PreviewFiles { get; init; }
    // A confirmed moved source may retire its registration, but never unlink a file
    // which reappeared at a path the user previewed as already absent.
    internal IReadOnlySet<string>? ExpectedMovedFiles { get; init; }
    // Request-local physical selection identity. Distinct retired boundaries can prove
    // the same private inputs; the deletion coordinator may execute that exact set once.
    // It never replaces the candidate-specific fingerprint rechecked during execution.
    internal string? PrivateSelectionFingerprint { get; init; }
    // Diagnostic only: never serialized to clients or reused as cleanup authority.
    internal Exception? PreviewError { get; init; }
}
public sealed record ArtifactWorkspaceCleanupInput(IReadOnlyList<string> ArtifactIds);
public sealed record ArtifactWorkspaceCleanupResult(string ArtifactId, bool Completed, string? Reason,
    int RemovedFiles = 0, long RemovedBytes = 0);

/// <summary>Manifest-based maintenance, never an age-based directory sweep. Original downloads,
/// registered result snapshots and step/history metadata are outside the reclamation set.</summary>
public sealed partial class ArtifactWorkspaceMaintenance
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly BakabaseDbContext db;
    private readonly IArtifactOperationStore store;
    private readonly FileProcessingPlanExecutor plans;
    private readonly RecursiveArchiveExecutor recursive;
    private readonly ArtifactWorkspaceCleanupJournal journal;
    private readonly ArtifactCleanupReferenceGuard references;
    private readonly FileProcessingWorkspaceResolver workspaces;
    internal string StageCleanupMetadataDirectory => workspaces.StageCleanupMetadataDirectory;

    public ArtifactWorkspaceMaintenance(BakabaseDbContext db, IArtifactOperationStore store,
        FileProcessingPlanExecutor plans, RecursiveArchiveExecutor recursive, AppService app, FileProcessingWorkspaceResolver? workspaces = null)
        : this(db, store, plans, recursive, app.AppDataDirectory, workspaces) { }

    internal ArtifactWorkspaceMaintenance(BakabaseDbContext db, IArtifactOperationStore store,
        FileProcessingPlanExecutor plans, RecursiveArchiveExecutor recursive, string appData, FileProcessingWorkspaceResolver? workspaces = null)
    {
        this.db = db; this.store = store; this.plans = plans; this.recursive = recursive;
        this.workspaces = workspaces ?? new(appData);
        journal = new(appData); references = new(db, this.workspaces);
    }

    // Keyset pagination revisits skipped candidates on the next complete cycle, including
    // consumers released after an earlier pass. A fixed first-page limit would starve old work.
    internal async Task<IReadOnlyList<string>> GetCandidateIdsAsync(string? afterId, int pageSize, CancellationToken ct)
    {
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var query = db.Set<ArtifactDbModel>().AsNoTracking()
            .Where(a => a.Managed && a.Kind == "processedFiles" && a.State == ArtifactLocationState.Available &&
                db.Set<ArtifactOperationDbModel>().Any(o => o.Id == a.OperationId &&
                    o.State == ArtifactOperationState.Completed && o.OutputArtifactId == a.Id));
        if (afterId != null) query = query.Where(a => string.Compare(a.Id, afterId) > 0);
        var ids = await query.OrderBy(a => a.Id).Select(a => a.Id).Take(pageSize).ToListAsync(ct);
        return ids;
    }

    public async Task<IReadOnlyList<ArtifactWorkspaceCleanupPreview>> PreviewAsync(CancellationToken ct)
    {
        var ids = await db.Set<ArtifactDbModel>().AsNoTracking()
            .Where(a => a.Managed && a.Kind == "processedFiles" && a.State == ArtifactLocationState.Available)
            .OrderBy(a => a.CreatedAt).Select(a => a.Id).Take(1000).ToListAsync(ct);
        return await PreviewCoreAsync(ids, ct);
    }

    public Task<IReadOnlyList<ArtifactWorkspaceCleanupPreview>> PreviewAsync(IReadOnlyList<string> artifactIds, CancellationToken ct)
    {
        if (artifactIds.Count is < 1 or > 100 || artifactIds.Any(string.IsNullOrWhiteSpace) || artifactIds.Distinct().Count() != artifactIds.Count)
            throw new ArgumentException("Select between one and 100 distinct artifact IDs.");
        return PreviewCoreAsync(artifactIds, ct);
    }

    internal Task<IReadOnlyList<ArtifactWorkspaceCleanupPreview>> PreviewForDeletionAsync(IReadOnlyList<string> ids,
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct, ArtifactCleanupReferenceGuard? previewReferences = null) =>
        PreviewCoreAsync(ids, ct, authorization, previewReferences);

    internal Task RecordAlreadyAbsentForDeletionAsync(ArtifactRecord artifact, IReadOnlyList<string> paths,
        ArtifactDeliveryCleanupConsent consent, CancellationToken ct) => journal.RecordAlreadyAbsentAsync(artifact, paths, consent, ct);

    private async Task<IReadOnlyList<ArtifactWorkspaceCleanupPreview>> PreviewCoreAsync(IReadOnlyList<string> ids, CancellationToken ct,
        ArtifactDeliveryCleanupAuthorization? authorization = null, ArtifactCleanupReferenceGuard? previewReferences = null)
    {
        previewReferences ??= references.CreatePreviewSnapshot();
        var result = new List<ArtifactWorkspaceCleanupPreview>();
        foreach (var id in ids)
        {
            var artifact = await store.GetArtifactAsync(id, ct);
            if (artifact == null) continue;
            try
            {
                if (authorization != null && artifact.State == ArtifactLocationState.Retired &&
                    await InspectRetiredWorkspaceAbsenceAsync(artifact, authorization, ct, includePrivateFiles: true) is { } absent)
                {
                    var privateSelection = CaptureSelection(absent.PrivateFiles ?? [], ct);
                    var selected = privateSelection.Files;
                    result.Add(new(id, artifact.OperationId, absent.OriginalRoot, true, selected.Count == 0 ? "AlreadyAbsent" : null,
                        selected.Count, selected.Sum(f => f.Bytes), absent.Fingerprint)
                        {PreviewFiles = selected.Select(f => (f.Path, f.Bytes)).ToArray(),
                            PrivateSelectionFingerprint = privateSelection.Fingerprint});
                    continue;
                }
                if (authorization == null && await AcquisitionProcessingStageCommit.IsCommittedStageOutputAsync(db, store, workspaces, artifact, ct))
                {
                    result.Add(new(id, artifact.OperationId, artifact.RootDirectory, false, "NothingToReclaim", 0, 0));
                    continue;
                }
                var reason = await IneligibleReason(artifact, ct, authorization);
                var files = reason == null || authorization != null
                    ? await Paths(artifact, ct, authorization != null, previewOnly: true) : [];
                var snapshot = CaptureSelection(files, ct);
                if (reason == null && files.Count > 0 && await previewReferences.HasReferencesAsync(artifact, authorization?.Consent.OwnerRunIds ?? [], ct, files,
                    authorization?.PreviewSupersededRuns))
                    reason = "ReferencedPath";
                result.Add(new(id, artifact.OperationId, artifact.RootDirectory, reason == null && files.Count > 0,
                    reason ?? (files.Count == 0 ? "NothingToReclaim" : null), reason == null ? snapshot.Files.Count : 0,
                    reason == null ? snapshot.Files.Sum(f => f.Bytes) : 0, reason == null ? snapshot.Fingerprint : null)
                    {PreviewFiles = snapshot.Files.Select(f => (f.Path, f.Bytes)).ToArray(),
                        PrivateSelectionFingerprint = reason == null ? snapshot.Fingerprint : null});
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            { result.Add(new(id, artifact.OperationId, artifact.RootDirectory, false, "WorkspaceUnavailable", 0, 0) {PreviewError = error}); }
        }
        return result;
    }

    public Task<IReadOnlyList<ArtifactWorkspaceCleanupResult>> ReclaimAsync(IReadOnlyList<string> artifactIds, CancellationToken ct,
        IReadOnlyDictionary<string, string>? expectedSelectionFingerprints = null) =>
        ReclaimCoreAsync(artifactIds, ct, expectedSelectionFingerprints, null);

    internal Task<IReadOnlyList<ArtifactWorkspaceCleanupResult>> ReclaimForDeletionAsync(IReadOnlyList<string> artifactIds,
        ArtifactDeliveryCleanupAuthorization authorization, IReadOnlyDictionary<string, string> fingerprints, CancellationToken ct) =>
        ReclaimCoreAsync(artifactIds, ct, fingerprints, authorization);

    private async Task<IReadOnlyList<ArtifactWorkspaceCleanupResult>> ReclaimCoreAsync(IReadOnlyList<string> artifactIds,
        CancellationToken ct, IReadOnlyDictionary<string, string>? expectedSelectionFingerprints,
        ArtifactDeliveryCleanupAuthorization? authorization)
    {
        if (artifactIds.Count is < 1 or > 100 || artifactIds.Any(string.IsNullOrWhiteSpace) || artifactIds.Distinct().Count() != artifactIds.Count)
            throw new ArgumentException("Select between one and 100 distinct artifact IDs.");
        if (expectedSelectionFingerprints != null && (expectedSelectionFingerprints.Count != artifactIds.Count ||
            artifactIds.Any(id => !expectedSelectionFingerprints.TryGetValue(id, out var fingerprint) ||
                fingerprint == null || fingerprint.Length != 64 || fingerprint.Any(c => !Uri.IsHexDigit(c)))))
            throw new ArgumentException("Provide the preview fingerprint for every selected artifact.");
        var results = new List<ArtifactWorkspaceCleanupResult>();
        foreach (var id in artifactIds)
        {
            ArtifactWorkspaceCleanupJournal.Receipt? receipt = null;
            try
            {
                // A cache hit often still has a live workflow consumer, or its private files
                // have already been reclaimed. Do not queue an exclusive global fence just
                // to discover that there is no work. NAS metadata reads also stay outside
                // the fence here. This is only an early refusal: every condition is checked
                // again below before any verification or filesystem mutation is authorized.
                var preview = await store.GetArtifactAsync(id, ct);
                if (preview == null) { results.Add(new(id, false, "ArtifactMissing")); continue; }
                if (authorization != null && preview.State == ArtifactLocationState.Retired)
                {
                    await using var retiredFence = await store.AcquireMaintenanceAsync(ct);
                    var retiredArtifact = await store.GetArtifactAsync(id, ct);
                    var absent = retiredArtifact == null ? null : await InspectRetiredWorkspaceAbsenceAsync(retiredArtifact, authorization, ct, includePrivateFiles: true);
                    var matches = absent != null && (expectedSelectionFingerprints == null ||
                        expectedSelectionFingerprints[id] == absent.Fingerprint);
                    if (!matches || absent?.Original == null || absent.PrivateFiles is not {Count: > 0})
                    { results.Add(new(id, matches, matches ? "AlreadyAbsent" : "CLEANUP_RETIRED_WORKSPACE_REQUIRES_REVIEW")); continue; }
                    var retiredFiles = CaptureSelection(absent.PrivateFiles, ct).Files;
                    var retiredSnapshot = ArtifactCleanupFileSnapshot.For(authorization)!;
                    foreach (var file in retiredFiles) retiredSnapshot.Observe(file.Path, file.Bytes, ct);
                    var retiredApproved = retiredFiles.Select(f => f.Path).ToHashSet(FileProcessingFiles.PathComparer);
                    receipt = await journal.BeginAsync(absent.Original, retiredFiles.ToDictionary(f => f.Path, f => f.Bytes), ct,
                        authorization.Consent);
                    await plans.ReclaimBoundaryIntermediatesAsync(absent.Original.RootDirectory, absent.Original.Manifest,
                        retiredApproved, ct, overrideRetention: true, retiredSnapshot);
                    await plans.ReclaimCompletedIntermediatesAsync(absent.Original.RootDirectory, absent.Original.Manifest,
                        retiredApproved, ct, overrideRetention: true, retiredSnapshot);
                    var retiredOutcome = await journal.FinishAsync(receipt, "Completed", ct);
                    var retiredComplete = retiredOutcome.RemovedFiles == retiredFiles.Count;
                    results.Add(new(id, retiredComplete, retiredComplete ? null : "WorkspaceChanged", retiredOutcome.RemovedFiles, retiredOutcome.RemovedBytes));
                    continue;
                }
                if (authorization == null && await AcquisitionProcessingStageCommit.IsCommittedStageOutputAsync(db, store, workspaces, preview, ct))
                { results.Add(new(id, true, "NothingToReclaim")); continue; }
                var unavailable = await IneligibleReason(preview, ct, authorization);
                if (unavailable != null) { results.Add(new(id, false, unavailable)); continue; }
                if (expectedSelectionFingerprints == null && (await Paths(preview, ct, authorization != null, previewOnly: true)).Count == 0)
                { results.Add(new(id, true, "NothingToReclaim")); continue; }
                await using var exclusive = await store.AcquireMaintenanceAsync(ct);
                var artifact = await store.GetArtifactAsync(id, ct);
                if (artifact == null) { results.Add(new(id, false, "ArtifactMissing")); continue; }
                var reason = await IneligibleReason(artifact, ct, authorization);
                if (reason != null) { results.Add(new(id, false, reason)); continue; }
                // Explicitly accepted deletion may skip byte reads, never the exact private
                // selection or its current identity. Automatic cleanup retains full SHA checks.
                var snapshot = ArtifactCleanupFileSnapshot.For(authorization);
                var metadataSelection = snapshot != null;
                var files = await Paths(artifact, ct, authorization != null, previewOnly: metadataSelection);
                var selection = CaptureSelection(files);
                if (expectedSelectionFingerprints != null && !selection.Fingerprint.Equals(
                        expectedSelectionFingerprints[id], StringComparison.OrdinalIgnoreCase))
                { results.Add(new(id, false, "SelectionChanged")); continue; }
                if (files.Count == 0) { results.Add(new(id, true, "NothingToReclaim")); continue; }
                foreach (var file in selection.Files) snapshot?.Observe(file.Path, file.Bytes, ct);
                // A registered artifact elsewhere may intentionally retain an internal file set.
                // Refuse the whole candidate instead of assuming its location is disposable.
                if (await store.HasRegisteredFilesAsync(files, ct))
                { results.Add(new(id, false, "ReferencedArtifact")); continue; }
                if (await references.HasReferencesAsync(artifact, authorization?.Consent.OwnerRunIds ?? [], ct, files))
                { results.Add(new(id, false, "ReferencedPath")); continue; }
                // A completed stage commit moves the result into the current visible
                // processing directory. Verify its independent receipt and exact output
                // before attempting the historical root (which must not be marked missing).
                var relocatedResult = snapshot == null
                    ? await AcquisitionProcessingStageCommit.ResolveMovedResultAsync(db, store, workspaces, artifact, ct)
                    : null;
                var verified = relocatedResult != null ? artifact : snapshot != null
                    ? await store.GetArtifactAsync(id, ct) : await store.ResolveByIdAsync(id, ct);
                if (verified == null || snapshot != null && !ArtifactCleanupPreviewFiles.MatchesManifest(verified, ct,
                        allowConfirmedAbsent: authorization?.Consent is {DeliveryAccepted: true, SkipFileVerification: true}))
                { results.Add(new(id, false, "ResultNotVerified")); continue; }
                if (await IneligibleReason(verified, ct, authorization) is { } changed)
                { results.Add(new(id, false, changed)); continue; }
                // Result verification can take time on a NAS. Re-read the exact private set
                // before its durable intent, even for background cleanup without a UI preview.
                var current = CaptureSelection(await Paths(verified, ct, authorization != null, previewOnly: snapshot != null));
                if (!current.Fingerprint.Equals(selection.Fingerprint, StringComparison.Ordinal))
                { results.Add(new(id, false, "SelectionChanged")); continue; }
                if (await references.HasReferencesAsync(verified, authorization?.Consent.OwnerRunIds ?? [], ct, current.Files.Select(f => f.Path).ToArray()))
                { results.Add(new(id, false, "ReferencedPath")); continue; }
                foreach (var file in current.Files) snapshot?.Observe(file.Path, file.Bytes, ct);
                var sizes = current.Files.ToDictionary(f => f.Path, f => f.Bytes, FileProcessingFiles.PathComparer);
                var approved = sizes.Keys.ToHashSet(FileProcessingFiles.PathComparer);
                // Durable intent precedes the first unlink. If the process stops mid-cleanup,
                // the precise candidates and committed result remain available for audit.
                receipt = await journal.BeginAsync(verified, sizes, ct, authorization?.Consent);
                await plans.ReclaimBoundaryIntermediatesAsync(verified.RootDirectory, verified.Manifest, approved, ct, authorization != null, snapshot, relocatedResult);
                await plans.ReclaimCompletedIntermediatesAsync(verified.RootDirectory, verified.Manifest, approved, ct, authorization != null, snapshot, relocatedResult);
                foreach (var planRoot in RelatedCompletedPlanRoots(verified))
                {
                    var stateFile = Path.Combine(Path.GetDirectoryName(planRoot)!, "state.json");
                    var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(stateFile, ct), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (state is not {Completed: true, FinalManifest: not null} || state.FinalFiles.Count == 0) continue;
                    // The executor validates these stored hashes against the files under its
                    // gate. Capturing an extra manifest before entering it reads every file twice.
                    await plans.ReclaimCompletedIntermediatesAsync(planRoot, state.FinalManifest, approved, ct, authorization != null, snapshot);
                }
                await recursive.ReclaimCompletedIntermediatesAsync(Path.GetDirectoryName(verified.RootDirectory)!, verified, approved, ct, authorization != null, snapshot, relocatedResult);
                await recursive.ReclaimReplacedPlanResultAsync(verified.RootDirectory, verified.Manifest, approved, ct, snapshot);
                var outcome = await journal.FinishAsync(receipt, "Completed", ct);
                var complete = outcome.RemovedFiles == files.Count;
                results.Add(new(id, complete, complete ? null : "WorkspaceChanged", outcome.RemovedFiles, outcome.RemovedBytes));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or OperationCanceledException)
            {
                ArtifactWorkspaceCleanupJournal.Receipt? partial = null;
                if (receipt != null)
                {
                    try { partial = await journal.FinishAsync(receipt, error is OperationCanceledException ? "Interrupted" : "Failed", CancellationToken.None); }
                    catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException or InvalidOperationException)
                    { /* The already durable intent still identifies this interrupted attempt. */ }
                }
                if (error is OperationCanceledException) throw;
                results.Add(new(id, false, "WorkspaceUnavailable", partial?.RemovedFiles ?? 0, partial?.RemovedBytes ?? 0));
            }
        }
        return results;
    }

    private sealed record SelectionFile(string Path, long Bytes, long LastWriteUtcTicks);
    private sealed record SelectionSnapshot(IReadOnlyList<SelectionFile> Files, string Fingerprint);

    // Private snapshots are immutable executor inputs, not arbitrary user files. A metadata
    // fingerprint binds the exact approved set without another full content scan of large archives.
    private static SelectionSnapshot CaptureSelection(IReadOnlyList<string> files, CancellationToken ct = default)
    {
        var entries = files.Select(Path.GetFullPath).Distinct(FileProcessingFiles.PathComparer)
            .OrderBy(path => path, FileProcessingFiles.PathComparer).Select(path =>
            {
                ct.ThrowIfCancellationRequested();
                ArtifactManifestFiles.RejectLinks(path);
                var info = new FileInfo(path);
                info.Refresh();
                if (!info.Exists) throw new FileNotFoundException("A cleanup candidate is no longer available.", path);
                return new SelectionFile(path, info.Length, info.LastWriteTimeUtc.Ticks);
            }).ToArray();
        // File-system case comparison is reflected only in the digest; retain real casing for I/O.
        var normalized = entries.Select(entry => entry with {Path = OperatingSystem.IsWindows()
            ? entry.Path.ToUpperInvariant() : entry.Path}).ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new {Version = 1, Files = normalized}, Json))).ToLowerInvariant();
        return new(entries, fingerprint);
    }

    internal async Task<IReadOnlyList<(string Path, long Bytes)>> DescribeFilesAsync(ArtifactWorkspaceCleanupPreview preview,
        CancellationToken ct, bool overrideRetention = false)
    {
        ct.ThrowIfCancellationRequested();
        if (preview.PreviewFiles != null) return preview.PreviewFiles;
        var artifact = await store.GetArtifactAsync(preview.ArtifactId, ct) ?? throw new InvalidDataException("ArtifactMissing");
        var selection = CaptureSelection(await Paths(artifact, ct, overrideRetention, previewOnly: true), ct);
        if (preview.Eligible && selection.Fingerprint != preview.SelectionFingerprint)
            throw new InvalidDataException("SelectionChanged");
        return selection.Files.Select(f => (f.Path, f.Bytes)).ToArray();
    }

    private async Task<string?> IneligibleReason(ArtifactRecord artifact, CancellationToken ct,
        ArtifactDeliveryCleanupAuthorization? authorization = null)
    {
        if (!artifact.Managed || artifact.Kind != "processedFiles" || artifact.State != ArtifactLocationState.Available)
            return "NotManagedProcessedArtifact";
        if (authorization != null && !await authorization.CoversAsync(artifact, ct)) return "DeliveryAcceptanceChanged";
        if (authorization == null && artifact.MetadataJson != null)
        {
            // Frozen preferences belong to this artifact, not today's global configuration.
            // Step overrides may intentionally reclaim a subset while retaining the parent inputs.
            if (!FileArtifactRetentionMetadata.TryRead(artifact.MetadataJson, out var metadata)) return "RetentionPolicyKeep";
            if (metadata?.StoragePolicy?.CleanupIntermediates == false)
            {
                var steps = metadata.ReadPlan()?.Steps;
                if (steps == null || steps.Any(s => s == null) || !steps.Any(s => s.StoragePolicy?.CleanupIntermediates == true))
                    return "RetentionPolicyKeep";
            }
        }
        var operation = await store.GetOperationAsync(artifact.OperationId, ct);
        if (operation?.State != ArtifactOperationState.Completed || operation.OutputArtifactId != artifact.Id)
            return "OperationNotCompleted";
        if (await (authorization?.HasActiveConsumersAsync(artifact.OperationId, ct) ?? store.HasActiveConsumersAsync(artifact.OperationId, ct))) return "ActiveConsumer";
        if (Path.GetFileName(artifact.RootDirectory) != "result")
        {
            if (!await FileProcessingPlanExecutor.IsRegisteredBoundaryAsync(artifact.RootDirectory, artifact.Manifest, ct))
                return "IntermediateResultRetained";
            // A later dynamic step may still reference download/earlier branch inputs in
            // this same checkpoint. A boundary is reclaimable only after every bound run
            // has stopped, including when no consumer lease happens to be alive right now.
            var bindings = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
                .Where(b => b.OperationId == artifact.OperationId).ToListAsync(ct);
            if (bindings.Count == 0 || bindings.Any(b => b.OwnerKind != "workflow" || !int.TryParse(b.OwnerId, out _)))
                return "BoundaryOwnerUnconfirmed";
            var runs = bindings.Select(b => int.Parse(b.OwnerId, System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToArray();
            var previewInactive = authorization?.PreviewSupersededRuns ?? [];
            if (await db.Set<WorkflowRunDbModel>().CountAsync(r => runs.Contains(r.Id) &&
                    (r.Status != WorkflowRunStatus.Pending && r.Status != WorkflowRunStatus.Running && r.Status != WorkflowRunStatus.Waiting ||
                     r.Status == WorkflowRunStatus.Waiting && r.PendingSignalJson == null && previewInactive.Contains(r.Id)), ct) != runs.Length) return "ActiveConsumer";
        }
        return null;
    }

    private static async Task<IReadOnlyList<string>> Paths(ArtifactRecord artifact, CancellationToken ct, bool overrideRetention = false,
        bool previewOnly = false)
    {
        var plan = await PlanPaths(artifact.RootDirectory);
        var boundary = await FileProcessingPlanExecutor.GetReclaimableBoundaryFilesAsync(artifact.RootDirectory, artifact.Manifest, ct, overrideRetention,
            verifyContents: !previewOnly);
        var recursive = await (previewOnly
            ? RecursiveArchiveExecutor.GetReclaimableFilesForPreviewAsync(Path.GetDirectoryName(artifact.RootDirectory)!, ct, overrideRetention)
            : RecursiveArchiveExecutor.GetReclaimableFilesAsync(Path.GetDirectoryName(artifact.RootDirectory)!, ct, overrideRetention));
        var related = new List<string>();
        foreach (var planRoot in RelatedCompletedPlanRoots(artifact))
        {
            ct.ThrowIfCancellationRequested();
            related.AddRange(await PlanPaths(planRoot));
        }
        var replacement = await RecursiveArchiveExecutor.ReplacedPlanResultAsync(artifact.RootDirectory, artifact.Manifest, ct, !previewOnly);
        return plan.Concat(boundary).Concat(recursive).Concat(related).Concat(replacement?.Files ?? [])
            .Distinct(FileProcessingFiles.PathComparer).ToArray();

        Task<IReadOnlyList<string>> PlanPaths(string root) => previewOnly
            ? FileProcessingPlanExecutor.GetReclaimableFilesForPreviewAsync(root, ct, overrideRetention)
            : FileProcessingPlanExecutor.GetReclaimableFilesAsync(root, ct, overrideRetention);
    }

    private static IEnumerable<string> RelatedCompletedPlanRoots(ArtifactRecord artifact)
    {
        var recursiveRoot = Path.GetDirectoryName(artifact.RootDirectory)!;
        if (Path.GetFileName(recursiveRoot) != "recursive" || !File.Exists(Path.Combine(recursiveRoot, "recursive-state.json"))) yield break;
        var planRoot = Path.Combine(Path.GetDirectoryName(recursiveRoot)!, "plan");
        ArtifactManifestFiles.RejectLinks(planRoot);
        if (!Directory.Exists(planRoot)) yield break;
        foreach (var checkpoint in Directory.EnumerateDirectories(planRoot))
        {
            var name = Path.GetFileName(checkpoint);
            if (name.Length == 64 && name.All(Uri.IsHexDigit) && File.Exists(Path.Combine(checkpoint, "state.json")) && Directory.Exists(Path.Combine(checkpoint, "result")))
                yield return Path.Combine(checkpoint, "result");
        }
    }

}
