using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.TestKit.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Prepared_plan_compiled_from_a_draft_retries_its_original_download_with_its_own_directories(bool configured)
    {
        var downloads = Directory.CreateDirectory(Path.Combine(_root, "downloads")).FullName;
        var workspace = Directory.CreateDirectory(Path.Combine(_root, "work")).FullName;
        var delivery = Directory.CreateDirectory(Path.Combine(_root, "delivery")).FullName;
        var storage = new UserStoragePolicy(false);
        var options = new TestBOptionsManager<AcquisitionOptions>(new()
            {ProcessingWorkspaceDirectory = configured ? workspace : null, DeliveryRootDirectory = configured ? delivery : null});
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var intents = new DownloadIntentService(new([], [], null!, null!), null!, storage, _db, null!, null!, null!, null!, null!);
        var plans = new AcquisitionPlanService(_db, [], _services.GetRequiredService<Bakabase.Modules.Acquisition.Abstractions.Services.IAcquisitionService>(),
            null!, intents, storage, artifacts: store,
            deliveryDirectories: new(storage, options), storageDefaults: new(options, storage));
        var source = new AcquisitionPlanSource {Id = "source", ProviderId = "pikpak", Kind = "share", MethodId = "pikpak.share",
            Url = "https://mypikpak.com/s/fixture"};
        var draft = await plans.CreateDraftAsync(new("Saved copy", OutputDirectory: downloads,
            DeliverySettings: new() {RootDirectory = delivery}, StoragePolicy: new() {ProcessingWorkspaceDirectory = workspace}), default);
        var prepared = await plans.PrepareDraftAsync(draft.Id, new(draft.Task.PlanRevision, new()
        {
            Title = "Saved copy", OutputDirectory = downloads, Parts = [new() {Id = "part", SelectedSourceId = source.Id,
                Processing = new() {Requirement = "notRequired"}, Sources = [source]}]
        }), default);
        var task = await _db.AcquisitionTasks.SingleAsync(t => t.Id == prepared.Id);
        var plan = prepared.Plan!;
        var workset = AcquisitionPlanCompiler.Workset(task.Id, task.PlanRevision, plan, []);
        var target = workset.Targets.Single(); var selected = target.SelectedSource!;
        var workflow = AcquisitionPlanCompiler.Compile(plan, [selected]);
        CollectionAssert.AreEqual(new[] {"download.selectSources", "download.enqueueSupported", "download.awaitCompletion",
            "acquisition.plan.process", "fs.publishArtifacts", "download.cleanupArtifacts"}, workflow.Nodes.Select(n => n.Kind).ToArray());
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Waiting, StartedAt = DateTime.Now, CurrentStepIndex = 2,
            WaitReason = "cloud.cloudAttention", PlanSnapshotJson = WorkflowRunPlans.Serialize(workflow)};
        _db.WorkflowRuns.Add(run); await _db.SaveChangesAsync();
        var operation = DownloadArtifactCoordinator.DownloadIntent(target, downloads);
        var directory = CloudStageDirectoryPath.PlannedDownload(target, downloads, operation);
        Directory.CreateDirectory(directory);
        var request = new CloudDownloadRequest(operation, selected, selected.AccountId, null, directory);
        var native = new DownloadTaskDbModel {Type = 1, Status = DownloadTaskDbModelStatus.Failed, Message = "PIKPAK_ORIGINAL_FILE_UNAVAILABLE",
            ThirdPartyId = CloudSites.Site(selected.ProviderId), DownloadPath = directory, Options = JsonSerializer.Serialize(request, JsonSerializerOptions.Web)};
        _db.DownloadTasks.Add(native); await _db.SaveChangesAsync();
        _db.CloudDownloadOperations.Add(new() {Id = operation, TargetId = target.Id, WorkflowRunId = run.Id, Kind = "download",
            State = "started", DownloadTaskId = native.Id, RequestJson = native.Options});
        _db.ArtifactOperationBindings.Add(new() {Id = "bound-original", OwnerKind = "workflow", OwnerId = run.Id.ToString(),
            Slot = CloudOperationService.OperationSlot(target, "download"), OperationId = operation});
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task",
            EntityId = task.Id.ToString(), Revision = task.PlanRevision.ToString()});
        task.WorkflowRunId = run.Id; task.Phase = "attention";
        var stopped = workset with {Targets = [target with {DownloadTaskId = native.Id, DownloadOperationId = operation, Problem = native.Message}]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(stopped); await _db.SaveChangesAsync();
        var provider = new PreparedRecoveryProvider {MethodId = "pikpak.share"};
        var recovery = new CloudDownloadRecoveryService(_db, [provider], storage);
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, storage, store, downloads: recovery);
        var local = new AcquisitionLocalFilesRecoveryService(_db, plans, storage, store, null!, _services);
        var host = new ProcessingRecoveryHost();
        var service = new AcquisitionRecoveryService(_db, diagnostics, new(), host, plans, localFiles: local);
        var continuation = new AcquisitionContinuationService(_db, service, new(), null!, null!, null!);
        var batch = new AcquisitionLocalRecoveryBatchService(_db, service, diagnostics, local, plans, options, storage,
            new PinnedBatchCapacity(), new(), continuations: continuation);
        var preview = await batch.PreviewAsync(new([task.Id], "automatic"));
        var row = preview.Rows.Single(); Assert.IsTrue(row.CanQueue, row.ReasonCode);
        Assert.AreEqual("RECOVERY_PREPARED_DOWNLOAD_REVALIDATED", row.ReasonCode);
        Assert.AreEqual(0, provider.Checks, "The ordinary list preview is local-only.");
        CollectionAssert.AreEqual(new[] {directory}, preview.Volumes.SelectMany(v => v.Paths).ToArray());
        var applied = await batch.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        await batch.TickAsync();
        Assert.AreEqual(1, host.Calls, (await batch.GetAsync(applied.BatchId)).Rows.Single().ReasonCode);
        Assert.AreEqual(operation, JsonSerializer.Deserialize<CloudSelectionSignal>(host.Signal!, WorkflowJson.Options)!
            .PreparedDownloadRetries![target.Id].OperationId);
    }

    [DataTestMethod]
    [DataRow("automatic", true, false, false, false)]
    [DataRow("automatic", false, false, false, false)]
    [DataRow("localOnly", true, false, false, false)]
    [DataRow("automatic", true, true, false, false)]
    [DataRow("automatic", true, false, true, false)]
    [DataRow("automatic", true, false, true, true)]
    public async Task Batch_changed_storage_defaults_do_not_require_completed_files_to_resume_original_download(string mode, bool available,
        bool retainedBytes, bool preparedCopy, bool loseCopy)
    {
        var (task, run, workset, download, _, provider, _) = await SeedRecoverableProvider();
        var storage = new UserStoragePolicy(false);
        if (preparedCopy)
        {
            provider = new PreparedRecoveryProvider();
            var downloads = new CloudDownloadRecoveryService(_db, [provider], storage);
            _diagnostics = new(_db, storage, _services.GetRequiredService<IArtifactOperationStore>(), downloads: downloads);
        }
        var options = new TestBOptionsManager<AcquisitionOptions>(new()
        {ProcessingWorkspaceDirectory = Directory.CreateDirectory(Path.Combine(_root, "current-work")).FullName,
            DeliveryRootDirectory = Directory.CreateDirectory(Path.Combine(_root, "current-delivery")).FullName});
        var source = workset.Targets.Single().SelectedSource!;
        task.PlanJson = JsonSerializer.Serialize(new AcquisitionPlan {Title = task.Title!, OutputDirectory = _root,
            Parts = [new() {Id = "part", SelectedSourceId = source.Id,
                Sources = [new() {Id = source.Id, Url = source.Url, ProviderId = source.ProviderId, Kind = source.Kind}],
                Processing = new() {Requirement = "unknown"}}],
            DeliveryLayout = new() {TaskId = task.Id, RootDirectory = options.Value.DeliveryRootDirectory!,
                TaskDirectory = Path.Combine(options.Value.DeliveryRootDirectory!, "task"),
                PartDirectories = new() {{"part", Path.Combine(options.Value.DeliveryRootDirectory!, "task")}}}}, WorkflowJson.Options);
        run.WaitReason = "cloud.cloudAttention";
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Download then local work", TriggerKind = CloudWorkflow.Trigger,
            Nodes = [new() {NodeId = "download", Kind = "download.awaitCompletion"},
                new() {NodeId = "process", Kind = AcquisitionPlanProcessingActivity.ActivityKind,
                    ConfigJson = JsonSerializer.Serialize(new AcquisitionPlanProcessingConfig("part", new() {Requirement = "unknown"}), WorkflowJson.Options)},
                new() {NodeId = "publish", Kind = "fs.publishArtifacts"}]});
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "1"});
        await _db.SaveChangesAsync();
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var plans = new AcquisitionPlanService(_db, [], null!, null!, null!, storage, artifacts: store,
            deliveryDirectories: new(storage, options), storageDefaults: new(options, storage));
        var local = new AcquisitionLocalFilesRecoveryService(_db, plans, storage, store, null!, _services);
        var host = new ProcessingRecoveryHost();
        var recovery = new AcquisitionRecoveryService(_db, _diagnostics, new(), host, plans, localFiles: local);
        var continuation = new AcquisitionContinuationService(_db, recovery, new(), null!, null!, null!);
        var service = new AcquisitionLocalRecoveryBatchService(_db, recovery, _diagnostics, local, plans,
            options, storage, new PinnedBatchCapacity(), new(), continuations: continuation);
        var frozen = run.PlanSnapshotJson; var request = download.Options;
        string? retained = null;
        if (retainedBytes)
        {
            Directory.CreateDirectory(download.DownloadPath);
            retained = Path.Combine(download.DownloadPath, "retained-original.bin");
            await File.WriteAllTextAsync(retained, "keep these original bytes");
        }
        var preview = await service.PreviewAsync(new([task.Id], mode));
        var row = preview.Rows.Single();
        if (preparedCopy) Assert.AreEqual("RECOVERY_PREPARED_DOWNLOAD_REVALIDATED", row.ReasonCode);
        Assert.AreEqual(mode == "automatic" && !retainedBytes, row.CanQueue, row.ReasonCode);
        if (retainedBytes)
        {
            Assert.AreEqual("RECOVERY_DOWNLOAD_HAS_OUTPUT", row.ReasonCode);
            Assert.AreEqual("keep these original bytes", await File.ReadAllTextAsync(retained!));
            Assert.AreEqual(0, host.Calls); Assert.AreEqual(0, provider.Checks);
            Assert.AreEqual(0, (await service.ListAsync()).Count);
            Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
            return;
        }
        Assert.AreEqual(0, provider.Checks, "The list preview must not call the provider.");
        if (mode == "localOnly") {Assert.AreEqual(0, host.Calls); return;}
        Assert.AreEqual("workflow", row.ActionCategory); Assert.AreEqual("workflow", row.RecoveryScope);
        Assert.IsTrue(row.CanOverrideStorage);
        CollectionAssert.AreEqual(new[] {download.DownloadPath}, preview.Volumes.SelectMany(v => v.Paths).ToArray());
        var batch = await service.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        provider.Available = available;
        if (loseCopy) ((PreparedRecoveryProvider)provider).Prepared = false;
        await service.TickAsync();
        Assert.AreEqual(available && !loseCopy ? 1 : 0, host.Calls, (await service.GetAsync(batch.BatchId)).Rows.Single().ReasonCode);
        Assert.IsTrue(provider.Checks > 0);
        if (!available)
        {
            var expected = await _diagnostics.DiagnoseAsync(task, run, default);
            Assert.AreEqual("RECOVERY_SOURCE_STILL_BLOCKED", expected.Row.ReasonCode);
            Assert.AreEqual(expected.Row.CurrentProblemCode, (await service.GetAsync(batch.BatchId)).Rows.Single().ReasonCode);
        }
        if (available && !loseCopy)
        {
            var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(host.Signal!, WorkflowJson.Options)!;
            Assert.IsTrue(signal.ObserveExistingOnly == true && signal.RevalidateExistingDownloads == true);
            CollectionAssert.AreEqual(new[] {"part"}, signal.ResumeTargetIds!);
        }
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(download.Id, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Id);
        Assert.AreEqual(request, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Options);
        Assert.AreEqual(frozen, (await _db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id)).PlanSnapshotJson);
        Assert.IsFalse(Directory.Exists(download.DownloadPath), "No payload downloader runs in this fixture.");
    }

    [TestMethod]
    public async Task Batch_with_changed_storage_verifies_existing_files_without_restarting_the_download()
    {
        var (run, target, download, operation, transfer, provider, downloads) = await SeedRetainedDownload();
        download.Key = "original";
        download.Message = "BAIDU_LOCAL_FILE_CHANGED";
        var task = await _db.AcquisitionTasks.SingleAsync(t => t.WorkflowRunId == run.Id);
        var source = target.SelectedSource!;
        task.PlanJson = JsonSerializer.Serialize(new AcquisitionPlan
        {
            Title = task.Title!, OutputDirectory = _root,
            Parts = [new() {Id = target.Id, SelectedSourceId = source.Id,
                Sources = [new() {Id = source.Id, Url = source.Url, ProviderId = source.ProviderId, Kind = source.Kind}],
                Processing = new() {Requirement = "unknown"}}]
        }, WorkflowJson.Options);
        run.WaitReason = "cloud.cloudAttention";
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Retained download", TriggerKind = CloudWorkflow.Trigger,
            Nodes = [new() {NodeId = "download", Kind = "download.awaitCompletion"}, new() {NodeId = "publish", Kind = "fs.publishArtifacts"}]});
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)! with {Targets = [target with {Problem = download.Message}]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "1"});
        await _db.SaveChangesAsync();

        var storage = new UserStoragePolicy(false);
        var options = new TestBOptionsManager<AcquisitionOptions>(new()
        {
            ProcessingWorkspaceDirectory = Directory.CreateDirectory(Path.Combine(_root, "current-work")).FullName,
            DeliveryRootDirectory = Directory.CreateDirectory(Path.Combine(_root, "current-delivery")).FullName
        });
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var plans = new AcquisitionPlanService(_db, [], null!, null!, null!, storage, artifacts: store,
            deliveryDirectories: new(storage, options), storageDefaults: new(options, storage));
        var local = new AcquisitionLocalFilesRecoveryService(_db, plans, storage, store, null!, _services);
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, storage, store, downloads: downloads);
        var host = new ProcessingRecoveryHost();
        var recovery = new AcquisitionRecoveryService(_db, diagnostics, new(), host, plans, localFiles: local);
        var continuation = new AcquisitionContinuationService(_db, recovery, new(), null!, null!, null!);
        var batches = new AcquisitionLocalRecoveryBatchService(_db, recovery, diagnostics, local, plans,
            options, storage, new PinnedBatchCapacity(), new(), continuations: continuation);
        var preview = await batches.PreviewAsync(new([task.Id], "automatic"));
        Assert.IsTrue(preview.Rows.Single().CanQueue, preview.Rows.Single().ReasonCode);
        Assert.AreEqual(0, provider.VerificationCalls);
        Assert.IsFalse(provider.Checks.Contains(true));
        var batch = await batches.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        await batches.TickAsync();
        Assert.AreEqual(1, host.Calls, (await batches.GetAsync(batch.BatchId)).Rows.Single().ReasonCode);
        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(host.Signal!, WorkflowJson.Options)!;
        Assert.IsTrue(signal.VerifyExistingFilesOnly == true && signal.ObserveExistingOnly == true);
        Assert.IsFalse(signal.RevalidateExistingDownloads == true);
        CollectionAssert.AreEqual(new[] {target.Id}, signal.ResumeTargetIds!);
        Assert.IsNull(AcquisitionDownloadOnlyRecoveryService.Targets(run, host.Signal!));
        Assert.IsTrue(provider.Checks.Count > 0);
        Assert.IsFalse(provider.Checks.Contains(true), "Verification must never probe the remote source for a retry.");
        Assert.AreEqual(download.Id, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Id);
        CollectionAssert.AreEquivalent(new[] {operation.Id, transfer.Id}, await _db.CloudDownloadOperations.Select(o => o.Id).ToArrayAsync());
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Batch_completed_original_download_uses_current_storage_with_exact_approved_receipt(bool changed)
    {
        var f = await LocalFilesFixture();
        var continuation = new AcquisitionContinuationService(_db, f.Recovery, new(), null!, null!, null!);
        var service = new AcquisitionLocalRecoveryBatchService(_db, f.Recovery, _diagnostics, f.Local, f.Plans,
            f.Options, new UserStoragePolicy(false), new PinnedBatchCapacity(), new(), continuations: continuation);
        var before = JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().ToArrayAsync());
        var preview = await service.PreviewAsync(new([f.TaskId], "automatic"));
        var row = preview.Rows.Single();
        Assert.IsTrue(row.CanQueue, row.ReasonCode);
        Assert.AreEqual("localFiles", row.RecoveryScope); Assert.AreEqual("localProcessing", row.ActionCategory);
        Assert.AreEqual(f.Options.Value.ProcessingWorkspaceDirectory, row.WorkingDirectory);
        StringAssert.StartsWith(row.DeliveryDirectory!, f.Options.Value.DeliveryRootDirectory!);
        Assert.AreEqual(before, JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().ToArrayAsync()));
        Assert.AreEqual(0, f.Host.Calls);

        var batch = await service.ApplyAsync(new(preview.PreviewToken, [f.TaskId]));
        var scheduled = new List<int>();
        await service.TickAsync(new HashSet<int>(), scheduled.Add, null, default);
        CollectionAssert.AreEqual(new[] {f.TaskId}, scheduled);
        Assert.AreEqual(0, f.Host.Calls);
        await service.TickAsync(scheduled.ToHashSet(), scheduled.Add, null, default);
        Assert.AreEqual(1, scheduled.Count);

        if (changed)
            await _db.DownloadResults.ExecuteUpdateAsync(s => s.SetProperty(r => r.Fingerprint, "changed"));
        await service.DispatchAsync(f.TaskId, default);
        if (changed)
        {
            var blocked = (await service.GetAsync(batch.BatchId)).Rows.Single();
            Assert.AreEqual("attention", blocked.Status);
            Assert.AreEqual("ACQUISITION_TASK_CHANGED", blocked.ReasonCode);
            Assert.AreEqual(0, f.Host.Calls);
            Assert.AreEqual(0, await _db.Artifacts.CountAsync());
            return;
        }
        Assert.AreEqual(1, f.Host.Calls, (await service.GetAsync(batch.BatchId)).Rows.Single().ReasonCode);
        Assert.AreEqual("running", (await service.GetAsync(batch.BatchId)).Rows.Single().Status);
        await service.DispatchAsync(f.TaskId, default);
        Assert.AreEqual(1, f.Host.Calls);
        Assert.AreEqual(before, JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().ToArrayAsync()));
    }
}
