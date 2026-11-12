using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.PostParser.Models.Domain;
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
public sealed class PostParserAttentionPurchaseTests
{
    private IServiceProvider _services = null!;
    private readonly Purchaser _purchaser = new();

    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider(s =>
        {
            s.RemoveAll<ISharedContentPurchaser>();
            s.AddSingleton<ISharedContentPurchaser>(_purchaser);
        });
        var options = _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value;
        options.AutoBuyThreshold = 10; options.MinimumRemainingCoins = 1000;
    }

    private async Task<PostParserTaskDbModel> Add(decimal? price = 5, decimal? balance = 2000,
        string availability = "unknown", string[]? evidence = null, string title = "matching",
        string kind = PostParserWorkflow.UnlockContent, string source = "SoulPlus")
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var row = new PostParserTaskDbModel {Link = "https://example.test/post/" + Guid.NewGuid(), Title = title,
            Source = PostParserSource.SoulPlus, Revision = 3, Targets = "[1]", ParsingState = "awaitingPurchase",
            ContentSnapshotJson = JsonSerializer.Serialize(new Content {SourceHint = source, Balance = balance,
                MainHtml = "preserved source", Locks = [new("https://example.test/lock/" + Guid.NewGuid(), price, false)]}, WorkflowJson.Options),
            AvailabilityJson = JsonSerializer.Serialize(new PostAvailabilityAssessment {Status = availability, Evidence = evidence?.ToList() ?? []}, WorkflowJson.Options)};
        db.PostParserTasks.Add(row); await db.SaveChangesAsync();
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Waiting, StartedAt = DateTime.Now,
            CurrentStepIndex = 1, WaitReason = "postParser.awaitingPurchase", WaitPromptJson = "{\"saved\":true}",
            PayloadJson = JsonSerializer.Serialize(new PostParserInput {TaskId = row.Id, Revision = row.Revision, Link = row.Link, SourceHint = source}, WorkflowJson.Options),
            PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Frozen parsing", TriggerKind = PostParserWorkflow.Trigger,
                Nodes = [new() {NodeId = "read", Kind = PostParserWorkflow.ReadContent}, new() {NodeId = "unlock", Kind = kind}]})};
        db.WorkflowRuns.Add(run); await db.SaveChangesAsync();
        row.WorkflowRunId = run.Id; await db.SaveChangesAsync();
        return row;
    }

    private async Task<PostParserTaskPurchasePreview> Preview(PostParserTaskFilterScope? input = null)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().PreviewPurchases(input ?? new() {Status = "attention"});
    }

    private async Task<PostParserTaskPurchaseResult> Confirm(PostParserTaskPurchasePreview input)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().ConfirmPurchases(new() {Scope = input.Scope, Tasks = input.Tasks});
    }

    [TestMethod]
    public async Task ValidatedReadinessProjectsSameBlockingReasonsInPagedAndFullRows()
    {
        var row = await Add(price: 999);
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var saved = await db.PostParserTasks.SingleAsync(t => t.Id == row.Id);
        var content = JsonSerializer.Deserialize<Content>(saved.ContentSnapshotJson!, WorkflowJson.Options)!;
        content = content with {Locks = content.Locks.Select(l => l with {BlockId = "proof", ContextText = "purchase proof"}).ToList()};
        content = Bakabase.Modules.PostParser.Services.PostContentReadinessPolicy.Apply(content,
            Bakabase.Modules.PostParser.Services.PostContentReadinessPolicy.FromAnalysis(content,
                [new() {BlockId = "proof", Relevance = "optional", Purpose = "purchaseProof", Evidence = ["purchase proof"]}]));
        saved.ContentSnapshotJson = JsonSerializer.Serialize(content, WorkflowJson.Options);
        saved.ContentReadinessJson = JsonSerializer.Serialize(new PostParserContentReadinessState().WithProjection(content), WorkflowJson.Options);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IPostParserTaskService>();
        var waiting = await service.Search(new() {Status = "attention", ReasonCode = "purchaseRequired"});
        Assert.AreEqual(0, waiting.TotalCount);
        var all = await service.Search(new());
        CollectionAssert.DoesNotContain(all.Items.Single().AttentionReasonCodes, "purchaseRequired");
        Assert.IsFalse(all.AttentionReasonCounts.ContainsKey("purchaseRequired"));
        saved.ParsingState = "complete";
        saved.Results = JsonSerializer.Serialize(new Dictionary<string, PostDownloadInfo> {{"DownloadInfo", new PostDownloadInfo {ContentReadiness = content.Readiness, Resources = []}}}, WorkflowJson.Options);
        var run = await db.WorkflowRuns.SingleAsync(r => r.Id == row.WorkflowRunId);
        run.Status = WorkflowRunStatus.Success;
        await db.SaveChangesAsync();
        var completed = await service.Search(new() {Status = "success"});
        Assert.AreEqual(1, completed.TotalCount, "Raw unbought auxiliary blocks must not turn a completed task back into purchase attention.");
        Assert.AreEqual(1, completed.StatusCounts["success"]);
        Assert.IsFalse(completed.Items.Single().ContentSnapshot!.Locks.Single().IsBought);
    }

    [TestMethod]
    public async Task AttentionReasonsOverlapAndFilteringCountsShareEffectiveEvidencePolicy()
    {
        var ambiguous = await Add(null, null, "expired", ["老了"]);
        var expired = await Add(15, 1000, "expired", ["链接已经失效"]);
        await Add(title: "unrelated");
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPostParserTaskService>();
        var page = await service.Search(new() {Keyword = "matching", Status = "attention", ReasonCode = "priceUnknown", PageSize = 1});
        Assert.AreEqual(1, page.TotalCount);
        Assert.AreEqual(ambiguous.Id, page.Items.Single().Id);
        CollectionAssert.Contains(page.Items.Single().AttentionReasonCodes, "availabilityUnknown");
        CollectionAssert.DoesNotContain(page.Items.Single().AttentionReasonCodes, "possiblyExpired");
        Assert.AreEqual(1, page.AttentionReasonCounts["possiblyExpired"], "Counts ignore the current reason selection but retain the keyword.");
        Assert.AreEqual(2, page.AttentionReasonCounts["purchaseRequired"]);
        var deletion = await service.PreviewDeletion(new() {Keyword = "matching", Status = "attention", ReasonCode = "overPurchaseLimit"});
        Assert.AreEqual(expired.Id, deletion.Tasks.Single().TaskId);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SourceSpecificQuotesAgreeAcrossPagedReasonsFiltersAndFullRows(bool pascalSnapshot)
    {
        var trusted = await Add(availability: "noExpiryReported");
        var untrusted = await Add(availability: "noExpiryReported");
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        const string sourceUrl = "https://www.north-plus.net/read.php?tid-42.html";
        const string purchaseUrl = "https://www.north-plus.net/job.php?action=buytopic&tid=42&pid=tpc&verify=fixture";
        var content = new Content
        {
            SourceHint = "SoulPlus", SourceUrl = sourceUrl, Balance = 2000,
            Locks = [new(purchaseUrl, 1, false) {Floor = "0"}, new(purchaseUrl, 999, false) {Floor = "0"}]
        };
        var options = new JsonSerializerOptions(WorkflowJson.Options)
            {PropertyNamingPolicy = pascalSnapshot ? null : JsonNamingPolicy.CamelCase};
        var saved = await db.PostParserTasks.Where(t => t.Id == trusted.Id || t.Id == untrusted.Id).ToListAsync();
        saved.Single(t => t.Id == trusted.Id).ContentSnapshotJson = JsonSerializer.Serialize(content, options);
        // The task's SoulPlus source must not override a different saved snapshot identity.
        saved.Single(t => t.Id == untrusted.Id).ContentSnapshotJson =
            JsonSerializer.Serialize(content with {SourceHint = "other"}, options);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IPostParserTaskService>();

        var all = await service.Search(new() {Status = "attention", PageSize = 1});
        Assert.AreEqual(2, all.TotalCount);
        Assert.AreEqual(1, all.AttentionReasonCounts.GetValueOrDefault("priceUnknown"));
        Assert.AreEqual(0, all.AttentionReasonCounts.GetValueOrDefault("overPurchaseLimit"));
        var unknown = await service.Search(new() {Status = "attention", ReasonCode = "priceUnknown"});
        Assert.AreEqual(1, unknown.TotalCount);
        Assert.AreEqual(untrusted.Id, unknown.Items.Single().Id);
        CollectionAssert.Contains(unknown.Items.Single().AttentionReasonCodes, "priceUnknown");
        var priced = await service.Search(new() {Status = "attention", ReasonCode = "purchaseRequired"});
        var trustedRow = priced.Items.Single(t => t.Id == trusted.Id);
        CollectionAssert.DoesNotContain(trustedRow.AttentionReasonCodes, "priceUnknown");
        CollectionAssert.DoesNotContain(trustedRow.AttentionReasonCodes, "overPurchaseLimit");
        Assert.AreEqual(0, (await service.Search(new() {Status = "attention", ReasonCode = "overPurchaseLimit"})).TotalCount);
        var deletion = await service.PreviewDeletion(new() {Status = "attention", ReasonCode = "priceUnknown"});
        Assert.AreEqual(untrusted.Id, deletion.Tasks.Single().TaskId);
    }

    [TestMethod]
    public void MalformedLegacyResultWrapperDoesNotBreakAttentionSerialization()
    {
        foreach (var data in new[] {"1", "[]", "null", "\"legacy\""})
        {
            var task = new PostParserTask {WorkflowStatus = WorkflowRunStatus.Waiting, ParsingState = "partial",
                Results = new() {[PostParseTarget.DownloadInfo] = JsonNode.Parse("{\"data\":" + data + "}")}};
            StringAssert.Contains(JsonSerializer.Serialize(task.AttentionReasonCodes), "incompleteInformation");
        }
    }

    [TestMethod]
    public async Task PreviewCoversWholeScopeWithoutPurchasingAndExplainsUnsafeQuotes()
    {
        for (var i = 0; i < 23; i++) await Add(availability: "noExpiryReported");
        await Add(null); await Add(11); await Add(5, null); await Add(5, 1000);
        await Add(kind: PostParserWorkflow.ExtractDownloadInfo); await Add(source: "unsupported");
        var preview = await Preview();
        Assert.AreEqual(29, preview.MatchedCount); Assert.AreEqual(23, preview.ActionableCount);
        Assert.AreEqual(115m, preview.KnownTotalCost);
        foreach (var code in new[] {"priceUnknown", "overPurchaseLimit", "balanceUnknown", "insufficientBalance", "notWaitingForPurchase", "sourceUnsupported"})
            Assert.AreEqual(1, preview.SkipReasonCounts[code]);
        Assert.AreEqual(0, _purchaser.Calls);
        Assert.AreEqual(0, _services.GetRequiredService<BTaskManager>().Tasks.Count());
    }

    [TestMethod]
    public async Task ConcurrentConfirmationQueuesOnlyFixedRunsOnceAndPreservesSourceAndHistory()
    {
        var row = await Add(availability: "noExpiryReported");
        var preview = await Preview();
        var newlyMatching = await Add();
        var results = await Task.WhenAll(Confirm(preview), Confirm(preview));
        Assert.AreEqual(1, results.Sum(r => r.QueuedCount));
        Assert.AreEqual(1, results.Sum(r => r.SkippedCount));
        Assert.AreEqual(0, results.Sum(r => r.FailedCount));
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var saved = await db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == row.Id);
        Assert.AreEqual(row.Revision, saved.Revision); Assert.AreEqual(row.ContentSnapshotJson, saved.ContentSnapshotJson);
        Assert.AreEqual(row.WorkflowRunId, saved.WorkflowRunId);
        Assert.AreEqual(2, await db.WorkflowRuns.CountAsync());
        var run = await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == row.WorkflowRunId);
        Assert.AreEqual(WorkflowRunStatus.Pending, run.Status);
        var signal = JsonSerializer.Deserialize<PostParserPurchaseSignal>(run.PendingSignalJson!, WorkflowJson.Options)!;
        CollectionAssert.AreEqual(preview.Tasks.Single().LockUrls, signal.LockUrls);
        Assert.IsTrue(signal.EnforceConfiguredLimits); Assert.AreEqual(5m, signal.MaxTotalCost);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == newlyMatching.WorkflowRunId)).Status);
        Assert.AreEqual(0, _purchaser.Calls, "The API only queues the native workflow; it does not make purchases.");
    }

    [TestMethod]
    public async Task ChangedRevisionScopeQuoteAndAccountAreSkippedWithoutBroadening()
    {
        var revision = await Add(); var scopeRow = await Add(); var quote = await Add(); var unchanged = await Add();
        var preview = await Preview(new() {Keyword = "matching", Status = "attention"});
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            await db.PostParserTasks.Where(t => t.Id == revision.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Revision, 4));
            await db.PostParserTasks.Where(t => t.Id == scopeRow.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Title, "different"));
            await db.PostParserTasks.Where(t => t.Id == quote.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ContentSnapshotJson,
                quote.ContentSnapshotJson!.Replace("preserved source", "changed source")));
        }
        var result = await Confirm(preview);
        Assert.AreEqual(1, result.QueuedCount);
        Assert.AreEqual("snapshotChanged", result.Items.Single(i => i.TaskId == revision.Id).Reason);
        Assert.AreEqual("scopeChanged", result.Items.Single(i => i.TaskId == scopeRow.Id).Reason);
        Assert.AreEqual("quoteChanged", result.Items.Single(i => i.TaskId == quote.Id).Reason);
        var next = await Add(title: "account");
        var accountPreview = await Preview(new() {Keyword = "account", Status = "attention"});
        _purchaser.Key = "different fixture account";
        Assert.AreEqual("quoteChanged", (await Confirm(accountPreview)).Items.Single().Reason);
        Assert.AreEqual(0, _purchaser.Calls);
    }

    [TestMethod]
    public async Task ConfirmRejectsExpandedLocksAndLoweredConfiguredLimit()
    {
        await Add();
        var preview = await Preview();
        preview.Tasks.Single().LockUrls.Add("https://example.test/not-approved");
        Assert.AreEqual("quoteChanged", (await Confirm(preview)).Items.Single().Reason);
        var fresh = await Preview();
        _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value.AutoBuyThreshold = 1;
        Assert.AreEqual("overPurchaseLimit", (await Confirm(fresh)).Items.Single().Reason);
    }

    [TestMethod]
    public async Task ChangedAttentionReasonIsNotDeletedOrPurchasedUnderTheOldReasonFilter()
    {
        var row = await Add(availability: "expired", evidence: ["资源链接失效了"]);
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPostParserTaskService>();
        var filter = new PostParserTaskFilterScope {Status = "attention", ReasonCode = "possiblyExpired"};
        var deletion = await service.PreviewDeletion(filter);
        var purchases = await service.PreviewPurchases(filter);
        await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().PostParserTasks.Where(t => t.Id == row.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.AvailabilityJson, "{\"status\":\"expired\",\"evidence\":[\"老了\"]}"));
        Assert.AreEqual("scopeChanged", (await Confirm(purchases)).Items.Single().Reason);
        var result = await service.ConfirmDeletion(new() {Scope = deletion.Scope, Tasks = deletion.Tasks});
        Assert.AreEqual("scopeChanged", result.Items.Single().Reason);
        Assert.IsFalse((await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().PostParserTasks.AsNoTracking().SingleAsync()).IsDeleted);
    }

    [TestMethod]
    public async Task MalformedOffPageLightFieldsAreConservativeAndDoNotBreakPaging()
    {
        var malformed = await Add(title: "offpage");
        await Add(title: "visible");
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        await db.PostParserTasks.Where(t => t.Id == malformed.Id).ExecuteUpdateAsync(s =>
            s.SetProperty(t => t.AvailabilityJson, "{\"status\":[],\"evidence\":42}")
                .SetProperty(t => t.ContentSnapshotJson, "{\"balance\":\"not a number\",\"locks\":[{\"url\":\"fixture\",\"price\":5,\"isBought\":false}]}"));
        var result = await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().Search(new() {Keyword = "visible"});
        Assert.AreEqual(2, result.GlobalTotal);
        Assert.AreEqual(1, result.TotalCount);
        Assert.AreEqual("visible", result.Items.Single().Title);
    }

    [TestMethod]
    public async Task ChangedRunCheckpointInvalidatesPreviewEvenWithinTheAllStatusScope()
    {
        var row = await Add();
        var preview = await Preview(new() {Status = "all"});
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.Where(r => r.Id == row.WorkflowRunId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Failed));
        Assert.AreEqual("quoteChanged", (await Confirm(preview)).Items.Single().Reason);
        Assert.AreEqual(0, _services.GetRequiredService<BTaskManager>().Tasks.Count());
    }

    [TestMethod]
    public async Task OneQueueFailureStillReportsOtherAcceptedRunsWithoutReplayingOrDeletingHistory()
    {
        var invalid = await Add(); var valid = await Add();
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var run = await db.WorkflowRuns.SingleAsync(r => r.Id == invalid.WorkflowRunId);
            var plan = WorkflowRunPlans.Read(run.PlanSnapshotJson)!;
            run.PlanSnapshotJson = WorkflowRunPlans.Serialize(plan with
                {Nodes = plan.Nodes.Select(n => n.NodeId == "unlock" ? n with {ConfigJson = "{"} : n).ToArray()});
            await db.SaveChangesAsync();
        }
        var result = await Confirm(await Preview());
        Assert.AreEqual(1, result.FailedCount);
        Assert.AreEqual("queueFailed", result.Items.Single(i => i.TaskId == invalid.Id).Reason);
        Assert.AreEqual(1, result.QueuedCount);
        Assert.AreEqual("queued", result.Items.Single(i => i.TaskId == valid.Id).Status);
        await using var checkScope = _services.CreateAsyncScope();
        Assert.AreEqual(2, await checkScope.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.CountAsync());
        Assert.AreEqual(0, _purchaser.Calls);
    }

    [TestMethod]
    public async Task TerminalHandlerStillAttachedCannotBeReplacedByAPurchaseResume()
    {
        var row = await Add();
        var before = await Preview();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = _services.GetRequiredService<BTaskManager>();
        await manager.Enqueue(BTaskBuilder.Create($"workflow.run.{row.WorkflowRunId}").Persistent().Run(async args =>
        {
            await args.UpdateTask(t => t.Status = BTaskStatus.Completed);
            entered.SetResult();
            await release.Task;
        }));
        var handler = manager.Tasks.Single();
        await handler.Start();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(handler.HasAttachedExecution);
            Assert.AreEqual("operationBusy", (await Preview()).Skipped.Single().Reason);
            Assert.AreEqual("operationBusy", (await Confirm(before)).Items.Single().Reason);
            await using var scope = _services.CreateAsyncScope();
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>()
                    .PurchaseAndResumeAsync(row.Id, row.Revision, before.Tasks.Single().LockUrls, 5, CancellationToken.None));
        }
        finally { release.TrySetResult(); await handler.WaitForExecutionAsync(); }
    }

    private sealed class Purchaser : ISharedContentPurchaser
    {
        public string Key = "fixture account";
        public string AccountKey => Key;
        public int Calls;
        public PostParserSource Source => PostParserSource.SoulPlus;
        public Task BuyAsync(string lockUrl, CancellationToken ct) { Calls++; throw new InvalidOperationException("No network purchases in API tests."); }
    }
}
