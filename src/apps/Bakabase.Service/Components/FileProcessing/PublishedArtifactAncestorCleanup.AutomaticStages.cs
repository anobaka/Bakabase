using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class PublishedArtifactAncestorCleanup
{
    // Moving completed processing into its current stage directory leaves the old
    // executor's private snapshots behind. Reclaim the whole chain while every stage
    // result still exists, before descendant-first retirement removes those verifiers.
    internal async Task<IReadOnlyList<ArtifactWorkspaceCleanupResult>> PrepareAutomaticPrivateCleanupAsync(
        string publicationId, IReadOnlyList<int> owners, CancellationToken ct)
    {
        if (maintenance == null) return [];
        var publication = await store.GetArtifactAsync(publicationId, ct);
        if (publication is not {Kind: "publishedFiles", State: ArtifactLocationState.Available})
            return [new(publicationId, false, "ResultNotVerified")];
        var currentOwners = await PublicationOwnersAsync(publicationId, ct);
        if (owners.Count == 0 || !owners.ToHashSet().SetEquals(currentOwners))
            return [new(publicationId, false, "UnboundArtifact")];
        if (await db.WorkflowRuns.AnyAsync(r => owners.Contains(r.Id) &&
                (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running ||
                 r.Status == WorkflowRunStatus.Waiting || r.PendingSignalJson != null), ct))
            return [new(publicationId, false, "ActiveOwner")];
        var graph = await AncestorsAsync(publication, ct);
        var results = new List<ArtifactWorkspaceCleanupResult>();
        foreach (var artifact in DescendantsFirst(graph.Values.Where(a => a.Id != publication.Id && a.Managed &&
                     a.Kind == "processedFiles" && a.State == ArtifactLocationState.Available)))
        {
            ct.ThrowIfCancellationRequested();
            results.Add((await maintenance.ReclaimAsync([artifact.Id], ct)).Single());
        }
        if (results.All(PrivateCleanupReady)) await ReleaseAutomaticStageViewsAsync(publicationId, owners, ct);
        return results;
    }

    private static bool PrivateCleanupReady(ArtifactWorkspaceCleanupResult outcome) => outcome.Completed ||
        outcome.Reason is "NotManagedProcessedArtifact" or "RetentionPolicyKeep";

    internal async Task<ArtifactWorkspaceCleanupResult> ReclaimAutomaticPreparedAsync(ArtifactWorkspaceCleanupPreview expected,
        IReadOnlyList<int> owners, IReadOnlyList<ArtifactWorkspaceCleanupResult> prepared, CancellationToken ct)
    {
        if (prepared.FirstOrDefault(r => !PrivateCleanupReady(r)) is {} failed)
            return new(expected.ArtifactId, false, failed.Reason);
        // The prepared results only order work. Registered retirement revalidates final
        // bytes, lineage, ownership and current references through its existing path.
        return await ReclaimAsync(expected, owners, ct);
    }
}
