using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    // Repair only a never-started stage hand-off proven by the currently waiting run.
    // An empty directory by itself is never permission to restore an older download.
    private async Task RepairUninitializedProcessingInputAsync(AcquisitionTaskDbModel row, AcquisitionPlan plan,
        string partId, AcquisitionStageDirectory binding, Func<string?, Task>? guardDirectory, CancellationToken ct)
    {
        if (workspaces == null || artifacts == null || stageScopeFactory == null || stageWriteGuard == null ||
            row.WorkflowRunId is not {} runId || binding.Allocation == null || !Directory.Exists(binding.Directory)) return;
        var part = plan.Parts.Single(p => p.Id == partId);
        if (part.ContinueFrom is not {} continuation) return;
        var run = await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == runId, ct);
        if (run.Status != WorkflowRunStatus.Waiting || run.PendingSignalJson != null ||
            !AcquisitionProcessingConflictWait.MatchesCurrentDirectoryWait(run)) return;
        var target = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson)?.Targets.SingleOrDefault(t => t.Id == partId);
        if (target == null || target.Excluded || target.ProcessedArtifactId != continuation.ArtifactId ||
            target.ProcessingOperationId != continuation.OperationId || target.ProcessingStepArtifacts.Count != 0 ||
            target.PublishedArtifactId != null || target.PublicationOperationId != null || target.PublishedFiles.Count != 0 ||
            target.StageDirectories?.Processing is not {} frozen || frozen.BindingId != binding.BindingId ||
            !ArtifactManifestFiles.PathComparer.Equals(frozen.Directory, binding.Directory)) return;
        var source = await artifacts.GetArtifactAsync(continuation.ArtifactId, ct);
        if (source?.Kind != AcquisitionCurrentDirectoryFiles.Kind || source.MetadataJson == null ||
            source.OperationId != continuation.OperationId || source.Fingerprint != continuation.Fingerprint) return;
        var download = plan.StageDirectories?.Parts.GetValueOrDefault(partId)?.Download;
        if (download == null || !ArtifactManifestFiles.PathComparer.Equals(download.Directory, source.RootDirectory) ||
            !await AcquisitionCurrentDirectoryFiles.IsOwnedAsync(db, artifacts, source, row.Id, runId, partId, ct)) return;
        try
        {
            using var metadata = JsonDocument.Parse(source.MetadataJson);
            if (metadata.RootElement.GetProperty("version").GetInt32() == 2)
            {
                var capture = JsonSerializer.Deserialize<AcquisitionStageDirectoryCaptureReceipt>(source.MetadataJson, Json);
                if (capture is not {Stage: "download"} || capture.TaskId != row.Id || capture.PartId != partId ||
                    capture.BindingId != download.BindingId) return;
            }
            else
            {
                // Older exact-root download bindings use a v1 captured directory. Its
                // prior artifact must itself be a download, never a prior processed result.
                var capture = JsonSerializer.Deserialize<AcquisitionCurrentDirectoryReceipt>(source.MetadataJson, Json);
                if (capture is not {Version: 1, PriorInput: not null} ||
                    await artifacts.GetArtifactAsync(capture.PriorInput.ArtifactId, ct) is not {Kind: "download"} prior ||
                    !ArtifactManifestFiles.PathComparer.Equals(prior.RootDirectory, source.RootDirectory)) return;
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException) {return;}
        // Any committed local processing, even when its visible files were later moved or
        // removed by the user, rules out initialization from the captured download.
        var operations = db.ArtifactOperationBindings.Where(b => b.OwnerKind == "workflow" && b.OwnerId == runId.ToString() &&
            b.Slot.StartsWith(partId + ":")).Select(b => b.OperationId);
        if (await db.ArtifactOperations.AnyAsync(o => operations.Contains(o.Id) && o.Kind == "files.process" &&
                o.State == ArtifactOperationState.Completed, ct)) return;
        storage.EnsureTreeMutationAllowed(binding.Directory);
        if (guardDirectory != null) await guardDirectory(source.RootDirectory);
        var excluded = (await workspaces.OwnedDirectoriesUnderAsync(binding.Directory, ct)).Select(p => p.Directory)
            .ToHashSet(ArtifactManifestFiles.PathComparer);
        if (AcquisitionStageDirectoryFileMove.Inspect(binding.Directory, excluded, ct).Files.Count != 0) return;
        await AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(source, workspaces, ct);
        using var writeScope = StorageWriteScope.Enter(new("workflow", runId.ToString()));
        await AcquisitionProcessingStageInput.PrepareCoreAsync(db, artifacts, storage, workspaces, row.Id, runId,
            partId, binding, source, new FileArtifactMaterializer(stageWriteGuard), stageScopeFactory, ct, plan.StoragePolicy);
    }
}
