using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Outbox intent and workflow success commit together. No file IO or cleanup runs on
/// the completion path; the serial lifecycle worker verifies the final artifact afterwards.</summary>
public sealed class PublishedArtifactCleanupCompletionObserver(BakabaseDbContext db) : IWorkflowRunCompletionObserver
{
    internal const string PendingKey = "publicationCleanupPending";
    internal const string ProgressKey = "publicationCleanup";
    public async Task StageAsync(WorkflowRunDbModel run, object payload, CancellationToken ct)
    {
        if (run.Status != WorkflowRunStatus.Success || run.FailedItemCount != 0) return;
        var owner = run.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var operations = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
            .Where(b => b.OwnerKind == "workflow" && b.OwnerId == owner).Select(b => b.OperationId).ToListAsync(ct);
        var publications = await db.Set<ArtifactDbModel>().Where(a => operations.Contains(a.OperationId) &&
            a.Kind == "publishedFiles" && a.State == ArtifactLocationState.Available).ToListAsync(ct);
        foreach (var publication in publications)
        {
            var metadata = JsonNode.Parse(publication.MetadataJson ?? "{}")!.AsObject();
            metadata[PendingKey] = true;
            metadata[ProgressKey] = new JsonObject { ["runId"] = run.Id, ["status"] = "pending",
                ["requestedAt"] = DateTime.UtcNow.ToString("O") };
            publication.MetadataJson = metadata.ToJsonString();
        }
    }
}
