using System.Text.Json;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Bittorrent.Components;
using Bakabase.Modules.HttpDownloads.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Extensions;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Modules.AI.Services;
using Bakabase.Service.Components.SourceAvailability;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionRuntimeOverviewTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private AcquisitionRuntimeOverviewService _overview = null!;
    private SourceAvailabilityService _availability = null!;
    private Reader _reader = null!;
    private Probe _probe = null!;
    private string _root = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTime At = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Local);

    [TestMethod]
    public async Task FileProgressOnlyReportsTheCurrentArchiveAndNeverBorrowsWorkflowPercentages()
    {
        static async Task<(string? Stage, int? Progress)> Read(int runId, int? step, string kind, WorkflowTaskProgress live)
        {
            var result = await new FileProcessingActivityRuntimeProvider([]).ReadAsync(new(runId, step, kind,
                WorkflowRunStatus.Running, live, []));
            return (result.Stage, (int?)result.Progress);
        }
        const string kind = "acquisition.plan.process";
        var live = new WorkflowTaskProgress(17, kind, "extractingArchive", 1, 62);
        Assert.AreEqual(("extractingArchive", (int?)62), await Read(17, 1, kind, live));
        Assert.AreEqual(("preparingFiles", (int?)null), await Read(17, 1, kind,
            live with {Stage = "preparingFiles"}));
        Assert.AreEqual(("extractingArchive", (int?)null), await Read(17, 1, kind,
            live with {StageProgress = 120}));
        Assert.IsNull((await Read(17, 2, kind, live)).Stage);
        Assert.IsNull((await Read(17, 1, "download.wait", live)).Stage);
        Assert.IsNull((await Read(18, 1, kind, live)).Stage);
    }

    [TestMethod]
    public async Task FileCapabilityReportsOnlyItsExplicitAiQueueStateAndAuthoritativeLimit()
    {
        var provider = new FileProcessingActivityRuntimeProvider([new FileProcessorLlmExecutionPolicy()]);
        var progress = new WorkflowTaskProgress(17, "fs.assessDelivery", "waitingForAi", 1, 50);
        var queued = await provider.ReadAsync(new(17, 1, progress.ActivityKind, WorkflowRunStatus.Running, progress, []));
        Assert.AreEqual("waitingForAi", queued.Stage);
        Assert.AreEqual("ai.fileProcessor", queued.Capability!.Id);
        Assert.AreEqual("queued", queued.Capability.Status);
        Assert.AreEqual("capacity", queued.Capability.WaitReason);
        Assert.AreEqual(1, queued.Capability.MaximumParallelism);
        Assert.IsNull(queued.Progress, "Waiting is not measured progress.");
        var absent = await provider.ReadAsync(new(17, 1, progress.ActivityKind, WorkflowRunStatus.Running, null, []));
        Assert.IsNull(absent.Capability!.Status, "A running workflow does not prove an AI call is active or queued.");
        var stale = await provider.ReadAsync(new(17, 2, progress.ActivityKind, WorkflowRunStatus.Running, progress, []));
        Assert.IsNull(stale.Stage);
        Assert.IsNull(stale.Capability!.MaximumParallelism);
    }

    [TestMethod]
    public async Task RegisteredThirdCapabilityReachesTheOverviewWithoutAcquisitionSpecificCases()
    {
        var (task, _) = await Seed([], "test.renderFrames", WorkflowRunStatus.Running);
        using var services = ThirdCapabilityServices();
        await using var scope = services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ThirdCapabilityReader>();
        var overview = scope.ServiceProvider.GetRequiredService<AcquisitionRuntimeOverviewService>();
        var pending = overview.GetAsync([task.Id]);
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(pending.IsCompleted, "The overview must await the capability's asynchronous snapshot reader.");
        reader.Release.TrySetResult();
        var runtime = (await pending).Single();
        Assert.AreEqual("renderer", runtime.Capability!.Id);
        Assert.AreEqual("Frame renderer", runtime.Capability.DisplayName);
        Assert.AreEqual("queued", runtime.Capability.Status);
        Assert.AreEqual(3, runtime.Capability.MaximumParallelism);
        Assert.AreEqual("waitingForRenderer", runtime.Stage);
        Assert.AreEqual("frames", runtime.ProgressScope);
        Assert.AreEqual(25d, runtime.Progress);
        Assert.AreEqual(0, runtime.DownloadCount);
        Assert.AreEqual(1, _reader.Calls, "The existing download snapshot read remains batched.");
        Assert.AreEqual(0, _probe.Calls, "Runtime readers must not probe sources.");
    }

    [TestMethod]
    public async Task ScopedCapabilityReaderHonorsRequestCancellationAndIsDisposedWithItsScope()
    {
        var (task, _) = await Seed([], "test.renderFrames", WorkflowRunStatus.Running);
        using var services = ThirdCapabilityServices();
        Assert.ThrowsExactly<InvalidOperationException>(() => services.GetRequiredService<WorkflowActivityRuntimeRegistry>());
        ThirdCapabilityReader reader;
        await using (var scope = services.CreateAsyncScope())
        {
            reader = scope.ServiceProvider.GetRequiredService<ThirdCapabilityReader>();
            using var cancelled = new CancellationTokenSource();
            var pending = scope.ServiceProvider.GetRequiredService<AcquisitionRuntimeOverviewService>()
                .GetAsync([task.Id], cancelled.Token);
            await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            Assert.IsFalse(reader.Disposed);
            await using var otherScope = services.CreateAsyncScope();
            Assert.AreNotSame(reader, otherScope.ServiceProvider.GetRequiredService<ThirdCapabilityReader>());
            Assert.AreNotSame(scope.ServiceProvider.GetRequiredService<WorkflowActivityRuntimeRegistry>(),
                otherScope.ServiceProvider.GetRequiredService<WorkflowActivityRuntimeRegistry>());
        }
        Assert.IsTrue(reader.Disposed);
    }

    private ServiceProvider ThirdCapabilityServices()
    {
        // Validate the actual workflow/runtime registrations without constructing unrelated subsystems.
        var registrations = new ServiceCollection().AddWorkflow<BakabaseDbContext>();
        IServiceCollection services = new ServiceCollection();
        services.Add(registrations.Single(d => d.ServiceType == typeof(WorkflowActivityRuntimeRegistry)));
        foreach (var descriptor in new ServiceCollection().AddCloudDownloads()
                     .Where(d => d.ServiceType == typeof(IWorkflowActivityRuntimeProvider)))
            services.Add(descriptor);
        services.AddScoped(_ => new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite(_db.Database.GetConnectionString()).Options));
        services.AddScoped<ThirdCapabilityReader>();
        services.AddScoped<IWorkflowActivityRuntimeProvider, ThirdCapability>();
        services.AddSingleton<IAcquisitionRuntimeDownloadReader>(_reader);
        services.AddSingleton(_availability);
        services.AddScoped<AcquisitionRuntimeOverviewService>();
        return services.BuildServiceProvider(new ServiceProviderOptions {ValidateScopes = true, ValidateOnBuild = true});
    }

    private sealed class ThirdCapabilityReader(BakabaseDbContext db) : IDisposable
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed { get; private set; }

        internal async Task ReadAsync(int runId, CancellationToken ct)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            Assert.IsTrue(await db.WorkflowRuns.AsNoTracking().AnyAsync(r => r.Id == runId, ct));
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class ThirdCapability(ThirdCapabilityReader reader) : IWorkflowActivityRuntimeProvider
    {
        public bool CanHandle(string kind) => kind == "test.renderFrames";
        public async ValueTask<WorkflowActivityRuntimeSnapshot> ReadAsync(WorkflowActivityRuntimeContext context,
            CancellationToken ct = default)
        {
            await reader.ReadAsync(context.WorkflowRunId, ct);
            return new()
            {
                Capability = new("renderer", "Frame renderer", Status: "queued", WaitReason: "gpu", ExecutionPolicy: "bounded", MaximumParallelism: 3),
                Stage = "waitingForRenderer", Progress = 25, ProgressScope = "frames"
            };
        }
    }

    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider();
        _db = _services.GetRequiredService<BakabaseDbContext>();
        _root = Path.Combine(Path.GetTempPath(), "runtime-overview-" + Guid.NewGuid().ToString("N"));
        _reader = new(); _probe = new();
        _availability = new(new SourceAvailabilityStore(_root), [_probe]);
        _overview = new(_db, _reader, _availability,
            runtimeProviders: _services.GetRequiredService<WorkflowActivityRuntimeRegistry>());
    }
    [TestCleanup]
    public void Cleanup() { (_services as IDisposable)?.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public async Task ExplicitPreparationCheckUpdatesBothSearchAndOverviewWithoutEditingTheTask()
    {
        var source = new AcquisitionPlanSource {Id = "source", Url = "https://gofile.io/d/fixture", ProviderId = "gofile"};
        var plan = JsonSerializer.Serialize(new AcquisitionPlan {Parts = [new() {Id = "part", SelectedSourceId = source.Id, Sources = [source]}]}, Json);
        var builderState = JsonSerializer.Serialize(new AcquisitionPlanBuilderState {Issues = [new("SOURCE_UNSUPPORTED", "Earlier capability check")]}, Json);
        var task = new AcquisitionTaskDbModel {Title = "Prepared task", BuilderId = "prepared", Status = AcquisitionStatus.Waiting,
            Phase = "attention", PlanRevision = 2, PlanJson = plan, BuilderStateJson = builderState};
        _db.AcquisitionTasks.Add(task);
        await _db.SaveChangesAsync();
        var projection = new AcquisitionSourceAttentionProjection(_availability);
        var search = new AcquisitionTaskSearchService(_db, _services.GetRequiredService<IAcquisitionService>(), projection);
        var overview = new AcquisitionRuntimeOverviewService(_db, _reader, _availability, projection);
        Assert.AreEqual("SOURCE_UNSUPPORTED", (await overview.GetAsync([task.Id])).Single().ProblemCode);
        Assert.AreEqual(0, (await search.SearchAsync(new(AttentionTag: "sourceUnavailable"))).TotalCount);

        _probe.Status = SourceAvailabilityStatuses.ConfirmedUnavailable;
        _probe.ReasonCode = "SOURCE_NOT_FOUND";
        var batches = new SourceAvailabilityBatchService(_db, new SourceAvailabilityStore(_root), _availability, null!, null!);
        var item = (await batches.SnapshotAsync([task.Id], default)).Single();
        Assert.IsTrue(item.IsPreparation);
        await _availability.CheckPreparationAsync(item.Preparation, item.Source, true, default);

        var result = (await overview.GetAsync([task.Id])).Single();
        Assert.AreEqual("SOURCE_NOT_FOUND", result.ProblemCode);
        CollectionAssert.AreEqual(new[] {"sourceUnavailable"}, result.AttentionTags.ToArray());
        var filtered = await search.SearchAsync(new(AttentionTag: "sourceUnavailable"));
        CollectionAssert.AreEqual(new[] {task.Id}, filtered.FilteredTaskIds.ToArray());
        CollectionAssert.AreEqual(new[] {"sourceUnavailable"}, filtered.Items.Single().AttentionTags.ToArray());
        Assert.AreEqual(1, _probe.Calls, "Search and overview must only read cached evidence.");
        await _db.Entry(task).ReloadAsync();
        Assert.AreEqual(plan, task.PlanJson);
        Assert.AreEqual(builderState, task.BuilderStateJson);
        Assert.IsNull(task.WorkflowRunId);
    }

    private static CloudDownloadTarget Target(string id = "part", string? problem = null, int? download = null) => new()
    {
        Id = id, SelectedSourceId = "source", Problem = problem, DownloadTaskId = download,
        Sources = [new() {Id = "source", Url = "https://pan.baidu.com/s/" + id, AccessCode = "code", ProviderId = "baidu"},
            new() {Id = "mirror", Url = "https://example.org/" + id, ProviderId = "http"}]
    };
    private async Task<(AcquisitionTaskDbModel Task, WorkflowRunDbModel Run)> Seed(List<CloudDownloadTarget> targets,
        string kind = "baidu.transfer", WorkflowRunStatus status = WorkflowRunStatus.Waiting)
    {
        var workset = new DownloadWorkset {Targets = targets};
        var run = new WorkflowRunDbModel {StartedAt = At, WaitingSince = At, CurrentStepIndex = 0, Status = status,
            PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Test", TriggerKind = "manual", Nodes = [new() {NodeId = "node", Kind = kind}]}),
            CurrentItemJson = JsonSerializer.Serialize(new {type = "untrusted.invalid.Clr.Type", item = workset}, Json),
            WaitPromptJson = status == WorkflowRunStatus.Waiting ? JsonSerializer.Serialize(workset, Json) : null};
        _db.WorkflowRuns.Add(run); await _db.SaveChangesAsync();
        var task = new AcquisitionTaskDbModel {Title = "Target", WorkflowRunId = run.Id, Status = status == WorkflowRunStatus.Success ? AcquisitionStatus.Completed :
            status == WorkflowRunStatus.Running ? AcquisitionStatus.Running : AcquisitionStatus.Waiting, UpdatedAt = At, CreatedAt = At,
            PlanHistoryJson = "preserved history"};
        _db.AcquisitionTasks.Add(task);
        _db.WorkflowNodeExecutions.Add(new() {WorkflowRunId = run.Id, NodeId = "node", StepIndex = 0, Kind = kind, Attempt = 1,
            Status = status == WorkflowRunStatus.Success ? "succeeded" : "waiting", StartedAt = At.ToUniversalTime(), CompletedAt = At.ToUniversalTime(),
            OutputsJson = JsonSerializer.Serialize(workset.GetWorkflowOutputMetadata(kind), Json)});
        await _db.SaveChangesAsync(); return (task, run);
    }
    private async Task<AcquisitionRuntimeOverview> Read(int id) => (await _overview.GetAsync([id])).Single();

    [TestMethod]
    public async Task ConfirmationRuntimePreservesDownloadHistoryAndDisappearsOnRevisionChangeOrDeletion()
    {
        var (task, run) = await Seed([Target(download: 42)], "download.awaitCompletion");
        _reader.Values = [new("site:42", null, run.Id, "completed", 100, 1200, 1200, At, null)];
        var confirmations = new AcquisitionConfirmationRuntime();
        _overview = new(_db, _reader, _availability, runtimeProviders: _services.GetRequiredService<WorkflowActivityRuntimeRegistry>(),
            confirmations: confirmations);
        var original = await Read(task.Id);
        Assert.AreEqual(1200L, original.TotalBytes);
        Assert.AreEqual(1, original.CompletedDownloadCount);
        using var active = confirmations.Observe(task, FileProcessingActivityRuntimeProvider.ConfirmingExistingFiles, default);
        var observed = await Read(task.Id);
        Assert.AreEqual("confirmation", observed.ProgressScope);
        Assert.AreEqual(original.WorkflowStatus, observed.WorkflowStatus);
        Assert.AreEqual(original.CompletedDownloadCount, observed.CompletedDownloadCount);
        CollectionAssert.AreEqual(original.Sources.ToArray(), observed.Sources.ToArray());
        Assert.IsNull(observed.Progress);
        Assert.IsNull(observed.CompletedBytes);
        Assert.IsNull(observed.TotalBytes);
        Assert.IsNull(observed.SpeedBytesPerSecond);
        Assert.IsFalse(observed.BytesPartial);
        task.PlanRevision++;
        await _db.SaveChangesAsync();
        var next = await Read(task.Id);
        Assert.AreNotEqual("confirmation", next.ProgressScope);
        Assert.AreEqual(task.PlanRevision, next.PlanRevision);
        Assert.AreEqual(original.TotalBytes, next.TotalBytes);
        using var newer = confirmations.Observe(task, FileProcessingActivityRuntimeProvider.ConfirmingExistingFiles, default);
        task.DeletedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        Assert.AreEqual(0, (await _overview.GetAsync([task.Id])).Count);
        Assert.AreEqual(0, _probe.Calls);
    }

    [DataTestMethod]
    [DataRow("live")]
    [DataRow("legacy")]
    [DataRow("no-progress")]
    [DataRow("old-progress")]
    [DataRow("wrong-run")]
    [DataRow("wrong-step")]
    [DataRow("wrong-kind")]
    [DataRow("other-node")]
    [DataRow("other-invocation-kind")]
    [DataRow("ambiguous")]
    [DataRow("finished")]
    [DataRow("superseded")]
    [DataRow("waiting")]
    [DataRow("checkpoint")]
    public async Task RunningInvocationBeforeCheckpointUsesOnlyMatchingWorkflowProgress(string scenario)
    {
        const string kind = "acquisition.plan.continueFiles";
        var (task, run) = await Seed([], kind, scenario == "waiting" ? WorkflowRunStatus.Waiting : WorkflowRunStatus.Running);
        run.CurrentStepIndex = scenario == "checkpoint" ? 1 : null;
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "fixture", TriggerKind = "manual",
            Nodes = [new() {NodeId = "node", Kind = kind}, new() {NodeId = "next", Kind = "fs.publishArtifacts"}]});
        var active = await _db.WorkflowNodeExecutions.SingleAsync();
        active.Status = "running";
        active.CompletedAt = scenario == "finished" ? DateTime.UtcNow : null;
        active.StartedAt = DateTime.UtcNow.AddMinutes(scenario == "old-progress" ? 1 : -1);
        if (scenario == "other-node") active.NodeId = "not-in-this-plan";
        if (scenario == "other-invocation-kind") active.Kind = "fs.publishArtifacts";
        if (scenario == "legacy")
        {
            var definition = new WorkflowDefinitionDbModel {Name = "legacy", TriggerKind = "manual"};
            _db.Add(definition);
            await _db.SaveChangesAsync();
            var activity = new WorkflowActivityDbModel {WorkflowDefinitionId = definition.Id, Order = 0, Kind = kind};
            _db.Add(activity);
            await _db.SaveChangesAsync();
            run.WorkflowDefinitionId = definition.Id;
            run.PlanSnapshotJson = null;
            active.NodeId = "activity:" + activity.Id;
        }
        if (scenario == "ambiguous") _db.WorkflowNodeExecutions.Add(new() {WorkflowRunId = run.Id, NodeId = "next",
            StepIndex = 1, Kind = "fs.publishArtifacts", Attempt = 1, Status = "running", StartedAt = active.StartedAt});
        if (scenario == "superseded") _db.WorkflowNodeExecutions.Add(new() {WorkflowRunId = run.Id, NodeId = active.NodeId,
            StepIndex = 0, Kind = kind, Attempt = 2, Status = "failed", StartedAt = active.StartedAt, CompletedAt = DateTime.UtcNow});
        await _db.SaveChangesAsync();
        var tasks = _services.GetRequiredService<BTaskManager>();
        _overview = new(_db, _reader, _availability, backgroundTasks: tasks,
            runtimeProviders: _services.GetRequiredService<WorkflowActivityRuntimeRegistry>());
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (scenario != "no-progress")
        {
            var id = $"workflow.run.{run.Id}";
            var live = new WorkflowTaskProgress(scenario == "wrong-run" ? run.Id + 1 : run.Id,
                scenario == "wrong-kind" ? "fs.publishArtifacts" : kind, "verifyingFiles", scenario == "wrong-step" ? 1 : 0);
            await tasks.Enqueue(BTaskBuilder.Create(id).Run(async args =>
            {
                await args.UpdateTask(t => t.Data = live);
                ready.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }).WhenStatusChanges((_, t) =>
            {
                if (t.Status == BTaskStatus.Completed) done.TrySetResult();
                return Task.CompletedTask;
            }));
            await tasks.Start(id);
        }
        try
        {
            if (scenario != "no-progress") await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var observed = await Read(task.Id);
            var hasNode = scenario is "live" or "legacy" or "no-progress" or "old-progress" or "wrong-run" or "wrong-step" or "wrong-kind";
            Assert.AreEqual(scenario == "checkpoint" ? 1 : hasNode ? 0 : (int?)null, observed.CurrentStepIndex);
            Assert.AreEqual(scenario == "checkpoint" ? "fs.publishArtifacts" : hasNode ? kind : null, observed.CurrentNodeKind);
            Assert.AreEqual(scenario is "live" or "legacy" ? "verifyingFiles" : null, observed.Stage);
            Assert.AreEqual(scenario is "live" or "legacy" ? "processing" : null, observed.ProgressScope);
            Assert.AreEqual(scenario == "waiting" ? "waiting" : "running", observed.WorkflowStatus);
            Assert.IsNull(observed.Progress);
            if (hasNode) Assert.AreEqual("fileProcessing", observed.Capability!.Id);
            Assert.AreEqual(scenario == "checkpoint" ? 1 : (int?)null,
                (await _db.WorkflowRuns.AsNoTracking().SingleAsync()).CurrentStepIndex);
        }
        finally
        {
            release.TrySetResult();
            if (scenario != "no-progress") await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [TestMethod]
    [DataRow("ready", "/work/delivery", "ready")]
    [DataRow("ready", null, "preparing")]
    [DataRow("preparing", null, "preparing")]
    public async Task Saved_plan_uses_its_list_stage_for_runtime_problems(string phase, string? targetDirectory, string expectedStage)
    {
        var task = new AcquisitionTaskDbModel
        {
            Title = "Prepared task", BuilderId = "test", Phase = phase, Status = AcquisitionStatus.Waiting,
            TargetDirectory = targetDirectory, Error = "BAIDU_SHARE_EXPIRED"
        };
        _db.AcquisitionTasks.Add(task);
        await _db.SaveChangesAsync();

        var overview = await Read(task.Id);
        Assert.IsNull(overview.ProblemCode);
        Assert.AreEqual(0, overview.AttentionTags.Count);
        var search = new AcquisitionTaskSearchService(_db, _services.GetRequiredService<IAcquisitionService>());
        var page = await search.SearchAsync(new(Stage: expectedStage));
        Assert.AreEqual(task.Id, page.Items.Single().Id);
    }

    [TestMethod]
    public async Task UnsupportedSourcePreparationDoesNotBecomeAProcessingOrDownloadFailure()
    {
        var task = new AcquisitionTaskDbModel
        {
            Title = "Unsupported source", BuilderId = "test", Phase = "attention", Status = AcquisitionStatus.Waiting,
            BuilderStateJson = JsonSerializer.Serialize(new
            {
                issues = new[]
                {
                    new {code = "PROCESSING_CONFIRMATION_REQUIRED", message = "Review later", blocking = false},
                    new {code = "SOURCE_UNSUPPORTED", message = "No automatic download adapter", blocking = true}
                }
            }, Json)
        };
        _db.AcquisitionTasks.Add(task); await _db.SaveChangesAsync();
        var result = await Read(task.Id);
        CollectionAssert.AreEqual(new[] {"downloadSourceUnsupported"}, result.AttentionTags.ToArray());
        Assert.AreEqual("SOURCE_UNSUPPORTED", result.ProblemCode);
        Assert.AreEqual(0, result.DownloadCount);
        Assert.AreEqual(0, result.CompletedDownloadCount);
        Assert.AreEqual(0, result.FailedDownloadCount);
        Assert.IsFalse(result.DownloadsCompletedAwaitingWorkflow);
    }

    [TestMethod]
    public async Task RuntimeTagsAndSearchIncludeEveryBlockingPreparationIssue()
    {
        var task = new AcquisitionTaskDbModel
        {
            Title = "Several preparation blockers", BuilderId = "test", Phase = "attention", Status = AcquisitionStatus.Waiting,
            BuilderStateJson = JsonSerializer.Serialize(new
            {
                issues = new[]
                {
                    new {code = "OUTPUT_DIRECTORY_REQUIRED", message = "Choose a directory", blocking = true},
                    new {code = "SOURCE_UNSUPPORTED", message = "No automatic download adapter", blocking = true},
                    new {code = "PROCESSING_CONFIRMATION_REQUIRED", message = "Review later", blocking = false}
                }
            }, Json)
        };
        _db.AcquisitionTasks.Add(task); await _db.SaveChangesAsync();
        var result = await Read(task.Id);
        var search = new AcquisitionTaskSearchService(_db, _services.GetRequiredService<IAcquisitionService>());
        var page = await search.SearchAsync(new(AttentionTag: "downloadSourceUnsupported"));
        Assert.AreEqual(task.Id, page.Items.Single().Id);
        CollectionAssert.AreEqual(new[] {"downloadSourceUnsupported", "configurationRequired"}, result.AttentionTags.ToArray());
        CollectionAssert.AreEqual(page.Items.Single().AttentionTags.ToArray(), result.AttentionTags.ToArray());
        Assert.AreEqual("OUTPUT_DIRECTORY_REQUIRED", result.ProblemCode);
        Assert.AreEqual(0, result.DownloadCount);
        Assert.AreEqual(0, page.FactCounts["downloadedPending"]);
    }

    [TestMethod]
    public async Task CurrentExecutionDoesNotRestoreResolvedPreparationBlockers()
    {
        var (task, _) = await Seed([Target(problem: "BAIDU_SHARE_EXPIRED")]);
        task.BuilderStateJson = "{\"issues\":[{\"code\":\"SOURCE_UNSUPPORTED\",\"blocking\":true},{\"code\":\"OUTPUT_DIRECTORY_REQUIRED\",\"blocking\":true}]}";
        await _db.SaveChangesAsync();
        var result = await Read(task.Id);
        CollectionAssert.AreEqual(new[] {"sourceUnavailable"}, result.AttentionTags.ToArray());
    }

    [TestMethod]
    public async Task AmbiguousShareFailureIsHistoricalAndPreciselySourceBoundWithoutMutationOrProbe()
    {
        var (task, run) = await Seed([Target(problem: "BAIDU_SHARE_UNAVAILABLE: secret account credential"), Target("other")]);
        var before = run.CurrentItemJson;
        var result = await Read(task.Id);
        Assert.AreEqual("BAIDU_SHARE_UNAVAILABLE", result.ProblemCode);
        var source = result.Sources.Single(s => s.PartId == "part" && s.Selected);
        Assert.AreEqual("unknown", source.Status); Assert.AreEqual("workflow", source.EvidenceKind);
        Assert.IsTrue(source.IsHistorical); Assert.AreEqual(new DateTimeOffset(At.ToUniversalTime()), source.ObservedAt);
        Assert.AreEqual("code", source.AccessCode); Assert.IsTrue(source.Url.EndsWith("/part"));
        Assert.IsTrue(result.Sources.Where(s => s != source).All(s => s.ReasonCode == null));
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret account"));
        Assert.AreEqual(0, _probe.Calls); Assert.AreEqual(1, _reader.Calls);
        await _db.Entry(run).ReloadAsync(); Assert.AreEqual(before, run.CurrentItemJson);
        Assert.AreEqual(1, await _db.WorkflowRuns.CountAsync()); Assert.AreEqual("preserved history", task.PlanHistoryJson);
    }

    [DataTestMethod]
    [DataRow("BAIDU_SHARE_CANCELLED", "confirmedUnavailable")]
    [DataRow("BAIDU_SHARE_EXPIRED", "confirmedUnavailable")]
    [DataRow("BAIDU_SHARE_ACCESS_CODE_REJECTED", "accessRequired")]
    [DataRow("PIXELDRAIN_NETWORK_ERROR", "temporarilyUnavailable")]
    [DataRow("GOFILE_SHARE_NOT_FOUND", "unknown")]
    [DataRow("PIXELDRAIN_SHARE_UNAVAILABLE", "unknown")]
    [DataRow("HTTP 404 credentials hidden", "unknown")]
    public async Task OnlyUnambiguousProviderCodesConfirmHistoricalUnavailability(string diagnostic, string status)
    {
        var (task, _) = await Seed([Target(problem: diagnostic)]);
        Assert.AreEqual(status, (await Read(task.Id)).Sources.Single(s => s.Selected).Status);
    }

    [TestMethod]
    public async Task SuccessfulLatestAttemptAndCompletedRunSuppressOlderErrors()
    {
        var (task, run) = await Seed([Target(problem: "BAIDU_SHARE_CANCELLED")]);
        _db.WorkflowNodeExecutions.Add(new() {WorkflowRunId = run.Id, NodeId = "node", StepIndex = 0, Kind = "baidu.transfer", Attempt = 2,
            Status = "succeeded", StartedAt = At.ToUniversalTime().AddMinutes(1), CompletedAt = At.ToUniversalTime().AddMinutes(1)});
        run.Status = WorkflowRunStatus.Running; task.Status = AcquisitionStatus.Running; await _db.SaveChangesAsync();
        var current = await Read(task.Id);
        Assert.IsNull(current.ProblemCode); Assert.IsTrue(current.Sources.All(s => s.ReasonCode == null));
        run.Status = WorkflowRunStatus.Success; task.Status = AcquisitionStatus.Completed; await _db.SaveChangesAsync();
        Assert.IsNull((await Read(task.Id)).ProblemCode);
    }

    [TestMethod]
    public async Task ActualDownloaderProgressWinsAndUnknownBytesStayNull()
    {
        var (task, run) = await Seed([Target(download: 42)], "download.awaitCompletion", WorkflowRunStatus.Running);
        _reader.Values = [new("site:42", null, null, "running", 43.7166, null, null, DateTimeOffset.UtcNow, null, 12.5)];
        var current = await Read(task.Id);
        Assert.AreEqual(43.7166, current.Progress); Assert.AreEqual("download", current.ProgressScope);
        Assert.AreEqual(12.5, current.SpeedBytesPerSecond); Assert.IsNull(current.CompletedBytes); Assert.IsNull(current.TotalBytes);
        Assert.AreEqual(0, current.CurrentStepIndex); Assert.AreEqual(1, current.StepCount);
        CollectionAssert.AreEqual(new[] {42}, _reader.SiteIds!.ToArray());
    }

    [TestMethod]
    public async Task ExplicitNativeOutputBindsEvenWithoutNativeWorkflowOwnershipAndReplacesParentCounter()
    {
        var (task, run) = await Seed([Target(download: 42)], "download.awaitCompletion", WorkflowRunStatus.Running);
        var node = await _db.WorkflowNodeExecutions.SingleAsync();
        node.OutputsJson = JsonSerializer.Serialize(new[] {new WorkflowOutputBlock {Kind = "task", Key = "part:downloadTask", TaskKind = "download", TaskId = "bt:exact"}}, Json);
        await _db.SaveChangesAsync();
        _reader.Values = [new("site:42", null, null, "completed", 100, 5, 5, DateTimeOffset.UtcNow, null),
            new("bt:exact", "site:42", null, "running", 25, 25, 100, DateTimeOffset.UtcNow, null),
            new("bt:unrelated", null, null, "failed", 0, 20, 100, DateTimeOffset.UtcNow, "BAIDU_SHARE_CANCELLED")];
        var current = await Read(task.Id);
        Assert.AreEqual(1, current.DownloadCount); Assert.AreEqual(25L, current.CompletedBytes); Assert.AreEqual(100L, current.TotalBytes);
        Assert.AreEqual(25d, current.Progress); Assert.AreEqual(0, current.FailedDownloadCount);
        CollectionAssert.Contains(_reader.SiteIds!.ToArray(), 42);
    }

    [TestMethod]
    public async Task PartialCountersNeverPretendToBeCompleteTotalsOrInventAverageProgress()
    {
        var (task, _) = await Seed([Target(download: 42), Target("other", download: 43)], "download.awaitCompletion", WorkflowRunStatus.Running);
        _reader.Values = [new("site:42", null, null, "running", 50, 50, 100, DateTimeOffset.UtcNow, null),
            new("site:43", null, null, "running", 75, null, null, DateTimeOffset.UtcNow, null)];
        var current = await Read(task.Id);
        Assert.IsTrue(current.BytesPartial); Assert.IsNull(current.CompletedBytes); Assert.IsNull(current.TotalBytes); Assert.IsNull(current.Progress);
    }

    [TestMethod]
    public async Task Parallel_downloads_show_combined_speed_but_finish_when_the_slowest_leaf_finishes()
    {
        var (task, _) = await Seed([Target(download: 42), Target("extra", download: 43), Target("done", download: 44)],
            "download.awaitCompletion", WorkflowRunStatus.Running);
        _reader.Values = [
            new("site:42", null, null, "running", 50, 50, 100, At, null, 10),
            new("site:43", null, null, "running", 50, 50, 100, At, null, 3, "Downloading"),
            new("site:44", null, null, "completed", 100, null, null, At, null, 999)];
        var result = await Read(task.Id);
        Assert.AreEqual(13d, result.SpeedBytesPerSecond);
        Assert.AreEqual(17L, result.EstimatedRemainingSeconds);
        Assert.AreEqual(2, result.ActiveDownloadCount);
    }

    [DataTestMethod]
    [DataRow("queued", null, 4d, 50L, 100L)]
    [DataRow("stopping", null, 4d, 50L, 100L)]
    [DataRow("failed", null, 4d, 50L, 100L)]
    [DataRow("running", "Checking", 4d, 50L, 100L)]
    [DataRow("running", "Reusing", 4d, 50L, 100L)]
    [DataRow("running", null, 0d, 50L, 100L)]
    [DataRow("running", null, 4d, null, 100L)]
    [DataRow("running", null, 4d, 50L, null)]
    [DataRow("running", null, 4d, 100L, 100L)]
    [DataRow("running", null, 4d, 120L, 100L)]
    [DataRow("running", null, double.PositiveInfinity, 0L, 100L)]
    [DataRow("running", null, double.Epsilon, 0L, 100L)]
    public async Task Each_unfinished_leaf_requires_trustworthy_transfer_counters_for_eta(string status, string? stage,
        double rate, long? done, long? total)
    {
        var (task, _) = await Seed([Target(download: 42), Target("other", download: 43)], "download.awaitCompletion", WorkflowRunStatus.Running);
        _reader.Values = [new("site:42", null, null, "running", 50, 50, 100, At, null, 10),
            new("site:43", null, null, status, null, done, total, At, null, rate, stage)];
        var result = await Read(task.Id);
        Assert.IsNull(result.EstimatedRemainingSeconds);
        if (status != "running" || stage != null) Assert.AreEqual(10d, result.SpeedBytesPerSecond);
        if (!double.IsFinite(rate)) Assert.IsNull(result.SpeedBytesPerSecond);
    }

    [TestMethod]
    public async Task Missing_native_group_and_overflowing_byte_totals_do_not_break_the_other_download()
    {
        var (task, _) = await Seed([Target(download: 42), Target("other", download: 43)], "download.awaitCompletion", WorkflowRunStatus.Running);
        _reader.Values = [new("site:42", null, null, "running", 50, long.MaxValue - 10, long.MaxValue, At, null, 1)];
        Assert.IsNull((await Read(task.Id)).EstimatedRemainingSeconds);
        _reader.Values = [.._reader.Values, new("site:43", null, null, "running", 75, 75, 100, At, null, 1)];
        var result = await Read(task.Id);
        Assert.IsNull(result.TotalBytes);
        Assert.IsNull(result.CompletedBytes);
        Assert.IsTrue(result.BytesPartial);
        Assert.AreEqual(25L, result.EstimatedRemainingSeconds);
    }

    [DataTestMethod]
    [DataRow("VerifyingSource")]
    [DataRow("VerifyingLocalFiles")]
    public async Task VerifyingExistingFilesShowsTheActiveStageWhileAnotherPartStillNeedsAttention(string stage)
    {
        var first = Target("first") with {DownloadTaskId = 11};
        var second = Target("second") with {DownloadTaskId = 12, Problem = "BAIDU_SHARE_EXPIRED"};
        var (task, run) = await Seed([first, second], "download.awaitCompletion", WorkflowRunStatus.Waiting);
        _reader.Values = [new("site:11", null, run.Id, "running", null, null, null, DateTimeOffset.UtcNow, null, Stage: stage),
            new("site:12", null, run.Id, "failed", 100, 50, 50, DateTimeOffset.UtcNow, "BAIDU_SHARE_EXPIRED")];
        var row = await Read(task.Id);
        Assert.AreEqual("waiting", row.WorkflowStatus);
        Assert.AreEqual("running", row.Capability?.Status);
        Assert.AreEqual(stage, row.Stage);
        Assert.IsNull(row.Progress);
        Assert.IsNull(row.SpeedBytesPerSecond);
        Assert.IsNull(row.EstimatedRemainingSeconds);
        Assert.AreEqual(1, row.ActiveDownloadCount);
        Assert.AreEqual(1, row.FailedDownloadCount);
        Assert.AreEqual("BAIDU_SHARE_EXPIRED", row.ProblemCode);
    }

    [TestMethod]
    public async Task NativeOutputWithoutCheckpointTaskIdStillBindsAndVerifyingHidesObsoletePercentage()
    {
        var (task, _) = await Seed([Target()], "download.awaitCompletion", WorkflowRunStatus.Running);
        var node = await _db.WorkflowNodeExecutions.SingleAsync();
        node.OutputsJson = JsonSerializer.Serialize(new[] {new WorkflowOutputBlock {Kind = "task", Key = "part:downloadTask", TaskKind = "download", TaskId = "bt:exact"}}, Json);
        await _db.SaveChangesAsync();
        _reader.Values = [new("bt:exact", null, null, "running", 67, null, null, DateTimeOffset.UtcNow, null, Stage: "Verifying")];
        var current = await Read(task.Id);
        Assert.AreEqual(1, current.DownloadCount); Assert.AreEqual("Verifying", current.Stage);
        Assert.IsNull(current.Progress); CollectionAssert.Contains(_reader.TransportIds!.ToArray(), "bt:exact");
    }

    [TestMethod]
    public async Task NewCheckpointDownloadDoesNotAccumulateAnOlderNodeBindingForTheSameTarget()
    {
        var (task, _) = await Seed([Target(download: 43)], "download.awaitCompletion", WorkflowRunStatus.Running);
        var node = await _db.WorkflowNodeExecutions.SingleAsync();
        node.OutputsJson = JsonSerializer.Serialize(new[] {new WorkflowOutputBlock {Kind = "task", Key = "part:downloadTask", TaskKind = "download", TaskId = "site:42"}}, Json);
        await _db.SaveChangesAsync();
        _reader.Values = [new("site:42", null, null, "failed", 5, 5, 100, DateTimeOffset.UtcNow, "BAIDU_SHARE_CANCELLED"),
            new("site:43", null, null, "running", 67, 67, 100, DateTimeOffset.UtcNow, null)];
        var current = await Read(task.Id);
        Assert.AreEqual(1, current.DownloadCount); Assert.AreEqual(67d, current.Progress); Assert.AreEqual(0, current.FailedDownloadCount);
        CollectionAssert.AreEqual(new[] {43}, _reader.SiteIds!.ToArray());
    }

    [TestMethod]
    public async Task SuccessfulSelectedSourceDoesNotEraseUnselectedMirrorsIndependentEvidence()
    {
        var target = Target() with {DownloadArtifactId = "already-downloaded"};
        _probe.Status = "confirmedUnavailable"; await _availability.CheckAsync(target.Sources[1], true, default);
        var (task, _) = await Seed([target]);
        var result = await Read(task.Id);
        Assert.IsNull(result.Sources.First(s => s.Selected).ReasonCode);
        Assert.AreEqual("confirmedUnavailable", result.Sources.Single(s => !s.Selected).Status);
    }

    [TestMethod]
    public async Task NewUnknownProbeDoesNotEraseClearHistoricalCancellationButNewAvailableCan()
    {
        var target = Target(problem: "BAIDU_SHARE_CANCELLED");
        var (task, _) = await Seed([target]);
        _probe.Status = "unknown"; await _availability.CheckAsync(target.SelectedSource!, true, default);
        var result = await Read(task.Id);
        Assert.AreEqual("confirmedUnavailable", result.Sources.First().Status); Assert.AreEqual("workflow", result.Sources.First().EvidenceKind);
        _probe.Status = "available"; await _availability.CheckAsync(target.SelectedSource!, true, default);
        Assert.AreEqual("available", (await Read(task.Id)).Sources.First().Status);
        Assert.AreEqual(2, _probe.Calls, "Overview reads never start another probe.");
    }

    [DataTestMethod]
    [DataRow("TERABOX_DOWNLOAD_IDENTITY_REJECTED", "accessRequired", false)]
    [DataRow("TERABOX_DOWNLOAD_IDENTITY_REJECTED", "accessRequired", true)]
    [DataRow("TERABOX_DOWNLOAD_ACCESS_DENIED", "accessRequired", false)]
    [DataRow("TERABOX_DOWNLOAD_ACCESS_DENIED", "accessRequired", true)]
    [DataRow("PIKPAK_ORIGINAL_FILE_UNAVAILABLE", "unknown", false)]
    [DataRow("PIKPAK_ORIGINAL_FILE_UNAVAILABLE", "unknown", true)]
    public async Task AvailableMetadataCannotReplaceOriginalDownloadFailure(string code, string status, bool fromDownload)
    {
        var target = Target(problem: fromDownload ? null : code, download: 42);
        var selected = target.SelectedSource! with
        {
            ProviderId = code.StartsWith("TERABOX_", StringComparison.Ordinal) ? "terabox" : "pikpak",
            Url = code.StartsWith("TERABOX_", StringComparison.Ordinal)
                ? "https://www.terabox.com/s/1fixture" : "https://mypikpak.com/s/fixture"
        };
        target = target with {Sources = [selected, target.Sources[1]]};
        var (task, run) = await Seed([target], "download.awaitCompletion");
        var executionAt = new DateTimeOffset(At.ToUniversalTime().AddMinutes(fromDownload ? 1 : 0));
        if (fromDownload)
            _reader.Values = [new("site:42", null, run.Id, "failed", 0, null, null, executionAt, code)];
        _probe.Status = "available";
        var metadata = await _availability.CheckAsync(selected, true, default);
        await _availability.CheckAsync(target.Sources[1], true, default);
        var savedCheckpoint = run.CurrentItemJson;

        var result = await Read(task.Id);
        var source = result.Sources.Single(s => s.Selected);
        Assert.AreEqual(code, result.ProblemCode);
        Assert.AreEqual(code, source.ReasonCode);
        Assert.AreEqual(status, source.Status);
        Assert.AreEqual(fromDownload ? "download" : "workflow", source.EvidenceKind);
        Assert.AreEqual(executionAt, source.ObservedAt);
        Assert.IsTrue(source.IsHistorical);
        Assert.IsNull(source.ExpiresAt, "A later metadata TTL must not be attached to execution evidence.");
        Assert.AreEqual("available", result.Sources.Single(s => !s.Selected).Status);
        Assert.AreEqual(JsonSerializer.Serialize(metadata, Json),
            JsonSerializer.Serialize(await _availability.GetAsync(selected, default), Json),
            "The independent successful metadata observation must remain intact.");
        Assert.AreEqual(2, _probe.Calls, "Reading execution evidence must not probe or retry a download.");
        await _db.Entry(run).ReloadAsync();
        Assert.AreEqual(savedCheckpoint, run.CurrentItemJson);
    }

    [TestMethod]
    public async Task AvailableMetadataStillRefreshesAConfirmedDownloadSourceExpiry()
    {
        var target = Target(download: 42);
        var (task, run) = await Seed([target], "download.awaitCompletion");
        _reader.Values = [new("site:42", null, run.Id, "failed", 0, null, null,
            new DateTimeOffset(At.ToUniversalTime().AddMinutes(1)), "BAIDU_SHARE_EXPIRED")];
        _probe.Status = "available";
        var metadata = await _availability.CheckAsync(target.SelectedSource!, true, default);

        var result = await Read(task.Id);
        var source = result.Sources.Single(s => s.Selected);
        Assert.AreEqual("available", source.Status);
        Assert.AreEqual(metadata.ReasonCode, source.ReasonCode);
        Assert.AreEqual("probe", source.EvidenceKind);
        Assert.AreEqual(metadata.CheckedAt, source.ObservedAt);
        Assert.AreEqual(metadata.ExpiresAt, source.ExpiresAt);
        Assert.IsFalse(source.IsHistorical);
        Assert.AreEqual(1, _probe.Calls);
    }

    [TestMethod]
    public async Task NewerSourceCacheCannotEraseTheCurrentExecutionBlocker()
    {
        var target = Target(problem: "PIXELDRAIN_NETWORK_ERROR");
        var (task, _) = await Seed([target]);
        _probe.Status = "unknown"; await _availability.CheckAsync(target.SelectedSource!, true, default);
        var result = await Read(task.Id);
        Assert.AreEqual("probe", result.Sources.First().EvidenceKind);
        Assert.AreEqual("PIXELDRAIN_NETWORK_ERROR", result.ProblemCode);
        Assert.AreEqual("SOURCE_NETWORK_ERROR", result.ProblemReasonCode);
        CollectionAssert.Contains(result.AttentionTags.ToArray(), "networkIssue");
    }

    [TestMethod]
    public async Task SuccessfulTargetSuppressesOldCachedFailureWhileOtherTargetStillWaits()
    {
        var target = Target() with {DownloadArtifactId = "already-downloaded"};
        _probe.Status = "confirmedUnavailable"; await _availability.CheckAsync(target.SelectedSource!, true, default);
        var (task, _) = await Seed([target, Target("other", problem: "BAIDU_SHARE_UNAVAILABLE")]);
        Assert.IsNull((await Read(task.Id)).Sources.First().ReasonCode);
    }

    [TestMethod]
    public async Task BatchesReadDownloadSnapshotsOnceAndRejectUnboundedInput()
    {
        var (first, _) = await Seed([Target(download: 42)]);
        var (second, _) = await Seed([Target("other", download: 43)]);
        Assert.AreEqual(2, (await _overview.GetAsync([second.Id, first.Id, first.Id])).Count);
        Assert.AreEqual(1, _reader.Calls);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => _overview.GetAsync(Enumerable.Range(1, 101).ToArray()));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => _overview.GetAsync([-1]));
    }

    [TestMethod]
    public async Task ReadingNativeSnapshotsCannotInitializeStorageOrResumeQueues()
    {
        await using var bt = new BtDownloadManager(null!, () => throw new AssertFailedException("A read initialized BT storage."));
        await using var http = new HttpDownloadManager(null!, () => throw new AssertFailedException("A read initialized HTTP storage."));
        Assert.AreEqual(0, (await bt.ReadSnapshotAsync()).Count);
        Assert.AreEqual(0, (await http.ReadSnapshotAsync()).Count);
    }

    [DataTestMethod]
    [DataRow("downloading", "Downloading")]
    [DataRow("reusing", "Reusing")]
    [DataRow("discovering", "Preparing")]
    [DataRow("Downloading original files with rclone", "Downloading")]
    [DataRow("Private /some/path?password=secret", null)]
    public void StageProjectionIsCanonicalAndNeverExposesArbitraryDiagnostics(string raw, string? expected) =>
        Assert.AreEqual(expected, AcquisitionRuntimeDownloadReader.SafeStage(raw));

    [TestMethod]
    public async Task CompletedDownloadSupersedesStaleWaitErrorWithoutAdvancingCheckpoint()
    {
        var (task, run) = await Seed([Target(problem: "BAIDU_OUTPUT_DIRECTORY_FAILED", download: 42)], "download.awaitCompletion");
        task.BuilderStateJson = AcquisitionDownloadContinuationService.WithState(null,
            new(run.Id, "downloadsOnly", DateTimeOffset.UtcNow));
        await _db.SaveChangesAsync();
        _reader.Values = [new("site:42", null, run.Id, "completed", 100, 100, 100, DateTimeOffset.UtcNow, null)];
        var checkpoint = run.CurrentItemJson;
        var result = await Read(task.Id);
        Assert.IsTrue(result.DownloadsCompletedAwaitingWorkflow); Assert.IsTrue(result.CanDiagnoseDownloadContinuation);
        Assert.IsTrue(result.CanStopWaitingRun);
        Assert.AreEqual("downloadsOnly", result.DownloadContinuation); Assert.IsNull(result.ProblemCode);
        Assert.AreEqual(0, result.AttentionTags.Count); Assert.AreEqual(1, result.CompletedDownloadCount);
        var selected = result.Sources.Single(s => s.Selected);
        Assert.IsNull(selected.ReasonCode); Assert.AreEqual("download", selected.EvidenceKind); Assert.AreEqual("available", selected.Status);
        await _db.Entry(run).ReloadAsync(); Assert.AreEqual(checkpoint, run.CurrentItemJson);
        Assert.AreEqual(WorkflowRunStatus.Waiting, run.Status); Assert.IsNull(run.PendingSignalJson); Assert.AreEqual(0, _probe.Calls);
        run.PendingSignalJson = "{}";
        await _db.SaveChangesAsync();
        Assert.IsFalse((await Read(task.Id)).CanStopWaitingRun);
    }

    [DataTestMethod]
    [DataRow("queued")]
    [DataRow("running")]
    public async Task HealthyCurrentActivityClearsOnlyItsOldErrorWhileCurrentStorageHoldRemains(string status)
    {
        var (task, run) = await Seed([Target(problem: "LOCAL_STORAGE_FULL", download: 42)], "download.awaitCompletion");
        _reader.Values = [new("site:42", null, run.Id, status, 20, 20, 100, DateTimeOffset.UtcNow, null)];
        var result = await Read(task.Id);
        Assert.AreEqual(status, result.DownloadActivity); Assert.AreEqual("waiting", result.WorkflowStatus);
        Assert.IsFalse(result.CanStopWaitingRun);
        Assert.IsNull(result.ProblemCode); Assert.IsNull(result.Sources.Single(s => s.Selected).ReasonCode);
        _reader.Values = [_reader.Values.Single() with {ProblemCode = "LOCAL_STORAGE_RESERVE_LOW"}];
        var held = await Read(task.Id);
        Assert.AreEqual(status, held.DownloadActivity); Assert.AreEqual("LOCAL_STORAGE_RESERVE_LOW", held.ProblemCode);
        Assert.IsTrue(held.AttentionTags.Contains("localStorage"));
    }

    [TestMethod]
    public async Task ExcludedFailedTargetDoesNotPolluteCurrentActiveDownloadAttention()
    {
        var excluded = Target("excluded", problem: "BAIDU_SHARE_EXPIRED", download: 43) with {Excluded = true};
        var (task, run) = await Seed([Target(download: 42), excluded], "download.awaitCompletion");
        _reader.Values = [new("site:42", null, run.Id, "running", 20, 20, 100, DateTimeOffset.UtcNow, null),
            new("site:43", null, run.Id, "failed", 0, 0, 100, DateTimeOffset.UtcNow, "BAIDU_SHARE_EXPIRED")];
        var result = await Read(task.Id);
        Assert.AreEqual("running", result.DownloadActivity); Assert.AreEqual(1, result.DownloadCount);
        Assert.AreEqual(0, result.FailedDownloadCount); Assert.IsNull(result.ProblemCode);
        Assert.AreEqual(0, result.AttentionTags.Count);
    }

    [TestMethod]
    public async Task CompletionOfOneTargetCannotHideUnfinishedSiblingOrAuthorizeLegacyRun()
    {
        var (task, run) = await Seed([Target(problem: "BAIDU_OUTPUT_DIRECTORY_FAILED", download: 42),
            Target("other", "PIKPAK_ORIGINAL_FILE_UNAVAILABLE", 43)], "download.awaitCompletion");
        _reader.Values = [new("site:42", null, run.Id, "completed", 100, 100, 100, DateTimeOffset.UtcNow, null),
            new("site:43", null, run.Id, "failed", 0, 0, 100, DateTimeOffset.UtcNow, "PIKPAK_ORIGINAL_FILE_UNAVAILABLE")];
        var result = await Read(task.Id);
        Assert.IsFalse(result.DownloadsCompletedAwaitingWorkflow); Assert.IsFalse(result.CanDiagnoseDownloadContinuation);
        Assert.AreEqual("manual", result.DownloadContinuation); Assert.AreEqual("PIKPAK_ORIGINAL_FILE_UNAVAILABLE", result.ProblemCode);
        Assert.IsNull(result.Sources.Single(s => s.PartId == "part" && s.Selected).ReasonCode);
    }

    [TestMethod]
    public async Task CompletedDownloadsDoNotHideTypedProcessingPathConflict()
    {
        var (task, run) = await Seed([Target(download: 42)], "acquisition.plan.process");
        run.WaitPromptJson = "{\"reason\":\"ProcessingPathConflict\"}";
        await _db.SaveChangesAsync();
        _reader.Values = [new("site:42", null, run.Id, "completed", 100, 100, 100, DateTimeOffset.UtcNow, null)];
        var result = await Read(task.Id);
        Assert.AreEqual("ProcessingPathConflict", result.ProblemCode);
        Assert.IsFalse(result.DownloadsCompletedAwaitingWorkflow); Assert.IsFalse(result.CanDiagnoseDownloadContinuation);
    }

    [DataTestMethod]
    [DataRow("StorageUnavailable", "LOCAL_STORAGE_RESERVE_LOW", "LOCAL_STORAGE_RESERVE_LOW")]
    [DataRow("StorageUnavailable", "LOCAL_STORAGE_ACCESS_DENIED", "LOCAL_STORAGE_ACCESS_DENIED")]
    [DataRow("StorageUnavailable", "BAIDU_SHARE_EXPIRED", "LOCAL_STORAGE_UNAVAILABLE")]
    [DataRow("DiskSpaceLimit", "No typed OS error", "LOCAL_STORAGE_FULL")]
    public async Task CurrentTypedProcessingStorageWaitWinsOverOldFailureWithoutExposingMessage(string reason,
        string message, string expected)
    {
        var (task, run) = await Seed([Target(download: 42)], AcquisitionPlanProcessingActivity.ActivityKind);
        run.WaitReason = "cloud.fileProcessing";
        run.ErrorMessage = task.Error = "BAIDU_SHARE_EXPIRED: an earlier attempt";
        run.WaitPromptJson = JsonSerializer.Serialize(new {reason, message = message + ": private-path SECRET_VALUE"}, Json);
        await _db.SaveChangesAsync();
        _reader.Values = [new("site:42", null, run.Id, "completed", 100, 100, 100, DateTimeOffset.UtcNow, null)];
        var result = await Read(task.Id);
        Assert.AreEqual(expected, result.ProblemCode); Assert.AreEqual(expected, result.ProblemReasonCode);
        CollectionAssert.AreEqual(new[] {"localStorage", "fileProcessing"}, result.AttentionTags.ToArray());
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("SECRET_VALUE"));
        Assert.IsFalse(result.DownloadsCompletedAwaitingWorkflow);
    }

    [DataTestMethod]
    [DataRow("baidu.transfer", "waiting", WorkflowRunStatus.Waiting)]
    [DataRow("acquisition.plan.process", "succeeded", WorkflowRunStatus.Waiting)]
    [DataRow("acquisition.plan.process", "running", WorkflowRunStatus.Running)]
    public async Task StaleProcessingStoragePromptCannotOverrideCurrentCursor(string kind, string nodeStatus, WorkflowRunStatus status)
    {
        var (task, run) = await Seed([Target()], kind, status);
        run.WaitPromptJson = "{\"reason\":\"StorageUnavailable\",\"message\":\"LOCAL_STORAGE_RESERVE_LOW\"}";
        (await _db.WorkflowNodeExecutions.SingleAsync()).Status = nodeStatus;
        await _db.SaveChangesAsync();
        var result = await Read(task.Id);
        Assert.AreNotEqual("LOCAL_STORAGE_RESERVE_LOW", result.ProblemCode);
        Assert.IsFalse(result.AttentionTags.Contains("localStorage"));
    }

    [TestMethod]
    public async Task EarlierDownloadCompletionCannotEraseANewerArtifactVerificationFailure()
    {
        var (task, run) = await Seed([Target(problem: "LOCAL_STORAGE_UNAVAILABLE", download: 42)], "download.awaitCompletion");
        _reader.Values = [new("site:42", null, run.Id, "completed", 100, 100, 100,
            new DateTimeOffset(At.ToUniversalTime().AddMinutes(-1)), null)];
        var result = await Read(task.Id);
        Assert.IsTrue(result.DownloadsCompletedAwaitingWorkflow);
        Assert.AreEqual("LOCAL_STORAGE_UNAVAILABLE", result.ProblemCode);
    }

    [DataTestMethod]
    [DataRow("acquisition.plan.process", "PasswordUnknown")]
    [DataRow("fs.assessDelivery", "PasswordUnknown")]
    [DataRow("fs.assessDelivery", "ProcessingInputNotMatched")]
    [DataRow("acquisition.plan.process", "ArchiveInputRequired")]
    public async Task CurrentPasswordWaitIsVisibleWithoutReadingExecutionHistory(string kind, string reason)
    {
        var target = Target(download: 42) with {DownloadArtifactId = "downloaded"};
        var (task, run) = await Seed([target], kind);
        run.WaitReason = "cloud.fileProcessing";
        run.WaitPromptJson = JsonSerializer.Serialize(new {reason, message = "SECRET_PASSWORD"});
        await _db.SaveChangesAsync();
        _reader.Values = [new("site:42", null, run.Id, "completed", 100, 100, 100, DateTimeOffset.UtcNow, null)];
        var result = await Read(task.Id);
        Assert.AreEqual(reason, result.ProblemCode);
        CollectionAssert.Contains(result.AttentionTags.ToArray(), "fileProcessing");
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("SECRET_PASSWORD"));
    }

    [DataTestMethod]
    [DataRow(WorkflowRunStatus.Running)]
    [DataRow(WorkflowRunStatus.Pending)]
    public async Task Active_processing_retry_does_not_display_its_previous_directory_failure(WorkflowRunStatus status)
    {
        var (task, run) = await Seed([Target() with {DownloadArtifactId = "already-downloaded"}],
            "acquisition.plan.process", status);
        task.Status = AcquisitionStatus.Failed;
        task.Error = run.ErrorMessage = "STAGE_COMMIT_REFERENCED";
        await _db.SaveChangesAsync();
        var current = await Read(task.Id);
        Assert.AreEqual(status.ToString().ToLowerInvariant(), current.WorkflowStatus);
        Assert.IsNull(current.ProblemCode);
        CollectionAssert.DoesNotContain(current.AttentionTags.ToArray(), "fileProcessing");
    }

    [TestMethod]
    public async Task CurrentFailedInvocationWinsOverAnEarlierPasswordWait()
    {
        var (task, run) = await Seed([Target(problem: "PasswordUnknown")], "fs.assessDelivery", WorkflowRunStatus.Failed);
        run.ErrorMessage = "PLAN_CURRENT_DIRECTORY_CHANGED: SECRET_PATH";
        run.WaitPromptJson = "{\"reason\":\"PasswordUnknown\"}";
        (await _db.WorkflowNodeExecutions.SingleAsync()).Status = "failed";
        await _db.SaveChangesAsync();
        var result = await Read(task.Id);
        Assert.AreEqual("PLAN_CURRENT_DIRECTORY_CHANGED", result.ProblemCode);
        CollectionAssert.Contains(result.AttentionTags.ToArray(), "fileProcessing");
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("SECRET_PATH"));
    }

    [DataTestMethod]
    [DataRow("acquisition.plan.process", "LOCAL_STORAGE_FULL")]
    [DataRow("fs.assessDelivery", "LOCAL_STORAGE_RESERVE_LOW")]
    [DataRow("fs.publishArtifacts", "LOCAL_STORAGE_FULL")]
    [DataRow("download.awaitCompletion", "LOCAL_STORAGE_RESERVE_LOW")]
    public async Task FailedStorageInvocationProjectsItsExplicitCauseWithoutReadingHistory(string kind, string code)
    {
        var (task, run) = await Seed([Target(problem: "PasswordUnknown", download: 42)], kind, WorkflowRunStatus.Failed);
        run.ErrorMessage = "An I/O write failed at SECRET_PATH";
        var node = await _db.WorkflowNodeExecutions.SingleAsync(); node.Status = "failed";
        node.OutputsJson = JsonSerializer.Serialize(new[] {
            new WorkflowOutputBlock {Kind = "warning", Key = "part:processing", Code = "PasswordUnknown"},
            new WorkflowOutputBlock {Kind = "warning", Key = "activity-failed", Code = code, Severity = "error"}
        }, Json);
        await _db.SaveChangesAsync();
        var result = await Read(task.Id);
        Assert.AreEqual(code, result.ProblemCode); Assert.AreEqual(code, result.ProblemReasonCode);
        CollectionAssert.AreEqual(new[] {"localStorage"}, result.AttentionTags.ToArray());
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("SECRET_PATH"));
    }

    [DataTestMethod]
    [DataRow("A different current failure at SECRET_PATH", "SOURCE_ATTENTION_REQUIRED")]
    [DataRow("A remote response mentioned LOCAL_STORAGE_FULL without an OS cause.", "SOURCE_ATTENTION_REQUIRED")]
    [DataRow("PLAN_CURRENT_DIRECTORY_CHANGED", "PLAN_CURRENT_DIRECTORY_CHANGED")]
    [DataRow("System.InvalidOperationException: STAGE_COMMIT_REFERENCED", "STAGE_COMMIT_REFERENCED")]
    public async Task FailedInvocationCannotInheritThePreviousStorageWait(string error, string expected)
    {
        var (task, run) = await Seed([Target(problem: "LOCAL_STORAGE_FULL")], "acquisition.plan.process", WorkflowRunStatus.Failed);
        run.ErrorMessage = error;
        run.WaitPromptJson = "{\"reason\":\"StorageUnavailable\",\"message\":\"LOCAL_STORAGE_RESERVE_LOW\"}";
        var node = await _db.WorkflowNodeExecutions.SingleAsync(); node.Status = "failed";
        node.OutputsJson = JsonSerializer.Serialize(new[] {
            new WorkflowOutputBlock {Kind = "warning", Key = "part:processing", Code = "StorageUnavailable"},
            new WorkflowOutputBlock {Kind = "warning", Key = "activity-failed", Code = "WORKFLOW_ACTIVITY_FAILED", Severity = "error"}
        }, Json);
        await _db.SaveChangesAsync();
        var result = await Read(task.Id);
        Assert.AreEqual(expected, result.ProblemCode);
        CollectionAssert.DoesNotContain(result.AttentionTags.ToArray(), "localStorage");
        Assert.IsTrue(result.Sources.All(s => s.ReasonCode == null));
    }

    [TestMethod]
    public async Task PublicationStorageWaitProjectsItsPreciseReserveCause()
    {
        var (task, run) = await Seed([Target() with {ProcessedArtifactId = "processed"}], "fs.publishArtifacts");
        run.WaitReason = "artifacts.publication";
        run.WaitPromptJson = "{\"targetId\":\"part\",\"reason\":\"StorageUnavailable\",\"message\":\"LOCAL_STORAGE_RESERVE_LOW\"}";
        await _db.SaveChangesAsync();
        var result = await Read(task.Id);
        Assert.AreEqual("LOCAL_STORAGE_RESERVE_LOW", result.ProblemCode);
        CollectionAssert.AreEqual(new[] {"localStorage", "fileProcessing"}, result.AttentionTags.ToArray());
    }

    [TestMethod]
    public async Task UnknownProcessingPromptCannotExposeFreeTextOrInventAReason()
    {
        var (task, run) = await Seed([Target()], "fs.assessDelivery");
        run.WaitPromptJson = "{\"reason\":\"SECRET_VALUE\",\"message\":\"PasswordUnknown\"}";
        await _db.SaveChangesAsync();
        var result = await Read(task.Id);
        Assert.AreEqual("SOURCE_ATTENTION_REQUIRED", result.ProblemCode);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("SECRET_VALUE"));
    }

    [DataTestMethod]
    [DataRow("LLM_DECISION_INVALID_JSON", "LLM_DECISION_INVALID_JSON")]
    [DataRow("SECRET_VALUE", "FILE_DELIVERY_RESULT_UNCERTAIN")]
    public async Task AssessmentSummaryUsesOnlyKnownCurrentDiagnostic(string diagnostic, string expected)
    {
        var (task, run) = await Seed([Target() with {DownloadArtifactId = "downloaded"}], "fs.assessDelivery");
        run.WaitPromptJson = "{\"reason\":\"FILE_DELIVERY_RESULT_UNCERTAIN\"}";
        (await _db.WorkflowNodeExecutions.SingleAsync()).OutputsJson = JsonSerializer.Serialize(new[] {
            new WorkflowOutputBlock {Kind = "warning", Key = "adaptive-assessment", Code = diagnostic}
        }, Json);
        await _db.SaveChangesAsync();
        var result = await Read(task.Id);
        Assert.AreEqual(expected, result.ProblemCode);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("SECRET_VALUE"));
    }

    private sealed class Reader : IAcquisitionRuntimeDownloadReader
    {
        public int Calls; public IReadOnlyCollection<int>? SiteIds; public IReadOnlyCollection<string>? TransportIds;
        public IReadOnlyList<AcquisitionRuntimeDownload> Values = [];
        public Task<IReadOnlyList<AcquisitionRuntimeDownload>> ReadAsync(IReadOnlyCollection<int> siteIds, IReadOnlyCollection<string> transportIds,
            IReadOnlyCollection<int> runIds, CancellationToken ct)
        { Calls++; SiteIds = siteIds; TransportIds = transportIds; return Task.FromResult(Values); }
    }
    private sealed class Probe : ISourceAvailabilityProbe
    {
        public int Calls; public string Status = "unknown"; public string ReasonCode = "TEST_OBSERVATION";
        public bool CanHandle(CloudSource source) => true;
        public Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct)
        { Calls++; return Task.FromResult(new SourceAvailabilityResult {Status = Status, ReasonCode = ReasonCode, ProbeId = "test", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)}); }
    }
}
