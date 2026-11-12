using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Services;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.Workflow;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class WorkflowSourceHistoryTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private WorkflowSourceIndex<BakabaseDbContext> _index = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider();
        _db = _services.GetRequiredService<BakabaseDbContext>();
        _index = new(_db, new WorkflowTriggerRegistry([new CloudSourcesTrigger(), new PostParserManualTrigger()]), new());
    }

    private async Task<WorkflowRunDbModel> Add(string trigger, string payload, List<WorkflowRunSourceDbModel>? sources = null)
    {
        var def = new WorkflowDefinitionDbModel {Name = trigger, TriggerKind = trigger};
        _db.WorkflowDefinitions.Add(def);
        await _db.SaveChangesAsync();
        var run = new WorkflowRunDbModel {WorkflowDefinitionId = def.Id, PayloadJson = payload,
            Status = WorkflowRunStatus.Success, StartedAt = DateTime.Now, Sources = sources ?? []};
        _db.WorkflowRuns.Add(run);
        await _db.SaveChangesAsync();
        return run;
    }

    [TestMethod]
    public async Task HistoricalTypedPayloadsIndexParsingAndDownstreamRunsWithoutConfusingTasksOrRevisions()
    {
        var parser = await Add(PostParserWorkflow.Trigger, "{\"taskId\":41,\"revision\":1}");
        var downstream = await Add(CloudWorkflow.Trigger, JsonSerializer.Serialize(new DownloadWorkset {PostTaskId = 41, PostRevision = 1}, WorkflowJson.Options));
        await Add(CloudWorkflow.Trigger, "{\"postTaskId\":410,\"postRevision\":1}");
        var newer = await Add(PostParserWorkflow.Trigger, "{\"taskId\":41,\"revision\":2}");
        await Add(PostParserWorkflow.Trigger, "{\"taskId\":41,\"TaskId\":410}");
        await _index.EnsureIndexedAsync(default);
        await _index.EnsureIndexedAsync(default);
        var controller = new WorkflowSourcesController(_db, _index, [], null!, null!);
        var result = await controller.Query(new() {Sources = [new("postParser", "task", "41", "1")], PageSize = 100}, default);
        Assert.AreEqual(2, result.Data!.Single().RunCount);
        CollectionAssert.AreEqual(new[] {downstream.Id, parser.Id}, result.Data.Single().Runs.Select(r => r.Run.Id).ToArray());
        var all = await controller.Query(new() {Sources = [new("postParser", "task", "41")], PageSize = 100}, default);
        Assert.AreEqual(3, all.Data!.Single().RunCount);
        Assert.AreEqual(newer.Id, all.Data.Single().Runs[0].Run.Id);
    }

    [TestMethod]
    public async Task MultipleSourcesMayShareEveryRunAndDeletingRunCascadesOnlyItsLinks()
    {
        var refs = new Payload([new("scan", "entry", "a", "1"), new("postParser", "task", "41", "1")]);
        var first = await Add("custom", "{}", WorkflowRunSourceDbModel.From(refs));
        var second = await Add("custom", "{}", WorkflowRunSourceDbModel.From(refs));
        var controller = new WorkflowSourcesController(_db, _index, [], null!, null!);
        var result = await controller.Query(new() {Sources = refs.SourceRefs.ToList()}, default);
        Assert.IsTrue(result.Data!.All(g => g.RunCount == 2));
        await _db.WorkflowRuns.Where(r => r.Id == first.Id).ExecuteDeleteAsync();
        Assert.AreEqual(2, await _db.Set<WorkflowRunSourceDbModel>().CountAsync());
        Assert.IsTrue(await _db.Set<WorkflowRunSourceDbModel>().AllAsync(l => l.WorkflowRunId == second.Id));
    }

    [TestMethod]
    public async Task ExcludingProducerTriggersKeepsCountsAndPagesAlignedAndProducerOnlyHasNoDownstreamRun()
    {
        await Add(PostParserWorkflow.Trigger, "{\"taskId\":42,\"revision\":1}");
        var first = await Add(CloudWorkflow.Trigger,
            JsonSerializer.Serialize(new DownloadWorkset {PostTaskId = 42, PostRevision = 1}, WorkflowJson.Options));
        var waiting = await Add(CloudWorkflow.Trigger,
            JsonSerializer.Serialize(new DownloadWorkset {PostTaskId = 42, PostRevision = 1}, WorkflowJson.Options));
        waiting.Status = WorkflowRunStatus.Waiting;
        await Add(PostParserWorkflow.Trigger, "{\"taskId\":42,\"revision\":2}");
        await Add(PostParserWorkflow.Trigger, "{\"taskId\":420,\"revision\":1}");
        await _db.SaveChangesAsync();
        var controller = new WorkflowSourcesController(_db, _index, [], null!, null!);
        var query = new WorkflowSourceRunsQuery {Sources = [new("postParser", "task", "42"), new("postParser", "task", "420")],
            ExcludedTriggerKinds = [PostParserWorkflow.Trigger], PageSize = 1};
        var firstPage = (await controller.Query(query, default)).Data!;
        var mixed = firstPage.Single(g => g.Source.EntityId == "42");
        Assert.AreEqual(2, mixed.RunCount);
        Assert.AreEqual(waiting.Id, mixed.Runs.Single().Run.Id);
        Assert.AreEqual(1, mixed.StatusCounts!["waiting"]);
        Assert.AreEqual(1, mixed.StatusCounts["success"]);
        var producerOnly = firstPage.Single(g => g.Source.EntityId == "420");
        Assert.AreEqual(0, producerOnly.RunCount);
        Assert.AreEqual(0, producerOnly.Runs.Count);
        Assert.AreEqual(0, producerOnly.StatusCounts!.Count);
        var secondPage = (await controller.Query(query with {PageIndex = 2}, default)).Data!.Single(g => g.Source.EntityId == "42");
        Assert.AreEqual(first.Id, secondPage.Runs.Single().Run.Id);
        Assert.AreEqual(2, secondPage.RunCount);
        Assert.AreEqual(2, secondPage.StatusCounts!.Values.Sum());
        var unfiltered = (await controller.Query(query with {ExcludedTriggerKinds = null}, default)).Data!;
        Assert.AreEqual(4, unfiltered.Single(g => g.Source.EntityId == "42").RunCount);
        Assert.AreEqual(1, unfiltered.Single(g => g.Source.EntityId == "420").RunCount);
    }

    [TestMethod]
    public async Task ExcludingTriggerDoesNotHideRunsWhoseDefinitionWasDeleted()
    {
        var source = new WorkflowSourceRef("scan", "entry", "unknown-trigger");
        var known = await Add("producer.custom", "{}", WorkflowRunSourceDbModel.From(new Payload([source])));
        var unknown = await Add("producer.custom", "{}", WorkflowRunSourceDbModel.From(new Payload([source])));
        await _db.WorkflowDefinitions.Where(d => d.Id == unknown.WorkflowDefinitionId).ExecuteDeleteAsync();
        var controller = new WorkflowSourcesController(_db, _index, [], null!, null!);
        var group = (await controller.Query(new() {Sources = [source], ExcludedTriggerKinds = ["producer.custom"]}, default)).Data!.Single();
        Assert.AreEqual(1, group.RunCount);
        Assert.AreEqual(unknown.Id, group.Runs.Single().Run.Id);
        Assert.AreEqual("", group.Runs.Single().WorkflowName);
        Assert.AreEqual(1, group.StatusCounts!["success"]);
        Assert.AreNotEqual(known.Id, group.Runs.Single().Run.Id);
    }

    [TestMethod]
    public async Task ExcludedTriggersAreBoundedBeforeRunningTheQuery()
    {
        var controller = new WorkflowSourcesController(_db, _index, [], null!, null!);
        foreach (var invalid in new List<string>[] {Enumerable.Repeat("producer", 33).ToList(), [""], [new string('x', 129)], ["bad\nkind"]})
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => controller.Query(new() {ExcludedTriggerKinds = invalid}, default));
    }

    [TestMethod]
    public void ReuseSummaryKeepsPublicationSeparateFromDownloadAndReadsNoAccessSecrets()
    {
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Success,
            OutputItemsJson = "[{\"targets\":[{\"downloadTaskId\":8467,\"processedFileCount\":10,\"publishedFileCount\":10,\"executionStatus\":\"reused\",\"publishedDirectory\":\"/downloads/result\"}]}]"};
        var view = new WorkflowSourceRunView {Run = new(), WorkflowName = "fixture"};
        new CloudSourceRunProjection().Enrich(run, view);
        Assert.AreEqual("delivered", view.Phase);
        Assert.IsTrue(view.Reused);
        Assert.AreEqual(10, view.CompletedFileCount);
        CollectionAssert.AreEqual(new[] {"site:8467"}, view.DownloadTaskIds.ToArray());
    }

    private sealed record Payload(IReadOnlyList<WorkflowSourceRef> SourceRefs) : IWorkflowSourcePayload;

    [TestMethod]
    public async Task HistoryPreservesBuiltinIdentityWithoutInferringItFromTheName()
    {
        var refs = new Payload([new("scan", "entry", "translation")]);
        var builtin = await Add("custom", "{}", WorkflowRunSourceDbModel.From(refs));
        var user = await Add("custom", "{}", WorkflowRunSourceDbModel.From(refs));
        const string name = "Acquire supported sources (v1)";
        await _db.WorkflowDefinitions.Where(d => d.Id == builtin.WorkflowDefinitionId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Name, name).SetProperty(d => d.IsBuiltin, true));
        await _db.WorkflowDefinitions.Where(d => d.Id == user.WorkflowDefinitionId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Name, name));

        var controller = new WorkflowSourcesController(_db, _index, [], null!, null!);
        var response = await controller.Query(new() {Sources = refs.SourceRefs.ToList()}, default);
        var runs = response.Data!.Single().Runs;
        Assert.AreEqual(name, runs.Single(r => r.Run.Id == builtin.Id).WorkflowName);
        Assert.IsTrue(runs.Single(r => r.Run.Id == builtin.Id).WorkflowIsBuiltin);
        Assert.AreEqual(name, runs.Single(r => r.Run.Id == user.Id).WorkflowName);
        Assert.IsFalse(runs.Single(r => r.Run.Id == user.Id).WorkflowIsBuiltin);
    }

    [TestMethod]
    public void SummaryCountsFinalDeliveryInsteadOfRawInputsOrExcludedMirrors()
    {
        var row = new WorkflowRunDbModel {Status = WorkflowRunStatus.Success,
            OutputItemsJson = "[{\"targets\":[{\"fileCount\":20,\"processedFileCount\":3,\"publishedFileCount\":3},{\"fileCount\":100,\"excluded\":true}]}]"};
        var view = new WorkflowSourceRunView {Run = new(), WorkflowName = "fixture"};
        new CloudSourceRunProjection().Enrich(row, view);
        Assert.AreEqual(3, view.CompletedFileCount);
    }

    [TestMethod]
    public async Task StatusCountsIncludeOlderWaitingRunsOutsideTheRequestedPageAndRespectRevision()
    {
        var source = new WorkflowSourceRef("scan", "entry", "status", "1");
        var waiting = await Add("custom", "{}", WorkflowRunSourceDbModel.From(new Payload([source])));
        waiting.Status = WorkflowRunStatus.Waiting;
        var failed = await Add("custom", "{}", WorkflowRunSourceDbModel.From(new Payload([source])));
        failed.Status = WorkflowRunStatus.Failed;
        var newest = await Add("custom", "{}", WorkflowRunSourceDbModel.From(new Payload([source])));
        await Add("custom", "{}", WorkflowRunSourceDbModel.From(new Payload([source with {Revision = "2"}])));
        await _db.SaveChangesAsync();

        var controller = new WorkflowSourcesController(_db, _index, [], null!, null!);
        var result = (await controller.Query(new() {Sources = [source], PageSize = 1}, default)).Data!.Single();
        Assert.AreEqual(3, result.RunCount);
        Assert.AreEqual(newest.Id, result.Runs.Single().Run.Id);
        Assert.AreEqual(1, result.StatusCounts!["waiting"]);
        Assert.AreEqual(1, result.StatusCounts["failed"]);
        Assert.AreEqual(1, result.StatusCounts["success"]);
        Assert.AreEqual(3, result.StatusCounts.Values.Sum());
        var empty = (await controller.Query(new() {Sources = [source with {EntityId = "absent"}]}, default)).Data!.Single();
        Assert.AreEqual(0, empty.RunCount);
        Assert.AreEqual(0, empty.StatusCounts!.Count);
    }

    [TestMethod]
    public async Task WaitingLegacySnapshotShowsOnlyPlannedDirectoryAndAllowlistedProblemWithoutInventingArtifacts()
    {
        var source = new WorkflowSourceRef("postParser", "task", "fixture");
        var row = await Add("custom", "{}", WorkflowRunSourceDbModel.From(new Payload([source])));
        row.Status = WorkflowRunStatus.Waiting;
        row.CurrentStepIndex = 3;
        row.StepStatsJson = "[{\"stepIndex\":2,\"kind\":\"recorded.previous\"}]";
        _db.Set<WorkflowActivityDbModel>().Add(new() {WorkflowDefinitionId = row.WorkflowDefinitionId,
            Order = 3, Kind = "edited.currentDefinition"});
        row.CurrentItemJson = JsonSerializer.Serialize(new {item = new {outputDirectory = "/server/planned",
            targets = new[] {new {id = "0", files = Array.Empty<string>(), problem =
                "Namespace.CloudProviderException: PIKPAK_ORIGINAL_FILE_UNAVAILABLE: https://secret.example/?token=PRIVATE_TOKEN at hidden.stack"},
                new {id = "excluded", files = Array.Empty<string>(), problem = "BAIDU_LOGIN_REQUIRED"}}}}, WorkflowJson.Options)
            .Replace("\"id\":\"excluded\"", "\"id\":\"excluded\",\"excluded\":true");
        await _db.SaveChangesAsync();
        var controller = new WorkflowSourcesController(_db, _index, [new CloudSourceRunProjection()], null!, null!);
        var view = (await controller.Query(new() {Sources = [source]}, default)).Data!.Single().Runs.Single();
        Assert.AreEqual("waiting", view.Phase);
        Assert.AreEqual(0, view.CompletedFileCount);
        Assert.AreEqual(0, view.Run.NodeExecutions.Count);
        Assert.IsNull(view.Run.ActivityKind, "A changed definition cannot invent a legacy run's unrecorded node kind.");
        var location = view.OutputLocations!.Single();
        Assert.AreEqual("working", location.Kind);
        Assert.AreEqual("/server/planned", location.Directory);
        Assert.IsTrue(location.IsPlanned);
        Assert.IsNull(location.ArtifactId);
        Assert.IsNull(location.FileCount);
        CollectionAssert.AreEqual(new[] {"PIKPAK_ORIGINAL_FILE_UNAVAILABLE"}, view.ProblemCodes!.ToArray());
        Assert.IsFalse(JsonSerializer.Serialize(view.OutputLocations).Contains("PRIVATE_TOKEN"));
    }

    [TestMethod]
    public async Task ArtifactLocationsReadLedgerAndRebaseAppDataWithoutHashingOrChangingRecordedState()
    {
        var source = new WorkflowSourceRef("scan", "entry", "artifacts");
        var row = await Add("custom", "{}", WorkflowRunSourceDbModel.From(new Payload([source])));
        row.Status = WorkflowRunStatus.Waiting;
        row.CurrentItemJson = "{\"item\":{\"targets\":[{\"processedArtifactId\":\"processed-id\",\"processedDirectory\":\"/old-appdata/files\",\"processedFiles\":[\"/old-appdata/files/missing-on-disk.bin\"]}]}}";
        var entries = new[] {new ArtifactManifestEntry("missing-on-disk.bin", 17, new string('a', 64))};
        var fingerprint = ArtifactManifestFiles.Fingerprint(entries);
        _db.Set<ArtifactDbModel>().Add(new() {Id = "processed-id", OperationId = "operation", Kind = "processedFiles",
            RootPath = "artifact-operations/files", RootIsAppData = true, State = ArtifactLocationState.Quarantined,
            Fingerprint = fingerprint, ManifestJson = JsonSerializer.Serialize(new ArtifactManifest(entries, fingerprint), WorkflowJson.Options)});
        row.NodeExecutions.Add(new() {NodeId = "stable-node", StepIndex = 1, Kind = "fs.processArtifacts", Attempt = 1,
            OutputsJson = JsonSerializer.Serialize(new[] {new WorkflowOutputBlock {Kind = "artifact", Key = "files",
                ArtifactId = "processed-id", Directory = "/stale/path"}}, WorkflowJson.Options)});
        await _db.SaveChangesAsync();
        var appData = Path.Combine(Path.GetTempPath(), "not-created-" + Guid.NewGuid().ToString("N"));
        var store = new ArtifactOperationStore(_db, () => appData);
        var controller = new WorkflowSourcesController(_db, _index, [new CloudSourceRunProjection()], null!, null!, store);
        var view = (await controller.Query(new() {Sources = [source]}, default)).Data!.Single().Runs.Single();
        var location = view.OutputLocations!.Single();
        Assert.AreEqual("processed", location.Kind);
        Assert.AreEqual(Path.Combine(appData, "artifact-operations", "files"), location.Directory);
        Assert.AreEqual("quarantined", location.State);
        Assert.AreEqual(1, location.FileCount);
        Assert.AreEqual(17L, location.TotalBytes);
        Assert.IsFalse(location.IsPlanned);
        Assert.IsFalse(Directory.Exists(appData));
        Assert.AreEqual(ArtifactLocationState.Quarantined,
            (await _db.Set<ArtifactDbModel>().AsNoTracking().SingleAsync(a => a.Id == "processed-id")).State);
    }

    [TestMethod]
    public void AcquisitionWithoutCloudTargetsStillExposesRecordedArtifactReferences()
    {
        var row = new WorkflowRunDbModel {Status = WorkflowRunStatus.Success,
            OutputItemsJson = "[{\"workingDirectory\":\"/working\",\"targetDirectory\":\"/delivered\",\"filesCount\":2,\"artifacts\":{\"artifact.publishedId\":\"final-id\"}}]"};
        var view = new WorkflowSourceRunView {Run = new(), WorkflowName = "custom"};
        new CloudSourceRunProjection().Enrich(row, view);
        Assert.AreEqual("delivered", view.Phase);
        Assert.AreEqual(2, view.CompletedFileCount);
        Assert.AreEqual("final-id", view.OutputLocations!.Single(l => l.Kind == "published").ArtifactId);
    }

    [TestMethod]
    public void ProblemProjectionRejectsArbitraryUppercaseSecretsAndResolvedAttemptWarnings()
    {
        var view = new WorkflowSourceRunView {Run = new() {Status = WorkflowRunStatus.Success,
            NodeExecutions = [new() {Id = 1, Outputs = [new() {Kind = "warning", Key = "old", Code = "BAIDU_LOGIN_REQUIRED"}]}]}, WorkflowName = "custom"};
        WorkflowSourceRunOutputs.CaptureRecordedMetadata(view);
        WorkflowSourceRunProblems.Add(view, "CloudException: RCLONE_ACCOUNT_CHANGED: token PRIVATE_ACCESS_TOKEN at Source.cs:1");
        WorkflowSourceRunProblems.Add(view, "OTHER_PRIVATE_CODE_AND_TOKEN");
        CollectionAssert.AreEqual(new[] {"RCLONE_ACCOUNT_CHANGED"}, view.ProblemCodes!.ToArray());
    }

    [DataTestMethod]
    [DataRow("GOOGLE_DOWNLOAD_QUOTA_EXCEEDED")]
    [DataRow("GOOGLE_DRIVE_SHARE_UNAVAILABLE_OR_ACCESS_REQUIRED")]
    [DataRow("GOFILE_SHARE_NOT_FOUND")]
    [DataRow("GOFILE_ACCESS_REQUIRED")]
    [DataRow("CLOUD_NETWORK_ERROR")]
    [DataRow("CLOUD_RATE_LIMITED")]
    [DataRow("CLOUD_ACCESS_REQUIRED")]
    [DataRow("CLOUD_TOOL_FAILED")]
    [DataRow("BAIDU_SHARE_VERIFICATION_FAILED")]
    [DataRow("BAIDU_SHARE_CANCELLED")]
    [DataRow("BAIDU_SHARE_EXPIRED")]
    [DataRow("BAIDU_SHARE_PAGE_INVALID")]
    [DataRow("BAIDU_SHARE_ACCESS_CODE_REJECTED")]
    [DataRow("BAIDU_SHARE_VERIFICATION_REQUIRED")]
    [DataRow("BAIDU_SHARE_RESPONSE_INVALID")]
    [DataRow("BAIDU_SHARE_NETWORK_ERROR")]
    [DataRow("BAIDU_TRANSFER_FAILED_CONFIRM_TARGET")]
    [DataRow("BAIDU_DOWNLOAD_INCOMPLETE")]
    [DataRow("BAIDU_OUTPUT_FILE_INVALID")]
    [DataRow("BAIDU_LOCAL_PATH_TOO_LONG")]
    [DataRow("BAIDU_LOCAL_PATH_MAPPING_CONFLICT")]
    [DataRow("BAIDU_LOCAL_PATH_MAPPING_INVALID")]
    [DataRow("BAIDU_OUTPUT_FILE_READ_FAILED")]
    [DataRow("BAIDU_LOCAL_FILE_CHANGED")]
    [DataRow("BAIDU_DOWNLOAD_CHECKSUM_MISMATCH")]
    [DataRow("PUBLICATION_IO_ERROR")]
    public void WrappedCurrentProviderCodesSurviveWithoutExceptionDetailsOrCanaries(string code)
    {
        var view = new WorkflowSourceRunView {Run = new() {Status = WorkflowRunStatus.Waiting}, WorkflowName = "custom"};
        WorkflowSourceRunProblems.Add(view, $"System.InvalidOperationException: BridgeException: {code}: " +
            "https://private.example/?token=SECRET_CANARY_UPPERCASE\n at PrivateStack.Canary() C:\\PRIVATE_PATH_CANARY");
        CollectionAssert.AreEqual(new[] {code}, view.ProblemCodes!.ToArray());
        Assert.IsFalse(JsonSerializer.Serialize(view.ProblemCodes).Contains("CANARY"));
    }

    [TestMethod]
    public void SuccessfulReusedDeliveryDoesNotBecomeBlockedByItsOlderFailedDownloadTask()
    {
        var row = new WorkflowRunDbModel {Status = WorkflowRunStatus.Success,
            OutputItemsJson = "[{\"targets\":[{\"downloadTaskId\":42,\"executionStatus\":\"reused\",\"publishedFileCount\":2,\"publishedDirectory\":\"/delivered\"}]}]"};
        var view = new WorkflowSourceRunView {Run = new() {Status = WorkflowRunStatus.Success}, WorkflowName = "custom"};
        new CloudSourceRunProjection().Enrich(row, view);
        var previous = new Bakabase.Modules.Downloader.Models.Tasks.DownloadTask
        {
            Id = "site:42", Category = "site", Provider = "site", ProviderTaskId = "42", Name = "fixture",
            OutputDirectory = "/old-download", Status = "failed", Error = "CloudException: GOOGLE_DOWNLOAD_QUOTA_EXCEEDED: SECRET_CANARY",
            CompletedBytes = 123
        };
        var unrelated = previous with {Id = "site:99", Error = "BAIDU_SHARE_VERIFICATION_FAILED"};
        var tasks = new[] {previous, unrelated}.ToDictionary(t => t.Id);
        WorkflowSourceRunOutputs.EnrichDownloadTasks(view, tasks);
        Assert.AreEqual("delivered", view.Phase);
        Assert.IsTrue(view.Reused);
        Assert.AreEqual(2, view.CompletedFileCount);
        Assert.AreEqual(0, view.ProblemCodes!.Count);
        Assert.AreEqual("site:42", view.DownloadTaskIds.Single());
        Assert.AreEqual(123L, view.DownloadedBytes);
        Assert.IsTrue(view.OutputLocations!.Single(l => l.Kind == "download").IsPlanned);

        var waiting = view with {Run = new() {Status = WorkflowRunStatus.Waiting}, ProblemCodes = []};
        WorkflowSourceRunOutputs.EnrichDownloadTasks(waiting, tasks);
        CollectionAssert.AreEqual(new[] {"GOOGLE_DOWNLOAD_QUOTA_EXCEEDED"}, waiting.ProblemCodes!.ToArray());
    }
}
