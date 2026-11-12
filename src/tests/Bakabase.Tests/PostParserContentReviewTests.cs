using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Content = Bakabase.Modules.PostParser.Models.Domain.PostContent;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostParserContentReviewTests
{
    private IServiceProvider _services = null!;
    private readonly Site _site = new();
    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider(s =>
        {
            s.RemoveAll<ISharedContentPurchaser>();
            s.AddSingleton<ISharedContentPurchaser>(_site);
            s.AddSingleton<IPostContentService>(_site);
            s.AddSingleton<IPostContentEvidenceCanonicalizer, PostContentEvidenceEquivalenceTests.FixtureCanonicalizer>();
        });
        var config = _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value;
        config.AutoBuyThreshold = 10; config.MinimumRemainingCoins = 1000;
    }
    private static PostContentLock Lock(string id, decimal? price) => new("https://example.test/buy/" + id, price, false)
        {BlockId = id, Floor = "0", PurchaseTargetId = "target:" + id, CurrencyCode = "SP", ContextText = "context " + id};
    private static Content Evidence(List<PostContentLock> locks, string outside = "A")
    {
        var fingerprint = new string(locks.Any(l => l.IsBought) ? 'B' : 'A', 64);
        return new() {SourceHint = "SoulPlus", SourceUrl = "https://example.test/post", Balance = 2000, CurrencyCode = "SP", CurrencyName = "SP币",
            MainHtml = "source", Locks = locks, SourceEvidenceFingerprint = fingerprint,
            ReaderEvidence = new() {ReaderId = "fixture", SourceIdentity = "post:fixture", ContentFingerprint = fingerprint,
                OutsideSlotsDigest = string.Concat(Enumerable.Repeat(outside, 64)), Slots = locks.Select(l => new PostContentEvidenceSlot
                {BlockId = l.BlockId!, OwnerId = "post", Floor = "0", CanonicalTargetId = l.PurchaseTargetId!, PurchaseTargetId = l.PurchaseTargetId,
                    Price = l.Price, CurrencyCode = l.CurrencyCode, IsReadable = l.IsBought, MetadataDigest = new string('C', 64),
                    ContentDigest = new string(l.IsBought ? 'D' : 'E', 64)}).ToList()}};
    }
    private async Task<PostParserTaskDbModel> Add(Content? content = null, string availability = "noExpiryReported")
    {
        _site.Content = content ?? Evidence([Lock("cheap", 1), Lock("expensive", 20)]);
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var row = new PostParserTaskDbModel {Link = _site.Content.SourceUrl!, Title = "Review fixture", Source = PostParserSource.SoulPlus,
            Revision = 2, Targets = "[1]", ParsingState = "awaitingPurchase", ContentSnapshotJson = JsonSerializer.Serialize(_site.Content, WorkflowJson.Options),
            AvailabilityJson = JsonSerializer.Serialize(new PostAvailabilityAssessment {Status = availability}, WorkflowJson.Options)};
        db.PostParserTasks.Add(row); await db.SaveChangesAsync();
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Waiting, StartedAt = DateTime.Now, CurrentStepIndex = 1,
            PayloadJson = JsonSerializer.Serialize(new PostParserInput {TaskId = row.Id, Revision = row.Revision, Link = row.Link, SourceHint = "SoulPlus"}, WorkflowJson.Options),
            CurrentItemJson = "{}", WaitReason = "postParser.awaitingPurchase",
            PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Review fixture", TriggerKind = PostParserWorkflow.Trigger,
                Nodes = [new() {NodeId = "read", Kind = PostParserWorkflow.ReadContent}, new() {NodeId = "unlock", Kind = PostParserWorkflow.UnlockContent}]})};
        db.WorkflowRuns.Add(run); await db.SaveChangesAsync(); row.WorkflowRunId = run.Id; await db.SaveChangesAsync(); return row;
    }
    private async Task<PostParserContentReviewPreview> Preview(params int[] ids)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().PreviewContentReview(new() {Status = "all", TaskIds = ids.ToList()});
    }
    private static ConfirmPostParserContentReviewInput Choose(PostParserContentReviewPreview preview, Func<PostParserContentReviewTarget, string>? action = null) => new()
    {
        RequestId = Guid.NewGuid().ToString(), Scope = preview.Scope,
        Tasks = preview.Tasks.Select(t => new PostParserContentReviewSelection {TaskId = t.TaskId, Revision = t.Revision, WorkflowRunId = t.WorkflowRunId!.Value,
            DecisionVersion = t.DecisionVersion, ContentFingerprint = t.ContentFingerprint!, QuoteHash = t.QuoteHash,
            Targets = t.Targets.Select(p => new PostParserContentReviewAction(p.TargetId, action?.Invoke(p) ?? p.DefaultAction)).ToList()}).ToList()
    };
    private async Task<PostParserContentReviewResult> Confirm(ConfirmPostParserContentReviewInput input)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().ConfirmContentReview(input);
    }
    private async Task<PostParserTaskDbModel> Read(int id)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == id);
    }
    private async Task<PostParserContentReviewExecution?> Execute(PostParserTaskDbModel row)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>().ExecuteContentReviewAsync(
            new() {TaskId = row.Id, Revision = row.Revision, Link = row.Link, SourceHint = "SoulPlus"}, row.WorkflowRunId!.Value, _site.Content, null, CancellationToken.None);
    }
    [TestMethod]
    public async Task PreviewDefaultsAndExactTaskScopeDoNotWriteOrPurchase()
    {
        var row = await Add(); await Add();
        var preview = await Preview(row.Id);
        Assert.AreEqual(1, preview.MatchedCount);
        CollectionAssert.AreEqual(new[] {"buy", "notRequired"}, preview.Tasks.Single().Targets.Select(t => t.DefaultAction).ToArray());
        Assert.IsFalse(preview.Tasks.Single().Targets[1].CanBuy);
        Assert.AreEqual("20", preview.Tasks.Single().Targets[1].PriceText);
        Assert.AreEqual(row.ContentSnapshotJson, (await Read(row.Id)).ContentSnapshotJson);
        Assert.AreEqual(0, _site.Buys);
    }
    [TestMethod]
    public async Task UnknownAvailabilityOrPriceDefersButVerifiedFreeDefaultsToBuy()
    {
        var row = await Add(Evidence([Lock("free", 0), Lock("paid", 20), Lock("unknown", null)]), "unknown");
        CollectionAssert.AreEqual(new[] {"buy", "defer", "defer"}, (await Preview(row.Id)).Tasks.Single().Targets.Select(t => t.DefaultAction).ToArray());
    }
    [TestMethod]
    public async Task ReplayIsIdempotentAndCannotChangeOrExpandTheBatch()
    {
        var row = await Add(); var other = await Add(); var request = Choose(await Preview(row.Id));
        var first = await Confirm(request); var second = await Confirm(request);
        Assert.AreEqual(1, first.QueuedCount); Assert.AreEqual(first.Items.Single().ReviewId, second.Items.Single().ReviewId);
        Assert.AreEqual(1, PostParserContentReadinessState.Read((await Read(row.Id)).ContentReadinessJson).Reviews.Count);
        var changed = request with {Tasks = request.Tasks.Select(t => t with {Targets = t.Targets.Select(a => a with {Action = "defer"}).ToList()}).ToList()};
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Confirm(changed));
        var expanded = Choose(await Preview(other.Id)) with {RequestId = request.RequestId};
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Confirm(expanded));
        Assert.IsNull((await Read(other.Id)).ContentReadinessJson);
        Assert.AreEqual(0, _site.Buys);
    }
    [TestMethod]
    public async Task AllDeferDoesNotClearExistingManualJudgmentOrQueue()
    {
        var row = await Add();
        var state = new PostParserContentReadinessState {History = [new() {Version = 1,
            ContentFingerprint = PostContentReadinessPolicy.Fingerprint(_site.Content), BlockIds = ["expensive"]}]};
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().PostParserTasks.Where(t => t.Id == row.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ContentReadinessJson, JsonSerializer.Serialize(state, WorkflowJson.Options)));
        var result = await Confirm(Choose(await Preview(row.Id), _ => "defer"));
        var after = PostParserContentReadinessState.Read((await Read(row.Id)).ContentReadinessJson);
        Assert.AreEqual(1, result.UnchangedCount); Assert.AreEqual(1, after.Version);
        CollectionAssert.AreEqual(state.History.Single().BlockIds, after.Active(_site.Content)!.BlockIds);
        Assert.AreEqual(0, _services.GetRequiredService<BTaskManager>().Tasks.Count());
    }
    [TestMethod]
    [DataRow("revision")][DataRow("decision")][DataRow("quote")][DataRow("account")][DataRow("limits")]
    public async Task ChangedApprovalContextSkipsWithoutQueueing(string change)
    {
        var row = await Add(); var request = Choose(await Preview(row.Id));
        await using var scope = _services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        if (change == "revision") await db.PostParserTasks.Where(t => t.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Revision, 3));
        if (change == "decision") await db.PostParserTasks.Where(t => t.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ContentReadinessJson, "{\"history\":[{\"version\":1,\"blockIds\":[]}]}"));
        if (change == "quote") await db.PostParserTasks.Where(t => t.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ContentSnapshotJson, row.ContentSnapshotJson!.Replace("source", "changed")));
        if (change == "account") _site.Account = "other";
        if (change == "limits") _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value.AutoBuyThreshold = 9;
        Assert.AreEqual(1, (await Confirm(request)).SkippedCount); Assert.AreEqual(0, _site.Buys);
    }
    [TestMethod]
    public async Task MixedPurchaseAndIgnoreCarriesOnlyVerifiedUnchangedBlocksWithoutASignal()
    {
        var row = await Add(); await Confirm(Choose(await Preview(row.Id)));
        var result = await Execute(row);
        Assert.IsTrue(result!.CanContinue);
        Assert.AreEqual(1, _site.Buys);
        Assert.IsTrue(_site.Content.Locks.Single(l => l.BlockId == "cheap").IsBought);
        Assert.IsFalse(result.Content.Locks.Single(l => l.BlockId == "expensive").IsBought);
        Assert.IsFalse(PostContentReadinessPolicy.HasBlockingLocks(result.Content));
        var state = PostParserContentReadinessState.Read((await Read(row.Id)).ContentReadinessJson);
        Assert.AreEqual(1, state.Reviews.Single().Transitions.Count);
        Assert.AreEqual("unlocked", state.Reviews.Single().Attempts.Single().Status);
        Assert.IsTrue((await Execute(row))!.CanContinue); Assert.AreEqual(1, _site.Buys);
    }
    [TestMethod]
    public async Task DeferredTargetStillBlocksAndIsNeverBoughtOnRetry()
    {
        var row = await Add(Evidence([Lock("cheap", 1), Lock("later", 2)]));
        await Confirm(Choose(await Preview(row.Id), t => t.BlockIds.Contains("later") ? "defer" : "buy"));
        Assert.IsFalse((await Execute(row))!.CanContinue); Assert.AreEqual(1, _site.Buys);
        Assert.IsFalse((await Execute(row))!.CanContinue); Assert.AreEqual(1, _site.Buys);
    }
    [TestMethod]
    public async Task ChangedOutsideEvidenceNeverCarriesTheIgnoreDecision()
    {
        var row = await Add(); await Confirm(Choose(await Preview(row.Id))); _site.ChangeOutside = true;
        var result = await Execute(row);
        Assert.IsFalse(result!.CanContinue); Assert.IsTrue(PostContentReadinessPolicy.HasBlockingLocks(result.Content));
        Assert.AreEqual(1, _site.Buys);
    }
    [TestMethod]
    public async Task AmbiguousPaymentCannotBeRepeatedEvenUnderANewReview()
    {
        var row = await Add(Evidence([Lock("cheap", 1)])); await Confirm(Choose(await Preview(row.Id)));
        _site.FailBuy = true;
        Assert.IsFalse((await Execute(row))!.CanContinue); Assert.AreEqual(1, _site.Buys);
        Assert.IsFalse((await Execute(row))!.CanContinue); Assert.AreEqual(1, _site.Buys);
        var preview = await Preview(row.Id);
        Assert.AreEqual("purchaseUncertain", preview.Tasks.Single().Targets.Single().BuyBlockReason);
        Assert.IsFalse(preview.Tasks.Single().Targets.Single().CanBuy);
        var next = await Confirm(Choose(preview, _ => "buy"));
        Assert.AreEqual(1, next.SkippedCount); Assert.AreEqual(1, _site.Buys);
        _site.Content = Evidence(_site.Content.Locks.Select(l => l with {IsBought = true}).ToList());
        Assert.IsTrue((await Execute(row))!.CanContinue); Assert.AreEqual(1, _site.Buys);
    }
    [TestMethod]
    public async Task AlreadyUnlockedTaskCanContinueWithNoTargetsAndNoPurchase()
    {
        var row = await Add(Evidence([Lock("done", 1) with {IsBought = true}]));
        var preview = await Preview(row.Id);
        Assert.IsTrue(preview.Tasks.Single().AlreadyUnlocked); Assert.AreEqual(0, preview.Tasks.Single().Targets.Count);
        Assert.AreEqual(1, (await Confirm(Choose(preview))).QueuedCount);
        Assert.IsTrue((await Execute(row))!.CanContinue); Assert.AreEqual(0, _site.Buys);
    }
    [TestMethod]
    public async Task SuccessfulPaymentThenReadFailureIsVerifiedOnRetryWithoutRepaying()
    {
        var row = await Add(Evidence([Lock("cheap", 1)])); await Confirm(Choose(await Preview(row.Id)));
        _site.FailReadAfterBuy = true;
        await Execute(row);
        Assert.AreEqual(1, _site.Buys);
        var state = PostParserContentReadinessState.Read((await Read(row.Id)).ContentReadinessJson);
        Assert.AreEqual("pending", state.Reviews.Single().Attempts.Single().Status);
        _site.FailReadAfterBuy = false;
        Assert.IsTrue((await Execute(row))!.CanContinue);
        Assert.AreEqual(1, _site.Buys);
        Assert.AreEqual("unlocked", PostParserContentReadinessState.Read((await Read(row.Id)).ContentReadinessJson)
            .Reviews.Single().Attempts.Single().Status);
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SameBlockIdInAnotherOwnerNeverClearsAnUncertainPayment(bool failedResponse)
    {
        var row = await Add(Evidence([Lock("cheap", 1)])); await Confirm(Choose(await Preview(row.Id)));
        _site.FailBuy = failedResponse; _site.ChangeOwner = !failedResponse;
        await Execute(row);
        if (failedResponse)
        {
            _site.Content = Evidence([Lock("cheap", 1) with {IsBought = true}]);
            _site.Content = _site.Content with {ReaderEvidence = _site.Content.ReaderEvidence! with
                {Slots = _site.Content.ReaderEvidence.Slots.Select(s => s with {OwnerId = "another-owner"}).ToList()}};
            await Execute(row);
        }
        var attempt = PostParserContentReadinessState.Read((await Read(row.Id)).ContentReadinessJson).Reviews.Single().Attempts.Single();
        Assert.AreEqual(failedResponse ? "pending" : "uncertain", attempt.Status);
        Assert.AreEqual(1, _site.Buys);
        // Readable content may continue, but that is not proof that this payment succeeded.
        await Execute(row);
        Assert.AreEqual(attempt.Status, PostParserContentReadinessState.Read((await Read(row.Id)).ContentReadinessJson)
            .Reviews.Single().Attempts.Single().Status);
    }
    [TestMethod]
    public async Task AnotherTaskCannotRepayAnUncertainTargetEvenAfterTheFirstTaskWasDeleted()
    {
        var first = await Add(Evidence([Lock("cheap", 1)]));
        var second = await Add(Evidence([Lock("cheap", 1)]));
        await Confirm(Choose(await Preview(first.Id))); await Confirm(Choose(await Preview(second.Id)));
        _site.FailBuy = true; await Execute(first); Assert.AreEqual(1, _site.Buys);
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().PostParserTasks.Where(t => t.Id == first.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true));
        _site.FailBuy = false;
        Assert.IsFalse((await Execute(second))!.CanContinue);
        Assert.AreEqual(1, _site.Buys);
        Assert.AreEqual(0, PostParserContentReadinessState.Read((await Read(second.Id)).ContentReadinessJson)
            .Reviews.Single().Attempts.Count);
    }
    [TestMethod]
    public async Task OldSnapshotCanBindFreshSlotEvidenceWithoutChangingApprovalFingerprint()
    {
        var fresh = Evidence([Lock("cheap", 1), Lock("expensive", 20)]);
        var row = await Add(fresh with {ReaderEvidence = null}); await Confirm(Choose(await Preview(row.Id)));
        _site.Content = fresh;
        Assert.IsTrue((await Execute(row))!.CanContinue);
        Assert.AreEqual(1, _site.Buys);
        Assert.AreEqual(1, PostParserContentReadinessState.Read((await Read(row.Id)).ContentReadinessJson)
            .Reviews.Single().Transitions.Count);
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FreshNonceUsesTheApprovedCanonicalTargetButAnotherReplyCannotBePurchased(bool changeReply)
    {
        var locked = Lock("cheap", 1) with {Url = "https://www.north-plus.net/job.php?action=buytopic&tid=42&pid=tpc&verify=old"};
        var row = await Add(Evidence([locked]) with {SourceUrl = "https://www.north-plus.net/read.php?tid-42.html"});
        await Confirm(Choose(await Preview(row.Id)));
        _site.RotateNonceOnRead = true;
        if (changeReply) _site.Content = _site.Content with
            {Locks = _site.Content.Locks.Select(l => l with {Url = l.Url!.Replace("pid=tpc", "pid=102")}).ToList()};
        var result = await Execute(row);
        Assert.AreEqual(changeReply ? 0 : 1, _site.Buys);
        if (!changeReply)
        {
            Assert.IsTrue(result!.CanContinue);
            StringAssert.Contains(_site.LastPurchasedUrl!, "verify=nonce2");
            var receipt = PostParserContentReadinessState.Read((await Read(row.Id)).ContentReadinessJson).Reviews.Single();
            StringAssert.Contains(receipt.Targets.Single().Url!, "verify=old");
            StringAssert.Contains(receipt.Attempts.Single().Url!, "verify=nonce2");
        }
    }
    [TestMethod]
    public async Task VolatileRefreshPreservesLegacyManualHistoryResultsAndWaitingCheckpoint()
    {
        var original = PostContentEvidenceEquivalenceTests.Count(Evidence([Lock("proof", 20)]), 1);
        var row = await Add(original);
        var history = new PostParserContentReadinessEvent {Version = 1, ContentFingerprint = PostContentReadinessPolicy.Fingerprint(original),
            SourceRevision = row.Revision, WorkflowRunId = row.WorkflowRunId, BlockIds = ["proof"]};
        var state = new PostParserContentReadinessState {History = [history]};
        var results = JsonSerializer.Serialize(new Dictionary<string, PostDownloadInfo> {{"DownloadInfo", new()
            {IsComplete = true, ContentReadiness = state.Apply(original).Readiness, Resources = [new() {Link = "https://example.test/file"}]}}}, WorkflowJson.Options);
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().PostParserTasks.Where(t => t.Id == row.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ContentReadinessJson, JsonSerializer.Serialize(state, WorkflowJson.Options))
                    .SetProperty(t => t.Results, results).SetProperty(t => t.ParsingState, "complete"));
        _site.Content = PostContentEvidenceEquivalenceTests.Count(original, 25);
        await using var services = _services.CreateAsyncScope();
        var workflow = services.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>();
        var oldView = await workflow.GetContentReadinessAsync(row.Id);
        var refreshed = await workflow.RefreshContentReadinessAsync(row.Id, new()
        {Revision = row.Revision, WorkflowRunId = row.WorkflowRunId, ExpectedDecisionVersion = oldView.DecisionVersion,
            ExpectedContentFingerprint = oldView.ContentFingerprint!});
        Assert.AreEqual("sufficient", refreshed.Status); Assert.AreEqual(history.Id, refreshed.ActiveDecisionId);
        Assert.AreEqual(1, refreshed.DecisionVersion); Assert.IsFalse(refreshed.ParsedResultMayBeStale);
        Assert.AreEqual(1, refreshed.EvidenceEquivalences.Count);
        Assert.AreEqual(JsonSerializer.Serialize(history, WorkflowJson.Options), JsonSerializer.Serialize(refreshed.History.Single(), WorkflowJson.Options));
        var saved = await Read(row.Id);
        Assert.AreEqual(results, saved.Results); Assert.IsTrue(PostParserSavedResults.HasCompletedDownloadResult(saved));
        var proof = PostParserSavedResults.ReadDownloadInfo(saved).ContentReadiness!;
        Assert.AreEqual(refreshed.ContentFingerprint, proof.Fingerprint);
        var run = await services.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == row.WorkflowRunId);
        Assert.AreEqual(WorkflowRunStatus.Waiting, run.Status); Assert.AreEqual("{}", run.CurrentItemJson);
        Assert.AreEqual(0, _site.Buys); Assert.AreEqual(1, _site.Reads);
        await workflow.RevokeContentReadinessAsync(row.Id, new() {Revision = row.Revision, WorkflowRunId = row.WorkflowRunId,
            ExpectedDecisionVersion = refreshed.DecisionVersion, ExpectedContentFingerprint = refreshed.ContentFingerprint!, DecisionId = history.Id});
        Assert.IsFalse(PostParserSavedResults.HasCompletedDownloadResult(await Read(row.Id)));
        Assert.AreEqual(results, (await Read(row.Id)).Results);
    }
    [TestMethod]
    public async Task VolatileChangeKeepsReceiptApprovalAndPendingPaymentInsteadOfRepurchasing()
    {
        var original = PostContentEvidenceEquivalenceTests.Count(Evidence([Lock("cheap", 1)]), 1);
        var row = await Add(original); await Confirm(Choose(await Preview(row.Id)));
        _site.FailBuy = true; await Execute(row);
        var before = PostParserContentReadinessState.Read((await Read(row.Id)).ContentReadinessJson);
        var receipt = before.Reviews.Single(); var attempt = receipt.Attempts.Single();
        _site.Content = PostContentEvidenceEquivalenceTests.Count(original, 99);
        Assert.IsFalse((await Execute(row))!.CanContinue);
        var after = PostParserContentReadinessState.Read((await Read(row.Id)).ContentReadinessJson);
        Assert.AreEqual(1, _site.Buys); Assert.AreEqual(receipt.ContentFingerprint, after.Reviews.Single().ContentFingerprint);
        Assert.AreEqual(receipt.Targets.Single().Url, after.Reviews.Single().Targets.Single().Url);
        Assert.AreEqual(receipt.MaxTotalCost, after.Reviews.Single().MaxTotalCost);
        Assert.AreEqual(attempt.BeforeFingerprint, after.Reviews.Single().Attempts.Single().BeforeFingerprint);
        Assert.AreEqual("pending", after.Reviews.Single().Attempts.Single().Status);
        Assert.AreEqual(JsonSerializer.Serialize(before.History, WorkflowJson.Options), JsonSerializer.Serialize(after.History, WorkflowJson.Options));
        Assert.AreEqual(1, after.EvidenceEquivalences.Count);
    }
    [TestMethod]
    public async Task OpenReviewSurvivesOnlyServerProvenVolatileRefreshAndStillRejectsPriceChanges()
    {
        var original = PostContentEvidenceEquivalenceTests.Count(Evidence([Lock("cheap", 1)]), 1);
        var row = await Add(original); var request = Choose(await Preview(row.Id));
        _site.Content = PostContentEvidenceEquivalenceTests.Count(original, 25);
        await using var scope = _services.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>();
        await workflow.RefreshContentReadinessAsync(row.Id, new() {Revision = row.Revision, WorkflowRunId = row.WorkflowRunId,
            ExpectedDecisionVersion = 0, ExpectedContentFingerprint = request.Tasks.Single().ContentFingerprint});
        Assert.AreEqual(request.Tasks.Single().QuoteHash, (await Preview(row.Id)).Tasks.Single().QuoteHash);
        Assert.AreEqual(1, (await Confirm(request)).QueuedCount);

        var changed = await Add(original); var oldRequest = Choose(await Preview(changed.Id));
        _site.Content = PostContentEvidenceEquivalenceTests.Count(original, 25) with {Locks = [Lock("cheap", 2)]};
        await workflow.RefreshContentReadinessAsync(changed.Id, new() {Revision = changed.Revision, WorkflowRunId = changed.WorkflowRunId,
            ExpectedDecisionVersion = 0, ExpectedContentFingerprint = oldRequest.Tasks.Single().ContentFingerprint});
        Assert.AreEqual(1, (await Confirm(oldRequest)).SkippedCount); Assert.AreEqual(0, _site.Buys);
    }
    private sealed class Site : IPostContentService, ISharedContentPurchaser
    {
        public Content Content = new(); public int Buys; public bool FailBuy; public bool ChangeOutside; public bool FailReadAfterBuy;
        public bool RotateNonceOnRead; public bool ChangeOwner; public int Reads; public string? LastPurchasedUrl;
        public string Account = "fixture"; public string AccountKey => Account; public PostParserSource Source => PostParserSource.SoulPlus;
        public bool CanRead(string reference, string? hint = null) => true;
        public Task<Content> ReadAsync(string reference, string? hint = null, CancellationToken ct = default)
        {
            Reads++;
            if (FailReadAfterBuy && Buys > 0) throw new InvalidOperationException("Fixture read failure after purchase");
            if (RotateNonceOnRead) Content = Content with {Locks = Content.Locks.Select(l => l with
                {Url = System.Text.RegularExpressions.Regex.Replace(l.Url!, "verify=[^&]+", "verify=nonce" + Reads)}).ToList()};
            return Task.FromResult(Content);
        }
        public Task BuyAsync(string url, CancellationToken ct)
        {
            Buys++; LastPurchasedUrl = url; if (FailBuy) throw new InvalidOperationException("Ambiguous fixture transport failure");
            Content = Evidence(Content.Locks.Select(l => l.Url == url ? l with {IsBought = true} : l).ToList(), ChangeOutside ? "F" : "A") with {SourceUrl = Content.SourceUrl};
            if (ChangeOwner) Content = Content with {ReaderEvidence = Content.ReaderEvidence! with
                {Slots = Content.ReaderEvidence.Slots.Select(s => s with {OwnerId = "another-owner"}).ToList()}};
            return Task.CompletedTask;
        }
    }
}
