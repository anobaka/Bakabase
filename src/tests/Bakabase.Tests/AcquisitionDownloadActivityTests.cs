using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.SourceAvailability;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionDownloadActivityTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private Reader _reader = null!;
    private AcquisitionDownloadActivityProjection _activity = null!;
    private AcquisitionTaskSearchService _search = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTime At = DateTime.Now.AddMinutes(-5);

    [TestInitialize] public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider();
        _db = _services.GetRequiredService<BakabaseDbContext>();
        _reader = new(); _activity = new(_db, _reader);
        _search = new(_db, _services.GetRequiredService<IAcquisitionService>(), downloadActivity: _activity);
    }
    [TestCleanup] public void Cleanup() => (_services as IDisposable)?.Dispose();

    private static CloudDownloadTarget Target(string id, int download, bool excluded = false, string? problem = null) => new()
    {
        Id = id, DownloadTaskId = download, Excluded = excluded, Problem = problem, SelectedSourceId = "source",
        Sources = [new() {Id = "source", Url = "https://example.org/" + id, ProviderId = "http"}]
    };
    private async Task<(AcquisitionTaskDbModel Task, WorkflowRunDbModel Run)> Seed(List<CloudDownloadTarget> targets,
        string kind = "download.awaitCompletion", string? waitReason = "cloud.cloudAttention")
    {
        var workset = new DownloadWorkset {Targets = targets};
        var run = new WorkflowRunDbModel {StartedAt = At, WaitingSince = At, CurrentStepIndex = 0,
            Status = WorkflowRunStatus.Waiting, WaitReason = waitReason,
            PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Test", TriggerKind = "manual", Nodes = [new() {NodeId = "node", Kind = kind}]}),
            CurrentItemJson = JsonSerializer.Serialize(new {item = workset}, Json),
            WaitPromptJson = JsonSerializer.Serialize(workset, Json)};
        _db.WorkflowRuns.Add(run); await _db.SaveChangesAsync();
        var task = new AcquisitionTaskDbModel {Title = "Target", BuilderId = "prepared", WorkflowRunId = run.Id,
            Status = AcquisitionStatus.Waiting, Phase = "attention", CreatedAt = At, UpdatedAt = At, PlanHistoryJson = "immutable history"};
        _db.AcquisitionTasks.Add(task);
        _db.WorkflowNodeExecutions.Add(new() {WorkflowRunId = run.Id, NodeId = "node", StepIndex = 0, Kind = kind,
            Attempt = 1, Status = "waiting", StartedAt = At.ToUniversalTime(), CompletedAt = At.ToUniversalTime(),
            OutputsJson = JsonSerializer.Serialize(workset.GetWorkflowOutputMetadata(kind), Json)});
        await _db.SaveChangesAsync(); return (task, run);
    }
    private static AcquisitionRuntimeDownload Download(string id, string status, int? run = null, string? parent = null) =>
        new(id, parent, run, status, 20, 20, 100, DateTimeOffset.UtcNow, status == "failed" ? "LOCAL_STORAGE_RESERVE_LOW" : null);

    [TestMethod]
    public async Task CloudStorageWaitStaysInTheQueueUntilAChildStartsDownloading()
    {
        await Seed([Target("one", 11), Target("two", 12)]);
        var wait = Download("site:11", "running") with
        {
            Stage = AcquisitionRuntimeDownloadReader.SafeStage("WaitingForCloudStorageCapacity")
        };
        _reader.Values = [wait, Download("site:12", "queued")];
        var waiting = await _search.SearchAsync(new(Stage: "running"));
        Assert.AreEqual("waitingForCloudStorage", waiting.Items.Single().DownloadActivity);
        _reader.Values = [wait, Download("site:12", "running")];
        var active = await _search.SearchAsync(new(Stage: "running"));
        Assert.AreEqual("running", active.Items.Single().DownloadActivity);
    }

    [TestMethod]
    public async Task CountsFiltersAndPageShareActivityWhilePersistedWaitingSurvivesCompletion()
    {
        var (running, run) = await Seed([Target("one", 11, problem: "LOCAL_STORAGE_RESERVE_LOW")]);
        var (queued, _) = await Seed([Target("two", 12)]);
        _reader.Values = [Download("site:11", "running"), Download("site:12", "queued")];
        running.Status = AcquisitionStatus.Failed; await _db.SaveChangesAsync(); // stale task index; current run is authoritative
        var page = await _search.SearchAsync(new(Stage: "running", PageSize: 1));
        Assert.AreEqual(2, page.StageCounts["running"]); Assert.AreEqual(0, page.StageCounts["attention"]);
        Assert.AreEqual(2, page.TotalCount); CollectionAssert.AreEquivalent(new[] {running.Id, queued.Id}, page.FilteredTaskIds.ToArray());
        Assert.AreEqual(AcquisitionStatus.Waiting, page.Items.Single().Status); Assert.AreEqual("running", page.Items.Single().Stage);
        Assert.AreEqual("queued", page.Items.Single().DownloadActivity);
        Assert.AreEqual(0, page.AttentionCounts["other"]);
        Assert.AreEqual(0, page.Items.Single().AttentionTags.Count);
        var allActive = await _search.SearchAsync(new(Stage: "running"));
        Assert.AreEqual(2, allActive.Items.Count);
        Assert.IsTrue(allActive.Items.All(t => t.AttentionTags.Count == 0), "Healthy queued and running tasks have no attention tag.");
        Assert.AreEqual(0, (await _search.SearchAsync(new(Stage: "running", AttentionTag: "other"))).TotalCount);
        Assert.AreEqual(0, (await _search.SearchAsync(new(AttentionTag: "localStorage"))).TotalCount);
        _reader.Values = [Download("site:11", "completed"), Download("site:12", "stopped")];
        var settled = await _search.SearchAsync(new());
        Assert.AreEqual(0, settled.StageCounts["running"]); Assert.AreEqual(2, settled.StageCounts["attention"]);
        Assert.IsTrue(settled.Items.All(i => i.DownloadActivity == null));
        Assert.AreEqual(0, settled.AttentionCounts["localStorage"]);
        await _db.Entry(run).ReloadAsync(); await _db.Entry(running).ReloadAsync();
        Assert.AreEqual(WorkflowRunStatus.Waiting, run.Status); Assert.AreEqual(AcquisitionStatus.Failed, running.Status);
        Assert.AreEqual("immutable history", running.PlanHistoryJson);
    }

    [TestMethod]
    public async Task CompletedDownloadsBecomePendingLocalFilesAndOnlySupersedeOlderPerPartWarnings()
    {
        var targets = new List<CloudDownloadTarget>
            {Target("one", 11, problem: "LOCAL_STORAGE_RESERVE_LOW"), Target("two", 12, problem: "LOCAL_STORAGE_RESERVE_LOW")};
        var (task, run) = await Seed(targets);
        task.PlanRevision = 1;
        task.PlanJson = JsonSerializer.Serialize(new {parts = targets.Select(t => new {id = t.Id})}, Json);
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Completed download", TriggerKind = CloudWorkflow.Trigger, Nodes = [
            new() {NodeId = "node", Kind = "download.awaitCompletion"},
            new() {NodeId = "publish", Kind = "fs.publishArtifacts"}]});
        run.CurrentItemJson = JsonSerializer.Serialize(new {type = typeof(DownloadWorkset).FullName + ", " +
            typeof(DownloadWorkset).Assembly.GetName().Name, item = new DownloadWorkset {Targets = targets}}, Json);
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task",
            EntityId = task.Id.ToString(), Revision = "1"});
        await _db.SaveChangesAsync();

        _reader.Values = [Download("site:11", "completed"), Download("site:12", "failed")];
        var mixed = await _search.SearchAsync(new());
        Assert.AreEqual(0, mixed.FactCounts["downloadedPending"]);
        Assert.AreEqual(1, mixed.FactCounts["spaceInsufficient"]);

        _reader.Values = [Download("site:11", "completed"), Download("site:12", "completed")];
        var finished = await _search.SearchAsync(new());
        Assert.AreEqual(1, finished.FactCounts["downloadedPending"]);
        Assert.AreEqual(0, finished.FactCounts["spaceInsufficient"]);
        Assert.IsNull(finished.Items.Single().DownloadActivity);
        Assert.AreEqual("attention", finished.Items.Single().Stage);

        var latest = await _db.WorkflowNodeExecutions.SingleAsync(n => n.WorkflowRunId == run.Id);
        latest.CompletedAt = DateTime.UtcNow.AddMinutes(1);
        await _db.SaveChangesAsync();
        Assert.AreEqual(1, (await _search.SearchAsync(new())).FactCounts["spaceInsufficient"]);
    }

    [TestMethod]
    public async Task MixedPartFailureRemainsSearchableWhileHealthyPartHistoricalFailureClears()
    {
        var (task, _) = await Seed([Target("healthy", 11, problem: "LOCAL_STORAGE_FULL"), Target("failed", 12, problem: "BAIDU_SHARE_EXPIRED")]);
        _reader.Values = [Download("site:11", "running"), Download("site:12", "failed")];
        var page = await _search.SearchAsync(new(AttentionTag: "sourceUnavailable"));
        Assert.AreEqual(task.Id, page.Items.Single().Id); Assert.AreEqual("running", page.Items.Single().DownloadActivity);
        Assert.AreEqual(0, page.AttentionCounts["localStorage"]);
        Assert.AreEqual(1, page.AttentionCounts["sourceUnavailable"]);
    }

    [TestMethod]
    public async Task LiveVerificationSupersedesOnlyTheBoundRunsTargetAndKeepsAnotherPartsFailure()
    {
        var (current, run) = await Seed([Target("verified", 11, problem: "LOCAL_STORAGE_FULL"),
            Target("unavailable", 12, problem: "BAIDU_SHARE_EXPIRED")]);
        var (other, _) = await Seed([Target("earlier", 11, problem: "LOCAL_STORAGE_FULL")]);
        _reader.Values = [new("site:11", null, run.Id, "running", null, null, null,
            DateTimeOffset.UtcNow, null, Stage: "VerifyingSource"), Download("site:12", "failed", run.Id)];
        var activities = await _activity.ReadAsync([current.Id, other.Id]);
        Assert.AreEqual("running", activities[current.Id].Activity);
        CollectionAssert.AreEquivalent(new[] {"verified"}, activities[current.Id].RecoveredPartIds.ToArray());
        Assert.IsFalse(activities.ContainsKey(other.Id));
        var unavailable = await _search.SearchAsync(new(AttentionTag: "sourceUnavailable"));
        Assert.AreEqual(current.Id, unavailable.Items.Single().Id);
        Assert.AreEqual(2, await _db.WorkflowRuns.CountAsync(r => r.Status == WorkflowRunStatus.Waiting));
    }

    [DataTestMethod]
    [DataRow("UNRECOGNIZED_PROVIDER_DIAGNOSTIC")]
    [DataRow(null)]
    public async Task UnrecoveredUnknownWarningRemainsOtherAlongsideHealthyQueuedPart(string? code)
    {
        var (task, run) = await Seed([Target("healthy", 11, problem: "LOCAL_STORAGE_FULL"), Target("failed", 12)]);
        var node = await _db.WorkflowNodeExecutions.SingleAsync(n => n.WorkflowRunId == run.Id);
        node.OutputsJson = JsonSerializer.Serialize(new[]
        {
            new WorkflowOutputBlock {Kind = "warning", Key = "healthy:problem", Code = "LOCAL_STORAGE_FULL"},
            new WorkflowOutputBlock {Kind = "warning", Key = "failed:problem", Code = code}
        }, Json);
        await _db.SaveChangesAsync();
        _reader.Values = [Download("site:11", "queued"), Download("site:12", "failed")];
        var page = await _search.SearchAsync(new(Stage: "running", AttentionTag: "other"));
        Assert.AreEqual(task.Id, page.Items.Single().Id);
        Assert.AreEqual("queued", page.Items.Single().DownloadActivity);
        CollectionAssert.AreEqual(new[] {"other"}, page.Items.Single().AttentionTags.ToArray());
        Assert.AreEqual(1, page.AttentionCounts["other"]);
        Assert.AreEqual(0, page.AttentionCounts["localStorage"]);
    }

    [TestMethod]
    public async Task ExcludedOldRunAndStoppedNativeChildrenCannotCreateActivity()
    {
        var (task, run) = await Seed([Target("excluded", 11, excluded: true), Target("real", 12)]);
        _reader.Values = [Download("site:11", "running"), Download("site:12", "completed"),
            Download("http:old", "running", run.Id + 100, "site:12")];
        var finished = (await _activity.ReadAsync([task.Id]))[task.Id];
        Assert.IsNull(finished.Activity);
        CollectionAssert.AreEqual(new[] {"real"}, finished.CompletedPartIds.ToArray());
        _db.ManagedDownloadTasks.Add(new() {Id = "http:stopped", Category = "http", Provider = "http", ProviderTaskId = "stopped",
            ParentId = "site:12", WorkflowRunId = run.Id, UserStopped = true});
        await _db.SaveChangesAsync();
        _reader.Values = [Download("site:12", "completed"), Download("http:stopped", "running", run.Id, "site:12")];
        Assert.AreEqual(0, (await _activity.ReadAsync([task.Id])).Count);
    }

    [DataTestMethod]
    [DataRow("http")]
    [DataRow("bt")]
    public async Task NativeChildActivityReplacesCompletedParentAndRunningWins(string category)
    {
        var (task, run) = await Seed([Target("real", 12), Target("second", 13)]);
        _reader.Values = [Download("site:12", "completed"), Download(category + ":child", "running", run.Id, "site:12"),
            Download("site:13", "queued")];
        Assert.AreEqual("running", (await _activity.ReadAsync([task.Id]))[task.Id].Activity);
    }

    [TestMethod]
    public async Task ManualProcessingAndTerminalTasksKeepTheirActualLifecycle()
    {
        var (manual, _) = await Seed([Target("part", 11)], waitReason: "cloud.chooseSources");
        var (processing, _) = await Seed([Target("part", 11)], "acquisition.plan.process");
        var (terminal, run) = await Seed([Target("part", 11)]);
        run.Status = WorkflowRunStatus.Success; terminal.Status = AcquisitionStatus.Completed; await _db.SaveChangesAsync();
        _reader.Values = [Download("site:11", "running")];
        Assert.AreEqual(0, (await _activity.ReadAsync([manual.Id, processing.Id, terminal.Id])).Count);
    }

    [TestMethod]
    public async Task MalformedCheckpointIsSafeAndDurableCurrentOwnershipSupportsLegacyDownloads()
    {
        var (task, run) = await Seed([]);
        run.CurrentItemJson = "malformed private checkpoint"; run.WaitPromptJson = "malformed private prompt";
        run.PlanSnapshotJson = "malformed private plan";
        _db.DownloadResultOwners.Add(new() {AcquisitionTaskId = task.Id, WorkflowRunId = run.Id, DownloadTaskId = 12});
        _db.CloudDownloadOperations.Add(new() {Id = "old", Kind = "download", WorkflowRunId = run.Id + 100,
            TargetId = "part", DownloadTaskId = 13});
        await _db.SaveChangesAsync();
        _reader.Values = [Download("site:12", "queued"), Download("site:13", "running")];
        Assert.AreEqual("queued", (await _activity.ReadAsync([task.Id]))[task.Id].Activity);
        CollectionAssert.AreEqual(new[] {12}, _reader.SiteIds.ToArray());
    }

    private sealed class Reader : IAcquisitionRuntimeDownloadReader
    {
        public IReadOnlyList<AcquisitionRuntimeDownload> Values { get; set; } = [];
        public IReadOnlyCollection<int> SiteIds { get; private set; } = [];
        public Task<IReadOnlyList<AcquisitionRuntimeDownload>> ReadAsync(IReadOnlyCollection<int> siteIds,
            IReadOnlyCollection<string> transportIds, IReadOnlyCollection<int> runIds, CancellationToken ct)
        { SiteIds = siteIds; return Task.FromResult(Values); }
    }
}
