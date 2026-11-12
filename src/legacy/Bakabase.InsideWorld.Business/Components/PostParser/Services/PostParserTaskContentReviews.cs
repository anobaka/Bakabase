using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Services;

public partial class PostParserTaskService<TDbContext>
{
    private static PostParserContentReviewScope ValidateReviewScope(PostParserContentReviewScope input)
    {
        if (input == null || input.TaskIds is {Count: > 10000} || input.TaskIds?.Any(id => id <= 0) == true ||
            input.TaskIds != null && input.TaskIds.Distinct().Count() != input.TaskIds.Count)
            throw new ArgumentException("Provide a bounded set of unique task IDs.");
        var filter = ValidateFilterScope(new() {Keyword = input.Keyword, Status = input.Status, ReasonCode = input.ReasonCode});
        return new() {Keyword = filter.Keyword, Status = filter.Status, ReasonCode = filter.ReasonCode, TaskIds = input.TaskIds?.Order().ToList()};
    }
    private static bool MatchesReviewScope(ParserPageFact fact, PostParserContentReviewScope scope) =>
        (scope.TaskIds == null || scope.TaskIds.Contains(fact.Id)) &&
        MatchesScope(fact, new() {Keyword = scope.Keyword, Status = scope.Status, ReasonCode = scope.ReasonCode});
    public async Task<PostParserContentReviewPreview> PreviewContentReview(PostParserContentReviewScope input, CancellationToken ct = default)
    {
        var scope = ValidateReviewScope(input);
        var ids = (await ReadFacts(scope.Keyword!, ct)).Where(f => MatchesReviewScope(f, scope)).Select(f => f.Id).ToList();
        if (ids.Count > 10_000) throw new ArgumentException("Narrow the review to at most 10000 tasks.");
        var rows = await ParserTasks.AsNoTracking().Where(t => ids.Contains(t.Id) && !t.IsDeleted).OrderBy(t => t.Id)
            .Select(t => new Bakabase.InsideWorld.Business.Components.PostParser.Models.Db.PostParserTaskDbModel
            {Id = t.Id, Title = t.Title, Link = t.Link, Source = t.Source, Revision = t.Revision, WorkflowRunId = t.WorkflowRunId,
                ParsingState = t.ParsingState, ContentSnapshotJson = t.ContentSnapshotJson,
                AvailabilityJson = t.AvailabilityJson, ContentReadinessJson = t.ContentReadinessJson}).ToListAsync(ct);
        var runIds = rows.Where(t => t.WorkflowRunId != null).Select(t => t.WorkflowRunId!.Value).ToList();
        var runs = await db.Set<WorkflowRunDbModel>().AsNoTracking().Include(r => r.NodeExecutions)
            .Where(r => runIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        return new() {Scope = scope, MatchedCount = ids.Count,
            AutoBuyThreshold = purchaseOptions.Value.AutoBuyThreshold, MinimumRemainingCoins = purchaseOptions.Value.MinimumRemainingCoins,
            Tasks = rows.Select(row => workflow.ContentReviewView(row, row.WorkflowRunId is { } id ? runs.GetValueOrDefault(id) : null, scope)).ToList()};
    }

    public async Task<PostParserContentReviewResult> ConfirmContentReview(ConfirmPostParserContentReviewInput input, CancellationToken ct = default)
    {
        var scope = ValidateReviewScope(input.Scope);
        if (!Guid.TryParse(input.RequestId, out _) || input.Tasks == null || input.Tasks.Count is 0 or > 10_000 ||
            input.Tasks.Any(t => t == null || t.TaskId <= 0 || t.Revision < 0 || t.WorkflowRunId <= 0 ||
                t.ContentFingerprint?.Length != 64 || t.QuoteHash?.Length != 64 || t.DecisionVersion < 0 || t.Targets == null || t.Targets.Count > 256 ||
                t.Targets.Any(a => a == null || a.TargetId?.Length != 64 || a.Action is not ("buy" or "notRequired" or "defer")) ||
                t.Targets.Select(a => a.TargetId).Distinct(StringComparer.Ordinal).Count() != t.Targets.Count) ||
            input.Tasks.Select(t => t.TaskId).Distinct().Count() != input.Tasks.Count)
            throw new ArgumentException("Provide a bounded, unique selection from the content review preview and a request ID.");
        var requestId = Guid.Parse(input.RequestId).ToString("N");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { scope, Tasks = input.Tasks.OrderBy(t => t.TaskId).Select(t => new { t.TaskId, t.Revision, t.WorkflowRunId,
            t.DecisionVersion, t.ContentFingerprint, t.QuoteHash, Targets = t.Targets.OrderBy(a => a.TargetId) }) }, WorkflowJson.Options))));
        var result = new PostParserContentReviewResult {RequestId = requestId, RequestedCount = input.Tasks.Count};
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            // Request IDs are global to this batch, not a way to add other tasks on a replay.
            var previousStates = await ParserTasks.AsNoTracking().Where(t => t.ContentReadinessJson != null && t.ContentReadinessJson.Contains(requestId))
                .Select(t => t.ContentReadinessJson).ToListAsync(ct);
            if (previousStates.SelectMany(s => PostParserContentReadinessState.Read(s).Reviews)
                .Any(r => r.RequestId == requestId && r.RequestHash != hash))
                throw new InvalidOperationException("POST_CONTENT_REVIEW_REQUEST_CHANGED: Use a new request ID for a different review selection.");
            foreach (var requested in input.Tasks)
            {
                ct.ThrowIfCancellationRequested();
                var row = await ParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == requested.TaskId, ct);
                var prior = PostParserContentReadinessState.Read(row?.ContentReadinessJson).Reviews.LastOrDefault(r => r.RequestId == requestId);
                if (prior != null)
                {
                    result.Items.Add(new(requested.TaskId, prior.ConfirmationOutcome == "prepared" ? "failed" : prior.ConfirmationOutcome,
                        prior.WorkflowRunId, prior.Id, prior.Problem));
                    continue;
                }
                string? problem = row == null || row.IsDeleted ? "missingOrDeleted" :
                    row.Revision != requested.Revision || row.WorkflowRunId != requested.WorkflowRunId ? "snapshotChanged" : null;
                if (problem == null)
                {
                    var fact = (await ReadFacts(scope.Keyword!, ct, requested.TaskId)).SingleOrDefault();
                    if (fact == null || !MatchesReviewScope(fact, scope)) problem = "scopeChanged";
                }
                if (problem != null) { result.Items.Add(new(requested.TaskId, "skipped", requested.WorkflowRunId, Reason: problem)); continue; }
                var run = await db.Set<WorkflowRunDbModel>().Include(r => r.NodeExecutions).SingleOrDefaultAsync(r => r.Id == requested.WorkflowRunId, ct);
                try { result.Items.Add(await workflow.ConfirmContentReviewUnderGateAsync(row!, run, scope, requested, requestId, hash, ct)); }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // A prior row may already be committed. Report this item; never replay it implicitly.
                    Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, error,
                        "Content review could not be accepted for task {TaskId}.", requested.TaskId);
                    db.ChangeTracker.Clear();
                    result.Items.Add(new(requested.TaskId, "failed", requested.WorkflowRunId, Reason: "queueFailed"));
                }
            }
        }
        finally { gate.Semaphore.Release(); }
        result.QueuedCount = result.Items.Count(i => i.Status == "queued");
        result.UnchangedCount = result.Items.Count(i => i.Status == "unchanged");
        result.SkippedCount = result.Items.Count(i => i.Status == "skipped");
        result.FailedCount = result.Items.Count(i => i.Status == "failed");
        return result;
    }
}
