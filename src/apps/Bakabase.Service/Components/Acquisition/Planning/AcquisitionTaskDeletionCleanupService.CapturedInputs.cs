using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionTaskDeletionCleanupService
{
    // A capture never becomes managed. Only explicitly accepted, published additions
    // inside a separately proven private processing result can enter this exact selection.
    internal sealed record CapturedInputCandidate(string ArtifactId, string ManagedArtifactId,
        string RootDirectory, string Fingerprint, string ManagedFingerprint, string SelectionFingerprint,
        IReadOnlyList<AcquisitionTaskDeleteCleanupFile> Files, IReadOnlyDictionary<string, string> FileIdentities,
        AcquisitionTaskDeleteCleanupGroup Group);

    internal async Task<IReadOnlyList<CapturedInputCandidate>> PreviewCapturedInputsAsync(
        ArtifactDeliveryCleanupAuthorization authorization, ArtifactCleanupReferenceGuard references,
        CancellationToken ct, Func<string, Task>? guardDirectory = null)
    {
        if (workspaces == null || !authorization.Consent.DeliveryAccepted && !authorization.Consent.WholeTaskFiles) return [];
        var owners = authorization.Consent.OwnerRunIds.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray();
        var ids = await db.Artifacts.AsNoTracking().Where(a => !a.Managed && a.Kind == AcquisitionCurrentDirectoryFiles.Kind &&
            db.ArtifactOperationBindings.Any(b => b.OperationId == a.OperationId && b.OwnerKind == "workflow" && owners.Contains(b.OwnerId)))
            .Select(a => a.Id).ToListAsync(ct);
        var result = new List<CapturedInputCandidate>();
        foreach (var id in ids)
        {
            var candidate = await InspectCapturedInputAsync(id, authorization, references, ct);
            if (candidate == null) continue;
            foreach (var file in candidate.Files) await EnsureDirectoryAuthorizedAsync(file.Path, guardDirectory);
            result.Add(candidate);
        }
        return result;
    }

    private async Task<CapturedInputCandidate?> InspectCapturedInputAsync(string id,
        ArtifactDeliveryCleanupAuthorization authorization, ArtifactCleanupReferenceGuard references, CancellationToken ct)
    {
        if (workspaces == null || !authorization.Consent.DeliveryAccepted && !authorization.Consent.WholeTaskFiles) return null;
        var capture = await artifacts.GetArtifactAsync(id, ct);
        if (capture is not {Managed: false, Kind: AcquisitionCurrentDirectoryFiles.Kind} ||
            capture.State is ArtifactLocationState.Quarantined or ArtifactLocationState.Retired) return null;
        Func<string, CapturedInputCandidate>? blocked = null;
        try
        {
            var source = capture;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var depth = 0; source.Kind == AcquisitionCurrentDirectoryFiles.Kind; depth++)
            {
                if (depth >= 32 || !seen.Add(source.Id) || source.MetadataJson == null || !SamePath(source.RootDirectory, capture.RootDirectory)) return null;
                var receipt = JsonSerializer.Deserialize<AcquisitionCurrentDirectoryReceipt>(source.MetadataJson, JsonSerializerOptions.Web);
                // Only version 1 captures inherit an artifact's managed input. A version 2
                // stage capture has its own directory binding and no PriorInput; validating
                // that binding does not authorize this inherited-workspace cleanup path.
                if (receipt is not {Version: 1, PriorInput: not null} || !authorization.Consent.OwnerRunIds.Contains(receipt.PreviousRunId) ||
                    !await AcquisitionCurrentDirectoryFiles.IsOwnedAsync(db, artifacts, source,
                        authorization.Consent.TaskId, receipt.PreviousRunId, receipt.PartId, ct)) return null;
                source = await artifacts.GetArtifactAsync(receipt.PriorInput.ArtifactId, ct);
                if (source == null) return null;
            }
            if (source is not {Managed: true, Kind: "processedFiles"} || source.State == ArtifactLocationState.Quarantined) return null;
            var row = await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == source.Id, ct);
            if (row == null || await workspaces.ReadOriginalArtifactDirectoryAsync(row, ct) is not { } original ||
                !SamePath(original, capture.RootDirectory)) return null;
            if (source.Manifest.Fingerprint != source.Fingerprint ||
                source.Fingerprint != ArtifactManifestFiles.Fingerprint(source.Manifest.Entries, source.Manifest.Directories) ||
                capture.Fingerprint != ArtifactManifestFiles.Fingerprint(capture.Manifest.Entries, capture.Manifest.Directories)) return null;
            _ = ArtifactManifestFiles.ResolveFiles(original, source.Manifest);
            _ = ArtifactManifestFiles.ResolveFiles(original, capture.Manifest);
            var producer = await artifacts.GetOperationAsync(source.OperationId, ct);
            if (producer is not {Kind: "files.process", State: ArtifactOperationState.Completed, Attempt: > 0} ||
                producer.OutputArtifactId != source.Id || !HexKey(producer.Id)) return null;
            var oldPaths = source.Manifest.Entries.Select(e => e.RelativePath).ToHashSet(ArtifactManifestFiles.PathComparer);
            var delta = capture.Manifest.Entries.Where(e => !oldPaths.Contains(e.RelativePath)).ToArray();
            if (delta.Length == 0) return null;
            var exact = delta.Select(e => new AcquisitionTaskDeleteCleanupFile(
                Path.GetFullPath(Path.Combine(original, e.RelativePath)), e.Length)).ToArray();
            CapturedInputCandidate Block(string reason) => new(capture.Id, source.Id, original, capture.Fingerprint,
                source.Fingerprint, "", exact, new Dictionary<string, string>(), new(capture.Id, "intermediate", original,
                    false, reason, [], exact, exact.Length, exact.Sum(f => f.Bytes),
                    Shared: reason is "SharedOwner" or "ReferencedArtifact" or "ReferencedPath" or "ActiveConsumer"));
            blocked = Block;
            var metadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(source.MetadataJson ?? "null", JsonSerializerOptions.Web);
            FileProcessingWorkspaceResolver.Location? location;
            if (metadata?.CompletedThroughStepId != null)
            {
                var plan = JsonSerializer.Deserialize<FileProcessingPlan>(metadata.PlanJson, JsonSerializerOptions.Web);
                if (plan == null || !plan.Steps.Any(s => s.Id == metadata.CompletedThroughStepId)) return Block("CLEANUP_DELIVERY_PROOF_MISSING");
                location = await workspaces.InspectCompletedCheckpointAsync(
                    FileArtifactProcessor.CheckpointKey(producer.InputFingerprint, plan), producer.Id, producer.Attempt,
                    metadata.WorkspaceDirectoryScope?.Directory ?? metadata.StoragePolicy?.ProcessingWorkspaceDirectory, ct);
            }
            else location = await workspaces.InspectExistingAsync("operations", producer.Id, producer.Attempt, ct);
            if (location == null || !await workspaces.HasOwnedLocationAsync(location, ct) ||
                !ArtifactCleanupPathReferences.StrictlyUnder(original, location.Directory)) return Block("CLEANUP_DELIVERY_PROOF_MISSING");
            foreach (var expected in authorization.Consent.Publications)
                if (await artifacts.GetArtifactAsync(expected.ArtifactId, ct) is not { } publication ||
                    !await authorization.MatchesAsync(publication, ct)) return Block("CLEANUP_DELIVERY_PROOF_MISSING");
            var owners = authorization.Consent.OwnerRunIds.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray();
            var sourceBindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == source.OperationId).ToListAsync(ct);
            if (sourceBindings.Count == 0 || sourceBindings.Any(b => b.OwnerKind != "workflow" || !owners.Contains(b.OwnerId))) return Block("SharedOwner");
            if (await authorization.HasActiveConsumersAsync(source.OperationId, ct) ||
                await authorization.HasActiveConsumersAsync(capture.OperationId, ct)) return Block("ActiveConsumer");
            if (!(await authorization.ReleasableViewIdsAsync(ct)).Contains(capture.Id, StringComparer.Ordinal)) return Block("SharedOwner");
            var paths = exact.Select(f => f.Path).ToArray();
            if (await authorization.HasOtherRegisteredFilesAsync(paths, capture.Id, ct)) return Block("ReferencedArtifact");
            // Retired records point at a purged quarantine. Use only its authenticated old
            // directory for path-reference checks; never revive or edit its registration.
            var originalSource = source with {RootDirectory = original,
                Files = ArtifactManifestFiles.ResolveFiles(original, source.Manifest)};
            if (await references.HasReferencesAsync(originalSource, authorization.Consent.OwnerRunIds, ct, paths,
                    authorization.PreviewSupersededRuns)) return Block("ReferencedPath");
            ArtifactManifestFiles.RejectLinks(original);
            if (!Directory.Exists(original)) return Block("WorkspaceUnavailable");
            var selected = new List<AcquisitionTaskDeleteCleanupFile>();
            var identities = new Dictionary<string, string>(ArtifactManifestFiles.PathComparer);
            foreach (var file in exact)
            {
                ct.ThrowIfCancellationRequested(); storage.EnsurePathAllowed(file.Path);
                var presence = ArtifactCleanupAbsence.Probe(original, file.Path, ct);
                if (presence == ArtifactCleanupPathPresence.Unavailable) return Block("WorkspaceUnavailable");
                if (presence == ArtifactCleanupPathPresence.ConfirmedAbsent) continue;
                var information = new FileInfo(file.Path); information.Refresh();
                if (information.Attributes.HasFlag(FileAttributes.Directory) || information.Length != file.Bytes) return Block("SelectionChanged");
                selected.Add(file);
                identities.Add(file.Path, JsonSerializer.Serialize(new {information.Length, information.CreationTimeUtc, information.LastWriteTimeUtc}));
            }
            // Historical captures can contain intermediate files already removed by the
            // user. A confirmed empty selection is an idempotent no-op, not an uncovered
            // deletion. Publication-based retention requires complete coverage; explicit task cleanup uses the private allocation above.
            if (selected.Count > 0 && !authorization.Consent.WholeTaskFiles && !(await ArtifactFileCoverage.EvaluateAsync(artifacts, capture,
                    authorization.Consent.Publications.Select(p => p.ArtifactId).ToArray(), ct)).Complete)
                return Block(ArtifactFileCoverage.IncompleteCode);
            var fingerprint = ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new {capture.Id, capture.Fingerprint,
                ManagedId = source.Id, ManagedFingerprint = source.Fingerprint, Root = original, Files = identities}));
            var executions = await db.WorkflowNodeExecutions.AsNoTracking().Where(n => authorization.Consent.OwnerRunIds.Contains(n.WorkflowRunId))
                .Select(n => new {n.WorkflowRunId, n.NodeId, n.Kind, n.OutputsJson}).ToListAsync(ct);
            var nodes = executions.Where(n => OutputArtifacts(n.OutputsJson).Contains(capture.Id, StringComparer.Ordinal))
                .Select(n => new AcquisitionTaskDeleteCleanupNode(n.WorkflowRunId, n.NodeId, n.Kind)).Distinct().ToArray();
            return new(capture.Id, source.Id, original, capture.Fingerprint, source.Fingerprint, fingerprint, selected, identities,
                new(capture.Id, "intermediate", original, true, selected.Count == 0 ? "AlreadyAbsent" : null,
                    nodes, selected, selected.Count, selected.Sum(f => f.Bytes)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            // Once a registered same-root managed source identifies residual additions,
            // an unavailable locator/directory must remain visible, never silently vanish.
            return blocked?.Invoke(error is IOException or UnauthorizedAccessException ? "WorkspaceUnavailable" : "CLEANUP_PREVIEW_FAILED");
        }
    }

    internal async Task<ArtifactWorkspaceCleanupResult> DeleteCapturedInputAsync(CapturedInputCandidate expected,
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct)
    {
        ArtifactWorkspaceCleanupJournal.Receipt? receipt = null;
        var removed = 0; long bytes = 0;
        try
        {
            await using var fence = await artifacts.AcquireMaintenanceAsync(ct);
            var current = await InspectCapturedInputAsync(expected.ArtifactId, authorization,
                new ArtifactCleanupReferenceGuard(db, workspaces), ct);
            if (current == null) return new(expected.ArtifactId, false, "SelectionChanged");
            if (!current.Group.Eligible) return new(expected.ArtifactId, false, current.Group.ReasonCode);
            if (current.ManagedArtifactId != expected.ManagedArtifactId || current.RootDirectory != expected.RootDirectory ||
                current.Fingerprint != expected.Fingerprint || current.ManagedFingerprint != expected.ManagedFingerprint ||
                current.FileIdentities.Any(p => !expected.FileIdentities.TryGetValue(p.Key, out var identity) || identity != p.Value))
                return new(expected.ArtifactId, false, "SelectionChanged");
            if (current.Files.Count == 0) return new(expected.ArtifactId, true, "AlreadyAbsent");
            var artifact = (await artifacts.GetArtifactAsync(current.ArtifactId, ct))!;
            var snapshot = new ArtifactCleanupFileSnapshot();
            foreach (var file in current.Files) snapshot.Observe(file.Path, file.Bytes, ct);
            if (!authorization.Consent.SkipFileVerification)
            {
                var actual = await ArtifactManifestFiles.CaptureAsync(current.RootDirectory, current.Files.Select(f => f.Path).ToArray(), ct);
                var recorded = artifact.Manifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
                if (actual.Entries.Any(e => !recorded.TryGetValue(e.RelativePath, out var original) || e != original))
                    return new(expected.ArtifactId, false, "SelectionChanged");
            }
            var rechecked = await InspectCapturedInputAsync(current.ArtifactId, authorization,
                new ArtifactCleanupReferenceGuard(db, workspaces), ct);
            if (rechecked is not {Group.Eligible: true} || rechecked.SelectionFingerprint != current.SelectionFingerprint)
                return new(expected.ArtifactId, false, rechecked?.Group.ReasonCode ?? "SelectionChanged");
            receipt = await maintenance.BeginDeliveredCleanupAsync(artifact,
                current.Files.ToDictionary(f => f.Path, f => f.Bytes), authorization.Consent, ct);
            foreach (var file in current.Files)
            {
                ct.ThrowIfCancellationRequested();
                if (!snapshot.Matches(file.Path, file.Bytes, ct)) throw new IOException("The captured cleanup file changed.");
                File.Delete(file.Path); removed++; bytes += file.Bytes;
            }
            await maintenance.FinishDeliveredCleanupAsync(receipt, "Completed", ct);
            // Leave the private result root, state and unregistered additions untouched.
            return new(expected.ArtifactId, true, null, removed, bytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            if (receipt != null)
                try { await maintenance.FinishDeliveredCleanupAsync(receipt,
                    error is OperationCanceledException ? "Interrupted" : "Failed", CancellationToken.None); }
                catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            if (error is OperationCanceledException) throw;
            return new(expected.ArtifactId, false, "CLEANUP_FILES_RETAINED", removed, bytes);
        }
    }
}
