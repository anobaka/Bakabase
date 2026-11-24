using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal static class AcquisitionPlanArtifactInputGuard
{
    public static async Task<ArtifactRecord> RequireAsync(BakabaseDbContext db, IArtifactOperationStore store,
        int taskId, string partId, AcquisitionPlanArtifactInput input, CancellationToken ct, bool publishedOutputOnly = false,
        bool publicationInputOnly = false, bool currentDirectorySource = false, AcquisitionStageDirectory? capturedDelivery = null)
    {
        if (input.PreviousRunId == 0)
        {
            var received = await store.GetArtifactAsync(input.ArtifactId, ct);
            if (input.TaskId != taskId || publishedOutputOnly || publicationInputOnly ||
                received is not {State: ArtifactLocationState.Available} || received.OperationId != input.OperationId ||
                received.Fingerprint != input.Fingerprint ||
                !await AcquisitionCurrentDirectoryFiles.IsOwnedAsync(db, store, received, taskId, 0, partId, ct))
                throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
            return received;
        }
        if (input.ManualDownload == true) throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
        var previous = input.PreviousRunId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var run = await db.Set<WorkflowRunDbModel>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == input.PreviousRunId, ct);
        var ownsRun = run != null && await OwnsRunAsync(db, input.PreviousRunId, taskId, ct);
        if (!ownsRun && run != null && input.TaskId == taskId && !publishedOutputOnly && !publicationInputOnly)
        {
            var state = await db.AcquisitionTasks.AsNoTracking().Where(t => t.Id == taskId && t.DeletedAt == null)
                .Select(t => t.BuilderStateJson).SingleOrDefaultAsync(ct);
            var saved = AcquisitionLocalFilesRecoveryReceipt.Read<AcquisitionPlanBuilderState>(state)?.RegisteredDownload;
            if (saved?.Input == input && saved.PartId == partId)
                return await saved.RequireAsync(db, store, run, taskId, partId, input, ct) ??
                    throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
        }
        var localDownloadReceipt = run != null && await AcquisitionLocalFilesRecoveryReceipt.AuthorizesAsync(db,
            taskId, partId, run, input, ct);
        var publicationStorageWait = run != null && AcquisitionPublicationStorageWait.Matches(run);
        var currentFilesWait = run != null && AcquisitionProcessingConflictWait.MatchesCurrentDirectoryWait(run);
        var artifact = await store.GetArtifactAsync(input.ArtifactId, ct);
        var stageFiles = artifact?.Kind == AcquisitionCurrentDirectoryFiles.Kind && artifact.MetadataJson != null &&
            JsonSerializer.Deserialize<AcquisitionStageDirectoryCaptureReceipt>(artifact.MetadataJson, WorkflowJson.Options) is {Version: 2} &&
            run is {Status: WorkflowRunStatus.Waiting, PendingSignalJson: null} &&
            await AcquisitionCurrentDirectoryFiles.IsOwnedAsync(db, store, artifact, taskId, input.PreviousRunId, partId, ct);
        var requiresStoppedPublication = publicationInputOnly &&
            (capturedDelivery == null || !AcquisitionCurrentDirectoryFiles.IsDeliveryOnlyStageCapture(artifact, capturedDelivery));
        if (requiresStoppedPublication && (run == null || !AcquisitionPublicationStorageWait.CanRetryDelivery(run)))
            throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
        if (input.TaskId != taskId || !ownsRun || run == null ||
            run.Status is not (WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted or WorkflowRunStatus.Cancelled or WorkflowRunStatus.Success) &&
            !AcquisitionProcessingConflictWait.MatchesForLocalContinuation(run) && !publicationStorageWait && !localDownloadReceipt && !currentFilesWait && !stageFiles)
            throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
        if (artifact?.Kind == AcquisitionCurrentDirectoryFiles.Kind)
        {
            if (publishedOutputOnly || requiresStoppedPublication ||
                !await AcquisitionCurrentDirectoryFiles.IsOwnedAsync(db, store, artifact, taskId, input.PreviousRunId, partId, ct) ||
                artifact.OperationId != input.OperationId || artifact.Fingerprint != input.Fingerprint ||
                (currentDirectorySource ? artifact.State is ArtifactLocationState.Quarantined or ArtifactLocationState.Retired :
                    artifact.State != ArtifactLocationState.Available))
                throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
            return artifact;
        }
        if (run.Status == WorkflowRunStatus.Waiting && !publicationStorageWait && !localDownloadReceipt &&
            (currentDirectorySource
                ? !AcquisitionProcessingConflictWait.MatchesCurrentInput(run, partId, input.ArtifactId, input.OperationId)
                : !AcquisitionProcessingConflictWait.MatchesForLocalContinuation(run)))
            throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
        if (AcquisitionProcessingConflictWait.InitialUnknownWorkset(run) is { } initial &&
            !AcquisitionProcessingConflictWait.MatchesInitialInput(initial, partId, input.ArtifactId, input.OperationId))
            throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
        var slots = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
            .Where(b => b.OwnerKind == "workflow" && b.OwnerId == previous && b.OperationId == input.OperationId)
            .Select(b => b.Slot).ToListAsync(ct);
        // Successful runs lend only their registered processing or publication outputs.
        // A publication is borrowed in place; it never becomes a new download source.
        if (run.Status == WorkflowRunStatus.Success && artifact?.Kind is not ("processedFiles" or "publishedFiles" or AcquisitionCompositeFiles.Kind) &&
            !(currentDirectorySource && artifact?.Kind == "download"))
            throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
        var continued = slots.Contains(partId + ":artifact.continuation");
        var bound = artifact?.Kind switch
        {
            "processedFiles" => continued || slots.Any(s => s.StartsWith(partId + ":process:", StringComparison.Ordinal)) ||
                slots.Contains(partId + ":adaptive:output:" + artifact.Id),
            "download" => continued || slots.Contains(partId + ":artifact.download"),
            "publishedFiles" => continued || slots.Contains(partId + ":artifact.publish"),
            AcquisitionCompositeFiles.Kind => !publishedOutputOnly && artifact != null &&
                await AcquisitionCompositeFiles.IsOwnedAsync(db, store, artifact, taskId, input.PreviousRunId, partId, ct),
            _ => false
        };
        if (!bound || publishedOutputOnly && (artifact?.Kind != "publishedFiles" || !slots.Contains(partId + ":artifact.publish"))) throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
        var operation = await store.GetOperationAsync(input.OperationId, ct);
        if (artifact?.Kind == "publishedFiles" && operation?.Kind != "files.publish" ||
            run.Status == WorkflowRunStatus.Success && artifact?.Kind == "processedFiles" && operation?.Kind != "files.process")
            throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
        if (artifact == null || artifact.OperationId != input.OperationId || artifact.Fingerprint != input.Fingerprint ||
            (currentDirectorySource ? artifact.State is ArtifactLocationState.Quarantined or ArtifactLocationState.Retired :
                artifact.State != ArtifactLocationState.Available) || artifact.Manifest.Entries.Count == 0 ||
            artifact.Manifest.Fingerprint != input.Fingerprint || operation?.State != ArtifactOperationState.Completed ||
            operation.OutputArtifactId != artifact.Id)
            throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
        if (publicationStorageWait || publicationInputOnly)
        {
            var target = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson)?.Targets
                .SingleOrDefault(t => t.Id == partId && !t.Excluded);
            if (target == null || target.ProcessedArtifactId != artifact.Id || target.ProcessingOperationId != artifact.OperationId ||
                !(artifact.Kind == "processedFiles" && operation.Kind == "files.process" && target.ProcessingIntentKey == operation.IntentKey ||
                  artifact.Kind == "publishedFiles" && operation.Kind == "files.publish" &&
                  await IsBorrowedPublicationInputAsync(db, taskId, run, target, artifact, ct)))
                throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
        }
        // Registration, rather than a provider's mutable queue row, is the durable completion
        // receipt. Replan and the continuation activity additionally validate every manifest
        // hash; this guard must never replace it with a directory scan or a new download.
        try { _ = ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest); }
        catch (InvalidDataException) { throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE"); }
        if (artifact.Kind == "download")
        {
            if (operation.Kind != "download" || string.IsNullOrWhiteSpace(operation.InputFingerprint))
                throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
            // A continuation binding is written only after this same input has passed the
            // ownership/manifest checks. It preserves provenance across further stopped runs.
            if (!continued)
            {
                var workset = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson);
                var targets = workset?.Targets.Where(t => t.Id == partId).ToArray();
                if (targets is not {Length: 1} || targets[0].SelectedSource == null ||
                    DownloadArtifactCoordinator.Source(targets[0]) != operation.InputFingerprint ||
                    !localDownloadReceipt && (targets[0].DownloadArtifactId != artifact.Id || targets[0].DownloadOperationId != artifact.OperationId ||
                    !targets[0].Files.ToHashSet(ArtifactManifestFiles.PathComparer).SetEquals(artifact.Files)))
                    throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
            }
        }
        return artifact;
    }

    // A local revision may attach an earlier delivery as its processed input. The artifact
    // retains its original producer. Only the exact input accepted by the frozen continuation
    // node and bound to this part may stand in for the completed processing result.
    internal static async Task<bool> IsBorrowedPublicationInputAsync(BakabaseDbContext db, int taskId,
        WorkflowRunDbModel run, CloudDownloadTarget target, ArtifactRecord artifact, CancellationToken ct)
    {
        if (artifact.Kind != "publishedFiles" || artifact.State != ArtifactLocationState.Available ||
            artifact.Manifest.Entries.Count == 0 || artifact.Manifest.Fingerprint != artifact.Fingerprint ||
            target.ProcessedArtifactId != artifact.Id || target.ProcessingOperationId != artifact.OperationId ||
            run.CurrentStepIndex is not > 0) return false;
        AcquisitionPlanArtifactInput[] inputs;
        try
        {
            var plan = WorkflowRunPlans.Read(run.PlanSnapshotJson);
            if (plan == null) return false;
            inputs = plan.Nodes.Take(run.CurrentStepIndex.Value)
                .Where(n => n.Kind == AcquisitionPlanContinueFilesActivity.ActivityKind)
                .Select(n => JsonSerializer.Deserialize<AcquisitionPlanContinueFilesConfig>(n.ConfigJson, WorkflowJson.Options))
                .Where(config => config?.Targets != null)
                .SelectMany(config => config!.Targets)
                .Where(entry => entry?.Target?.Id == target.Id)
                .Select(entry => entry.Input).ToArray();
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        { return false; }
        if (inputs is not [{ } input] || input.TaskId != taskId || input.PreviousRunId == run.Id ||
            input.ArtifactId != artifact.Id || input.OperationId != artifact.OperationId || input.Fingerprint != artifact.Fingerprint ||
            target.ProcessingIntentKey != Intent(input)) return false;
        return await HasContinuationBindingAsync(db, run.Id, target.Id, artifact.OperationId, ct) &&
            await OwnsRunAsync(db, input.PreviousRunId, taskId, ct) &&
            await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" &&
                b.OwnerId == input.PreviousRunId.ToString() && b.OperationId == artifact.OperationId &&
                (b.Slot == target.Id + ":artifact.publish" || b.Slot == target.Id + ":artifact.continuation"), ct);
    }

    public static async Task<bool> OwnsRunAsync(BakabaseDbContext db, long runId, int taskId, CancellationToken ct)
    {
        var task = taskId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var owners = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.WorkflowRunId == runId &&
            s.Module == "acquisition" && s.EntityKind == "task").Select(s => s.EntityId).Distinct().ToArrayAsync(ct);
        // A local continuation may not silently borrow a run shared with another acquisition.
        return owners is {Length: 1} && owners[0] == task;
    }

    // This request only presents a choice to the user. A later command must still validate
    // content before processing, publishing or registering any files.
    internal static bool HasExpectedFileLayout(ArtifactRecord artifact, IUserStoragePolicy storage, CancellationToken ct)
    {
        storage.EnsurePathAllowed(artifact.RootDirectory);
        ArtifactManifestFiles.RejectLinks(artifact.RootDirectory);
        ArtifactManifestFiles.VerifyDirectories(artifact.RootDirectory, artifact.Manifest);
        if (artifact.Files.Count == 0 || artifact.Files.Count != artifact.Manifest.Entries.Count ||
            artifact.Fingerprint != artifact.Manifest.Fingerprint ||
            ArtifactManifestFiles.Fingerprint(artifact.Manifest.Entries, artifact.Manifest.Directories) != artifact.Fingerprint)
            return false;
        for (var i = 0; i < artifact.Files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = artifact.Files[i];
            storage.EnsurePathAllowed(file);
            ArtifactManifestFiles.RejectLinks(file);
            if (!File.Exists(file) || new FileInfo(file).Length != artifact.Manifest.Entries[i].Length) return false;
        }
        return true;
    }

    internal static async Task<bool> HasPinnedContinuationAsync(BakabaseDbContext db, int runId, string partId,
        string operationId, CancellationToken ct)
    {
        var owner = runId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var consumer = Consumer(runId, partId);
        return await db.Set<WorkflowRunDbModel>().AnyAsync(r => r.Id == runId && r.Status == WorkflowRunStatus.Running, ct) &&
            await db.Set<ArtifactConsumerDbModel>().AnyAsync(c => c.OperationId == operationId && c.ConsumerKey == consumer &&
                c.OwnerKind == "workflow" && c.OwnerId == owner && c.ReleasedAt == null, ct) &&
            await HasContinuationBindingAsync(db, runId, partId, operationId, ct);
    }

    internal static Task<bool> HasContinuationBindingAsync(BakabaseDbContext db, int runId, string partId,
        string operationId, CancellationToken ct) => db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
        .AnyAsync(b => b.OwnerKind == "workflow" && b.OwnerId == runId.ToString() &&
            b.Slot == partId + ":artifact.continuation" && b.OperationId == operationId, ct);

    public static string Intent(AcquisitionPlanArtifactInput input) => "acquisition.artifact-input:" + input.ArtifactId + ":" + input.Fingerprint;
    public static string Consumer(long runId, string partId) => "workflow:" + runId + ":continuation:" + partId;
}
