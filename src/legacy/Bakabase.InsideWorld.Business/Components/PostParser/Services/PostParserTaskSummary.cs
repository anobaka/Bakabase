using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Services;

internal static class PostParserTaskSummary
{
    public static TaskSummary Create(IEnumerable<PostParserTask> tasks, PostParserSource? source = null,
        IReadOnlySet<string>? activeTasks = null)
    {
        var summary = new TaskSummary();
        foreach (var task in tasks.Where(t => !t.IsDeleted && (!source.HasValue || t.Source == source.Value)))
        {
            summary.Total++;
            if (task.WorkflowRunId is { } runId && activeTasks?.Contains($"workflow.run.{runId}") == true)
                continue;
            var downloadInfo = Result(task, PostParseTarget.DownloadInfo).Data;
            var group = Classify(task.WorkflowRunId, task.WorkflowStatus, task.ParsingState, IsFailed(task),
                task.ContentSnapshot != null && Bakabase.Modules.PostParser.Services.PostContentReadinessPolicy.HasBlockingLocks(task.ContentSnapshot) ||
                (downloadInfo?["isComplete"] ?? downloadInfo?["IsComplete"]) is JsonValue complete && complete.TryGetValue<bool>(out var isComplete) && !isComplete,
                (downloadInfo?["resources"] ?? downloadInfo?["Resources"]) is JsonArray,
                task.Targets.Count > 0 && task.Targets.All(target => Result(task, target).Data != null));
            if (group == "failed") summary.Failed++;
            else if (group == "success") summary.Completed++;
        }
        return summary;
    }

    // A single precedence order for the paged query, global summary and legacy full-row summary.
    // Live BTask state is overlaid by callers before these persisted-state decisions.
    internal static string Classify(int? runId, WorkflowRunStatus? status, string? parsingState,
        bool failed, bool partial, bool hasResources, bool allTargetsHaveData)
    {
        if (status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running or WorkflowRunStatus.Interrupted) return "ready";
        if (failed) return "failed";
        if (parsingState is "awaitingAi" or "awaitingPurchase" or "possiblyExpired" or "partial" || partial ||
            status == WorkflowRunStatus.Waiting) return "attention";
        if (parsingState == "snapshotSaved") return "ready";
        if (status == WorkflowRunStatus.Success || status == null && parsingState == "complete" && hasResources ||
            runId == null && (parsingState == "complete" || allTargetsHaveData)) return "success";
        return "ready";
    }

    // Shared with the confirmed batch action. Reconcile the current workflow first, so a stale
    // error from a previous attempt cannot reparse an already successful replacement run.
    internal static bool IsFailed(PostParserTask task) => !task.IsDeleted &&
        task.WorkflowStatus is not (WorkflowRunStatus.Pending or WorkflowRunStatus.Running or WorkflowRunStatus.Interrupted) &&
        (!string.IsNullOrEmpty(task.Error) ||
         task.WorkflowStatus is WorkflowRunStatus.Failed or WorkflowRunStatus.Cancelled ||
         task.Targets.Any(target => Result(task, target).Error));

    // The parser page accepts both legacy data/error wrappers and current direct result objects.
    private static (JsonObject? Data, bool Error) Result(PostParserTask task, PostParseTarget target)
    {
        if (task.Results?.GetValueOrDefault(target) is not JsonObject result) return (null, false);
        if (result.ContainsKey("data") || result.ContainsKey("error") || result.ContainsKey("parsedAt"))
            return (result["data"] as JsonObject,
                result["error"] is JsonValue error && error.TryGetValue<string>(out var message) && !string.IsNullOrEmpty(message));
        return (result, false);
    }
}
