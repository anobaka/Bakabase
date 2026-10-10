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
            // Match the parser page: an active/restarted workflow takes precedence over saved results.
            if (task.WorkflowRunId is { } runId && activeTasks?.Contains($"workflow.run.{runId}") == true ||
                task.WorkflowStatus is WorkflowRunStatus.Pending or WorkflowRunStatus.Running or WorkflowRunStatus.Interrupted)
                continue;

            if (!string.IsNullOrEmpty(task.Error) ||
                task.WorkflowStatus is WorkflowRunStatus.Failed or WorkflowRunStatus.Cancelled ||
                task.Targets.Any(target => Result(task, target).Error))
            {
                summary.Failed++;
                continue;
            }

            var downloadInfo = Result(task, PostParseTarget.DownloadInfo).Data;
            if (task.ParsingState is "awaitingAi" or "awaitingPurchase" or "possiblyExpired" or "partial" or "snapshotSaved" ||
                task.ContentSnapshot?.Locks.Any(l => !l.IsBought) == true ||
                downloadInfo?["isComplete"] is JsonValue complete && complete.TryGetValue<bool>(out var isComplete) && !isComplete ||
                task.WorkflowStatus == WorkflowRunStatus.Waiting)
                continue;

            if (task.WorkflowStatus == WorkflowRunStatus.Success ||
                task.WorkflowRunId == null && (task.ParsingState == "complete" ||
                    task.Targets.Count > 0 && task.Targets.All(target => Result(task, target).Data != null)))
                summary.Completed++;
        }
        return summary;
    }

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
