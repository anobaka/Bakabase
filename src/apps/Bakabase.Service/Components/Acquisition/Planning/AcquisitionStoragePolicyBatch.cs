using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    private static int[] BatchIds(IReadOnlyList<int> ids)
    {
        if (ids == null || ids.Count is < 1 or > 10000 || ids.Any(i => i <= 0) || ids.Distinct().Count() != ids.Count)
            throw new ArgumentException("STORAGE_TASK_SELECTION_INVALID");
        return ids.ToArray();
    }
    private static string StorageVersion(AcquisitionTaskDbModel row) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Serialize(new {row.Id, row.DeletedAt, row.Status, row.Phase, row.WorkflowRunId, row.PlanRevision, row.PlanJson,
            row.BuilderStateJson, row.PlanHistoryJson, row.TargetDirectory, UpdatedAtTicks = row.UpdatedAt.Ticks}))));
    private string DefaultsVersion(bool applySettings, bool tracksDownloadDefault) => Serialize(new
    {
        Policy = applySettings ? CaptureStorage() : null,
        Delivery = applySettings ? DeliveryDirectories.Capture() : null,
        DownloadRoot = tracksDownloadDefault && outputDirectories?.ResolveNew(null) is {Length: > 0} directory
            ? Path.GetFullPath(directory) : null
    });
    private async Task<string?> StorageBlock(AcquisitionTaskDbModel? row, CancellationToken ct)
    {
        if (row == null || row.DeletedAt != null || row.BuilderId == null) return "task_missing";
        if (row.Status != AcquisitionStatus.Waiting || !await HasNeverExecutedAsync(row, ct)) return "already_started";
        if (ReadHistory(row).Count >= 100) return "revision_limit";
        return null;
    }

    public async Task<AcquisitionStoragePolicyPreview> PreviewStoragePolicyAsync(AcquisitionStoragePolicyPreviewInput input,
        AcquisitionStoragePolicyPreviewStore previews, Func<string, Task> guard, CancellationToken ct)
    {
        var ids = BatchIds(input.TaskIds);
        // Newtonsoft's parameterized-record binding does not preserve optional bool constructor
        // defaults for omitted JSON properties. Only an explicit false opts out of defaults.
        var applyCurrentSettings = input.ApplyCurrentSettings != false;
        if (!applyCurrentSettings && input.OutputDirectory == null) throw new ArgumentException("STORAGE_POLICY_CHANGE_REQUIRED");
        var policy = applyCurrentSettings ? CaptureStorage() : null;
        var delivery = applyCurrentSettings ? DeliveryDirectories.Capture() : null;
        var useDownloadDefault = input.OutputDirectory == "";
        var output = useDownloadDefault ? outputDirectories?.ResolveNew(null) : input.OutputDirectory;
        if (input.OutputDirectory != null)
        {
            ValidateDirectory(output, allowEmpty: false);
            AcquisitionOutputDirectoryResolver.ValidateConfiguredRoot(output, storage);
            output = Path.GetFullPath(output!);
            await guard(output);
        }
        // The version describes exactly what this preview captured, including when a settings
        // write interleaves with asynchronous authorization. Never stamp old values with a new version.
        var defaultsVersion = Serialize(new {Policy = policy, Delivery = delivery, DownloadRoot = useDownloadDefault ? output : null});
        if (policy?.ProcessingWorkspaceDirectory.Length > 0) await guard(policy.ProcessingWorkspaceDirectory);
        if (delivery?.RootDirectory.Length > 0) await guard(delivery.RootDirectory);
        var rows = await Tasks.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        var result = new List<AcquisitionStoragePolicyRow>(); var versions = new Dictionary<int, string>();
        foreach (var id in ids)
        {
            var row = rows.SingleOrDefault(t => t.Id == id); var reason = await StorageBlock(row, ct);
            string? download = null, processing = null, destination = null;
            if (reason == null)
            {
                if (!string.IsNullOrWhiteSpace(row!.TargetDirectory)) await guard(row.TargetDirectory);
                try
                {
                    var state = State(row);
                    var selectedOutput = output ?? row.TargetDirectory ?? "";
                    var selectedPolicy = policy ?? state.StoragePolicy;
                    var selectedDelivery = delivery ?? state.DeliverySettings;
                    if (row.PlanJson != null) ValidateStoragePaths(Plan(row) with {StoragePolicy = selectedPolicy});
                    download = Path.Combine(selectedOutput, selectedPolicy == null ? "cloud-" + new string('0', 16) :
                        ArtifactStoragePolicies.RenderDownload(selectedPolicy, row.Title, new string('0', 64), "provider"));
                    processing = Path.Combine(!string.IsNullOrEmpty(selectedPolicy?.ProcessingWorkspaceDirectory) ? selectedPolicy.ProcessingWorkspaceDirectory : selectedOutput,
                        ".bakabase-work", selectedPolicy == null ? Path.Combine("operations", new string('0', 64), "1") :
                        ArtifactStoragePolicies.RenderProcessing(selectedPolicy, row.Title, new string('0', 64), "plan", 1));
                    if (row.PlanJson != null && selectedDelivery != null)
                        destination = DeliveryDirectories.Resolve(id, row.Title!, selectedOutput, Plan(row).Parts, selectedDelivery,
                            applyCurrentSettings || output != null && selectedDelivery.RootDirectory.Length == 0 ? null : state.DeliveryLayout).TaskDirectory;
                    versions[id] = StorageVersion(row);
                }
                catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException) {reason = "policy_invalid";}
            }
            result.Add(new(id, row?.Title, reason == null, reason, row?.PlanRevision ?? 0, download, processing, destination));
        }
        var preview = new AcquisitionStoragePolicyPreview(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(10), result, versions.Count, policy);
        previews.Add(preview.PreviewId, new("policy", preview.ExpiresAt, versions)
            {Policy = policy, Delivery = delivery, OutputDirectory = output, TracksDownloadDefault = useDownloadDefault,
                ApplyCurrentSettings = applyCurrentSettings, DefaultsVersion = defaultsVersion});
        return preview;
    }

    public async Task<AcquisitionStoragePolicyApplyResult> ApplyStoragePolicyAsync(AcquisitionStoragePolicyApplyInput input,
        AcquisitionStoragePolicyPreviewStore previews, Func<string, Task> guard, CancellationToken ct)
    {
        var ids = BatchIds(input.TaskIds); var entry = previews.Claim(input.PreviewId, "policy", ids);
        if (entry.Result is AcquisitionStoragePolicyApplyResult saved) return saved;
        var applied = new List<int>(); var skipped = new List<AcquisitionStoragePolicySkip>();
        foreach (var id in ids)
        {
            var gate = AcquisitionTaskGate.For(id);
            if (!await gate.WaitAsync(0, ct)) {skipped.Add(new(id, "task_busy")); continue;}
            try
            {
                var row = await Tasks.SingleOrDefaultAsync(t => t.Id == id, ct);
                if (row != null) await db.Entry(row).ReloadAsync(ct);
                var reason = await StorageBlock(row, ct);
                if (reason == null && StorageVersion(row!) != entry.Versions[id]) reason = "revision_changed";
                if (reason == null && DefaultsVersion(entry.ApplyCurrentSettings, entry.TracksDownloadDefault) != entry.DefaultsVersion) reason = "policy_changed";
                if (reason != null) {skipped.Add(new(id, reason)); continue;}
                await GuardStoragePathsAsync(row!, guard);
                if (entry.Policy?.ProcessingWorkspaceDirectory.Length > 0) await guard(entry.Policy.ProcessingWorkspaceDirectory);
                if (entry.Delivery?.RootDirectory.Length > 0) await guard(entry.Delivery.RootDirectory);
                if (entry.OutputDirectory != null)
                {
                    ValidateDirectory(entry.OutputDirectory, allowEmpty: false);
                    AcquisitionOutputDirectoryResolver.ValidateConfiguredRoot(entry.OutputDirectory, storage);
                    await guard(entry.OutputDirectory);
                }
                var revision = row!.PlanRevision; var phase = row.Phase;
                if (entry.OutputDirectory != null)
                {
                    row.TargetDirectory = entry.OutputDirectory;
                    // Inherited delivery moves with an unstarted task's download root. Explicit
                    // final roots and historical null policies remain independent and unchanged.
                    if (State(row).DeliverySettings is {RootDirectory.Length: 0})
                        row.BuilderStateJson = Serialize(State(row) with {DeliveryLayout = null});
                }
                await UpdateStoragePolicyAsync(row, entry.Policy, null, ct);
                await UpdateDeliverySettingsAsync(row, entry.Delivery, ct);
                if (row.PlanJson != null) SavePlan(row, Plan(row) with {OutputDirectory = row.TargetDirectory ?? ""}, Sources(row), "storagePolicy.apply",
                    refreshStageDirectories: true);
                else {row.PlanRevision++; row.UpdatedAt = DateTime.Now;}
                row.Phase = phase;
                var changed = await Tasks.Where(t => t.Id == id && t.DeletedAt == null && t.WorkflowRunId == null &&
                    t.Status == AcquisitionStatus.Waiting && t.PlanRevision == revision).ExecuteUpdateAsync(update => update
                    .SetProperty(t => t.PlanJson, row.PlanJson).SetProperty(t => t.PlanHistoryJson, row.PlanHistoryJson)
                    .SetProperty(t => t.BuilderStateJson, row.BuilderStateJson).SetProperty(t => t.PlanRevision, row.PlanRevision)
                    .SetProperty(t => t.TargetDirectory, row.TargetDirectory)
                    .SetProperty(t => t.UpdatedAt, row.UpdatedAt), ct);
                db.Entry(row).State = EntityState.Detached;
                if (changed == 1) applied.Add(id); else skipped.Add(new(id, "revision_changed"));
            }
            catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException)
            {skipped.Add(new(id, "policy_invalid")); db.ChangeTracker.Clear();}
            finally {gate.Release();}
        }
        var result = new AcquisitionStoragePolicyApplyResult(applied, skipped); lock (entry) entry.Result = result; return result;
    }

    private async Task GuardStoragePathsAsync(AcquisitionTaskDbModel row, Func<string, Task> guard)
    {
        foreach (var path in new[] {row.TargetDirectory, State(row).DeliverySettings?.RootDirectory, State(row).StoragePolicy?.ProcessingWorkspaceDirectory})
            if (!string.IsNullOrWhiteSpace(path)) await guard(path);
        if (row.PlanJson == null) return;
        foreach (var part in Plan(row).Parts)
        {
            if (!string.IsNullOrWhiteSpace(part.StoragePolicy?.ProcessingWorkspaceDirectory)) await guard(part.StoragePolicy.ProcessingWorkspaceDirectory);
            foreach (var step in part.Sources.Select(s => s.Processing).Append(part.Processing).Where(p => p != null).SelectMany(p => p!.Steps))
                if (!string.IsNullOrWhiteSpace(step.StoragePolicy?.ProcessingWorkspaceDirectory)) await guard(step.StoragePolicy.ProcessingWorkspaceDirectory);
        }
    }

    private async Task<(string? Reason, List<string> Ids, List<string> Publications, List<int> Owners)> CleanupArtifacts(AcquisitionTaskDbModel? row, CancellationToken ct)
    {
        if (row == null || row.DeletedAt != null) return ("task_missing", [], [], []);
        if (row.Status != AcquisitionStatus.Completed || row.WorkflowRunId is not {} runId) return ("not_completed", [], [], []);
        if (!await db.Set<WorkflowRunDbModel>().AnyAsync(r => r.Id == runId && r.Status == WorkflowRunStatus.Success, ct)) return ("not_completed", [], [], []);
        var owner = runId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var task = row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var runs = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.Module == "acquisition" &&
            s.EntityKind == "task" && s.EntityId == task).Select(s => s.WorkflowRunId).Distinct().ToListAsync(ct);
        // The current task/run foreign key is also authoritative for older runs created
        // before source indexing. Older revisions require the explicit source relation.
        if (!runs.Contains(runId)) runs.Add(runId);
        var owners = runs.Select(r => r.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var operations = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking().Where(b => b.OwnerKind == "workflow" && owners.Contains(b.OwnerId))
            .Select(b => b.OperationId).Distinct().ToListAsync(ct);
        var ids = await db.Set<ArtifactDbModel>().AsNoTracking().Where(a => operations.Contains(a.OperationId) && a.Managed && a.Kind == "processedFiles")
            .Select(a => a.Id).Distinct().ToListAsync(ct);
        var publishedOperations = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking().Where(b =>
            b.OwnerKind == "workflow" && b.OwnerId == owner && b.Slot.EndsWith(":artifact.publish"))
            .Select(b => b.OperationId).Distinct().ToListAsync(ct);
        var publications = await db.Set<ArtifactDbModel>().AsNoTracking().Where(a => publishedOperations.Contains(a.OperationId) &&
            a.Kind == "publishedFiles" && a.State == ArtifactLocationState.Available).Select(a => a.Id).ToListAsync(ct);
        return ids.Count == 0 && publications.Count == 0 ? ("no_artifacts", [], [], []) : (null, ids, publications, runs);
    }
    public async Task<AcquisitionWorkspaceCleanupPreview> PreviewWorkspaceCleanupAsync(AcquisitionStoragePolicyPreviewInput input,
        AcquisitionStoragePolicyPreviewStore previews, ArtifactWorkspaceMaintenance maintenance, Func<string, Task> guard, CancellationToken ct,
        PublishedArtifactAncestorCleanup? ancestors = null)
    {
        if (input.OverrideRetention) return await PreviewExplicitWorkspaceCleanupAsync(input, previews, maintenance, guard, ct, ancestors);
        var ids = BatchIds(input.TaskIds); var rows = await Tasks.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        var result = new List<AcquisitionWorkspaceCleanupRow>(); var versions = new Dictionary<int, string>();
        var recorded = new Dictionary<int, IReadOnlyList<ArtifactWorkspaceCleanupPreview>>();
        var processing = new Dictionary<int, IReadOnlyList<AcquisitionProcessingWorkspaceCandidate>>();
        var ownerRuns = new Dictionary<int, IReadOnlyList<int>>();
        var included = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var row = rows.SingleOrDefault(t => t.Id == id); var (reason, artifactIds, publications, owners) = await CleanupArtifacts(row, ct);
            var candidates = new List<ArtifactWorkspaceCleanupPreview>();
            var stageFiles = new List<AcquisitionProcessingWorkspaceCandidate>();
            if (reason == "not_completed" && await ReadCheckpointReviewAsync(row, ct) != null)
                reason = "processed_checkpoint_requires_confirmation";
            if (reason == null && publications.Count == 0 && artifactIds.All(included.Contains)) reason = "shared_artifact";
            artifactIds = artifactIds.Where(a => !included.Contains(a)).ToList();
            if (reason == null)
            {
                await GuardStoragePathsAsync(row!, guard);
                foreach (var artifactId in artifactIds)
                {
                    if (await artifacts!.GetArtifactDirectoryAsync(artifactId, ct) is {} directory) await guard(directory);
                }
                foreach (var chunk in artifactIds.Chunk(100)) candidates.AddRange(await maintenance.PreviewAsync(chunk, ct));
                if (ancestors != null && publications.Count > 0)
                    candidates.AddRange(await ancestors.PreviewAsync(publications, owners, ct));
                foreach (var candidate in candidates.Where(c => c.Eligible)) await guard(candidate.ResultDirectory);
                foreach (var publicationId in publications)
                    if (await artifacts!.GetArtifactDirectoryAsync(publicationId, ct) is {} directory) await guard(directory);
                var retained = candidates.Any(c => c.Reason == "RetentionPolicyKeep");
                candidates = candidates.Where(c => c.Eligible).ToList();
                await RequireRetainedInputSelectionAsync(row!, candidates, ct);
                stageFiles = await ProcessingWorkspacesAsync(row!, owners, guard, ancestors, ct);
                if (stageFiles.Count > 0)
                {
                    // The same producer can also report its old private snapshot after committing it to this stage.
                    // Its separate maintenance selection must never count or remove any of these physical files twice.
                    stageFiles = stageFiles.Select(stage => stage with {RetireMovedInputs = candidates.Where(c =>
                        c.PublishedArtifactId != null && c.PreviewFiles == null && stage.MovedInputIds.Contains(c.ArtifactId))
                        .Select(c => c.ArtifactId).Distinct().ToArray()}).ToList();
                    candidates.RemoveAll(c => stageFiles.Any(stage => stage.RetireMovedInputs.Contains(c.ArtifactId) ||
                        StageDirectoryOverlap(stage.Selection.Root, c.ResultDirectory) ||
                        stage.StageArtifactIds.Contains(c.ArtifactId) || c.PreviewFiles?.Any(f => StageDirectoryOverlap(stage.Selection.Root, f.Path)) == true));
                    processing[id] = stageFiles;
                }
                if (candidates.Count == 0 && stageFiles.Count == 0) reason = retained ? "policy_keep" : "nothing_to_reclaim";
                else {versions[id] = StorageVersion(row!); recorded[id] = candidates; ownerRuns[id] = owners; included.UnionWith(candidates.Select(c => c.ArtifactId));}
            }
            result.Add(new(id, row?.Title, reason == null, reason, candidates.Select(c => c.ArtifactId).Concat(stageFiles.SelectMany(s => s.StageArtifactIds)).Distinct().ToArray(),
                candidates.Sum(c => c.FileCount) + stageFiles.Sum(s => s.Selection.Remaining.Count),
                candidates.Sum(c => c.Bytes) + stageFiles.Sum(s => s.Selection.Remaining.Sum(f => f.Entry.Length))));
        }
        var preview = new AcquisitionWorkspaceCleanupPreview(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(10), result, versions.Count);
        previews.Add(preview.PreviewId, new("cleanup", preview.ExpiresAt, versions) {Artifacts = recorded, CleanupOwnerRuns = ownerRuns, ProcessingWorkspaces = processing}); return preview;
    }
    public async Task<AcquisitionWorkspaceCleanupResult> ReclaimWorkspacesAsync(AcquisitionStoragePolicyApplyInput input,
        AcquisitionStoragePolicyPreviewStore previews, ArtifactWorkspaceMaintenance maintenance, Func<string, Task> guard, CancellationToken ct,
        PublishedArtifactAncestorCleanup? ancestors = null)
    {
        var ids = BatchIds(input.TaskIds); var entry = previews.Claim(input.PreviewId, "cleanup", ids);
        if (entry.Result is AcquisitionWorkspaceCleanupResult saved) return saved;
        if (entry.CleanupConsents.Count > 0 || entry.ReviewedCheckpoints.Count > 0) return await ReclaimExplicitWorkspacesAsync(ids, entry, maintenance, guard, ct, ancestors);
        var completed = new List<int>(); var skipped = new List<AcquisitionStoragePolicySkip>(); var files = 0; long bytes = 0;
        foreach (var id in ids)
        {
            // Apply may finish an obsolete owner before taking its own mutation gates.
            // The fixed selection is still fully rechecked below; preview remains read-only.
            if (ancestors != null)
            {
                string? blocked = null;
                foreach (var publicationId in entry.Artifacts[id].Select(a => a.PublishedArtifactId).Where(p => p != null).Distinct())
                {
                    var prepared = await ancestors.PreparePublicationOwnersAsync(publicationId!, ct);
                    if (!prepared.Ready) { blocked = prepared.Reason ?? "ActiveOwner"; break; }
                }
                if (blocked != null) { skipped.Add(new(id, blocked)); continue; }
            }
            using var cleanupLease = AcquisitionTaskGate.TryEnterIntermediateCleanup(id, ct);
            var runGates = new List<SemaphoreSlim>();
            if (cleanupLease == null) {skipped.Add(new(id, "task_busy")); continue;}
            try
            {
                var row = await Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
                var stageFiles = entry.ProcessingWorkspaces.GetValueOrDefault(id);
                int[] heldOwners = stageFiles != null ? entry.CleanupOwnerRuns[id].Distinct().Order().ToArray()
                    : row?.WorkflowRunId is {} runId ? [runId] : [];
                foreach (var owner in heldOwners)
                {
                    var runGate = WorkflowRunMutationGate.For(owner);
                    if (!await runGate.WaitAsync(0, ct)) break;
                    runGates.Add(runGate);
                }
                if (runGates.Count != heldOwners.Length) {skipped.Add(new(id, "task_busy")); continue;}
                var (reason, artifactIds, publications, owners) = await CleanupArtifacts(row, ct);
                if (reason == null && StorageVersion(row!) != entry.Versions[id]) reason = "revision_changed";
                if (reason == null && (!entry.CleanupOwnerRuns[id].Order().SequenceEqual(owners.Order()) ||
                    entry.Artifacts[id].Any(a => a.PublishedArtifactId == null ? !artifactIds.Contains(a.ArtifactId) :
                        ancestors == null || !publications.Contains(a.PublishedArtifactId)))) reason = "files_changed";
                if (reason != null) {skipped.Add(new(id, reason)); continue;}
                await GuardStoragePathsAsync(row!, guard);
                if (stageFiles != null)
                {
                    var outcome = await RemoveProcessingWorkspacesAsync(row!, owners, stageFiles, guard, ancestors, ct);
                    files += outcome.RemovedFiles;
                    bytes += outcome.RemovedBytes;
                    if (!outcome.Completed) {skipped.Add(new(id, outcome.Reason ?? "files_changed")); continue;}
                }
                var succeeded = true;
                foreach (var chunk in entry.Artifacts[id].Where(a => a.PublishedArtifactId == null).Chunk(100))
                {
                    foreach (var a in chunk) await guard(a.ResultDirectory);
                    var current = await maintenance.PreviewAsync(chunk.Select(a => a.ArtifactId).ToArray(), ct);
                    // PreviewFiles is request-local presentation data. Compare every bound
                    // selection field, not the newly allocated summary array's object identity.
                    if (current.Count != chunk.Length || chunk.Any(a => !current.Any(c =>
                            (c with {PreviewFiles = null}) == (a with {PreviewFiles = null}))))
                    {skipped.Add(new(id, "files_changed")); succeeded = false; break;}
                    var reclaimed = await maintenance.ReclaimAsync(chunk.Select(a => a.ArtifactId).ToArray(), ct, chunk.ToDictionary(a => a.ArtifactId, a => a.SelectionFingerprint!));
                    files += reclaimed.Sum(r => r.RemovedFiles); bytes += reclaimed.Sum(r => r.RemovedBytes);
                    if (reclaimed.Any(r => !r.Completed)) {skipped.Add(new(id, "artifact_busy")); succeeded = false; break;}
                }
                if (ancestors != null && succeeded)
                    foreach (var candidate in entry.Artifacts[id].Where(a => a.PublishedArtifactId != null))
                    {
                        await guard(candidate.ResultDirectory);
                        if (await artifacts!.GetArtifactDirectoryAsync(candidate.PublishedArtifactId!, ct) is {} directory) await guard(directory);
                        var outcome = await ancestors.ReclaimWithHeldRunGatesAsync(candidate, owners, heldOwners, ct);
                        files += outcome.RemovedFiles; bytes += outcome.RemovedBytes;
                        if (!outcome.Completed) { skipped.Add(new(id, outcome.Reason ?? "artifact_busy")); succeeded = false; }
                    }
                if (succeeded) completed.Add(id);
            }
            finally {foreach (var runGate in runGates) runGate.Release();}
        }
        var result = new AcquisitionWorkspaceCleanupResult(completed, files, bytes, skipped); lock (entry) entry.Result = result; return result;
    }
}
