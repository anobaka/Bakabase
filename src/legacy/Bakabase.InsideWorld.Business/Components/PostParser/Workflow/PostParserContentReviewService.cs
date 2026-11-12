using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Content = Bakabase.Modules.PostParser.Models.Domain.PostContent;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Workflow;

public sealed partial class PostParserWorkflowService<TDbContext>
{
    private static string ReviewHash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, WorkflowJson.Options))));
    private static string ReviewTargetId(Content content, PostContentLock block, int index) =>
        ReviewHash(SharedContentPurchasePricing.TargetKey(content, block) ?? $"missing:{index}");
    private static List<(string Id, List<PostContentLock> Blocks)> ReviewGroups(Content content) => content.Locks
        .Where(l => !l.IsBought).Select((l, i) => (Lock: l, Key: ReviewTargetId(content, l, i)))
        .GroupBy(v => v.Key, StringComparer.Ordinal).Select(g => (g.Key, g.Select(v => v.Lock).ToList())).ToList();
    private static bool UncertainTarget(PostParserContentReadinessState state, string targetId) => state.Reviews
        .Any(r => r.Attempts.Any(a => a.TargetId == targetId && a.Status is "pending" or "uncertain"));

    internal PostParserContentReviewTask ContentReviewView(PostParserTaskDbModel row, WorkflowRunDbModel? run, PostParserContentReviewScope scope)
    {
        var content = row.ToDomainModel().ContentSnapshot;
        var state = PostParserContentReadinessState.Read(row.ContentReadinessJson);
        PostParserInput? payload = null;
        try { payload = JsonSerializer.Deserialize<PostParserInput>(run?.PayloadJson ?? "null", WorkflowJson.Options); }
        catch (JsonException) { }
        var problem = run == null || run.Status is not (WorkflowRunStatus.Waiting or WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted) ||
            PostParserPurchaseRules.CurrentActivityKind(run) != PostParserWorkflow.UnlockContent ? "notWaitingForPurchase" :
            ReadinessBusy(run) ? "operationBusy" : payload?.TaskId != row.Id || payload.Revision != row.Revision ? "snapshotChanged" :
            content == null ? "noPurchaseQuote" : null;
        var purchaser = purchasers.FirstOrDefault(p => string.Equals(p.Source.ToString(), content?.SourceHint ?? payload?.SourceHint, StringComparison.OrdinalIgnoreCase));
        var accountHash = purchaser == null ? null : SharedContentPurchasePolicy.HashAccountKey(purchaser.AccountKey);
        var threshold = purchaseOptions.Value.AutoBuyThreshold;
        var reserve = purchaseOptions.Value.MinimumRemainingCoins;
        var canonicalPolicy = "";
        var semanticFingerprint = content == null ? null : PostContentReadinessPolicy.Fingerprint(content);
        if (content != null && PostContentEvidenceEquivalence.TryCanonicalWholeFingerprint(content, evidenceCanonicalizers,
                out var policyId, out var canonicalFingerprint))
        { canonicalPolicy = policyId; semanticFingerprint = canonicalFingerprint; }
        var quoteAvailability = row.ToDomainModel().Availability;
        var targets = new List<PostParserContentReviewTarget>();
        if (content != null)
        {
            var stable = PostParserContentReadinessState.HasStableBlockIds(content);
            var availability = PostAvailabilityEvidencePolicy.Apply(row.ToDomainModel().Availability)?.Status;
            var blocking = PostContentReadinessPolicy.BlockingLocks(content).ToHashSet();
            foreach (var (id, blocks) in ReviewGroups(content))
            {
                var price = SharedContentPurchasePricing.Quote(content, blocks);
                var buyProblem = UncertainTarget(state, id) ? "purchaseUncertain" : purchaser == null ? "sourceUnsupported" :
                    string.IsNullOrWhiteSpace(blocks[0].Url) ? "noPurchaseQuote" : price is null or < 0 ? "priceUnknown" :
                    threshold < 0 || price > threshold ? "overPurchaseLimit" :
                    price > 0 && content.Balance == null ? "balanceUnknown" :
                    reserve < 0 || price > 0 && content.Balance - price < reserve ? "insufficientBalance" : null;
                var recommended = price == 0 && buyProblem == null ? "buy" :
                    price is null or < 0 || availability is not ("noExpiryReported" or "restored") ? "defer" :
                    stable && (price > threshold || blocks.All(l => !blocking.Contains(l))) ? "notRequired" :
                    buyProblem == null ? "buy" : "defer";
                targets.Add(new() {TargetId = id, Floor = blocks[0].Floor, Blocks = blocks,
                    BlockIds = blocks.Where(b => b.BlockId != null).Select(b => b.BlockId!).ToList(), Price = price,
                    CurrencyCode = content.CurrencyCode, CurrencyName = content.CurrencyName,
                    CanBuy = buyProblem == null, CanMarkNotRequired = stable, BuyBlockReason = buyProblem, DefaultAction = recommended});
            }
        }
        return new() {TaskId = row.Id, Source = row.Source, Title = row.Title ?? content?.Title, Link = row.Link,
            Revision = row.Revision, WorkflowRunId = row.WorkflowRunId, DecisionVersion = state.Version,
            ContentFingerprint = content == null ? null : PostContentReadinessPolicy.Fingerprint(content),
            QuoteHash = ReviewHash(new {scope, row.Id, row.Revision, row.WorkflowRunId, row.Source, row.Link,
                canonicalPolicy, semanticFingerprint, content?.Balance, content?.CurrencyCode, content?.CurrencyName,
                state.Version, state.History, state.ParsedResultMayBeStale,
                Reviews = state.Reviews.Select(r => new {r.Id, r.RequestId, r.RequestHash, r.SourceRevision, r.WorkflowRunId,
                    r.DecisionVersion, r.ContentFingerprint, r.AccountKeyHash, r.AutoBuyThreshold, r.MinimumRemainingCoins,
                    r.MaxTotalCost, r.ConfirmationOutcome, r.Targets, r.Attempts}),
                Readiness = content?.Readiness == null ? null : new {content.Readiness.Basis, content.Readiness.PolicyVersion, content.Readiness.Decisions},
                Availability = quoteAvailability == null ? null : quoteAvailability with {ContentReadiness = null},
                run?.Status, run?.CurrentStepIndex, run?.CurrentItemJson, run?.PlanSnapshotJson,
                threshold, reserve, accountHash}), CanSubmit = problem == null, BlockReason = problem,
            AlreadyUnlocked = content != null && content.Locks.All(l => l.IsBought), Targets = targets};
    }

    internal async Task<PostParserContentReviewOutcome> ConfirmContentReviewUnderGateAsync(PostParserTaskDbModel row,
        WorkflowRunDbModel? run, PostParserContentReviewScope scope, PostParserContentReviewSelection selection,
        string requestId, string requestHash, CancellationToken ct)
    {
        var view = ContentReviewView(row, run, scope);
        if (!view.CanSubmit) return new(row.Id, "skipped", row.WorkflowRunId, Reason: view.BlockReason);
        var persistedState = PostParserContentReadinessState.Read(row.ContentReadinessJson);
        if (view.DecisionVersion != selection.DecisionVersion ||
            !persistedState.MatchesFingerprint(selection.ContentFingerprint, view.ContentFingerprint ?? "") || view.QuoteHash != selection.QuoteHash)
            return new(row.Id, "skipped", row.WorkflowRunId, Reason: "quoteChanged");
        if (!view.Targets.Select(t => t.TargetId).ToHashSet(StringComparer.Ordinal).SetEquals(selection.Targets.Select(t => t.TargetId)))
            return new(row.Id, "skipped", row.WorkflowRunId, Reason: "targetSelectionChanged");
        var actions = selection.Targets.ToDictionary(t => t.TargetId, t => t.Action, StringComparer.Ordinal);
        if (view.Targets.Any(t => actions[t.TargetId] == "buy" && !t.CanBuy || actions[t.TargetId] == "notRequired" && !t.CanMarkNotRequired))
            return new(row.Id, "skipped", row.WorkflowRunId, Reason: "actionUnavailable");
        var content = row.ToDomainModel().ContentSnapshot!;
        var state = PostParserContentReadinessState.Read(row.ContentReadinessJson);
        if (state.Reviews.Count >= 500 || state.History.Count >= 1000)
            return new(row.Id, "skipped", row.WorkflowRunId, Reason: "historyFull");
        var buyTargets = view.Targets.Where(t => actions[t.TargetId] == "buy").ToList();
        var total = buyTargets.Sum(t => t.Price!.Value);
        if (total > 0 && (content.Balance == null || content.Balance - total < purchaseOptions.Value.MinimumRemainingCoins))
            return new(row.Id, "skipped", row.WorkflowRunId, Reason: "insufficientBalance");
        var purchaser = purchasers.FirstOrDefault(p => string.Equals(p.Source.ToString(), content.SourceHint, StringComparison.OrdinalIgnoreCase));
        var receipt = new PostParserContentReviewReceipt {RequestId = requestId, RequestHash = requestHash,
            SourceRevision = row.Revision, WorkflowRunId = run!.Id, ContentFingerprint = view.ContentFingerprint!, Evidence = content,
            AccountKeyHash = purchaser == null ? "" : SharedContentPurchasePolicy.HashAccountKey(purchaser.AccountKey),
            AutoBuyThreshold = purchaseOptions.Value.AutoBuyThreshold, MinimumRemainingCoins = purchaseOptions.Value.MinimumRemainingCoins,
            MaxTotalCost = total, Targets = view.Targets.Select(t => new PostParserContentReviewApprovedTarget
            {TargetId = t.TargetId, Action = actions[t.TargetId], Url = t.Blocks[0].Url, Price = t.Price, BlockIds = t.BlockIds,
                ReaderTargetIds = content.ReaderEvidence?.Slots.Where(s => t.BlockIds.Contains(s.BlockId)).Select(s => s.CanonicalTargetId).Distinct().ToList() ?? []}).ToList()};
        var allDefer = receipt.Targets.Count > 0 && receipt.Targets.All(t => t.Action == "defer");
        var entry = new PostParserContentReadinessEvent {Version = state.Version + 1,
            ContentFingerprint = view.ContentFingerprint!, SourceRevision = row.Revision, WorkflowRunId = row.WorkflowRunId,
            BlockIds = receipt.Targets.Where(t => t.Action == "notRequired").SelectMany(t => t.BlockIds).Distinct().ToList(),
            Reason = "Explicit content review: selected restricted blocks are not required."};
        receipt.DecisionVersion = allDefer ? state.Version : entry.Version;
        receipt.ConfirmationOutcome = allDefer ? "unchanged" : "prepared";
        state = state with {Reviews = state.Reviews.Append(receipt).ToList(), History = allDefer ? state.History : state.History.Append(entry).ToList()};
        content = state.Apply(content);
        await PersistReadinessUnderGateAsync(row, state, content, ct);
        if (allDefer) return new(row.Id, "unchanged", run.Id, receipt.Id);
        try
        {
            // Only this task's existing frozen unlock node resumes. No purchase happens in this endpoint.
            run.CurrentItemJson = WorkflowItemSnapshot.Capture(new PostParserContentItem(
                JsonSerializer.Deserialize<PostParserInput>(run.PayloadJson!, WorkflowJson.Options)!, content)
                {Availability = row.ToDomainModel().Availability});
            run.Status = WorkflowRunStatus.Waiting; run.CompletedAt = null; run.ErrorMessage = null;
            await db.SaveChangesAsync(ct);
            await resumer.ResumeAsync(run.Id, JsonSerializer.Serialize(new PostParserPurchaseSignal {ContentReviewId = receipt.Id}, WorkflowJson.Options), ct);
            receipt.ConfirmationOutcome = "queued";
            await PersistReadinessUnderGateAsync(row, state, content, ct);
            await PublishAsync(row, WorkflowRunStatus.Pending);
            return new(row.Id, "queued", run.Id, receipt.Id);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            receipt.ConfirmationOutcome = "failed"; receipt.Problem = "queueFailed";
            await PersistReadinessUnderGateAsync(row, state, content, ct);
            return new(row.Id, "failed", run.Id, receipt.Id, "queueFailed");
        }
    }
}
