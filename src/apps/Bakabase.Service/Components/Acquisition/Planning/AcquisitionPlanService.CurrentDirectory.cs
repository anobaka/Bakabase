using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Modules.Workflow.Artifacts;

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
        if (!input.Items.Any(i => i.InputMode == "currentDirectory")) return input;
        if (input.CompositeInputs != null || input.DeliveryContentLayout != null || input.RetryDeliveryOnly == true ||
            !input.Items.Select(i => i.PartId).ToHashSet().SetEquals(plan.Parts.Select(p => p.Id)) ||
            input.Items.Any(i => i.ContinueFromArtifactId == null))
            throw new ArgumentException("PLAN_CURRENT_DIRECTORY_MODE_INVALID");
        var items = input.Items.ToArray();
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
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
}
