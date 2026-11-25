using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Exceptions;
using Bakabase.InsideWorld.Business;
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
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionTaskDeletionCleanupService
{
    internal sealed record StageDirectoryCandidate(string Id, int TaskId, int TaskRevision, string PartId,
        string Stage, string RootDirectory, string BindingFingerprint,
        IReadOnlyList<AcquisitionTaskDeleteCleanupFile> Files, IReadOnlyList<string> Directories,
        IReadOnlyDictionary<string, string> FileIdentities, AcquisitionTaskDeleteCleanupGroup Group,
        string SelectionId, bool CleanupIntermediates, bool DeleteDeliveredFiles)
    {
        public string? ManifestArtifactId { get; init; }
        public IReadOnlyList<string> CoveredRoots { get; init; } = [];
        public bool DirectDeletion { get; init; }
        public bool PublicationFilesOnly { get; init; }
    }

    // An explicit accepted whole-task cleanup owns directory selections, not an old
    // extraction manifest. This does not opt automatic intermediate cleanup into tree deletion.
    internal async Task<IReadOnlyList<StageDirectoryCandidate>> PreviewStageDirectoriesForDeletionAsync(
        AcquisitionTaskDbModel task, IReadOnlyList<int> owners, ArtifactDeliveryCleanupAuthorization authorization,
        bool cleanupIntermediates, bool deleteDeliveredFiles, CancellationToken ct,
        Func<string, Task>? guardDirectory = null, bool directDirectoryDeletion = false)
    {
        if (workspaces == null || !authorization.Consent.CanDeleteWorkspace) return [];
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson ?? "null", JsonSerializerOptions.Web);
        if (plan == null) return [];
        var selection = await StageCleanupSelectionsAsync(task, plan, owners, cleanupIntermediates, deleteDeliveredFiles, ct);
        // With no persisted stage map, a publication alone cannot account for old
        // intermediate files. Use exact-file cleanup unless there is a recorded download.
        if (!authorization.Consent.WholeTaskFiles && plan.StageDirectories == null && cleanupIntermediates &&
            !selection.Selected.Any(s => s.Stage == "download")) return [];
        var result = new List<StageDirectoryCandidate>();
        // Inspect maximal roots first. A proven parent covers its selected descendants;
        // a refused parent never hides a smaller independently safe selection.
        foreach (var candidate in selection.Selected.OrderBy(s => s.Entry.Directory.Length))
        {
            var root = Path.GetFullPath(candidate.Entry.Directory);
            var parentIndex = result.FindIndex(r => r.Group.Eligible && (ArtifactManifestFiles.PathComparer.Equals(r.RootDirectory, root) ||
                ArtifactCleanupPathReferences.StrictlyUnder(root, r.RootDirectory)));
            if (parentIndex >= 0)
            {
                // A single directory operation must account for every selected stage it covers.
                var parent = result[parentIndex];
                result[parentIndex] = parent with {Group = parent.Group with {Scopes = parent.Group.Scopes | candidate.Scopes}};
                continue;
            }
            var inspected = await InspectStageDirectoryForDeletionAsync(task, plan, candidate, selection, owners,
                authorization, cleanupIntermediates, deleteDeliveredFiles, ct, guardDirectory, directDirectoryDeletion);
            result.Add(inspected with {Group = inspected.Group with {Scopes = candidate.Scopes}});
        }
        return result;
    }

    private async Task<StageDirectoryCandidate> InspectStageDirectoryForDeletionAsync(AcquisitionTaskDbModel task,
        AcquisitionPlan plan, StageCleanupSelection selected, StageCleanupSelectionSet selections, IReadOnlyList<int> owners,
        ArtifactDeliveryCleanupAuthorization authorization, bool cleanupIntermediates, bool deleteDeliveredFiles,
        CancellationToken ct, Func<string, Task>? guardDirectory, bool directDirectoryDeletion = false)
    {
        var (partId, stage, entry) = (selected.PartId, selected.Stage, selected.Entry);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entry.Directory));
        var id = "stage-directory:" + task.Id + ":" + partId + ":" + stage + ":" + ArtifactManifestFiles.Hash(root);
        var binding = ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new {task.Id, task.PlanRevision,
            SelectionId = selected.Id, partId, stage, Entry = entry, selected.Workspace, selected.HistoricalDeliveryLayout,
            cleanupIntermediates, deleteDeliveredFiles}));
        var files = new List<AcquisitionTaskDeleteCleanupFile>(); var directories = new List<string>();
        var identities = new Dictionary<string, string>(ArtifactManifestFiles.PathComparer);
        StageDirectoryCandidate Result(string? reason) => new(id, task.Id, task.PlanRevision, partId, stage, root,
            binding, files, directories, identities, new(id, stage switch {"download" => "download", "delivery" => "publishedFiles", _ => "processedFiles"},
                root, reason == null || reason == "AlreadyAbsent", reason, [], files, files.Count, files.Sum(f => f.Bytes),
                Shared: reason is "ReferencedPath" or "SharedOwner" or "ActiveConsumer", SizeUnknown: directDirectoryDeletion || reason != null && reason != "AlreadyAbsent"),
            selected.Id, cleanupIntermediates, deleteDeliveredFiles) {DirectDeletion = directDirectoryDeletion};
        void LogReference(string reference) => logger?.LogWarning(
            "Acquisition cleanup retained task {TaskId} stage {Stage} due to reference {Reference}",
            task.Id, stage, reference);
        StageDirectoryCandidate Referenced(string reference)
        {
            LogReference(reference);
            return Result("ReferencedPath");
        }
        Task CheckReferencesAsync(int? ownDownload) =>
            new AcquisitionStageDirectoryReferenceGuard(db, artifacts, storage, workspaces)
                .RequireAsync(task, owners.ToArray(), partId, root, ct, ownDownload, explicitTaskCleanup: owners.Count > 0,
                    abandonUnfinishedFiles: authorization.Consent.AbandonUnfinishedFiles,
                    hasActiveConsumers: authorization.HasActiveConsumersAsync,
                    referenceFound: logger == null ? null : LogReference);
        try
        {
            await EnsureDirectoryAuthorizedAsync(root, guardDirectory);
            storage.EnsureTreeMutationAllowed(root); ArtifactManifestFiles.RejectLinks(root);
            if (Path.GetDirectoryName(root) is not { } parent || ArtifactManifestFiles.PathComparer.Equals(root, Path.GetPathRoot(root)))
                return Referenced("filesystem-root");
            if (workspaces != null && StageCleanupOverlap(root, workspaces.StageCleanupMetadataDirectory))
                return Referenced("cleanup-metadata-root");
            // Selected same-task roots may nest. Any unselected stage/publication,
            // including an earlier delivery, remains protected even inside an owned root.
            if (selections.ProtectedRoots.Any(p => StageCleanupOverlap(root, p))) return Referenced("protected-stage-root");
            if (selections.ConfiguredRoots.Any(p => StageCleanupOverlap(root, p) &&
                    !ArtifactCleanupPathReferences.StrictlyUnder(root, p))) return Referenced("configured-root");
            await RequireStageCleanupInactiveAsync(db, task, owners, authorization, ct);
            int? ownDownload = null;
            var exists = Directory.Exists(root);
            // A delivery can be planned before any of its task or part folders exist.
            // Probe from the root recorded by that exact layout, even if the immediate parent is missing.
            var absenceBoundary = selected.DeliveryLayoutRoot ?? parent;
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
            else if (!authorization.Consent.AbandonUnfinishedFiles && entry.Evidence == "userDirectoryRevision" && !string.IsNullOrEmpty(entry.BindingId))
            {
                // The user explicitly bound this exact directory, and selected its stage
                // for accepted deletion. 'borrowed' controls processing reuse, not this
                // separate destructive consent. External claims are still checked below.
                if (stage == "download") ownDownload = await StageOwnedDownloadAsync(task, owners, partId, root, plan, ct);
            }
            else if (stage == "delivery" && entry.Evidence is "taskLayout" or "plannedDelivery" or "deliveryRecord" or "publicationRecord")
            {
                var published = selections.PublicationRoots.Contains(root, ArtifactManifestFiles.PathComparer);
                var hasLayout = selections.DeliveryLayoutRoots.Contains(root, ArtifactManifestFiles.PathComparer);
                if (!hasLayout || !published && !(authorization.Consent.WholeTaskFiles && selected.Current))
                {
                    // A superseded layout may have been saved before publication ever ran.
                    // With an accessible layout root, a confirmed missing path needs no ownership grant.
                    if (selected.HistoricalDeliveryLayout && !exists)
                    {
                        var presence = ArtifactCleanupAbsence.Probe(absenceBoundary, root, ct);
                        if (presence == ArtifactCleanupPathPresence.ConfirmedAbsent) return Result("AlreadyAbsent");
                        if (presence == ArtifactCleanupPathPresence.Unavailable) return Result("WorkspaceUnavailable");
                    }
                    // A publication receipt records exact files independently of who owns their container.
                    // Never promote a refused stage to directory ownership or cross another selected stage.
                    if (published && !hasLayout && deleteDeliveredFiles && selected.Scopes == AcquisitionCleanupScopes.Delivery &&
                        !selections.Selected.Any(other => other.Id != selected.Id && StageCleanupOverlap(root, other.Entry.Directory)))
                    {
                        var publications = authorization.Consent.Publications.Where(p => StageCleanupOverlap(root, p.RootDirectory)).Take(2).ToArray();
                        if (publications.Length == 1 && SamePath(publications[0].RootDirectory, root))
                        {
                            await CheckReferencesAsync(null);
                            var manifest = await WholeTaskArtifactAsync(task, authorization, publications[0].ArtifactId,
                                cleanupIntermediates, [], new ArtifactCleanupReferenceGuard(db, workspaces), ct, guardDirectory,
                                publicationFilesOnly: true);
                            if (manifest != null)
                                return manifest with {PartId = partId, SelectionId = selected.Id, BindingFingerprint =
                                    ArtifactManifestFiles.Hash(binding + ":" + manifest.BindingFingerprint)};
                        }
                    }
                    return Result("CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED");
                }
            }
            else if (stage == "download" && entry is {Ownership: "borrowed", Evidence: "retainedDownloadSource", BindingId: {} revisionId})
            {
                // The current native destination is different. Prove its unique operation still records the
                // selected old source, then run the usual full foreign-reference check for the historical root.
                if (!await OwnsRetainedDownloadSourceAsync(task.Id, owners, partId, root, revisionId, ct))
                    return Result("CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED");
            }
            else if (stage == "download" && entry is {Ownership: "borrowed", Evidence: "downloadRecord"})
            {
                ownDownload = await StageOwnedDownloadAsync(task, owners, partId, root, plan, ct);
                if (ownDownload == null) return Result("CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED");
            }
            else if (!(selected.Current && !authorization.Consent.AbandonUnfinishedFiles && stage == "processing" && entry.Evidence == "processingRecord"))
                return Result("CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED");
            await CheckReferencesAsync(ownDownload);
            if (!exists)
                return Result(ArtifactCleanupAbsence.Probe(absenceBoundary, root, ct) == ArtifactCleanupPathPresence.ConfirmedAbsent
                    ? "AlreadyAbsent" : "WorkspaceUnavailable");
            if (directDirectoryDeletion) return Result(null);
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
        catch (UserStoragePathException error) when (error.MountUnavailable)
        {
            return Result("WorkspaceUnavailable");
        }
        catch (Exception error) when (error is not (UserStoragePathException or UnauthorizedAccessException) &&
            error is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException)
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

    internal static async Task RequireStageCleanupInactiveAsync(BakabaseDbContext db, AcquisitionTaskDbModel task, IReadOnlyList<int> owners,
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct)
    {
        var previewInactive = authorization.PreviewSupersededRuns;
        var taskInactive = authorization.Consent.WholeTaskFiles || authorization.Consent.AbandonUnfinishedFiles
            ? await authorization.IsCurrentAsync(ct) : task.Status == AcquisitionStatus.Completed;
        var noCurrentRun = authorization.Consent.WholeTaskFiles && task.WorkflowRunId == null;
        if (!taskInactive || !noCurrentRun && (task.WorkflowRunId == null || !owners.Contains(task.WorkflowRunId.Value)))
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
        // A pending stage journal is a recoverable intent, not a live writer. Explicit
        // whole-task deletion may abandon an interrupted move/input after the task/run
        // gates and runtime checks have quiesced it. Keep the journal as history; normal
        // stage edits and automatic cleanup must still wait for the hand-off to finish.
        if (!authorization.Consent.WholeTaskFiles && state.RootElement.ValueKind == JsonValueKind.Object && state.RootElement.EnumerateObject().Any(p =>
            p.Name is "processingStageCommitId" or "processingStageInputId" or "directoryChange" && p.Value.ValueKind != JsonValueKind.Null))
            throw new InvalidOperationException("STAGE_DIRECTORY_TASK_ACTIVE");
        var keys = owners.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        var operations = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OwnerKind == "workflow" && keys.Contains(b.OwnerId))
            .Select(b => b.OperationId).Distinct().ToArrayAsync(ct);
        if (await db.ArtifactOperations.AnyAsync(o => operations.Contains(o.Id) && (o.State == ArtifactOperationState.Running || o.LeaseUntil > DateTime.UtcNow), ct))
            throw new InvalidOperationException("STAGE_DIRECTORY_TASK_ACTIVE");
        // A task can have read an independently owned source elsewhere. Its consumer
        // does not hold every current stage directory. Whole-task deletion checks these
        // leases/pins only on registrations overlapping each selected directory below.
        if (!authorization.Consent.WholeTaskFiles)
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

    private async Task<bool> OwnsRetainedDownloadSourceAsync(int taskId, IReadOnlyList<int> owners,
        string partId, string root, string revisionId, CancellationToken ct)
    {
        var candidates = await (from op in db.CloudDownloadOperations.AsNoTracking()
            join native in db.DownloadTasks.AsNoTracking() on op.DownloadTaskId equals native.Id
            where owners.Contains(op.WorkflowRunId) && op.TargetId == partId && op.Kind == "download" &&
                (op.State == "started" || op.State == "completed") && native.Status != DownloadTaskDbModelStatus.InProgress &&
                op.RequestJson == native.Options && native.Options != null && native.Options.Contains(revisionId)
            select new {Operation = op.Id, Native = native.Id, native.Options, native.DownloadPath}).Take(2).ToArrayAsync(ct);
        if (candidates.Length != 1) return false;
        var row = candidates[0];
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(row.Options!, JsonSerializerOptions.Web);
        if (request?.OperationId != row.Operation || string.IsNullOrWhiteSpace(request.Source?.Id) || string.IsNullOrWhiteSpace(request.Source.Url) ||
            !SamePath(CloudDownloadDestinations.ExecutionDirectory(request), row.DownloadPath) ||
            !CloudDownloadDestinations.RetainedSources(request).Any(r => r.RevisionId == revisionId && SamePath(r.PreviousDirectory, root)))
            return false;
        return !await db.CloudDownloadOperations.AnyAsync(o => o.DownloadTaskId == row.Native && o.Id != row.Operation, ct) &&
            !await db.DownloadResultOwners.AnyAsync(o => o.DownloadTaskId == row.Native &&
                (o.AcquisitionTaskId != taskId || !owners.Contains(o.WorkflowRunId)), ct);
    }

    private async Task<int?> StageOwnedDownloadAsync(AcquisitionTaskDbModel task, IReadOnlyList<int> owners,
        string partId, string root, AcquisitionPlan plan, CancellationToken ct)
    {
        var rows = await db.DownloadTasks.AsNoTracking().Where(d => d.Status != DownloadTaskDbModelStatus.InProgress).ToListAsync(ct);
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
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct, Action<string>? deferred = null)
    {
        if (!authorization.Consent.CanDeleteWorkspace || !expected.Group.Eligible)
            return new(expected.Id, false, "CLEANUP_DELIVERY_PROOF_MISSING");
        AcquisitionStageDirectoryCleanupReceipt? receipt = null;
        var removed = 0;
        long removedBytes = 0;
        try
        {
            var publication = expected.PublicationFilesOnly && expected.DeleteDeliveredFiles && expected.Group.RegisteredFilesOnly
                ? authorization.Consent.Publications.SingleOrDefault(p => p.ArtifactId == expected.ManifestArtifactId &&
                    p.RootDirectory == expected.RootDirectory) : null;
            await using var fence = publication == null ? await artifacts.AcquireMaintenanceAsync(ct)
                : await artifacts.AcquirePublishedFilesMaintenanceAsync(publication.ArtifactId, publication.RootDirectory,
                    publication.Fingerprint, expected.Files.Select(f => f.Path).ToArray(), ct);
            var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == expected.TaskId, ct);
            if (task == null || task.PlanRevision != expected.TaskRevision) return new(expected.Id, false, "SelectionChanged");
            StageDirectoryCandidate? current;
            if (expected.ManifestArtifactId is {} artifactId && !expected.PublicationFilesOnly)
                current = await WholeTaskArtifactAsync(task, authorization, artifactId, expected.CleanupIntermediates, expected.CoveredRoots,
                    new ArtifactCleanupReferenceGuard(db, workspaces), ct, null, expected.DirectDeletion);
            else
            {
                var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson ?? "null", JsonSerializerOptions.Web);
                if (plan == null) return new(expected.Id, false, "SelectionChanged");
                var selections = await StageCleanupSelectionsAsync(task, plan, authorization.Consent.OwnerRunIds,
                    expected.CleanupIntermediates, expected.DeleteDeliveredFiles, ct);
                var selected = selections.Selected.SingleOrDefault(s => s.Id == expected.SelectionId);
                if (selected == null) return new(expected.Id, false, "SelectionChanged");
                current = await InspectStageDirectoryForDeletionAsync(task, plan, selected, selections,
                    authorization.Consent.OwnerRunIds, authorization, expected.CleanupIntermediates, expected.DeleteDeliveredFiles, ct, null,
                    expected.DirectDeletion);
            }
            if (current == null) return new(expected.Id, false, "SelectionChanged");
            if (!current.Group.Eligible) return new(expected.Id, false, current.Group.ReasonCode);
            if (current.Id != expected.Id || current.PublicationFilesOnly != expected.PublicationFilesOnly ||
                current.RootDirectory != expected.RootDirectory || current.ManifestArtifactId != expected.ManifestArtifactId ||
                current.BindingFingerprint != expected.BindingFingerprint ||
                expected.PublicationFilesOnly && current.Files.Select(f => f.Path).Except(expected.Files.Select(f => f.Path),
                    ArtifactManifestFiles.PathComparer).Any() ||
                expected.Group.ReasonCode == "AlreadyAbsent" && current.Group.ReasonCode != "AlreadyAbsent" ||
                !expected.DirectDeletion && (current.FileIdentities.Any(p => !expected.FileIdentities.TryGetValue(p.Key, out var prior) || p.Value != prior) ||
                current.Directories.Any(p => !expected.Directories.Contains(p, ArtifactManifestFiles.PathComparer))))
                return new(expected.Id, false, "SelectionChanged");
            if (current.Group.ReasonCode == "AlreadyAbsent") return new(expected.Id, true, "AlreadyAbsent");
            if (expected.ManifestArtifactId != null && (expected.PublicationFilesOnly || !authorization.Consent.SkipFileVerification))
            {
                var source = await artifacts.GetArtifactAsync(expected.ManifestArtifactId, ct);
                if (source == null) return new(expected.Id, false, "SelectionChanged");
                var snapshot = await ArtifactManifestFiles.CaptureAsync(current.RootDirectory, current.Files.Select(f => f.Path).ToArray(), ct);
                var registered = source.Manifest.Entries.ToHashSet();
                if (snapshot.Entries.Any(e => !registered.Contains(e))) return new(expected.Id, false, "SelectionChanged");
            }
            receipt = new(Guid.NewGuid().ToString("N"), task.Id, task.PlanRevision, expected.PartId, expected.Stage,
                current.RootDirectory, current.BindingFingerprint, DateTime.UtcNow, "Prepared", current.Files,
                authorization.Consent) {LiveTaskVersion = task.DeletedAt == null ? Version(task) : null};
            await AcquisitionStageDirectoryCleanupReceipt.SaveAsync(maintenance.StageCleanupMetadataDirectory, receipt, ct);
            ct.ThrowIfCancellationRequested();
            // Every current descendant was inspected for links and foreign claims above.
            // Root selection is frozen; there is no hash or old-manifest reconstruction.
            if (current.DirectDeletion && current.ManifestArtifactId == null)
            {
                // A previous unlink can leave only SMB delete-on-close names. Retrying those
                // unlinks can stall the remaining stages until the file server releases them.
                if (authorization.Consent.WholeTaskFiles &&
                    DeferredEmptyDirectoryTree.CaptureMarkerTree(current.RootDirectory, ct) is {} waiting)
                    return await RetainedAsync("SmbDeletePending", false, waiting);
                // Confirmation selects the owned directory's current contents. Recursive
                // deletion unlinks directory symlinks instead of traversing their targets.
                Directory.Delete(current.RootDirectory, recursive: true);
            }
            else if (!authorization.Consent.WholeTaskFiles && current.ManifestArtifactId == null)
                Directory.Delete(current.RootDirectory, recursive: true);
            else
            {
                // Delete only entries present in the confirmed inventory. A new child
                // arriving after the last inspection makes the nonrecursive rmdir fail.
                foreach (var file in current.Files)
                {
                    ct.ThrowIfCancellationRequested(); ArtifactManifestFiles.RejectLinks(file.Path);
                    if (!current.DirectDeletion)
                    {
                        var leaf = new FileInfo(file.Path); leaf.Refresh();
                        if (!leaf.Exists || JsonSerializer.Serialize(new {leaf.Length, leaf.CreationTimeUtc, leaf.LastWriteTimeUtc}) != current.FileIdentities[file.Path])
                            throw new IOException("The selected file changed.");
                    }
                    File.Delete(file.Path);
                    removed++;
                    removedBytes += file.Bytes;
                }
                if (current.ManifestArtifactId == null)
                    foreach (var directory in current.Directories.OrderByDescending(d => d.Length).Append(current.RootDirectory))
                    {
                        ct.ThrowIfCancellationRequested(); ArtifactManifestFiles.RejectLinks(directory);
                        Directory.Delete(directory, recursive: false);
                    }
            }
            var absent = current.ManifestArtifactId != null
                ? ArtifactCleanupAbsence.AllConfirmedAbsent(current.RootDirectory, current.Files.Select(f => f.Path).ToArray(), ct)
                : ArtifactCleanupAbsence.Probe(Path.GetDirectoryName(current.RootDirectory)!, current.RootDirectory, ct)
                    == ArtifactCleanupPathPresence.ConfirmedAbsent;
            if (!absent)
                return await RetainedAsync("Partial", true);
            await AcquisitionStageDirectoryCleanupReceipt.SaveAsync(maintenance.StageCleanupMetadataDirectory,
                receipt with {Status = "Completed", FinishedAt = DateTime.UtcNow, RemovedFiles = current.Files.Count,
                    RemovedBytes = current.Group.Bytes}, CancellationToken.None);
            return new(expected.Id, true, null, current.Files.Count, current.Group.Bytes);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            var result = await RetainedAsync(error is OperationCanceledException ? "Interrupted" : removed > 0 ? "Partial" : "Failed", error is IOException);
            if (error is OperationCanceledException) throw;
            logger?.LogWarning("Acquisition cleanup retained task {TaskId} stage {Stage}, {Reason} ({ExceptionType}, {HResult})",
                expected.TaskId, expected.Stage, result.Reason, error.GetType().Name, error.HResult);
            return result;
        }

        async Task<ArtifactWorkspaceCleanupResult> RetainedAsync(string status, bool checkMarkers,
            IReadOnlyList<DeferredDirectoryIdentity>? waiting = null)
        {
            if (receipt == null) return new(expected.Id, false, "CLEANUP_FILES_RETAINED", removed, removedBytes);
            var (reason, id) = await RecordRetainedStageAsync(expected, receipt, status, checkMarkers, removed, removedBytes, waiting);
            if (id != null) deferred?.Invoke(id);
            return new(expected.Id, false, reason, removed, removedBytes);
        }
    }

    internal async Task<(string Reason, string? DeferredReceipt)> RecordRetainedStageAsync(StageDirectoryCandidate expected,
        AcquisitionStageDirectoryCleanupReceipt receipt, string status, bool checkMarkers, int removed, long removedBytes,
        IReadOnlyList<DeferredDirectoryIdentity>? waiting = null)
    {
        const string retained = "CLEANUP_FILES_RETAINED";
        try
        {
            var checkSmb = checkMarkers && expected.DirectDeletion && expected.ManifestArtifactId == null && receipt.Consent.WholeTaskFiles;
            var directories = waiting ?? (checkSmb ? await DeferredEmptyDirectoryTree.CaptureAfterDeleteAsync(expected.RootDirectory) : null);
            var finished = receipt with {Status = directories == null ? status : "SmbDeletePending", WaitingDirectories = directories,
                SelectionId = expected.SelectionId, Scopes = expected.Group.Scopes, CleanupIntermediates = expected.CleanupIntermediates,
                FinishedAt = DateTime.UtcNow, RemovedFiles = removed, RemovedBytes = removedBytes};
            await AcquisitionStageDirectoryCleanupReceipt.SaveAsync(maintenance.StageCleanupMetadataDirectory, finished, CancellationToken.None);
            if (directories != null) return (DeferredStageCleanup.PendingReason, receipt.Id);
            if (checkSmb && OperatingSystem.IsWindows() && FileProcessingFiles.ContainsOnlySmbDeleteMarkers(expected.RootDirectory))
                return (DeferredStageCleanup.PendingReason, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        return (retained, null);
    }

    private static bool StageCleanupOverlap(string a, string b) => ArtifactManifestFiles.PathComparer.Equals(a, b) ||
        ArtifactCleanupPathReferences.StrictlyUnder(a, b) || ArtifactCleanupPathReferences.StrictlyUnder(b, a);
}
