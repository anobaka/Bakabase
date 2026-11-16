using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionTaskDeletionCleanupService
{
    internal sealed record StageDirectoryCandidate(string Id, int TaskId, int TaskRevision, string PartId,
        string Stage, string RootDirectory, string BindingFingerprint,
        IReadOnlyList<AcquisitionTaskDeleteCleanupFile> Files, IReadOnlyList<string> Directories,
        IReadOnlyDictionary<string, string> FileIdentities, AcquisitionTaskDeleteCleanupGroup Group,
        string SelectionId, bool CleanupIntermediates, bool DeleteDeliveredFiles);

    // An explicit accepted whole-task cleanup owns directory selections, not an old
    // extraction manifest. This does not opt automatic intermediate cleanup into tree deletion.
    internal async Task<IReadOnlyList<StageDirectoryCandidate>> PreviewStageDirectoriesForDeletionAsync(
        AcquisitionTaskDbModel task, IReadOnlyList<int> owners, ArtifactDeliveryCleanupAuthorization authorization,
        bool cleanupIntermediates, bool deleteDeliveredFiles, CancellationToken ct,
        Func<string, Task>? guardDirectory = null)
    {
        if (workspaces == null || !authorization.Consent.DeliveryAccepted || !authorization.Consent.SkipFileVerification) return [];
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson ?? "null", JsonSerializerOptions.Web);
        if (plan?.StageDirectories is not { } stages) return [];
        AcquisitionStageDirectoryResolver.Validate(stages, plan);
        if (stages.TaskId != task.Id) throw new InvalidDataException("CLEANUP_DELIVERY_PROOF_MISSING");
        var selection = await StageCleanupSelectionsAsync(task, plan, owners, cleanupIntermediates, deleteDeliveredFiles, ct);
        var result = new List<StageDirectoryCandidate>();
        // Inspect maximal roots first. A proven parent covers its selected descendants;
        // a refused parent never hides a smaller independently safe selection.
        foreach (var candidate in selection.Selected.OrderBy(s => s.Entry.Directory.Length))
        {
            var root = Path.GetFullPath(candidate.Entry.Directory);
            if (result.Any(r => r.Group.Eligible && (ArtifactManifestFiles.PathComparer.Equals(r.RootDirectory, root) ||
                ArtifactCleanupPathReferences.StrictlyUnder(root, r.RootDirectory)))) continue;
            result.Add(await InspectStageDirectoryForDeletionAsync(task, plan, candidate, selection, owners,
                authorization, cleanupIntermediates, deleteDeliveredFiles, ct, guardDirectory));
        }
        return result;
    }

    private async Task<StageDirectoryCandidate> InspectStageDirectoryForDeletionAsync(AcquisitionTaskDbModel task,
        AcquisitionPlan plan, StageCleanupSelection selected, StageCleanupSelectionSet selections, IReadOnlyList<int> owners,
        ArtifactDeliveryCleanupAuthorization authorization, bool cleanupIntermediates, bool deleteDeliveredFiles,
        CancellationToken ct, Func<string, Task>? guardDirectory)
    {
        var (partId, stage, entry) = (selected.PartId, selected.Stage, selected.Entry);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entry.Directory));
        var id = "stage-directory:" + task.Id + ":" + partId + ":" + stage + ":" + ArtifactManifestFiles.Hash(root);
        var binding = ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new {task.Id, task.PlanRevision,
            SelectionId = selected.Id, partId, stage, Entry = entry, selected.Workspace, cleanupIntermediates, deleteDeliveredFiles}));
        var files = new List<AcquisitionTaskDeleteCleanupFile>(); var directories = new List<string>();
        var identities = new Dictionary<string, string>(ArtifactManifestFiles.PathComparer);
        StageDirectoryCandidate Result(string? reason) => new(id, task.Id, task.PlanRevision, partId, stage, root,
            binding, files, directories, identities, new(id, stage switch {"download" => "download", "delivery" => "publishedFiles", _ => "processedFiles"},
                root, reason == null || reason == "AlreadyAbsent", reason, [], files, files.Count, files.Sum(f => f.Bytes),
                Shared: reason is "ReferencedPath" or "SharedOwner" or "ActiveConsumer", SizeUnknown: reason != null && reason != "AlreadyAbsent"),
            selected.Id, cleanupIntermediates, deleteDeliveredFiles);
        try
        {
            await EnsureDirectoryAuthorizedAsync(root, guardDirectory);
            storage.EnsureTreeMutationAllowed(root); ArtifactManifestFiles.RejectLinks(root);
            if (Path.GetDirectoryName(root) is not { } parent || ArtifactManifestFiles.PathComparer.Equals(root, Path.GetPathRoot(root)))
                return Result("ReferencedPath");
            if (workspaces != null && StageCleanupOverlap(root, workspaces.StageCleanupMetadataDirectory))
                return Result("ReferencedPath");
            // Selected same-task roots may nest. Any unselected stage/publication,
            // including an earlier delivery, remains protected even inside an owned root.
            if (selections.ProtectedRoots.Any(p => StageCleanupOverlap(root, p))) return Result("ReferencedPath");
            if (selections.ConfiguredRoots.Any(p => StageCleanupOverlap(root, p) &&
                    !ArtifactCleanupPathReferences.StrictlyUnder(root, p))) return Result("ReferencedPath");
            await RequireStageCleanupInactiveAsync(task, owners, authorization, ct);
            int? ownDownload = null;
            var exists = Directory.Exists(root);
            if (entry is {Ownership: "exclusive", Evidence: "taskAllocation", Allocation: { } allocation})
            {
                FileProcessingWorkspaceResolver.ValidateDirectoryScope(allocation);
                if (workspaces == null || allocation.OwnerKind != "acquisition" || allocation.OwnerId != task.Id.ToString(CultureInfo.InvariantCulture) ||
                    allocation.Role != stage + "-" + partId || !ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(allocation.Directory), root) ||
                    exists && !await workspaces.HasScopedDirectoryAsync(allocation, ct)) return Result("CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED");
                if (stage == "download") ownDownload = await StageOwnedDownloadAsync(task, owners, partId, root, plan, ct);
            }
            else if (selected.Workspace is { } workspace)
            {
                if (workspaces == null || exists && !await workspaces.HasOwnedLocationAsync(workspace, ct))
                    return Result("CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED");
            }
            else if (entry.Evidence == "userDirectoryRevision" && !string.IsNullOrEmpty(entry.BindingId))
            {
                // The user explicitly bound this exact directory, and selected its stage
                // for accepted deletion. 'borrowed' controls processing reuse, not this
                // separate destructive consent. External claims are still checked below.
                if (stage == "download") ownDownload = await StageOwnedDownloadAsync(task, owners, partId, root, plan, ct);
            }
            else if (stage == "delivery" && entry.Evidence is "taskLayout" or "plannedDelivery" or "deliveryRecord" or "publicationRecord")
            {
                if (!selections.PublicationRoots.Contains(root, ArtifactManifestFiles.PathComparer) ||
                    !selections.DeliveryLayoutRoots.Contains(root, ArtifactManifestFiles.PathComparer))
                    return Result("CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED");
            }
            else if (stage == "download" && entry is {Ownership: "borrowed", Evidence: "downloadRecord"})
            {
                ownDownload = await StageOwnedDownloadAsync(task, owners, partId, root, plan, ct);
                if (ownDownload == null) return Result("CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED");
            }
            else if (!(selected.Current && stage == "processing" && entry.Evidence == "processingRecord"))
                return Result("CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED");
            await new AcquisitionStageDirectoryReferenceGuard(db, artifacts, storage, workspaces)
                .RequireAsync(task, owners.ToArray(), partId, root, ct, ownDownload, explicitTaskCleanup: true);
            if (!exists)
                return Result(ArtifactCleanupAbsence.Probe(parent, root, ct) == ArtifactCleanupPathPresence.ConfirmedAbsent
                    ? "AlreadyAbsent" : "WorkspaceUnavailable");
            var pending = new Stack<(string Path, int Depth)>(); pending.Push((root, 0)); var count = 0;
            while (pending.TryPop(out var current))
            {
                ct.ThrowIfCancellationRequested(); ArtifactManifestFiles.RejectLinks(current.Path);
                if (current.Depth > 128 || ++count > 100000) return Result("CLEANUP_FILES_RETAINED");
                foreach (var path in Directory.EnumerateFileSystemEntries(current.Path))
                {
                    ct.ThrowIfCancellationRequested(); ArtifactManifestFiles.RejectLinks(path); storage.EnsurePathAllowed(path);
                    if (Directory.Exists(path)) {directories.Add(path); pending.Push((path, current.Depth + 1)); continue;}
                    if (++count > 100000) return Result("CLEANUP_FILES_RETAINED");
                    var file = new FileInfo(path); file.Refresh();
                    if (!file.Exists || file.Attributes.HasFlag(FileAttributes.Directory)) return Result("SelectionChanged");
                    files.Add(new(path, file.Length));
                    identities.Add(path, JsonSerializer.Serialize(new {file.Length, file.CreationTimeUtc, file.LastWriteTimeUtc}));
                }
            }
            return Result(null);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            return Result(error.Message switch
            {
                "STAGE_DIRECTORY_SHARED" or "STAGE_DIRECTORY_REFERENCED" => "ReferencedPath",
                "STAGE_DIRECTORY_TASK_ACTIVE" => "ActiveConsumer",
                "STAGE_DIRECTORY_OWNERSHIP_UNKNOWN" => "SharedOwner",
                _ => error is IOException or UnauthorizedAccessException ? "WorkspaceUnavailable" : "CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED"
            });
        }
    }

    private async Task RequireStageCleanupInactiveAsync(AcquisitionTaskDbModel task, IReadOnlyList<int> owners,
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct)
    {
        var previewInactive = authorization.PreviewSupersededRuns;
        if (task.Status != AcquisitionStatus.Completed || task.WorkflowRunId == null || !owners.Contains(task.WorkflowRunId.Value))
            throw new InvalidOperationException("STAGE_DIRECTORY_TASK_ACTIVE");
        var taskKey = task.Id.ToString(CultureInfo.InvariantCulture);
        var sources = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.Module == "acquisition" &&
            s.EntityKind == "task" && (s.EntityId == taskKey || owners.Contains(s.WorkflowRunId))).ToListAsync(ct);
        if (sources.Any(s => s.EntityId != taskKey) || !sources.Select(s => s.WorkflowRunId).Distinct().ToHashSet().SetEquals(owners))
            throw new InvalidOperationException("STAGE_DIRECTORY_OWNERSHIP_UNKNOWN");
        if (await db.WorkflowRuns.CountAsync(r => owners.Contains(r.Id), ct) != owners.Count ||
            await db.WorkflowRuns.AnyAsync(r => owners.Contains(r.Id) && (r.Status == WorkflowRunStatus.Running ||
                r.Status == WorkflowRunStatus.Pending || r.PendingSignalJson != null ||
                r.Status == WorkflowRunStatus.Waiting && !previewInactive.Contains(r.Id)), ct))
            throw new InvalidOperationException("STAGE_DIRECTORY_TASK_ACTIVE");
        if (AcquisitionLocalRecoveryQueueEntry.Read(task) is {IsOpen: true} || AcquisitionContinuationEntry.Read(task) is {IsOpen: true})
            throw new InvalidOperationException("STAGE_DIRECTORY_TASK_ACTIVE");
        using var state = JsonDocument.Parse(task.BuilderStateJson ?? "{}");
        if (state.RootElement.ValueKind == JsonValueKind.Object && state.RootElement.EnumerateObject().Any(p =>
            p.Name is "processingStageCommitId" or "processingStageInputId" or "directoryChange" && p.Value.ValueKind != JsonValueKind.Null))
            throw new InvalidOperationException("STAGE_DIRECTORY_TASK_ACTIVE");
        var keys = owners.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        var operations = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OwnerKind == "workflow" && keys.Contains(b.OwnerId))
            .Select(b => b.OperationId).Distinct().ToArrayAsync(ct);
        if (await db.ArtifactOperations.AnyAsync(o => operations.Contains(o.Id) && (o.State == ArtifactOperationState.Running || o.LeaseUntil > DateTime.UtcNow), ct))
            throw new InvalidOperationException("STAGE_DIRECTORY_TASK_ACTIVE");
        foreach (var operation in operations)
            if (await authorization.HasActiveConsumersAsync(operation, ct)) throw new InvalidOperationException("STAGE_DIRECTORY_TASK_ACTIVE");
        var downloads = await db.CloudDownloadOperations.AsNoTracking().Where(o => owners.Contains(o.WorkflowRunId) && o.DownloadTaskId != null)
            .Select(o => o.DownloadTaskId!.Value).ToArrayAsync(ct);
        var resultDownloads = await db.DownloadResultOwners.AsNoTracking().Where(o => o.AcquisitionTaskId == task.Id)
            .Select(o => o.DownloadTaskId).ToArrayAsync(ct);
        downloads = downloads.Concat(resultDownloads).Distinct().ToArray();
        if (await db.DownloadTasks.AnyAsync(d => downloads.Contains(d.Id) && d.Status == DownloadTaskDbModelStatus.InProgress, ct))
            throw new InvalidOperationException("STAGE_DIRECTORY_TASK_ACTIVE");
    }

    private async Task<int?> StageOwnedDownloadAsync(AcquisitionTaskDbModel task, IReadOnlyList<int> owners,
        string partId, string root, AcquisitionPlan plan, CancellationToken ct)
    {
        var rows = await db.DownloadTasks.AsNoTracking().Where(d => d.Status == DownloadTaskDbModelStatus.Complete).ToListAsync(ct);
        var matches = rows.Where(d => !string.IsNullOrWhiteSpace(d.DownloadPath) &&
            ArtifactManifestFiles.PathComparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(d.DownloadPath)), root)).ToArray();
        if (matches.Length != 1) return null;
        var id = matches[0].Id;
        var operations = await db.CloudDownloadOperations.AsNoTracking().Where(o => o.DownloadTaskId == id).ToListAsync(ct);
        if (operations.Count == 0 || operations.Any(o => o.Kind != "download" || o.TargetId != partId ||
            !owners.Contains(o.WorkflowRunId) || o.State is not ("started" or "completed")) ||
            await db.DownloadResultOwners.AnyAsync(o => o.DownloadTaskId == id && o.AcquisitionTaskId != task.Id, ct)) return null;
        // A completed cloud transport's frozen concrete destination is an allocation,
        // unlike an acquisition's historical shared output root. The native task can have
        // been moved to another storage volume without changing its semantic plan root.
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(matches[0].Options ?? "null", JsonSerializerOptions.Web);
        if (request == null || string.IsNullOrWhiteSpace(request.Source?.Id) || string.IsNullOrWhiteSpace(request.Source.Url) ||
            operations.Any(o => o.Id != request.OperationId) ||
            !ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(CloudDownloadDestinations.ExecutionDirectory(request)), root)) return null;
        return id;
    }

    internal async Task<ArtifactWorkspaceCleanupResult> DeleteStageDirectoryAsync(StageDirectoryCandidate expected,
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct)
    {
        if (!authorization.Consent.DeliveryAccepted || !authorization.Consent.SkipFileVerification || !expected.Group.Eligible)
            return new(expected.Id, false, "CLEANUP_DELIVERY_PROOF_MISSING");
        AcquisitionStageDirectoryCleanupReceipt? receipt = null;
        try
        {
            await using var fence = await artifacts.AcquireMaintenanceAsync(ct);
            var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == expected.TaskId, ct);
            if (task == null || task.PlanRevision != expected.TaskRevision || workspaces == null) return new(expected.Id, false, "SelectionChanged");
            var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson ?? "null", JsonSerializerOptions.Web);
            if (plan == null) return new(expected.Id, false, "SelectionChanged");
            var selections = await StageCleanupSelectionsAsync(task, plan, authorization.Consent.OwnerRunIds,
                expected.CleanupIntermediates, expected.DeleteDeliveredFiles, ct);
            var selected = selections.Selected.SingleOrDefault(s => s.Id == expected.SelectionId);
            if (selected == null) return new(expected.Id, false, "SelectionChanged");
            var current = await InspectStageDirectoryForDeletionAsync(task, plan, selected, selections,
                authorization.Consent.OwnerRunIds, authorization, expected.CleanupIntermediates,
                expected.DeleteDeliveredFiles, ct, null);
            if (!current.Group.Eligible) return new(expected.Id, false, current.Group.ReasonCode);
            if (current.RootDirectory != expected.RootDirectory || current.BindingFingerprint != expected.BindingFingerprint ||
                current.FileIdentities.Any(p => !expected.FileIdentities.TryGetValue(p.Key, out var prior) || p.Value != prior) ||
                current.Directories.Any(p => !expected.Directories.Contains(p, ArtifactManifestFiles.PathComparer)))
                return new(expected.Id, false, "SelectionChanged");
            if (current.Group.ReasonCode == "AlreadyAbsent") return new(expected.Id, true, "AlreadyAbsent");
            receipt = new(Guid.NewGuid().ToString("N"), task.Id, task.PlanRevision, expected.PartId, expected.Stage,
                current.RootDirectory, current.BindingFingerprint, DateTime.UtcNow, "Prepared", current.Files,
                authorization.Consent);
            await AcquisitionStageDirectoryCleanupReceipt.SaveAsync(workspaces.StageCleanupMetadataDirectory, receipt, ct);
            ct.ThrowIfCancellationRequested();
            // Every current descendant was inspected for links and foreign claims above.
            // Root selection is frozen; there is no hash or old-manifest reconstruction.
            Directory.Delete(current.RootDirectory, recursive: true);
            var absent = ArtifactCleanupAbsence.Probe(Path.GetDirectoryName(current.RootDirectory)!, current.RootDirectory, ct)
                == ArtifactCleanupPathPresence.ConfirmedAbsent;
            await AcquisitionStageDirectoryCleanupReceipt.SaveAsync(workspaces.StageCleanupMetadataDirectory,
                receipt with {Status = absent ? "Completed" : "Partial", FinishedAt = DateTime.UtcNow}, CancellationToken.None);
            return new(expected.Id, absent, absent ? null : "CLEANUP_FILES_RETAINED", absent ? current.Files.Count : 0,
                absent ? current.Files.Sum(f => f.Bytes) : 0);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            if (receipt != null && workspaces != null)
                try
                {
                    await AcquisitionStageDirectoryCleanupReceipt.SaveAsync(workspaces.StageCleanupMetadataDirectory,
                        receipt with {Status = error is OperationCanceledException ? "Interrupted" : "Failed", FinishedAt = DateTime.UtcNow}, CancellationToken.None);
                }
                catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            if (error is OperationCanceledException) throw;
            return new(expected.Id, false, "CLEANUP_FILES_RETAINED");
        }
    }

    private static bool StageCleanupOverlap(string a, string b) => ArtifactManifestFiles.PathComparer.Equals(a, b) ||
        ArtifactCleanupPathReferences.StrictlyUnder(a, b) || ArtifactCleanupPathReferences.StrictlyUnder(b, a);
}
