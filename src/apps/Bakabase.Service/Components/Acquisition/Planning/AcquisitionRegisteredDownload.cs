using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionRegisteredDownloadsInput(int ExpectedRevision);
public sealed record AcquisitionRegisteredDownloadSelection(int ExpectedRevision, string Token);
public sealed record AcquisitionRegisteredDownloadCandidate(string SourceId, string SourceTitle, int RunId, int DownloadTaskId,
    string Directory, int FileCount, long TotalBytes, string Token, string Stage);
public sealed record AcquisitionRegisteredDownloadsPreview(IReadOnlyList<AcquisitionRegisteredDownloadCandidate> Candidates);

internal sealed record AcquisitionRegisteredDownload(WorkflowSourceRef Origin, string PartId, string ProducerTargetId,
    int DownloadTaskId, int ResultId, AcquisitionPlanArtifactInput Input, string Directory, string SourceUrl,
    int PreviousRevision, string Token, string Stage = "download")
{
    internal static bool Stopped(WorkflowRunDbModel run) => run.PendingSignalJson == null &&
        run.Status is WorkflowRunStatus.Waiting or WorkflowRunStatus.Failed or WorkflowRunStatus.Success or
            WorkflowRunStatus.Cancelled or WorkflowRunStatus.Interrupted;

    internal static async Task<IReadOnlyList<CloudDownloadTarget>> TargetsAsync(BakabaseDbContext db, IArtifactOperationStore store,
        WorkflowRunDbModel run, CancellationToken ct)
    {
        if (AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson) is {} cursor) return cursor.Targets;
        // Successful runs may discard their cursor. Recover only a selected target from the
        // original payload paired with its producer-owned completed download output.
        if (run.Status != WorkflowRunStatus.Success || Read<DownloadWorkset>(run.PayloadJson) is not {} payload) return [];
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OwnerKind == "workflow" &&
            b.OwnerId == run.Id.ToString() && b.Slot.EndsWith(":artifact.download")).ToListAsync(ct);
        // Historical success previews can omit file arrays. Use only their target and stage
        // locators; the producer ledger, file manifest and live bytes are verified separately.
        var completed = Read<DownloadWorkset[]>(run.OutputItemsJson)?.SelectMany(w => w.Targets).ToLookup(t => t.Id);
        var matches = new List<CloudDownloadTarget>();
        foreach (var original in payload.Targets.Where(t => !t.Excluded))
        {
            var target = original;
            var binding = bindings.SingleOrDefault(b => b.Slot == target.Id + ":artifact.download");
            if (binding == null || (await store.GetOperationAsync(binding.OperationId, ct))?.OutputArtifactId is not {} output) continue;
            var artifact = await store.GetArtifactAsync(output, ct);
            if (artifact == null || Read<DownloadArtifactMetadata>(artifact.MetadataJson)?.DownloadTaskId is not {} native) continue;
            var outputs = completed?[target.Id].Where(t => !t.Excluded && t.SelectedSourceId != null &&
                (target.SelectedSourceId == null || t.SelectedSourceId == target.SelectedSourceId) &&
                t.DownloadArtifactId == artifact.Id && t.DownloadTaskId == native).ToArray();
            if (outputs is [var last])
                target = target with {SelectedSourceId = last.SelectedSourceId, AccountId = target.AccountId ?? last.AccountId,
                    ProcessedArtifactId = last.ProcessedArtifactId, ProcessingOperationId = last.ProcessingOperationId,
                    ProcessedDirectory = last.ProcessedDirectory, PublishedArtifactId = last.PublishedArtifactId,
                    PublicationOperationId = last.PublicationOperationId, PublishedDirectory = last.PublishedDirectory,
                    PublicationStatus = last.PublicationStatus};
            if (target.SelectedSource == null) continue;
            if (target.AccountId == null)
            {
                var slot = CloudOperationService.OperationSlot(target, "download");
                var requests = await db.CloudDownloadOperations.AsNoTracking().Where(o => o.Kind == "download" && o.DownloadTaskId == native &&
                    db.ArtifactOperationBindings.Any(b => b.OwnerKind == "workflow" && b.OwnerId == run.Id.ToString() &&
                        b.Slot == slot && b.OperationId == o.Id)).Select(o => o.RequestJson).Take(2).ToArrayAsync(ct);
                if (requests is [var request]) target = target with {AccountId = Read<CloudDownloadRequest>(request)?.AccountId};
            }
            matches.Add(target with {DownloadTaskId = native, DownloadOperationId = binding.OperationId, DownloadArtifactId = artifact.Id,
                DownloadDirectory = artifact.RootDirectory, Files = artifact.Files.ToList()});
        }
        return matches;
    }

    // Authenticate the producer's source row, workset, transport result, artifact and recorded binding.
    // A title, a directory name, or a URL shared by two unrelated tasks does not establish provenance.
    internal static async Task<ArtifactRecord?> ReadAsync(BakabaseDbContext db, IArtifactOperationStore store,
        int taskId, WorkflowRunDbModel run, WorkflowSourceRef origin, CloudDownloadTarget target, string url, CancellationToken ct)
    {
        if (!Stopped(run) || target.Excluded || target.TransportTaskId != null || target.DownloadTaskId is not {} native ||
            target.DownloadArtifactId == null || target.DownloadOperationId == null || target.SelectedSource is not {} selected ||
            selected.Url != url && selected.OriginalUrl != url) return null;
        var refs = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.WorkflowRunId == run.Id).ToArrayAsync(ct);
        if (await db.AcquisitionTasks.AsNoTracking().AnyAsync(t => t.WorkflowRunId == run.Id && t.Id != taskId, ct) ||
            refs.Any(s => s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId != taskId.ToString()) ||
            !refs.Any(s => s.Module == origin.Module && s.EntityKind == origin.EntityKind && s.EntityId == origin.EntityId &&
                s.Revision == origin.Revision && s.ResultKey == origin.ResultKey)) return null;
        var operation = await store.GetOperationAsync(target.DownloadOperationId, ct);
        if (operation is not {Kind: "download", State: ArtifactOperationState.Completed} ||
            operation.OutputArtifactId != target.DownloadArtifactId || operation.InputFingerprint != DownloadArtifactCoordinator.Source(target))
            return null;
        var artifact = await store.GetArtifactAsync(target.DownloadArtifactId, ct);
        if (artifact is not {Kind: "download", State: ArtifactLocationState.Available} || artifact.OperationId != operation.Id ||
            artifact.Fingerprint != artifact.Manifest.Fingerprint || artifact.Manifest.Entries.Count == 0 ||
            Read<DownloadArtifactMetadata>(artifact.MetadataJson)?.DownloadTaskId != native ||
            !ArtifactManifestFiles.PathComparer.Equals(artifact.RootDirectory, target.DownloadDirectory) ||
            !artifact.Files.ToHashSet(ArtifactManifestFiles.PathComparer).SetEquals(target.Files)) return null;
        if (!await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" &&
                b.OwnerId == run.Id.ToString() && b.OperationId == operation.Id && b.Slot == target.Id + ":artifact.download", ct) ||
            !await db.DownloadTasks.AsNoTracking().AnyAsync(d => d.Id == native && d.Status == DownloadTaskDbModelStatus.Complete, ct))
            return null;
        var downloadOwners = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == operation.Id &&
            b.Slot.EndsWith(":artifact.download")).Select(b => new {b.OwnerKind, b.OwnerId}).Distinct().ToListAsync(ct);
        var acceptable = new HashSet<int> {run.Id};
        foreach (var owner in downloadOwners)
        {
            if (owner.OwnerKind != "workflow" || !int.TryParse(owner.OwnerId, out var otherId)) return null;
            if (otherId == run.Id) continue;
            var other = await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == otherId, ct);
            if (other == null || !Stopped(other) ||
                await db.AcquisitionTasks.AsNoTracking().AnyAsync(t => t.WorkflowRunId == otherId, ct) ||
                await db.Set<WorkflowRunSourceDbModel>().AnyAsync(s => s.WorkflowRunId == otherId && s.Module == "acquisition", ct) ||
                !await db.Set<WorkflowRunSourceDbModel>().AnyAsync(s => s.WorkflowRunId == otherId && s.Module == origin.Module &&
                    s.EntityKind == origin.EntityKind && s.EntityId == origin.EntityId && s.Revision == origin.Revision &&
                    s.ResultKey != null && s.ResultKey != "", ct)) return null;
            acceptable.Add(otherId);
        }
        if (await HasDownloadWriterAsync(db, artifact, ct)) return null;
        var transports = await db.CloudDownloadOperations.AsNoTracking().Where(o => o.Kind == "download" &&
            acceptable.Contains(o.WorkflowRunId) && o.DownloadTaskId == native).ToArrayAsync(ct);
        if (transports is not [var transport] ||
            transport.WorkflowRunId == run.Id && transport.TargetId != target.Id ||
            !await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" &&
                b.OwnerId == run.Id.ToString() && b.OperationId == transport.Id &&
                b.Slot == CloudOperationService.OperationSlot(target, "download"), ct)) return null;
        var request = Read<CloudDownloadRequest>(transport.RequestJson);
        if (request?.Source == null || request.OperationId != transport.Id ||
            CloudSourceIdentity.Key(request.Source, request.AccountId) != operation.InputFingerprint) return null;
        return artifact;
    }

    internal static async Task<ArtifactRecord?> ReadStageAsync(BakabaseDbContext db, IArtifactOperationStore store,
        WorkflowRunDbModel run, CloudDownloadTarget target, ArtifactRecord download, string stage, CancellationToken ct)
    {
        if (stage == "download") return download;
        var delivery = stage == "delivery";
        if ((!delivery && stage != "processing") || delivery && target.PublicationStatus != "delivered") return null;
        var id = delivery ? target.PublishedArtifactId : target.ProcessedArtifactId;
        var op = delivery ? target.PublicationOperationId : target.ProcessingOperationId;
        var root = delivery ? target.PublishedDirectory : target.ProcessedDirectory;
        if (id == null || op == null || root == null) return null;
        var leaf = await store.GetArtifactAsync(id, ct);
        if (leaf == null || leaf.Kind != (delivery ? "publishedFiles" : "processedFiles") || leaf.OperationId != op ||
            leaf.State != ArtifactLocationState.Available || leaf.Manifest.Entries.Count == 0 ||
            leaf.Fingerprint != leaf.Manifest.Fingerprint || !ArtifactManifestFiles.PathComparer.Equals(leaf.RootDirectory, root)) return null;
        var slots = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OwnerKind == "workflow" &&
            b.OwnerId == run.Id.ToString() && b.OperationId == op).Select(b => b.Slot).ToArrayAsync(ct);
        if (delivery ? !slots.Contains(target.Id + ":artifact.publish") : !slots.Any(s =>
            s == target.Id + ":artifact.process" || s.StartsWith(target.Id + ":process:", StringComparison.Ordinal) ||
            s == target.Id + ":adaptive:output:" + id)) return null;
        // A merge from multiple uploads is not proof that the result belongs to this one source.
        var node = leaf;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (node.Id != download.Id)
        {
            if (!seen.Add(node.Id) || seen.Count > 64 || node.ParentArtifactIds is not [var parent] ||
                node.Kind is not ("processedFiles" or "publishedFiles")) return null;
            var producer = await store.GetOperationAsync(node.OperationId, ct);
            if (producer is not {State: ArtifactOperationState.Completed} || producer.OutputArtifactId != node.Id ||
                producer.Kind != (node.Kind == "publishedFiles" ? "files.publish" : "files.process")) return null;
            var upstream = parent == download.Id ? download : await store.GetArtifactAsync(parent, ct);
            if (upstream == null || upstream.Fingerprint != upstream.Manifest.Fingerprint ||
                producer.InputFingerprint != upstream.Fingerprint) return null;
            node = upstream;
        }
        return await HasDownloadWriterAsync(db, leaf, ct) ? null : leaf;
    }

    private static async Task<bool> HasDownloadWriterAsync(BakabaseDbContext db, ArtifactRecord artifact, CancellationToken ct)
    {
        var writing = await db.DownloadTasks.AsNoTracking().Where(d => d.Status == DownloadTaskDbModelStatus.InProgress)
            .Select(d => d.DownloadPath).Distinct().ToListAsync(ct);
        return writing.Where(d => !string.IsNullOrWhiteSpace(d) && Path.IsPathFullyQualified(d)).Any(d =>
            ArtifactManifestFiles.PathComparer.Equals(Path.TrimEndingDirectorySeparator(d), Path.TrimEndingDirectorySeparator(artifact.RootDirectory)) ||
            artifact.Files.Any(file => ArtifactCleanupPathReferences.StrictlyUnder(file, d)));
    }

    internal static T? Read<T>(string? json) where T : class
    {
        try {return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, WorkflowJson.Options);}
        catch (JsonException) {return null;}
    }

    // A foreign workflow never becomes owned by the borrower. Only the exact download authorized
    // by the task-local server receipt may be read, without granting cleanup or publication rights.
    internal async Task<ArtifactRecord?> RequireAsync(BakabaseDbContext db, IArtifactOperationStore store,
        WorkflowRunDbModel run, int taskId, string partId, AcquisitionPlanArtifactInput input, CancellationToken ct)
    {
        if (Input != input || PartId != partId || taskId != input.TaskId || run.Id != input.PreviousRunId) return null;
        var target = (await TargetsAsync(db, store, run, ct))
            .SingleOrDefault(t => t.Id == ProducerTargetId);
        if (target?.DownloadTaskId != DownloadTaskId) return null;
        if (await ReadAsync(db, store, taskId, run, Origin, target, SourceUrl, ct) is not {} source) return null;
        var artifact = await ReadStageAsync(db, store, run, target, source, Stage, ct);
        return artifact?.Id == input.ArtifactId && artifact.Fingerprint == input.Fingerprint &&
            artifact.OperationId == input.OperationId && artifact.RootDirectory == Directory ? artifact : null;
    }
}
