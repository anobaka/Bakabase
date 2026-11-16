using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record AcquisitionStageMovedEntries(string JournalId, string SourceArtifactId, string SourceFingerprint,
    string CanonicalArtifactId, string OriginalRoot, IReadOnlyList<string> RelativePaths, int TaskId, int RunId, string PartId);

internal static partial class AcquisitionProcessingStageCommit
{
    private sealed record CompletedStageCommit(FileProcessingRelocatedResult Proof,
        AcquisitionProcessingStageCommitReceipt Journal, ArtifactRecord Canonical);

    internal static async Task<FileProcessingRelocatedResult?> ResolveMovedResultAsync(BakabaseDbContext db,
        IArtifactOperationStore store, FileProcessingWorkspaceResolver workspaces, ArtifactRecord source, CancellationToken ct) =>
        (await ReadCompletedCommitAsync(db, store, workspaces, source, verifyCurrentFiles: true, ct))?.Proof;

    internal static async Task<AcquisitionStageMovedEntries?> ReadMovedEntriesAsync(BakabaseDbContext db,
        IArtifactOperationStore store, FileProcessingWorkspaceResolver workspaces, ArtifactRecord source, CancellationToken ct)
    {
        var verified = await ReadCompletedCommitAsync(db, store, workspaces, source, verifyCurrentFiles: false, ct);
        if (verified == null) return null;
        var journal = verified.Journal;
        return new(journal.Id, source.Id, source.Fingerprint, verified.Canonical.Id, source.RootDirectory,
            journal.Move.Files.Select(f => f.RelativePath).ToArray(), journal.TaskId, journal.RunId, journal.PartId);
    }

    private static async Task<CompletedStageCommit?> ReadCompletedCommitAsync(BakabaseDbContext db,
        IArtifactOperationStore store, FileProcessingWorkspaceResolver workspaces, ArtifactRecord source,
        bool verifyCurrentFiles, CancellationToken ct)
    {
        if (!source.Managed || source.Kind != "processedFiles" || verifyCurrentFiles && source.State != ArtifactLocationState.Available ||
            source.State is not (ArtifactLocationState.Available or ArtifactLocationState.Quarantined or ArtifactLocationState.Retired)) return null;
        try
        {
            var children = await db.ArtifactLineage.AsNoTracking().Where(l => l.ParentArtifactId == source.Id)
                .Select(l => l.ChildArtifactId).Distinct().ToArrayAsync(ct);
            foreach (var id in children)
            {
                var current = await store.GetArtifactAsync(id, ct);
                var evidence = current == null ? null : ReadMetadata(current)?.StageCommit;
                if (current is not {Kind: "processedFiles"} ||
                    (verifyCurrentFiles ? current.State != ArtifactLocationState.Available && !(current.State == ArtifactLocationState.Retired && !current.Managed)
                        : current.State is not (ArtifactLocationState.Available or ArtifactLocationState.Retired)) ||
                    evidence is not {Version: 1} || evidence.SourceArtifactId != source.Id ||
                    evidence.SourceFingerprint != source.Fingerprint || string.IsNullOrWhiteSpace(evidence.SourceDirectory) ||
                    !Same(evidence.SourceDirectory, source.RootDirectory) || string.IsNullOrWhiteSpace(evidence.Directory) ||
                    (current.State == ArtifactLocationState.Available || verifyCurrentFiles) && !Same(evidence.Directory, current.RootDirectory) || evidence.Id is not {Length: 64} ||
                    evidence.Id.Any(c => !Uri.IsHexDigit(c)) || !Guid.TryParseExact(evidence.BindingId, "N", out _)) continue;
                var journal = JsonSerializer.Deserialize<AcquisitionProcessingStageCommitReceipt>(
                    await File.ReadAllTextAsync(JournalPath(workspaces, evidence.Id), ct), Json);
                if (journal?.Move?.Files == null || journal.ReusedFiles == null || journal.Id != evidence.Id ||
                    journal.Id != ArtifactManifestFiles.Hash("processing-stage-v1:" + evidence.TaskId + ":" + evidence.PartId + ":" + evidence.BindingId + ":" + source.Id) ||
                    journal.TaskId != evidence.TaskId ||
                    journal.RunId != evidence.RunId || journal.PartId != evidence.PartId || journal.BindingId != evidence.BindingId ||
                    journal.SourceArtifactId != source.Id || journal.SourceFingerprint != source.Fingerprint ||
                    journal.ArtifactId != current.Id || journal.Fingerprint != source.Fingerprint ||
                    journal.Directory != evidence.Directory || journal.SourceDirectory != evidence.SourceDirectory ||
                    journal.Move.SourceDirectory != source.RootDirectory || journal.Move.Directory != evidence.Directory ||
                    !journal.Move.Files.All(f => f != null && f.Completed && source.Manifest.Entries.Any(e =>
                        e.RelativePath == f.RelativePath && e.Length == f.Length)) ||
                    !journal.Move.Files.Select(f => f.RelativePath).Concat(journal.ReusedFiles).ToHashSet(StringComparer.Ordinal)
                        .SetEquals(source.Manifest.Entries.Select(e => e.RelativePath)) ||
                    journal.Move.Files.Count + journal.ReusedFiles.Count != source.Manifest.Entries.Count) continue;
                var proof = new FileProcessingRelocatedResult(source.RootDirectory, evidence.Directory, current.Manifest);
                if (!proof.Matches(source.RootDirectory, source.Manifest)) continue;
                var ownerSources = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s =>
                    s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId == evidence.TaskId.ToString())
                    .Select(s => s.WorkflowRunId).Distinct().ToArrayAsync(ct);
                if (!ownerSources.Contains(evidence.RunId) || !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, evidence.RunId, evidence.TaskId, ct)) continue;
                if (await db.WorkflowRuns.AnyAsync(r => ownerSources.Contains(r.Id) && (r.Status == WorkflowRunStatus.Running ||
                    r.Status == WorkflowRunStatus.Pending || r.PendingSignalJson != null), ct)) continue;
                var validOwners = true;
                foreach (var artifact in new[] {source, current})
                {
                    var operation = await store.GetOperationAsync(artifact.OperationId, ct);
                    if (operation is not {State: ArtifactOperationState.Completed, Kind: "files.process"} ||
                        operation.OutputArtifactId != artifact.Id || artifact.Id == current.Id && operation.InputFingerprint != source.Fingerprint)
                    {validOwners = false; break;}
                    if (await store.HasActiveConsumersAsync(artifact.OperationId, ct)) {validOwners = false; break;}
                    var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == artifact.OperationId).ToListAsync(ct);
                    if (bindings.Count == 0 || bindings.Any(b => b.OwnerKind != "workflow" || !int.TryParse(b.OwnerId, out var run) ||
                            !ownerSources.Contains(run) || !b.Slot.StartsWith(evidence.PartId + ":", StringComparison.Ordinal)))
                    {validOwners = false; break;}
                    var runs = bindings.Select(b => int.Parse(b.OwnerId)).Distinct().ToArray();
                    if (await db.WorkflowRuns.AnyAsync(r => runs.Contains(r.Id) && (r.Status == WorkflowRunStatus.Running ||
                        r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Waiting || r.PendingSignalJson != null), ct))
                    {validOwners = false; break;}
                }
                if (!validOwners) continue;
                var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == evidence.TaskId, ct);
                if (task == null) continue;
                var plan = task.PlanJson == null ? null : JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson, Json);
                var history = task.PlanHistoryJson == null ? [] : JsonSerializer.Deserialize<AcquisitionPlanRevision[]>(task.PlanHistoryJson, Json) ?? [];
                bool Bound(AcquisitionPlan? value) => AcquisitionCurrentDirectoryFiles.Entry(value?.StageDirectories, evidence.PartId, "processing") is {} b &&
                    b.BindingId == evidence.BindingId && Same(b.Directory, evidence.Directory);
                if (!Bound(plan) && !history.Any(h => Bound(h.Plan))) continue;
                // Full current bytes are reverified here and again under the executor's cleanup
                // gate. Missing or modified canonical files cannot authorize deleting snapshots.
                // Retiring a borrowed mixed view releases its reference only. It neither
                // moves nor deletes its files, so its exact original root remains a valid
                // verification location when every current byte still matches. Managed
                // quarantined/retired payloads never use this exception.
                var available = !verifyCurrentFiles || (current.State == ArtifactLocationState.Retired && !current.Managed
                    ? (await ArtifactManifestFiles.ValidateAsync(current.RootDirectory, current.Manifest, ct)).Available
                    : await store.ResolveByIdAsync(current.Id, ct) is {} verified && verified.Fingerprint == source.Fingerprint);
                if (available) return new(proof, journal, current);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { /* Missing/corrupt provenance leaves all original cleanup protection in place. */ }
        return null;
    }
    internal static async Task<bool> IsCommittedStageOutputAsync(BakabaseDbContext db, IArtifactOperationStore store,
        FileProcessingWorkspaceResolver workspaces, ArtifactRecord artifact, CancellationToken ct)
    {
        if (artifact.Kind != "processedFiles" || artifact.State != ArtifactLocationState.Available) return false;
        try
        {
            var metadata = ReadMetadata(artifact);
            var sourceId = metadata?.StageCommit?.SourceArtifactId ?? metadata?.InputArtifactId;
            if (sourceId == null || await store.GetArtifactAsync(sourceId, ct) is not {} source ||
                await ResolveMovedResultAsync(db, store, workspaces, source, ct) is not {} proof ||
                !Same(proof.VerificationRoot, artifact.RootDirectory)) return false;
            var children = await db.ArtifactLineage.AsNoTracking().Where(l => l.ParentArtifactId == source.Id)
                .Select(l => l.ChildArtifactId).Distinct().ToArrayAsync(ct);
            foreach (var id in children)
            {
                var full = await store.GetArtifactAsync(id, ct);
                var evidence = full == null ? null : ReadMetadata(full)?.StageCommit;
                if (full?.RootDirectory != artifact.RootDirectory || evidence?.SourceArtifactId != source.Id ||
                    evidence.Id is not {Length: 64}) continue;
                var journal = JsonSerializer.Deserialize<AcquisitionProcessingStageCommitReceipt>(
                    await File.ReadAllTextAsync(JournalPath(workspaces, evidence.Id), ct), Json);
                if (journal?.ArtifactId != full.Id || journal.Fingerprint != full.Fingerprint ||
                    !proof.Matches(source.RootDirectory, full.Manifest)) continue;
                if (artifact.Id == full.Id) return true;
                if (journal.OwnedArtifactId != artifact.Id || !artifact.Managed || !full.ParentArtifactIds.Contains(artifact.Id) ||
                    artifact.Manifest.Entries.Any(e => !full.Manifest.Entries.Any(f => FileDispositionProofs.SameEntry(e, f)))) continue;
                var producer = await store.GetOperationAsync(artifact.OperationId, ct);
                if (producer is not {Kind: "files.process", State: ArtifactOperationState.Completed} || producer.OutputArtifactId != artifact.Id) continue;
                var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == artifact.OperationId).ToListAsync(ct);
                if (bindings.Count == 0 || bindings.Any(b => b.OwnerKind != "workflow" || !int.TryParse(b.OwnerId, out _) ||
                    !b.Slot.StartsWith(evidence.PartId + ":", StringComparison.Ordinal))) continue;
                var owned = true;
                foreach (var binding in bindings)
                    if (!await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, int.Parse(binding.OwnerId), evidence.TaskId, ct)) {owned = false; break;}
                if (owned) return true;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { }
        return false;
    }

}
