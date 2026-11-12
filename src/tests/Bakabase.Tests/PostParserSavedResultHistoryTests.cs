using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Acquisition;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Utils;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Bakabase.Service.Components.DownloadIntents;
using System.Text.Json;
using Content = Bakabase.Modules.PostParser.Models.Domain.PostContent;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostParserSavedResultHistoryTests
{
    private const string Complete = "{\"DownloadInfo\":{\"isComplete\":true,\"resources\":[{\"link\":\"https://files.example/archive.zip\",\"password\":\"saved-secret\"}]}}";
    private IServiceProvider _services = null!;
    private BakabaseDbContext Db => _services.GetRequiredService<BakabaseDbContext>();
    private PostParserWorkflowService<BakabaseDbContext> Workflow => _services.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>();

    [TestInitialize]
    public async Task Setup() => _services = await TestServiceBuilder.BuildServiceProvider(services =>
    {
        services.RemoveAll<DownloadMethodRegistry>();
        services.AddScoped(_ => new DownloadMethodRegistry([], [], null!, null!));
    });

    private async Task<PostParserTaskDbModel> Saved(string? error = PostParserSavedResults.MissingWorkflowRunError,
        string? state = "complete", string results = Complete)
    {
        var task = new PostParserTaskDbModel
        {
            Link = "https://example.test/saved-post", Title = "Saved resource", Revision = 7,
            WorkflowRunId = 987654, ParsingState = state, Results = results, Error = error,
            CompletedAt = error == null ? new DateTime(2026, 10, 1, 2, 3, 4, DateTimeKind.Utc) : null
        };
        Db.PostParserTasks.Add(task);
        await Db.SaveChangesAsync();
        return task;
    }

    private CloudDownloadsController Downloads(Definitions definitions) => new(Db, [], definitions,
        _services.GetRequiredService<IUserStoragePolicy>(), _services.GetRequiredService<PostParserTaskExecutionGate>(), Workflow)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = _services } }
    };

    private Task<SingletonResponse<Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowRunViewModel>> Download(
        PostParserTaskDbModel task, Definitions definitions, int? revision = null) =>
        Downloads(definitions).FromPost(Path.GetTempPath(), new()
        { TaskId = task.Id, Revision = revision ?? task.Revision, ResourceIndices = [0], WorkflowDefinitionId = 123 }, default);

    [DataTestMethod]
    [DataRow(null)]
    [DataRow(PostParserSavedResults.MissingWorkflowRunError)]
    public async Task CompletedResultsSurvivePrunedHistoryAndOnlyTheKnownBookkeepingErrorIsRepaired(string? error)
    {
        var task = await Saved(error);
        var before = task with { };
        var tasks = _services.GetRequiredService<IPostParserTaskService>();
        Assert.IsNull((await tasks.GetAll()).Single(t => t.Id == task.Id).Error);
        Assert.IsNull((await tasks.GetAll()).Single(t => t.Id == task.Id).Error);
        var stored = await Db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.IsNull(stored.Error);
        Assert.AreEqual(before.Results, stored.Results);
        Assert.AreEqual(before.Revision, stored.Revision);
        Assert.AreEqual(before.WorkflowRunId, stored.WorkflowRunId);
        Assert.AreEqual(before.CompletedAt, stored.CompletedAt, "Do not invent a replacement completion timestamp.");
        var summary = await tasks.GetSummary();
        Assert.AreEqual(1, summary.Completed);
        Assert.AreEqual(0, summary.Failed);
    }

    [TestMethod]
    public async Task GenuineStoredErrorsAreNotClearedWhenHistoryIsMissing()
    {
        var task = await Saved("Actual parsing or link-check failure");
        await Workflow.RefreshTasksAsync();
        var stored = await Db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.AreEqual(task.Error, stored.Error);
        var definitions = new Definitions();
        Assert.AreNotEqual((int)ResponseCode.Success, (await Download(task, definitions)).Code);
        Assert.AreEqual(0, definitions.Started);
    }

    [DataTestMethod]
    [DataRow("partial", Complete)]
    [DataRow("possiblyExpired", Complete)]
    [DataRow("complete", "{\"DownloadInfo\":{\"isComplete\":false,\"resources\":[{\"link\":\"https://files.example/a.zip\"}]}}")]
    [DataRow("complete", "{\"DownloadInfo\":{\"error\":\"Extractor failed\",\"data\":{\"isComplete\":true,\"resources\":[{\"link\":\"https://files.example/a.zip\"}]}}}")]
    [DataRow("complete", "{\"DownloadInfo\":{\"isComplete\":true,\"resources\":[null]}}")]
    [DataRow("complete", "{\"DownloadInfo\":null}")]
    [DataRow("complete", "{\"DownloadInfo\":{\"isComplete\":true}}")]
    [DataRow("complete", "malformed")]
    public async Task IncompleteExpiredMalformedAndErrorResultsRemainBlocked(string state, string results)
    {
        var task = await Saved(state: state, results: results);
        await Workflow.RefreshTasksAsync();
        Assert.AreEqual(PostParserSavedResults.MissingWorkflowRunError,
            (await Db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).Error);
        var definitions = new Definitions();
        Assert.AreNotEqual((int)ResponseCode.Success, (await Download(task, definitions)).Code);
        Assert.AreEqual(0, definitions.Started);
        var importer = ActivatorUtilities.CreateInstance<PostParserAcquisitionService>(_services);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => importer.ImportAsync(task.Id, task.Revision, null, [0], default));
    }

    [TestMethod]
    public async Task StartAllDoesNotResetOrReparseCompletedResultsAfterHistoryCleanup()
    {
        var task = await Saved();
        await Workflow.DispatchAsync();
        var stored = await Db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.IsNull(stored.Error);
        Assert.AreEqual(task.Results, stored.Results);
        Assert.AreEqual(task.Revision, stored.Revision);
        Assert.AreEqual(task.WorkflowRunId, stored.WorkflowRunId);
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task DirectDownloadRepairsTheSyntheticErrorWithoutAListRequestAndKeepsTheSavedCredentials()
    {
        var task = await Saved();
        var definitions = new Definitions();
        var result = await Download(task, definitions);
        Assert.AreEqual((int)ResponseCode.Success, result.Code, result.Message);
        Assert.AreEqual(1, definitions.Started);
        var workset = (DownloadWorkset)definitions.Payload!;
        Assert.AreEqual(task.Id, workset.PostTaskId);
        Assert.AreEqual(task.Revision, workset.PostRevision);
        Assert.AreEqual("https://files.example/archive.zip", workset.Targets.Single().Sources.Single().Url);
        Assert.AreEqual("saved-secret", workset.Targets.Single().Sources.Single().ArchivePassword);
        Assert.IsNull((await Db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).Error);
    }

    [DataTestMethod]
    [DataRow("current")]
    [DataRow("revoked")]
    [DataRow("changed")]
    public async Task CompatibilityDownloadRequiresCurrentTaskOwnedReadinessDespiteSuccessfulParsingHistory(string scenario)
    {
        var content = new Content { MainHtml = "visible download details", Locks =
            [new("https://example.test/pay", 5, false) { BlockId = "proof", ContextText = "purchase proof" }] };
        var decision = new PostParserContentReadinessEvent { Version = 1,
            ContentFingerprint = PostContentReadinessPolicy.Fingerprint(content), BlockIds = ["proof"] };
        var state = new PostParserContentReadinessState { History = [decision] };
        content = state.Apply(content);
        var result = new PostDownloadInfo { IsComplete = true, ContentReadiness = content.Readiness,
            Resources = [new() { Link = "https://files.example/archive.zip" }] };
        var results = JsonSerializer.Serialize(new Dictionary<string, PostDownloadInfo> { ["DownloadInfo"] = result }, WorkflowJson.Options);
        var task = await Saved(error: null, results: results);
        var definition = new WorkflowDefinitionDbModel { Name = "Completed parsing fixture", TriggerKind = PostParserWorkflow.Trigger };
        Db.WorkflowDefinitions.Add(definition);
        await Db.SaveChangesAsync();
        var run = new WorkflowRunDbModel { WorkflowDefinitionId = definition.Id, Status = WorkflowRunStatus.Success,
            StartedAt = DateTime.Now, CompletedAt = DateTime.Now };
        Db.WorkflowRuns.Add(run);
        await Db.SaveChangesAsync();
        task.WorkflowRunId = run.Id;
        task.ContentSnapshotJson = JsonSerializer.Serialize(scenario == "changed"
            ? content with { MainHtml = "new required download details" } : content, WorkflowJson.Options);
        if (scenario == "revoked")
            state.History.Add(new() { Version = 2, Action = "revoke", DecisionId = decision.Id });
        task.ContentReadinessJson = JsonSerializer.Serialize(state, WorkflowJson.Options);
        await Db.SaveChangesAsync();

        // The historical string reader remains useful, but cannot authorize a new execution.
        Assert.IsTrue(PostParserSavedResults.ReadDownloadInfo(results).IsComplete);
        var definitions = new Definitions();
        var response = await Download(task, definitions);
        if (scenario == "current")
        {
            Assert.AreEqual((int)ResponseCode.Success, response.Code, response.Message);
            Assert.AreEqual(1, definitions.Started);
        }
        else
        {
            Assert.AreNotEqual((int)ResponseCode.Success, response.Code);
            StringAssert.Contains(response.Message, "content-readiness confirmation changed");
            Assert.AreEqual(0, definitions.Started);
        }
        var stored = await Db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.AreEqual(results, stored.Results);
        Assert.AreEqual(task.ContentSnapshotJson, stored.ContentSnapshotJson);
        Assert.AreEqual(task.ContentReadinessJson, stored.ContentReadinessJson);
        Assert.AreEqual(run.Id, stored.WorkflowRunId);
        Assert.IsNull(stored.Error, "A revoked confirmation does not rewrite successful parsing history.");
        Assert.AreEqual(WorkflowRunStatus.Success, (await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id)).Status);
    }

    [TestMethod]
    public async Task ImportUsesTheSameRepairWithoutReparsingOrCreatingADownload()
    {
        var task = await Saved();
        var imported = await ActivatorUtilities.CreateInstance<PostParserAcquisitionService>(_services)
            .ImportAsync(task.Id, task.Revision, null, [0], default);
        Assert.IsTrue(imported.Created);
        Assert.AreEqual(1, imported.LeadCount);
        Assert.IsNull((await Db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).Error);
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task RepairDoesNotPermitAnOldRevisionToStartADownloadOrImport()
    {
        var task = await Saved();
        var definitions = new Definitions();
        Assert.AreNotEqual((int)ResponseCode.Success, (await Download(task, definitions, task.Revision - 1)).Code);
        Assert.AreEqual(0, definitions.Started);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ActivatorUtilities.CreateInstance<PostParserAcquisitionService>(_services)
                .ImportAsync(task.Id, task.Revision - 1, null, [0], default));
    }

    [TestMethod]
    public async Task AFailedCurrentExecutionStillRejectsItsCompleteIntermediateSnapshot()
    {
        var definition = new WorkflowDefinitionDbModel { Name = "Parsing fixture", TriggerKind = PostParserWorkflow.Trigger };
        Db.WorkflowDefinitions.Add(definition);
        await Db.SaveChangesAsync();
        var run = new WorkflowRunDbModel
        { WorkflowDefinitionId = definition.Id, Status = WorkflowRunStatus.Failed, StartedAt = DateTime.Now, ErrorMessage = "The final parsing step failed." };
        Db.WorkflowRuns.Add(run);
        await Db.SaveChangesAsync();
        var task = await Saved(error: null);
        await Db.PostParserTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowRunId, run.Id));
        var definitions = new Definitions();
        Assert.AreNotEqual((int)ResponseCode.Success, (await Download(task, definitions)).Code);
        Assert.AreEqual(0, definitions.Started);
        Assert.AreEqual(run.ErrorMessage, (await Db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).Error);
    }

    private sealed class Definitions : IWorkflowDefinitionService
    {
        public int Started { get; private set; }
        public object? Payload { get; private set; }
        public Task<WorkflowDefinition?> GetAsync(int id) => Task.FromResult<WorkflowDefinition?>(new() { Id = id, TriggerKind = CloudWorkflow.Trigger });
        public Task<WorkflowRun> RunManagedAsync(int id, object payload, CancellationToken ct = default)
        { Started++; Payload = payload; return Task.FromResult(new WorkflowRun { Id = 1234, WorkflowDefinitionId = id }); }
        public Task<WorkflowDefinition> CreateAsync(WorkflowDefinitionCreationInputModel input, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkflowDefinition> UpdateAsync(int id, WorkflowDefinitionUpdateInputModel input, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(int id) => throw new NotSupportedException();
        public Task<List<WorkflowDefinition>> SearchAsync(WorkflowDefinitionSearchInputModel input) => throw new NotSupportedException();
        public Task<SearchResponse<WorkflowRun>> SearchRunsAsync(WorkflowRunSearchInputModel input) => throw new NotSupportedException();
        public Task<WorkflowRun> RunManuallyAsync(int id, string? argsJson, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
