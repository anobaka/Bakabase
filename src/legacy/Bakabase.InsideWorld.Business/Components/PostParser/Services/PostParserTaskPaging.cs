using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Services;

public partial class PostParserTaskService<TDbContext>
{
    private static readonly string[] StatusGroups = ["running", "queued", "ready", "attention", "success", "failed", "paused"];
    private static readonly HashSet<string> RunningStages = ["fetching", "waitingForAi", "checkingAvailability", "purchasing", "extracting", "checkingLinks"];

    public async Task<PostParserTaskPage> Search(PostParserTaskSearchInput input, CancellationToken ct = default)
    {
        if (input.PageIndex < 1 || input.PageSize is < 1 or > 100 || input.Keyword?.Length > 1000 ||
            input.Status is not (null or "all") && !StatusGroups.Contains(input.Status) || input.LocateTaskId is <= 0)
            throw new ArgumentException("Provide a valid parser page, bounded keyword and status filter.");
        var scope = ValidateFilterScope(new() {Keyword = input.Keyword, Status = input.Status, ReasonCode = input.ReasonCode});
        var facts = await ReadFacts(scope.Keyword!, ct);
        var filtered = facts.Where(f => MatchesScope(f, scope)).ToList();
        var page = Math.Min(input.PageIndex, Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)input.PageSize)));
        if (input.LocateTaskId is { } locate && filtered.FindIndex(f => f.Id == locate) is >= 0 and var index)
            page = index / input.PageSize + 1;
        var selected = filtered.Skip((page - 1) * input.PageSize).Take(input.PageSize).ToList();
        var ids = selected.Select(f => f.Id).ToList();
        // Only this bounded query materializes the saved HTML, model result and content snapshot.
        var rows = await ParserTasks.AsNoTracking().Where(t => ids.Contains(t.Id) && !t.IsDeleted).OrderBy(t => t.Id).ToListAsync(ct);
        var runIds = rows.Where(t => t.WorkflowRunId != null).Select(t => t.WorkflowRunId!.Value).ToList();
        var runs = await db.Set<WorkflowRunDbModel>().AsNoTracking().Where(r => runIds.Contains(r.Id))
            .Select(r => new {r.Id, r.Status, r.ErrorMessage, r.CompletedAt}).ToDictionaryAsync(r => r.Id, ct);
        var sourceIds = ids.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
        var handoffs = await db.Set<AcquisitionHandoffDbModel>().AsNoTracking()
            .Where(h => h.SourceModule == "postParser" && h.SourceKind == "task" && sourceIds.Contains(h.SourceId)).ToListAsync(ct);
        var handoffsBySource = handoffs.ToDictionary(h => (h.SourceId, h.SourceRevision));
        var items = rows.Select(row =>
        {
            var task = row.ToDomainModel();
            if (task.WorkflowRunId is { } id && runs.TryGetValue(id, out var run))
            {
                task.WorkflowStatus = run.Status;
                task.Error = run.Status switch
                {
                    WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted => run.ErrorMessage ?? "The parsing workflow failed.",
                    WorkflowRunStatus.Cancelled => "The parsing workflow was cancelled.",
                    WorkflowRunStatus.Success when PostParserWorkflowService<TDbContext>.IsPending(task) =>
                        "The workflow completed without download information. Parse this task again.",
                    _ => null
                };
                task.CompletedAt = run.Status == WorkflowRunStatus.Success && task.Error == null ? run.CompletedAt?.ToUniversalTime() : null;
            }
            else if (task.WorkflowRunId != null)
            {
                var complete = PostParserSavedResults.HasCompletedDownloadResult(row);
                if (!complete) task.Error ??= PostParserSavedResults.MissingWorkflowRunError;
                else if (task.Error == PostParserSavedResults.MissingWorkflowRunError) task.Error = null;
            }
            // A terminal queued handler is reconciled in the lightweight facts as well as the
            // page item, without running RefreshAll (which reads every large saved payload).
            var fact = selected.Single(f => f.Id == row.Id);
            if (fact.ReconciledTerminalHandler && row.Revision == fact.Revision && row.WorkflowRunId == fact.WorkflowRunId &&
                task.WorkflowStatus == WorkflowRunStatus.Pending)
            {
                task.WorkflowStatus = fact.WorkflowStatus;
                if (fact.WorkflowStatus is WorkflowRunStatus.Cancelled or WorkflowRunStatus.Failed)
                    task.Error ??= "The queued parsing task did not complete.";
            }
            if (handoffsBySource.TryGetValue((task.Id.ToString(CultureInfo.InvariantCulture), task.Revision.ToString(CultureInfo.InvariantCulture)), out var handoff))
                task.AcquisitionHandoff = AcquisitionHandoffView.From(handoff);
            task.AutoBuyThreshold = purchaseOptions.Value.AutoBuyThreshold;
            task.MinimumRemainingCoins = purchaseOptions.Value.MinimumRemainingCoins;
            return task;
        }).ToList();
        return new()
        {
            Items = items, PageIndex = page, PageSize = input.PageSize, TotalCount = filtered.Count, GlobalTotal = facts.Count,
            AttentionReasonCounts = facts.Where(f => f.MatchesKeyword && f.Group == "attention").SelectMany(f => f.AttentionReasons)
                .GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count()),
            StatusCounts = StatusGroups.ToDictionary(g => g, g => facts.Count(f => f.Group == g)),
            RunningStages = facts.Where(f => f.Group == "running").GroupBy(f => f.Stage ?? "running").ToDictionary(g => g.Key, g => g.Count()),
            RunningTaskId = facts.FirstOrDefault(f => f.Group == "running")?.Id,
            StartableCount = facts.Count(f => f.Startable),
            PendingHandoffCount = await db.Set<AcquisitionHandoffDbModel>().CountAsync(h => h.SourceModule == "postParser" &&
                h.SourceKind == "task" && (h.Status == "queued" || h.Status == "prepared"), ct)
        };
    }

    public async Task<ReParseFailedPostParserTasksInput> GetFailedSelection(CancellationToken ct = default) => new()
    {
        Tasks = (await ReadFacts("", ct)).Where(f => f.Group == "failed").Select(f => new PostParserReparseSelection
            {TaskId = f.Id, Revision = f.Revision, WorkflowRunId = f.WorkflowRunId}).ToList()
    };

    public async Task<PostParserTaskFilteredSelection> SelectFilteredTasks(PostParserTaskFilterScope scope,
        int maxTasks, CancellationToken ct = default)
    {
        if (maxTasks is < 1 or > 10_000)
            throw new ArgumentException("Provide a task selection limit between 1 and 10000.");
        var normalized = ValidateFilterScope(scope);
        var selected = (await ReadFacts(normalized.Keyword!, ct)).Where(f => MatchesScope(f, normalized)).ToList();
        // Never return a truncated manifest that could be mistaken for all matching tasks.
        // Callers that combine these identities with payloads hold the parser execution gate.
        return new(selected.Count, selected.Count > maxTasks, selected.Count > maxTasks ? [] :
            selected.Select(f => new PostParserTaskSelectionSnapshot(f.Id, f.Revision, f.WorkflowRunId)).ToList());
    }

    private async Task<List<ParserPageFact>> ReadFacts(string keyword, CancellationToken ct, int taskId = 0)
    {
        // SQLite evaluates scalar predicates inside the database. The application never receives
        // the off-page Results/ContentSnapshotJson columns. json_tree also searches escaped Unicode
        // and decoded values, rather than losing passwords/titles stored as JSON escape sequences.
        var facts = await db.Database.SqlQueryRaw<ParserPageFact>(FactsSql, keyword, PostParserSavedResults.MissingWorkflowRunError, taskId).ToListAsync(ct);
        var live = tasks.Tasks.ToDictionary(t => t.Id);
        foreach (var fact in facts)
        {
            var handler = fact.WorkflowRunId is { } id ? live.GetValueOrDefault($"workflow.run.{id}") : null;
            var status = handler?.Task.Status;
            if (fact.WorkflowStatus == WorkflowRunStatus.Pending && handler is {HasAttachedExecution: false} &&
                status is BTaskStatus.Cancelled or BTaskStatus.Error)
            {
                fact.WorkflowStatus = status == BTaskStatus.Cancelled ? WorkflowRunStatus.Cancelled : WorkflowRunStatus.Failed;
                fact.ReconciledTerminalHandler = true;
            }
            fact.HasError = fact.WorkflowStatus switch
            {
                WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted or WorkflowRunStatus.Cancelled => true,
                WorkflowRunStatus.Success => fact.IsPending,
                WorkflowRunStatus.Pending or WorkflowRunStatus.Running or WorkflowRunStatus.Waiting => false,
                _ => fact.StoredError && !(fact.MissingRunError && fact.SavedComplete) || fact.WorkflowRunId != null && !fact.SavedComplete
            };
            var lockItems = ParseLightJson<List<PostContentLock>>(fact.LockJson) ?? [];
            IReadOnlyList<PostContentLock>? blocking = null;
            var projection = ParseLightJson<PostParserReadinessProjection>(fact.ReadinessProjectionJson);
            if (projection != null && !string.IsNullOrWhiteSpace(fact.ReadinessFingerprint) &&
                projection.Fingerprint == fact.ReadinessFingerprint && projection.BlockingBlockIds != null)
            {
                var ids = projection.BlockingBlockIds.ToHashSet(StringComparer.Ordinal);
                blocking = lockItems.Where(l => !l.IsBought && (string.IsNullOrWhiteSpace(l.BlockId) || ids.Contains(l.BlockId))).ToList();
                fact.HasLocks = blocking.Count > 0;
            }
            fact.Group = status switch
            {
                BTaskStatus.NotStarted => "queued",
                BTaskStatus.Paused => "paused",
                BTaskStatus.Running or BTaskStatus.Pausing or BTaskStatus.Resuming or BTaskStatus.Cancelling => "running",
                _ => PostParserTaskSummary.Classify(fact.WorkflowRunId, fact.WorkflowStatus, fact.ParsingState,
                    fact.HasError || fact.TargetError, fact.HasLocks || fact.DownloadIncomplete,
                    fact.HasResources, fact.AllTargetsHaveData)
            };
            if (fact.Group == "attention")
            {
                var locks = lockItems;
                var evidence = ParseLightJson<List<string>>(fact.AvailabilityEvidenceJson);
                fact.AttentionReasons = PostParserAttentionReasons.Create(fact.ParsingState, fact.DownloadIncomplete,
                    new() {Locks = locks, Balance = fact.Balance, SourceHint = fact.ContentSourceHint, SourceUrl = fact.ContentSourceUrl}, fact.AvailabilityStatus, evidence,
                    purchaseOptions.Value.AutoBuyThreshold, purchaseOptions.Value.MinimumRemainingCoins, blocking);
            }
            if (fact.Group == "running")
            {
                var stage = (handler?.Task.Data as WorkflowTaskProgress)?.Stage;
                fact.Stage = stage != null && RunningStages.Contains(stage) ? stage : "running";
            }
            fact.Startable = !(handler?.HasAttachedExecution == true || status is BTaskStatus.WaitingForInput ||
                    status?.IsActiveOrPending() == true) &&
                fact.WorkflowStatus is not (WorkflowRunStatus.Success or WorkflowRunStatus.Waiting or WorkflowRunStatus.Running) &&
                !(fact.SavedComplete && !fact.HasError) && (fact.WorkflowRunId != null || fact.HasError || fact.IsPending);
        }
        return facts;
    }

    private static T? ParseLightJson<T>(string? json)
    {
        try { return json == null ? default : JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web); }
        catch (JsonException) { return default; }
    }

    private static bool MatchesScope(ParserPageFact fact, PostParserTaskFilterScope scope) =>
        fact.MatchesKeyword && (scope.Status == "all" || fact.Group == scope.Status) &&
        (scope.ReasonCode == null || fact.Group == "attention" && fact.AttentionReasons.Contains(scope.ReasonCode));

    internal sealed class ParserPageFact
    {
        public int Id { get; set; }
        public int Revision { get; set; }
        public int Source { get; set; }
        public int? WorkflowRunId { get; set; }
        public WorkflowRunStatus? WorkflowStatus { get; set; }
        public string? ParsingState { get; set; }
        public bool StoredError { get; set; }
        public bool MissingRunError { get; set; }
        public bool IsPending { get; set; }
        public bool SavedComplete { get; set; }
        public bool TargetError { get; set; }
        public bool AllTargetsHaveData { get; set; }
        public bool DownloadIncomplete { get; set; }
        public bool HasResources { get; set; }
        public bool HasLocks { get; set; }
        public bool MatchesKeyword { get; set; }
        public string? AvailabilityStatus { get; set; }
        public string? AvailabilityEvidenceJson { get; set; }
        public string? LockJson { get; set; }
        public string? ContentSourceHint { get; set; }
        public string? ContentSourceUrl { get; set; }
        public string? ReadinessFingerprint { get; set; }
        public string? ReadinessProjectionJson { get; set; }
        public decimal? Balance { get; set; }
        [NotMapped] public List<string> AttentionReasons { get; set; } = [];
        [NotMapped] public bool HasError { get; set; }
        [NotMapped] public string Group { get; set; } = "ready";
        [NotMapped] public string? Stage { get; set; }
        [NotMapped] public bool Startable { get; set; }
        [NotMapped] public bool ReconciledTerminalHandler { get; set; }
    }

    private const string FactsSql = """
        WITH source AS (
            SELECT t.Id, t.Revision, t.Source, t.WorkflowRunId, r.Status AS WorkflowStatus, t.ParsingState,
                CASE WHEN json_valid(t.AvailabilityJson) AND json_type(t.AvailabilityJson) = 'object' THEN t.AvailabilityJson ELSE '{{}}' END AS AvailabilityJson,
                t.Error, r.ErrorMessage AS RunError, t.Title, t.Link, t.Text,
                CASE WHEN json_valid(t.ContentReadinessJson) AND json_type(t.ContentReadinessJson) = 'object' THEN t.ContentReadinessJson ELSE '{{}}' END AS ReadinessJson,
                CASE WHEN json_valid(t.Results) AND json_type(t.Results) = 'object' THEN t.Results ELSE '{{}}' END AS ResultJson,
                CASE WHEN json_valid(t.Targets) AND json_type(t.Targets) = 'array' AND json_array_length(t.Targets) > 0 THEN t.Targets ELSE '[1]' END AS TargetJson,
                CASE WHEN json_valid(t.ContentSnapshotJson) AND json_type(t.ContentSnapshotJson) = 'object' THEN t.ContentSnapshotJson ELSE '{{}}' END AS ContentJson,
                CASE WHEN json_valid(t.Results) THEN json_type(t.Results) = 'object' ELSE 0 END AS ResultsPresent
            FROM PostParserTasks t LEFT JOIN WorkflowRuns r ON r.Id = t.WorkflowRunId WHERE t.IsDeleted = 0 AND ({2} = 0 OR t.Id = {2})
        ), nodes AS MATERIALIZED (
            -- An optimization fence keeps SQLite from re-evaluating the nested JSON extraction
            -- for every status predicate. This is scoped to this query, never a stale task cache.
            SELECT source.*,
                CASE WHEN json_type(ResultJson, '$.DownloadInfo') = 'object' THEN json_extract(ResultJson, '$.DownloadInfo')
                     WHEN json_type(ResultJson, '$."1"') = 'object' THEN json_extract(ResultJson, '$."1"') ELSE '{{}}' END AS DownloadJson
            FROM source
        ), data AS (
            SELECT nodes.*,
                CASE WHEN json_type(DownloadJson, '$.data') = 'object' THEN json_extract(DownloadJson, '$.data')
                     WHEN json_type(DownloadJson, '$.Data') = 'object' THEN json_extract(DownloadJson, '$.Data') ELSE DownloadJson END AS DownloadData,
                NOT EXISTS (SELECT 1 FROM json_each(TargetJson) target WHERE NOT EXISTS (
                    SELECT 1 FROM json_each(ResultJson) result WHERE result.key = CAST(target.value AS TEXT) OR
                        target.value IN (1, '1', 'DownloadInfo') AND result.key IN ('DownloadInfo', '1'))) AS AllTargetsPresent,
                NOT EXISTS (SELECT 1 FROM json_each(TargetJson) target WHERE NOT EXISTS (
                    SELECT 1 FROM json_each(ResultJson) result WHERE
                        (result.key = CAST(target.value AS TEXT) OR target.value IN (1, '1', 'DownloadInfo') AND result.key IN ('DownloadInfo', '1')) AND
                        CASE WHEN result.type = 'object' THEN CASE WHEN json_type(result.value, '$.data') IS NOT NULL OR json_type(result.value, '$.error') IS NOT NULL OR json_type(result.value, '$.parsedAt') IS NOT NULL
                        THEN json_type(result.value, '$.data') = 'object' ELSE 1 END ELSE 0 END)) AS AllTargetsHaveData,
                EXISTS (SELECT 1 FROM json_each(TargetJson) target JOIN json_each(ResultJson) result ON
                    result.key = CAST(target.value AS TEXT) OR target.value IN (1, '1', 'DownloadInfo') AND result.key IN ('DownloadInfo', '1')
                    WHERE CASE WHEN result.type = 'object' THEN json_type(result.value, '$.error') = 'text' AND json_extract(result.value, '$.error') <> '' ELSE 0 END) AS TargetError
            FROM nodes
        ), normalized AS (
            SELECT data.*,
                CASE WHEN json_type(DownloadData, '$.resources') = 'array' THEN json_extract(DownloadData, '$.resources')
                     WHEN json_type(DownloadData, '$.Resources') = 'array' THEN json_extract(DownloadData, '$.Resources') ELSE '[]' END AS ResourceJson,
                CASE WHEN json_type(ContentJson, '$.locks') = 'array' THEN json_extract(ContentJson, '$.locks')
                     WHEN json_type(ContentJson, '$.Locks') = 'array' THEN json_extract(ContentJson, '$.Locks') ELSE '[]' END AS LockJson
            FROM data
        )
        SELECT Id, Revision, Source, WorkflowRunId, WorkflowStatus, ParsingState, LockJson,
            CASE WHEN json_type(ContentJson, '$.sourceHint') = 'text' THEN json_extract(ContentJson, '$.sourceHint')
                 WHEN json_type(ContentJson, '$.SourceHint') = 'text' THEN json_extract(ContentJson, '$.SourceHint') END AS ContentSourceHint,
            CASE WHEN json_type(ContentJson, '$.sourceUrl') = 'text' THEN json_extract(ContentJson, '$.sourceUrl')
                 WHEN json_type(ContentJson, '$.SourceUrl') = 'text' THEN json_extract(ContentJson, '$.SourceUrl') END AS ContentSourceUrl,
            CASE WHEN json_type(ContentJson, '$.readiness.fingerprint') = 'text' THEN json_extract(ContentJson, '$.readiness.fingerprint')
                 WHEN json_type(ContentJson, '$.Readiness.Fingerprint') = 'text' THEN json_extract(ContentJson, '$.Readiness.Fingerprint') END AS ReadinessFingerprint,
            CASE WHEN json_type(ReadinessJson, '$.projection') = 'object' THEN json_extract(ReadinessJson, '$.projection') ELSE NULL END AS ReadinessProjectionJson,
            CASE WHEN json_type(ContentJson, '$.balance') IN ('integer','real') THEN json_extract(ContentJson, '$.balance')
                 WHEN json_type(ContentJson, '$.Balance') IN ('integer','real') THEN json_extract(ContentJson, '$.Balance') END AS Balance,
            CASE WHEN json_type(AvailabilityJson, '$.status') = 'text' THEN json_extract(AvailabilityJson, '$.status')
                 WHEN json_type(AvailabilityJson, '$.Status') = 'text' THEN json_extract(AvailabilityJson, '$.Status') END AS AvailabilityStatus,
            CASE WHEN json_type(AvailabilityJson, '$.evidence') = 'array' THEN json_extract(AvailabilityJson, '$.evidence')
                 WHEN json_type(AvailabilityJson, '$.Evidence') = 'array' THEN json_extract(AvailabilityJson, '$.Evidence') ELSE '[]' END AS AvailabilityEvidenceJson,
            COALESCE(Error <> '', 0) AS StoredError, COALESCE(Error = {1}, 0) AS MissingRunError,
            (ParsingState IS NOT NULL AND ParsingState <> 'complete') OR NOT ResultsPresent OR NOT AllTargetsPresent AS IsPending,
            TargetError, AllTargetsHaveData,
            (COALESCE(json_type(DownloadData, '$.isComplete') = 'false', 0) OR
                COALESCE(json_type(DownloadData, '$.IsComplete') = 'false', 0)) AS DownloadIncomplete,
            COALESCE(json_type(DownloadData, '$.resources') = 'array' OR json_type(DownloadData, '$.Resources') = 'array', 0) AS HasResources,
            COALESCE(ParsingState = 'complete' AND NOT TargetError AND
                (json_type(DownloadData, '$.resources') = 'array' OR json_type(DownloadData, '$.Resources') = 'array') AND
                COALESCE(json_type(DownloadData, '$.isComplete') <> 'false', 1) AND
                COALESCE(json_type(DownloadData, '$.IsComplete') <> 'false', 1) AND
                NOT EXISTS (SELECT 1 FROM json_each(ResourceJson) WHERE type <> 'object'), 0) AS SavedComplete,
            EXISTS (SELECT 1 FROM json_each(LockJson)
                WHERE CASE WHEN type = 'object' THEN COALESCE(json_extract(value, '$.isBought'), json_extract(value, '$.IsBought'), 0) = 0 ELSE 0 END) AS HasLocks,
            CASE WHEN {0} = '' THEN 1
                WHEN instr(lower(CAST(Id AS TEXT) || char(10) || COALESCE(Title, '') || char(10) || COALESCE(Link, '') || char(10) || COALESCE(Text, '') || char(10) || COALESCE(Error, '') || char(10) || COALESCE(RunError, '')), lower({0})) > 0 THEN 1
                WHEN instr(lower(ResultJson), lower({0})) > 0 THEN 1
                WHEN EXISTS (SELECT 1 FROM json_tree(ResultJson) WHERE type = 'text' AND instr(lower(atom), lower({0})) > 0) THEN 1
                ELSE EXISTS (SELECT 1 FROM json_tree(ContentJson) WHERE type = 'text' AND instr(lower(atom), lower({0})) > 0) END AS MatchesKeyword
        FROM normalized ORDER BY Id
        """;
}
