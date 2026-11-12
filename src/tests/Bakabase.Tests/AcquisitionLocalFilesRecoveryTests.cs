using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.TestKit.Implementations;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private sealed class LocalFilesHost(Bakabase.InsideWorld.Business.BakabaseDbContext db) : IWorkflowDefinitionService
    {
        public bool FailBeforeRun;
        public int Calls;
        public async Task<WorkflowRun> RunPlanAsync(WorkflowRunPlan plan, object payload,
            IReadOnlyList<WorkflowSourceRef>? sources = null, CancellationToken ct = default)
        {
            if (FailBeforeRun) throw new InvalidOperationException("Simulated restart after prepared revision");
            Calls++;
            var row = new WorkflowRunDbModel {Status = WorkflowRunStatus.Pending, StartedAt = DateTime.UtcNow,
                PlanSnapshotJson = WorkflowRunPlans.Serialize(plan), PayloadJson = JsonSerializer.Serialize(payload, WorkflowJson.Options),
                Sources = (sources ?? []).Select(s => new WorkflowRunSourceDbModel {Module = s.Module, EntityKind = s.EntityKind,
                    EntityId = s.EntityId, Revision = s.Revision ?? ""}).ToList()};
            db.WorkflowRuns.Add(row); await db.SaveChangesAsync(ct); return new() {Id = row.Id, Status = row.Status};
        }
        public Task<WorkflowDefinition> CreateAsync(WorkflowDefinitionCreationInputModel input, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkflowDefinition> UpdateAsync(int id, WorkflowDefinitionUpdateInputModel input, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(int id) => throw new NotSupportedException();
        public Task<WorkflowDefinition?> GetAsync(int id) => throw new NotSupportedException();
        public Task<List<WorkflowDefinition>> SearchAsync(WorkflowDefinitionSearchInputModel input) => throw new NotSupportedException();
        public Task<SearchResponse<WorkflowRun>> SearchRunsAsync(WorkflowRunSearchInputModel input) => throw new NotSupportedException();
        public Task<WorkflowRun> RunManuallyAsync(int id, string? argsJson, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkflowRun> RunManagedAsync(int id, object payload, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private async Task<(int TaskId, int RunId, AcquisitionRecoveryService Recovery, AcquisitionLocalFilesRecoveryService Local,
        LocalFilesHost Host, AcquisitionPlanService Plans, TestBOptionsManager<AcquisitionOptions> Options)> LocalFilesFixture(bool dependencyMissing = false)
    {
        var (task, run, workset, _, _) = await SeedRevisedCompletedDownload("download.awaitCompletion", "11111111111111111111111111111111");
        run.WaitReason = "cloud.cloudAttention";
        run.Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "1"}];
        var source = workset.Targets.Single().SelectedSource!;
        var plan = new AcquisitionPlan {Title = "Local continuation", OutputDirectory = workset.OutputDirectory, Parts = [new()
        {Id = "part", SelectedSourceId = source.Id, Sources = [new() {Id = source.Id, Url = source.Url, Kind = source.Kind,
            ProviderId = source.ProviderId, AccountId = source.AccountId, AccessCode = source.AccessCode}],
            Processing = dependencyMissing ? new() {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive",
                ZipPasswordCodePage = (ZipPasswordCodePage)936}]} : new() {Requirement = "unknown"}}]};
        task.PlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options);
        task.BuilderStateJson = "{\"query\":\"preserved search\",\"candidates\":[]}";
        task.PlanHistoryJson = JsonSerializer.Serialize(new[] {new AcquisitionPlanRevision(1, plan, [], DateTime.UtcNow)}, WorkflowJson.Options);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var options = new TestBOptionsManager<AcquisitionOptions>(new()
        {ProcessingWorkspaceDirectory = Directory.CreateDirectory(Path.Combine(_root, "current-work")).FullName,
            DeliveryRootDirectory = Directory.CreateDirectory(Path.Combine(_root, "current-delivery")).FullName,
            StoragePolicy = new() {SourceCleanup = "delete"}});
        var storage = new UserStoragePolicy(false);
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var host = new LocalFilesHost(_db);
        // Local continuations have zero acquisition candidates. Keep the real plan/replan
        // service while making any accidental provider resolution impossible in this fixture.
        var intents = new DownloadIntentService(new([], [], null!, null!), null!, storage, _db,
            null!, null!, null!, null!, null!);
        var plans = new AcquisitionPlanService(_db, [], _services.GetRequiredService<IAcquisitionService>(), host,
            intents, storage, artifacts: store,
            deliveryDirectories: new(storage, options), storageDefaults: new(options, storage));
        var local = new AcquisitionLocalFilesRecoveryService(_db, plans, storage, store,
            new(_db, store, storage, _services.GetRequiredService<DownloadResultService>()), _services);
        return (task.Id, run.Id, new(_db, _diagnostics, new(), new RecordingHost(), plans, localFiles: local), local, host, plans, options);
    }

    [TestMethod]
    public async Task LocalFiles_preview_is_readonly_and_apply_starts_only_one_local_revision_preserving_originals()
    {
        var f = await LocalFilesFixture();
        var originalRun = JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync());
        var originalDownloads = JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().ToArrayAsync());
        var originalOperations = JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().ToArrayAsync());
        var originalResults = JsonSerializer.Serialize(await _db.DownloadResults.AsNoTracking().ToArrayAsync());
        var originalTask = await _db.AcquisitionTasks.AsNoTracking().SingleAsync();
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        var row = preview.Rows.Single(); Assert.IsTrue(row.CanContinue, row.ReasonCode);
        Assert.AreEqual("localFiles", row.Action);
        Assert.AreEqual(f.Options.Value.ProcessingWorkspaceDirectory, row.LocalFiles!.Parts.Single().WorkingDirectory);
        StringAssert.StartsWith(row.LocalFiles.Parts.Single().DeliveryDirectory, f.Options.Value.DeliveryRootDirectory);
        Assert.AreEqual(0, await _db.Artifacts.CountAsync());
        Assert.AreEqual(originalTask.BuilderStateJson, (await _db.AcquisitionTasks.AsNoTracking().SingleAsync()).BuilderStateJson);
        var result = await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId]));
        Assert.AreEqual(0, result.Skipped.Count, string.Join(",", result.Skipped.Select(s => s.ReasonCode)));
        var outcome = result.LocalFiles!.Single(); Assert.AreEqual("started", outcome.Status); Assert.AreEqual(2, outcome.PlanRevision);
        Assert.AreEqual(outcome, (await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId]))).LocalFiles!.Single());
        Assert.AreEqual(1, f.Host.Calls); Assert.AreEqual(2, await _db.WorkflowRuns.CountAsync());
        var detail = (await f.Plans.GetAsync(f.TaskId, default))!;
        Assert.AreEqual("keep", detail.Plan!.StoragePolicy!.SourceCleanup);
        Assert.AreEqual("unknown", detail.Plan.Parts.Single().Processing!.Requirement);
        Assert.IsTrue(AcquisitionPlanCompiler.Compile(detail.Plan).Nodes.All(n => n.Kind is
            "acquisition.plan.continueFiles" or "acquisition.plan.process" or "fs.assessDelivery" or "fs.publishArtifacts"));
        Assert.AreEqual(originalRun, JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == f.RunId)));
        Assert.AreEqual(originalDownloads, JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().ToArrayAsync()));
        Assert.AreEqual(originalOperations, JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().ToArrayAsync()));
        Assert.AreEqual(originalResults, JsonSerializer.Serialize(await _db.DownloadResults.AsNoTracking().ToArrayAsync()));
        Assert.AreEqual(originalTask.PurchaseRecordJson, (await _db.AcquisitionTasks.AsNoTracking().SingleAsync()).PurchaseRecordJson);
        Assert.AreEqual("preserved search", detail.Query);
        Assert.IsFalse(Directory.Exists(detail.Plan.DeliveryLayout!.TaskDirectory));
    }

    [DataTestMethod]
    [DataRow("delete", "keep")]
    [DataRow("keep", "delete")]
    [DataRow("quarantine", "delete")]
    public async Task Local_continuation_preserves_frozen_retention_without_replacing_it_with_temporary_borrow_protection(
        string frozen, string current)
    {
        var f = await LocalFilesFixture();
        var task = await _db.AcquisitionTasks.SingleAsync();
        var policy = new ArtifactStoragePolicy {SourceCleanup = frozen};
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson!, WorkflowJson.Options)! with {StoragePolicy = policy};
        var state = JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(task.BuilderStateJson!, WorkflowJson.Options)!;
        task.PlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options);
        task.BuilderStateJson = JsonSerializer.Serialize(state with {StoragePolicy = policy}, WorkflowJson.Options);
        task.PlanHistoryJson = JsonSerializer.Serialize(new[] {new AcquisitionPlanRevision(1, plan, [], DateTime.UtcNow)}, WorkflowJson.Options);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        f.Options.Value.StoragePolicy = new() {SourceCleanup = current};
        var inputs = JsonSerializer.Deserialize<string[]>((await _db.DownloadResults.AsNoTracking().SingleAsync()).FilesJson)!;
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        Assert.IsTrue(preview.Rows.Single().CanContinue, preview.Rows.Single().ReasonCode);
        var result = await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId]));
        Assert.AreEqual(0, result.Skipped.Count);
        var detail = (await f.Plans.GetAsync(f.TaskId, default))!;
        Assert.AreEqual(frozen, detail.Plan!.StoragePolicy!.SourceCleanup);
        Assert.IsNull(detail.Plan.Parts.Single().StoragePolicy?.SourceCleanup);
        Assert.AreEqual(frozen, detail.PlanHistory[0].Plan.StoragePolicy!.SourceCleanup);
        Assert.IsTrue(inputs.All(File.Exists), "A retention preference cannot reclaim a borrowed input while its continuation is pending.");
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await _db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == f.RunId)).Status);
    }

    [DataTestMethod]
    [DataRow("settings")]
    [DataRow("files")]
    [DataRow("answer")]
    [DataRow("shared")]
    [DataRow("deleted")]
    public async Task LocalFiles_apply_rejects_changed_review_evidence_before_adoption(string change)
    {
        var f = await LocalFilesFixture(); var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        Assert.IsTrue(preview.Rows.Single().CanContinue);
        if (change == "settings") f.Options.Value.DeliveryRootDirectory = Directory.CreateDirectory(Path.Combine(_root, "other-delivery")).FullName;
        if (change == "files") await File.WriteAllTextAsync(JsonSerializer.Deserialize<string[]>((await _db.DownloadResults.AsNoTracking().SingleAsync()).FilesJson)![0], "changed");
        if (change == "answer") await _db.WorkflowRuns.Where(r => r.Id == f.RunId).ExecuteUpdateAsync(u => u.SetProperty(r => r.PendingSignalJson, "{}"));
        if (change == "deleted") await _db.AcquisitionTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(u => u.SetProperty(t => t.DeletedAt, DateTime.Now));
        if (change == "shared")
        {
            _db.ArtifactOperationBindings.Add(new() {Id = Guid.NewGuid().ToString("N"), OwnerKind = "workflow", OwnerId = "999",
                Slot = "part:download", OperationId = (await _db.CloudDownloadOperations.AsNoTracking().SingleAsync()).Id});
            await _db.SaveChangesAsync();
        }
        var result = await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId]));
        Assert.AreEqual(1, result.Skipped.Count); Assert.AreEqual(0, f.Host.Calls);
        Assert.AreEqual(1, (await _db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
        Assert.AreEqual(0, await _db.Artifacts.CountAsync());
    }

    [TestMethod]
    public async Task LocalFiles_partial_artifact_registration_can_be_retried_without_rewriting_old_checkpoint()
    {
        var f = await LocalFilesFixture(); var run = await _db.WorkflowRuns.AsNoTracking().SingleAsync();
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson)!;
        var result = await _db.DownloadResults.AsNoTracking().SingleAsync();
        var target = workset.Targets.Single() with {DownloadDirectory = result.DownloadDirectory,
            Files = JsonSerializer.Deserialize<List<string>>(result.FilesJson)!};
        var registered = await new DownloadArtifactCoordinator(_db, _services.GetRequiredService<IArtifactOperationStore>(), new UserStoragePolicy(false),
            _services.GetRequiredService<DownloadResultService>()).RecordAsync(new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = "{}", Services = _services, Logger = NullLogger.Instance},
            target, workset.OutputDirectory, default);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => AcquisitionPlanArtifactInputGuard.RequireAsync(_db,
            _services.GetRequiredService<IArtifactOperationStore>(), f.TaskId, target.Id,
            new(f.TaskId, run.Id, registered.DownloadArtifactId!, registered.DownloadOperationId!, result.Fingerprint), default));
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        Assert.IsTrue(preview.Rows.Single().CanContinue, preview.Rows.Single().ReasonCode);
        var applied = await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId]));
        Assert.AreEqual("started", applied.LocalFiles!.Single().Status); Assert.AreEqual(1, await _db.Artifacts.CountAsync());
        Assert.AreEqual(run.CurrentItemJson, (await _db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id)).CurrentItemJson);
    }

    [TestMethod]
    public async Task LocalFiles_prepared_receipt_survives_restart_and_queue_removal_without_a_third_revision()
    {
        var f = await LocalFilesFixture(); f.Host.FailBeforeRun = true;
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId])));
        var task = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(); Assert.AreEqual(2, task.PlanRevision); Assert.IsNull(task.WorkflowRunId);
        await _db.DownloadResults.ExecuteDeleteAsync(); await _db.DownloadTasks.ExecuteDeleteAsync();
        f.Host.FailBeforeRun = false;
        var restarted = new AcquisitionRecoveryService(_db, _diagnostics, new(), new RecordingHost(), f.Plans, localFiles: f.Local);
        var next = await restarted.PreviewAsync(new([f.TaskId], "localFiles")); Assert.IsTrue(next.Rows.Single().CanContinue, next.Rows.Single().ReasonCode);
        var result = await restarted.ApplyAsync(new(next.PreviewToken, [f.TaskId]));
        Assert.AreEqual("started", result.LocalFiles!.Single().Status); Assert.AreEqual(2, result.LocalFiles.Single().PlanRevision);
        Assert.AreEqual(1, f.Host.Calls); Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task LocalFiles_missing_processing_dependency_reports_prepared_and_reuses_same_revision()
    {
        var f = await LocalFilesFixture(dependencyMissing: true);
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        var result = await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId]));
        Assert.AreEqual("prepared", result.LocalFiles!.Single().Status);
        Assert.AreEqual("DEPENDENCY_REQUIRED", result.LocalFiles.Single().ReasonCode); Assert.AreEqual(0, f.Host.Calls);
        var next = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles")); Assert.IsTrue(next.Rows.Single().CanContinue);
        var retried = await f.Recovery.ApplyAsync(new(next.PreviewToken, [f.TaskId]));
        Assert.AreEqual(2, retried.LocalFiles!.Single().PlanRevision); Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }
}
