using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Carry layout evidence from an owned artifact at the same stage root.
/// A matching directory name alone never grants permission to collapse it.</summary>
internal static class AcquisitionStageDirectoryProvenance
{
    internal sealed record Source(ArtifactManifest Manifest, ArtifactDirectoryProvenance Provenance);

    internal static async Task<IReadOnlyList<Source>> ReadAsync(BakabaseDbContext db, IArtifactOperationStore store,
        int taskId, string partId, string directory, CancellationToken ct)
    {
        var task = taskId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var runs = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.Module == "acquisition" &&
            s.EntityKind == "task" && s.EntityId == task).Select(s => s.WorkflowRunId).Distinct().ToArrayAsync(ct);
        var owners = runs.Select(r => r.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var candidates = await (from artifact in db.Artifacts.AsNoTracking()
            join binding in db.ArtifactOperationBindings.AsNoTracking() on artifact.OperationId equals binding.OperationId
            where artifact.State == ArtifactLocationState.Available && artifact.MetadataJson != null &&
                binding.OwnerKind == "workflow" && owners.Contains(binding.OwnerId) && binding.Slot.StartsWith(partId + ":")
            orderby artifact.CreatedAt descending
            select new {artifact.Id, binding.OwnerId}).Distinct().Take(256).ToArrayAsync(ct);
        var result = new List<Source>();
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var artifact = await store.GetArtifactAsync(candidate.Id, ct);
            if (artifact == null || !ArtifactManifestFiles.PathComparer.Equals(artifact.RootDirectory, directory) ||
                !Directory.Exists(artifact.RootDirectory) || !int.TryParse(candidate.OwnerId, out var runId)) continue;
            var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == artifact.OperationId).ToArrayAsync(ct);
            if (bindings.Any(b => b.OwnerKind != "workflow" || !owners.Contains(b.OwnerId) ||
                !b.Slot.StartsWith(partId + ":", StringComparison.Ordinal))) continue;
            var input = new AcquisitionPlanArtifactInput(taskId, runId, artifact.Id,
                artifact.OperationId, artifact.Fingerprint);
            var proof = await AcquisitionCurrentDirectoryProvenance.ReadAsync(db, store, taskId, partId, input, ct);
            if (proof != null) result.Add(new(artifact.Manifest, proof));
        }
        return result;
    }

    internal static ArtifactDirectoryProvenance? Project(IReadOnlyList<Source> sources, ArtifactManifest current)
    {
        var entries = sources.SelectMany(source => AcquisitionCurrentDirectoryProvenance.Project(
            source.Manifest, source.Provenance, current)?.Entries ?? []).ToArray();
        // Bind intersects conflicting evidence. New/changed leaves remain without evidence,
        // so the delivery projector retains any generated directory containing those leaves.
        return entries.Length == 0 ? null : FileArtifactDeliveryLayout.Bind(current, entries);
    }
}
