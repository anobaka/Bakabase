using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.HttpDownloads.Abstractions;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.CloudDownloads;

/// <summary>Read and resume original magnet downloads recorded before native dispatches
/// carried a workflow write context or a durable consumer.</summary>
internal sealed class CloudLegacyBitTorrentResume(BakabaseDbContext db, IBtDownloadManager bt,
    IHttpDownloadManager? http, IArtifactOperationStore store, IUserStoragePolicy storage,
    FileProcessingWorkspaceResolver? workspaces)
{
    internal sealed record Dispatch(BtLegacyWorkflowMagnet Native, ArtifactOperationRequest Operation, string Owner,
        BtDownloadJob Job);

    public async Task<Dispatch?> InspectAsync(int runId, CloudDownloadTarget target, string outputRoot,
        CancellationToken ct, bool executing = false)
    {
        if (target is not {Excluded: false, DownloadTaskId: null, AcquisitionNonce: null, DownloadDirectoryScope: null,
                RemoteEntry: null, DownloadArtifactId: null, ProcessedArtifactId: null, PublishedArtifactId: null,
                ProcessingOperationId: null, PublicationOperationId: null, CleanupReceiptId: null,
                Files.Count: 0, ProcessedFiles.Count: 0, PublishedFiles.Count: 0, ProcessingStepArtifacts.Count: 0} ||
            target.SelectedSource is not {ProviderId: "magnet", Kind: "share"} source ||
            target.TransportTaskId is not {} transport || !transport.StartsWith("bt:", StringComparison.Ordinal) ||
            !Guid.TryParseExact(transport[3..], "N", out _) || !Path.IsPathFullyQualified(outputRoot) ||
            CloudStageDirectoryPath.Download(target) != null) return null;

        var run = await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct);
        if (run == null || run.PendingSignalJson != null || run.CurrentStepIndex is not >= 1 ||
            (executing ? run.Status != WorkflowRunStatus.Running :
                run.Status != WorkflowRunStatus.Waiting || run.WaitReason != "cloud.cloudAttention")) return null;
        var plan = WorkflowRunPlans.Read(run.PlanSnapshotJson);
        var index = run.CurrentStepIndex.Value;
        if (plan?.TriggerKind != CloudWorkflow.Trigger || plan.Nodes.ElementAtOrDefault(index)?.Kind != "download.awaitCompletion" ||
            plan.Nodes[index - 1] is not {Kind: "download.enqueueSupported"} enqueue) return null;
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson);
        if (workset is not {Targets.Count: 1, NoRepeatExistingDownloads: true} ||
            !ArtifactManifestFiles.PathComparer.Equals(outputRoot, workset.OutputDirectory)) return null;
        var recorded = workset.Targets[0];
        if (JsonSerializer.Serialize(recorded, WorkflowJson.Options) != JsonSerializer.Serialize(target, WorkflowJson.Options)) return null;

        var tasks = await db.AcquisitionTasks.AsNoTracking().Where(t => t.WorkflowRunId == runId).Take(2).ToListAsync(ct);
        if (tasks.Count != 1 || tasks[0] is not {DeletedAt: null, PlanRevision: > 0} task ||
            task.Status is AcquisitionStatus.Cancelled or AcquisitionStatus.Completed ||
            workset.Id != FormattableString.Invariant($"acquisition:{task.Id}:{task.PlanRevision}") ||
            !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, runId, task.Id, ct)) return null;

        var dispatched = await db.Set<WorkflowNodeExecutionDbModel>().AsNoTracking()
            .Where(n => n.WorkflowRunId == runId && n.StepIndex == index - 1 && n.NodeId == enqueue.NodeId &&
                n.Kind == enqueue.Kind && n.Status == "succeeded").Take(2).ToListAsync(ct);
        if (dispatched.Count != 1 || dispatched[0].OutputsTruncated || dispatched[0].OutputsJson.Length > WorkflowOutputMetadataCapture.MaximumCharacters ||
            JsonSerializer.Deserialize<WorkflowOutputBlock[]>(dispatched[0].OutputsJson, WorkflowJson.Options) is not {} outputs ||
            outputs.Count(b => b is {Kind: "task", TaskKind: "download"} && b.TaskId == transport && b.Key == target.Id + ":downloadTask") != 1)
            return null;

        // Old checkpoints have neither a consumer nor a slot binding. The exclusive recorded
        // lease owner and the unchanged first attempt are mandatory proof, even if the lease expired.
        var intent = DownloadArtifactCoordinator.DownloadIntent(recorded, outputRoot, workspaces);
        var input = DownloadArtifactCoordinator.Source(recorded);
        var owner = FormattableString.Invariant($"workflow:{runId}:{target.Id}");
        var operation = await store.GetOperationAsync(intent, ct);
        if (operation is not {Kind: "download", Attempt: 1, Restartable: true, State: ArtifactOperationState.Running,
                OutputArtifactId: null, LeaseToken.Length: > 0} || operation.IntentKey != intent ||
            operation.InputFingerprint != input || operation.LeaseOwner != owner ||
            target.DownloadOperationId != null && target.DownloadOperationId != intent ||
            await db.ArtifactConsumers.AsNoTracking().AnyAsync(c => c.OperationId == intent, ct) ||
            await db.Artifacts.AsNoTracking().AnyAsync(a => a.OperationId == intent, ct)) return null;
        var runIdText = runId.ToString(CultureInfo.InvariantCulture);
        var slot = CloudOperationService.OperationSlot(recorded, "download");
        if (await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OperationId == intent ||
            b.OwnerKind == "workflow" && b.OwnerId == runIdText && b.Slot == slot, ct)) return null;
        var nativeId = transport[3..];
        if (await db.ManagedDownloadTasks.AsNoTracking()
                .Where(d => d.Id == transport || d.Provider == "bt" && d.ProviderTaskId == nativeId)
                .AnyAsync(d => d.Id != transport || d.Provider != "bt" || d.ProviderTaskId != nativeId ||
                    d.OriginKind != "workflow" || d.OriginId != intent || d.WorkflowRunId != null ||
                    d.WorkflowDefinitionId != null || d.DownloadResultId != null || d.SiteTaskId != null || d.ParentId != null, ct))
            return null;

        var token = "\"" + transport + "\"";
        if (await db.WorkflowRuns.AsNoTracking().AnyAsync(r => r.Id != runId && r.CurrentItemJson != null &&
                r.CurrentItemJson.Contains(token), ct) ||
            await db.Set<WorkflowNodeExecutionDbModel>().AsNoTracking().AnyAsync(n => n.WorkflowRunId != runId &&
                (n.OutputsJson.Contains(token) || n.InputsJson != null && n.InputsJson.Contains(token)), ct)) return null;

        var folder = recorded.StoragePolicy == null ? "cloud-" + intent[..16] :
            ArtifactStoragePolicies.RenderDownload(recorded.StoragePolicy, recorded.Title, intent, source.ProviderId);
        var directory = Path.GetFullPath(Path.Combine(outputRoot, folder, operation.Attempt.ToString(CultureInfo.InvariantCulture)));
        var native = new BtLegacyWorkflowMagnet(nativeId, intent, operation.Attempt, source.Url, directory);
        if (await bt.InspectLegacyWorkflowMagnetAsync(native, ct) is not {} job) return null;
        var prefix = directory + Path.DirectorySeparatorChar;
        if (await db.DownloadTasks.AsNoTracking().AnyAsync(d => d.DownloadPath == directory ||
                d.DownloadPath.StartsWith(prefix) || (d.DownloadPath != "" && directory.StartsWith(d.DownloadPath + Path.DirectorySeparatorChar)), ct) ||
            http != null && (await http.ReadSnapshotAsync(ct)).Any(j => Overlaps(directory, j.OutputDirectory))) return null;
        storage.EnsurePathAllowed(directory);
        ArtifactManifestFiles.RejectLinks(directory);
        return new(native, new(intent, intent, "download", input), owner, job);
    }

    public async Task ResumeAsync(int runId, CloudDownloadTarget target, string outputRoot, CancellationToken ct)
    {
        var gate = WorkflowRunMutationGate.For(runId);
        await gate.WaitAsync(ct);
        try
        {
            var dispatch = await InspectAsync(runId, target, outputRoot, ct, executing: true)
                ?? throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
            if (!dispatch.Job.CanResume && !dispatch.Job.CanRetry) return;
            var claim = await store.ClaimSameAttemptAsync(dispatch.Operation, dispatch.Native.Attempt, dispatch.Owner,
                TimeSpan.FromMinutes(2), ct);
            if (claim.Status != ArtifactClaimStatus.Acquired || claim.StartedNewAttempt ||
                await bt.ResumeLegacyWorkflowMagnetAsync(dispatch.Native, ct) == null)
                throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
        }
        finally {gate.Release();}
    }

    private static bool Overlaps(string left, string right)
    {
        static bool Within(string root, string path)
        {
            var relative = Path.GetRelativePath(root, path);
            return !Path.IsPathRooted(relative) && relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }
        return Within(left, right) || Within(right, left);
    }
}
