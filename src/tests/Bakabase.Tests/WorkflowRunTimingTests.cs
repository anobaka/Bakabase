using System.Text.Json;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Extensions;
using Bakabase.Modules.Workflow.Services;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Bakabase.Tests;

[TestClass]
public sealed class WorkflowRunTimingTests
{
    private static readonly JsonSerializerSettings HostJson = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        DateFormatString = "yyyy-MM-dd HH:mm:ss.fff"
    };

    [DataTestMethod]
    [DataRow(WorkflowRunStatus.Waiting)]
    [DataRow(WorkflowRunStatus.Success)]
    public async Task SqliteDatesHaveTheSameInstantInDetailListAndSourceHistoryWithoutChangingStoredRows(WorkflowRunStatus status)
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<BakabaseDbContext>();
        var localSubmitted = new DateTime(2025, 4, 5, 20, 35, 0, DateTimeKind.Local);
        var utcNodeStart = localSubmitted.ToUniversalTime().AddMinutes(1);
        var row = new WorkflowRunDbModel
        {
            WorkflowDefinitionId = 0, Status = status, StartedAt = localSubmitted,
            CompletedAt = status == WorkflowRunStatus.Success ? localSubmitted.AddHours(1) : null,
            WaitingSince = status == WorkflowRunStatus.Waiting ? localSubmitted.AddMinutes(2) : null,
            PayloadJson = "{\"unchanged\":true}", CurrentItemJson = "{\"checkpoint\":true}",
            Sources = [new() {Module = "test", EntityKind = "entry", EntityId = "time"}],
            NodeExecutions = [new() {NodeId = "plan:step", Kind = "test", Attempt = 1, Status = "succeeded",
                StartedAt = utcNodeStart, CompletedAt = utcNodeStart.AddSeconds(20)}]
        };
        db.WorkflowRuns.Add(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var before = await db.WorkflowRuns.AsNoTracking().Include(r => r.NodeExecutions).SingleAsync(r => r.Id == row.Id);
        Assert.AreEqual(DateTimeKind.Unspecified, before.StartedAt.Kind);
        Assert.AreEqual(DateTimeKind.Unspecified, before.NodeExecutions.Single().StartedAt.Kind);

        var workflow = new WorkflowController(sp.GetRequiredService<IWorkflowDefinitionService>(), null!, null!, null!, null!, null!, null!);
        var detail = (await workflow.GetRun(row.Id, db, sp.GetRequiredService<BTaskManager>(), default)).Data!;
        var listed = (await workflow.SearchRuns(0, new())).Data!.Single();
        var index = new WorkflowSourceIndex<BakabaseDbContext>(db, new WorkflowTriggerRegistry([]), new());
        var sourceController = new WorkflowSourcesController(db, index, [], null!, null!);
        var source = (await sourceController.Query(new() {Sources = [new("test", "entry", "time")]}, default))
            .Data!.Single().Runs.Single().Run;

        foreach (var view in new[] {detail, listed, source})
        {
            using var json = JsonDocument.Parse(JsonConvert.SerializeObject(view, HostJson));
            AssertInstant(json.RootElement, "startedAt", localSubmitted.ToUniversalTime());
            AssertInstant(json.RootElement, "completedAt", row.CompletedAt?.ToUniversalTime());
            AssertInstant(json.RootElement, "waitingSince", row.WaitingSince?.ToUniversalTime());
            var node = json.RootElement.GetProperty("nodeExecutions").EnumerateArray().Single();
            AssertInstant(node, "startedAt", utcNodeStart);
            AssertInstant(node, "completedAt", utcNodeStart.AddSeconds(20));
        }

        var after = await db.WorkflowRuns.AsNoTracking().Include(r => r.NodeExecutions).SingleAsync(r => r.Id == row.Id);
        Assert.AreEqual(before.StartedAt, after.StartedAt);
        Assert.AreEqual(before.CompletedAt, after.CompletedAt);
        Assert.AreEqual(before.WaitingSince, after.WaitingSince);
        Assert.AreEqual(before.NodeExecutions.Single().StartedAt, after.NodeExecutions.Single().StartedAt);
        Assert.AreEqual(before.NodeExecutions.Single().CompletedAt, after.NodeExecutions.Single().CompletedAt);
        Assert.AreEqual(before.PayloadJson, after.PayloadJson);
        Assert.AreEqual(before.CurrentItemJson, after.CurrentItemJson);
        Assert.IsFalse(db.ChangeTracker.HasChanges());
    }

    [DataTestMethod]
    [DataRow(DateTimeKind.Utc)]
    [DataRow(DateTimeKind.Local)]
    [DataRow(DateTimeKind.Unspecified)]
    public void NodeTimestampProjectionPreservesUtcInstantsAndDoesNotMutateDomainRecords(DateTimeKind kind)
    {
        var utc = new DateTime(2025, 4, 5, 12, 35, 0, DateTimeKind.Utc);
        var stored = kind switch
        {
            DateTimeKind.Local => utc.ToLocalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(utc, DateTimeKind.Unspecified),
            _ => utc
        };
        var node = new WorkflowNodeExecution {StartedAt = stored};
        var domain = new WorkflowRun {StartedAt = utc, NodeExecutions = [node]};
        var view = WorkflowRunViewModel.From(domain);
        Assert.AreEqual(utc, view.StartedAt);
        Assert.AreEqual(utc, view.NodeExecutions.Single().StartedAt);
        Assert.AreEqual(DateTimeKind.Utc, view.NodeExecutions.Single().StartedAt.Kind);
        Assert.IsNull(view.NodeExecutions.Single().CompletedAt);
        Assert.IsNull(view.CompletedAt);
        Assert.IsNull(view.WaitingSince);
        Assert.AreEqual(kind, domain.NodeExecutions.Single().StartedAt.Kind);
        Assert.AreEqual(stored, domain.NodeExecutions.Single().StartedAt);
        Assert.AreNotSame(node, view.NodeExecutions.Single());
    }

    private static void AssertInstant(JsonElement parent, string property, DateTime? expected)
    {
        if (expected == null)
        {
            Assert.IsTrue(!parent.TryGetProperty(property, out var absent) || absent.ValueKind == JsonValueKind.Null);
            return;
        }
        var value = parent.GetProperty(property);
        Assert.IsTrue(value.GetString()!.EndsWith('Z'), $"{property} must include an explicit UTC zone.");
        Assert.AreEqual(expected.Value, value.GetDateTime(), property);
        Assert.AreEqual(DateTimeKind.Utc, value.GetDateTime().Kind, property);
    }
}
