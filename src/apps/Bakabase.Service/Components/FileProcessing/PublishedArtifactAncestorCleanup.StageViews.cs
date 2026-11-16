using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class PublishedArtifactAncestorCleanup
{
    private async Task<IReadOnlyList<string>> ReadReleasedStageViewsAsync(ArtifactRecord publication,
        IReadOnlyDictionary<string, ArtifactRecord> graph, IReadOnlyList<int> owners, CancellationToken ct)
    {
        if (workspaces == null || owners.Count == 0) return [];
        var views = graph.Values.Where(a => !a.Managed && a.Kind == "processedFiles" &&
            a.State == ArtifactLocationState.Retired && a.MetadataJson != null).ToArray();
        var owned = await AcquisitionTaskCleanupViews.OwnedClosuresAsync(db, views.Select(v => v.Id).ToArray(),
            owners.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray(), ct);
        var result = new List<string>();
        foreach (var view in views)
        {
            if (!owned.Contains(view.Id)) continue;
            FileArtifactProcessingMetadata? metadata;
            try { metadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(view.MetadataJson!, Json); }
            catch (JsonException) { continue; }
            if (metadata?.StageCommit is {} receipt &&
                await AcquisitionProcessingStageCommit.IsOwnedViewAsync(db, store, workspaces, view, receipt.TaskId, owners, ct) &&
                (await ArtifactFileCoverage.EvaluateAsync(store, view, [publication.Id], ct)).Complete) result.Add(view.Id);
        }
        return result;
    }

    // A mixed stage output is a read-only view of owned outputs and borrowed files.
    // Retiring this reference after verified delivery never deletes or adopts those files.
    private async Task ReleaseAutomaticStageViewsAsync(string publicationId, IReadOnlyList<int> owners, CancellationToken ct)
    {
        if (workspaces == null || owners.Count == 0) return;
        await using var fence = await store.AcquireMaintenanceAsync(ct);
        if (!owners.ToHashSet().SetEquals(await PublicationOwnersAsync(publicationId, ct)) ||
            await db.WorkflowRuns.AnyAsync(r => owners.Contains(r.Id) &&
                (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running ||
                 r.Status == WorkflowRunStatus.Waiting || r.PendingSignalJson != null), ct)) return;
        var publication = await store.GetArtifactAsync(publicationId, ct);
        if (publication is not {Kind: "publishedFiles", State: ArtifactLocationState.Available} ||
            await store.GetOperationAsync(publication.OperationId, ct) is not
                {Kind: "files.publish", State: ArtifactOperationState.Completed} operation ||
            operation.OutputArtifactId != publication.Id ||
            await store.ResolveByIdAsync(publication.Id, ct) is not {} verified ||
            verified.Fingerprint != publication.Fingerprint) return;
        var graph = await AncestorsAsync(publication, ct);
        var views = graph.Values.Where(a => !a.Managed && a.Kind == "processedFiles" &&
            a.State == ArtifactLocationState.Available && a.MetadataJson != null).ToArray();
        var owned = await AcquisitionTaskCleanupViews.OwnedClosuresAsync(db, views.Select(v => v.Id).ToArray(),
            owners.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray(), ct);
        foreach (var view in views)
        {
            ct.ThrowIfCancellationRequested();
            if (!owned.Contains(view.Id)) continue;
            FileArtifactProcessingMetadata? metadata;
            try { metadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(view.MetadataJson!, Json); }
            catch (JsonException) { continue; }
            if (metadata?.StageCommit is not {} receipt ||
                !await AcquisitionProcessingStageCommit.IsOwnedViewAsync(db, store, workspaces, view, receipt.TaskId, owners, ct) ||
                !(await ArtifactFileCoverage.EvaluateAsync(store, view, [publication.Id], ct)).Complete) continue;
            await db.Artifacts.Where(a => a.Id == view.Id && !a.Managed && a.State == ArtifactLocationState.Available &&
                    a.OperationId == view.OperationId && a.Fingerprint == view.Fingerprint && a.MetadataJson == view.MetadataJson)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ArtifactLocationState.Retired)
                    .SetProperty(a => a.UpdatedAt, DateTime.UtcNow), ct);
        }
    }
}
