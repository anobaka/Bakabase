using System;
using System.Linq;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>A publication storage wait may lend its completed processing input to an explicit
/// local revision. It never authorizes answering an unrelated wait or replaying network work.</summary>
internal static class AcquisitionPublicationStorageWait
{
    // A terminal publisher can lend only its exact processed input, even when it failed
    // before recording a wait prompt. Unexecuted processing or unknown later nodes make
    // a direct delivery unsafe.
    internal static bool CanRetryDelivery(WorkflowRunDbModel run)
    {
        if (run.Status == WorkflowRunStatus.Waiting && !Matches(run)) return false;
        if (run.Status is not (WorkflowRunStatus.Waiting or WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted or
            WorkflowRunStatus.Cancelled) || run.PendingSignalJson != null || run.CurrentStepIndex is not >= 0)
            return false;
        try
        {
            var plan = WorkflowRunPlans.Read(run.PlanSnapshotJson);
            var step = run.CurrentStepIndex.Value;
            var node = plan?.Nodes.ElementAtOrDefault(step);
            return plan?.TriggerKind == CloudWorkflow.Trigger && node?.Kind == "fs.publishArtifacts" &&
                plan.Nodes.Skip(step + 1).All(n => n.Kind == "download.cleanupArtifacts") &&
                JsonSerializer.Deserialize<FileArtifactPublicationConfig>(node.ConfigJson, WorkflowJson.Options)
                    is {Source: "processed", ReusePolicy: "reuseVerified"};
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or System.IO.InvalidDataException)
        { return false; }
    }

    internal static bool Matches(WorkflowRunDbModel run)
    {
        if (run.Status != WorkflowRunStatus.Waiting || run.PendingSignalJson != null ||
            run.WaitReason != "artifacts.publication" || run.CurrentStepIndex is not { } step || step < 0 ||
            run.WaitPromptJson is not {Length: > 0 and <= 1024 * 1024}) return false;
        try
        {
            var plan = WorkflowRunPlans.Read(run.PlanSnapshotJson);
            var node = plan?.Nodes.ElementAtOrDefault(step);
            if (plan?.TriggerKind != CloudWorkflow.Trigger || node?.Kind != "fs.publishArtifacts" ||
                plan.Nodes.Skip(step + 1).Any(n => n.Kind is not
                    (AcquisitionPlanProcessingActivity.ActivityKind or AssessFileArtifactDeliveryActivity.ActivityKind or
                        "fs.publishArtifacts" or "download.cleanupArtifacts")))
                return false;
            var publication = JsonSerializer.Deserialize<FileArtifactPublicationConfig>(node.ConfigJson, WorkflowJson.Options);
            if (publication?.Source != "processed" || publication.ReusePolicy != "reuseVerified") return false;
            using var prompt = JsonDocument.Parse(run.WaitPromptJson);
            if (prompt.RootElement.ValueKind != JsonValueKind.Object ||
                !prompt.RootElement.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String ||
                !prompt.RootElement.TryGetProperty("targetId", out var target) || target.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(target.GetString())) return false;
            var code = reason.GetString();
            if (code != "StorageUnavailable" && (code == null || LocalStorageFailures.PersistedCode(code) != code)) return false;
            var waiting = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson)?.Targets
                .Where(t => t.Id == target.GetString() && !t.Excluded).ToArray();
            return waiting is {Length: 1} && (waiting[0].PublicationWaitReason == "StorageUnavailable" ||
                waiting[0].PublicationWaitReason == code);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or System.IO.InvalidDataException)
        { return false; }
    }
}
