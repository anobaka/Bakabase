using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    // A local continuation already names its exact completed input. If it stopped
    // before its first processing hand-off, its directory can be displayed without
    // adopting another folder or inventing an exclusive ownership claim. GET never
    // saves this projection; the ordinary new processing revision retains it.
    private async Task<AcquisitionStageDirectories> ProjectInitialDownloadDirectoryAsync(AcquisitionTaskDbModel row,
        AcquisitionPlan plan, AcquisitionStageDirectories mapping, WorkflowRunDbModel? run, CancellationToken ct)
    {
        if (artifacts == null || workspaces == null || row.DeletedAt != null || row.Status == AcquisitionStatus.Completed ||
            run == null || run.Status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running or WorkflowRunStatus.Success ||
            run.PendingSignalJson != null) return mapping;
        var parts = mapping.Parts.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var current = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson);
        foreach (var part in plan.Parts)
        {
            if (!parts.TryGetValue(part.Id, out var directories) || directories.Download != null || directories.Processing != null ||
                part.ContinueFrom is not {} input || input.TaskId != row.Id) continue;
            var targets = current?.Targets.Where(t => t.Id == part.Id).ToArray();
            if (targets is {Length: > 1}) continue;
            var target = targets?.SingleOrDefault();
            if (target != null && (target.Excluded || target.ProcessingStepArtifacts.Count > 0 ||
                target.PublishedArtifactId != null || target.PublicationOperationId != null || target.PublishedFiles.Count > 0 ||
                target.ProcessedArtifactId != null && target.ProcessedArtifactId != input.ArtifactId ||
                target.DownloadArtifactId != null && target.DownloadArtifactId != input.ArtifactId ||
                target.DownloadOperationId != null && target.DownloadOperationId != input.OperationId)) continue;
            var owner = run.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var prefix = part.Id + ":";
            var operations = db.ArtifactOperationBindings.Where(b => b.OwnerKind == "workflow" && b.OwnerId == owner &&
                b.Slot.StartsWith(prefix)).Select(b => b.OperationId);
            if (await db.ArtifactOperations.AnyAsync(o => operations.Contains(o.Id) && o.State == ArtifactOperationState.Completed &&
                (o.Kind == "files.process" || o.Kind == "files.publish"), ct)) continue;
            var artifact = await artifacts.GetArtifactAsync(input.ArtifactId, ct);
            if (artifact is not {Kind: "download"} || artifact.OperationId != input.OperationId || artifact.Fingerprint != input.Fingerprint ||
                !await AcquisitionCurrentDirectoryFiles.CanReadAsync(db, artifacts, storage, workspaces, row.Id,
                    input.PreviousRunId, part.Id, artifact, ct, activeRunId: run.Id)) continue;
            // The artifact root is the input container; never infer its parent from a
            // native downloader name, a common prefix or an old configured storage root.
            parts[part.Id] = directories with {Download = new(Path.GetFullPath(artifact.RootDirectory), "borrowed", "downloadRecord")};
        }
        return mapping with {Parts = parts};
    }
}
