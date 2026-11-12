using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Explicit adoption of completed original downloads into a local-only revision.
/// Preview reads evidence; apply registers those bytes and reuses the normal replan/run protocol.</summary>
public sealed class AcquisitionLocalFilesRecoveryService(BakabaseDbContext db, AcquisitionPlanService plans,
    IUserStoragePolicy storage, IArtifactOperationStore artifacts, DownloadArtifactCoordinator registrar,
    IServiceProvider services, BTaskManager? tasks = null, FileProcessingWorkspaceResolver? workspaces = null)
{
    private sealed record Checked(AcquisitionRecoveryDiagnosis Diagnosis, AcquisitionLocalFilesStorage? Storage = null,
        DownloadWorkset? Workset = null, IReadOnlyList<CloudDownloadTarget>? Targets = null);

    public async Task<AcquisitionRecoveryDiagnosis> DiagnoseAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
        bool verifyFiles, CancellationToken ct, Func<string?, Task>? guardDirectory = null) =>
        (await CheckAsync(task, run, verifyFiles, guardDirectory, ct)).Diagnosis;

    private async Task<Checked> CheckAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
        bool verifyFiles, Func<string?, Task>? guardDirectory, CancellationToken ct)
    {
        Checked Blocked(string code) => new(new(new(task.Id, task.Title, run?.Id, false, null, code, "reviewTask")));
        if (task.DeletedAt != null) return Blocked("ACQUISITION_TASK_DELETED");
        if (task.Status == AcquisitionStatus.Cancelled) return Blocked("RECOVERY_TASK_CANCELLED");
        if (task.BuilderId == null || task.PlanJson == null) return Blocked("RECOVERY_LOCAL_FILES_UNAVAILABLE");
        if (run != null && tasks?.IsPending("workflow.run." + run.Id) == true) return Blocked("ACQUISITION_TASK_ACTIVE");
        try
        {
            var plan = AcquisitionLocalFilesRecoveryReceipt.Read<AcquisitionPlan>(task.PlanJson)!;
            var receipt = AcquisitionLocalFilesRecoveryReceipt.Read(task);
            var captured = plans.CaptureLocalFilesStorage(task);
            if (guardDirectory != null)
            {
                await guardDirectory(captured.Policy.ProcessingWorkspaceDirectory);
                await guardDirectory(captured.Delivery.RootDirectory);
            }
            if (receipt?.ResultingRevision is { } prepared)
            {
                if (task.PlanRevision != prepared || run != null || task.Status != AcquisitionStatus.Waiting ||
                    plan.Parts.Count != receipt.Parts.Count || plan.Parts.Any(p => p.ContinueFrom == null ||
                        !receipt.Parts.Any(e => e.PartId == p.Id && e.ArtifactId == p.ContinueFrom.ArtifactId &&
                            e.OperationId == p.ContinueFrom.OperationId && e.Fingerprint == p.ContinueFrom.Fingerprint)) ||
                    !LocalOnly(plan)) return Blocked("RECOVERY_LOCAL_FILES_UNAVAILABLE");
                if (AcquisitionLocalFilesRecoveryReceipt.Hash(captured) != AcquisitionLocalFilesRecoveryReceipt.Hash(receipt.Storage))
                    return Blocked("RECOVERY_STORAGE_SETTINGS_CHANGED");
                foreach (var part in plan.Parts)
                {
                    var artifact = await AcquisitionPlanArtifactInputGuard.RequireAsync(db, artifacts, task.Id, part.Id, part.ContinueFrom!, ct);
                    if (guardDirectory != null) await guardDirectory(artifact.RootDirectory);
                    storage.EnsurePathAllowed(artifact.RootDirectory);
                    if (verifyFiles && !(await ArtifactManifestFiles.ValidateAsync(artifact.RootDirectory, artifact.Manifest, ct)).Available)
                        return Blocked("RECOVERY_FILES_UNVERIFIED");
                }
                return Ready(receipt.Preview, captured);
            }
            if (run == null || task.Status != AcquisitionStatus.Waiting || !AcquisitionLocalFilesRecoveryReceipt.Awaiting(run) ||
                !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, run.Id, task.Id, ct))
                return Blocked(run?.PendingSignalJson != null ? "RECOVERY_PENDING_ANSWER" : "RECOVERY_LOCAL_FILES_UNAVAILABLE");
            var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson);
            var selected = workset?.Targets.Where(t => !t.Excluded).ToArray();
            if (selected is not {Length: > 0 and <= 100} || selected.Select(t => t.Id).Distinct().Count() != selected.Length ||
                !selected.Select(t => t.Id).ToHashSet().SetEquals(plan.Parts.Select(p => p.Id)) ||
                selected.Any(t => t.SelectedSource == null || t.DownloadTaskId == null || t.TransportTaskId != null ||
                    t.AcquisitionNonce != null || t.ProcessedArtifactId != null || t.PublishedArtifactId != null ||
                    t.ProcessingWaitReason != null || t.PublicationWaitReason != null || t.DeliveryReview != null ||
                    t.ProcessedFiles.Count > 0 || t.PublishedFiles.Count > 0) || plan.Parts.Any(p => p.ContinueFrom != null))
                return Blocked("RECOVERY_LOCAL_FILES_UNAVAILABLE");
            var layout = plans.LocalFilesLayout(task, captured);
            var diagnosis = new AcquisitionRecoveryDiagnostics(db, storage, artifacts, tasks, workspaces);
            var parts = new List<AcquisitionLocalFilesRecoveryPart>();
            var targets = new List<CloudDownloadTarget>();
            var proofs = new List<string>();
            foreach (var target in selected)
            {
                var root = await diagnosis.DownloadExecutionRoot(run.Id, target, workset!.OutputDirectory, ct,
                    requireBinding: true, allowRegisteredArtifact: true);
                var problem = await diagnosis.CheckDownload(target.DownloadTaskId, root, verifyFiles, ct);
                if (problem != null && problem != "RECOVERY_VERIFY_FILES") return Blocked(problem);
                var result = await db.DownloadResults.AsNoTracking().Where(r => r.DownloadTaskId == target.DownloadTaskId)
                    .OrderByDescending(r => r.Id).FirstAsync(ct);
                var files = JsonSerializer.Deserialize<List<string>>(result.FilesJson, WorkflowJson.Options)!;
                var proof = await AcquisitionLocalFilesRecoveryReceipt.DownloadEvidenceAsync(db,
                    DownloadArtifactCoordinator.DownloadIntent(target, workset.OutputDirectory, workspaces),
                    target.DownloadTaskId!.Value, result.Id, ct);
                if (proof == null) return Blocked("RECOVERY_FILES_UNVERIFIED");
                proofs.Add(proof);
                if (guardDirectory != null)
                {
                    await guardDirectory(result.DownloadDirectory);
                    await guardDirectory(layout.PartDirectories[target.Id]);
                }
                var part = plan.Parts.Single(p => p.Id == target.Id);
                var oldPolicy = plan.StoragePolicy == null ? null : ArtifactStoragePolicies.Resolve(plan.StoragePolicy, part.StoragePolicy);
                parts.Add(new(target.Id, target.DownloadTaskId!.Value, result.Id, result.Fingerprint, files.Count,
                    result.DownloadDirectory, oldPolicy?.ProcessingWorkspaceDirectory, captured.Policy.ProcessingWorkspaceDirectory,
                    plan.DeliveryLayout?.PartDirectories.GetValueOrDefault(target.Id) ?? workset.OutputDirectory,
                    layout.PartDirectories[target.Id]));
                targets.Add(target with {DownloadDirectory = result.DownloadDirectory, Files = files,
                    DownloadArtifactId = null, DownloadOperationId = null});
            }
            return Ready(new(parts), captured, workset, targets, proofs);

            Checked Ready(AcquisitionLocalFilesRecoveryPreview preview, AcquisitionLocalFilesStorage settings,
                DownloadWorkset? original = null, IReadOnlyList<CloudDownloadTarget>? completed = null,
                IReadOnlyList<string>? downloadEvidence = null) =>
                new(new(new(task.Id, task.Title, run?.Id, verifyFiles, "localFiles", verifyFiles ? "RECOVERY_LOCAL_FILES_READY" : "RECOVERY_VERIFY_FILES",
                    LocalFiles: preview), AcquisitionLocalFilesRecoveryReceipt.Hash(new {settings, preview, downloadEvidence})), settings, original, completed);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or JsonException)
        {
            return Blocked(error.Message is "RECOVERY_LOCAL_STORAGE_REQUIRED" or "PLAN_REPLAN_DELIVERY_CONFLICT" or
                "RECOVERY_DOWNLOAD_IDENTITY_CHANGED" ? error.Message : "RECOVERY_LOCAL_FILES_UNAVAILABLE");
        }
    }

    // Caller owns the acquisition gate. All original-run mutation is fenced for the
    // entire adoption/replan, then the existing run path starts the new local revision.
    public async Task<AcquisitionLocalFilesRecoveryOutcome> ApplyWhileLockedAsync(int taskId, string expectedVersion,
        string? expectedEvidence, Func<string?, Task>? guardDirectory, CancellationToken ct, string? expectedStorageJson = null)
    {
        var task = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
        var originalRunId = task.WorkflowRunId ?? AcquisitionLocalFilesRecoveryReceipt.Read(task)?.PreviousRunId
            ?? throw new InvalidOperationException("RECOVERY_LOCAL_FILES_UNAVAILABLE");
        var gate = WorkflowRunMutationGate.For(originalRunId); await gate.WaitAsync(ct);
        try
        {
            task = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
            var run = task.WorkflowRunId is { } id ? await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == id, ct) : null;
            if (AcquisitionRecoveryService.Version(task, run) != expectedVersion)
                throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
            var check = await CheckAsync(task, run, true, guardDirectory, ct);
            if (!check.Diagnosis.Row.CanContinue) throw new InvalidOperationException(check.Diagnosis.Row.ReasonCode);
            if (expectedStorageJson != null && JsonSerializer.Serialize(check.Storage, WorkflowJson.Options) != expectedStorageJson)
                throw new InvalidOperationException("RECOVERY_STORAGE_SETTINGS_CHANGED");
            if (expectedEvidence != null && check.Diagnosis.SignalJson != expectedEvidence) throw new InvalidOperationException("RECOVERY_STORAGE_SETTINGS_CHANGED");
            if (run != null)
            {
                task = await new AcquisitionDownloadContinuationService(db).SaveWhileLockedAsync(task, run.Id,
                    AcquisitionDownloadContinuationService.DownloadsOnly, ct);
                var provenance = new List<AcquisitionLocalFilesProvenance>();
                var previewParts = new List<AcquisitionLocalFilesRecoveryPart>();
                var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = run.WorkflowDefinitionId,
                    TriggerKind = CloudWorkflow.Trigger, Payload = check.Workset!, ActivityConfigJson = "{}",
                    Services = services, Logger = NullLogger.Instance};
                foreach (var target in check.Targets!)
                {
                    var part = check.Diagnosis.Row.LocalFiles!.Parts.Single(p => p.PartId == target.Id);
                    var intent = DownloadArtifactCoordinator.DownloadIntent(target, check.Workset!.OutputDirectory, workspaces);
                    var evidence = await AcquisitionLocalFilesRecoveryReceipt.DownloadEvidenceAsync(db, intent,
                        part.DownloadTaskId, part.DownloadResultId, ct) ?? throw new InvalidOperationException("RECOVERY_FILES_UNVERIFIED");
                    var registered = await registrar.RecordAsync(context, target, check.Workset.OutputDirectory, ct);
                    var artifact = await artifacts.GetArtifactAsync(registered.DownloadArtifactId!, ct);
                    if (artifact == null || artifact.Fingerprint != part.Fingerprint || artifact.RootDirectory != part.SourceDirectory ||
                        !artifact.Files.ToHashSet(ArtifactManifestFiles.PathComparer).SetEquals(target.Files) ||
                        evidence != await AcquisitionLocalFilesRecoveryReceipt.DownloadEvidenceAsync(db, intent,
                            part.DownloadTaskId, part.DownloadResultId, ct)) throw new InvalidOperationException("RECOVERY_FILES_UNVERIFIED");
                    provenance.Add(new(part.PartId, part.DownloadTaskId, part.DownloadResultId, intent, evidence,
                        part.Fingerprint, artifact.Id, artifact.OperationId));
                    previewParts.Add(part with {ArtifactId = artifact.Id});
                }
                var receipt = new AcquisitionLocalFilesRecoveryReceipt(run.Id, task.PlanRevision,
                    AcquisitionLocalFilesRecoveryReceipt.Hash(run), check.Storage!, provenance, new(previewParts));
                var builder = string.IsNullOrWhiteSpace(task.BuilderStateJson) ? new JsonObject() : JsonNode.Parse(task.BuilderStateJson)!.AsObject();
                builder["localFilesRecovery"] = JsonSerializer.SerializeToNode(receipt, WorkflowJson.Options);
                var json = builder.ToJsonString(WorkflowJson.Options);
                var changed = await db.AcquisitionTasks.Where(t => t.Id == taskId && t.WorkflowRunId == run.Id &&
                    t.DeletedAt == null && t.PlanRevision == task.PlanRevision && t.BuilderStateJson == task.BuilderStateJson)
                    .ExecuteUpdateAsync(u => u.SetProperty(t => t.BuilderStateJson, json), ct);
                if (changed != 1) throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
                var plan = AcquisitionLocalFilesRecoveryReceipt.Read<AcquisitionPlan>(task.PlanJson)!;
                var input = new AcquisitionPlanReplanInput(task.PlanRevision, run.Id, "recovery.localFiles.currentStorage",
                    provenance.Select(p => new AcquisitionPlanProcessingReplacement(p.PartId,
                        AcquisitionPlanCompiler.Processing(plan.Parts.Single(part => part.Id == p.PartId)), [p.ArtifactId], p.ArtifactId)).ToArray(),
                    UseCurrentStorageDefaults: true);
                await plans.ReplanLocalFilesWhileLockedAsync(taskId, input, check.Storage!, guardDirectory, ct);
            }
        }
        finally {gate.Release();}
        task = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
        // Unknown instructions are preserved verbatim. Their normal processing node asks
        // for review before creating an input snapshot or running any file operation.
        var detail = await plans.RunWhileLockedAsync(taskId, task.PlanRevision, null, ct);
        return new(taskId, originalRunId, detail.Task.WorkflowRunId, detail.Task.PlanRevision,
            detail.Task.WorkflowRunId == null ? "prepared" : "started", detail.Task.WorkflowRunId == null
                ? detail.Issues.FirstOrDefault(i => i.Blocking)?.Code ?? "RECOVERY_PREPARED_PLAN_BLOCKED" : null);
    }

    private static bool LocalOnly(AcquisitionPlan plan) => plan.Parts.All(p => p.ContinueFrom != null) &&
        AcquisitionPlanCompiler.Compile(plan).Nodes.All(n => n.Kind is AcquisitionPlanContinueFilesActivity.ActivityKind or
            AcquisitionPlanProcessingActivity.ActivityKind or AssessFileArtifactDeliveryActivity.ActivityKind or "fs.publishArtifacts");
}
