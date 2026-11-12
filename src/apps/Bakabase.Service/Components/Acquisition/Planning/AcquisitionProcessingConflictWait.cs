using System;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>A server-recorded deterministic processing wait can lend its files to a revised plan.
/// The original run keeps its wait and immutable history after the task moves to that revision.</summary>
internal static partial class AcquisitionProcessingConflictWait
{
    private static readonly string[] LocalContinuationReasons =
    [
        "ProcessingPathConflict", "ExtractionPlanUnknown", "PasswordUnknown", "ArchiveInputRequired",
        "ProcessingInputNotMatched", "RepeatedContent", "ExtractionLayoutConflict", "FILE_DELIVERY_RESULT_UNCERTAIN",
        "FILE_DELIVERY_ARCHIVE_REVIEW_REQUIRED"
    ];

    public static bool Matches(WorkflowRunDbModel run)
    {
        if (run.Status != WorkflowRunStatus.Waiting || run.PendingSignalJson != null ||
            run.WaitReason != "cloud.fileProcessing" || run.CurrentStepIndex is not { } step || step < 0)
            return false;
        try
        {
            if (WorkflowRunPlans.Read(run.PlanSnapshotJson)?.Nodes.ElementAtOrDefault(step)?.Kind !=
                AcquisitionPlanProcessingActivity.ActivityKind) return false;
            using var prompt = JsonDocument.Parse(run.WaitPromptJson ?? "{}");
            return prompt.RootElement.ValueKind == JsonValueKind.Object &&
                   prompt.RootElement.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String &&
                   reason.GetString() == "ProcessingPathConflict";
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        { return false; }
    }

    /// <summary>
    /// A stopped file-processing or delivery-assessment node may lend its latest
    /// verified artifact to a local continuation. This deliberately does not match
    /// download, transfer or source-selection nodes, so a continuation can never
    /// replay network work merely because a task is waiting.
    /// </summary>
    public static bool MatchesForLocalContinuation(WorkflowRunDbModel run)
    {
        // Early deterministic path-conflict prompts only recorded the reason.
        // Ownership and the unchanged manifest are still verified when borrowing an input.
        if (Matches(run) || InitialUnknownWorkset(run) != null) return true;
        if (run.Status != WorkflowRunStatus.Waiting || run.PendingSignalJson != null ||
            run.WaitReason != "cloud.fileProcessing" || run.CurrentStepIndex is not { } step || step < 0)
            return false;
        try
        {
            var node = WorkflowRunPlans.Read(run.PlanSnapshotJson)?.Nodes.ElementAtOrDefault(step);
            if (node?.Kind is not (AcquisitionPlanProcessingActivity.ActivityKind or
                Bakabase.Service.Components.FileProcessing.AssessFileArtifactDeliveryActivity.ActivityKind)) return false;
            using var prompt = JsonDocument.Parse(run.WaitPromptJson ?? "{}");
            var root = prompt.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("artifactId", out var artifact) || artifact.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(artifact.GetString())) return false;
            if (!root.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String)
                return false;
            return LocalContinuationReasons.Contains(reason.GetString(), StringComparer.Ordinal);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        { return false; }
    }
    // Fresh directory capture is a local file operation. Its eligibility follows the
    // stopped activity boundary rather than a provider-specific error vocabulary.
    internal static bool MatchesCurrentDirectoryWait(WorkflowRunDbModel run)
    {
        if (run.Status != WorkflowRunStatus.Waiting || run.PendingSignalJson != null ||
            run.WaitReason != "cloud.fileProcessing" || run.CurrentStepIndex is not >= 0) return false;
        try
        {
            var node = WorkflowRunPlans.Read(run.PlanSnapshotJson)?.Nodes.ElementAtOrDefault(run.CurrentStepIndex.Value);
            if (node?.Kind is not (AcquisitionPlanProcessingActivity.ActivityKind or
                Bakabase.Service.Components.FileProcessing.AssessFileArtifactDeliveryActivity.ActivityKind)) return false;
            using var config = JsonDocument.Parse(node.ConfigJson);
            var target = config.RootElement.GetProperty("targetId").GetString();
            using var prompt = JsonDocument.Parse(run.WaitPromptJson ?? "{}");
            if (prompt.RootElement.TryGetProperty("targetId", out var promptedTarget) &&
                (promptedTarget.ValueKind != JsonValueKind.String || promptedTarget.GetString() != target)) return false;
            var workset = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson);
            return !string.IsNullOrWhiteSpace(target) && workset?.Targets is {Count: > 0 and <= 100} &&
                workset.Targets.Select(t => t.Id).Distinct().Count() == workset.Targets.Count &&
                workset.Targets.Count(t => t.Id == target && !t.Excluded) == 1;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        { return false; }
    }

    internal static bool MatchesCurrentInput(WorkflowRunDbModel run, string partId, string artifactId, string operationId)
    {
        if (!MatchesCurrentDirectoryWait(run)) return false;
        var target = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson)?.Targets.SingleOrDefault(t => t.Id == partId && !t.Excluded);
        if (target == null) return false;
        return target.ProcessedArtifactId != null
            ? target.ProcessedArtifactId == artifactId && target.ProcessingOperationId == operationId
            : target.DownloadArtifactId == artifactId && target.DownloadOperationId == operationId;
    }

}
