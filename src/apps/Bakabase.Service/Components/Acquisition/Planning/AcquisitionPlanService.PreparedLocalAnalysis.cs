using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    private static bool HasPreparedDownloads(AcquisitionTaskDbModel row, AcquisitionPlan? plan, AcquisitionPlanBuilderState state)
    {
        var receipt = state.LocalFilesRecovery;
        if (row.DeletedAt != null || row.WorkflowRunId != null || row.Status != AcquisitionStatus.Waiting || row.Phase != "ready" ||
            !AcquisitionLocalFilesRecoveryService.CanPrepareWithoutFileOperations(row) || plan is not {Parts.Count: > 0}) return false;
        if (state.RegisteredDownload is {} registered && plan.Parts is [var part])
            return registered.PartId == part.Id && registered.Input == part.ContinueFrom;
        return receipt?.ResultingRevision != null && row.PlanRevision >= receipt.ResultingRevision.Value &&
            receipt.Parts.Count == plan.Parts.Count && plan.Parts.All(p =>
                p.ContinueFrom is { } input && input.TaskId == row.Id && input.PreviousRunId == receipt.PreviousRunId &&
                receipt.Parts.Any(r => r.PartId == p.Id && r.ArtifactId == input.ArtifactId &&
                    r.OperationId == input.OperationId && r.Fingerprint == input.Fingerprint));
    }

    public Task<AcquisitionPlanTaskDetail> KeepPreparedFilesAsync(int id, AcquisitionKeepPreparedFilesInput input,
        CancellationToken ct, Func<string?, Task>? guardDirectory = null) =>
        SetPreparedProcessingChoiceAsync(id, input.ExpectedRevision, "notRequired",
            string.IsNullOrWhiteSpace(input.Reason) ? "delivery.keepPreparedFiles" : input.Reason.Trim(), ct, guardDirectory);

    public Task<AcquisitionPlanTaskDetail> DeferPreparedProcessingAsync(int id, int expectedRevision,
        CancellationToken ct, Func<string?, Task>? guardDirectory = null) =>
        SetPreparedProcessingChoiceAsync(id, expectedRevision, "unknown", "processing.deferPreparedFiles", ct, guardDirectory);

    private async Task<AcquisitionPlanTaskDetail> SetPreparedProcessingChoiceAsync(int id, int expectedRevision,
        string requirement, string reason, CancellationToken ct, Func<string?, Task>? guardDirectory)
    {
        if (reason.Length > 1000) throw new ArgumentException("PLAN_REPLAN_INVALID");
        var gate = AcquisitionTaskGate.For(id);
        await gate.WaitAsync(ct);
        try
        {
            var row = await Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct)
                ?? throw new ArgumentException("ACQUISITION_TASK_NOT_FOUND");
            if (row.PlanJson == null) throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
            var plan = Plan(row);
            var state = State(row);
            if (!HasPreparedDownloads(row, plan, state)) throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
            var configured = !plan.AdaptiveFileProcessing && !plan.PublishRetainedFiles && plan.Parts.All(p =>
                p.UseAdaptiveAssessment == false && p.Processing is {Steps.Count: 0} && p.Processing.Requirement == requirement);
            var same = configured && ReadHistory(row).LastOrDefault()?.Reason == reason;
            // A lost save response can be replayed for that exact revision and choice. Never
            // reinterpret a stale approval of a different note or differently bound inputs.
            if (!same || expectedRevision != row.PlanRevision - 1) Revision(row, expectedRevision);
            if (guardDirectory != null)
            {
                if (state.RegisteredDownload is {} borrowed) await guardDirectory(borrowed.Directory);
                else foreach (var part in state.LocalFilesRecovery!.Preview.Parts) await guardDirectory(part.SourceDirectory);
            }
            if (!await CanAnalyzePreparedFilesAsync(row, ct)) throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
            if (!same)
            {
                var updated = plan with
                {
                    PublishRetainedFiles = false, AdaptiveFileProcessing = false, AdaptiveFileProcessingContext = null,
                    Parts = plan.Parts.Select(p => p with {Processing = new() {Requirement = requirement}, UseAdaptiveAssessment = false,
                        AppliedProcessingRevisionId = null, DeliveryProjectionFingerprint = null}).ToList()
                };
                await SavePreparedChoiceAsync(row, updated, reason, "PLAN_VERSION_CONFLICT", ct);
            }
            return (await GetAsync(row.Id, ct))!;
        }
        finally { gate.Release(); }
    }

    // A prepared revision can already own all inputs through older runs even though it has
    // no run of its own. A new task or a plan with an outstanding acquisition cannot enter here.
    internal static async Task<IReadOnlyList<(string PartId, ArtifactRecord Artifact)>> RegisteredPreparedInputsAsync(
        BakabaseDbContext context, IArtifactOperationStore store, AcquisitionTaskDbModel row, CancellationToken ct)
    {
        if (row.WorkflowRunId != null || row.Status != AcquisitionStatus.Waiting || row.DeletedAt != null ||
            row.Phase != "ready" || row.PlanJson == null || row.BuilderId == null || State(row).DirectoryChange != null) return [];
        var plan = Plan(row);
        if (plan.Parts is not {Count: > 0} || plan.Parts.Any(p => p?.ContinueFrom == null) ||
            !AcquisitionLocalFilesRecoveryService.LocalOnly(plan)) return [];
        var id = row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var revision = row.PlanRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (await context.Set<WorkflowRunSourceDbModel>().AnyAsync(s => s.Module == "acquisition" && s.EntityKind == "task" &&
                s.EntityId == id && s.Revision == revision, ct)) return [];
        var inputs = new List<(string, ArtifactRecord)>(plan.Parts.Count);
        foreach (var part in plan.Parts)
        {
            var input = await AcquisitionPlanArtifactInputGuard.RequireAsync(context, store, row.Id, part.Id, part.ContinueFrom!, ct);
            if (input.Manifest.Entries.Count == 0) return [];
            inputs.Add((part.Id, input));
        }
        return inputs;
    }

    internal async Task<bool> CanAnalyzePreparedFilesAsync(AcquisitionTaskDbModel row, CancellationToken ct)
    {
        if (artifacts == null) return false;
        var inputs = await RegisteredPreparedInputsAsync(db, artifacts, row, ct);
        if (inputs.Count == 0) return false;
        foreach (var (_, input) in inputs)
        {
            storage.EnsurePathAllowed(input.RootDirectory);
            if (input.Kind == AcquisitionCurrentDirectoryFiles.Kind)
                await AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(input, workspaces, ct);
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
            var updated = plan with
            {
                PublishRetainedFiles = false, AdaptiveFileProcessing = true, AdaptiveFileProcessingMaxIterations = maxIterations,
                Parts = plan.Parts.Select(p => p with {Processing = new() {Requirement = "unknown"}, UseAdaptiveAssessment = true,
                    AppliedProcessingRevisionId = null, DeliveryProjectionFingerprint = null}).ToList()
            };
            await SavePreparedChoiceAsync(row, updated, "analysis.preparedLocalFiles", "ADAPTIVE_MIGRATION_CHANGED", ct);
        }
        // This action offers no reserve override. Keep the configured reserve even
        // when a global temporary emergency mode was selected for other work.
        return await RunWhileLockedAsync(row.Id, row.PlanRevision, null, ct, useEmergencySpaceReserve: false);
    }

    private async Task SavePreparedChoiceAsync(AcquisitionTaskDbModel row, AcquisitionPlan updated, string reason,
        string conflictCode, CancellationToken ct)
    {
        var expected = row.PlanRevision;
        var previousPlan = row.PlanJson;
        var previousState = row.BuilderStateJson;
        SavePlan(row, updated, Sources(row), reason);
        var changed = await Tasks.Where(t => t.Id == row.Id && t.DeletedAt == null && t.WorkflowRunId == null &&
                t.Status == AcquisitionStatus.Waiting && t.Phase == "ready" && t.PlanRevision == expected &&
                t.PlanJson == previousPlan && t.BuilderStateJson == previousState)
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.PlanRevision, row.PlanRevision)
                .SetProperty(t => t.PlanJson, row.PlanJson).SetProperty(t => t.PlanHistoryJson, row.PlanHistoryJson)
                .SetProperty(t => t.BuilderStateJson, row.BuilderStateJson).SetProperty(t => t.UpdatedAt, row.UpdatedAt), ct);
        if (changed != 1) throw new InvalidOperationException(conflictCode);
    }
}
