using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record ReviewedCheckpointPart(string TargetId, string DownloadId, string DownloadOperationId,
    string ResultId, string ResultOperationId, string ResultFingerprint);
internal sealed record ReviewedCheckpointWait(int RunId, string Identity, IReadOnlyList<ReviewedCheckpointPart> Parts);
internal sealed record ReviewedCheckpointSelection(ReviewedCheckpointWait Wait, IReadOnlyList<ReviewedProcessingBoundary> Sources);

public sealed partial class AcquisitionPlanService
{
    private async Task<ReviewedCheckpointWait?> ReadCheckpointReviewAsync(AcquisitionTaskDbModel? task, CancellationToken ct)
    {
        if (task == null || task.DeletedAt != null || task.WorkflowRunId is not {} id) return null;
        var run = await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
        if (run == null || run.Status != WorkflowRunStatus.Waiting || run.WaitReason != "cloud.fileProcessing" ||
            run.PendingSignalJson != null || run.CurrentStepIndex is not >= 0 || run.WaitPromptJson == null ||
            !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, id, task.Id, ct)) return null;
        try
        {
            var nodes = WorkflowRunPlans.Read(run.PlanSnapshotJson)?.Nodes;
            var index = run.CurrentStepIndex.Value;
            if (nodes == null || index != nodes.Count - 2 ||
                nodes[index].Kind is not (AcquisitionPlanProcessingActivity.ActivityKind or AssessFileArtifactDeliveryActivity.ActivityKind) ||
                nodes[index + 1].Kind != "fs.publishArtifacts") return null;
            var publication = JsonSerializer.Deserialize<FileArtifactPublicationConfig>(nodes[index + 1].ConfigJson, JsonSerializerOptions.Web);
            if (publication?.Source != "processed") return null;
            using var prompt = JsonDocument.Parse(run.WaitPromptJson);
            using var config = JsonDocument.Parse(nodes[index].ConfigJson);
            string? Field(string field) => prompt.RootElement.TryGetProperty(field, out var value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var focus = Field("targetId");
            if (!prompt.RootElement.TryGetProperty("deliveryReview", out var marker) || marker.ValueKind != JsonValueKind.True ||
                Field("reason") != "FILE_DELIVERY_RESULT_UNCERTAIN" || string.IsNullOrWhiteSpace(Field("reviewToken")) ||
                string.IsNullOrWhiteSpace(focus) || !config.RootElement.TryGetProperty("targetId", out var configured) ||
                configured.GetString() != focus) return null;
            var targets = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson)?.Targets;
            if (targets == null || targets.Count == 0 || targets.Any(t => t.Excluded || t.PublishedArtifactId != null ||
                t.PublicationOperationId != null || string.IsNullOrWhiteSpace(t.DownloadArtifactId) ||
                string.IsNullOrWhiteSpace(t.DownloadOperationId) || string.IsNullOrWhiteSpace(t.ProcessedArtifactId) ||
                string.IsNullOrWhiteSpace(t.ProcessingOperationId) || t.DeliveryReview?.ArtifactId != t.ProcessedArtifactId ||
                t.DeliveryReview?.Selection != null || string.IsNullOrWhiteSpace(t.DeliveryReview?.Fingerprint) ||
                (t.Id == focus ? t.DeliveryReview!.Accepted || t.DeliveryReview.WaitReason != Field("reason") ||
                    AssessFileArtifactDeliveryActivity.ReviewToken(t.Id, t.DeliveryReview) != Field("reviewToken") ||
                    t.ProcessedArtifactId != Field("artifactId") || t.DeliveryReview.Fingerprint != Field("fingerprint") :
                    !t.DeliveryReview!.Accepted))) return null;
            var parts = targets.Select(t => new ReviewedCheckpointPart(t.Id, t.DownloadArtifactId!, t.DownloadOperationId!,
                t.ProcessedArtifactId!, t.ProcessingOperationId!, t.DeliveryReview!.Fingerprint)).ToArray();
            if (parts.Count(t => t.TargetId == focus) != 1 || parts.Select(t => t.TargetId).Distinct().Count() != parts.Length ||
                parts.SelectMany(t => new[] {t.ResultId, t.DownloadId}).Distinct().Count() != parts.Length * 2) return null;
            var identity = ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new {run.Id, index, run.PlanSnapshotJson,
                run.WaitPromptJson, run.CurrentItemJson, TaskVersion = StorageVersion(task)}));
            return new(id, identity, parts);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or InvalidDataException)
        { return null; }
    }
}
