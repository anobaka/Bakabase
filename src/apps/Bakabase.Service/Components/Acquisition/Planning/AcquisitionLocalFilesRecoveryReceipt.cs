using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record AcquisitionLocalFilesStorage(ArtifactStoragePolicy Policy, AcquisitionDeliverySettings Delivery);
internal sealed record AcquisitionLocalFilesProvenance(string PartId, int DownloadTaskId, int DownloadResultId,
    string DownloadIntent, string DownloadEvidence, string Fingerprint, string ArtifactId, string OperationId);

// Only the verified local recovery path writes this receipt. It supplements a stopped
// checkpoint without rewriting that run's historical current item or frozen plan.
internal sealed record AcquisitionLocalFilesRecoveryReceipt(int PreviousRunId, int PreviousRevision,
    string Checkpoint, AcquisitionLocalFilesStorage Storage, IReadOnlyList<AcquisitionLocalFilesProvenance> Parts,
    AcquisitionLocalFilesRecoveryPreview Preview, int? ResultingRevision = null)
{
    internal const string RevisionReason = "recovery.localFiles.currentStorage";
    internal static T? Read<T>(string? json) where T : class => string.IsNullOrWhiteSpace(json) ? null :
        JsonSerializer.Deserialize<T>(json, WorkflowJson.Options);
    internal static AcquisitionLocalFilesRecoveryReceipt? Read(AcquisitionTaskDbModel task) =>
        Read<AcquisitionPlanBuilderState>(task.BuilderStateJson)?.LocalFilesRecovery;
    internal static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, WorkflowJson.Options))));
    internal static bool Awaiting(WorkflowRunDbModel run) => run.Status == WorkflowRunStatus.Waiting &&
        run.PendingSignalJson == null && run.WaitReason == "cloud.cloudAttention" &&
        WorkflowRunPlans.Read(run.PlanSnapshotJson)?.Nodes.ElementAtOrDefault(run.CurrentStepIndex ?? -1)?.Kind == "download.awaitCompletion";
    internal static bool CanReplan(AcquisitionTaskDbModel task, WorkflowRunDbModel run) =>
        task.DeletedAt == null && task.Status == AcquisitionStatus.Waiting && task.WorkflowRunId == run.Id &&
        Read(task) is {ResultingRevision: null} receipt && receipt.PreviousRunId == run.Id &&
        receipt.PreviousRevision == task.PlanRevision && receipt.Checkpoint == Hash(run) && Awaiting(run);

    internal static async Task<string?> DownloadEvidenceAsync(BakabaseDbContext db, string intent, int taskId,
        int resultId, CancellationToken ct)
    {
        var download = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(d => d.Id == taskId, ct);
        var result = await db.DownloadResults.AsNoTracking().SingleOrDefaultAsync(r => r.Id == resultId && r.DownloadTaskId == taskId, ct);
        var operation = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == intent, ct);
        if (download?.Status != DownloadTaskDbModelStatus.Complete || result == null || operation?.DownloadTaskId != taskId)
            return null;
        return Hash(new {download.Id, download.Status, download.DownloadPath, download.Options,
            Result = new {result.Id, result.DownloadTaskId, result.DownloadDirectory, result.FilesJson, result.Fingerprint},
            Operation = new {operation.Id, operation.Kind, operation.State, operation.WorkflowRunId, operation.TargetId,
                operation.DownloadTaskId, operation.RequestJson}});
    }

    internal static async Task<bool> AuthorizesAsync(BakabaseDbContext db, int taskId, string partId,
        WorkflowRunDbModel run, AcquisitionPlanArtifactInput input, CancellationToken ct)
    {
        var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct);
        var receipt = task == null ? null : Read(task);
        if (task?.DeletedAt != null || receipt == null || receipt.PreviousRunId != run.Id || receipt.Checkpoint != Hash(run) ||
            !Awaiting(run) || task!.PlanRevision < receipt.PreviousRevision ||
            receipt.ResultingRevision is { } revision && task.PlanRevision < revision) return false;
        var part = receipt.Parts.SingleOrDefault(p => p.PartId == partId);
        if (part == null || part.ArtifactId != input.ArtifactId || part.OperationId != input.OperationId ||
            part.Fingerprint != input.Fingerprint ||
            receipt.ResultingRevision == null &&
            await DownloadEvidenceAsync(db, part.DownloadIntent, part.DownloadTaskId, part.DownloadResultId, ct) != part.DownloadEvidence)
            return false;
        var targets = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson)?.Targets.Where(t => t.Id == partId).ToArray();
        if (targets is not {Length: 1} || targets[0].DownloadTaskId != part.DownloadTaskId || targets[0].SelectedSource == null)
            return false;
        var bindings = await db.ArtifactOperationBindings.AsNoTracking()
            .Where(b => b.OperationId == part.DownloadIntent || b.OperationId == part.OperationId).ToListAsync(ct);
        if (!bindings.Any(b => b.OwnerKind == "workflow" && b.OwnerId == run.Id.ToString() &&
            b.OperationId == part.DownloadIntent && b.Slot == CloudOperationService.OperationSlot(targets[0], "download"))) return false;
        foreach (var binding in bindings)
        {
            if (binding.OwnerKind != "workflow") return false;
            if (binding.OwnerId == run.Id.ToString() &&
                (binding.Slot == CloudOperationService.OperationSlot(targets[0], "download") || binding.Slot == partId + ":artifact.download")) continue;
            // Subsequent local revisions legitimately borrow this artifact; a different
            // acquisition or slot must never acquire provenance through this receipt.
            if (binding.Slot != partId + ":artifact.continuation" || !int.TryParse(binding.OwnerId, out var consumerRun) ||
                !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, consumerRun, taskId, ct)) return false;
        }
        return true;
    }
}
