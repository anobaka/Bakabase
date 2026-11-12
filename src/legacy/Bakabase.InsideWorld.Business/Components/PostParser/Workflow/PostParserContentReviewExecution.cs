using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Microsoft.EntityFrameworkCore;
using Content = Bakabase.Modules.PostParser.Models.Domain.PostContent;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Workflow;

public sealed record PostParserContentReviewExecution(Content Content, bool CanContinue, List<string> Warnings);

public sealed partial class PostParserWorkflowService<TDbContext>
{
    private PostParserTaskExecutionGate ReviewGate => gate;
    private Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain.SoulPlusOptions ReviewOptions => purchaseOptions.Value;
    public async Task<PostParserContentReviewExecution?> ExecuteContentReviewAsync(PostParserInput input, int runId,
        Content content, string? expectedReviewId, CancellationToken ct)
    {
        if (input.TaskId is not { } id) return null;
        PostParserContentReviewReceipt? review;
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            await EnsureCurrentAsync(input, runId, ct);
            var row = await ParserTasks.AsNoTracking().SingleAsync(t => t.Id == id, ct);
            var state = PostParserContentReadinessState.Read(row.ContentReadinessJson);
            review = state.Reviews.LastOrDefault(r => r.SourceRevision == input.Revision && r.WorkflowRunId == runId);
            if (review == null)
                return expectedReviewId == null ? null : new(content, false, ["POST_CONTENT_REVIEW_CHANGED"]);
            if (expectedReviewId != null && expectedReviewId != review.Id || state.Version != review.DecisionVersion)
                return new(content, !PostContentReadinessPolicy.HasBlockingLocks(content), ["POST_CONTENT_REVIEW_CHANGED"]);
            ResolveUncertainAttempts(state, content);
            if (!TryAdvanceReviewEvidence(state, review, content, out state, out content))
                return new(content, false, ["POST_CONTENT_REVIEW_EVIDENCE_CHANGED"]);
            await PersistReadinessUnderGateAsync(row, state, content, ct);
        }
        finally { gate.Semaphore.Release(); }
        if (!PostContentReadinessPolicy.HasBlockingLocks(content)) return new(content, true, []);
        if (purchaseOptions.Value.AutoBuyThreshold != review.AutoBuyThreshold ||
            purchaseOptions.Value.MinimumRemainingCoins != review.MinimumRemainingCoins)
            return new(content, false, ["POST_CONTENT_REVIEW_LIMITS_CHANGED"]);
        var observer = new ReviewPurchaseObserver(this, input, runId, review.Id);
        var approved = ReviewGroups(content).Where(g => review.Targets.Any(t => t.TargetId == g.Id && t.Action == "buy") &&
                review.Attempts.All(a => a.TargetId != g.Id))
            .Select(g => (Key: SharedContentPurchasePricing.TargetKey(content, g.Blocks[0]),
                Price: review.Targets.Single(t => t.TargetId == g.Id).Price)).Where(g => g.Key != null)
            .ToDictionary(t => t.Key!, t => t.Price, StringComparer.Ordinal);
        var warnings = new List<string>();
        if (approved.Count > 0)
        {
            try
            {
                var result = await new SharedContentPurchasePolicy(contentReader, purchasers).PurchaseAsync(input.Link!,
                    content.SourceHint ?? input.SourceHint, review.AutoBuyThreshold, review.MinimumRemainingCoins, null, ct,
                    enforceThreshold: true, maxTotalCost: Math.Max(0, review.MaxTotalCost - review.Attempts.Sum(a => a.Price)),
                    expectedAccountKeyHash: review.AccountKeyHash, observer: observer, approvedTargetPrices: approved);
                content = result.Content;
                warnings.AddRange(result.Warnings);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // An attempted request remains durable even when the response/read fails. A retry
                // may verify an unlock but cannot repeat that payment under another review ID.
                warnings.Add("POST_CONTENT_REVIEW_PURCHASE_UNCERTAIN");
            }
        }
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            await EnsureCurrentAsync(input, runId, ct);
            var row = await ParserTasks.AsNoTracking().SingleAsync(t => t.Id == id, ct);
            var state = PostParserContentReadinessState.Read(row.ContentReadinessJson);
            review = state.Reviews.Last(r => r.Id == review.Id);
            ResolveUncertainAttempts(state, content);
            var valid = state.Version == review.DecisionVersion && TryAdvanceReviewEvidence(state, review, content, out state, out content);
            if (!valid) warnings.Add("POST_CONTENT_REVIEW_EVIDENCE_CHANGED");
            if (state.Reviews.Any(r => r.Attempts.Any(a => a.Status is "pending" or "uncertain")))
                warnings.Add("POST_CONTENT_REVIEW_PURCHASE_UNCERTAIN");
            await PersistReadinessUnderGateAsync(row, state, content, ct);
            return new(content, valid && !PostContentReadinessPolicy.HasBlockingLocks(content), warnings.Distinct().ToList());
        }
        finally { gate.Semaphore.Release(); }
    }

    private static void ResolveUncertainAttempts(PostParserContentReadinessState state, Content fresh)
    {
        foreach (var receipt in state.Reviews)
        {
            if (receipt.Evidence.SourceHint != fresh.SourceHint || receipt.Evidence.SourceUrl != fresh.SourceUrl) continue;
            foreach (var attempt in receipt.Attempts.Where(a => a.Status is "pending" or "uncertain"))
            {
                if (VerifyAttemptUnlock(receipt, attempt, fresh)) attempt.Status = "unlocked";
            }
        }
    }

    private static Content PurchaseEvidence(Content content) => new()
    {
        SourceHint = content.SourceHint, SourceUrl = content.SourceUrl, Scope = content.Scope,
        SourceEvidenceFingerprint = content.SourceEvidenceFingerprint, ReaderEvidence = content.ReaderEvidence,
        Locks = content.Locks.Select(l => new PostContentLock(null, l.Price, l.IsBought)
            {BlockId = l.BlockId, Floor = l.Floor, PurchaseTargetId = l.PurchaseTargetId, CurrencyCode = l.CurrencyCode}).ToList()
    };

    private static bool VerifyAttemptUnlock(PostParserContentReviewReceipt receipt,
        PostParserContentPurchaseAttempt attempt, Content fresh)
    {
        if (attempt.BeforeEvidence is not { } before) return false;
        var target = receipt.Targets.Single(t => t.TargetId == attempt.TargetId);
        var canonicalTargets = before.ReaderEvidence?.Slots.Where(s => target.BlockIds.Contains(s.BlockId) && !s.IsReadable)
            .Select(s => s.CanonicalTargetId).Distinct(StringComparer.Ordinal).ToList() ?? [];
        return PostContentEvidenceTransition.TryValidateAuthorizedUnlock(before, fresh, canonicalTargets, out _);
    }

    private bool TryAdvanceReviewEvidence(PostParserContentReadinessState state, PostParserContentReviewReceipt receipt,
        Content fresh, out PostParserContentReadinessState updated, out Content resolved)
    {
        updated = state.RecordEquivalent(receipt.Evidence, fresh, evidenceCanonicalizers, out var equivalent);
        resolved = updated.Apply(fresh);
        var previousHash = PostContentReadinessPolicy.Fingerprint(receipt.Evidence);
        var nextHash = PostContentReadinessPolicy.Fingerprint(fresh);
        if (equivalent) { receipt.Evidence = fresh; return true; }
        if (fresh.Locks.All(l => l.IsBought)) { receipt.Evidence = fresh; return true; }
        var attemptedBlocks = receipt.Targets.Where(t => receipt.Attempts.Any(a => a.TargetId == t.TargetId))
            .SelectMany(t => t.BlockIds).ToHashSet(StringComparer.Ordinal);
        var paidTargets = receipt.Evidence.ReaderEvidence?.Slots.Where(s => !s.IsReadable && attemptedBlocks.Contains(s.BlockId))
            .Select(s => s.CanonicalTargetId).Distinct().ToList() ?? [];
        if (state.History.Count >= 1000 ||
            !PostContentEvidenceTransition.TryValidateAuthorizedUnlock(receipt.Evidence, fresh, paidTargets, out var transition)) return false;
        var ignored = receipt.Targets.Where(t => t.Action == "notRequired").SelectMany(t => t.BlockIds)
            .Intersect(transition!.UnchangedBlockIds, StringComparer.Ordinal).ToList();
        var entry = new PostParserContentReadinessEvent {Version = state.Version + 1, ContentFingerprint = nextHash,
            SourceRevision = receipt.SourceRevision, WorkflowRunId = receipt.WorkflowRunId, BlockIds = ignored,
            Reason = "Carried forward the explicit review after its verified purchase revealed only the approved content."};
        updated = state with {History = state.History.Append(entry).ToList()};
        receipt.DecisionVersion = entry.Version;
        receipt.Transitions.Add(new(previousHash, nextHash, ignored, DateTime.UtcNow));
        receipt.Evidence = fresh;
        resolved = updated.Apply(fresh);
        return true;
    }

    private sealed class ReviewPurchaseObserver(PostParserWorkflowService<TDbContext> owner, PostParserInput input,
        int runId, string reviewId) : ISharedContentPurchaseObserver
    {
        public async Task<bool> BeforePurchaseAsync(Content content, PostContentLock offer, CancellationToken ct)
        {
            await owner.ReviewGate.Semaphore.WaitAsync(ct);
            try
            {
                await owner.EnsureCurrentAsync(input, runId, ct);
                var row = await owner.ParserTasks.AsNoTracking().SingleAsync(t => t.Id == input.TaskId, ct);
                var state = PostParserContentReadinessState.Read(row.ContentReadinessJson);
                var receipt = state.Reviews.LastOrDefault(r => r.Id == reviewId);
                var targetId = ReviewHash(SharedContentPurchasePricing.TargetKey(content, offer));
                var target = receipt?.Targets.SingleOrDefault(t => t.Action == "buy" && t.TargetId == targetId);
                if (receipt == null || target == null || state.Version != receipt.DecisionVersion ||
                    state.Reviews.Last(r => r.SourceRevision == input.Revision && r.WorkflowRunId == runId).Id != reviewId ||
                    UncertainTarget(state, target.TargetId) || receipt.Attempts.Any(a => a.TargetId == target.TargetId) ||
                    owner.ReviewOptions.AutoBuyThreshold != receipt.AutoBuyThreshold ||
                    owner.ReviewOptions.MinimumRemainingCoins != receipt.MinimumRemainingCoins ||
                    !owner.TryAdvanceReviewEvidence(state, receipt, content, out state, out content)) return false;
                // This callback runs inside the purchaser's account gate. Keep soft-deleted task
                // journals in the check: another task/review cannot evade an unresolved payment.
                var otherJournals = await owner.ParserTasks.AsNoTracking().Where(t => t.Id != input.TaskId &&
                        t.ContentReadinessJson != null && t.ContentReadinessJson.Contains(receipt.AccountKeyHash) &&
                        t.ContentReadinessJson.Contains(target.TargetId))
                    .Select(t => t.ContentReadinessJson).ToListAsync(ct);
                if (otherJournals.SelectMany(s => PostParserContentReadinessState.Read(s).Reviews)
                    .Any(r => r.AccountKeyHash == receipt.AccountKeyHash && r.Attempts.Any(a =>
                        a.TargetId == target.TargetId && a.Status is "pending" or "uncertain"))) return false;
                receipt.Attempts.Add(new() {TargetId = target.TargetId, Url = offer.Url, Price = offer.Price!.Value,
                    BeforeFingerprint = PostContentReadinessPolicy.Fingerprint(content), BeforeEvidence = PurchaseEvidence(content)});
                await owner.PersistReadinessUnderGateAsync(row, state, content, ct);
                return true;
            }
            finally { owner.ReviewGate.Semaphore.Release(); }
        }

        public async Task AfterPurchaseAsync(Content before, Content after, PostContentLock offer, bool unlocked, CancellationToken ct)
        {
            await owner.ReviewGate.Semaphore.WaitAsync(ct);
            try
            {
                await owner.EnsureCurrentAsync(input, runId, ct);
                var row = await owner.ParserTasks.AsNoTracking().SingleAsync(t => t.Id == input.TaskId, ct);
                var state = PostParserContentReadinessState.Read(row.ContentReadinessJson);
                var receipt = state.Reviews.Single(r => r.Id == reviewId);
                var target = receipt.Targets.Single(t => t.TargetId == ReviewHash(SharedContentPurchasePricing.TargetKey(before, offer)));
                var attempt = receipt.Attempts.Last(a => a.TargetId == target.TargetId);
                attempt.Status = unlocked && VerifyAttemptUnlock(receipt, attempt, after) ? "unlocked" : "uncertain";
                owner.TryAdvanceReviewEvidence(state, receipt, after, out state, out var resolved);
                await owner.PersistReadinessUnderGateAsync(row, state, resolved, ct);
            }
            finally { owner.ReviewGate.Semaphore.Release(); }
        }
    }
}
