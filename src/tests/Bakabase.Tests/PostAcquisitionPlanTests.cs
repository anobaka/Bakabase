using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Tests;

[TestClass]
public sealed partial class PostAcquisitionPlanTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext Db => _services.GetRequiredService<BakabaseDbContext>();
    private PostAcquisitionPlanAdapter Adapter => _services.GetRequiredService<PostAcquisitionPlanAdapter>();

    [TestInitialize]
    public async Task Setup() => _services = await TestServiceBuilder.BuildServiceProvider(services =>
    {
        services.RemoveAll<Bakabase.Modules.Downloader.Cloud.ISourceAvailabilityProbe>();
        services.RemoveAll<DownloadMethodRegistry>();
        services.AddScoped(_ => new DownloadMethodRegistry([], [], null!, null!));
    });

    [TestCleanup]
    public void Cleanup() => (_services as IDisposable)?.Dispose();

    private async Task<PostParserTaskDbModel> Saved()
    {
        var task = new PostParserTaskDbModel
        {
            Title = "Saved resource", Link = "https://example.test/post", Revision = 4, ParsingState = "complete", WorkflowRunId = 987654,
            Results = JsonSerializer.Serialize(new {DownloadInfo = new {isComplete = true,
            groups = new[] {new {id = "main", title = "Main content", kind = "main"}, new {id = "preview", title = "Preview", kind = "preview"}},
            resources = new object[]
            {
                new {link = "https://example.test/a.zip", code = "saved-code", password = "saved-password", groupId = "main",
                    extraction = new {requirement = "required", steps = new[] {new {id = "extract", op = "extractArchive", input = "download"}}, evidence = new[] {"Extract the archive"}}},
                new {link = "https://example.test/mirror.zip", groupId = "main"},
                new {link = "https://example.test/preview.jpg", groupId = "preview"}
            }}})
        };
        Db.PostParserTasks.Add(task);
        await Db.SaveChangesAsync();
        return task;
    }

    [TestMethod]
    public async Task Saved_result_creates_a_plan_without_starting_parser_or_workflow_and_keeps_origin()
    {
        var saved = await Saved();
        var original = saved.Results;
        var submission = await Adapter.CreateAsync(new() {TaskId = saved.Id, Revision = 4, ResourceIndices = [0]}, Path.GetTempPath(), default);
        Assert.AreEqual(0, submission.Runs.Count);
        Assert.AreEqual(0, submission.ResourceIds.Count);
        var task = await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == submission.AcquisitionTaskIds.Single());
        Assert.IsNull(task.ResourceId);
        Assert.IsNull(task.WorkflowRunId);
        Assert.IsNotNull(task.PlanJson);
        using var plan = JsonDocument.Parse(task.PlanJson);
        var part = plan.RootElement.GetProperty("parts")[0];
        Assert.AreEqual(1, plan.RootElement.GetProperty("parts").GetArrayLength());
        Assert.AreEqual(2, part.GetProperty("sources").GetArrayLength(), "Keep explicitly grouped mirrors without executing them.");
        Assert.AreEqual("manual", part.GetProperty("selectionMode").GetString());
        Assert.AreEqual("https://example.test/mirror.zip", part.GetProperty("sources")[1].GetProperty("url").GetString());
        Assert.AreEqual("1", part.GetProperty("sources")[1].GetProperty("processingSource").GetProperty("resultKey").GetString());
        Assert.AreEqual("saved-code", part.GetProperty("sources")[0].GetProperty("accessCode").GetString());
        Assert.AreEqual("saved-password", part.GetProperty("sources")[0].GetProperty("archivePassword").GetString());
        Assert.AreEqual("extractArchive", part.GetProperty("processing").GetProperty("steps")[0].GetProperty("op").GetString());
        Assert.IsTrue(task.BuilderStateJson!.Contains("postParser", StringComparison.Ordinal));
        var unchanged = await Db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == saved.Id);
        Assert.AreEqual(original, unchanged.Results);
        Assert.AreEqual(4, unchanged.Revision);
        Assert.AreEqual(987654, unchanged.WorkflowRunId);
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task Stale_revision_and_two_mirrors_never_create_tasks()
    {
        var saved = await Saved();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Adapter.CreateAsync(new() {TaskId = saved.Id, Revision = 3, ResourceIndices = [0]}, Path.GetTempPath(), default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Adapter.CreateAsync(new() {TaskId = saved.Id, Revision = 4, ResourceIndices = [0, 1]}, Path.GetTempPath(), default));
        Assert.AreEqual(0, await Db.AcquisitionTasks.CountAsync());
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task Explicit_instruction_edit_is_scoped_to_new_plan_and_unselected_content_is_excluded()
    {
        var saved = await Saved();
        var original = saved.Results;
        var submission = await Adapter.CreateAsync(new() {TaskId = saved.Id, Revision = 4, ResourceIndices = [0],
            CandidateProcessingPlans = new() {{"0", "{\"requirement\":\"notRequired\",\"steps\":[]}"}}}, Path.GetTempPath(), default);
        var task = await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == submission.AcquisitionTaskIds.Single());
        using var plan = JsonDocument.Parse(task.PlanJson!);
        Assert.AreEqual("notRequired", plan.RootElement.GetProperty("parts")[0].GetProperty("processing").GetProperty("requirement").GetString());
        Assert.IsFalse(task.PlanJson!.Contains("preview.jpg", StringComparison.Ordinal));
        Assert.AreEqual(original, (await Db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == saved.Id)).Results);
    }

    [TestMethod]
    public async Task LegacyScreenshotExclusionKeepsSourceIndexAndSuppliesOnlyCoverMetadata()
    {
        var saved = await Saved();
        var result = System.Text.Json.Nodes.JsonNode.Parse(saved.Results!)!;
        var resources = result["DownloadInfo"]!["resources"]!.AsArray();
        var image = resources[2]!.DeepClone(); resources.RemoveAt(2); resources.Insert(0, image);
        saved.Results = result.ToJsonString();
        saved.ContentSnapshotJson = JsonSerializer.Serialize(new PostContent
            {MainHtml = "<p>Preview</p><img src='https://example.test/preview.jpg'>"}, JsonSerializerOptions.Web);
        await Db.SaveChangesAsync();
        var original = saved.Results;
        var domain = saved.ToDomainModel();
        CollectionAssert.AreEqual(new[] {0}, domain.DownloadMedia.ExcludedResourceIndices);
        Assert.AreEqual("https://example.test/preview.jpg", domain.DownloadMedia.CoverCandidates.Single().Url);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Adapter.CreateAsync(new() {TaskId = saved.Id, Revision = 4, ResourceIndices = [0]}, Path.GetTempPath(), default));
        var submission = await Adapter.CreateAsync(new() {TaskId = saved.Id, Revision = 4, ResourceIndices = [1]}, Path.GetTempPath(), default);
        var task = await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == submission.AcquisitionTaskIds.Single());
        using var plan = JsonDocument.Parse(task.PlanJson!);
        Assert.AreEqual("1", plan.RootElement.GetProperty("parts")[0].GetProperty("selectedSourceId").GetString());
        Assert.AreEqual(1, plan.RootElement.GetProperty("parts").GetArrayLength());
        Assert.AreEqual("https://example.test/preview.jpg", plan.RootElement.GetProperty("coverCandidates")[0].GetProperty("url").GetString());
        Assert.AreEqual(original, (await Db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == saved.Id)).Results);
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task OmittedManualAcquisitionDirectoryUsesTheCommonDefaultButExplicitEmptyDoesNot()
    {
        _services.GetRequiredService<Bootstrap.Components.Configuration.Abstractions.IBOptions<
            Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain.DownloaderGlobalOptions>>().Value.DefaultDownloadPath = Path.GetTempPath();
        var saved = await Saved();
        var input = new PostDownloadIntentRequest {TaskId = saved.Id, Revision = saved.Revision, ResourceIndices = [0]};
        var created = await Adapter.CreateAsync(input, null, default);
        Assert.AreEqual(Path.GetTempPath(), (await Db.AcquisitionTasks.FindAsync(created.AcquisitionTaskIds.Single()))!.TargetDirectory);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Adapter.CreateAsync(input, "", default));
    }
}
