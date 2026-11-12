using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal static partial class AcquisitionProcessingConflictWait
{
    // Early initial-plan prompts omitted artifactId although the download registration was
    // already committed into the current typed checkpoint. Only this unprocessed boundary
    // can recover that reference; later attempts may not roll back to their original download.
    internal static DownloadWorkset? InitialUnknownWorkset(WorkflowRunDbModel run)
    {
        if (run.Status != WorkflowRunStatus.Waiting || run.PendingSignalJson != null ||
            run.WaitReason != "cloud.fileProcessing" || run.CurrentStepIndex is not >= 0) return null;
        try
        {
            var node = WorkflowRunPlans.Read(run.PlanSnapshotJson)?.Nodes.ElementAtOrDefault(run.CurrentStepIndex.Value);
            if (node?.Kind != AcquisitionPlanProcessingActivity.ActivityKind) return null;
            var config = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(node.ConfigJson, WorkflowJson.Options);
            if (config == null || config.StopAfterStepId != null || config.ContinueFrom != null || config.UseVerifiedInput ||
                config.Plan is not {Requirement: "unknown", Steps.Count: 0}) return null;
            using var prompt = JsonDocument.Parse(run.WaitPromptJson ?? "{}");
            var root = prompt.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("reason", out var reason) || reason.GetString() != "ExtractionPlanUnknown" ||
                !root.TryGetProperty("targetId", out var targetId) || targetId.GetString() != config.TargetId ||
                root.TryGetProperty("artifactId", out var artifact) && artifact.ValueKind != JsonValueKind.Null)
                return null;
            var workset = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson);
            if (workset?.Targets is not {Count: > 0 and <= 100} ||
                workset.Targets.Select(t => t.Id).Distinct().Count() != workset.Targets.Count) return null;
            var current = workset.Targets.SingleOrDefault(t => t.Id == config.TargetId);
            if (current == null || !InitialInput(current) || current.ProcessingWaitReason != "ExtractionPlanUnknown" ||
                AcquisitionPlanProcessingActivity.EffectivePlan(config, current) is not {Requirement: "unknown", Steps.Count: 0}) return null;
            return workset;
        }
        catch (Exception error) when (error is JsonException or IOException or ArgumentException or InvalidOperationException)
        {return null;}
    }

    internal static bool MatchesInitialInput(DownloadWorkset workset, string partId, string artifactId, string operationId) =>
        workset.Targets.SingleOrDefault(t => t.Id == partId) is { } target && InitialInput(target) &&
        target.DownloadArtifactId == artifactId && target.DownloadOperationId == operationId;

    private static bool InitialInput(CloudDownloadTarget target) => !target.Excluded && target.SelectedSource != null &&
        target.AcquisitionNonce == null && !string.IsNullOrWhiteSpace(target.DownloadArtifactId) &&
        !string.IsNullOrWhiteSpace(target.DownloadOperationId) && target.Files is {Count: > 0} &&
        target.ProcessedArtifactId == null && target.ProcessingOperationId == null &&
        target.ProcessedFiles is {Count: 0} && target.ProcessingStepArtifacts is {Count: 0} &&
        target.PublishedArtifactId == null && target.PublicationOperationId == null &&
        target.PublishedFiles is {Count: 0} && target.DeliveryReview == null;
}
