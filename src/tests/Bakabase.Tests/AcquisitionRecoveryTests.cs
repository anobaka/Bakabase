using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace Bakabase.Tests;

[TestClass]
// Independent fixture databases reuse task IDs, while production task gates are
// process-wide. Concurrent classes would simulate another active task accidentally.
[DoNotParallelize]
public sealed partial class AcquisitionRecoveryTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private AcquisitionRecoveryDiagnostics _diagnostics = null!;
    private string _root = null!;
    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "acquisition-recovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_root);
        _services = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.AddTransient(sp => new BakabaseLocalizer(sp.GetRequiredService<IStringLocalizer<SharedResource>>()));
            // Keep the configured reserve active and measure a fixture volume. The failure
            // cases provide their own capacity evidence independently of the developer's disk.
            services.RemoveAll<IStorageWriteGuard>();
            services.AddSingleton<IStorageWriteGuard>(sp => new StorageWriteGuard(
                sp.GetRequiredService<IStorageWritePolicyProvider>(), _ => (_root, 1L << 40), TimeSpan.FromMilliseconds(500)));
        });
        _db = _services.GetRequiredService<BakabaseDbContext>();
        _diagnostics = new(_db, new UserStoragePolicy(false), _services.GetRequiredService<IArtifactOperationStore>());
    }
    [TestCleanup] public void Cleanup() {(_services as IDisposable)?.Dispose(); Directory.Delete(_root, true);}
    private async Task<(AcquisitionTaskDbModel Task, WorkflowRunDbModel Run, DownloadWorkset Workset)> Seed(string kind = "baidu.transfer",
        WorkflowRunStatus status = WorkflowRunStatus.Failed, string? error = "Cannot access a disposed object. Object name: 'IServiceProvider'.")
    {
        var workset = new DownloadWorkset {OutputDirectory = _root, Targets = [new() {Id = "part", AccountId = "account", SelectedSourceId = "source",
            Sources = [new() {Id = "source", ProviderId = "baidu", Kind = "share", Url = "https://pan.baidu.com/s/fixture", AccessCode = "code", AccountId = "account"}]}]};
        var run = new WorkflowRunDbModel {Status = status, ErrorMessage = error, StartedAt = DateTime.Now, CompletedAt = DateTime.Now,
            CurrentStepIndex = 0, CurrentItemJson = WorkflowItemSnapshot.Capture(workset), PlanSnapshotJson = WorkflowRunPlans.Serialize(new()
            {Name = "Recovery fixture", TriggerKind = CloudWorkflow.Trigger, Nodes = [new() {NodeId = "step", Kind = kind}]})};
        _db.WorkflowRuns.Add(run); await _db.SaveChangesAsync();
        var task = new AcquisitionTaskDbModel {Title = "Fixture", BuilderId = "prepared", PlanRevision = 1, Phase = "attention",
            Status = AcquisitionStatus.Waiting, WorkflowRunId = run.Id, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now,
            PurchaseRecordJson = "[{\"retained\":true}]", PlanHistoryJson = "[]"};
        _db.AcquisitionTasks.Add(task); await _db.SaveChangesAsync(); return (task, run, workset);
    }
    [TestMethod]
    public async Task PreviewRecognizesPreTransferDisposedFailureWithoutWritingAnyOperationOrHistory()
    {
        var (task, run, _) = await Seed(); var before = JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync());
        var result = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsTrue(result.Row.CanContinue); Assert.AreEqual("RECOVERY_RUNTIME_SCOPE_FIXED", result.Row.ReasonCode);
        Assert.AreEqual("RECOVERY_SERVICE_SCOPE_DISPOSED", result.Row.CurrentProblemCode);
        Assert.AreEqual(before, JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync()));
        Assert.AreEqual(0, await _db.CloudDownloadOperations.CountAsync()); Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
    }
    [DataTestMethod]
    [DataRow("unknown")]
    [DataRow("running")]
    [DataRow("failed")]
    public async Task PriorUnconfirmedTransferNeverBecomesAnAutomaticRetry(string state)
    {
        var (task, run, workset) = await Seed(); var target = workset.Targets.Single();
        _db.CloudDownloadOperations.Add(new() {Id = CloudOperationService.Key("transfer.v2", DownloadArtifactCoordinator.Source(target), "/Bakabase"),
            WorkflowRunId = run.Id, TargetId = target.Id, Kind = "transfer", State = state, RequestJson = "retained"});
        await _db.SaveChangesAsync(); var result = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsFalse(result.Row.CanContinue); Assert.AreEqual(state, (await _db.CloudDownloadOperations.AsNoTracking().SingleAsync()).State);
    }
    [DataTestMethod]
    [DataRow("BAIDU_TRANSFER_FAILED_CONFIRM_TARGET")]
    [DataRow("BAIDU_TRANSFER_INCOMPLETE_CONFIRM_TARGET")]
    public async Task DeferredRecoveryReportsUnconfirmedTransferTargetWithoutRepeatingTransfer(string code)
    {
        var (task, run, workset) = await Seed(status: WorkflowRunStatus.Waiting, error: null);
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with
        {
            Targets = [workset.Targets[0] with {Problem = $"BridgeException: {code}"}]
        });
        await _db.SaveChangesAsync();
        task = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        run = await _db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);

        var diagnosis = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsFalse(diagnosis.Row.CanContinue);
        Assert.AreEqual(code, diagnosis.Row.CurrentProblemCode);
        Assert.AreEqual("inspectSource", diagnosis.Row.ManualAction);

        var recovery = new AcquisitionRecoveryService(_db, _diagnostics, new(), null!);
        var result = await recovery.ContinueDeferredAsync(task.Id, "workflow",
            AcquisitionContinuationEntry.ContentVersion(task, run), default, _ => Task.CompletedTask);
        Assert.AreEqual(0, result.ContinuedTaskIds.Count);
        Assert.AreEqual("RECOVERY_TRANSFER_REVIEW_REQUIRED", result.Skipped.Single().ReasonCode);
        Assert.AreEqual(0, await _db.CloudDownloadOperations.CountAsync());
    }
    [TestMethod]
    public async Task ChangedTransferBindingAndMissingAccountCannotBeMistakenForAnUnattemptedTransfer()
    {
        var (task, run, workset) = await Seed();
        _db.ArtifactOperationBindings.Add(new() {Id = "binding", OwnerKind = "workflow", OwnerId = run.Id.ToString(), Slot = "part:transfer", OperationId = "different"});
        await _db.SaveChangesAsync(); Assert.IsFalse((await _diagnostics.DiagnoseAsync(task, run, default)).Row.CanContinue);
        _db.ArtifactOperationBindings.RemoveRange(_db.ArtifactOperationBindings); await _db.SaveChangesAsync();
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [workset.Targets[0] with {AccountId = null}]});
        Assert.IsFalse((await _diagnostics.DiagnoseAsync(task, run, default)).Row.CanContinue);
    }
    [DataTestMethod]
    [DataRow("Tls connection failed")]
    [DataRow("BAIDU_SHARE_UNAVAILABLE")]
    [DataRow("The previous transfer outcome is unknown")]
    public async Task UnrelatedFailuresRemainManual(string error)
    {
        var (task, run, _) = await Seed(error: error);
        Assert.IsFalse((await _diagnostics.DiagnoseAsync(task, run, default)).Row.CanContinue);
    }
    [TestMethod]
    public async Task MissingStorageAndPurchaseNodesAreNeverAutomaticallyContinued()
    {
        var (task, run, _) = await Seed(); Directory.Delete(_root);
        Assert.AreEqual("RECOVERY_STORAGE_UNAVAILABLE", (await _diagnostics.DiagnoseAsync(task, run, default)).Row.ReasonCode);
        Directory.CreateDirectory(_root);
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Unsafe future step", TriggerKind = CloudWorkflow.Trigger,
            Nodes = [new() {NodeId = "transfer", Kind = "baidu.transfer"}, new() {NodeId = "buy", Kind = "post.purchase"}]});
        Assert.AreEqual("RECOVERY_NODE_UNSUPPORTED", (await _diagnostics.DiagnoseAsync(task, run, default)).Row.ReasonCode);
    }
    [DataTestMethod]
    [DataRow("download.enqueueSupported", "{\"reusePolicy\":\"force\"}")]
    [DataRow("fs.publishArtifacts", "{\"reusePolicy\":\"force\"}")]
    [DataRow("download.selectSources", "{}")]
    [DataRow("download.enqueueSupported", "{\"accountId\":\"different\"}")]
    public async Task ContinuationCannotSilentlyChangeLaterSourceOrForceNewSideEffects(string nextKind, string config)
    {
        var (task, run, _) = await Seed();
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Changed future policy", TriggerKind = CloudWorkflow.Trigger,
            Nodes = [new() {NodeId = "transfer", Kind = "baidu.transfer"}, new() {NodeId = "next", Kind = nextKind, ConfigJson = config}]});
        Assert.IsFalse((await _diagnostics.DiagnoseAsync(task, run, default)).Row.CanContinue);
    }
    [TestMethod]
    public async Task ARecognizedFailureCannotHideAnotherTargetsUnknownProblem()
    {
        var (task, run, workset) = await Seed();
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [workset.Targets[0], workset.Targets[0] with
            {Id = "another", Problem = "BAIDU_SHARE_UNAVAILABLE"}]});
        Assert.IsFalse((await _diagnostics.DiagnoseAsync(task, run, default)).Row.CanContinue);
    }
    [DataTestMethod]
    [DataRow("baidu.transfer")]
    [DataRow("download.enqueueSupported")]
    [DataRow("download.awaitCompletion")]
    public async Task PublicationCannotResumeIntoAnUnconstrainedLaterDownload(string laterKind)
    {
        var (task, run, _) = await Seed("fs.publishArtifacts", WorkflowRunStatus.Waiting,
            "An artifact file changed while its manifest was being recorded.");
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Publication before download", TriggerKind = CloudWorkflow.Trigger,
            Nodes = [new() {NodeId = "publish", Kind = "fs.publishArtifacts"}, new() {NodeId = "later", Kind = laterKind}]});
        Assert.AreEqual("RECOVERY_NODE_UNSUPPORTED", (await _diagnostics.DiagnoseAsync(task, run, default)).Row.ReasonCode);
    }
    [TestMethod]
    public async Task FailedPublicationWithoutATypedWaitingAnswerRemainsManual()
    {
        var (task, run, _) = await Seed("fs.publishArtifacts", WorkflowRunStatus.Failed,
            "An artifact file changed while its manifest was being recorded.");
        Assert.IsFalse((await _diagnostics.DiagnoseAsync(task, run, default)).Row.CanContinue);
    }
    [DataTestMethod]
    [DataRow("baidu.transfer", "PUBLICATION_IO_ERROR: The process cannot access the file because it is being used by another process.")]
    [DataRow("download.enqueueSupported", "PUBLICATION_IO_ERROR: The process cannot access the file because it is being used by another process.")]
    [DataRow("download.awaitCompletion", "PUBLICATION_IO_ERROR: The process cannot access the file because it is being used by another process.")]
    [DataRow("baidu.transfer", "LOCAL_STORAGE_RESERVE_LOW")]
    [DataRow("download.enqueueSupported", "LOCAL_STORAGE_RESERVE_LOW")]
    [DataRow("download.awaitCompletion", "LOCAL_STORAGE_RESERVE_LOW")]
    public async Task PublicationIoFailureCannotAuthorizeAnEarlierSideEffect(string kind, string message)
    {
        var (task, run, _) = await Seed(kind, WorkflowRunStatus.Waiting, message);
        run.WaitReason = "artifacts.publication";
        run.WaitPromptJson = "{\"targetId\":\"part\",\"reason\":\"StorageUnavailable\"}";
        Assert.IsFalse((await _diagnostics.DiagnoseAsync(task, run, default)).Row.CanContinue);
        Assert.AreEqual(0, await _db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
    }
    [TestMethod]
    public async Task PreTransferRecoveryRejectsADamagedExistingDownloadRatherThanTreatingItAsFirstDownload()
    {
        var (task, run, workset) = await Seed();
        var download = new DownloadTaskDbModel {DownloadPath = _root, Status = DownloadTaskDbModelStatus.Complete};
        _db.DownloadTasks.Add(download); await _db.SaveChangesAsync();
        _db.CloudDownloadOperations.Add(new() {Id = DownloadArtifactCoordinator.DownloadIntent(workset.Targets[0], _root),
            Kind = "download", State = "succeeded", DownloadTaskId = download.Id, WorkflowRunId = run.Id, TargetId = "part"});
        await _db.SaveChangesAsync();
        Assert.AreEqual("RECOVERY_FILES_UNVERIFIED", (await _diagnostics.DiagnoseAsync(task, run, default)).Row.ReasonCode);
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }
    [DataTestMethod]
    [DataRow("An artifact file changed while its manifest was being recorded.", "PublicationIoError", false)]
    [DataRow("The process cannot access the file because it is being used by another process.", "StorageUnavailable", false)]
    [DataRow("Access to the destination path is denied.", "StorageUnavailable", false)]
    [DataRow("LOCAL_STORAGE_RESERVE_LOW", "StorageUnavailable", true)]
    [DataRow("LOCAL_STORAGE_FULL", "StorageUnavailable", true)]
    [DataRow("LOCAL_STORAGE_ACCESS_DENIED", "StorageUnavailable", true)]
    [DataRow("LOCAL_STORAGE_UNAVAILABLE", "StorageUnavailable", true)]
    [DataRow("LOCAL_STORAGE_WRITE_FAILED", "StorageUnavailable", true)]
    public async Task PublicationRecoveryVerifiesSavedInputAndPreservesOriginalArtifactState(string message, string waitReason, bool typed)
    {
        var (task, run, workset) = await Seed("fs.publishArtifacts", WorkflowRunStatus.Waiting, error: null);
        var file = Path.Combine(_root, "processed.bin"); await File.WriteAllTextAsync(file, "verified processed output");
        var manifest = await ArtifactManifestFiles.CaptureAsync(_root, [file]);
        _db.ArtifactOperations.Add(new() {Id = "processed", IntentKey = "intent", Kind = "files.process", State = ArtifactOperationState.Completed,
            OutputArtifactId = "output", UpdatedAt = DateTime.UtcNow});
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()});
        _db.ArtifactOperationBindings.Add(new() {OwnerKind = "workflow", OwnerId = run.Id.ToString(), Slot = "part:process:final", OperationId = "processed"});
        _db.Set<ArtifactDbModel>().Add(new() {Id = "output", OperationId = "processed", RootPath = _root,
            ManifestJson = JsonSerializer.Serialize(manifest, JsonSerializerOptions.Web), Fingerprint = manifest.Fingerprint,
            State = ArtifactLocationState.Available, UpdatedAt = DateTime.UtcNow});
        await _db.SaveChangesAsync();
        var before = JsonSerializer.Serialize(await _db.Set<ArtifactDbModel>().AsNoTracking().SingleAsync());
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [workset.Targets[0] with
            {ProcessedArtifactId = "output", ProcessingOperationId = "processed", ProcessingIntentKey = "intent",
                PublicationWaitReason = waitReason, Problem = typed ? message : null}]});
        run.WaitReason = "artifacts.publication";
        run.WaitPromptJson = JsonSerializer.Serialize(new {targetId = "part", reason = waitReason,
            message = typed ? message : "PUBLICATION_IO_ERROR: " + message});
        var ready = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsTrue(ready.Row.CanContinue); StringAssert.Contains(ready.SignalJson, "part");
        await File.WriteAllTextAsync(file, "modified input");
        Assert.IsFalse((await _diagnostics.DiagnoseAsync(task, run, default)).Row.CanContinue);
        Assert.AreEqual(before, JsonSerializer.Serialize(await _db.Set<ArtifactDbModel>().AsNoTracking().SingleAsync()),
            "A read-only preview must not use ResolveById and mutate artifact availability.");
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "result")));
        Assert.AreEqual(0, await _db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("Storage guard documentation mentions LOCAL_STORAGE_RESERVE_LOW")]
    [DataRow("LOCAL_STORAGE_RESERVE_LOW unexpected remote explanation")]
    public async Task MentioningAStorageCodeDoesNotAuthorizePublicationRecovery(string message)
    {
        var (task, run, _) = await Seed("fs.publishArtifacts", WorkflowRunStatus.Waiting, error: message);
        run.WaitReason = "artifacts.publication";
        run.WaitPromptJson = "{\"targetId\":\"part\",\"reason\":\"StorageUnavailable\"}";
        var result = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsFalse(result.Row.CanContinue);
        Assert.AreEqual("RECOVERY_MANUAL_REVIEW", result.Row.ReasonCode);
        Assert.AreEqual(0, await _db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
    }
    [TestMethod]
    public async Task CompletedDownloadRequiresExactSavedManifestAndCannotRestartFailedTransport()
    {
        var (task, run, workset) = await Seed("download.awaitCompletion", WorkflowRunStatus.Waiting,
            "Choose a folder inside a mounted storage location. private-path");
        var file = Path.Combine(_root, "official.bin"); await File.WriteAllTextAsync(file, "official bytes");
        var manifest = await ArtifactManifestFiles.CaptureAsync(_root, [file]);
        var download = new DownloadTaskDbModel {DownloadPath = _root, Status = DownloadTaskDbModelStatus.Complete};
        _db.DownloadTasks.Add(download); await _db.SaveChangesAsync();
        _db.DownloadResults.Add(new() {DownloadTaskId = download.Id, DownloadDirectory = _root, Path = _root,
            FilesJson = JsonSerializer.Serialize(new[] {file}), Fingerprint = manifest.Fingerprint});
        await _db.SaveChangesAsync(); run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with
        {Targets = [workset.Targets[0] with {DownloadTaskId = download.Id}]});
        var ready = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsTrue(ready.Row.CanContinue); StringAssert.Contains(ready.SignalJson, "observeExistingOnly");
        await File.WriteAllTextAsync(file, "changed bytes");
        Assert.AreEqual("RECOVERY_FILES_UNVERIFIED", (await _diagnostics.DiagnoseAsync(task, run, default)).Row.ReasonCode);
        download.Status = DownloadTaskDbModelStatus.Failed; await _db.SaveChangesAsync();
        Assert.AreEqual("RECOVERY_DOWNLOAD_REVIEW_REQUIRED", (await _diagnostics.DiagnoseAsync(task, run, default)).Row.ReasonCode);
    }
    [TestMethod]
    public async Task ConfirmationIsFixedVersionSubsetAndRepeatedConfirmationDoesNotRepeatContinuation()
    {
        var (one, _, _) = await Seed(); var (two, _, _) = await Seed();
        var host = new RecordingHost(); var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var preview = await service.PreviewAsync(new([one.Id, two.Id]));
        Assert.AreEqual(2, preview.ContinuableCount);
        var before = await _db.AcquisitionTasks.AsNoTracking().ToArrayAsync();
        var result = await service.ApplyAsync(new(preview.PreviewToken, [one.Id]));
        CollectionAssert.AreEqual(new[] {one.Id}, result.ContinuedTaskIds.ToArray()); Assert.AreEqual(1, host.Calls);
        await service.ApplyAsync(new(preview.PreviewToken, [one.Id])); Assert.AreEqual(1, host.Calls);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.ApplyAsync(new(preview.PreviewToken, [two.Id])));
        var after = await _db.AcquisitionTasks.AsNoTracking().ToArrayAsync();
        Assert.AreEqual("automatic", AcquisitionDownloadContinuationService.Read(after.Single(t => t.Id == one.Id))!.Mode);
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after.Select(t => t.Id == one.Id
            ? t with {BuilderStateJson = before.Single(b => b.Id == one.Id).BuilderStateJson} : t).ToArray()),
            "Only the accepted task's explicit continuation intent may change; business data and unselected tasks stay intact.");
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
    }
    [TestMethod]
    public async Task TaskVersionAndLateTransferOutcomeAreRecheckedInsideRunGate()
    {
        var (one, _, _) = await Seed(); var (two, run, workset) = await Seed();
        var host = new RecordingHost {BeforeValidation = async () =>
        {
            _db.CloudDownloadOperations.Add(new() {Id = CloudOperationService.Key("transfer.v2", DownloadArtifactCoordinator.Source(workset.Targets[0]), "/Bakabase"),
                WorkflowRunId = run.Id, TargetId = "part", Kind = "transfer", State = "unknown", RequestJson = "preserved"});
            await _db.SaveChangesAsync();
        }};
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var preview = await service.PreviewAsync(new([one.Id, two.Id])); one.PlanRevision++; await _db.SaveChangesAsync();
        var result = await service.ApplyAsync(new(preview.PreviewToken, [one.Id, two.Id]));
        Assert.AreEqual(0, result.ContinuedTaskIds.Count); Assert.AreEqual(0, host.Calls);
        CollectionAssert.AreEquivalent(new[] {"ACQUISITION_TASK_CHANGED", "RECOVERY_TRANSFER_UNKNOWN"}, result.Skipped.Select(s => s.ReasonCode).ToArray());
    }
    [TestMethod]
    public async Task UnknownApplyOutcomeRequiresNewPreviewAndCannotBeAutomaticallyReplayed()
    {
        var (task, _, _) = await Seed(); var host = new RecordingHost {ThrowAfterContinuation = true};
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var preview = await service.PreviewAsync(new([task.Id]));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.ApplyAsync(new(preview.PreviewToken, [task.Id])));
        var error = await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.ApplyAsync(new(preview.PreviewToken, [task.Id])));
        Assert.AreEqual("RECOVERY_OUTCOME_UNKNOWN", error.Message); Assert.AreEqual(1, host.Calls);
    }
    [TestMethod]
    public async Task CheapDiagnosisCompletesForAllRowsBeforeAnyDeepFileVerification()
    {
        var (one, _, _) = await Seed(); var (two, _, _) = await Seed();
        var diagnostics = new PhasedDiagnostics(one.Id, two.Id);
        var service = new AcquisitionRecoveryService(_db, diagnostics, new(), new RecordingHost());
        var preview = await service.PreviewAsync(new([one.Id, two.Id]));
        CollectionAssert.AreEqual(new[] {one.Id, two.Id}, diagnostics.CheapIds);
        Assert.AreEqual(2, preview.ContinuableCount);
        Assert.IsFalse(preview.Rows.Any(r => r.ReasonCode == "RECOVERY_VERIFY_FILES"));
    }
    private sealed class PhasedDiagnostics(int deepId, int cheapId) : IAcquisitionRecoveryDiagnostics
    {
        public readonly List<int> CheapIds = [];
        public Task<AcquisitionRecoveryDiagnosis> DiagnoseAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
            CancellationToken ct, bool verifyFiles = true, bool verifyContent = false, bool includeStoppedDownloads = false,
            bool includeStoppedVerification = false, bool deferCompletedDownloadVerification = false)
        {
            if (!verifyFiles) CheapIds.Add(task.Id);
            else CollectionAssert.AreEqual(new[] {deepId, cheapId}, CheapIds,
                "File verification must not consume the preview budget before cheap diagnostics finish.");
            var ready = verifyFiles || task.Id == cheapId;
            return Task.FromResult(new AcquisitionRecoveryDiagnosis(new(task.Id, task.Title, run?.Id, ready,
                ready ? "retry" : null, ready ? "RECOVERY_RUNTIME_SCOPE_FIXED" : "RECOVERY_VERIFY_FILES")));
        }
    }
    private sealed class RecordingHost : IWorkflowRunResumer
    {
        public int Calls; public Func<Task>? BeforeValidation; public bool ThrowAfterContinuation;
        public string? Signal;
        public Task ResumeAsync(int runId, string signalJson, CancellationToken ct = default) => throw new AssertFailedException("Must use guarded continuation");
        public Task RequeueAsync(int runId, CancellationToken ct = default) => throw new AssertFailedException("Must use guarded continuation");
        public Task UpdateWaitAsync(int runId, string reason, string? promptJson, CancellationToken ct = default) => throw new AssertFailedException();
        public async Task ContinueConditionallyAsync(int runId, bool waiting, string signal, Func<CancellationToken, Task> validate, CancellationToken ct = default)
        {
            var gate = WorkflowRunMutationGate.For(runId); await gate.WaitAsync(ct);
            try {if (BeforeValidation != null) await BeforeValidation(); await validate(ct); Signal = signal; Calls++;
                if (ThrowAfterContinuation) throw new InvalidOperationException("Uncertain queue receipt");}
            finally {gate.Release();}
        }
    }
}
