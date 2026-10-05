using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostParserWorkflowRunHistoryTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private WorkflowDefinitionDbModel _legacy = null!;
    private WorkflowDefinitionDbModel _current = null!;
    private WorkflowDefinitionDbModel _other = null!;
    private readonly DateTime _startedAt = new(2026, 10, 5, 10, 0, 0);

    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider();
        _db = _services.GetRequiredService<BakabaseDbContext>();
        _legacy = new() {Name = "Parse post download information", TriggerKind = PostParserWorkflow.Trigger};
        _current = new() {Name = PostParserWorkflow.BuiltinName, TriggerKind = PostParserWorkflow.Trigger};
        _other = new() {Name = "Unrelated workflow", TriggerKind = "other.manual"};
        _db.Set<WorkflowDefinitionDbModel>().AddRange(_legacy, _current, _other);
        await _db.SaveChangesAsync();
    }

    private WorkflowRunDbModel Run(WorkflowDefinitionDbModel definition, string? payload, int minutes = 0)
    {
        var run = new WorkflowRunDbModel
        {
            WorkflowDefinitionId = definition.Id, PayloadJson = payload,
            Status = WorkflowRunStatus.Success, StartedAt = _startedAt.AddMinutes(minutes),
            StepStatsJson = "[]"
        };
        _db.Set<WorkflowRunDbModel>().Add(run);
        return run;
    }

    private Task<Bootstrap.Models.ResponseModels.SearchResponse<Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowRunViewModel>>
        Search(int? taskId = null, int page = 1, int size = 20) =>
        new PostParserWorkflowRunsController(_db).Search(new()
        {
            TaskId = taskId, PageIndex = page, PageSize = size
        }, CancellationToken.None);

    [TestMethod]
    public async Task TaskHistoryUsesExactJsonIdentityAndIncludesEveryRevisionAndDefinition()
    {
        var old = Run(_legacy, "{\"TaskId\":1,\"Revision\":1}");
        var latest = Run(_current, "{ \"taskId\" : 1, \"revision\" : 8 }", 1);
        Run(_current, "{\"taskId\":10,\"revision\":1}", 2);
        Run(_other, "{\"taskId\":1,\"revision\":9}", 3);
        Run(_current, "{\"text\":\"taskId:1\"}", 4);
        Run(_current, "{\"input\":{\"taskId\":1}}", 5);
        Run(_current, "{\"taskId\":\"1\"}", 6);
        Run(_current, "{\"taskId\":1,\"TaskId\":10}", 7);
        Run(_current, "{\"taskId\":1", 8);
        Run(_current, "[]", 9);
        Run(_current, null, 10);
        await _db.SaveChangesAsync();

        var result = await Search(1);
        Assert.AreEqual(2, result.TotalCount);
        CollectionAssert.AreEqual(new[] {latest.Id, old.Id}, result.Data!.Select(r => r.Id).ToArray());
        CollectionAssert.AreEqual(new[] {_current.Id, _legacy.Id},
            result.Data!.Select(r => r.WorkflowDefinitionId).ToArray());
        Assert.AreEqual(0, (await Search(100)).TotalCount);
    }

    [TestMethod]
    public async Task TaskFilteringPrecedesPaginationAndTiedTimesHaveStableOrder()
    {
        var first = Run(_legacy, "{\"taskId\":1,\"revision\":1}");
        Run(_current, "{\"taskId\":10}", 20);
        var second = Run(_current, "{\"taskId\":1,\"revision\":2}");
        Run(_current, "{\"taskId\":2}", 30);
        var third = Run(_current, "{\"taskId\":1,\"revision\":3}");
        await _db.SaveChangesAsync();

        var page1 = await Search(1, size: 2);
        var page2 = await Search(1, page: 2, size: 2);
        Assert.AreEqual(3, page1.TotalCount);
        Assert.AreEqual(3, page2.TotalCount);
        CollectionAssert.AreEqual(new[] {third.Id, second.Id}, page1.Data!.Select(r => r.Id).ToArray());
        CollectionAssert.AreEqual(new[] {first.Id}, page2.Data!.Select(r => r.Id).ToArray());
        Assert.AreEqual(0, (await Search(1, page: int.MaxValue, size: 200)).Data!.Count());
    }

    [TestMethod]
    public async Task AllHistoryContainsOnlyPostParserSourcesIncludingLegacyAndUnlinkedRuns()
    {
        var old = Run(_legacy, "{\"taskId\":1,\"revision\":1}");
        var current = Run(_current, "{\"taskId\":1,\"revision\":2}", 1);
        var manual = Run(_current, "{\"text\":\"Pasted post without a saved task\"}", 2);
        var malformedHistorical = Run(_legacy, "legacy unreadable payload", 3);
        Run(_other, "{\"taskId\":1}", 10);
        await _db.SaveChangesAsync();

        var page1 = await Search(size: 2);
        var page2 = await Search(page: 2, size: 2);
        Assert.AreEqual(4, page1.TotalCount);
        Assert.AreEqual(4, page2.TotalCount);
        CollectionAssert.AreEqual(new[] {malformedHistorical.Id, manual.Id}, page1.Data!.Select(r => r.Id).ToArray());
        CollectionAssert.AreEqual(new[] {current.Id, old.Id}, page2.Data!.Select(r => r.Id).ToArray());
    }
}
