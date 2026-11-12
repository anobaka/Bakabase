using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class PostLocalProcessingTests
{
    private IServiceProvider _services = null!;
    private string _directory = null!;
    private string _file = null!;
    private int _definitionId;
    private BakabaseDbContext Db => _services.GetRequiredService<BakabaseDbContext>();

    [TestInitialize]
    public async Task Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "post-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _file = Path.Combine(_directory, "selected.txt");
        await File.WriteAllTextAsync(_file, "fixture");
        _services = await TestServiceBuilder.BuildServiceProvider();
        await _services.GetRequiredService<FileProcessingWorkflowSeeder>().SeedAsync();
        _definitionId = await Db.WorkflowDefinitions.Where(d => d.IsBuiltin && d.TriggerKind == FileProcessingPlanTrigger.TriggerKind)
            .Select(d => d.Id).SingleAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        (_services as IDisposable)?.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private async Task<PostParserTaskDbModel> Saved(string? error = PostParserSavedResults.MissingWorkflowRunError)
    {
        var task = new PostParserTaskDbModel
        {
            Link = "https://example.invalid/post/1", Title = "Saved post", Revision = 3,
            WorkflowRunId = 1234567, ParsingState = "complete", Error = error,
            Results = "{\"DownloadInfo\":{\"isComplete\":true,\"resources\":[{\"link\":\"https://example.invalid/file.zip\",\"extraction\":{\"requirement\":\"notRequired\",\"steps\":[]}}]}}"
        };
        Db.PostParserTasks.Add(task);
        await Db.SaveChangesAsync();
        return task;
    }

    private PostLocalProcessingService Service() => new(Db,
        _services.GetRequiredService<PostParserTaskExecutionGate>(),
        _services.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>(),
        _services.GetRequiredService<IWorkflowDefinitionService>(), _services.GetRequiredService<IUserStoragePolicy>());

    private PostLocalProcessingRequest Request(PostParserTaskDbModel task) => new()
    {
        TaskId = task.Id, Revision = task.Revision, ResourceIndex = 0,
        WorkflowDefinitionId = _definitionId, Files = [_file], AlreadyProcessed = true
    };

    [TestMethod]
    public async Task Local_post_run_uses_saved_plan_and_persists_verified_result_provenance()
    {
        var task = await Saved();
        var started = await Service().StartAsync(Request(task), _directory, default);
        var run = await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == started.Id);
        var refs = await Db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.WorkflowRunId == started.Id).ToListAsync();
        Assert.AreEqual(2, refs.Count);
        Assert.IsTrue(refs.All(s => s.Module == "postParser" && s.EntityKind == "task" && s.EntityId == task.Id.ToString() && s.Revision == "3"));
        CollectionAssert.AreEquivalent(new[] {"", "0"}, refs.Select(s => s.ResultKey).ToArray());
        var payload = JsonSerializer.Deserialize<FileProcessingPlanPayload>(run.PayloadJson!, WorkflowJson.Options)!;
        Assert.AreEqual(Path.GetFullPath(_directory), payload.Directory);
        CollectionAssert.AreEqual(new[] {_file}, payload.Files.ToArray());
        Assert.IsTrue(payload.AlreadyProcessed);
        Assert.AreEqual($"post:{task.Id}:3:0", payload.BindingId);
        Assert.IsTrue(payload.ExtractionPlanJson.Contains("notRequired", StringComparison.Ordinal));
        Assert.IsNull((await Db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).Error);
    }

    [TestMethod]
    public async Task Ordinary_manual_payload_cannot_forge_post_provenance()
    {
        var args = JsonSerializer.SerializeToNode(new FileProcessingPlanPayload
        {
            Directory = _directory, Files = [_file], AlreadyProcessed = true,
            BindingId = "post:99:3:0", ExtractionPlanJson = "{\"requirement\":\"notRequired\",\"steps\":[]}"
        }, WorkflowJson.Options)!.AsObject();
        args["sourceRefs"] = JsonNode.Parse("[{\"module\":\"postParser\",\"entityKind\":\"task\",\"entityId\":\"99\"}]");
        var run = await _services.GetRequiredService<IWorkflowDefinitionService>().RunManuallyAsync(_definitionId, args.ToJsonString());
        Assert.AreEqual(0, await Db.Set<WorkflowRunSourceDbModel>().CountAsync(s => s.WorkflowRunId == run.Id));
    }

    [TestMethod]
    public async Task Stale_revision_and_invalid_result_index_create_no_run()
    {
        var task = await Saved(error: null);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Service().StartAsync(Request(task) with {Revision = 2}, _directory, default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Service().StartAsync(Request(task) with {ResourceIndex = 1}, _directory, default));
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task Genuine_parse_errors_and_files_outside_bound_directory_are_rejected()
    {
        var task = await Saved(error: "Actual parser failure");
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Service().StartAsync(Request(task), _directory, default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Service().StartAsync(Request(task) with
            {Files = [Path.Combine(Path.GetDirectoryName(_directory)!, "outside.txt")]}, _directory, default));
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
    }
}
