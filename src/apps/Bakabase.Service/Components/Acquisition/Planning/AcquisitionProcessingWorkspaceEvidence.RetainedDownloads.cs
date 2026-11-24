using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record AcquisitionRetainedDownloadFile(string Path, long Length, long LastWriteTicks, string Identity)
{
    internal static AcquisitionRetainedDownloadFile Capture(string path)
    {
        ArtifactManifestFiles.RejectLinks(path);
        var info = new FileInfo(path);
        return new(path, info.Length, info.LastWriteTimeUtc.Ticks, ArtifactFileIdentity.Read(path));
    }

    internal static void RequireUnchanged(IReadOnlyList<AcquisitionRetainedDownloadFile> files, CancellationToken ct)
    {
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (file != Capture(file.Path)) throw new IOException("files_changed");
        }
    }
}

internal sealed record AcquisitionRetainedDownload(string ArtifactId, IReadOnlyList<AcquisitionRetainedDownloadFile> Files);

internal static partial class AcquisitionProcessingWorkspaceEvidence
{
    private static async Task<List<ArtifactRecord>> RetainedSourcePublicationsAsync(BakabaseDbContext db, IArtifactOperationStore store,
        int? runId, string partId, IReadOnlyList<ArtifactRecord> publications, CancellationToken ct)
    {
        var result = new List<ArtifactRecord>();
        if (runId == null) return result;
        var slot = partId + ":artifact.publish";
        var owner = runId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var operations = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OwnerKind == "workflow" &&
            b.OwnerId == owner && b.Slot == slot).Select(b => b.OperationId).ToListAsync(ct);
        foreach (var publication in publications)
        {
            if (!operations.Contains(publication.OperationId) || publication.ParentArtifactIds is not [var parent]) continue;
            var resultSource = await store.GetArtifactAsync(parent, ct);
            if (resultSource is {Kind: "processedFiles", Managed: true} && AllowsCleanup(Read(resultSource), "keep"))
                result.Add(publication);
        }
        return result;
    }

    private static async Task<AcquisitionRetainedDownload?> VerifiedRetainedDownloadAsync(BakabaseDbContext db,
        IArtifactOperationStore store, AcquisitionStageDirectory? downloadStage, AcquisitionStageDirectory processingStage,
        int taskId, int runId, string partId, ArtifactRecord input, CancellationToken ct)
    {
        if (downloadStage == null || !Same(downloadStage.Directory, input.RootDirectory) ||
            Same(input.RootDirectory, processingStage.Directory) ||
            ArtifactCleanupPathReferences.StrictlyUnder(input.RootDirectory, processingStage.Directory) ||
            ArtifactCleanupPathReferences.StrictlyUnder(processingStage.Directory, input.RootDirectory) ||
            input.State != ArtifactLocationState.Available) return null;
        var original = input;
        if (input.Kind == AcquisitionCurrentDirectoryFiles.Kind)
        {
            if (input.ParentArtifactIds is not [var parent] ||
                !await AcquisitionCurrentDirectoryFiles.IsOwnedAsync(db, store, input, taskId, runId, partId, ct) ||
                await store.GetArtifactAsync(parent, ct) is not {} source || !Same(source.RootDirectory, input.RootDirectory) ||
                !FileDispositionProofs.SameManifest(source.Manifest, input.Manifest)) return null;
            original = source;
        }
        if (original is not {Kind: "download", State: ArtifactLocationState.Available} ||
            original.Fingerprint != input.Fingerprint || original.Fingerprint != original.Manifest.Fingerprint ||
            await store.GetOperationAsync(original.OperationId, ct) is not {State: ArtifactOperationState.Completed} operation ||
            operation.OutputArtifactId != original.Id) return null;
        var files = ArtifactManifestFiles.ResolveFiles(original.RootDirectory, original.Manifest)
            .Select(AcquisitionRetainedDownloadFile.Capture).ToArray();
        if (!(await ArtifactManifestFiles.ValidateAsync(original.RootDirectory, original.Manifest, ct)).Available) return null;
        AcquisitionRetainedDownloadFile.RequireUnchanged(files, ct);
        return new(original.Id, files);
    }

    // The final processing result may have been published directly from its private checkpoint.
    // Recover its original location only from the registered result or the completed quarantine receipt.
    private static async Task AddDeliveredCheckpointCopiesAsync(BakabaseDbContext db, IArtifactOperationStore store,
        FileProcessingWorkspaceResolver workspaces, AcquisitionStageDirectory stage, string owner, string partId,
        ArtifactRecord input, IReadOnlyList<ArtifactRecord> publications, Dictionary<string, ArtifactManifestEntry> files,
        CancellationToken ct, int? checkpointTaskId, HashSet<string> proofIds)
    {
        foreach (var publication in publications)
        {
            if (publication.ParentArtifactIds is not [var id] ||
                !await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OperationId == publication.OperationId &&
                    b.OwnerKind == "workflow" && b.OwnerId == owner && b.Slot == partId + ":artifact.publish", ct)) continue;
            var output = await store.GetArtifactAsync(id, ct);
            if (checkpointTaskId is {} taskId && output != null && Read(output)?.StageCommit is {} handoff)
                output = await RetiredCheckpointProducerAsync(db, store, workspaces, stage, owner, partId, taskId, output, handoff, ct);
            if (output is not {Kind: "processedFiles", Managed: true, State: ArtifactLocationState.Available or ArtifactLocationState.Retired} || !output.ParentArtifactIds.SequenceEqual([input.Id]) ||
                await store.GetOperationAsync(output.OperationId, ct) is not {Kind: "files.process", State: ArtifactOperationState.Completed} producer ||
                producer.InputFingerprint != input.Fingerprint || producer.OutputArtifactId != output.Id) continue;
            var metadata = Read(output);
            if (metadata?.InputArtifactId != input.Id || !AllowsCleanup(metadata, "keep") ||
                metadata.WorkspaceDirectoryScope != stage.Allocation || metadata.CompletedThroughStepId is not {} last ||
                !FileDispositionProofs.ValidFor(metadata.FileDisposition, input, output) ||
                !(await ArtifactFileCoverage.EvaluateAsync(store, output, [publication.Id], ct)).Complete) continue;
            var recipe = JsonSerializer.Deserialize<FileProcessingPlan>(metadata.PlanJson, Json);
            if (recipe?.Steps.LastOrDefault()?.Id != last ||
                !await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OperationId == output.OperationId &&
                    b.OwnerKind == "workflow" && b.OwnerId == owner && (b.Slot == partId + ":process:complete" || b.Slot == partId + ":process:" + last), ct)) continue;
            var registered = await db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == output.Id, ct);
            var original = await workspaces.ReadOriginalArtifactDirectoryAsync(registered, ct);
            var locator = await workspaces.InspectOwnedCheckpointForStageCleanupAsync(FileArtifactProcessor.CheckpointKey(input, recipe),
                output.OperationId, producer.Attempt, stage.Directory, ct);
            if (original == null || locator == null || !ArtifactCleanupPathReferences.StrictlyUnder(locator.Directory, stage.Directory) ||
                !await workspaces.HasOwnedLocationAsync(locator, ct)) continue;
            var checkpointDirectory = FileProcessingPlanExecutor.CheckpointInputDirectory(locator.Directory, recipe, input, workspaces.IdentityPath);
            if (!Same(original, Path.Combine(checkpointDirectory, "result"))) continue;
            var completed = output with {RootDirectory = original};
            var snapshot = await FileProcessingPlanExecutor.ReadReclaimableInputAsync(locator.Directory, recipe, input, completed,
                last, workspaces.IdentityPath, ct, "keep");
            if (snapshot == null) continue;
            if (!AddEntries(snapshot.InputManifest.Entries, Path.Combine(snapshot.Directory, "inputs"), stage.Directory, files))
                throw new InvalidDataException("Conflicting processing input manifests.");
            if (checkpointTaskId != null)
            {
                proofIds.Add(output.Id);
                proofIds.Add(id);
                // This follow-up owns only the recorded input snapshot. Old step-output
                // ledgers can still describe files independently retired by earlier passes.
                continue;
            }
            IReadOnlyList<ArtifactManifestEntry> intermediate;
            try
            {
                var prior = await RegisteredBoundariesAsync(db, store, workspaces, input, recipe, metadata, owner, partId, snapshot.Directory, ct);
                intermediate = await FileProcessingPlanExecutor.ReadReclaimableStepOutputsAsync(snapshot, recipe, input, completed, prior, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                InvalidDataException or ArgumentException or InvalidOperationException)
            { continue; }
            if (!AddEntries(intermediate, snapshot.Directory, stage.Directory, files))
                throw new InvalidDataException("Conflicting processing output manifests.");
        }
    }

    private static async Task<ArtifactRecord?> RetiredCheckpointProducerAsync(BakabaseDbContext db, IArtifactOperationStore store,
        FileProcessingWorkspaceResolver workspaces, AcquisitionStageDirectory stage, string owner, string partId,
        int taskId, ArtifactRecord registered, AcquisitionProcessingStageCommitReceipt evidence, CancellationToken ct)
    {
        if (registered is not {Kind: "processedFiles", Managed: true, State: ArtifactLocationState.Retired} ||
            !AllowsCleanup(Read(registered), "keep") || evidence.Version != 1 || evidence.TaskId != taskId ||
            evidence.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture) != owner || evidence.PartId != partId ||
            evidence.BindingId != stage.BindingId || !Same(evidence.Directory, stage.Directory) ||
            evidence.Id != registered.OperationId || !registered.ParentArtifactIds.SequenceEqual([evidence.SourceArtifactId]) ||
            evidence.Id != ArtifactManifestFiles.Hash($"processing-stage-v1:{taskId}:{partId}:{stage.BindingId}:{evidence.SourceArtifactId}") ||
            !await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OperationId == registered.OperationId &&
                    b.OwnerKind == "workflow" && b.OwnerId == owner && b.Slot == partId + ":process:stage:" + registered.Id, ct)) return null;
        var row = await db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == registered.Id, ct);
        var original = await workspaces.ReadOriginalArtifactDirectoryAsync(row, ct);
        if (original == null || !Same(original, stage.Directory)) return null;
        var source = await store.GetArtifactAsync(evidence.SourceArtifactId, ct);
        var operation = await store.GetOperationAsync(registered.OperationId, ct);
        if (source is not {Kind: "processedFiles", Managed: true, State: ArtifactLocationState.Retired} ||
            source.Fingerprint != evidence.SourceFingerprint ||
            operation is not {Kind: "files.process", State: ArtifactOperationState.Completed} ||
            operation.OutputArtifactId != registered.Id || operation.InputFingerprint != source.Fingerprint ||
            !FileDispositionProofs.SameManifest(source.Manifest, registered.Manifest) ||
            !FileDispositionProofs.ValidFor(Read(registered)?.FileDisposition, source, registered)) return null;
        var custody = await AcquisitionTaskCleanupViews.OwnedClosuresAsync(db, [registered.Id, source.Id], [owner], ct);
        if (!custody.Contains(registered.Id) || !custody.Contains(source.Id)) return null;
        var path = Path.Combine(workspaces.StageCommitMetadataDirectory, evidence.Id + ".json");
        ArtifactManifestFiles.RejectLinks(path);
        if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024 * 1024) return null;
        var journal = JsonSerializer.Deserialize<AcquisitionProcessingStageCommitReceipt>(await File.ReadAllTextAsync(path, ct), Json);
        var producerRow = await db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == source.Id, ct);
        var producerRoot = await workspaces.ReadOriginalArtifactDirectoryAsync(producerRow, ct);
        return producerRoot != null && Same(evidence.SourceDirectory, producerRoot) &&
            OwnsCommittedFiles(registered with {RootDirectory = original}, evidence, journal) ? source : null;
    }

    private static async Task<Dictionary<string, ArtifactRecord>> RegisteredBoundariesAsync(BakabaseDbContext db,
        IArtifactOperationStore store, FileProcessingWorkspaceResolver workspaces, ArtifactRecord input, FileProcessingPlan recipe,
        FileArtifactProcessingMetadata final, string owner, string partId, string checkpoint, CancellationToken ct)
    {
        var result = new Dictionary<string, ArtifactRecord>(StringComparer.Ordinal);
        foreach (var step in recipe.Steps.SkipLast(1))
        {
            if (step.Op != "extractArchive") continue;
            var slot = partId + ":process:" + step.Id;
            var bound = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OwnerKind == "workflow" &&
                b.OwnerId == owner && b.Slot == slot).Select(b => b.OperationId).SingleOrDefaultAsync(ct);
            if (bound == null || await store.GetOperationAsync(bound, ct) is not
                {Kind: "files.process", State: ArtifactOperationState.Completed, OutputArtifactId: {} id} operation ||
                operation.InputFingerprint != input.Fingerprint || await store.GetArtifactAsync(id, ct) is not
                {Kind: "processedFiles", Managed: true, State: ArtifactLocationState.Available or ArtifactLocationState.Retired} artifact || artifact.OperationId != bound ||
                !artifact.ParentArtifactIds.SequenceEqual([input.Id])) continue;
            var metadata = Read(artifact);
            if (metadata?.InputArtifactId != input.Id || metadata.CompletedThroughStepId != step.Id ||
                metadata.PlanJson != final.PlanJson || metadata.WorkspaceDirectoryScope != final.WorkspaceDirectoryScope ||
                !AllowsCleanup(metadata, "keep") || !FileDispositionProofs.ValidFor(metadata.FileDisposition, input, artifact)) continue;
            var record = await db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == id, ct);
            var original = await workspaces.ReadOriginalArtifactDirectoryAsync(record, ct);
            if (original != null && ArtifactCleanupPathReferences.StrictlyUnder(original, checkpoint))
                result.Add(step.Id, artifact with {RootDirectory = original});
        }
        return result;
    }
}
