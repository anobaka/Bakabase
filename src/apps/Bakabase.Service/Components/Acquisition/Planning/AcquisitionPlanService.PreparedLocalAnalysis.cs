using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    // A prepared revision can already own all inputs through older runs even though it has
    // no run of its own. A new task or a plan with an outstanding acquisition cannot enter here.
    internal async Task<bool> CanAnalyzePreparedFilesAsync(AcquisitionTaskDbModel row, CancellationToken ct)
    {
        if (row.WorkflowRunId != null || row.Status != AcquisitionStatus.Waiting || row.DeletedAt != null ||
            row.Phase != "ready" || row.PlanJson == null || row.BuilderId == null || artifacts == null ||
            State(row).DirectoryChange != null) return false;
        var plan = Plan(row);
        if (plan.Parts.Count == 0 || plan.Parts.Any(p => p.ContinueFrom == null) ||
            !AcquisitionLocalFilesRecoveryService.LocalOnly(plan)) return false;
        var id = row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var revision = row.PlanRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (await db.Set<WorkflowRunSourceDbModel>().AnyAsync(s => s.Module == "acquisition" && s.EntityKind == "task" &&
                s.EntityId == id && s.Revision == revision, ct)) return false;
        foreach (var part in plan.Parts)
        {
            var input = await AcquisitionPlanArtifactInputGuard.RequireAsync(db, artifacts, row.Id, part.Id, part.ContinueFrom!, ct);
            storage.EnsurePathAllowed(input.RootDirectory);
            if (input.Kind == AcquisitionCurrentDirectoryFiles.Kind)
                await AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(input, workspaces, ct);
            if (input.Manifest.Entries.Count == 0) return false;
            var files = ArtifactManifestFiles.ResolveFiles(input.RootDirectory, input.Manifest);
            for (var i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var path = files[i];
                storage.EnsurePathAllowed(path);
                ArtifactManifestFiles.RejectLinks(path);
                var file = new FileInfo(path);
                if (!file.Exists || file.Length != input.Manifest.Entries[i].Length) return false;
            }
        }
        return true;
    }

    // Caller holds AcquisitionTaskGate. Persist only a task-local processing choice and
    // retain the old frozen plan. Normal Run validates the complete file contents and
    // starts (or reattaches) the single run for the resulting revision.
    internal async Task<AcquisitionPlanTaskDetail> RunPreparedAnalysisWhileLockedAsync(
        AcquisitionTaskDbModel row, int maxIterations, CancellationToken ct)
    {
        if (!await CanAnalyzePreparedFilesAsync(row, ct)) throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
        var plan = Plan(row);
        var alreadyConfigured = plan.AdaptiveFileProcessing && plan.AdaptiveFileProcessingMaxIterations == maxIterations &&
            plan.Parts.All(p => p.UseAdaptiveAssessment == true && p.Processing is {Requirement: "unknown", Steps.Count: 0});
        if (!alreadyConfigured)
        {
            var expected = row.PlanRevision;
            var previousPlan = row.PlanJson;
            var previousState = row.BuilderStateJson;
            var updated = plan with
            {
                PublishRetainedFiles = false, AdaptiveFileProcessing = true, AdaptiveFileProcessingMaxIterations = maxIterations,
                Parts = plan.Parts.Select(p => p with {Processing = new() {Requirement = "unknown"}, UseAdaptiveAssessment = true,
                    AppliedProcessingRevisionId = null, DeliveryProjectionFingerprint = null}).ToList()
            };
            SavePlan(row, updated, Sources(row), "analysis.preparedLocalFiles");
            var changed = await Tasks.Where(t => t.Id == row.Id && t.DeletedAt == null && t.WorkflowRunId == null &&
                    t.Status == AcquisitionStatus.Waiting && t.Phase == "ready" && t.PlanRevision == expected &&
                    t.PlanJson == previousPlan && t.BuilderStateJson == previousState)
                .ExecuteUpdateAsync(update => update.SetProperty(t => t.PlanRevision, row.PlanRevision)
                    .SetProperty(t => t.PlanJson, row.PlanJson).SetProperty(t => t.PlanHistoryJson, row.PlanHistoryJson)
                    .SetProperty(t => t.BuilderStateJson, row.BuilderStateJson).SetProperty(t => t.UpdatedAt, row.UpdatedAt), ct);
            if (changed != 1) throw new InvalidOperationException("ADAPTIVE_MIGRATION_CHANGED");
        }
        // This action offers no reserve override. Keep the configured reserve even
        // when a global temporary emergency mode was selected for other work.
        return await RunWhileLockedAsync(row.Id, row.PlanRevision, null, ct, useEmergencySpaceReserve: false);
    }
}
