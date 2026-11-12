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
using Bakabase.InsideWorld.Business.Components.PostParser.Controllers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Service;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostParserDeletionTests
{
    private IServiceProvider _services = null!;
    private const string Results = "{\"DownloadInfo\":{\"isComplete\":true,\"resources\":[]}}";

    [TestInitialize]
    public async Task Setup() => _services = await TestServiceBuilder.BuildServiceProvider();

    private async Task<PostParserTaskDbModel> Add(string title = "matched", WorkflowRunStatus status = WorkflowRunStatus.Failed)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var run = new WorkflowRunDbModel {Status = status, StartedAt = DateTime.Now,
            ErrorMessage = "中文错误", CurrentStepIndex = 1, CurrentItemJson = "{\"saved\":true}",
            WaitReason = status == WorkflowRunStatus.Waiting ? "manual confirmation" : null,
            WaitPromptJson = status == WorkflowRunStatus.Waiting ? "{\"question\":\"saved\"}" : null};
        db.WorkflowRuns.Add(run); await db.SaveChangesAsync();
        var task = new PostParserTaskDbModel {Revision = 4, WorkflowRunId = run.Id, Title = title,
            Link = "https://example.test/" + Guid.NewGuid(), Targets = "[1]", Results = Results,
            ParsingState = "complete", ContentSnapshotJson = "{\"mainHtml\":\"preserved source\"}"};
        db.PostParserTasks.Add(task); await db.SaveChangesAsync();
        return task;
    }

    private async Task<PostParserTaskDeletionPreview> Preview(string? keyword = null, string? status = null)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>()
            .PreviewDeletion(new() {Keyword = keyword, Status = status});
    }

    private async Task<PostParserTaskDeletionResult> Confirm(PostParserTaskDeletionPreview preview)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPostParserTaskService>()
            .ConfirmDeletion(new() {Scope = preview.Scope, Tasks = preview.Tasks});
    }

    [TestMethod]
    public async Task PreviewCoversAllMatchingPagesAndConfirmationNeverAddsNewMatches()
    {
        var selected = new List<PostParserTaskDbModel>();
        for (var i = 0; i < 23; i++) selected.Add(await Add());
        var unmatched = await Add("unrelated");
        var success = await Add(status: WorkflowRunStatus.Success);
        var preview = await Preview("matched", "failed");
        Assert.AreEqual(23, preview.Count);
        Assert.AreEqual(23, preview.Tasks.Count);
        var newlyMatching = await Add();
        var deleted = await Confirm(preview);
        Assert.AreEqual(23, deleted.RequestedCount);
        Assert.AreEqual(23, deleted.DeletedCount);
        Assert.AreEqual(0, deleted.SkippedCount);
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var rows = await db.PostParserTasks.AsNoTracking().ToListAsync();
        Assert.AreEqual(23, rows.Count(r => r.IsDeleted));
        Assert.IsTrue(rows.Where(r => new[] {unmatched.Id, success.Id, newlyMatching.Id}.Contains(r.Id)).All(r => !r.IsDeleted));
        Assert.IsTrue(rows.Where(r => r.IsDeleted).All(r => r.Revision == 5 && r.Results == Results &&
            r.ContentSnapshotJson == "{\"mainHtml\":\"preserved source\"}"));
        Assert.AreEqual(26, await db.WorkflowRuns.CountAsync(), "Soft deletion must retain every existing run.");
        Assert.AreEqual(0, _services.GetRequiredService<BTaskManager>().Tasks.Count(), "Deletion must not enqueue any parser work.");
    }

    [TestMethod]
    public async Task ConfirmationSkipsChangedRevisionRunStatusAndKeyword()
    {
        var revision = await Add();
        var runIdentity = await Add();
        var status = await Add();
        var keyword = await Add();
        var unchanged = await Add();
        var preview = await Preview("matched", "failed");
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            await db.PostParserTasks.Where(t => t.Id == revision.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Revision, 5));
            await db.PostParserTasks.Where(t => t.Id == runIdentity.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowRunId, unchanged.WorkflowRunId));
            await db.WorkflowRuns.Where(r => r.Id == status.WorkflowRunId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Success));
            await db.PostParserTasks.Where(t => t.Id == keyword.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Title, "different"));
        }
        var result = await Confirm(preview);
        Assert.AreEqual(1, result.DeletedCount);
        Assert.AreEqual(4, result.SkippedCount);
        Assert.AreEqual("snapshotChanged", result.Items.Single(i => i.TaskId == revision.Id).Reason);
        Assert.AreEqual("snapshotChanged", result.Items.Single(i => i.TaskId == runIdentity.Id).Reason);
        Assert.AreEqual("scopeChanged", result.Items.Single(i => i.TaskId == status.Id).Reason);
        Assert.AreEqual("scopeChanged", result.Items.Single(i => i.TaskId == keyword.Id).Reason);
        Assert.AreEqual("deleted", result.Items.Single(i => i.TaskId == unchanged.Id).Outcome);
    }

    [TestMethod]
    public async Task ConcurrentConfirmationsDeleteOnceAndKeepWaitingCheckpoint()
    {
        var row = await Add(status: WorkflowRunStatus.Waiting);
        var preview = await Preview(null, "attention");
        var results = await Task.WhenAll(Confirm(preview), Confirm(preview));
        Assert.AreEqual(1, results.Sum(r => r.DeletedCount));
        Assert.AreEqual(1, results.Sum(r => r.SkippedCount));
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        Assert.AreEqual(5, (await db.PostParserTasks.FindAsync(row.Id))!.Revision);
        var run = (await db.WorkflowRuns.FindAsync(row.WorkflowRunId))!;
        Assert.AreEqual(WorkflowRunStatus.Cancelled, run.Status);
        Assert.AreEqual("manual confirmation", run.WaitReason);
        Assert.AreEqual("{\"question\":\"saved\"}", run.WaitPromptJson);
        Assert.AreEqual("{\"saved\":true}", run.CurrentItemJson);
        Assert.AreEqual(1, run.CurrentStepIndex);
    }

    [TestMethod]
    public async Task LegacyDeleteAllAndInvalidConfirmationCannotMutateTasks()
    {
        await Add();
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPostParserTaskService>();
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => service.DeleteAll());
        StringAssert.Contains(error.Message, "Refresh the page and try again.");
        var legacy = new PostParserController(service, null!, null!, null!);
        Assert.ThrowsExactly<InvalidOperationException>(() => legacy.DeleteAll());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ConfirmDeletion(new()));
        var preview = await service.PreviewDeletion(new());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ConfirmDeletion(new()
            {Scope = preview.Scope, Tasks = [preview.Tasks.Single(), preview.Tasks.Single()]}));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.PreviewDeletion(new() {Status = "invalid"}));
        Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().PostParserTasks.CountAsync(t => !t.IsDeleted));
    }

    [TestMethod]
    public async Task HttpPreviewAndConfirmUseTheReturnedLightweightManifest()
    {
        var selected = await Add();
        await using var scope = _services.CreateAsyncScope();
        await using var host = await ServiceGateHost.StartAsync([typeof(PostParserBatchController)], services =>
            services.AddSingleton(scope.ServiceProvider.GetRequiredService<IPostParserTaskService>()));
        host.Remote.Mode = RemoteAccessMode.Unrestricted;
        using var request = host.Request(HttpMethod.Post, "/post-parser/tasks/delete/preview");
        request.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.3.100");
        request.Content = new StringContent("{\"keyword\":\"中文错误\",\"status\":\"failed\"}", Encoding.UTF8, "application/json");
        using var response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(content);
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual(1, data.GetProperty("count").GetInt32());
        Assert.AreEqual(selected.Id, data.GetProperty("tasks")[0].GetProperty("taskId").GetInt32());
        Assert.IsFalse(content.Contains("preserved source", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("example.test", StringComparison.Ordinal));
        using var confirm = host.Request(HttpMethod.Post, "/post-parser/tasks/delete/confirm");
        confirm.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.3.100");
        confirm.Content = new StringContent(data.GetRawText(), Encoding.UTF8, "application/json");
        using var confirmed = await host.SendAsync(confirm);
        Assert.AreEqual(HttpStatusCode.OK, confirmed.StatusCode);
        using var deleted = JsonDocument.Parse(await confirmed.Content.ReadAsStringAsync());
        Assert.AreEqual(1, deleted.RootElement.GetProperty("data").GetProperty("deletedCount").GetInt32());
    }
}
