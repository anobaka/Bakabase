using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Completion compensation, not an exception to cleanup ownership. Only an exact
/// later revision with every required publication verified may retire its obsolete waits.</summary>
public sealed class AcquisitionVerifiedDeliveryRunRetirement(BakabaseDbContext db, IArtifactOperationStore artifacts,
    IAcquisitionDeletionActivityReader activity, AcquisitionSupersededRunCleanup retirement) : IArtifactCleanupOwnerPreparation
{
    public async Task<ArtifactCleanupOwnerPreparationResult?> PrepareAsync(string publicationId, CancellationToken ct)
    {
        var publication = await artifacts.GetArtifactAsync(publicationId, ct);
        if (publication?.Kind != "publishedFiles") return null;
        var direct = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == publication.OperationId)
            .ToArrayAsync(ct);
        if (direct.Length == 0 || direct.Any(b => b.OwnerKind != "workflow" || !int.TryParse(b.OwnerId, out _))) return null;
        var directIds = direct.Select(b => int.Parse(b.OwnerId, CultureInfo.InvariantCulture)).Distinct().ToArray();
        // Deleted historical owners remain evidence of sharing, even though their rows are hidden.
        var tasks = await db.AcquisitionTasks.AsNoTracking().Where(t => t.WorkflowRunId != null && directIds.Contains(t.WorkflowRunId.Value)).ToArrayAsync(ct);
        if (tasks.Length == 0) return null;
        if (tasks.Length != 1 || tasks[0].DeletedAt != null || tasks[0].WorkflowRunId is not { } completed ||
            !directIds.Contains(completed))
            return Block("SharedOwner");
        var taskGate = AcquisitionTaskGate.For(tasks[0].Id);
        if (!await taskGate.WaitAsync(0, ct)) return Block("ActiveOwner", retry: true);
        var runGates = new List<SemaphoreSlim>();
        try
        {
            var task = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == tasks[0].Id, ct);
            if (task.DeletedAt != null || task.WorkflowRunId != completed || task.Status != AcquisitionStatus.Completed)
                return Block("ActiveOwner", retry: true);
            var live = (await activity.ReadAsync([task], ct)).GetValueOrDefault(task.Id);
            if (live == null) return Block("ACQUISITION_EXECUTION_STATE_UNKNOWN");
            if (live.BlockReason != null) return Block(live.BlockReason, retry: true);
            var owners = live.RunIds.Distinct().Order().ToArray();
            if (!owners.Contains(completed) || directIds.Any(id => !owners.Contains(id))) return Block("UnboundArtifact");
            if (!await db.WorkflowRuns.AnyAsync(r => owners.Contains(r.Id) && r.Id != completed &&
                    (r.Status == WorkflowRunStatus.Waiting || r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running), ct))
                return new(true);
            foreach (var owner in owners)
            {
                var gate = WorkflowRunMutationGate.For(owner);
                if (!await gate.WaitAsync(0, ct)) return Block("ActiveOwner", retry: true);
                runGates.Add(gate);
            }
            var expected = await retirement.PreviewAsync(task, owners, ct);
            if (expected == null) return Block("CLEANUP_OLD_EXECUTION_REQUIRES_REVIEW");
            if (expected.Runs.Count == 0) return new(true);
            var outputs = await RequiredPublicationsAsync(db, artifacts, task, publicationId, ct);
            if (outputs == null) return Block("CLEANUP_DELIVERY_PROOF_MISSING");
            await using var fence = await artifacts.AcquireMaintenanceAsync(ct);
            var verified = new List<ArtifactAcceptedPublication>();
            foreach (var output in outputs)
            {
                var current = await artifacts.ResolveByIdAsync(output.Id, ct);
                if (current == null || current.Kind != "publishedFiles" || current.OperationId != output.OperationId ||
                    current.RootDirectory != output.RootDirectory || current.Fingerprint != output.Fingerprint ||
                    current.MetadataJson != output.MetadataJson || !FileDispositionProofs.SameManifest(current.Manifest, output.Manifest) ||
                    !current.ParentArtifactIds.SequenceEqual(output.ParentArtifactIds)) return Block("CLEANUP_DELIVERY_ACCEPTANCE_REQUIRED");
                verified.Add(new(current.Id, current.OperationId, current.Fingerprint, current.RootDirectory));
            }
            // Hashing can be slow: reread all bound slots and versions before the durable
            // retirement. The helper repeats activity, signals, history and sharing checks.
            var latestOutputs = await RequiredPublicationsAsync(db, artifacts, task, publicationId, ct);
            if (latestOutputs == null || !latestOutputs.Select(Identity).Order().SequenceEqual(outputs.Select(Identity).Order()) ||
                !await retirement.ApplyVerifiedDeliveryAsync(task, owners, expected, verified, ct))
                return Block("CLEANUP_OLD_EXECUTION_REQUIRES_REVIEW");
            return new(true);
        }
        finally
        {
            foreach (var gate in runGates) gate.Release();
            taskGate.Release();
        }
    }

    internal static async Task<IReadOnlyList<ArtifactRecord>?> RequiredPublicationsAsync(BakabaseDbContext db,
        IArtifactOperationStore artifacts, AcquisitionTaskDbModel task, string? publicationId, CancellationToken ct)
    {
        AcquisitionPlan? plan;
        try { plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson ?? "null", JsonSerializerOptions.Web); }
        catch (JsonException) { return null; }
        if (plan is not {Version: 1, Delivery: "files", Parts.Count: > 0 and <= 100} ||
            plan.Parts.Any(p => p == null || string.IsNullOrWhiteSpace(p.Id)) ||
            plan.Parts.Select(p => p.Id).Distinct().Count() != plan.Parts.Count || task.WorkflowRunId is not { } runId) return null;
        if (!await db.WorkflowRuns.AnyAsync(r => r.Id == runId && r.Status == WorkflowRunStatus.Success && r.FailedItemCount == 0, ct)) return null;
        var expected = plan.Parts.Select(p => p.Id + ":artifact.publish").ToHashSet(StringComparer.Ordinal);
        var owner = runId.ToString(CultureInfo.InvariantCulture);
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OwnerKind == "workflow" &&
            b.OwnerId == owner && b.Slot.EndsWith(":artifact.publish")).ToArrayAsync(ct);
        if (bindings.Length != expected.Count || !expected.SetEquals(bindings.Select(b => b.Slot))) return null;
        var results = new List<ArtifactRecord>();
        foreach (var binding in bindings)
        {
            var operation = await artifacts.GetOperationAsync(binding.OperationId, ct);
            if (operation is not {Kind: "files.publish", State: ArtifactOperationState.Completed, OutputArtifactId: not null}) return null;
            var output = await artifacts.GetArtifactAsync(operation.OutputArtifactId, ct);
            if (output?.Kind != "publishedFiles" || output.OperationId != operation.Id || output.State != ArtifactLocationState.Available) return null;
            results.Add(output);
        }
        return publicationId == null || results.Any(a => a.Id == publicationId) ? results.DistinctBy(a => a.Id).ToArray() : null;
    }

    private static string Identity(ArtifactRecord artifact) => JsonSerializer.Serialize(new
        {artifact.Id, artifact.OperationId, artifact.RootDirectory, artifact.Fingerprint, artifact.MetadataJson, artifact.ParentArtifactIds});
    private static ArtifactCleanupOwnerPreparationResult Block(string reason, bool retry = false) => new(false, reason, retry);
}
