using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Services;

public partial class PostParserTaskService<TDbContext>
{
    public async Task<PostParserTaskPurchasePreview> PreviewPurchases(PostParserTaskFilterScope input,
        CancellationToken ct = default)
    {
        var scope = ValidateFilterScope(input);
        var facts = (await ReadFacts(scope.Keyword!, ct)).Where(f => MatchesScope(f, scope)).ToList();
        if (facts.Count > 10_000) throw new ArgumentException("Narrow the purchase filter to at most 10000 tasks.");
        var ids = facts.Select(f => f.Id).ToList();
        var rows = await ParserTasks.AsNoTracking().Where(t => ids.Contains(t.Id) && !t.IsDeleted).OrderBy(t => t.Id)
            .Select(t => new PostParserTaskDbModel {Id = t.Id, Revision = t.Revision, WorkflowRunId = t.WorkflowRunId,
                Source = t.Source, Link = t.Link, ParsingState = t.ParsingState, ContentSnapshotJson = t.ContentSnapshotJson,
                AvailabilityJson = t.AvailabilityJson, ContentReadinessJson = t.ContentReadinessJson}).ToListAsync(ct);
        var runIds = rows.Where(t => t.WorkflowRunId != null).Select(t => t.WorkflowRunId!.Value).Distinct().ToList();
        var runs = await db.Set<WorkflowRunDbModel>().AsNoTracking().Include(r => r.NodeExecutions)
            .Where(r => runIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var result = new PostParserTaskPurchasePreview {Scope = scope, MatchedCount = facts.Count,
            AutoBuyThreshold = purchaseOptions.Value.AutoBuyThreshold, MinimumRemainingCoins = purchaseOptions.Value.MinimumRemainingCoins};
        var factsById = facts.ToDictionary(f => f.Id);
        foreach (var row in rows)
        {
            var fact = factsById[row.Id];
            string? problem = row.Revision != fact.Revision || row.WorkflowRunId != fact.WorkflowRunId ? "snapshotChanged" : null;
            var selection = problem == null ? PurchaseSelection(row, row.WorkflowRunId is { } id ? runs.GetValueOrDefault(id) : null, out problem, out _) : null;
            if (selection != null) result.Tasks.Add(selection);
            else result.Skipped.Add(new(row.Id, problem!));
        }
        foreach (var missing in ids.Except(rows.Select(r => r.Id))) result.Skipped.Add(new(missing, "missingOrDeleted"));
        result.ActionableCount = result.Tasks.Count;
        result.CurrencyTotals = result.Tasks.GroupBy(t => (t.CurrencyCode, t.CurrencyName))
            .Select(g => new PostParserCurrencyTotal(g.Key.CurrencyCode, g.Key.CurrencyName, g.Sum(t => t.MaxTotalCost))).ToList();
        if (result.CurrencyTotals.Count == 1)
        {
            result.KnownTotalCost = result.CurrencyTotals[0].Total;
            result.CurrencyCode = result.CurrencyTotals[0].CurrencyCode;
            result.CurrencyName = result.CurrencyTotals[0].CurrencyName;
        }
        result.SkipReasonCounts = result.Skipped.GroupBy(t => t.Reason).ToDictionary(g => g.Key, g => g.Count());
        return result;
    }

    public async Task<PostParserTaskPurchaseResult> ConfirmPurchases(ConfirmPostParserTaskPurchasesInput input,
        CancellationToken ct = default)
    {
        var scope = ValidateFilterScope(input.Scope);
        if (input.Tasks == null || input.Tasks.Count > 10_000 || input.Tasks.Select(t => t?.TaskId).Distinct().Count() != input.Tasks.Count ||
            input.Tasks.Any(t => t == null || t.TaskId <= 0 || t.Revision < 0 || t.WorkflowRunId <= 0 ||
                t.LockUrls == null || t.LockUrls.Count is 0 or > 100 || t.LockUrls.Any(u => string.IsNullOrWhiteSpace(u) || u.Length > 8192) ||
                t.LockUrls.Distinct(StringComparer.Ordinal).Count() != t.LockUrls.Count || t.MaxTotalCost < 0 ||
                t.QuoteHash == null || t.QuoteHash.Length != 64 || !t.QuoteHash.All(Uri.IsHexDigit)))
            throw new ArgumentException("Provide a bounded, unique purchase selection from the preview.");
        var result = new PostParserTaskPurchaseResult {RequestedCount = input.Tasks.Count};
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            // Enumerate the filter once. Near each enqueue recheck only that fixed ID; never
            // rescan the entire table per item or add newly matching tasks to this request.
            var eligible = (await ReadFacts(scope.Keyword!, ct)).Where(f => MatchesScope(f, scope)).Select(f => f.Id).ToHashSet();
            foreach (var requested in input.Tasks)
            {
                ct.ThrowIfCancellationRequested();
                var row = await ParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == requested.TaskId, ct);
                string? problem = row == null || row.IsDeleted ? "missingOrDeleted" :
                    row.Revision != requested.Revision || row.WorkflowRunId != requested.WorkflowRunId ? "snapshotChanged" :
                    !eligible.Contains(row.Id) ? "scopeChanged" : null;
                if (problem == null)
                {
                    var currentFact = (await ReadFacts(scope.Keyword!, ct, requested.TaskId)).SingleOrDefault();
                    if (currentFact == null || !MatchesScope(currentFact, scope)) problem = "scopeChanged";
                }
                if (problem != null) { result.Items.Add(new(requested.TaskId, "skipped", requested.WorkflowRunId, problem)); continue; }
                var run = await db.Set<WorkflowRunDbModel>().AsNoTracking().Include(r => r.NodeExecutions)
                    .SingleOrDefaultAsync(r => r.Id == requested.WorkflowRunId, ct);
                var current = PurchaseSelection(row!, run, out problem, out var expectedAccountKeyHash);
                if (current != null && (current.QuoteHash != requested.QuoteHash || current.MaxTotalCost != requested.MaxTotalCost ||
                    !current.LockUrls.SequenceEqual(requested.LockUrls, StringComparer.Ordinal))) problem = "quoteChanged";
                if (problem != null) { result.Items.Add(new(requested.TaskId, "skipped", requested.WorkflowRunId, problem)); continue; }
                try
                {
                    await workflow.PurchaseAndResumeUnderGateAsync(row!.Id, row.Revision, requested.WorkflowRunId,
                        requested.LockUrls, requested.MaxTotalCost, ct, expectedAccountKeyHash);
                    result.Items.Add(new(row.Id, "queued", requested.WorkflowRunId));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // Preserve partial acceptance. Never automatically replay an ambiguous enqueue.
                    logger.LogWarning(error, "Could not queue confirmed content purchase for parser task {TaskId}.", requested.TaskId);
                    result.Items.Add(new(requested.TaskId, "failed", requested.WorkflowRunId, "queueFailed"));
                }
            }
        }
        finally { gate.Semaphore.Release(); }
        result.QueuedCount = result.Items.Count(i => i.Status == "queued");
        result.SkippedCount = result.Items.Count(i => i.Status == "skipped");
        result.FailedCount = result.Items.Count(i => i.Status == "failed");
        return result;
    }

    private PostParserTaskPurchaseSelection? PurchaseSelection(PostParserTaskDbModel row, WorkflowRunDbModel? run, out string? problem, out string? accountKeyHash)
    {
        problem = null;
        accountKeyHash = null;
        if (run == null || run.Status is not (WorkflowRunStatus.Waiting or WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted) ||
            PostParserPurchaseRules.CurrentActivityKind(run) != PostParserWorkflow.UnlockContent)
        { problem = "notWaitingForPurchase"; return null; }
        if (tasks.Tasks.Any(t => t.Id == $"workflow.run.{run.Id}" && (t.HasAttachedExecution || !t.Task.Status.IsFinished())))
        { problem = "operationBusy"; return null; }
        PostParserInput? input;
        try { input = JsonSerializer.Deserialize<PostParserInput>(run.PayloadJson ?? "null", WorkflowJson.Options); }
        catch (JsonException) { input = null; }
        if (input?.TaskId != row.Id || input.Revision != row.Revision)
        { problem = "snapshotChanged"; return null; }
        var content = row.ToDomainModel().ContentSnapshot;
        var availability = ParseLightJson<PostAvailabilityAssessment>(row.AvailabilityJson);
        if (content == null) { problem = "noPurchaseQuote"; return null; }
        var sourceHint = content.SourceHint ?? input.SourceHint;
        if (!PostParserPurchaseRules.SupportsSource(sourceHint, purchasers)) { problem = "sourceUnsupported"; return null; }
        var quote = PostParserPurchaseQuote.Create(content, purchaseOptions.Value.AutoBuyThreshold, purchaseOptions.Value.MinimumRemainingCoins);
        if (quote.EligibleLockUrls.Count == 0)
        {
            var reasons = PostParserAttentionReasons.Create(row.ParsingState, false, content, availability?.Status,
                availability?.Evidence, purchaseOptions.Value.AutoBuyThreshold, purchaseOptions.Value.MinimumRemainingCoins);
            problem = new[] {"priceUnknown", "overPurchaseLimit", "balanceUnknown", "insufficientBalance"}.FirstOrDefault(reasons.Contains) ?? "noEligibleItems";
            return null;
        }
        if (quote.EligibleLockUrls.Count > 100) { problem = "noEligibleItems"; return null; }
        var accountKey = purchasers.First(p => string.Equals(p.Source.ToString(), sourceHint, StringComparison.OrdinalIgnoreCase)).AccountKey;
        accountKeyHash = SharedContentPurchasePolicy.HashAccountKey(accountKey);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            row.Id, row.Revision, row.WorkflowRunId, row.Source, row.Link, row.ContentSnapshotJson, row.AvailabilityJson, row.ContentReadinessJson,
            run.Status, run.CurrentStepIndex, run.CurrentItemJson, run.WaitPromptJson, run.PlanSnapshotJson,
            purchaseOptions.Value.AutoBuyThreshold, purchaseOptions.Value.MinimumRemainingCoins, accountKeyHash,
            quote.EligibleLockUrls, quote.EligibleTotal
        }))));
        return new() {TaskId = row.Id, Revision = row.Revision, WorkflowRunId = run.Id, LockUrls = quote.EligibleLockUrls,
            CurrencyCode = quote.CurrencyCode, CurrencyName = quote.CurrencyName, MaxTotalCost = quote.EligibleTotal, QuoteHash = hash};
    }
}
