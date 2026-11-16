using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Exceptions;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Clears explicitly selected, owned stage directories; older tasks retain exact-file cleanup.
/// Neither a directory name nor task deletion alone grants ownership.</summary>
public sealed partial class AcquisitionTaskDeletionCleanupService(BakabaseDbContext db, IArtifactOperationStore artifacts,
    ArtifactWorkspaceMaintenance maintenance, PublishedArtifactAncestorCleanup ancestors, IUserStoragePolicy storage,
    AcquisitionSupersededRunCleanup? supersededRuns = null, FileProcessingWorkspaceResolver? workspaces = null,
    ILogger<AcquisitionTaskDeletionCleanupService>? logger = null)
{
    internal sealed record Plan(AcquisitionTaskDeleteCleanup Summary, string TaskVersion,
        ArtifactDeliveryCleanupConsent? Consent, IReadOnlyList<ArtifactWorkspaceCleanupPreview> Candidates,
        AcquisitionSupersededRunCleanup.Plan? Superseded = null, IReadOnlyList<string>? AuthorizedDirectories = null,
        IReadOnlyList<AbsentCandidate>? AlreadyAbsent = null,
        IReadOnlyDictionary<string, IReadOnlyList<ArtifactCleanupRetirementDependency>>? RetirementDependencies = null, bool RetryRetainedFiles = false,
        IReadOnlyList<DeliveredCandidate>? DeliveredFiles = null,
        IReadOnlyList<AbandonedInputCandidate>? AbandonedInputs = null,
        IReadOnlyList<CapturedInputCandidate>? CapturedInputs = null,
        IReadOnlyList<StageDirectoryCandidate>? StageDirectories = null);
    internal sealed record AbsentCandidate(string ArtifactId, string OperationId, string RootDirectory,
        string Fingerprint, string PublishedArtifactId, IReadOnlyList<string> Files);

    internal async Task<Plan> PreviewAsync(AcquisitionTaskDbModel task, IReadOnlyList<int> owners,
        bool acceptDelivery, bool skipFileVerification, CancellationToken ct, Func<string, Task>? guardDirectory = null, bool retryRetainedFiles = false,
        bool cleanupIntermediates = true, bool deleteDeliveredFiles = false)
    {
        Plan Block(string reason) => new(new(0, 0, [], reason, SizeIncomplete: true), Version(task), null, []);
        if ((task.DeletedAt != null) != retryRetainedFiles)
            return Block(retryRetainedFiles ? "CLEANUP_TASK_NOT_DELETED" : "ACQUISITION_TASK_DELETED");
        var aliases = (workspaces?.ReadRelocations() ?? []).ToDictionary(m => m.SourceRoot, m => m.DestinationRoot, ArtifactManifestFiles.PathComparer);
        string SummaryPath(string path)
        {
            var full = Path.GetFullPath(path);
            for (string? parent = full; parent != null; parent = Path.GetDirectoryName(parent))
                if (aliases.TryGetValue(parent, out var target)) return Path.GetFullPath(Path.Combine(target, Path.GetRelativePath(parent, full)));
            return full;
        }
        // Older accepted cleanups retain their persisted receipts and replacement proofs.
        // A normal delete preview grants only the selected task's own files.
        var borrowed = AcquisitionRegisteredDownload.Read<AcquisitionPlanBuilderState>(task.BuilderStateJson)?.RegisteredDownload;
        var wholeTask = borrowed != null || !acceptDelivery || task.Status != AcquisitionStatus.Completed || task.WorkflowRunId == null;
        var runId = task.WorkflowRunId ?? 0;
        if (runId != 0 && !owners.Contains(runId) || await db.WorkflowRuns.CountAsync(r => owners.Contains(r.Id), ct) != owners.Count)
            return Block("ACQUISITION_EXECUTION_STATE_UNKNOWN");
        AcquisitionSupersededRunCleanup.Plan? superseded = null;
        if (await db.WorkflowRuns.AnyAsync(r => owners.Contains(r.Id) &&
                (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running || r.Status == WorkflowRunStatus.Waiting), ct))
        {
            if (retryRetainedFiles) return Block("CLEANUP_OLD_EXECUTION_REQUIRES_REVIEW");
            superseded = supersededRuns == null ? null : await supersededRuns.PreviewAsync(task, owners, ct, abandonUnfinishedFiles: wholeTask);
            if (superseded == null) return Block("CLEANUP_OLD_EXECUTION_REQUIRES_REVIEW");
        }
        if (wholeTask) return await PreviewWholeTaskFilesAsync(task, owners, superseded, cleanupIntermediates, deleteDeliveredFiles,
            skipFileVerification, retryRetainedFiles, ct, guardDirectory, borrowed?.Input.OperationId);
        if (!await db.WorkflowRuns.AnyAsync(r => r.Id == runId && r.Status == WorkflowRunStatus.Success && r.FailedItemCount == 0, ct))
            return Block("CLEANUP_TASK_NOT_COMPLETED");
        var ownerKeys = owners.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
        var bindings = await db.ArtifactOperationBindings.AsNoTracking()
            .Where(b => b.OwnerKind == "workflow" && ownerKeys.Contains(b.OwnerId)).ToListAsync(ct);
        var latestPublications = bindings.Where(b => b.OwnerId == runId.ToString(CultureInfo.InvariantCulture) &&
            b.Slot.EndsWith(":artifact.publish", StringComparison.Ordinal)).Select(b => b.OperationId).Distinct().ToArray();
        if (!await db.Artifacts.AsNoTracking().AnyAsync(a => latestPublications.Contains(a.OperationId) &&
                a.Kind == "publishedFiles" && a.State != ArtifactLocationState.Retired && a.State != ArtifactLocationState.Quarantined, ct))
            return Block("CLEANUP_DELIVERY_PROOF_MISSING");
        // Explicit final-file deletion includes every registered delivery version of the
        // fixed task/run scope. Older publications are not arbitrary directory contents.
        var publications = deleteDeliveredFiles ? bindings.Where(b => b.Slot.EndsWith(":artifact.publish", StringComparison.Ordinal))
            .Select(b => b.OperationId).Distinct().ToArray() : latestPublications;
        var outputIds = await db.Artifacts.AsNoTracking().Where(a => publications.Contains(a.OperationId) &&
            a.Kind == "publishedFiles" && a.State != ArtifactLocationState.Retired && a.State != ArtifactLocationState.Quarantined)
            .OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id).Select(a => a.Id).ToListAsync(ct);
        if (outputIds.Count == 0) return Block("CLEANUP_DELIVERY_PROOF_MISSING");
        var outputs = new List<ArtifactAcceptedPublication>();
        foreach (var id in outputIds)
        {
            var output = await artifacts.GetArtifactAsync(id, ct);
            if (output == null) return Block("CLEANUP_DELIVERY_PROOF_MISSING");
            storage.EnsurePathAllowed(output.RootDirectory);
            if (guardDirectory != null) await guardDirectory(output.RootDirectory);
            if (acceptDelivery && !skipFileVerification) ArtifactManifestFiles.ResolveFiles(output.RootDirectory, output.Manifest);
            // A preview observes the registered delivery without reading its complete bytes
            // or updating artifact state. Mutation still performs full content validation.
            if (cleanupIntermediates && !acceptDelivery && (output.State != ArtifactLocationState.Available ||
                                    !ArtifactCleanupPreviewFiles.MatchesManifest(output, ct)))
                return Block("CLEANUP_DELIVERY_ACCEPTANCE_REQUIRED");
            outputs.Add(new(output.Id, output.OperationId, output.Fingerprint, output.RootDirectory));
        }
        var consent = new ArtifactDeliveryCleanupConsent(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            task.Id, task.PlanRevision, runId, acceptDelivery, owners.Order().ToArray(), outputs, skipFileVerification, deleteDeliveredFiles);
        var authorization = Authorization(task, consent, superseded, retryRetainedFiles);
        var operationIds = bindings.Select(b => b.OperationId).Distinct().ToArray();
        var previewReferences = new ArtifactCleanupReferenceGuard(db, workspaces, cachePreviewReads: true);
        var stageDirectories = await PreviewStageDirectoriesForDeletionAsync(task, owners, authorization,
            cleanupIntermediates, deleteDeliveredFiles, ct, guardDirectory);
        if (stageDirectories.Count > 0)
        {
            // Current bindings, proven earlier stage roots and private allocations form
            // one deduplicated directory selection. Do not repeat the old manifest/hash
            // traversal for these explicitly selected roots.
            // A failed attempt can stage a private copy in the global processing workspace
            // before recording a binding. Only its existing exact-file proof may select it.
            var roots = stageDirectories.Select(c => c.RootDirectory).ToArray();
            var extraInputs = cleanupIntermediates
                ? await PreviewAbandonedInputsAsync(operationIds, authorization, previewReferences, ct, guardDirectory, roots) : [];
            var stageGroups = stageDirectories.Select(c => c.Group).Concat(extraInputs.Select(c => c.Group)).ToArray();
            var stageFiles = Unique(stageGroups.Where(g => g.Eligible).SelectMany(g => g.Files));
            var identifiedStageFiles = Unique(stageGroups.SelectMany(g => g.Files));
            return new(new(stageFiles.Count, stageFiles.Sum(f => f.Bytes), stageGroups,
                    SupersededRuns: superseded?.Runs.Select(r => new AcquisitionTaskDeleteSupersededRun(
                        r.RunId, r.Revision, r.ReplacedByRevision)).ToArray(),
                    IdentifiedFileCount: identifiedStageFiles.Count, IdentifiedBytes: identifiedStageFiles.Sum(f => f.Bytes),
                    SizeIncomplete: stageGroups.Any(g => g.SizeUnknown)), Version(task), consent, [], superseded,
                stageDirectories.Select(c => c.RootDirectory).Distinct(ArtifactManifestFiles.PathComparer).ToArray(),
                RetryRetainedFiles: retryRetainedFiles, StageDirectories: stageDirectories, AbandonedInputs: extraInputs);
        }
        var processed = await db.Artifacts.AsNoTracking().Where(a => operationIds.Contains(a.OperationId) &&
            a.Managed && a.Kind == "processedFiles").Select(a => a.Id).ToListAsync(ct);
        var authorized = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(cleanupIntermediates ? outputIds.Concat(processed) : outputIds);
        while (pending.TryDequeue(out var id))
        {
            ct.ThrowIfCancellationRequested();
            if (!visited.Add(id)) continue;
            if (visited.Count > 10000) throw new InvalidDataException("CLEANUP_LINEAGE_TOO_LARGE");
            var artifact = await artifacts.GetArtifactAsync(id, ct) ?? throw new InvalidDataException("CLEANUP_DELIVERY_PROOF_MISSING");
            authorized.Add(artifact.RootDirectory);
            if (artifact.Managed && artifact.Kind == "processedFiles" && Path.GetDirectoryName(artifact.RootDirectory) is { } checkpoint)
            {
                authorized.Add(checkpoint);
                if (Path.GetFileName(checkpoint) == "recursive" && Path.GetDirectoryName(checkpoint) is { } operationRoot)
                    authorized.Add(operationRoot);
            }
            if (cleanupIntermediates) foreach (var parent in artifact.ParentArtifactIds) pending.Enqueue(parent);
        }
        foreach (var path in authorized)
        { storage.EnsurePathAllowed(path); if (guardDirectory != null) await guardDirectory(path); }
        var candidates = new List<ArtifactWorkspaceCleanupPreview>();
        if (cleanupIntermediates)
        {
            foreach (var chunk in processed.Chunk(100))
                candidates.AddRange(await maintenance.PreviewForDeletionAsync(chunk, authorization, ct, previewReferences));
            candidates.AddRange(await ancestors.PreviewForDeletionAsync(outputIds, owners, authorization, ct, previewReferences));
        }
        var executions = await db.WorkflowNodeExecutions.AsNoTracking().Where(n => owners.Contains(n.WorkflowRunId))
            .Select(n => new {n.WorkflowRunId, n.NodeId, n.Kind, n.OutputsJson}).ToListAsync(ct);
        var outputOwners = executions.SelectMany(n => OutputArtifacts(n.OutputsJson).Select(id => (Id: id,
            Node: new AcquisitionTaskDeleteCleanupNode(n.WorkflowRunId, n.NodeId, n.Kind))))
            .GroupBy(pair => pair.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(p => p.Node).Distinct().ToArray());
        var groups = new List<AcquisitionTaskDeleteCleanupGroup>();
        var deliveries = new List<DeliveredCandidate>();
        var absent = new List<AbsentCandidate>();
        var unreadable = new HashSet<ArtifactWorkspaceCleanupPreview>();
        var unavailableWorkspaces = candidates.Where(c => c.PublishedArtifactId == null && c.PreviewError != null)
            .GroupBy(c => c.ArtifactId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().PreviewError!, StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (candidate.Reason is "NothingToReclaim" or "AlreadyRetired") continue;
            // A registered boundary may be retained by private-workspace cleanup but safely
            // handled by its separate registered-ancestor proof below.
            if (candidate.PublishedArtifactId == null && candidate.Reason == "IntermediateResultRetained" &&
                candidates.Any(c => c.ArtifactId == candidate.ArtifactId && c.PublishedArtifactId != null)) continue;
            var artifact = await artifacts.GetArtifactAsync(candidate.ArtifactId, ct);
            if (artifact == null) continue;
            storage.EnsurePathAllowed(candidate.ResultDirectory);
            IReadOnlyList<AcquisitionTaskDeleteCleanupFile> files;
            var candidateReason = candidate.Reason;
            var sizeUnknown = false;
            IReadOnlyList<AcquisitionTaskDeleteCleanupFile> Unavailable(Exception error)
            {
                sizeUnknown = true; unreadable.Add(candidate);
                if (candidate.PublishedArtifactId == null) unavailableWorkspaces[artifact.Id] = error;
                candidateReason = candidate.Reason ?? "CLEANUP_PREVIEW_FAILED";
                logger?.LogWarning(error, "Unable to preview acquisition cleanup workspace {ArtifactId} for task {TaskId}", artifact.Id, task.Id);
                return [];
            }
            // A successful registration does not make an unreadable private workspace
            // safe to discard. Keep both selections for this artifact blocked while
            // still allowing independent, explicitly selected final deliveries.
            if (candidate.PublishedArtifactId != null && unavailableWorkspaces.TryGetValue(artifact.Id, out var workspaceError))
            {
                files = Unavailable(workspaceError);
                candidateReason = "WorkspaceUnavailable";
            }
            else if (candidate.PublishedArtifactId == null)
            {
                // A moved/retired registered result is not proof that its private snapshots
                // disappeared too. Keep the task visible when that workspace cannot be proven.
                if (artifact.State == ArtifactLocationState.Retired && candidate.Eligible && candidate.PreviewFiles != null)
                    files = candidate.PreviewFiles.Select(f => new AcquisitionTaskDeleteCleanupFile(f.Path, f.Bytes)).ToArray();
                else if (artifact.State == ArtifactLocationState.Retired && candidate.Reason != "AlreadyAbsent")
                { files = []; candidateReason = "CLEANUP_RETIRED_WORKSPACE_REQUIRES_REVIEW"; }
                else
                {
                    try
                    {
                        files = candidate.PreviewError != null ? Unavailable(candidate.PreviewError) :
                            (await maintenance.DescribeFilesAsync(candidate, ct, overrideRetention: true))
                            .Select(f => new AcquisitionTaskDeleteCleanupFile(f.Path, f.Bytes)).ToArray();
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException)
                    {
                        // This workspace was not authorized. Do not repeat a failed enumeration
                        // or discard an independently verified delivery selection with it.
                        files = Unavailable(error);
                    }
                }
            }
            else if (candidate.PreviewFiles != null)
                files = candidate.PreviewFiles.Select(f => new AcquisitionTaskDeleteCleanupFile(
                    Path.GetFullPath(f.Path), f.Bytes)).ToArray();
            else files = artifact.Manifest.Entries.Select(e => new AcquisitionTaskDeleteCleanupFile(
                Path.GetFullPath(Path.Combine(candidate.ResultDirectory, e.RelativePath)), e.Length)).ToArray();
            foreach (var file in files) storage.EnsurePathAllowed(file.Path);
            if (guardDirectory != null) foreach (var file in files) await guardDirectory(file.Path);
            // This is only a registered artifact's full manifest, not a presentation subset
            // or private snapshots next to its result directory. Never relocate/retire its
            // registration: another owner may still need that historical record.
            var exactPaths = files.Select(f => f.Path).ToArray();
            var alreadyAbsent = !unreadable.Contains(candidate) && candidate.PublishedArtifactId != null && candidate.PreviewFiles == null &&
                artifact.State is not (ArtifactLocationState.Quarantined or ArtifactLocationState.Retired) &&
                ArtifactManifestFiles.PathComparer.Equals(artifact.RootDirectory, candidate.ResultDirectory) &&
                await authorization.CoversAsync(artifact, ct) &&
                ArtifactCleanupAbsence.AllConfirmedAbsent(artifact.RootDirectory, exactPaths, ct);
            if (alreadyAbsent)
            {
                absent.Add(new(artifact.Id, artifact.OperationId, artifact.RootDirectory, artifact.Fingerprint,
                    candidate.PublishedArtifactId!, exactPaths));
                candidateReason = "AlreadyAbsent";
                files = []; // No remaining bytes or unlink candidates; full paths stay in the server-owned plan.
            }
            else if (candidate.Eligible && candidate.PublishedArtifactId != null && candidate.PreviewFiles == null &&
                     !Directory.Exists(artifact.RootDirectory))
            {
                // A disappeared mount/root without independent observation remains
                // unavailable, rather than entering quarantine and failing on a stat.
                unreadable.Add(candidate); sizeUnknown = true; candidateReason = "WorkspaceUnavailable";
            }
            var nodes = outputOwners.GetValueOrDefault(artifact.Id) ?? [];
            if (nodes.Length == 0) nodes = bindings.Where(b => b.OperationId == artifact.OperationId)
                .Select(b => new AcquisitionTaskDeleteCleanupNode(int.Parse(b.OwnerId, CultureInfo.InvariantCulture), NodeId(b.Slot),
                    candidate.PublishedArtifactId == null ? "processingWorkspace" : artifact.Kind)).Distinct().ToArray();
            var mappedFiles = new List<AcquisitionTaskDeleteCleanupFile>(files.Count);
            foreach (var file in files)
            {
                var path = SummaryPath(file.Path);
                if (!ArtifactManifestFiles.PathComparer.Equals(path, file.Path))
                    await EnsureDirectoryAuthorizedAsync(path, guardDirectory);
                mappedFiles.Add(file with {Path = path});
            }
            files = Unique(mappedFiles);
            // A private selection can be next to the registered result (for example,
            // plan/result archives replaced by recursive/result files). Its manifest
            // is not the cleanup selection, nor is its registered root the displayed
            // directory. The immutable selection fingerprint still governs execution.
            var actualDirectory = candidate.PreviewFiles != null && files.Count > 0
                ? CommonFileDirectory(files) : SummaryPath(candidate.ResultDirectory);
            if (!ArtifactManifestFiles.PathComparer.Equals(actualDirectory, candidate.ResultDirectory))
                await EnsureDirectoryAuthorizedAsync(actualDirectory, guardDirectory);
            groups.Add(new(artifact.Id, candidate.PublishedArtifactId == null ? "intermediate" : artifact.Kind,
                actualDirectory, !sizeUnknown && (candidate.Eligible || alreadyAbsent), candidateReason, nodes, files,
                files.Count, files.Sum(f => f.Bytes), candidate.Reason is "SharedOwner" or "ReferencedArtifact" or "ReferencedPath" or "ReferencedDescendant",
                SizeUnknown: sizeUnknown || candidateReason == "CLEANUP_RETIRED_WORKSPACE_REQUIRES_REVIEW"));
        }
        if (deleteDeliveredFiles)
            foreach (var id in outputIds)
            {
                var artifact = await artifacts.GetArtifactAsync(id, ct);
                if (artifact == null) throw new InvalidDataException("CLEANUP_DELIVERY_PROOF_MISSING");
                var selection = await PreviewDeliveredFilesAsync(artifact, authorization, previewReferences, ct);
                foreach (var file in selection.Files) await EnsureDirectoryAuthorizedAsync(file.Path, guardDirectory);
                var nodes = outputOwners.GetValueOrDefault(id) ?? [new(runId, "", "fs.publishArtifacts")];
                groups.Add(new(id, "publishedFiles", artifact.RootDirectory, selection.Reason == null,
                    selection.Reason ?? (selection.Files.Count == 0 ? "AlreadyAbsent" : null), nodes,
                    selection.Files, selection.Files.Count, selection.Files.Sum(f => f.Bytes), Shared: selection.Reason is "SharedOwner" or "ReferencedArtifact" or "ReferencedPath"));
                if (selection.Reason == null) deliveries.Add(selection);
            }
        var abandonedInputs = cleanupIntermediates
            ? await PreviewAbandonedInputsAsync(operationIds, authorization, previewReferences, ct, guardDirectory) : [];
        groups.AddRange(abandonedInputs.Select(candidate => candidate.Group));
        var capturedInputs = cleanupIntermediates
            ? await PreviewCapturedInputsAsync(authorization, previewReferences, ct, guardDirectory) : [];
        groups.AddRange(capturedInputs.Select(candidate => candidate.Group));
        var unique = Unique(groups.Where(g => g.Eligible).SelectMany(g => g.Files));
        var identified = Unique(groups.SelectMany(g => g.Files));
        return new(new(unique.Count, unique.Sum(f => f.Bytes), groups, SupersededRuns: superseded?.Runs.Select(r =>
                new AcquisitionTaskDeleteSupersededRun(r.RunId, r.Revision, r.ReplacedByRevision)).ToArray(),
                IdentifiedFileCount: identified.Count, IdentifiedBytes: identified.Sum(f => f.Bytes), SizeIncomplete: groups.Any(g => g.SizeUnknown)), Version(task), consent,
            candidates.Where(c => c.Eligible && !unreadable.Contains(c) && !absent.Any(a => a.ArtifactId == c.ArtifactId &&
                a.PublishedArtifactId == c.PublishedArtifactId)).ToArray(), superseded, authorized.ToArray(), absent,
            authorization.RetirementDependencies, retryRetainedFiles, deliveries, abandonedInputs, capturedInputs);
    }

    internal async Task EnsureAuthorizedAsync(Plan plan, Func<string, Task>? guardDirectory, CancellationToken ct)
    {
        foreach (var path in (plan.AuthorizedDirectories ?? []).Concat(plan.Summary.Groups.SelectMany(g =>
                     g.Files.Select(f => f.Path).Prepend(g.Directory)))
                 .Concat((plan.AlreadyAbsent ?? []).SelectMany(a => a.Files.Prepend(a.RootDirectory)))
                 .Distinct(ArtifactManifestFiles.PathComparer))
        {
            ct.ThrowIfCancellationRequested();
            try { await EnsureDirectoryAuthorizedAsync(path, guardDirectory); }
            catch (UserStoragePathException error) when (error.MountUnavailable && plan.StageDirectories is {Count: > 0})
            {
                // The remote scope passed. Each stage checks the live storage boundary
                // again before mutation, so an offline volume cannot block another stage.
            }
        }
    }

    internal async Task EnsureDirectoryAuthorizedAsync(string path, Func<string, Task>? guardDirectory)
    {
        if (guardDirectory != null) await guardDirectory(path);
        storage.EnsurePathAllowed(path);
    }

    internal async Task<AcquisitionTaskDeleteCleanupOutcome> ApplyAsync(AcquisitionTaskDbModel task, Plan plan,
        CancellationToken ct, Action<int, long>? reportProgress = null)
    {
        if (plan.Consent == null || plan.TaskVersion != Version(task))
            return new(task.Id, false, 0, 0, [plan.Summary.ReasonCode ?? "ACQUISITION_TASK_CHANGED"]);
        var closed = Array.Empty<int>();
        if (plan.Superseded is {Runs.Count: > 0} old)
        {
            if (supersededRuns == null || !await supersededRuns.ApplyAsync(task, plan.Consent.OwnerRunIds, old, ct))
                return new(task.Id, false, 0, 0, ["CLEANUP_OLD_EXECUTION_REQUIRES_REVIEW"]);
            closed = old.Runs.Select(r => r.RunId).ToArray();
        }
        // Execution never inherits the read-only preview's pending cancellation exceptions.
        var authorization = Authorization(task, plan.Consent with {ConfirmedAt = DateTimeOffset.UtcNow}, retryRetainedFiles: plan.RetryRetainedFiles);
        authorization.RestoreRetirementDependencies(plan.RetirementDependencies);
        var removed = 0; long bytes = 0; var reasons = new HashSet<string>(StringComparer.Ordinal);
        var retained = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        void Keep(string artifactId, string reason)
        {
            reasons.Add(reason);
            foreach (var group in plan.Summary.Groups.Where(g => g.ArtifactId == artifactId)) retained.Add(group.Directory);
        }
        foreach (var group in plan.Summary.Groups.Where(g => !g.Eligible))
            Keep(group.ArtifactId, group.ReasonCode ?? "CLEANUP_FILES_RETAINED");
        foreach (var expected in plan.AlreadyAbsent ?? [])
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var fence = await artifacts.AcquireMaintenanceAsync(ct);
                var current = await artifacts.GetArtifactAsync(expected.ArtifactId, ct);
                if (current == null || current.OperationId != expected.OperationId || current.Fingerprint != expected.Fingerprint ||
                    current.Manifest.Fingerprint != expected.Fingerprint || current.RootDirectory != expected.RootDirectory ||
                    current.State is ArtifactLocationState.Quarantined or ArtifactLocationState.Retired ||
                    !ArtifactManifestFiles.ResolveFiles(current.RootDirectory, current.Manifest)
                        .SequenceEqual(expected.Files, ArtifactManifestFiles.PathComparer) ||
                    !await authorization.CoversAsync(current, ct))
                { Keep(expected.ArtifactId, "SelectionChanged"); continue; }
                var finalVerified = true;
                foreach (var publication in authorization.Consent.Publications)
                {
                    var output = await artifacts.GetArtifactAsync(publication.ArtifactId, ct);
                    if (output == null || !await authorization.MatchesAsync(output, ct) ||
                        !authorization.Consent.DeliveryAccepted && await artifacts.ResolveByIdAsync(output.Id, ct) == null)
                    { finalVerified = false; break; }
                }
                if (!finalVerified) { Keep(expected.ArtifactId, "CLEANUP_DELIVERY_ACCEPTANCE_REQUIRED"); continue; }
                if (!ArtifactCleanupAbsence.AllConfirmedAbsent(expected.RootDirectory, expected.Files, ct))
                { Keep(expected.ArtifactId, "SelectionChanged"); continue; }
                // A no-op has its own durable receipt and never falls back to deleting a file
                // which appeared after preview. Other owners/consumers/locations stay unchanged.
                await maintenance.RecordAlreadyAbsentForDeletionAsync(current, expected.Files, authorization.Consent, ct);
            }
            catch (UserStoragePathException) { reasons.Add("CLEANUP_PATH_NOT_AUTHORIZED"); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
            { Keep(expected.ArtifactId, "WorkspaceUnavailable"); }
        }
        foreach (var captured in plan.CapturedInputs ?? [])
        {
            if (!captured.Group.Eligible) continue;
            var outcome = await DeleteCapturedInputAsync(captured, authorization, ct);
            removed += outcome.RemovedFiles; bytes += outcome.RemovedBytes;
            reportProgress?.Invoke(removed, bytes);
            if (!outcome.Completed) Keep(captured.Group.ArtifactId, outcome.Reason ?? "CLEANUP_FILES_RETAINED");
        }
        foreach (var abandoned in plan.AbandonedInputs ?? [])
        {
            if (!abandoned.Group.Eligible) continue;
            var outcome = await DeleteAbandonedInputsAsync(abandoned, authorization, ct);
            removed += outcome.RemovedFiles; bytes += outcome.RemovedBytes;
            reportProgress?.Invoke(removed, bytes);
            if (!outcome.Completed) Keep(abandoned.Id, outcome.Reason ?? "CLEANUP_FILES_RETAINED");
        }
        foreach (var stageDirectory in plan.StageDirectories ?? [])
        {
            if (!plan.Summary.Groups.Any(g => g.ArtifactId == stageDirectory.Id && g.Eligible)) continue;
            var outcome = await DeleteStageDirectoryAsync(stageDirectory, authorization, ct);
            removed += outcome.RemovedFiles; bytes += outcome.RemovedBytes;
            reportProgress?.Invoke(removed, bytes);
            if (!outcome.Completed) Keep(stageDirectory.Id, outcome.Reason ?? "CLEANUP_FILES_RETAINED");
        }
        foreach (var candidate in plan.Candidates)
        {
            ct.ThrowIfCancellationRequested();
            var started = Stopwatch.GetTimestamp();
            var filesBefore = removed;
            var bytesBefore = bytes;
            try
            {
                storage.EnsurePathAllowed(candidate.ResultDirectory);
                var result = candidate.PublishedArtifactId == null
                    ? (await maintenance.ReclaimForDeletionAsync([candidate.ArtifactId], authorization,
                        new Dictionary<string, string> {[candidate.ArtifactId] = candidate.SelectionFingerprint!}, ct)).Single()
                    : await ancestors.ReclaimForDeletionAsync(candidate, plan.Consent.OwnerRunIds, authorization, ct);
                removed += result.RemovedFiles; bytes += result.RemovedBytes;
                reportProgress?.Invoke(removed, bytes);
                if (!result.Completed) Keep(candidate.ArtifactId, result.Reason ?? "CLEANUP_FILES_RETAINED");
            }
            catch (UserStoragePathException) { reasons.Add("CLEANUP_PATH_NOT_AUTHORIZED"); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
            { Keep(candidate.ArtifactId, "CLEANUP_FILES_RETAINED"); }
            finally
            {
                logger?.LogInformation("Acquisition cleanup candidate {ArtifactId} for task {TaskId} took {ElapsedMs} ms. Skip content checks: {SkipContentChecks}; removed files: {Files}; removed bytes: {Bytes}",
                    candidate.ArtifactId, task.Id, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    authorization.Consent.SkipFileVerification, removed - filesBefore, bytes - bytesBefore);
            }
        }
        foreach (var delivery in plan.DeliveredFiles ?? [])
        {
            var outcome = await DeleteDeliveredFilesAsync(delivery, authorization, ct);
            removed += outcome.RemovedFiles; bytes += outcome.RemovedBytes;
            reportProgress?.Invoke(removed, bytes);
            if (!outcome.Completed) Keep(delivery.ArtifactId, outcome.Reason ?? "CLEANUP_FILES_RETAINED");
        }
        return new(task.Id, reasons.Count == 0, removed, bytes, reasons.ToArray(), closed,
            reasons.Contains("CLEANUP_PATH_NOT_AUTHORIZED") ? [] : retained.ToArray());
    }

    private ArtifactDeliveryCleanupAuthorization Authorization(AcquisitionTaskDbModel task, ArtifactDeliveryCleanupConsent consent,
        AcquisitionSupersededRunCleanup.Plan? superseded = null, bool retryRetainedFiles = false)
    {
        var version = Version(task);
        var previewInactive = superseded?.Runs.Select(r => r.RunId).ToArray() ?? [];
        return new(consent, async ct =>
        {
            var current = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == task.Id, ct);
            return current != null && (current.DeletedAt != null) == retryRetainedFiles && Version(current) == version &&
                (current.WorkflowRunId == consent.CompletedRunId || consent.WholeTaskFiles && consent.CompletedRunId == 0 && current.WorkflowRunId == null) &&
                (consent.WholeTaskFiles || (consent.AbandonUnfinishedFiles ? current.Status is AcquisitionStatus.Waiting or AcquisitionStatus.Failed or AcquisitionStatus.Cancelled :
                    current.Status == AcquisitionStatus.Completed && await db.WorkflowRuns.AnyAsync(r => r.Id == consent.CompletedRunId &&
                        r.Status == WorkflowRunStatus.Success && r.FailedItemCount == 0, ct))) &&
                await db.WorkflowRuns.CountAsync(r => consent.OwnerRunIds.Contains(r.Id) &&
                    (r.Status != WorkflowRunStatus.Pending && r.Status != WorkflowRunStatus.Running && r.Status != WorkflowRunStatus.Waiting ||
                     r.Status == WorkflowRunStatus.Waiting && r.PendingSignalJson == null && previewInactive.Contains(r.Id)), ct) == consent.OwnerRunIds.Count;
        }, artifacts, previewInactive, previewInactive.Length == 0 ? null : (operation, ct) =>
        {
            var ignored = previewInactive.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
            var now = DateTime.UtcNow;
            return db.ArtifactConsumers.AsNoTracking().AnyAsync(c => c.OperationId == operation && c.ReleasedAt == null &&
                (c.OwnerKind == "workflow" ? db.WorkflowRuns.Any(r => r.Id.ToString() == c.OwnerId &&
                    (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running ||
                     r.Status == WorkflowRunStatus.Waiting && (!ignored.Contains(c.OwnerId!) || r.PendingSignalJson != null))) : c.LeaseUntil > now), ct);
        }, boundaryCoverage: ancestors.HasBoundaryCoverageAsync,
            releasableViews: ct => AcquisitionTaskCleanupViews.ReadAsync(db, artifacts, consent, ct, workspaces),
            ownsCompletedProcessing: async (source, ct) =>
            {
                var owners = consent.OwnerRunIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
                var bindings = await db.ArtifactOperationBindings.AsNoTracking()
                    .Where(b => b.OperationId == source.OperationId).ToListAsync(ct);
                if (bindings.Count == 0 || bindings.Any(b => b.OwnerKind != "workflow" || !owners.Contains(b.OwnerId)) ||
                    await artifacts.HasActiveConsumersAsync(source.OperationId, ct)) return false;
                var operation = await artifacts.GetOperationAsync(source.OperationId, ct);
                var current = await artifacts.GetArtifactAsync(source.Id, ct);
                return operation is {Kind: "files.process", State: ArtifactOperationState.Completed} &&
                    operation.OutputArtifactId == source.Id && current is {Managed: true, Kind: "processedFiles"} &&
                    current.OperationId == source.OperationId && current.Fingerprint == source.Fingerprint &&
                    current.Manifest.Fingerprint == source.Manifest.Fingerprint;
            });
    }

    private static string NodeId(string slot) => slot.LastIndexOf(":artifact.", StringComparison.Ordinal) is >= 0 and var at ? slot[..at] : "";
    internal static List<AcquisitionTaskDeleteCleanupFile> Unique(IEnumerable<AcquisitionTaskDeleteCleanupFile> files,
        Func<string, string>? normalize = null) =>
        files.GroupBy(f => normalize?.Invoke(f.Path) ?? Path.GetFullPath(f.Path), ArtifactManifestFiles.PathComparer)
            .Select(g => new AcquisitionTaskDeleteCleanupFile(g.Key, g.Max(f => f.Bytes))).ToList();
    private static string CommonFileDirectory(IReadOnlyList<AcquisitionTaskDeleteCleanupFile> files)
    {
        var directory = Path.GetDirectoryName(files[0].Path)!;
        foreach (var file in files.Skip(1))
        {
            while (true)
            {
                var relative = Path.GetRelativePath(directory, file.Path);
                if (!Path.IsPathRooted(relative) && relative != ".." &&
                    !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) break;
                directory = Path.GetDirectoryName(directory) ?? throw new InvalidDataException("CLEANUP_SELECTION_ROOTS_DIFFER");
            }
        }
        return directory;
    }
    private static IReadOnlyList<string> OutputArtifacts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return (JsonSerializer.Deserialize<WorkflowOutputBlock[]>(json, JsonSerializerOptions.Web) ?? [])
            .Where(b => !string.IsNullOrEmpty(b.ArtifactId)).Select(b => b.ArtifactId!).Distinct(StringComparer.Ordinal).ToArray(); }
        catch (JsonException) { return []; }
    }
    private static string Version(AcquisitionTaskDbModel task) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {task.Id, task.PlanRevision, task.WorkflowRunId,
            task.Status, task.UpdatedAt, task.PlanJson, task.PlanHistoryJson, task.DeletedAt}))));
}
