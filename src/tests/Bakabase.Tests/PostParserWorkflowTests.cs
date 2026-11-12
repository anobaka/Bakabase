using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Db;
using ParserTask = Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.PostParserTask;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostParserWorkflowTests
{
    private IServiceProvider _services = null!;
    private FakeReader _reader = null!;
    private FakeExtractor _extractor = null!;
    private FakePurchaser _purchaser = null!;
    private FakeAvailabilityAnalyzer _analyzer = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _reader = new();
        _extractor = new();
        _purchaser = new(_reader);
        _analyzer = new();
        _services = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.AddSingleton<IPostContentService>(_reader);
            services.AddSingleton<IPostDownloadInfoExtractor>(_extractor);
            services.AddSingleton<IPostAvailabilityAnalyzer>(_analyzer);
            services.AddSingleton<IPostLinkHealthChecker, FakeHealthChecker>();
            services.RemoveAll<ISharedContentPurchaser>();
            services.AddSingleton<ISharedContentPurchaser>(_purchaser);
        });
        _services.GetRequiredService<IBOptions<ThirdPartyOptions>>().Value.AutomaticallyParsingPosts = false;
        await using var scope = _services.CreateAsyncScope();
        var providers = scope.ServiceProvider.GetRequiredService<IAiProviderService>();
        var provider = await providers.AddAsync(new() {Kind = AiProviderKind.OpenAI, Name = "fixture", LlmEnabled = true});
        await scope.ServiceProvider.GetRequiredService<IAiFeatureService>().SaveConfigAsync(new AiFeatureConfigDbModel
        {
            Feature = AiFeature.PostParser, ProviderConfigId = provider.Id, ModelId = "fixture", UseDefault = false
        });
    }

    private async Task<int> Add(string link = "https://example.test/post/1", PostParserSource source = 0)
    {
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPostParserTaskService>();
        await service.AddRange(new() {[source] = [link]}, [PostParseTarget.DownloadInfo]);
        return (await service.GetAll()).Single(t => t.Link == link).Id;
    }

    private async Task Dispatch()
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>().DispatchAsync();
    }

    private async Task Reparse(int id)
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().ReParse(id);
    }

    private async Task Retry(int id)
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().Retry(id);
    }

    private async Task WaitForBTask(string taskId, BTaskStatus expected = BTaskStatus.Completed)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var manager = _services.GetRequiredService<BTaskManager>();
        while (manager.Tasks.Single(t => t.Id == taskId) is var handler &&
               (!handler.Task.Status.IsFinished() || handler.HasAttachedExecution))
            await Task.Delay(20, timeout.Token);
        Assert.AreEqual(expected, manager.Tasks.Single(t => t.Id == taskId).Task.Status);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReparseOnlyQueuesTheSelectedPostAndCoalescesConcurrentRequests(bool automaticallyParse)
    {
        var selected = await Add();
        var other = await Add("https://example.test/post/other");
        var before = await TaskState(selected);
        _services.GetRequiredService<IBOptions<ThirdPartyOptions>>().Value.AutomaticallyParsingPosts = automaticallyParse;

        await Task.WhenAll(Reparse(selected), Reparse(selected), Reparse(selected));

        var queued = await TaskState(selected);
        Assert.IsNotNull(queued.WorkflowRunId);
        Assert.AreEqual(before.Revision, queued.Revision);
        Assert.IsNull((await TaskState(other)).WorkflowRunId);
        var manager = _services.GetRequiredService<BTaskManager>();
        Assert.AreEqual(1, manager.Tasks.Count(t => t.Id == $"workflow.run.{queued.WorkflowRunId}"));
        Assert.IsTrue(manager.Tasks.Single(t => t.Id == $"workflow.run.{queued.WorkflowRunId}").Task.IsPersistent);

        await Execute(queued.WorkflowRunId.Value);
        await Task.WhenAll(Reparse(selected), Reparse(selected), Reparse(selected));
        var fresh = await TaskState(selected);
        Assert.AreNotEqual(queued.WorkflowRunId, fresh.WorkflowRunId);
        Assert.AreEqual(queued.Revision + 1, fresh.Revision);
        Assert.IsNull((await TaskState(other)).WorkflowRunId);
    }

    [TestMethod]
    public async Task AutomaticAdditionQueuesOnlyItsInputsAndInitializationDoesNotScanTheBacklog()
    {
        var backlog = await Add("https://example.test/post/backlog");
        var selected = await Add("https://example.test/post/selected");
        _services.GetRequiredService<IBOptions<ThirdPartyOptions>>().Value.AutomaticallyParsingPosts = true;
        await _services.GetRequiredService<PostParserTaskTrigger>().Initialize();
        Assert.IsFalse(_services.GetRequiredService<BTaskManager>().Tasks.Any(t => t.Id == PostParserTaskTrigger.TaskId));
        Assert.IsNull((await TaskState(backlog)).WorkflowRunId);

        var added = await Add("https://example.test/post/new");
        Assert.IsNotNull((await TaskState(added)).WorkflowRunId);
        Assert.IsNull((await TaskState(backlog)).WorkflowRunId);
        Assert.IsNull((await TaskState(selected)).WorkflowRunId);
        await Add("https://example.test/post/selected");
        Assert.IsNotNull((await TaskState(selected)).WorkflowRunId);
        Assert.IsNull((await TaskState(backlog)).WorkflowRunId);
        Assert.AreEqual(0, _reader.Reads, "automatic addition should enqueue background work, not fetch in the request");
    }

    [TestMethod]
    public async Task StartAllUsesOneBackgroundDispatcherAndCanRestartAfterCompletion()
    {
        var first = await Add();
        var second = await Add("https://example.test/post/second");
        _services.GetRequiredService<IBOptions<TaskOptions>>().Value.Tasks =
            [new BTaskDbModel {Id = PostParserTaskTrigger.TaskId, Interval = TimeSpan.FromMinutes(1)}];
        var trigger = _services.GetRequiredService<PostParserTaskTrigger>();
        var manager = _services.GetRequiredService<BTaskManager>();
        var gate = _services.GetRequiredService<PostParserTaskExecutionGate>();
        await gate.Semaphore.WaitAsync();
        try
        {
            await Task.WhenAll(trigger.Start(), trigger.Start(), trigger.Start());
            var dispatch = manager.Tasks.Single(t => t.Id == PostParserTaskTrigger.TaskId);
            Assert.AreEqual(BTaskStatus.Running, dispatch.Task.Status);
            Assert.IsTrue(dispatch.Task.IsPersistent);
            Assert.IsNull(dispatch.Task.Interval);
            Assert.AreEqual(0, _reader.Reads);
        }
        finally { gate.Semaphore.Release(); }
        await WaitForBTask(PostParserTaskTrigger.TaskId);
        var firstRun = (await TaskState(first)).WorkflowRunId;
        Assert.IsNotNull(firstRun);
        Assert.IsNotNull((await TaskState(second)).WorkflowRunId);

        var third = await Add("https://example.test/post/third");
        await trigger.Start();
        await WaitForBTask(PostParserTaskTrigger.TaskId);
        Assert.IsNotNull((await TaskState(third)).WorkflowRunId);
        Assert.AreEqual(firstRun, (await TaskState(first)).WorkflowRunId);
        await using var scope = _services.CreateAsyncScope();
        Assert.AreEqual(3, await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.CountAsync());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("malformed")]
    public async Task LegacyPostWithoutTargetsCanBeParsedWithoutCreatingDuplicateRuns(string? targets)
    {
        int id;
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var task = new PostParserTaskDbModel {Link = "https://example.test/legacy", Targets = targets};
            db.PostParserTasks.Add(task);
            await db.SaveChangesAsync();
            id = task.Id;
        }
        var untouched = await Add("https://example.test/untouched");

        await Task.WhenAll(Reparse(id), Reparse(id), Reparse(id));

        var queued = await TaskState(id);
        Assert.IsNotNull(queued.WorkflowRunId);
        CollectionAssert.AreEqual(new[] {PostParseTarget.DownloadInfo}, queued.Targets);
        Assert.AreEqual(0, queued.Revision);
        Assert.IsNull((await TaskState(untouched)).WorkflowRunId);
        var manager = _services.GetRequiredService<BTaskManager>();
        Assert.AreEqual(1, manager.Tasks.Count(t => t.Id == $"workflow.run.{queued.WorkflowRunId}"));
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            Assert.AreEqual(1, await db.WorkflowRuns.CountAsync());
            Assert.AreEqual(targets, await db.PostParserTasks.Where(t => t.Id == id).Select(t => t.Targets).SingleAsync(),
                "Loading and starting legacy inputs must not require a database backfill.");
        }

        await manager.Start($"workflow.run.{queued.WorkflowRunId}");
        await WaitForBTask($"workflow.run.{queued.WorkflowRunId}");

        var completed = await TaskState(id);
        Assert.AreEqual(WorkflowRunStatus.Success, completed.WorkflowStatus);
        Assert.IsNotNull(completed.CompletedAt);
        Assert.IsNotNull(completed.Results![PostParseTarget.DownloadInfo]);
        Assert.AreEqual(1, _reader.Reads);
    }

    [TestMethod]
    public async Task StartAllIncludesLegacyInputsWithoutTargetsAndSkipsTheirCompletedResults()
    {
        List<int> ids;
        int completedId;
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var pending = new string?[] {null, "[]", "null", "malformed"}.Select((targets, index) =>
                new PostParserTaskDbModel {Link = $"https://example.test/legacy/{index}", Targets = targets}).ToList();
            var completed = new PostParserTaskDbModel
            {
                Link = "https://example.test/legacy/completed",
                Results = "{\"DownloadInfo\":{\"resources\":[]}}"
            };
            db.PostParserTasks.AddRange(pending);
            db.PostParserTasks.Add(completed);
            await db.SaveChangesAsync();
            ids = pending.Select(t => t.Id).ToList();
            completedId = completed.Id;
        }
        var trigger = _services.GetRequiredService<PostParserTaskTrigger>();
        await Task.WhenAll(trigger.Start(), trigger.Start(), trigger.Start());
        await WaitForBTask(PostParserTaskTrigger.TaskId);
        await trigger.Start();
        await WaitForBTask(PostParserTaskTrigger.TaskId);

        var manager = _services.GetRequiredService<BTaskManager>();
        foreach (var id in ids)
        {
            var task = await TaskState(id);
            Assert.IsNotNull(task.WorkflowRunId);
            Assert.AreEqual(1, manager.Tasks.Count(t => t.Id == $"workflow.run.{task.WorkflowRunId}"));
            await manager.Start($"workflow.run.{task.WorkflowRunId}");
            await WaitForBTask($"workflow.run.{task.WorkflowRunId}");
            Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        }
        Assert.IsNull((await TaskState(completedId)).WorkflowRunId);
        Assert.AreEqual(ids.Count, _reader.Reads);
        await using var verification = _services.CreateAsyncScope();
        Assert.AreEqual(ids.Count, await verification.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task RepeatedRetryKeepsOneRunAndExecutesAfterTheRequestScopeIsDisposed()
    {
        _extractor.FailuresRemaining = 1;
        var id = await Add();
        var backlog = await Add("https://example.test/post/backlog");
        await Reparse(id);
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Failed, (await TaskState(id)).WorkflowStatus);

        await Task.WhenAll(Retry(id), Retry(id), Retry(id));
        var manager = _services.GetRequiredService<BTaskManager>();
        Assert.AreEqual(1, manager.Tasks.Count(t => t.Id == $"workflow.run.{runId}"));
        Assert.AreEqual(runId, (await TaskState(id)).WorkflowRunId);
        Assert.IsNull((await TaskState(backlog)).WorkflowRunId);
        await manager.Start($"workflow.run.{runId}");
        await WaitForBTask($"workflow.run.{runId}");

        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(1, _reader.Reads);
        Assert.AreEqual(2, _extractor.Extractions);
        Assert.IsNull((await TaskState(backlog)).WorkflowRunId);
    }

    [TestMethod]
    public async Task RepeatedAdditionKeepsAnActiveRunEvenAfterAnIntermediateResultWasPublished()
    {
        var id = await Add();
        await Reparse(id);
        var before = await TaskState(id);
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            await db.PostParserTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s =>
                s.SetProperty(t => t.ParsingState, "complete")
                    .SetProperty(t => t.Results, "{\"DownloadInfo\":{\"resources\":[]}}"));
            await db.WorkflowRuns.Where(r => r.Id == before.WorkflowRunId).ExecuteUpdateAsync(s =>
                s.SetProperty(r => r.Status, WorkflowRunStatus.Running));
        }
        _services.GetRequiredService<IBOptions<ThirdPartyOptions>>().Value.AutomaticallyParsingPosts = true;
        await Add();
        var after = await TaskState(id);
        Assert.AreEqual(before.Revision, after.Revision);
        Assert.AreEqual(before.WorkflowRunId, after.WorkflowRunId);
        Assert.AreEqual(WorkflowRunStatus.Running, after.WorkflowStatus);
    }

    [TestMethod]
    public async Task StartAllRetriesFailedPostsAndSkipsFinishedAndWaitingPosts()
    {
        var completeId = await Add("https://example.test/post/complete");
        await Reparse(completeId);
        var completedRun = (await TaskState(completeId)).WorkflowRunId!.Value;
        await Execute(completedRun);
        _extractor.FailuresRemaining = 1;
        var failedId = await Add("https://example.test/post/failed");
        await Reparse(failedId);
        var failedRun = (await TaskState(failedId)).WorkflowRunId!.Value;
        await Execute(failedRun);
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IAiFeatureService>()
                .SaveConfigAsync(new() {Feature = AiFeature.PostParser, UseDefault = false});
        var waitingId = await Add("https://example.test/post/waiting");
        await Reparse(waitingId);
        var waitingRun = (await TaskState(waitingId)).WorkflowRunId!.Value;
        await Execute(waitingRun);
        var pendingId = await Add("https://example.test/post/pending");

        await _services.GetRequiredService<PostParserTaskTrigger>().Start();
        await WaitForBTask(PostParserTaskTrigger.TaskId);

        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(completeId)).WorkflowStatus);
        Assert.AreEqual(completedRun, (await TaskState(completeId)).WorkflowRunId);
        Assert.AreEqual(WorkflowRunStatus.Pending, (await TaskState(failedId)).WorkflowStatus);
        Assert.AreEqual(failedRun, (await TaskState(failedId)).WorkflowRunId);
        Assert.IsNull((await TaskState(failedId)).Error);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await TaskState(waitingId)).WorkflowStatus);
        Assert.AreEqual(waitingRun, (await TaskState(waitingId)).WorkflowRunId);
        Assert.IsNotNull((await TaskState(pendingId)).WorkflowRunId);
    }

    [TestMethod]
    [DataRow(WorkflowRunStatus.Failed)]
    [DataRow(WorkflowRunStatus.Interrupted)]
    [DataRow(WorkflowRunStatus.Cancelled)]
    public async Task BulkAndRowRetryShareOneStoppedRunAndResumeItsSavedContent(WorkflowRunStatus stoppedStatus)
    {
        _extractor.FailuresRemaining = 1;
        var id = await Add();
        await Reparse(id);
        var before = await TaskState(id);
        var runId = before.WorkflowRunId!.Value;
        var manager = _services.GetRequiredService<BTaskManager>();
        await manager.Start($"workflow.run.{runId}");
        await WaitForBTask($"workflow.run.{runId}");
        Assert.AreEqual(WorkflowRunStatus.Failed, (await TaskState(id)).WorkflowStatus);
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.Where(r => r.Id == runId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, stoppedStatus));

        var trigger = _services.GetRequiredService<PostParserTaskTrigger>();
        await Task.WhenAll(trigger.Start(), trigger.Start(), Retry(id), Retry(id));
        await WaitForBTask(PostParserTaskTrigger.TaskId);
        await trigger.Start();
        await WaitForBTask(PostParserTaskTrigger.TaskId);

        var queued = await TaskState(id);
        Assert.AreEqual(runId, queued.WorkflowRunId);
        Assert.AreEqual(before.Revision, queued.Revision);
        Assert.AreEqual(WorkflowRunStatus.Pending, queued.WorkflowStatus);
        Assert.AreEqual(1, manager.Tasks.Count(t => t.Id == $"workflow.run.{runId}"));
        await manager.Start($"workflow.run.{runId}");
        await WaitForBTask($"workflow.run.{runId}");
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(1, _reader.Reads, "Retry must resume saved content instead of fetching the post again.");
        Assert.AreEqual(2, _extractor.Extractions);
        await using var verification = _services.CreateAsyncScope();
        Assert.AreEqual(1, await verification.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.CountAsync());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StartAllRestartsLegacyErrorsAndMissingRuns(bool missingRun)
    {
        int id;
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var task = new PostParserTaskDbModel
            {
                Link = "https://example.test/legacy-error", Error = "Old failure", Revision = 2,
                WorkflowRunId = missingRun ? 123456 : null,
                Results = "{\"DownloadInfo\":{\"resources\":[]}}", ParsingState = "partial"
            };
            db.PostParserTasks.Add(task);
            await db.SaveChangesAsync();
            id = task.Id;
        }
        var trigger = _services.GetRequiredService<PostParserTaskTrigger>();
        await Task.WhenAll(trigger.Start(), trigger.Start());
        await WaitForBTask(PostParserTaskTrigger.TaskId);
        var queued = await TaskState(id);
        Assert.IsNotNull(queued.WorkflowRunId);
        Assert.AreNotEqual(123456, queued.WorkflowRunId);
        Assert.AreEqual(3, queued.Revision);
        Assert.IsNull(queued.Error);
        Assert.IsNull(queued.Results);
        var manager = _services.GetRequiredService<BTaskManager>();
        await manager.Start($"workflow.run.{queued.WorkflowRunId}");
        await WaitForBTask($"workflow.run.{queued.WorkflowRunId}");
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(1, _reader.Reads);
    }

    [TestMethod]
    public async Task ARejectedBulkRetryKeepsItsReasonAndDoesNotPreventOtherPostsFromStarting()
    {
        _extractor.FailuresRemaining = 1;
        var failedId = await Add("https://example.test/failed");
        await Reparse(failedId);
        var failedRun = (await TaskState(failedId)).WorkflowRunId!.Value;
        await Execute(failedRun);
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.Where(r => r.Id == failedRun)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.PayloadJson, "malformed"));
        var pendingId = await Add("https://example.test/pending");

        await _services.GetRequiredService<PostParserTaskTrigger>().Start();
        await WaitForBTask(PostParserTaskTrigger.TaskId, BTaskStatus.Error);

        var failed = await TaskState(failedId);
        Assert.AreEqual(failedRun, failed.WorkflowRunId);
        Assert.AreEqual(WorkflowRunStatus.Failed, failed.WorkflowStatus);
        StringAssert.Contains(failed.Error, "Run payload is invalid");
        Assert.AreEqual(failed.Error, (await RunState(failedRun)).ErrorMessage);
        var pending = await TaskState(pendingId);
        Assert.IsNotNull(pending.WorkflowRunId);
        var manager = _services.GetRequiredService<BTaskManager>();
        await manager.Start($"workflow.run.{pending.WorkflowRunId}");
        await WaitForBTask($"workflow.run.{pending.WorkflowRunId}");
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(pendingId)).WorkflowStatus);
    }

    [TestMethod]
    public async Task BulkRetryDoesNotReplaceAStoppedRunWhileItsBackgroundTaskIsStillUnwinding()
    {
        var id = await Add();
        await Reparse(id);
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        var manager = _services.GetRequiredService<BTaskManager>();
        await manager.Clean($"workflow.run.{runId}");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await manager.Enqueue(BTaskBuilder.Create($"workflow.run.{runId}").Persistent().Run(async _ =>
        {
            await using var scope = _services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.Where(r => r.Id == runId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Failed)
                    .SetProperty(r => r.ErrorMessage, "Finishing failure"));
            entered.TrySetResult();
            await release.Task;
        }));
        await manager.Start($"workflow.run.{runId}");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var pendingId = await Add("https://example.test/pending");
        try
        {
            await _services.GetRequiredService<PostParserTaskTrigger>().Start();
            await WaitForBTask(PostParserTaskTrigger.TaskId);
            Assert.AreEqual(WorkflowRunStatus.Failed, (await TaskState(id)).WorkflowStatus);
            Assert.AreEqual(BTaskStatus.Running, manager.Tasks.Single(t => t.Id == $"workflow.run.{runId}").Task.Status);
            Assert.IsNotNull((await TaskState(pendingId)).WorkflowRunId);
        }
        finally { release.TrySetResult(); }
        await WaitForBTask($"workflow.run.{runId}");
    }

    [TestMethod]
    public async Task FailedBulkDispatchTransactionDoesNotLeakTrackedChangesIntoTheNextPost()
    {
        var failedId = await Add("https://example.test/failed-transaction");
        var nextId = await Add("https://example.test/next");
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var injected = false;
            db.SavingChanges += (_, _) =>
            {
                if (injected || !db.ChangeTracker.Entries<PostParserTaskDbModel>()
                        .Any(e => e.Entity.Id == failedId && e.Entity.WorkflowRunId != null)) return;
                injected = true;
                throw new InvalidOperationException("Fixture failed while linking the new workflow run.");
            };
            await Assert.ThrowsExceptionAsync<AggregateException>(() => scope.ServiceProvider
                .GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>().DispatchAsync());
            Assert.IsTrue(injected);
        }

        var failed = await TaskState(failedId);
        Assert.IsNull(failed.WorkflowRunId, "The rolled-back run must not be linked by a later post's SaveChanges.");
        StringAssert.Contains(failed.Error, "Fixture failed while linking");
        var next = await TaskState(nextId);
        Assert.IsNotNull(next.WorkflowRunId);
        await using (var scope = _services.CreateAsyncScope())
            Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.CountAsync());
        var manager = _services.GetRequiredService<BTaskManager>();
        await manager.Start($"workflow.run.{next.WorkflowRunId}");
        await WaitForBTask($"workflow.run.{next.WorkflowRunId}");
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(nextId)).WorkflowStatus);
        Assert.AreEqual(1, _reader.Reads);
    }

    [TestMethod]
    public async Task BulkDispatchReportsProgressAndPausesOutsideTheSinglePostGate()
    {
        var first = await Add("https://example.test/post/first");
        var second = await Add("https://example.test/post/second");
        var third = await Add("https://example.test/post/third");
        var pause = new PauseTokenSource();
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pause.OnPause += _ => { paused.TrySetResult(); return Task.CompletedTask; };
        using var cancellation = new CancellationTokenSource();
        var progress = new List<int>();
        var process = new List<string>();
        var parsing = Task.Run(async () =>
        {
            await using var scope = _services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().ParseAll(
                percentage =>
                {
                    progress.Add(percentage);
                    if (percentage == 33) pause.Pause();
                    return Task.CompletedTask;
                },
                message => { process.Add(message); return Task.CompletedTask; },
                pause.Token, cancellation.Token);
        });

        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Reparse(third).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { cancellation.Cancel(); }
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => parsing);
        CollectionAssert.AreEqual(new[] {0, 33}, progress);
        CollectionAssert.AreEqual(new[] {"0/3", "1/3"}, process);
        Assert.IsNotNull((await TaskState(first)).WorkflowRunId);
        Assert.IsNull((await TaskState(second)).WorkflowRunId);
        Assert.IsNotNull((await TaskState(third)).WorkflowRunId);
    }

    private async Task<ParserTask> TaskState(int id)
    {
        await using var scope = _services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().GetAll()).Single(t => t.Id == id);
    }

    private async Task<WorkflowRunDbModel> RunState(int id)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().Set<WorkflowRunDbModel>().AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task Execute(int id)
    {
        await using var scope = _services.CreateAsyncScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<WorkflowRunner<BakabaseDbContext>>().ExecuteAsync(id,
                new BTaskArgs(new PauseToken(), CancellationToken.None, new BTask("fixture", () => "fixture"),
                    _ => Task.CompletedTask, _services));
        }
        catch (OperationCanceledException) { }
        await _services.GetRequiredService<BTaskManager>().Clean($"workflow.run.{id}");
    }

    [TestMethod]
    public async Task ConcurrentDispatchAndRepeatedAddShareOneRun()
    {
        var beforeCreation = DateTime.UtcNow;
        var id = await Add();
        var created = await TaskState(id);
        var revision = created.Revision;
        Assert.IsNotNull(created.CreatedAt);
        Assert.IsTrue(created.CreatedAt >= beforeCreation && created.CreatedAt <= DateTime.UtcNow);
        Assert.AreEqual(DateTimeKind.Utc, created.CreatedAt.Value.Kind);
        Assert.IsNull(created.CompletedAt);
        await Task.WhenAll(Dispatch(), Dispatch(), Dispatch());
        var first = await TaskState(id);
        await Add();
        await Dispatch();
        var second = await TaskState(id);
        Assert.AreEqual(revision, second.Revision);
        Assert.AreEqual(first.WorkflowRunId, second.WorkflowRunId);
        Assert.AreEqual(created.CreatedAt, second.CreatedAt);
        Assert.IsNull(second.CompletedAt);
        await using var scope = _services.CreateAsyncScope();
        Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().Set<WorkflowRunDbModel>().CountAsync());
        await Execute(first.WorkflowRunId!.Value);
        var done = await TaskState(id);
        Assert.AreEqual(WorkflowRunStatus.Success, done.WorkflowStatus);
        Assert.AreEqual(created.CreatedAt, done.CreatedAt);
        Assert.IsNotNull(done.CompletedAt);
        Assert.AreEqual((await RunState(done.WorkflowRunId!.Value)).CompletedAt!.Value.ToUniversalTime(), done.CompletedAt);
        Assert.AreEqual(DateTimeKind.Utc, done.CompletedAt.Value.Kind);
        Assert.AreEqual("Extracted title", done.Title);
        Assert.AreEqual("secret", done.Results![PostParseTarget.DownloadInfo]!["resources"]![0]!["password"]!.GetValue<string>());
        Assert.AreEqual(1, _reader.Reads);
    }

    [TestMethod]
    public async Task ContentGroupsSurviveTaskPersistenceLinkChecksAndWorkflowOutput()
    {
        _extractor.Result = new()
        {
            Title = "Grouped post",
            Groups = [new() {Id = "full", Title = "完整本体", Kind = "main", Summary = "二选一的分流", Evidence = ["百度与MEGA内容相同"]},
                new() {Id = "preview", Title = "试玩", Kind = "preview"}],
            Resources = [new() {Link = "https://pan.baidu.com/s/full", GroupId = "full", Code = "code"},
                new() {Link = "https://mega.nz/file/full#key", GroupId = "full", Password = "archive"},
                new() {Link = "https://example.test/preview", GroupId = "preview"}]
        };
        var id = await Add();
        await Dispatch();
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        await Execute(runId);

        var done = await TaskState(id);
        Assert.AreEqual(WorkflowRunStatus.Success, done.WorkflowStatus);
        var saved = done.Results![PostParseTarget.DownloadInfo]!;
        Assert.AreEqual(3, saved["schemaVersion"]!.GetValue<int>());
        Assert.AreEqual("full", saved["resources"]![0]!["groupId"]!.GetValue<string>());
        Assert.AreEqual("full", saved["resources"]![1]!["groupId"]!.GetValue<string>());
        Assert.AreEqual("preview", saved["resources"]![2]!["groupId"]!.GetValue<string>());
        Assert.IsNotNull(saved["resources"]![0]!["linkHealth"]);
        Assert.AreEqual("二选一的分流", saved["groups"]![0]!["summary"]!.GetValue<string>());
        Assert.AreEqual("百度与MEGA内容相同", saved["groups"]![0]!["evidence"]![0]!.GetValue<string>());
        using var output = JsonDocument.Parse((await RunState(runId)).OutputItemsJson!);
        var summary = output.RootElement[0];
        Assert.AreEqual(id, summary.GetProperty("taskId").GetInt32());
        Assert.AreEqual(3, summary.GetProperty("sourceCount").GetInt32());
        Assert.AreEqual(2, summary.GetProperty("groupCount").GetInt32());
        Assert.AreEqual(3, summary.GetProperty("processingUnknownCount").GetInt32());
        Assert.IsFalse(summary.TryGetProperty("result", out _), "full parsed results belong to the source module");
        Assert.IsFalse(output.RootElement.GetRawText().Contains("pan.baidu.com"));
    }

    [TestMethod]
    public async Task FailedExtractionRetriesSameRunFromSavedContent()
    {
        _extractor.FailuresRemaining = 1;
        var id = await Add();
        var createdAt = (await TaskState(id)).CreatedAt;
        await Dispatch();
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Failed, (await TaskState(id)).WorkflowStatus);
        Assert.IsNotNull((await RunState(runId)).CompletedAt);
        Assert.IsNull((await TaskState(id)).CompletedAt);
        Assert.AreEqual(2, (await RunState(runId)).CurrentStepIndex);
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().Retry(id);
        Assert.IsNull((await TaskState(id)).CompletedAt);
        Assert.AreEqual(createdAt, (await TaskState(id)).CreatedAt);
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(runId, (await TaskState(id)).WorkflowRunId);
        Assert.IsNotNull((await TaskState(id)).CompletedAt);
        Assert.AreEqual(createdAt, (await TaskState(id)).CreatedAt);
        Assert.AreEqual(1, _reader.Reads);
        Assert.AreEqual(2, _extractor.Extractions);
    }

    [TestMethod]
    public async Task ReparseDuringExtractionKeepsTheActiveRunAndItsCompletion()
    {
        _extractor.Hold = true;
        var id = await Add();
        await Dispatch();
        var old = await TaskState(id);
        var executing = Execute(old.WorkflowRunId!.Value);
        await _extractor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.WhenAll(Reparse(id), Reparse(id), Retry(id));
        var fresh = await TaskState(id);
        Assert.AreEqual(old.CreatedAt, fresh.CreatedAt);
        Assert.IsNull(fresh.CompletedAt);
        _extractor.Hold = false;
        _extractor.Release.TrySetResult();
        await executing;
        Assert.AreEqual(old.WorkflowRunId, fresh.WorkflowRunId);
        Assert.AreEqual(old.Revision, fresh.Revision);
        Assert.IsNotNull((await TaskState(id)).Results);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual((await RunState(fresh.WorkflowRunId.Value)).CompletedAt!.Value.ToUniversalTime(), (await TaskState(id)).CompletedAt);
    }

    [TestMethod]
    public async Task ReparseKeepsOriginalCreationAndClearsPreviousCompletion()
    {
        var id = await Add();
        await Dispatch();
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        await Execute(runId);
        var completed = await TaskState(id);
        Assert.IsNotNull(completed.CompletedAt);
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().ReParse(id);
        var pending = await TaskState(id);
        Assert.AreEqual(completed.CreatedAt, pending.CreatedAt);
        Assert.IsNull(pending.CompletedAt);
        Assert.IsNotNull(pending.WorkflowRunId);
        var newRunId = (await TaskState(id)).WorkflowRunId!.Value;
        Assert.AreNotEqual(runId, newRunId);
        await Execute(newRunId);
        var latest = await TaskState(id);
        Assert.AreEqual(completed.CreatedAt, latest.CreatedAt);
        Assert.AreEqual((await RunState(newRunId)).CompletedAt!.Value.ToUniversalTime(), latest.CompletedAt);
    }

    [TestMethod]
    public async Task SupersededRunCannotChangeLatestSuccessfulCompletion()
    {
        var id = await Add();
        await Dispatch();
        var old = await TaskState(id);
        await Execute(old.WorkflowRunId!.Value);
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().ReParse(id);
        await Dispatch();
        var newRunId = (await TaskState(id)).WorkflowRunId!.Value;
        await Execute(newRunId);
        var latest = await TaskState(id);
        Assert.IsNotNull(latest.CompletedAt);
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            await db.WorkflowRuns.Where(r => r.Id == old.WorkflowRunId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Failed)
                    .SetProperty(r => r.CompletedAt, DateTime.Now.AddMinutes(1)));
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
                scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>()
                    .SaveResultAsync(new PostParserInput {TaskId = id, Revision = old.Revision},
                        old.WorkflowRunId!.Value, new PostDownloadInfo {Title = "Stale title"}, CancellationToken.None));
        }
        var refreshed = await TaskState(id);
        Assert.AreEqual(latest.CreatedAt, refreshed.CreatedAt);
        Assert.AreEqual(latest.CompletedAt, refreshed.CompletedAt);
        Assert.AreEqual(latest.Title, refreshed.Title);
        Assert.IsNull(refreshed.Error);
    }

    [TestMethod]
    public async Task HistoricalRecordsKeepUnknownTimesAndPastedTextGetsCreationTime()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var historical = new PostParserTaskDbModel
        {
            Link = "https://example.test/historical", Targets = "[1]", Results = "{\"DownloadInfo\":{}}"
        };
        db.PostParserTasks.Add(historical);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IPostParserTaskService>();
        await service.AddInputs([], [PostParseTarget.DownloadInfo], [], "Pasted content", null);
        var all = await service.GetAll();
        var old = all.Single(t => t.Id == historical.Id);
        Assert.IsNull(old.CreatedAt);
        Assert.IsNull(old.CompletedAt);
        Assert.IsNotNull(all.Single(t => t.Text == "Pasted content").CreatedAt);
    }

    [TestMethod]
    public async Task SuccessfulWorkflowWithoutResultsDoesNotClaimCompletedParsing()
    {
        var id = await Add();
        await Dispatch();
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.Where(r => r.Id == runId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Success)
                    .SetProperty(r => r.CompletedAt, DateTime.Now));
        var task = await TaskState(id);
        Assert.IsNotNull(task.Error);
        Assert.IsNull(task.CompletedAt);
    }

    [TestMethod]
    public async Task DeleteDuringReadingDoesNotExtractOrResurrect()
    {
        _reader.Hold = true;
        var id = await Add();
        await Dispatch();
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        var executing = Execute(runId);
        await _reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().Delete(id);
        _reader.Release.TrySetResult();
        await executing;
        var deleted = await TaskState(id);
        Assert.IsTrue(deleted.IsDeleted);
        Assert.IsNull(deleted.Results);
        Assert.AreEqual(0, _extractor.Extractions);
        Assert.AreEqual(0, _purchaser.Purchases);
        await Dispatch();
        Assert.AreEqual(runId, (await TaskState(id)).WorkflowRunId);
    }

    [TestMethod]
    public async Task LostQueuedTaskReusesCommittedRun()
    {
        var id = await Add();
        await Dispatch();
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        await _services.GetRequiredService<BTaskManager>().Clean($"workflow.run.{runId}");
        await Dispatch();
        Assert.AreEqual(runId, (await TaskState(id)).WorkflowRunId);
        Assert.AreEqual(1, _services.GetRequiredService<BTaskManager>().Tasks.Count(t => t.Id == $"workflow.run.{runId}"));
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
    }

    [TestMethod]
    public async Task RestartRequiresAnExplicitRetryThenResumesFromCheckpointWithoutReadingAgain()
    {
        _extractor.FailuresRemaining = 1;
        var id = await Add();
        await Dispatch();
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        await Execute(runId);
        await _services.GetRequiredService<BTaskManager>().Clean($"workflow.run.{runId}");
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            await db.Set<WorkflowRunDbModel>().Where(r => r.Id == runId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Running));
            await scope.ServiceProvider.GetRequiredService<WorkflowRunRehydrator<BakabaseDbContext>>().MarkInterruptedRunsAsync();
            await scope.ServiceProvider.GetRequiredService<WorkflowRunRehydrator<BakabaseDbContext>>().ReEnqueuePendingRunsAsync();
        }
        Assert.AreEqual(WorkflowRunStatus.Interrupted, (await RunState(runId)).Status);
        Assert.IsFalse(_services.GetRequiredService<BTaskManager>().Tasks.Any(t => t.Id == $"workflow.run.{runId}"));
        Assert.AreEqual(2, (await RunState(runId)).CurrentStepIndex);
        await Retry(id);
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(1, _reader.Reads);
    }

    [TestMethod]
    [DataRow("revision")]
    [DataRow("run")]
    [DataRow("step")]
    [DataRow("status")]
    [DataRow("decision")]
    public async Task ConditionalRetryRejectsChangedTaskOrCheckpoint(string changed)
    {
        _extractor.FailuresRemaining = 1;
        var id = await Add();
        await Dispatch();
        var task = await TaskState(id);
        var runId = task.WorkflowRunId!.Value;
        await Execute(runId);
        var before = await RunState(runId);
        await using var scope = _services.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>();
        var controller = new Bakabase.Service.Controllers.PostParserAcquisitionController(null!);
        var response = await controller.Retry(id, workflow, CancellationToken.None,
            changed == "revision" ? task.Revision + 1 : task.Revision,
            changed == "run" ? runId + 1 : runId,
            changed == "step" ? before.CurrentStepIndex + 1 : before.CurrentStepIndex,
            changed == "status" ? WorkflowRunStatus.Interrupted : before.Status,
            changed == "decision" ? 1 : 0);

        Assert.AreNotEqual(0, response.Code);
        var after = await RunState(runId);
        Assert.AreEqual(before.Status, after.Status);
        Assert.AreEqual(before.CurrentStepIndex, after.CurrentStepIndex);
        Assert.AreEqual(before.CurrentItemJson, after.CurrentItemJson);
        Assert.AreEqual(before.PendingSignalJson, after.PendingSignalJson);
        Assert.AreEqual(runId, (await TaskState(id)).WorkflowRunId);
        Assert.IsFalse(_services.GetRequiredService<BTaskManager>().Tasks.Any(t => t.Id == $"workflow.run.{runId}"));
        Assert.AreEqual(0, _purchaser.Purchases);
    }

    [TestMethod]
    public async Task ConditionalRetryWithMatchingCheckpointKeepsTheSameRunAndRevision()
    {
        _extractor.FailuresRemaining = 1;
        var id = await Add();
        await Dispatch();
        var task = await TaskState(id);
        var runId = task.WorkflowRunId!.Value;
        await Execute(runId);
        var before = await RunState(runId);
        await using (var scope = _services.CreateAsyncScope())
        {
            var controller = new Bakabase.Service.Controllers.PostParserAcquisitionController(null!);
            var response = await controller.Retry(id,
                scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>(), CancellationToken.None,
                task.Revision, runId, before.CurrentStepIndex, before.Status, 0);
            Assert.AreEqual(0, response.Code);
        }
        Assert.AreEqual(WorkflowRunStatus.Pending, (await RunState(runId)).Status);
        Assert.AreEqual(before.PendingSignalJson, (await RunState(runId)).PendingSignalJson);
        await Execute(runId);
        var recovered = await TaskState(id);
        Assert.AreEqual(task.WorkflowRunId, recovered.WorkflowRunId);
        Assert.AreEqual(task.Revision, recovered.Revision);
        Assert.AreEqual(WorkflowRunStatus.Success, recovered.WorkflowStatus);
        Assert.AreEqual(1, _reader.Reads);
        Assert.AreEqual(0, _purchaser.Purchases);
    }

    [TestMethod]
    public async Task RestartClearsTheRequestedQueueWithoutStartingUnrequestedPosts()
    {
        var queuedId = await Add();
        await Reparse(queuedId);
        var unrequestedId = await Add("https://example.test/not-requested");
        var runId = (await TaskState(queuedId)).WorkflowRunId!.Value;
        var manager = _services.GetRequiredService<BTaskManager>();
        await manager.Clean($"workflow.run.{runId}");
        await using (var scope = _services.CreateAsyncScope())
        {
            var rehydrator = scope.ServiceProvider.GetRequiredService<WorkflowRunRehydrator<BakabaseDbContext>>();
            await rehydrator.MarkInterruptedRunsAsync();
            await rehydrator.ReEnqueuePendingRunsAsync();
        }
        Assert.AreEqual(WorkflowRunStatus.Interrupted, (await RunState(runId)).Status);
        Assert.IsNull((await TaskState(unrequestedId)).WorkflowRunId);
        Assert.IsFalse(manager.Tasks.Any(t => t.Id.StartsWith("workflow.run.")));
        await Retry(queuedId);
        Assert.AreEqual(runId, (await TaskState(queuedId)).WorkflowRunId);
        Assert.AreEqual("postParser", manager.Tasks.Single(t => t.Id == $"workflow.run.{runId}").Task.ConcurrencyGroup);
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(queuedId)).WorkflowStatus);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StoppingAQueuedBackgroundTaskCanBeRetriedFromItsRowOrStartAll(bool startAll)
    {
        var id = await Add();
        await Reparse(id);
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        var manager = _services.GetRequiredService<BTaskManager>();
        await manager.Stop($"workflow.run.{runId}");
        Assert.AreEqual(WorkflowRunStatus.Pending, (await RunState(runId)).Status);
        Assert.AreEqual(WorkflowRunStatus.Cancelled, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(0, (await RunState(runId)).CurrentStepIndex);
        if (startAll) await Dispatch();
        else await Retry(id);
        var queued = await TaskState(id);
        Assert.AreEqual(runId, queued.WorkflowRunId);
        Assert.AreEqual(WorkflowRunStatus.Pending, queued.WorkflowStatus);
        Assert.AreEqual(BTaskStatus.NotStarted, manager.GetTaskViewModel($"workflow.run.{runId}")!.Status);
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(1, _reader.Reads);
    }

    [TestMethod]
    public async Task ReparseImmediatelyAfterStoppingAQueuedTaskDoesNotDependOnRefreshingTheList()
    {
        var id = await Add();
        await Reparse(id);
        var previous = await TaskState(id);
        var manager = _services.GetRequiredService<BTaskManager>();
        await manager.Stop($"workflow.run.{previous.WorkflowRunId}");
        Assert.AreEqual(WorkflowRunStatus.Pending, (await RunState(previous.WorkflowRunId!.Value)).Status);

        // Deliberately do not call GetAll between stopping the BTask and starting the row again.
        await Reparse(id);
        var current = await TaskState(id);
        Assert.AreNotEqual(previous.WorkflowRunId, current.WorkflowRunId);
        Assert.AreEqual(previous.Revision + 1, current.Revision);
        Assert.AreEqual(WorkflowRunStatus.Cancelled, (await RunState(previous.WorkflowRunId.Value)).Status);
        Assert.AreEqual(WorkflowRunStatus.Pending, current.WorkflowStatus);
        Assert.AreEqual(BTaskStatus.NotStarted, manager.GetTaskViewModel($"workflow.run.{current.WorkflowRunId}")!.Status);
        await Execute(current.WorkflowRunId!.Value);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(1, _reader.Reads);
    }

    [TestMethod]
    public async Task OverallLimitKeepsExcessPostsQueuedWhileReadingOverlapsAnotherPostsExtraction()
    {
        _services.GetRequiredService<IBOptions<ThirdPartyOptions>>().Value.PostParserMaxConcurrency = 2;
        _extractor.Hold = true;
        var first = await Add();
        var second = await Add("https://example.test/second");
        var third = await Add("https://example.test/third");
        await Dispatch();
        var firstRun = (await TaskState(first)).WorkflowRunId!.Value;
        var secondRun = (await TaskState(second)).WorkflowRunId!.Value;
        var thirdRun = (await TaskState(third)).WorkflowRunId!.Value;
        var manager = _services.GetRequiredService<BTaskManager>();
        try
        {
            await manager.Start($"workflow.run.{firstRun}");
            await _extractor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            _reader.Hold = true;
            _reader.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await manager.Start($"workflow.run.{secondRun}");
            await _reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await manager.Start($"workflow.run.{thirdRun}");
            Assert.AreEqual(BTaskStatus.NotStarted, manager.GetTaskViewModel($"workflow.run.{thirdRun}")!.Status);
            Assert.AreEqual(2, _reader.Reads);
            var firstProgress = (WorkflowTaskProgress)manager.GetTaskViewModel($"workflow.run.{firstRun}")!.Data!;
            var secondProgress = (WorkflowTaskProgress)manager.GetTaskViewModel($"workflow.run.{secondRun}")!.Data!;
            Assert.AreEqual(PostParserWorkflow.ExtractDownloadInfo, firstProgress.ActivityKind);
            Assert.AreEqual("fetching", secondProgress.Stage);
            Assert.AreEqual(firstRun, firstProgress.WorkflowRunId);
        }
        finally
        {
            _reader.Release.TrySetResult();
            _extractor.Release.TrySetResult();
            await WaitForBTask($"workflow.run.{firstRun}");
            await WaitForBTask($"workflow.run.{secondRun}");
        }
        await manager.Start($"workflow.run.{thirdRun}");
        await WaitForBTask($"workflow.run.{thirdRun}");
        Assert.AreEqual(3, _reader.Reads);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(third)).WorkflowStatus);
    }

    [TestMethod]
    public async Task GenericTextWorkflowDoesNotRequireTaskResourceOrReader()
    {
        await using var scope = _services.CreateAsyncScope();
        var definition = await scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>().SeedAsync();
        var run = await scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionService>().RunManuallyAsync(definition,
            JsonSerializer.Serialize(new PostParserInput {Text = "shared link and password", Title = "Pasted"}, WorkflowJson.Options));
        Assert.AreEqual("postParser", _services.GetRequiredService<BTaskManager>().Tasks
            .Single(t => t.Id == $"workflow.run.{run.Id}").Task.ConcurrencyGroup);
        await Execute(run.Id);
        Assert.AreEqual(WorkflowRunStatus.Success, (await RunState(run.Id)).Status);
        using var output = JsonDocument.Parse((await RunState(run.Id)).OutputItemsJson!);
        Assert.AreEqual("downloadInformation", output.RootElement[0].GetProperty("stage").GetString());
        Assert.AreEqual(1, output.RootElement[0].GetProperty("sourceCount").GetInt32());
        Assert.IsFalse(output.RootElement[0].TryGetProperty("input", out _));
        Assert.IsFalse(output.RootElement[0].TryGetProperty("result", out _));
        Assert.AreEqual(0, _reader.Reads);
        Assert.AreEqual(1, _extractor.Extractions);
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().Set<PostParserTaskDbModel>().CountAsync());
    }

    [TestMethod]
    public async Task GenericWorkflowRespectsTheDefaultZeroPurchaseLimit()
    {
        _reader.Locks = [new("https://example.test/lock", 1, false)];
        await using var scope = _services.CreateAsyncScope();
        var definition = await scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>().SeedAsync();
        var run = await scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionService>().RunManuallyAsync(definition,
            JsonSerializer.Serialize(new PostParserInput {Link = "https://example.test/post", SourceHint = "SoulPlus"}, WorkflowJson.Options));
        await Execute(run.Id);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await RunState(run.Id)).Status);
        Assert.AreEqual(0, _purchaser.Purchases);
        Assert.AreEqual(1, _extractor.Extractions);
    }

    [TestMethod]
    public async Task ReusableUnlockNodeHonorsAutomaticConfigurationAndManualApprovalWithoutAParserTask()
    {
        _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value.AutoBuyThreshold = 10;
        _reader.Locks = [new("https://example.test/lock", 5, false)];
        await using var scope = _services.CreateAsyncScope();
        var definition = await scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>().SeedAsync();
        var definitions = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionService>();
        var input = JsonSerializer.Serialize(new PostParserInput {Link = "https://example.test/post", SourceHint = "SoulPlus"}, WorkflowJson.Options);
        var automatic = await definitions.RunManuallyAsync(definition, input);
        await Execute(automatic.Id);
        Assert.AreEqual(WorkflowRunStatus.Success, (await RunState(automatic.Id)).Status);
        Assert.AreEqual(1, _purchaser.Purchases);

        _reader.Locks = [new("https://example.test/lock", 5, false)];
        await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().Set<WorkflowActivityDbModel>()
            .Where(a => a.WorkflowDefinitionId == definition && a.Kind == PostParserWorkflow.UnlockContent)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ConfigJson, "{}"));
        var manual = await definitions.RunManuallyAsync(definition, input);
        await Execute(manual.Id);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await RunState(manual.Id)).Status);
        Assert.AreEqual(1, _purchaser.Purchases, "Disabling automatic purchase must leave this run waiting.");
        await using (var resumeScope = _services.CreateAsyncScope())
            await resumeScope.ServiceProvider.GetRequiredService<IWorkflowRunResumer>().ResumeAsync(manual.Id,
                JsonSerializer.Serialize(new PostParserPurchaseSignal {LockUrls = ["https://example.test/lock"]}, WorkflowJson.Options));
        await Execute(manual.Id);
        Assert.AreEqual(WorkflowRunStatus.Success, (await RunState(manual.Id)).Status);
        Assert.AreEqual(2, _purchaser.Purchases);
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().Set<PostParserTaskDbModel>().CountAsync());
    }

    [TestMethod]
    public async Task SavedSoulPlusTaskKeepsConfiguredPurchaseThreshold()
    {
        _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value.AutoBuyThreshold = 10;
        _reader.Locks = [new("https://example.test/lock", 5, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        await Execute((await TaskState(id)).WorkflowRunId!.Value);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(1, _purchaser.Purchases);
        Assert.IsTrue(_reader.Reads >= 3, "Purchase state and account balance must be refreshed around a purchase.");
    }

    [TestMethod]
    public async Task UnknownPriceSavesPartialContentAndWaitsWithoutPurchasing()
    {
        _reader.Locks = [new("https://example.test/lock", null, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        await Execute((await TaskState(id)).WorkflowRunId!.Value);
        var pending = await TaskState(id);
        Assert.AreEqual(WorkflowRunStatus.Waiting, pending.WorkflowStatus);
        Assert.IsNotNull(pending.ContentSnapshot);
        Assert.IsFalse(pending.Results![PostParseTarget.DownloadInfo]!["isComplete"]!.GetValue<bool>());
        Assert.IsNull(pending.CompletedAt);
        Assert.AreEqual(0, _purchaser.Purchases);
        Assert.AreEqual(1, _extractor.Extractions);
    }

    [TestMethod]
    public async Task LegacyResultsAndUserscriptDeleteStatusesRemainAvailable()
    {
        var id = await Add(source: PostParserSource.SoulPlus);
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            await db.Set<PostParserTaskDbModel>().Where(t => t.Id == id).ExecuteUpdateAsync(s =>
                s.SetProperty(t => t.Results, "{\"DownloadInfo\":{\"resources\":[]}}"));
        }
        await Dispatch();
        Assert.IsNull((await TaskState(id)).WorkflowRunId);
        await using var check = _services.CreateAsyncScope();
        var service = check.ServiceProvider.GetRequiredService<IPostParserTaskService>();
        var link = "https://example.test/post/1";
        Assert.AreEqual(PostParserTaskStatus.Complete, (await service.GetStatusesByLinks(PostParserSource.SoulPlus, [link]))[link]);
        Assert.AreEqual(1, await service.DeleteByLinks(PostParserSource.SoulPlus, [link]));
        Assert.AreEqual(PostParserTaskStatus.Deleted, (await service.GetStatusesByLinks(PostParserSource.SoulPlus, [link]))[link]);
    }

    [TestMethod]
    public async Task ManualTriggerRejectsTaskImpersonationAndAmbiguousInput()
    {
        var trigger = new PostParserManualTrigger();
        Assert.IsFalse(trigger.Matches(new PostParserInput {Text = "input"}, null));
        Assert.ThrowsException<InvalidOperationException>(() => trigger.BuildManualPayload(null, "{\"taskId\":1,\"text\":\"input\"}"));
        Assert.ThrowsException<InvalidOperationException>(() => trigger.BuildManualPayload(null, "{\"text\":\"input\",\"link\":\"https://example.test\"}"));
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task MissingAiStillPersistsContentAndResumesAfterConfiguration()
    {
        AiFeatureConfigDbModel configured;
        await using (var scope = _services.CreateAsyncScope())
        {
            var features = scope.ServiceProvider.GetRequiredService<IAiFeatureService>();
            configured = (await features.GetConfigAsync(AiFeature.PostParser))! with { };
            await features.SaveConfigAsync(new() {Feature = AiFeature.PostParser, UseDefault = false});
        }
        var id = await Add();
        await Dispatch();
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        await Execute(runId);
        var waiting = await TaskState(id);
        Assert.AreEqual(WorkflowRunStatus.Waiting, waiting.WorkflowStatus);
        Assert.AreEqual("awaitingAi", waiting.ParsingState);
        Assert.AreEqual("Post title", waiting.ContentSnapshot!.Title);
        Assert.AreEqual("reply-2", waiting.ContentSnapshot.Comments.Single().Id);
        Assert.AreEqual(0, _extractor.Extractions);
        await using (var scope = _services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAiFeatureService>().SaveConfigAsync(configured);
            await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().Retry(id);
        }
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(1, _reader.Reads);
    }

    [TestMethod]
    public async Task AffordableItemsAreBoughtWhileExpensiveItemsRemainPending()
    {
        _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value.AutoBuyThreshold = 5;
        _reader.Locks = [new("https://example.test/cheap", 5, false), new("https://example.test/dear", 20, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        await Execute((await TaskState(id)).WorkflowRunId!.Value);
        var task = await TaskState(id);
        Assert.AreEqual(1, _purchaser.Purchases);
        Assert.AreEqual(WorkflowRunStatus.Waiting, task.WorkflowStatus);
        Assert.AreEqual("awaitingPurchase", task.ParsingState);
        Assert.AreEqual(1, task.ContentSnapshot!.Locks.Count(l => !l.IsBought));
        Assert.IsFalse(task.Results![PostParseTarget.DownloadInfo]!["isComplete"]!.GetValue<bool>());
        Assert.IsNull(task.CompletedAt);
    }

    [TestMethod]
    [DataRow("expired", "possiblyExpired")]
    [DataRow("unknown", "awaitingPurchase")]
    public async Task UncertainPostWaitsAndExplicitPurchaseResumesTheSameRun(string availability, string state)
    {
        _analyzer.Status = availability;
        _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value.AutoBuyThreshold = 10;
        _reader.Locks = [new("https://example.test/lock", 5, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        var task = await TaskState(id);
        await Execute(task.WorkflowRunId!.Value);
        var pending = await TaskState(id);
        Assert.AreEqual(state, pending.ParsingState);
        Assert.AreEqual(0, _purchaser.Purchases);
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>()
                .PurchaseAndResumeAsync(id, pending.Revision, ["https://example.test/lock"], 5, CancellationToken.None);
        Assert.AreEqual("postParser", _services.GetRequiredService<BTaskManager>().Tasks
            .Single(t => t.Id == $"workflow.run.{task.WorkflowRunId}").Task.ConcurrencyGroup);
        await Execute(task.WorkflowRunId.Value);
        Assert.AreEqual(1, _purchaser.Purchases);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(task.WorkflowRunId, (await TaskState(id)).WorkflowRunId);
        Assert.AreEqual(availability, (await TaskState(id)).Availability!.Status,
            "An explicit purchase approval must not rewrite the evidence-based availability assessment.");
    }

    [TestMethod]
    [DataRow("expired")]
    [DataRow("unknown")]
    public async Task OneClickPurchaseRevalidatesLimitsQuoteAndDisplayedTotal(string availability)
    {
        _analyzer.Status = availability;
        var options = _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value;
        options.AutoBuyThreshold = 10;
        options.MinimumRemainingCoins = 96;
        _reader.Locks = [new("https://example.test/lock", 5, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        var task = await TaskState(id);
        await Execute(task.WorkflowRunId!.Value);
        Task Buy(decimal maximum = 5) => Purchase(id, task.Revision, maximum);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Buy());
        Assert.AreEqual(0, _purchaser.Purchases);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await TaskState(id)).WorkflowStatus);
        options.MinimumRemainingCoins = 0;
        options.AutoBuyThreshold = 4;
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Buy());
        options.AutoBuyThreshold = 10;
        _reader.Locks = [new("https://example.test/lock", 7, false)];
        await Buy();
        await Execute(task.WorkflowRunId.Value);
        Assert.AreEqual(0, _purchaser.Purchases, "The saved five-coin quote must not authorize a seven-coin price at execution.");
        Assert.AreEqual(7m, (await TaskState(id)).PurchaseQuote!.EligibleTotal);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Buy());
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Buy(0));
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await TaskState(id)).WorkflowStatus);
        await Buy(7);
        await Execute(task.WorkflowRunId.Value);
        Assert.AreEqual(1, _purchaser.Purchases);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task OneClickPurchaseRechecksLimitsAfterItWasQueued(bool lowerThreshold)
    {
        _analyzer.Status = "expired";
        var options = _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value;
        options.AutoBuyThreshold = 5;
        _reader.Locks = [new("https://example.test/lock", 5, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        var task = await TaskState(id);
        await Execute(task.WorkflowRunId!.Value);
        await Purchase(id, task.Revision, 5);
        if (lowerThreshold) options.AutoBuyThreshold = 4;
        else _reader.Balance = 4;
        await Execute(task.WorkflowRunId.Value);
        Assert.AreEqual(0, _purchaser.Purchases);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await TaskState(id)).WorkflowStatus);
    }

    [TestMethod]
    public async Task ExplicitPurchaseAlsoSupportsHealthyPostsWithoutBypassingConfiguredLimits()
    {
        _reader.Locks = [new("https://example.test/lock", 5, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        var task = await TaskState(id);
        await Execute(task.WorkflowRunId!.Value);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Purchase(id, task.Revision, 5));
        Assert.AreEqual(0, _purchaser.Purchases);
        _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value.AutoBuyThreshold = 5;
        await Purchase(id, task.Revision, 5);
        await Execute(task.WorkflowRunId.Value);
        Assert.AreEqual(1, _purchaser.Purchases);
        Assert.AreEqual(task.WorkflowRunId, (await TaskState(id)).WorkflowRunId);
    }

    [TestMethod]
    [DataRow("expired", false)]
    [DataRow("unknown", false)]
    [DataRow("expired", true)]
    public async Task FreeUnlockPrecedesRiskAssessmentAndNeverIncludesPaidItems(string availability, bool paidRemainder)
    {
        _analyzer.Status = availability;
        _reader.Balance = null;
        var options = _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value;
        options.AutoBuyThreshold = 10;
        options.MinimumRemainingCoins = 1000;
        _reader.Locks = [new("https://example.test/free", 0, false)];
        if (paidRemainder) _reader.Locks.Add(new("https://example.test/paid", 1, false));
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        var runId = (await TaskState(id)).WorkflowRunId!.Value;
        await Execute(runId);
        var task = await TaskState(id);
        Assert.AreEqual(1, _purchaser.Purchases);
        Assert.IsTrue(task.ContentSnapshot!.Locks.Single(l => l.Url!.EndsWith("/free")).IsBought);
        Assert.AreEqual(paidRemainder ? WorkflowRunStatus.Waiting : WorkflowRunStatus.Success, task.WorkflowStatus);
        Assert.AreEqual(paidRemainder ? 1 : 0, _analyzer.Assessments,
            "No availability inference is needed to authorize an explicit free offer.");
        if (paidRemainder) Assert.IsFalse(task.ContentSnapshot.Locks.Single(l => l.Url!.EndsWith("/paid")).IsBought);
    }

    [TestMethod]
    public async Task FreeUnlockWorksBeforeMissingAiButExtractionStillWaitsForAi()
    {
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IAiFeatureService>().SaveConfigAsync(new() {Feature = AiFeature.PostParser, UseDefault = false});
        _reader.Locks = [new("https://example.test/free", 0, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        await Execute((await TaskState(id)).WorkflowRunId!.Value);
        Assert.AreEqual(1, _purchaser.Purchases);
        Assert.AreEqual("awaitingAi", (await TaskState(id)).ParsingState);
        Assert.AreEqual(0, _analyzer.Assessments);
        Assert.AreEqual(0, _extractor.Extractions);
    }

    [TestMethod]
    public async Task ZeroPriceThatChangesDuringRefreshIsNotRetriedAsPaidInTheSameRun()
    {
        _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value.AutoBuyThreshold = 10;
        _reader.Locks = [new("https://example.test/free", 0, false)];
        _reader.OnRead = count => { if (count >= 2) _reader.Locks = [new("https://example.test/free", 1, false)]; };
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        await Execute((await TaskState(id)).WorkflowRunId!.Value);
        Assert.AreEqual(0, _purchaser.Purchases);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(1m, (await TaskState(id)).PurchaseQuote!.EligibleTotal);
    }

    [TestMethod]
    public async Task QueuedPurchaseUsesFrozenNodeAndRejectsLaterAccountChanges()
    {
        _analyzer.Status = "expired";
        _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value.AutoBuyThreshold = 5;
        _reader.Locks = [new("https://example.test/lock", 5, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        var task = await TaskState(id);
        await Execute(task.WorkflowRunId!.Value);
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            await db.WorkflowActivities.Where(a => a.WorkflowDefinitionId == task.WorkflowDefinitionId && a.Order == 1)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Kind, "not.the.frozen.node"));
        }
        await Purchase(id, task.Revision, 5);
        _purchaser.AccountKey = "different-account";
        await Execute(task.WorkflowRunId.Value);
        Assert.AreEqual(0, _purchaser.Purchases);
        Assert.AreEqual(WorkflowRunStatus.Failed, (await TaskState(id)).WorkflowStatus);
    }

    [TestMethod]
    public async Task RetryRefreshesLockedCheckpointAfterPurchaseReadFailureBeforeAskingAi()
    {
        _analyzer.Status = "expired";
        _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value.AutoBuyThreshold = 5;
        _reader.Locks = [new("https://example.test/lock", 5, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        var original = await TaskState(id);
        var runId = original.WorkflowRunId!.Value;
        await Execute(runId);
        await Purchase(id, original.Revision, 5);
        _reader.OnRead = _ =>
        {
            if (_purchaser.Purchases > 0) throw new System.IO.IOException("Fixture post-purchase refresh failed.");
        };
        await Execute(runId);
        var failed = await RunState(runId);
        Assert.AreEqual(WorkflowRunStatus.Failed, failed.Status);
        Assert.AreEqual(1, failed.CurrentStepIndex);
        Assert.IsNull(failed.PendingSignalJson, "The runner already consumed this attempt's purchase signal.");
        Assert.IsTrue(((PostParserContentItem)WorkflowItemSnapshot.Restore(failed.CurrentItemJson!))
            .Content.Locks.Single().IsBought == false, "The durable checkpoint still predates the purchase.");
        Assert.IsTrue(_reader.Locks.Single().IsBought, "The source has already committed the purchase.");
        var assessments = _analyzer.Assessments;

        _reader.OnRead = null;
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IAiFeatureService>()
                .SaveConfigAsync(new() {Feature = AiFeature.PostParser, UseDefault = false});
        await Retry(id);
        await Execute(runId);

        var recovered = await TaskState(id);
        Assert.AreEqual(original.WorkflowRunId, recovered.WorkflowRunId);
        Assert.AreEqual(original.Revision, recovered.Revision);
        Assert.AreEqual(1, _purchaser.Purchases);
        Assert.AreEqual(assessments, _analyzer.Assessments, "Already unlocked content needs no new availability analysis.");
        Assert.IsTrue(recovered.ContentSnapshot!.Locks.Single().IsBought);
        Assert.AreEqual(WorkflowRunStatus.Waiting, recovered.WorkflowStatus);
        Assert.AreEqual("awaitingAi", recovered.ParsingState);
        Assert.AreEqual(2, (await RunState(runId)).CurrentStepIndex,
            "Unlock must finish; only the separate extraction node may wait for AI configuration.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MixedPriceWaitingCheckpointResumesSameRunUsingFirstQuoteOrFreshUnlockedState(bool alreadyUnlocked)
    {
        const string post = "https://www.north-plus.net/read.php?tid-42.html";
        const string payment = "https://www.north-plus.net/job.php?action=buytopic&tid=42&pid=tpc&verify=fixture";
        var options = _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value;
        options.AutoBuyThreshold = 0;
        _reader.SourceUrl = post;
        _reader.Locks = [new(payment, 2, false) {Floor = "0"}, new(payment, 5, false) {Floor = "0"}];
        var id = await Add(post, PostParserSource.SoulPlus);
        await Dispatch();
        var original = await TaskState(id);
        var runId = original.WorkflowRunId!.Value;
        await Execute(runId);
        var waiting = await RunState(runId);
        Assert.AreEqual(WorkflowRunStatus.Waiting, waiting.Status);
        var saved = (PostParserContentItem)WorkflowItemSnapshot.Restore(waiting.CurrentItemJson!);
        CollectionAssert.AreEqual(new decimal?[] {2, 5}, saved.Content.Locks.Select(l => l.Price).ToArray());
        var assessments = _analyzer.Assessments;

        options.AutoBuyThreshold = 10;
        if (alreadyUnlocked) _reader.Locks = _reader.Locks.Select(l => l with {IsBought = true}).ToList();
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IWorkflowRunResumer>().ResumeAsync(runId, "{}");
        await Execute(runId);

        var completed = await TaskState(id);
        Assert.AreEqual(runId, completed.WorkflowRunId);
        Assert.AreEqual(original.Revision, completed.Revision);
        Assert.AreEqual(WorkflowRunStatus.Success, completed.WorkflowStatus);
        Assert.AreEqual(alreadyUnlocked ? 0 : 1, _purchaser.Purchases);
        Assert.AreEqual(alreadyUnlocked ? 100m : 98m, _reader.Balance);
        if (alreadyUnlocked) Assert.AreEqual(assessments, _analyzer.Assessments);
    }

    [TestMethod]
    public async Task RetryWithoutSignalKeepsCheckpointUrlsAndPriceCeilings()
    {
        _analyzer.Status = "expired";
        _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value.AutoBuyThreshold = 10;
        _reader.Locks = [new("https://example.test/stable", 5, false), new("https://example.test/increased", 5, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        var original = await TaskState(id);
        var runId = original.WorkflowRunId!.Value;
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await RunState(runId)).Status);
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.Where(r => r.Id == runId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Interrupted)
                    .SetProperty(r => r.PendingSignalJson, (string?)null));

        _analyzer.Status = "noExpiryReported";
        _reader.Locks = [new("https://example.test/stable", 5, false), new("https://example.test/increased", 7, false),
            new("https://example.test/new", 1, false)];
        await Retry(id);
        await Execute(runId);

        var recovered = await TaskState(id);
        Assert.AreEqual(original.WorkflowRunId, recovered.WorkflowRunId);
        Assert.AreEqual(original.Revision, recovered.Revision);
        Assert.AreEqual(1, _purchaser.Purchases);
        Assert.AreEqual(95m, _reader.Balance);
        Assert.IsTrue(_reader.Locks.Single(l => l.Url!.EndsWith("/stable")).IsBought);
        Assert.IsFalse(_reader.Locks.Single(l => l.Url!.EndsWith("/increased")).IsBought);
        Assert.IsFalse(_reader.Locks.Single(l => l.Url!.EndsWith("/new")).IsBought);
        Assert.AreEqual(WorkflowRunStatus.Waiting, recovered.WorkflowStatus);
    }

    [TestMethod]
    public void LegacyUnsubstantiatedExpiryIsProjectedWithoutChangingSavedHistory()
    {
        var raw = new PostParserTaskDbModel
        {
            Link = "https://example.test/post", ParsingState = "possiblyExpired", ParsingMessage = "model inference",
            AvailabilityJson = "{\"status\":\"expired\",\"evidence\":[\"老了\"],\"reason\":\"model inference\"}",
            Results = "{\"DownloadInfo\":{\"isComplete\":false,\"availability\":{\"status\":\"expired\"}}}"
        };
        var beforeAvailability = raw.AvailabilityJson;
        var beforeResults = raw.Results;
        var view = raw.ToDomainModel();
        Assert.AreEqual("unknown", view.Availability!.Status);
        Assert.AreEqual("awaitingPurchase", view.ParsingState);
        Assert.AreEqual(PostAvailabilityEvidencePolicy.InsufficientEvidence, view.ParsingMessage);
        Assert.AreEqual(beforeAvailability, raw.AvailabilityJson);
        Assert.AreEqual(beforeResults, raw.Results);
        Assert.AreEqual("possiblyExpired", raw.ParsingState);
    }

    [TestMethod]
    public async Task ConcurrentUnlockClicksQueueOnceAndStayRunningThroughPurchaseAndAi()
    {
        _analyzer.Status = "expired";
        _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value.AutoBuyThreshold = 5;
        _reader.Locks = [new("https://example.test/lock", 5, false), new("https://example.test/lock", 5, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        var task = await TaskState(id);
        await Execute(task.WorkflowRunId!.Value);
        Assert.AreEqual(5m, (await TaskState(id)).PurchaseQuote!.EligibleTotal);
        await Task.WhenAll(Purchase(id, task.Revision, 5), Purchase(id, task.Revision, 5), Purchase(id, task.Revision, 5));
        Assert.AreEqual(WorkflowRunStatus.Pending, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(1, _services.GetRequiredService<BTaskManager>().Tasks.Count(t => t.Id == $"workflow.run.{task.WorkflowRunId}"));

        _purchaser.Hold = true;
        _extractor.Hold = true;
        _extractor.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = Execute(task.WorkflowRunId.Value);
        try
        {
            await _purchaser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(WorkflowRunStatus.Running, (await TaskState(id)).WorkflowStatus);
            await Purchase(id, task.Revision, 5);
            Assert.AreEqual(1, _purchaser.Purchases);
            _purchaser.Release.TrySetResult();
            await _extractor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(WorkflowRunStatus.Running, (await TaskState(id)).WorkflowStatus);
            Assert.AreEqual(95m, _reader.Balance);
        }
        finally
        {
            _purchaser.Release.TrySetResult();
            _extractor.Release.TrySetResult();
            await execution;
        }
        Assert.AreEqual(1, _purchaser.Purchases);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
    }

    private async Task Purchase(int id, int revision, decimal maximum)
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>()
            .PurchaseAndResumeAsync(id, revision, ["https://example.test/lock"], maximum, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("  The visible information already supplies the resource.  ")]
    public async Task ManualReadinessContinuesSameRunWithoutPurchasingAndSurvivesReparse(string? reason)
    {
        _analyzer.Status = "expired";
        _reader.Locks = [new("https://example.test/proof", 999999, false)
            {BlockId = "proof-1", ContextText = "Purchase proof, not required for the resource"}];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch();
        var original = await TaskState(id);
        await Execute(original.WorkflowRunId!.Value);
        await using var scope = _services.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>();
        var view = await workflow.GetContentReadinessAsync(id);
        Assert.IsTrue(view.CanConfirm);
        Assert.IsTrue(view.CanContinue);
        var analyses = _analyzer.Assessments;
        var confirmed = await workflow.ConfirmContentReadinessAsync(id, new()
        {
            Revision = view.Revision, WorkflowRunId = view.WorkflowRunId,
            ExpectedContentFingerprint = view.ContentFingerprint!, ExpectedDecisionVersion = view.DecisionVersion,
            NotRequiredBlockIds = ["proof-1"], Reason = reason, ContinueParsing = true
        });
        Assert.AreEqual("manual", confirmed.Basis);
        Assert.AreEqual(reason?.Trim() ?? "", confirmed.History.Single().Reason,
            "Missing notes must remain empty instead of inventing a user explanation.");
        Assert.AreEqual(reason?.Trim() ?? "", confirmed.Assessment!.Decisions.Single().Reason);
        await Execute(original.WorkflowRunId.Value);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(0, _purchaser.Purchases);
        Assert.AreEqual(analyses, _analyzer.Assessments);
        Assert.IsFalse(_reader.Locks.Single().IsBought);
        await Reparse(id);
        var reparsed = await TaskState(id);
        await Execute(reparsed.WorkflowRunId!.Value);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(0, _purchaser.Purchases);
        var repeated = await workflow.GetContentReadinessAsync(id);
        Assert.AreEqual(confirmed.ActiveDecisionId, repeated.ActiveDecisionId);
        Assert.AreEqual(1, repeated.History.Count);
        Assert.AreEqual(original.WorkflowRunId, repeated.History[0].WorkflowRunId);
    }

    [TestMethod]
    public async Task ReadinessRejectsStaleCasAndRevocationNeverChangesBoughtState()
    {
        _analyzer.Status = "expired";
        _reader.Locks = [new("https://example.test/proof", 999999, false) {BlockId = "proof", ContextText = "proof"}];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch(); await Execute((await TaskState(id)).WorkflowRunId!.Value);
        await using var scope = _services.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>();
        var view = await workflow.GetContentReadinessAsync(id);
        var input = new Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.ConfirmPostParserContentReadinessInput
        {Revision = view.Revision, WorkflowRunId = view.WorkflowRunId, ExpectedContentFingerprint = view.ContentFingerprint!,
            ExpectedDecisionVersion = view.DecisionVersion, NotRequiredBlockIds = ["proof"], Reason = "Auxiliary proof"};
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => workflow.ConfirmContentReadinessAsync(id, input with {Reason = new string('x', 1001)}));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => workflow.ConfirmContentReadinessAsync(id, input with {Revision = input.Revision + 1}));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => workflow.ConfirmContentReadinessAsync(id, input with {WorkflowRunId = 99999}));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => workflow.ConfirmContentReadinessAsync(id, input with {ExpectedContentFingerprint = new string('0', 64)}));
        var accepted = await workflow.ConfirmContentReadinessAsync(id, input);
        Assert.AreEqual("Auxiliary proof", accepted.History.Single().Reason);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => workflow.ConfirmContentReadinessAsync(id, input));
        _reader.Balance++;
        var sameEvidence = await workflow.RefreshContentReadinessAsync(id, RefreshInput(accepted));
        Assert.AreEqual(accepted.ActiveDecisionId, sameEvidence.ActiveDecisionId);
        Assert.AreEqual("manual", sameEvidence.Basis);
        Assert.IsFalse(sameEvidence.ParsedResultMayBeStale);
        var revoked = await workflow.RevokeContentReadinessAsync(id, new()
        {Revision = view.Revision, WorkflowRunId = view.WorkflowRunId, ExpectedContentFingerprint = view.ContentFingerprint!,
            ExpectedDecisionVersion = accepted.DecisionVersion, DecisionId = accepted.ActiveDecisionId!});
        Assert.AreEqual("needsUnlock", revoked.Status);
        Assert.IsNull(revoked.ActiveDecisionId);
        Assert.AreEqual(2, revoked.History.Count);
        Assert.AreEqual("revoke", revoked.History[1].Action);
        Assert.IsFalse(revoked.ContentSnapshot!.Locks.Single().IsBought);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await TaskState(id)).WorkflowStatus);
        await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.Where(r => r.Id == view.WorkflowRunId)
            .ExecuteUpdateAsync(update => update.SetProperty(r => r.CurrentStepIndex, 2));
        var otherStep = await workflow.GetContentReadinessAsync(id);
        Assert.IsTrue(otherStep.CanConfirm);
        Assert.IsFalse(otherStep.CanContinue, "A different waiting node must never advertise content-unlock continuation.");
    }

    [TestMethod]
    public async Task ChangedFreshContentInvalidatesReadinessSignalWithoutPurchasing()
    {
        _analyzer.Status = "expired";
        _reader.Locks = [new("https://example.test/proof", 5, false) {BlockId = "proof", ContextText = "proof"}];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch(); var task = await TaskState(id); await Execute(task.WorkflowRunId!.Value);
        await using var scope = _services.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>();
        var view = await workflow.GetContentReadinessAsync(id);
        await workflow.ConfirmContentReadinessAsync(id, new()
        {Revision = view.Revision, WorkflowRunId = view.WorkflowRunId, ExpectedContentFingerprint = view.ContentFingerprint!,
            ExpectedDecisionVersion = view.DecisionVersion, NotRequiredBlockIds = ["proof"], Reason = "Auxiliary", ContinueParsing = true});
        _reader.Locks = [new("https://example.test/proof", 5, false) {BlockId = "proof", ContextText = "Download password now required"}];
        await Execute(task.WorkflowRunId.Value);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(0, _purchaser.Purchases);
        var changed = await workflow.GetContentReadinessAsync(id);
        Assert.IsTrue(changed.HasStaleDecision);
        Assert.IsNull(changed.Assessment);
    }

    [TestMethod]
    public async Task LegacyUnidentifiedLocksRequireFreshReadBeforeManualConfirmation()
    {
        _analyzer.Status = "expired";
        _reader.Locks = [new("https://example.test/proof", 50, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch(); await Execute((await TaskState(id)).WorkflowRunId!.Value);
        await using var scope = _services.CreateAsyncScope();
        var view = await scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>().GetContentReadinessAsync(id);
        Assert.IsFalse(view.CanConfirm);
        Assert.AreEqual("needsUnlock", view.Status);
    }

    [TestMethod]
    public async Task EvidenceRefreshUpgradesLegacyLocksWithoutAnalysisPaymentOrChangingTheWaitingCheckpoint()
    {
        _analyzer.Status = "expired";
        _reader.Locks = [new("https://example.test/proof", 50, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch(); var task = await TaskState(id); await Execute(task.WorkflowRunId!.Value);
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var workflow = scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>();
        var view = await workflow.GetContentReadinessAsync(id);
        var before = await db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        var run = await RunState(task.WorkflowRunId.Value);
        var counts = (_reader.Reads, _analyzer.Assessments, _extractor.Extractions);
        _reader.Locks = [new("https://example.test/proof", 50, false) {BlockId = "proof", ContextText = "purchase proof"}];
        var fresh = await workflow.RefreshContentReadinessAsync(id, RefreshInput(view));
        Assert.IsTrue(fresh.CanConfirm);
        Assert.IsTrue(fresh.CanContinue);
        Assert.IsTrue(fresh.ParsedResultMayBeStale);
        Assert.AreEqual("proof", fresh.ContentSnapshot!.Locks.Single().BlockId);
        Assert.IsFalse(fresh.ContentSnapshot.Locks.Single().IsBought);
        Assert.AreEqual(counts.Reads + 1, _reader.Reads);
        Assert.AreEqual(counts.Assessments, _analyzer.Assessments);
        Assert.AreEqual(counts.Extractions, _extractor.Extractions);
        Assert.AreEqual(0, _purchaser.Purchases);
        var stored = await db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.AreEqual(before.Results, stored.Results);
        Assert.AreEqual(before.Revision, stored.Revision);
        Assert.AreEqual(before.WorkflowRunId, stored.WorkflowRunId);
        var unchanged = await RunState(task.WorkflowRunId.Value);
        Assert.AreEqual(run.Status, unchanged.Status);
        Assert.AreEqual(run.CurrentStepIndex, unchanged.CurrentStepIndex);
        Assert.AreEqual(run.CurrentItemJson, unchanged.CurrentItemJson);
        Assert.AreEqual(run.WaitPromptJson, unchanged.WaitPromptJson);
        Assert.AreEqual(run.PendingSignalJson, unchanged.PendingSignalJson);
        Assert.AreEqual(1, await db.WorkflowRuns.CountAsync());
        await workflow.ConfirmContentReadinessAsync(id, new()
        {
            Revision = fresh.Revision, WorkflowRunId = fresh.WorkflowRunId,
            ExpectedContentFingerprint = fresh.ContentFingerprint!, ExpectedDecisionVersion = fresh.DecisionVersion,
            NotRequiredBlockIds = ["proof"], Reason = "Visible information suffices", ContinueParsing = true
        });
        await Execute(task.WorkflowRunId.Value);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.IsFalse((await workflow.GetContentReadinessAsync(id)).ParsedResultMayBeStale);
        Assert.AreEqual(0, _purchaser.Purchases);
        Assert.AreEqual(counts.Assessments, _analyzer.Assessments);
    }

    [TestMethod]
    public async Task EvidenceRefreshRejectsStaleApprovalAndActiveExecutionBeforeReading()
    {
        _analyzer.Status = "expired";
        _reader.Locks = [new("https://example.test/proof", 50, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch(); await Execute((await TaskState(id)).WorkflowRunId!.Value);
        await using var scope = _services.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>();
        var view = await workflow.GetContentReadinessAsync(id);
        var input = RefreshInput(view);
        var reads = _reader.Reads;
        foreach (var stale in new[] {input with {Revision = input.Revision + 1}, input with {WorkflowRunId = 98765},
                     input with {ExpectedContentFingerprint = new string('0', 64)}, input with {ExpectedDecisionVersion = 98765}})
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => workflow.RefreshContentReadinessAsync(id, stale));
        await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.Where(r => r.Id == view.WorkflowRunId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Running));
        Assert.IsFalse((await workflow.GetContentReadinessAsync(id)).CanRefresh);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => workflow.RefreshContentReadinessAsync(id, input));
        Assert.AreEqual(reads, _reader.Reads);
    }

    [DataTestMethod]
    [DataRow("decision")]
    [DataRow("run")]
    [DataRow("source")]
    public async Task EvidenceRefreshNeverOverwritesConcurrentChanges(string change)
    {
        _analyzer.Status = "expired";
        _reader.Locks = [new("https://example.test/proof", 50, false) {BlockId = "proof", ContextText = "proof"}];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch(); await Execute((await TaskState(id)).WorkflowRunId!.Value);
        await using var scope = _services.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>();
        var view = await workflow.GetContentReadinessAsync(id);
        _reader.Hold = true;
        _reader.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = workflow.RefreshContentReadinessAsync(id, RefreshInput(view));
        await _reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var other = _services.CreateAsyncScope();
            if (change == "decision")
                await other.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>().ConfirmContentReadinessAsync(id, new()
                {
                    Revision = view.Revision, WorkflowRunId = view.WorkflowRunId,
                    ExpectedContentFingerprint = view.ContentFingerprint!, ExpectedDecisionVersion = view.DecisionVersion,
                    NotRequiredBlockIds = ["proof"], Reason = "Confirmed while the read was in flight"
                });
            else if (change == "run")
                await other.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.Where(r => r.Id == view.WorkflowRunId)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Running));
            else
                await other.ServiceProvider.GetRequiredService<BakabaseDbContext>().PostParserTasks.Where(t => t.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Link, "https://example.test/changed-post"));
        }
        finally { _reader.Release.TrySetResult(); }
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => refresh);
        var after = await workflow.GetContentReadinessAsync(id);
        Assert.AreEqual(change == "decision" ? "manual" : null, after.Basis);
        Assert.AreEqual(change == "decision" ? 1 : 0, after.History.Count);
        Assert.AreEqual(view.ContentFingerprint, after.ContentFingerprint);
        Assert.AreEqual(0, _purchaser.Purchases);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedOrCancelledEvidenceRefreshPreservesTheSavedSnapshot(bool cancel)
    {
        _analyzer.Status = "expired";
        _reader.Locks = [new("https://example.test/proof", 50, false)];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch(); await Execute((await TaskState(id)).WorkflowRunId!.Value);
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var workflow = scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>();
        var view = await workflow.GetContentReadinessAsync(id);
        var before = await db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        using var cts = new CancellationTokenSource();
        _reader.OnRead = _ => { if (cancel) cts.Cancel(); else throw new InvalidOperationException("reader failure"); };
        if (cancel) await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => workflow.RefreshContentReadinessAsync(id, RefreshInput(view), cts.Token));
        else await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => workflow.RefreshContentReadinessAsync(id, RefreshInput(view), cts.Token));
        var after = await db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.AreEqual(before.ContentSnapshotJson, after.ContentSnapshotJson);
        Assert.AreEqual(before.ContentReadinessJson, after.ContentReadinessJson);
        Assert.AreEqual(before.Results, after.Results);
        Assert.AreEqual(0, _purchaser.Purchases);
    }

    [TestMethod]
    public async Task EvidenceRefreshIgnoresVolatileFieldsAndStaleResultsStayBlockedUntilSuccessfulExtraction()
    {
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch(); await Execute((await TaskState(id)).WorkflowRunId!.Value);
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var workflow = scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>();
        var view = await workflow.GetContentReadinessAsync(id);
        _reader.Balance++;
        _reader.CapturedAt = DateTimeOffset.UtcNow;
        var unchanged = await workflow.RefreshContentReadinessAsync(id, RefreshInput(view));
        Assert.AreEqual(view.ContentFingerprint, unchanged.ContentFingerprint);
        Assert.IsFalse(unchanged.ParsedResultMayBeStale);
        _reader.MainHtml = "changed resource credentials";
        var changed = await workflow.RefreshContentReadinessAsync(id, RefreshInput(unchanged));
        Assert.IsTrue(changed.ParsedResultMayBeStale);
        var old = await db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.IsTrue(PostParserSavedResults.ReadDownloadInfo(old.Results!).IsComplete, "The old output is still available as history.");
        Assert.ThrowsException<ArgumentException>(() => PostParserSavedResults.ReadDownloadInfo(old));
        var originalResult = PostParserSavedResults.ReadDownloadInfo(old.Results!);
        var originalInput = new PostParserInput {TaskId = id, Revision = old.Revision};
        await workflow.SaveResultAsync(originalInput, old.WorkflowRunId!.Value, originalResult, default);
        Assert.IsTrue((await workflow.GetContentReadinessAsync(id)).ParsedResultMayBeStale,
            "A link-check saving an old complete result is not a new extraction.");
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => workflow.SaveResultAsync(originalInput,
            old.WorkflowRunId.Value, originalResult, default, unchanged.ContentFingerprint));
        _extractor.FailuresRemaining = 1;
        await Reparse(id); await Execute((await TaskState(id)).WorkflowRunId!.Value);
        Assert.IsTrue((await workflow.GetContentReadinessAsync(id)).ParsedResultMayBeStale);
        await Retry(id); await Execute((await TaskState(id)).WorkflowRunId!.Value);
        Assert.IsFalse((await workflow.GetContentReadinessAsync(id)).ParsedResultMayBeStale);
        Assert.IsTrue(PostParserSavedResults.HasCompletedDownloadResult(await db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == id)));
    }

    private static Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.RefreshPostParserContentReadinessInput RefreshInput(
        Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.PostParserContentReadinessView view) => new()
    {Revision = view.Revision, WorkflowRunId = view.WorkflowRunId, ExpectedContentFingerprint = view.ContentFingerprint!, ExpectedDecisionVersion = view.DecisionVersion};

    [TestMethod]
    public async Task EvidenceOptionalBlocksCompleteParsingWithoutPaymentOrAdditionalAiCall()
    {
        _reader.Locks = [new("https://example.test/proof", 999999, false)
            {BlockId = "proof", ContextText = "purchase proof only"}];
        _analyzer.Decisions = [new() {BlockId = "proof", Relevance = "optional", Purpose = "purchaseProof", Evidence = ["purchase proof"]}];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch(); var task = await TaskState(id); await Execute(task.WorkflowRunId!.Value);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(0, _purchaser.Purchases);
        Assert.AreEqual(1, _analyzer.Assessments);
        Assert.IsFalse(_reader.Locks.Single().IsBought);
        Assert.AreEqual("analysis", (await TaskState(id)).ContentSnapshot!.Readiness!.Basis);
    }

    [TestMethod]
    public async Task SuccessfulRequiredPurchaseReassessesFreshAuxiliaryEvidenceWithoutAnotherPaymentRound()
    {
        _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value.AutoBuyThreshold = 10;
        _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value.MinimumRemainingCoins = 0;
        _reader.Locks = [new("https://example.test/credential", 1, false) {BlockId = "credential", ContextText = "download password"},
            new("https://example.test/proof", 999999, false) {BlockId = "proof", ContextText = "purchase proof only"}];
        _analyzer.Decisions = [new() {BlockId = "credential", Relevance = "required", Purpose = "downloadCredentials", Evidence = ["download password"]},
            new() {BlockId = "proof", Relevance = "optional", Purpose = "purchaseProof", Evidence = ["purchase proof"]}];
        var id = await Add(source: PostParserSource.SoulPlus);
        await Dispatch(); var task = await TaskState(id); await Execute(task.WorkflowRunId!.Value);
        Assert.AreEqual(WorkflowRunStatus.Success, (await TaskState(id)).WorkflowStatus);
        Assert.AreEqual(1, _purchaser.Purchases);
        Assert.AreEqual(2, _analyzer.Assessments, "Changed readable evidence requires one fresh analysis, never another payment round.");
        Assert.IsTrue(_reader.Locks.Single(l => l.BlockId == "credential").IsBought);
        Assert.IsFalse(_reader.Locks.Single(l => l.BlockId == "proof").IsBought);
    }

    private sealed class FakeAvailabilityAnalyzer : IPostAvailabilityAnalyzer
    {
        public string Status = "noExpiryReported";
        public int Assessments;
        public List<PostContentBlockDecision>? Decisions;
        public Task<PostAvailabilityAssessment> AnalyzeAsync(PostContent content, CancellationToken ct = default)
        {
            Assessments++;
            return Task.FromResult(new PostAvailabilityAssessment {Status = Status,
                ContentReadiness = Decisions == null ? null : PostContentReadinessPolicy.FromAnalysis(content, Decisions),
                Evidence = [Status == "expired" ? "下载链接失效了" : Status == "restored" ? "已经补档了" : "fixture reply"]});
        }
    }

    private sealed class FakeHealthChecker : IPostLinkHealthChecker
    {
        public Task<PostLinkHealth> CheckAsync(string url, string? accessCode = null, CancellationToken ct = default) =>
            Task.FromResult(new PostLinkHealth {Status = "unknown", Reason = "fixture"});
    }

    private sealed class FakeReader : IPostContentService
    {
        public int Reads;
        public Action<int>? OnRead;
        public bool Hold;
        public decimal? Balance = 100;
        public DateTimeOffset? CapturedAt;
        public string MainHtml = "shared content";
        public string? SourceUrl;
        public List<PostContentLock> Locks = [];
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CanRead(string reference, string? sourceHint = null) => true;
        public async Task<PostContent> ReadAsync(string reference, string? sourceHint = null, CancellationToken ct = default)
        {
            var count = Interlocked.Increment(ref Reads);
            OnRead?.Invoke(count);
            Entered.TrySetResult();
            if (Hold) await Release.Task.WaitAsync(ct);
            return new() {Title = "Post title", MainHtml = MainHtml, CapturedAt = CapturedAt, SourceUrl = SourceUrl, SourceHint = sourceHint, Locks = Locks.ToList(), Balance = Balance,
                Comments = [new() {Id = "reply-2", Floor = "2", Author = "fixture", Html = "reply"}]};
        }
    }

    private sealed class FakeExtractor : IPostDownloadInfoExtractor
    {
        public PostDownloadInfo? Result;
        public int Extractions;
        public int FailuresRemaining;
        public bool Hold;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<PostDownloadInfo> ExtractAsync(PostContent content, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Extractions);
            Entered.TrySetResult();
            if (Hold) await Release.Task.WaitAsync(ct);
            if (FailuresRemaining-- > 0) throw new InvalidOperationException("fixture extraction failure");
            return Result ?? new() {Title = "Extracted title", Resources = [new() {Link = "https://example.test/file.zip", Code = "code", Password = "secret"}]};
        }
    }

    private sealed class FakePurchaser(FakeReader reader) : ISharedContentPurchaser
    {
        public PostParserSource Source => PostParserSource.SoulPlus;
        public string AccountKey { get; set; } = Guid.NewGuid().ToString();
        public int Purchases;
        public bool Hold;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task BuyAsync(string lockUrl, CancellationToken ct)
        {
            Purchases++;
            Entered.TrySetResult();
            if (Hold) await Release.Task.WaitAsync(ct);
            reader.Balance -= reader.Locks.First(l => l.Url == lockUrl).Price;
            reader.Locks = reader.Locks.Select(l => l.Url == lockUrl ? l with {IsBought = true} : l).ToList();
        }
    }
}
