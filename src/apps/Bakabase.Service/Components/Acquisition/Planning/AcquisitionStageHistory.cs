using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Extensions;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionStageAttempt(string Id, string Stage, int Attempt, int RunId, string Status,
    [property: Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))] DateTime? StartedAt,
    [property: Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))] DateTime? CompletedAt,
    IReadOnlyList<string> Nodes, IReadOnlyList<WorkflowOutputBlock> Outputs, bool OutputsTruncated, bool Legacy)
{
    public bool HasAssessmentCalls { get; init; }
    internal long? SavedAssessmentExecutionId { get; init; }
    internal IReadOnlyList<long> ExecutionIds { get; init; } = [];
}

/// <summary>Task-owned runs persist each node invocation before execution. The first invocation in a stage
/// attempt is its immutable identity. Reconstructing the task view never starts work or writes checkpoints.</summary>
internal static class AcquisitionStageHistory
{
    public static string? StageFor(string kind) => kind switch
    {
        "download.prepareSources" or "download.selectSources" or "baidu.transfer" or "download.enqueueSupported" or
        "download.awaitCompletion" or AcquisitionPlanContinueFilesActivity.ActivityKind or
        AcquisitionStepKinds.ResolveSharedContent or AcquisitionStepKinds.SelectLink or AcquisitionStepKinds.FetchHttp or
        AcquisitionStepKinds.FetchFromPlatform or AcquisitionStepKinds.FetchExHentai or AcquisitionStepKinds.FetchMagnet or
        AcquisitionStepKinds.FetchTorrent or AcquisitionStepKinds.FetchResultTorrent or AcquisitionStepKinds.WaitForInbox or
        AcquisitionStepKinds.PickLocalDirectory => "download",
        AcquisitionPlanProcessingActivity.ActivityKind or AssessFileArtifactDeliveryActivity.ActivityKind or
        "fs.processArtifacts" or AcquisitionStepKinds.Unpack => "processing",
        "fs.publishArtifacts" or "download.cleanupArtifacts" or AcquisitionStepKinds.Place or AcquisitionStepKinds.Materialize => "delivery",
        _ => null
    };

    internal static string? LocalInputStage(string? firstKind, string? nextKind) =>
        firstKind == AcquisitionPlanContinueFilesActivity.ActivityKind && nextKind != null ? StageFor(nextKind) : null;

    private sealed class RunBoundary
    {
        public int RunId { get; init; }
        public string? FirstKind { get; init; }
        public string? NextKind { get; init; }
        public string? CurrentKind { get; init; }
        public string? AdaptiveWaitMessage { get; init; }
    }

    private sealed record Observation(string Id, string Kind, int Step, string Status, bool Resumed,
        DateTime? Started, DateTime? Ended, string? Outputs, bool Truncated, bool Legacy);

    public static async Task<IReadOnlyList<AcquisitionStageAttempt>> ReadAsync(BakabaseDbContext db, int taskId,
        int? currentRun, CancellationToken ct, int? requestedRun = null)
    {
        var owner = taskId.ToString(CultureInfo.InvariantCulture);
        var links = db.Set<WorkflowRunSourceDbModel>().Where(s => s.Module == "acquisition" && s.EntityKind == "task");
        var owned = db.WorkflowRuns.AsNoTracking().Where(r =>
            (r.Id == currentRun || links.Any(s => s.WorkflowRunId == r.Id && s.EntityId == owner)) &&
            !links.Any(s => s.WorkflowRunId == r.Id && s.EntityId != owner) &&
            !db.AcquisitionTasks.Any(t => t.Id != taskId && t.WorkflowRunId == r.Id));
        if (requestedRun.HasValue) owned = owned.Where(r => r.Id == requestedRun.Value);
        var runs = await owned.OrderBy(r => r.Id).Select(r => new { r.Id, r.Status, r.StepStatsJson,
            r.CurrentStepIndex }).ToListAsync(ct);
        var ids = runs.Select(r => r.Id).ToArray();
        if (ids.Length == 0) return [];
        // A frozen plan can contain a full set of private processing instructions. Read only
        // the kind names needed to identify reuse and an old run's exact cursor.
        var boundaries = await db.Database.SqlQuery<RunBoundary>($"""
            SELECT r.Id RunId,
                json_extract(r.PlanSnapshotJson, '$.nodes[0].kind') FirstKind,
                json_extract(r.PlanSnapshotJson, '$.nodes[1].kind') NextKind,
                CASE WHEN r.CurrentStepIndex >= 0 THEN
                    json_extract(r.PlanSnapshotJson, '$.nodes[' || r.CurrentStepIndex || '].kind') END CurrentKind,
                CASE WHEN r.WaitReason = 'cloud.fileProcessing' AND json_valid(r.WaitPromptJson) THEN
                    CASE WHEN json_extract(r.WaitPromptJson, '$.adaptiveAssessment') = 1
                        AND json_extract(r.WaitPromptJson, '$.deliveryReview') = 1
                        AND json_type(r.WaitPromptJson, '$.message') = 'text'
                    THEN substr(json_extract(r.WaitPromptJson, '$.message'), 1, 256) END
                END AdaptiveWaitMessage
            FROM WorkflowRuns r
            WHERE r.Id IN (SELECT value FROM json_each({JsonSerializer.Serialize(ids)})) AND json_valid(r.PlanSnapshotJson)
            """).ToDictionaryAsync(r => r.RunId, ct);
        var nodes = (await db.WorkflowNodeExecutions.AsNoTracking().Where(n => ids.Contains(n.WorkflowRunId))
            .OrderBy(n => n.Id).Select(n => new { n.WorkflowRunId, n.Id, n.Kind, n.StepIndex, n.Status, n.StartedAt,
                n.CompletedAt, n.OutputsJson, n.OutputsTruncated, n.ResumeKind }).ToListAsync(ct)).ToLookup(n => n.WorkflowRunId);
        var attempts = new List<AcquisitionStageAttempt>();
        var counts = new Dictionary<string, int>();
        foreach (var run in runs)
        {
            var boundary = boundaries.GetValueOrDefault(run.Id);
            var localInputStage = LocalInputStage(boundary?.FirstKind, boundary?.NextKind);
            var recorded = nodes[run.Id].Select(n => new Observation(n.Id.ToString(CultureInfo.InvariantCulture), n.Kind,
                n.StepIndex, n.Status == "succeeded" ? "completed" : n.Status, n.ResumeKind is "checkpoint" or "signal",
                DateTime.SpecifyKind(n.StartedAt, DateTimeKind.Utc),
                n.CompletedAt is {} end ? DateTime.SpecifyKind(end, DateTimeKind.Utc) : null,
                n.OutputsJson, n.OutputsTruncated, false)).ToList();
            // An older run may acquire a modern node record on its first retry after upgrade.
            // Earlier completed steps are still known from the saved counters, but their exact times are unknown.
            var before = recorded.Count == 0 ? int.MaxValue : recorded.Min(n => n.Step);
            var observations = WorkflowExtensions.ParseStepStats(run.StepStatsJson)
                .Where(s => s.StepIndex < before).OrderBy(s => s.StepIndex).Select(s =>
                    new Observation($"step-{s.StepIndex}", s.Kind, s.StepIndex, s.FailedCount == 0 ? "completed" : "failed",
                        false, null, null, null, false, true)).ToList();
            // A waiting cursor proves this node ran. A pending/interrupted cursor can simply point at the next unstarted step.
            if (recorded.Count == 0 && run.Status == WorkflowRunStatus.Waiting &&
                run.CurrentStepIndex is >= 0 && observations.All(n => n.Step != run.CurrentStepIndex))
            {
                if (boundary?.CurrentKind is {} kind)
                    observations.Add(new($"step-{run.CurrentStepIndex}", kind, run.CurrentStepIndex.Value,
                        RunStatus(run.Status), false, null, null, null, false, true));
            }
            observations.AddRange(recorded);
            if (observations.LastOrDefault() is { Status: "running" or "waiting" } last &&
                run.Status is WorkflowRunStatus.Cancelled or WorkflowRunStatus.Interrupted or WorkflowRunStatus.Failed)
                observations[^1] = last with { Status = RunStatus(run.Status) };

            var group = new List<Observation>();
            string? previousStage = null;
            void FinishGroup()
            {
                if (group.Count == 0 || previousStage == null) return;
                var first = group[0];
                var lastNode = group[^1];
                var number = counts.GetValueOrDefault(previousStage) + 1;
                counts[previousStage] = number;
                var outputs = new WorkflowOutputMetadataCapture();
                // A retained run prompt describes only the last suspension at its current cursor.
                // Earlier attempts on the same run may have stopped for entirely different reasons.
                long? savedAssessment = null;
                if (run.Status == WorkflowRunStatus.Waiting && ReferenceEquals(lastNode, observations[^1]) &&
                    run.CurrentStepIndex == lastNode.Step && lastNode.Status == "waiting" &&
                    lastNode.Kind == AssessFileArtifactDeliveryActivity.ActivityKind && boundary?.CurrentKind == lastNode.Kind &&
                    AdaptiveAssessmentDiagnostic.FromSavedWait(boundary.AdaptiveWaitMessage) is { } diagnostic)
                {
                    outputs.Add([diagnostic]);
                    if (!lastNode.Legacy && diagnostic.Code != "MAX_LLM_ASSESSMENT_CALLS")
                        savedAssessment = long.Parse(lastNode.Id, CultureInfo.InvariantCulture);
                }
                foreach (var node in group) outputs.Add(WorkflowOutputMetadataCapture.Read(node.Outputs));
                attempts.Add(new($"{run.Id}/{first.Id}", previousStage, number, run.Id, lastNode.Status,
                    first.Started, lastNode.Ended, group.Select(n => n.Kind).ToArray(), outputs.Outputs.ToList(),
                    outputs.Truncated || group.Any(n => n.Truncated), group.Any(n => n.Legacy))
                    {
                        HasAssessmentCalls = group.Where(n => n.Kind == AssessFileArtifactDeliveryActivity.ActivityKind)
                            .Any(n => WorkflowOutputMetadataCapture.Read(n.Outputs, includeDetails: true)
                                .Any(block => AssessmentCallReceipt.Read(block) != null)),
                        SavedAssessmentExecutionId = savedAssessment,
                        ExecutionIds = group.Where(n => !n.Legacy).Select(n => long.Parse(n.Id, CultureInfo.InvariantCulture)).ToArray()
                    });
                group.Clear();
            }

            foreach (var node in observations)
            {
                var stage = node.Kind == AcquisitionPlanContinueFilesActivity.ActivityKind && localInputStage != null
                    ? localInputStage : StageFor(node.Kind);
                if (previousStage != stage || node.Resumed || group.Count > 0 && node.Step <= group[^1].Step)
                    FinishGroup();
                previousStage = stage;
                if (stage != null) group.Add(node);
            }
            FinishGroup();
        }
        return attempts;
    }

    private static string RunStatus(WorkflowRunStatus status) => status switch
    {
        WorkflowRunStatus.Failed => "failed",
        WorkflowRunStatus.Interrupted => "interrupted",
        WorkflowRunStatus.Waiting => "waiting",
        WorkflowRunStatus.Running => "running",
        WorkflowRunStatus.Cancelled => "cancelled",
        _ => "unknown"
    };
}
