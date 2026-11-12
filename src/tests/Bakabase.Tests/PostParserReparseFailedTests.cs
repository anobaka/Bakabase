using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Service;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostParserReparseFailedTests
{
    private IServiceProvider _services = null!;
    private const string CompleteResult = "{\"DownloadInfo\":{\"isComplete\":true,\"resources\":[]}}";

    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider();
        _services.GetRequiredService<IBOptions<ThirdPartyOptions>>().Value.AutomaticallyParsingPosts = false;
    }

    private async Task<PostParserReparseSelection> Add(WorkflowRunStatus? status = WorkflowRunStatus.Failed,
        string? error = "fixture failure", string? results = null, bool deleted = false)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        WorkflowRunDbModel? run = null;
        if (status.HasValue)
        {
            run = new() {Status = status.Value, StartedAt = DateTime.Now.AddDays(-1),
                CompletedAt = DateTime.Now, ErrorMessage = error, CurrentStepIndex = 2,
                CurrentItemJson = "{\"fixture\":\"saved execution\"}", OutputItemsJson = "[]", StepStatsJson = "[]"};
            db.WorkflowRuns.Add(run);
            await db.SaveChangesAsync();
        }
        var row = new PostParserTaskDbModel {Link = "https://example.test/" + Guid.NewGuid(),
            Revision = 4, WorkflowRunId = run?.Id, CreatedAt = DateTime.UtcNow.AddDays(-2),
            Error = error, Targets = "[1]", Results = results, IsDeleted = deleted,
            ParsingState = results == CompleteResult ? "complete" : null};
        db.PostParserTasks.Add(row);
        await db.SaveChangesAsync();
        return new() {TaskId = row.Id, Revision = row.Revision, WorkflowRunId = row.WorkflowRunId};
    }

    private async Task<PostParserReparseBatchResult> Reparse(params PostParserReparseSelection[] tasks)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>()
            .ReParseFailed(new() {Tasks = tasks.ToList()});
    }

    [TestMethod]
    public async Task FullSelectionQueuesBeyondAPageAndPreservesHistoryAndSchedulerConfiguration()
    {
        var selections = new List<PostParserReparseSelection>();
        for (var i = 0; i < 51; i++) selections.Add(await Add());
        var unrelated = await Add();
        var purchase = _services.GetRequiredService<IBOptions<SoulPlusOptions>>().Value;
        purchase.AutoBuyThreshold = 7;
        purchase.MinimumRemainingCoins = 1234;
        string oldHistory;
        await using (var scope = _services.CreateAsyncScope())
            oldHistory = JsonSerializer.Serialize(await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>()
                .WorkflowRuns.AsNoTracking().OrderBy(r => r.Id).ToListAsync());

        var result = await Reparse(selections.ToArray());

        Assert.AreEqual(51, result.RequestedCount);
        Assert.AreEqual(51, result.QueuedCount);
        Assert.AreEqual(0, result.FailedCount + result.SkippedCount);
        await using var check = _services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var oldIds = selections.Select(s => s.WorkflowRunId!.Value).Append(unrelated.WorkflowRunId!.Value).ToList();
        Assert.AreEqual(oldHistory, JsonSerializer.Serialize(await db.WorkflowRuns.AsNoTracking()
            .Where(r => oldIds.Contains(r.Id)).OrderBy(r => r.Id).ToListAsync()));
        Assert.AreEqual(unrelated.WorkflowRunId, (await db.PostParserTasks.FindAsync(unrelated.TaskId))!.WorkflowRunId);
        foreach (var item in result.Items)
        {
            var row = (await db.PostParserTasks.FindAsync(item.TaskId))!;
            Assert.AreEqual(5, row.Revision);
            Assert.AreEqual(item.WorkflowRunId, row.WorkflowRunId);
            var handler = _services.GetRequiredService<BTaskManager>().Tasks.Single(t => t.Id == $"workflow.run.{item.WorkflowRunId}");
            Assert.AreEqual("postParser", handler.Task.ConcurrencyGroup);
            Assert.AreEqual(BTaskStatus.NotStarted, handler.Task.Status);
        }
        Assert.AreEqual(7, purchase.AutoBuyThreshold);
        Assert.AreEqual(1234, purchase.MinimumRemainingCoins);
        Assert.IsFalse(_services.GetRequiredService<IBOptions<ThirdPartyOptions>>().Value.AutomaticallyParsingPosts);
    }

    [TestMethod]
    public async Task ConcurrentBatchesAndRepeatedConfirmationCreateOneNewRevisionAndRun()
    {
        var selection = await Add();
        var results = await Task.WhenAll(Reparse(selection), Reparse(selection), Reparse(selection));
        Assert.AreEqual(1, results.Sum(r => r.QueuedCount));
        Assert.AreEqual(2, results.Sum(r => r.SkippedCount));
        Assert.AreEqual(0, results.Sum(r => r.FailedCount));
        await using var check = _services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        Assert.AreEqual(2, await db.WorkflowRuns.CountAsync());
        Assert.AreEqual(5, (await db.PostParserTasks.FindAsync(selection.TaskId))!.Revision);
    }

    [TestMethod]
    public async Task CurrentFailureClassificationExcludesActiveWaitingInterruptedAndStaleSuccessfulErrors()
    {
        var failed = await Add();
        var cancelled = await Add(WorkflowRunStatus.Cancelled);
        var targetError = await Add(null, null, "{\"DownloadInfo\":{\"error\":\"saved target failure\"}}");
        var staleSuccess = await Add(WorkflowRunStatus.Success, "old failure", CompleteResult);
        var complete = await Add(null, null, CompleteResult);
        var waiting = await Add(WorkflowRunStatus.Waiting, null);
        var interrupted = await Add(WorkflowRunStatus.Interrupted);
        var pending = await Add(WorkflowRunStatus.Pending);
        var running = await Add(WorkflowRunStatus.Running);
        var deleted = await Add(deleted: true);
        var live = await Add();
        await _services.GetRequiredService<BTaskManager>().Enqueue(BTaskBuilder.Create($"workflow.run.{live.WorkflowRunId}")
            .Persistent().Run(_ => Task.CompletedTask));

        var result = await Reparse(failed, cancelled, targetError, staleSuccess, complete, waiting, interrupted,
            pending, running, deleted, live);

        CollectionAssert.AreEquivalent(new[] {failed.TaskId, cancelled.TaskId, targetError.TaskId},
            result.Items.Where(i => i.Status == "queued").Select(i => i.TaskId).ToArray());
        Assert.AreEqual(8, result.SkippedCount);
        Assert.AreEqual(0, result.FailedCount);
        Assert.AreEqual("TASK_ACTIVE", result.Items.Single(i => i.TaskId == live.TaskId).Reason);
        await using var check = _services.CreateAsyncScope();
        var saved = await check.ServiceProvider.GetRequiredService<BakabaseDbContext>().PostParserTasks.FindAsync(staleSuccess.TaskId);
        Assert.AreEqual(staleSuccess.WorkflowRunId, saved!.WorkflowRunId);
        Assert.AreEqual(CompleteResult, saved.Results);
        Assert.IsNull(saved.Error, "Reconciliation must remove stale failure before the batch checks eligibility.");
    }

    [TestMethod]
    public async Task ChangedConfirmationIsSkippedEvenWhenTheNewRevisionIsAlsoFailed()
    {
        var selection = await Add();
        var changedRun = await Add();
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            await db.PostParserTasks.Where(t => t.Id == selection.TaskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Revision, 5));
            await db.PostParserTasks.Where(t => t.Id == changedRun.TaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowRunId, selection.WorkflowRunId));
        }
        var result = await Reparse(selection, changedRun, new() {TaskId = 999999, Revision = 0});
        Assert.AreEqual(0, result.QueuedCount + result.FailedCount);
        CollectionAssert.AreEqual(new[] {"TASK_CHANGED", "TASK_CHANGED", "TASK_UNAVAILABLE"}, result.Items.Select(i => i.Reason).ToArray());
    }

    [TestMethod]
    public async Task AQueueFailureReportsPartialResultsWithoutLeakingExceptionsOrReplayingFailedChanges()
    {
        var first = await Add();
        var second = await Add();
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            // Isolated test database only: fail one dispatch after reset, before its run commits.
            await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER fixture_queue_failure BEFORE INSERT ON WorkflowRuns " +
                $"WHEN json_extract(NEW.PayloadJson, '$.taskId') = {first.TaskId} BEGIN SELECT RAISE(FAIL, 'private-canary'); END;");
        }
        var result = await Reparse(first, second);
        Assert.AreEqual(1, result.QueuedCount);
        Assert.AreEqual(1, result.FailedCount);
        Assert.AreEqual("QUEUE_FAILED", result.Items[0].Reason);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("private-canary", StringComparison.Ordinal));
        Assert.AreEqual(second.TaskId, result.Items.Single(i => i.Status == "queued").TaskId);
        await using var check = _services.CreateAsyncScope();
        var db2 = check.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        Assert.AreEqual(3, await db2.WorkflowRuns.CountAsync());
        Assert.AreEqual(WorkflowRunStatus.Failed, (await db2.WorkflowRuns.FindAsync(first.WorkflowRunId))!.Status);
        Assert.IsNotNull((await db2.PostParserTasks.FindAsync(first.TaskId))!.Error);
    }

    [TestMethod]
    public async Task DisabledWorkflowReturnsFailureAndDoesNotClaimToHaveQueued()
    {
        var selection = await Add();
        await using (var scope = _services.CreateAsyncScope())
        {
            var id = await scope.ServiceProvider.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>().SeedAsync();
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowDefinitions
                .Where(d => d.Id == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.Enabled, false));
        }
        var result = await Reparse(selection);
        Assert.AreEqual(1, result.FailedCount);
        Assert.AreEqual(0, result.QueuedCount);
        Assert.IsNull(result.Items.Single().WorkflowRunId);
    }

    [TestMethod]
    public async Task InvalidDuplicateSnapshotsAreRejectedBeforeAnyMutationAndEmptySelectionIsValid()
    {
        var selection = await Add();
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Reparse(selection, selection));
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPostParserTaskService>();
        var response = await new PostParserBatchController(service).ReParseFailed(new());
        Assert.AreEqual(0, response.Data!.RequestedCount);
        Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task HttpEndpointAcceptsConfirmedSnapshotsAndReturnsPerItemCountsToRemoteClients()
    {
        var selection = await Add(null);
        await using var scope = _services.CreateAsyncScope();
        await using var host = await ServiceGateHost.StartAsync([typeof(PostParserBatchController)], services =>
            services.AddSingleton(scope.ServiceProvider.GetRequiredService<IPostParserTaskService>()));
        host.Remote.Mode = RemoteAccessMode.Unrestricted;
        using var request = host.Request(HttpMethod.Post, "/post-parser/tasks/reparse-failed");
        request.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.3.100");
        request.Content = new StringContent(JsonSerializer.Serialize(new {tasks = new[]
            {new {taskId = selection.TaskId, revision = selection.Revision}}}), Encoding.UTF8, "application/json");
        using var response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(0, json.RootElement.GetProperty("code").GetInt32());
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual(1, data.GetProperty("requestedCount").GetInt32());
        Assert.AreEqual(1, data.GetProperty("queuedCount").GetInt32());
        Assert.AreEqual("queued", data.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.IsTrue(data.GetProperty("items")[0].GetProperty("workflowRunId").GetInt32() > 0);
    }
}
