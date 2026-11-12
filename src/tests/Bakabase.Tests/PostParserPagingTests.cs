using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Service;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostParserPagingTests
{
    private IServiceProvider _services = null!;
    private readonly SqlCapture _sql = new();
    private const string Complete = "{\"DownloadInfo\":{\"isComplete\":true,\"resources\":[]}}";

    [TestInitialize]
    public async Task Setup() => _services = await TestServiceBuilder.BuildServiceProvider(s =>
        s.ConfigureDbContext<BakabaseDbContext>(o => o.AddInterceptors(_sql)));

    private async Task<PostParserTaskDbModel> Add(string? results = null, string? state = null,
        WorkflowRunStatus? status = null, string? error = null, BTaskStatus? live = null,
        string? content = null, string? title = null)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        int? runId = null;
        if (status != null)
        {
            var run = new WorkflowRunDbModel {Status = status.Value, StartedAt = DateTime.Now, CompletedAt = DateTime.Now,
                ErrorMessage = "fixture workflow failure"};
            db.WorkflowRuns.Add(run); await db.SaveChangesAsync(); runId = run.Id;
        }
        var row = new PostParserTaskDbModel {Revision = 3, WorkflowRunId = runId,
            Link = "https://example.test/" + Guid.NewGuid(), Title = title ?? "fixture post",
            Targets = "[1]", Results = results, ParsingState = state, Error = error, ContentSnapshotJson = content};
        db.PostParserTasks.Add(row); await db.SaveChangesAsync();
        if (live != null)
        {
            var manager = _services.GetRequiredService<BTaskManager>();
            await manager.Enqueue(BTaskBuilder.Create($"workflow.run.{runId}").Persistent().Run(_ => Task.CompletedTask));
            var task = manager.Tasks.Single(t => t.Id == $"workflow.run.{runId}").Task;
            task.Status = live.Value;
            task.Data = new WorkflowTaskProgress(runId!.Value, "fixture", "extracting");
        }
        return row;
    }

    private async Task<PostParserTaskPage> Search(PostParserTaskSearchInput? input = null)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().Search(input ?? new());
    }

    [TestMethod]
    public async Task PageLoadsOnlyItsPayloadRowsAndSummaryAndSelectionNeverMaterializeLargeColumns()
    {
        for (var i = 0; i < 23; i++)
            await Add(Complete, "complete", content: JsonSerializer.Serialize(new {mainHtml = new string('x', 16_384) + "BODY_" + i}));
        _sql.Commands.Clear();
        var result = await Search(new() {PageIndex = 2, PageSize = 5});
        Assert.AreEqual(23, result.GlobalTotal);
        Assert.AreEqual(23, result.TotalCount);
        Assert.AreEqual(5, result.Items.Count);
        Assert.AreEqual(6, result.Items[0].Id);
        Assert.AreEqual(23, result.StatusCounts["success"]);
        var full = _sql.Commands.Where(IsPayloadQuery).ToList();
        Assert.AreEqual(1, full.Count, "Only one bounded page query may retrieve the large saved columns.");
        StringAssert.Contains(full[0], " IN ");
        _sql.Commands.Clear();
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPostParserTaskService>();
        Assert.AreEqual(23, (await service.GetSummary()).Completed);
        Assert.AreEqual(0, (await service.GetFailedSelection()).Tasks.Count);
        Assert.IsFalse(_sql.Commands.Any(IsPayloadQuery), "Global counts/selection must not call GetAll or RefreshAll.");
    }

    [TestMethod]
    public async Task FilteringUsesGlobalStatusPriorityAndIncludesOffPageLiveState()
    {
        await Add(Complete, "complete", WorkflowRunStatus.Success, "stale old error");
        var failed = await Add(status: WorkflowRunStatus.Failed);
        await Add("{\"DownloadInfo\":{\"error\":\"legacy target failed\"}}");
        await Add(state: "partial", status: WorkflowRunStatus.Waiting, error: "old error");
        await Add(status: WorkflowRunStatus.Interrupted);
        await Add(status: WorkflowRunStatus.Pending);
        await Add(Complete, "complete", WorkflowRunStatus.Pending, live: BTaskStatus.NotStarted);
        await Add(Complete, "complete", WorkflowRunStatus.Running, live: BTaskStatus.Paused);
        var running = await Add(Complete, "complete", WorkflowRunStatus.Running, live: BTaskStatus.Running);
        var cancelled = await Add(status: WorkflowRunStatus.Pending, live: BTaskStatus.Cancelled);
        var result = await Search(new() {Status = "failed", PageSize = 1});
        Assert.AreEqual(failed.Id, result.Items.Single().Id);
        Assert.AreEqual(3, result.TotalCount);
        Assert.AreEqual(10, result.GlobalTotal);
        Assert.AreEqual(1, result.StatusCounts["success"]);
        Assert.AreEqual(1, result.StatusCounts["attention"]);
        Assert.AreEqual(2, result.StatusCounts["ready"]);
        Assert.AreEqual(1, result.StatusCounts["running"]);
        Assert.AreEqual(1, result.StatusCounts["paused"]);
        Assert.AreEqual(1, result.StatusCounts["queued"]);
        Assert.AreEqual(running.Id, result.RunningTaskId);
        Assert.AreEqual(1, result.RunningStages["extracting"]);
        // The legacy nested-error row needs explicit reparse, not StartAll: all requested
        // result keys already exist and it has no run/task-level error to retry.
        Assert.AreEqual(4, result.StartableCount);
        var located = await Search(new() {LocateTaskId = running.Id, PageSize = 2});
        Assert.AreEqual(5, located.PageIndex);
        Assert.IsTrue(located.Items.Any(t => t.Id == running.Id));
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPostParserTaskService>();
        Assert.AreEqual(3, (await service.GetFailedSelection()).Tasks.Count);
        Assert.AreEqual(3, (await service.GetSummary()).Failed);
        Assert.AreEqual(WorkflowRunStatus.Pending, (await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>()
            .WorkflowRuns.FindAsync(cancelled.WorkflowRunId))!.Status, "The paging/read-only projection must not rewrite execution state.");
    }

    [TestMethod]
    public async Task SearchFindsDecodedUnicodeLinksPasswordsInstructionsAndCommentsOutsideTheFirstPage()
    {
        await Add(Complete, "complete");
        var selected = await Add(JsonSerializer.Serialize(new {DownloadInfo = new
        {
            isComplete = true, title = "结果标题", groups = new[] {new {title = "GROUP NeedLe"}},
            resources = new[] {new {link = "https://cloud.example/item", code = "提取码值", password = "p'\\word",
                extraction = new {steps = new[] {new {op = "renameLiteral", findText = "删除标记"}}}}}
        }}), "complete", content: JsonSerializer.Serialize(new {mainHtml = "<p>正文线索</p>",
            comments = new[] {new {html = "<p>评论线索</p>"}}}));
        foreach (var keyword in new[] {"结果标题", "group needle", "cloud.example", "提取码值", "p'\\word", "删除标记", "正文线索", "评论线索"})
        {
            var page = await Search(new() {Keyword = keyword, PageSize = 1});
            Assert.AreEqual(1, page.TotalCount, keyword);
            Assert.AreEqual(selected.Id, page.Items.Single().Id, keyword);
            Assert.AreEqual(2, page.GlobalTotal);
        }
        Assert.AreEqual(0, (await Search(new() {Keyword = "' OR 1=1 --"})).TotalCount);
    }

    [TestMethod]
    public async Task SearchFindsCurrentWorkflowErrorAcrossPagesWhilePreservingStoredTaskError()
    {
        const string storedError = "The post page could not be read.";
        const string currentError = "Source service：帖子ID非法";
        await Add(Complete, "complete");
        var first = await Add(status: WorkflowRunStatus.Failed, error: storedError);
        var second = await Add(status: WorkflowRunStatus.Failed, error: storedError);
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().WorkflowRuns
                .Where(r => r.Id == first.WorkflowRunId || r.Id == second.WorkflowRunId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ErrorMessage, currentError));

        foreach (var (pageIndex, expectedId) in new[] {(1, first.Id), (2, second.Id)})
        {
            var page = await Search(new() {Keyword = "帖子ID非法", Status = "failed", PageIndex = pageIndex, PageSize = 1});
            Assert.AreEqual(2, page.TotalCount);
            Assert.AreEqual(3, page.GlobalTotal);
            Assert.AreEqual(2, page.StatusCounts["failed"]);
            Assert.AreEqual(pageIndex, page.PageIndex);
            Assert.AreEqual(expectedId, page.Items.Single().Id);
            Assert.AreEqual(currentError, page.Items.Single().Error);
        }
        var oldErrorSearch = await Search(new() {Keyword = storedError});
        Assert.AreEqual(2, oldErrorSearch.TotalCount, "Stored errors remain part of the existing search scope.");
        await using var check = _services.CreateAsyncScope();
        var stored = await check.ServiceProvider.GetRequiredService<BakabaseDbContext>().PostParserTasks
            .Where(t => t.Id == first.Id || t.Id == second.Id).Select(t => t.Error).ToListAsync();
        Assert.IsTrue(stored.All(error => error == storedError), "A read must not rewrite the task's stored error.");
    }

    [TestMethod]
    public async Task LegacyMissingHistoryAndMalformedJsonRemainReadableWithoutInventingSuccessfulResults()
    {
        var saved = await Add(Complete, "complete", error: PostParserSavedResults.MissingWorkflowRunError);
        var malformed = await Add("not-json", content: "not-json");
        await Add("{\"DownloadInfo\":\"text\"}", content: "{\"locks\":\"not-an-array\"}");
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().PostParserTasks
                .Where(t => t.Id == saved.Id || t.Id == malformed.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowRunId, 99999));
        var result = await Search();
        Assert.AreEqual(3, result.Items.Count);
        Assert.AreEqual(1, result.StatusCounts["success"]);
        Assert.AreEqual(1, result.StatusCounts["failed"]);
        Assert.IsNull(result.Items.Single(t => t.Id == saved.Id).Error);
        Assert.IsNotNull(result.Items.Single(t => t.Id == malformed.Id).Error);
    }

    [TestMethod]
    public async Task SubsequentSearchReadsCommittedPayloadAndStatusChangesWithinTheSameRevision()
    {
        var row = await Add("{\"DownloadInfo\":{\"isComplete\":false,\"resources\":[]}}", "partial",
            content: "{\"mainHtml\":\"previous comment phrase\"}");
        var before = await Search(new() {Keyword = "previous comment phrase"});
        Assert.AreEqual(1, before.TotalCount);
        Assert.AreEqual(1, before.StatusCounts["attention"]);
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().PostParserTasks.Where(t => t.Id == row.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Results, Complete).SetProperty(t => t.ParsingState, "complete")
                    .SetProperty(t => t.ContentSnapshotJson, "{\"mainHtml\":\"updated comment phrase\"}"));
        var oldSearch = await Search(new() {Keyword = "previous comment phrase"});
        Assert.AreEqual(0, oldSearch.TotalCount);
        Assert.AreEqual(1, oldSearch.StatusCounts["success"]);
        var updated = await Search(new() {Keyword = "updated comment phrase"});
        Assert.AreEqual(row.Id, updated.Items.Single().Id);
        Assert.AreEqual(row.Revision, updated.Items.Single().Revision);
        Assert.AreEqual(1, updated.TotalCount);
    }

    [TestMethod]
    public async Task LatestRunStatusIsNotOverwrittenByTheEarlierFactsSnapshot()
    {
        var row = await Add(Complete, "complete", WorkflowRunStatus.Pending);
        _sql.BeforeRead = command =>
        {
            if (!IsPayloadQuery(command.CommandText)) return;
            _sql.BeforeRead = null;
            using var update = command.Connection!.CreateCommand();
            update.CommandText = "UPDATE WorkflowRuns SET Status = $status WHERE Id = $id";
            var status = update.CreateParameter(); status.ParameterName = "$status"; status.Value = (int)WorkflowRunStatus.Success;
            var id = update.CreateParameter(); id.ParameterName = "$id"; id.Value = row.WorkflowRunId;
            update.Parameters.Add(status); update.Parameters.Add(id);
            update.ExecuteNonQuery();
        };
        var result = await Search();
        Assert.AreEqual(WorkflowRunStatus.Success, result.Items.Single().WorkflowStatus);
        Assert.IsNull(result.Items.Single().Error);
    }

    [TestMethod]
    public async Task PascalCaseIncompleteInformationRemainsAttentionInPageAndSummary()
    {
        await Add("{\"DownloadInfo\":{\"IsComplete\":false,\"Resources\":[]}}", "complete");
        var result = await Search();
        Assert.AreEqual(1, result.StatusCounts["attention"]);
        Assert.AreEqual(0, result.StatusCounts["success"]);
        Assert.AreEqual(0, PostParserTaskSummary.Create(result.Items).Completed);
        await using var scope = _services.CreateAsyncScope();
        Assert.AreEqual(0, (await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>().GetSummary()).Completed);
    }

    [TestMethod]
    public async Task ClampsRemovedLastPageAndCountsPendingHandoffsWithoutLoadingOffPagePayloads()
    {
        var first = await Add(error: "failed");
        var second = await Add(error: "failed");
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            await db.PostParserTasks.Where(t => t.Id == second.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true));
            db.AcquisitionHandoffs.AddRange(new AcquisitionHandoffDbModel {Id = "queued", SourceModule = "postParser", SourceKind = "task", Status = "queued"},
                new AcquisitionHandoffDbModel {Id = "prepared", SourceModule = "postParser", SourceKind = "task", SourceId = "off-page", Status = "prepared"});
            await db.SaveChangesAsync();
        }
        var result = await Search(new() {PageIndex = 99, PageSize = 1});
        Assert.AreEqual(1, result.PageIndex);
        Assert.AreEqual(first.Id, result.Items.Single().Id);
        Assert.AreEqual(2, result.PendingHandoffCount);
        await using var check = _services.CreateAsyncScope();
        Assert.AreEqual(1, (await check.ServiceProvider.GetRequiredService<IPostParserTaskService>().GetFailedSelection()).Tasks.Count);
    }

    [TestMethod]
    public async Task HttpSearchIsBoundedAndFailedSelectionReturnsOnlyConfirmationIdentities()
    {
        await Add(error: "private error"); await Add(error: "private error");
        await using var scope = _services.CreateAsyncScope();
        await using var host = await ServiceGateHost.StartAsync([typeof(PostParserBatchController)], services =>
            services.AddSingleton(scope.ServiceProvider.GetRequiredService<IPostParserTaskService>()));
        host.Remote.Mode = RemoteAccessMode.Unrestricted;
        using var search = host.Request(HttpMethod.Post, "/post-parser/tasks/search");
        search.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.3.100");
        search.Content = new StringContent("{\"pageIndex\":1,\"pageSize\":1,\"status\":\"failed\"}", Encoding.UTF8, "application/json");
        using var response = await host.SendAsync(search);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(1, json.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
        using var selection = host.Request(HttpMethod.Get, "/post-parser/tasks/failed-selection");
        selection.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.3.100");
        using var selected = await host.SendAsync(selection);
        var content = await selected.Content.ReadAsStringAsync();
        using var selectedJson = JsonDocument.Parse(content);
        Assert.AreEqual(2, selectedJson.RootElement.GetProperty("data").GetProperty("tasks").GetArrayLength());
        Assert.IsFalse(content.Contains("private error", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("example.test", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FilteredSelectionSharesKeywordStatusReasonRulesWithoutLoadingPayloadsOrTruncating()
    {
        await Add(Complete, "complete", title: "matching complete");
        var partial = await Add("{\"DownloadInfo\":{\"isComplete\":false,\"resources\":[]}}", "partial",
            status: WorkflowRunStatus.Waiting, title: "matching partial");
        await Add("{\"DownloadInfo\":{\"isComplete\":false,\"resources\":[]}}", "partial",
            status: WorkflowRunStatus.Waiting, title: "another partial");
        var filter = new PostParserTaskFilterScope
            {Keyword = "matching", Status = "attention", ReasonCode = "incompleteInformation"};
        var page = await Search(new() {Keyword = filter.Keyword, Status = filter.Status, ReasonCode = filter.ReasonCode});
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPostParserTaskService>();
        _sql.Commands.Clear();
        var selected = await service.SelectFilteredTasks(filter, 1000);
        Assert.AreEqual(page.TotalCount, selected.MatchedCount); Assert.AreEqual(1, selected.MatchedCount);
        Assert.AreEqual(partial.Id, selected.Tasks.Single().TaskId);
        Assert.AreEqual(partial.Revision, selected.Tasks.Single().Revision);
        Assert.AreEqual(partial.WorkflowRunId, selected.Tasks.Single().WorkflowRunId);
        Assert.IsFalse(_sql.Commands.Any(IsPayloadQuery), "Selection must not materialize saved results or source HTML.");
        _sql.Commands.Clear();
        var overflow = await service.SelectFilteredTasks(new() {Keyword = "matching"}, 1);
        Assert.AreEqual(2, overflow.MatchedCount); Assert.IsTrue(overflow.LimitExceeded);
        Assert.AreEqual(0, overflow.Tasks.Count, "An oversized filter cannot silently become a partial selection.");
        Assert.IsFalse(_sql.Commands.Any(IsPayloadQuery));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SelectFilteredTasks(new() {Status = "invalid"}, 1000));
    }

    private static bool IsPayloadQuery(string sql) => !sql.TrimStart().StartsWith("WITH", StringComparison.Ordinal) &&
        sql.Contains("\"ContentSnapshotJson\"", StringComparison.Ordinal) && sql.Contains("\"Results\"", StringComparison.Ordinal);

    private sealed class SqlCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public Action<DbCommand>? BeforeRead { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {Commands.Add(command.CommandText); BeforeRead?.Invoke(command); return ValueTask.FromResult(result);}
    }
}
