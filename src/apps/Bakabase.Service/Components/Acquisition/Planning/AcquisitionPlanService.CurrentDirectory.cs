using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    private AcquisitionPlan PrepareCurrentDirectoryStorage(AcquisitionTaskDbModel row, AcquisitionPlan plan)
    {
        var state = State(row);
        var policy = state.StoragePolicy ?? plan.StoragePolicy ?? new ArtifactStoragePolicy();
        // Old tasks often have no frozen workspace setting at all. Reading their current
        // files is a new processing attempt, not permission to put scratch files inside
        // that input directory. Capture only the missing placement default; keep existing
        // task/part/step overrides and retention preferences unchanged.
        if (string.IsNullOrWhiteSpace(policy.ProcessingWorkspaceDirectory))
            policy = policy with {ProcessingWorkspaceDirectory = CaptureStorage().ProcessingWorkspaceDirectory};
        row.BuilderStateJson = Serialize(state with {StoragePolicy = policy});
        return plan with {StoragePolicy = policy};
    }

    private async Task<AcquisitionPlanReplanInput> PrepareCurrentDirectoryInputsAsync(AcquisitionTaskDbModel row,
        AcquisitionPlan plan, AcquisitionPlanReplanInput input, Func<string?, Task>? guardDirectory, CancellationToken ct)
    {
        if (!input.Items.Any(i => i.InputMode is "currentDirectory" or "stageDirectory")) return input;
        var stages = input.Items.All(i => i.InputMode == "stageDirectory");
        if (input.CompositeInputs != null || input.DeliveryContentLayout != null || input.RetryDeliveryOnly == true && !stages ||
            !input.Items.Select(i => i.PartId).ToHashSet().SetEquals(plan.Parts.Select(p => p.Id)) ||
            input.Items.Any(i => i.ContinueFromArtifactId == null && i.InputMode != "stageDirectory"))
            throw new ArgumentException("PLAN_CURRENT_DIRECTORY_MODE_INVALID");
        var items = input.Items.ToArray();
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (item.InputMode == "stageDirectory")
            {
                items[index] = await PrepareStageInputAsync(row, plan, input, item, guardDirectory, ct);
                continue;
            }
            if (item.InputMode != "currentDirectory") continue;
            if (item.ContinueFromArtifactId == null || item.ArtifactEvidenceIds.Count != 1 ||
                item.ArtifactEvidenceIds[0] != item.ContinueFromArtifactId || item.ExpectedDeliveryProjectionFingerprint != null)
                throw new ArgumentException("PLAN_CURRENT_DIRECTORY_MODE_INVALID");
            FileProcessingPlanValidator.Validate(item.Plan);
            var part = plan.Parts.SingleOrDefault(p => p.Id == item.PartId)
                ?? throw new ArgumentException("PLAN_REPLAN_INVALID");
            var source = await artifacts!.GetArtifactAsync(item.ContinueFromArtifactId, ct)
                ?? throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
            var previous = part.ContinueFrom?.ArtifactId == source.Id ? part.ContinueFrom.PreviousRunId : input.ExpectedRunId;
            var captured = await AcquisitionCurrentDirectoryFiles.CaptureAsync(db, artifacts, storage, workspaces,
                row.Id, row.PlanRevision + 1, input.ExpectedRunId, item.PartId, source, guardDirectory, ct, previous);
            items[index] = item with {ArtifactEvidenceIds = [captured.Id], ContinueFromArtifactId = captured.Id};
        }
        return input with {Items = items};
    }

    private async Task<AcquisitionPlanProcessingReplacement> PrepareStageInputAsync(AcquisitionTaskDbModel row,
        AcquisitionPlan plan, AcquisitionPlanReplanInput input, AcquisitionPlanProcessingReplacement item,
        Func<string?, Task>? guardDirectory, CancellationToken ct)
    {
        FileProcessingPlanValidator.Validate(item.Plan);
        if (!plan.Parts.Any(p => p.Id == item.PartId) || !AcquisitionCurrentDirectoryFiles.ValidStage(item.InputStage))
            throw new ArgumentException("PLAN_STAGE_DIRECTORY_INVALID");
        await RequireStageDirectoryInactiveAsync(row, ct);
        var directories = await CurrentStageDirectoriesAsync(row, plan, ct);
        var binding = AcquisitionCurrentDirectoryFiles.Entry(directories, item.PartId, item.InputStage!)
            ?? throw new InvalidOperationException("PLAN_STAGE_DIRECTORY_UNAVAILABLE");
        var owners = await StageDirectoryOwnersAsync(row, ct);
        async Task Guard()
        {
            await RequireStageDirectoryInactiveAsync(row, ct);
            await StageDirectoryReferencesAsync(row, owners, item.PartId, binding.Directory, ct);
            if (guardDirectory != null) await guardDirectory(binding.Directory);
        }
        await Guard();
        if (item.InputStage == "processing" && binding.BindingId != null)
            await RepairUninitializedProcessingInputAsync(row, plan, item.PartId, binding, guardDirectory, ct);
        ArtifactRecord captured;
        if (binding.BindingId != null)
            captured = await AcquisitionCurrentDirectoryFiles.CaptureStageAsync(db, artifacts!, storage, workspaces,
                row.Id, row.PlanRevision + 1, input.ExpectedRunId, item.PartId, item.InputStage!, binding, Guard, ct);
        else
        {
            // Legacy locations remain usable only through the server's recorded exact-root
            // artifact. A missing/new explicit folder never falls back to an older result.
            await Guard();
            ArtifactRecord? source = null;
            foreach (var record in await db.Artifacts.AsNoTracking().Where(a => a.State != ArtifactLocationState.Retired)
                         .Select(a => new {a.Id, a.RootPath, a.RootIsAppData}).ToArrayAsync(ct))
            {
                var root = await workspaces.ReadOriginalArtifactDirectoryAsync(new Bakabase.Modules.Workflow.Artifacts.ArtifactDbModel
                    {RootPath = record.RootPath, RootIsAppData = record.RootIsAppData, State = ArtifactLocationState.Available}, ct);
                if (root == null || !ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(root), Path.GetFullPath(binding.Directory))) continue;
                var candidate = await artifacts!.GetArtifactAsync(record.Id, ct);
                if (candidate == null || !ArtifactManifestFiles.PathComparer.Equals(
                    Path.GetFullPath(candidate.RootDirectory), Path.GetFullPath(binding.Directory))) continue;
                var continued = plan.Parts.Single(p => p.Id == item.PartId).ContinueFrom;
                var sourceRun = continued?.ArtifactId == candidate.Id ? continued.PreviousRunId : input.ExpectedRunId;
                if (await AcquisitionCurrentDirectoryFiles.CanReadAsync(db, artifacts, storage, workspaces,
                    row.Id, sourceRun, item.PartId, candidate, ct, activeRunId: input.ExpectedRunId)) {source = candidate; break;}
            }
            if (source == null) throw new InvalidOperationException("PLAN_STAGE_DIRECTORY_ALIGNMENT_REQUIRED");
            var previous = plan.Parts.Single(p => p.Id == item.PartId).ContinueFrom;
            captured = await AcquisitionCurrentDirectoryFiles.CaptureAsync(db, artifacts, storage, workspaces, row.Id,
                row.PlanRevision + 1, input.ExpectedRunId, item.PartId, source, guardDirectory, ct,
                previous?.ArtifactId == source.Id ? previous.PreviousRunId : input.ExpectedRunId);
        }
        return item with {InputMode = "currentDirectory", InputStage = null,
            ArtifactEvidenceIds = [captured.Id], ContinueFromArtifactId = captured.Id};
    }

    private static AcquisitionStageDirectories PreserveBoundDirectories(AcquisitionStageDirectories previous,
        AcquisitionStageDirectories replacement) => replacement with {Parts = replacement.Parts.ToDictionary(p => p.Key,
        p => previous.Parts.TryGetValue(p.Key, out var old) ? p.Value with
        {
            Download = old.Download?.BindingId != null ? old.Download : p.Value.Download,
            Processing = old.Processing?.BindingId != null ? old.Processing : p.Value.Processing,
            Delivery = old.Delivery?.BindingId != null ? old.Delivery : p.Value.Delivery
        } : p.Value, StringComparer.Ordinal)};
}
