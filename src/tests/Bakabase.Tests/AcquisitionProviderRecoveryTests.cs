using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.CloudDownloads.PikPakCli;
using Bakabase.Tests.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private class RecoverableProvider : ICloudDownloadProvider, ICloudDownloadRecoveryProvider, ICloudDownloadAbandonmentProvider
    {
        public string ProviderId => "pikpak";
        public string? MethodId { get; init; }
        public int Checks;
        public int ReadinessChecks;
        public bool Available = true;
        public bool CanAbandon = true;
        public Task<CloudReadiness> CheckCanAbandonAsync(CloudDownloadRequest request, CancellationToken ct) =>
            Task.FromResult(new CloudReadiness(CanAbandon, CanAbandon ? null : "REMOTE_WRITE_UNCERTAIN"));
        public Func<Task>? DuringProbe;
        public bool CanRevalidateDownloadFailure(string code) => code == "PIKPAK_ORIGINAL_FILE_UNAVAILABLE";
        public Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct)
        {
            ReadinessChecks++;
            return Task.FromResult(new CloudReadiness(true));
        }
        public async Task<CloudReadiness> RevalidateDownloadAsync(CloudDownloadRequest request, CancellationToken ct)
        {Checks++; if (DuringProbe != null) await DuringProbe(); return new CloudReadiness(Available, Available ? null : "PIKPAK_ORIGINAL_FILE_UNAVAILABLE");}
        public Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request, Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
            => throw new AssertFailedException("Diagnosis must never download payloads.");
    }

    private sealed class PreparedRecoveryProvider : RecoverableProvider, ICloudPreparedDownloadRetryProvider
    {
        public bool Prepared = true;
        public bool CanRetryPreparedDownloadFailure(string code) => code == "PIKPAK_CLI_REQUEST_FAILED";
        public Task<CloudReadiness> CheckPreparedDownloadRetryAsync(CloudDownloadRequest request, CancellationToken ct) =>
            Task.FromResult(new CloudReadiness(Prepared));
        public Task<CloudDownloadResult> RetryPreparedDownloadAsync(CloudDownloadRequest request, Func<CloudDownloadProgress, Task> progress,
            CancellationToken ct) => throw new AssertFailedException("Admission never reads payload.");
    }

    private sealed class SealedRecoveryProvider : RecoverableProvider, ICloudPreparedDownloadRetryProvider
    {
        public bool Sealed { get; set; }
        public bool CanHandoffCompletedPreparedDownloadFailure(string failureCode) => failureCode == "PIKPAK_DOWNLOAD_INCOMPLETE";
        public Task<CloudReadiness> CheckCompletedPreparedDownloadAsync(CloudDownloadRequest request, CancellationToken ct) =>
            Task.FromResult(new CloudReadiness(Sealed));
        public Task<CloudReadiness> CheckPreparedDownloadRetryAsync(CloudDownloadRequest request, CancellationToken ct) =>
            throw new AssertFailedException("The reported failure requires a sealed original, not a regular retry.");
        public Task<CloudDownloadResult> RetryPreparedDownloadAsync(CloudDownloadRequest request, Func<CloudDownloadProgress, Task> progress,
            CancellationToken ct) => throw new AssertFailedException("The reported failure cannot download anything.");
    }

    [DataTestMethod]
    [DataRow("workflow", true, false)]
    [DataRow("downloads", true, false)]
    [DataRow("workflow", false, false)]
    [DataRow("workflow", true, true)]
    [DataRow("downloads", true, true)]
    public async Task Incomplete_registration_can_only_confirm_the_exact_sealed_payload(string scope, bool sealedPayload, bool losesSeal)
    {
        var (task, run, workset, download, operation, _, _) =
            await SeedRecoverableProvider(failure: "PIKPAK_DOWNLOAD_INCOMPLETE");
        download.Progress = 100;
        await _db.SaveChangesAsync();
        var provider = new SealedRecoveryProvider {Sealed = sealedPayload, Available = false};
        var storage = new UserStoragePolicy(false);
        var downloads = new CloudDownloadRecoveryService(_db, [provider], storage);
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, storage,
            _services.GetRequiredService<IArtifactOperationStore>(), downloads: downloads);
        var host = new RecordingHost();
        var starter = new OnlyDownloadStarter(this, run.Id);
        var service = new AcquisitionRecoveryService(_db, diagnostics, new(), host,
            downloadOnly: OnlyDownloads(downloads, starter, new()));
        var preview = await service.PreviewAsync(new([task.Id], scope));
        var row = preview.Rows.Single();
        Assert.AreEqual(sealedPayload, row.CanContinue, row.ReasonCode);
        Assert.AreEqual(0, provider.ReadinessChecks);
        Assert.AreEqual(0, provider.Checks);
        if (!sealedPayload) return;
        Assert.AreEqual("RECOVERY_SEALED_DOWNLOAD_HANDOFF_READY", row.ReasonCode);
        if (losesSeal)
        {
            host.BeforeValidation = () => {provider.Sealed = false; return Task.CompletedTask;};
            starter.BetweenLeases = () => {provider.Sealed = false; return Task.CompletedTask;};
        }
        var result = await service.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        Assert.AreEqual(losesSeal ? 0 : 1, result.ContinuedTaskIds.Count);
        if (!losesSeal && scope == "workflow")
        {
            var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(host.Signal!, WorkflowJson.Options)!;
            var confirmed = signal.PreparedDownloadRetries!.Values.Single();
            Assert.IsTrue(confirmed.HandoffOnly);
            Assert.AreEqual(operation.Id, confirmed.OperationId);
        }
        if (!losesSeal && scope == "downloads")
        {
            CollectionAssert.AreEqual(new[] {download.Id}, starter.Calls);
            Assert.IsTrue(starter.HandoffPreparedPayloadOnly);
            Assert.AreEqual(operation.Id, starter.PreparedRetryOperationId);
        }
        if (losesSeal) Assert.AreEqual(0, host.Calls + starter.Calls.Count);
        Assert.AreEqual(0, provider.ReadinessChecks);
        Assert.AreEqual(0, provider.Checks);
        Assert.AreEqual(1, await _db.CloudDownloadOperations.CountAsync());
    }

    [TestMethod]
    [DataRow(true, 0, true)]
    [DataRow(true, 41, true)]
    [DataRow(false, 0, false)]
    [DataRow(false, 41, false)]
    public async Task Unclassified_provider_failure_requires_the_saved_original_even_when_no_files_are_visible(
        bool hasSavedCopy, int reportedProgress, bool canRetry)
    {
        var (_, run, workset, download, _, _, _) = await SeedRecoverableProvider(failure: "PIKPAK_CLI_REQUEST_FAILED");
        download.Progress = reportedProgress;
        await _db.SaveChangesAsync();
        var provider = new PreparedRecoveryProvider {Prepared = hasSavedCopy};
        var recovery = new CloudDownloadRecoveryService(_db, [provider], new UserStoragePolicy(false));
        var target = workset.Targets.Single();
        var result = await recovery.CheckAsync(run.Id, target, workset.OutputDirectory, true, default);
        Assert.AreEqual(canRetry, result.CanRetry);
        Assert.AreEqual(canRetry, result.PreparedRetry != null);
        Assert.AreEqual(canRetry ? 1 : 0, provider.Checks);
    }

    [TestMethod]
    [DataRow("confirmed")]
    [DataRow("became-unproven")]
    [DataRow("another-failure")]
    [DataRow("shared-directory")]
    [DataRow("overlapping-writer")]
    [DataRow("registered-output")]
    public async Task Prepared_download_retry_pins_the_native_failure_and_rechecks_before_start(string scenario)
    {
        var (task, run, workset, download, operation, _, _) = await SeedRecoverableProvider();
        var provider = new PreparedRecoveryProvider();
        var recovery = new CloudDownloadRecoveryService(_db, [provider], new UserStoragePolicy(false));
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, new UserStoragePolicy(false),
            _services.GetRequiredService<IArtifactOperationStore>(), downloads: recovery);
        var starter = new OnlyDownloadStarter(this, run.Id);
        var service = new AcquisitionRecoveryService(_db, diagnostics, new(), new RecordingHost(),
            downloadOnly: OnlyDownloads(recovery, starter, new OnlyDownloadGuard()));
        Directory.CreateDirectory(download.DownloadPath);
        var marker = Path.Combine(download.DownloadPath, ".smbdeleteAAA012344.4");
        await File.WriteAllTextAsync(marker, "system placeholder");
        Assert.IsFalse((await recovery.CheckNoOutputForSourceRevisionAsync(run.Id, workset.Targets.Single(),
            workset.OutputDirectory, default)).Candidate);
        var preview = await service.PreviewAsync(new([task.Id], "downloads"));
        Assert.AreEqual("RECOVERY_PREPARED_DOWNLOAD_REVALIDATED", preview.Rows.Single().ReasonCode);
        var action = await diagnostics.DiagnoseAsync(task, run, default);
        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(action.SignalJson, JsonSerializerOptions.Web)!;
        Assert.AreEqual(operation.Id, signal.PreparedDownloadRetries![workset.Targets.Single().Id].OperationId);
        switch (scenario)
        {
            case "became-unproven":
                starter.BetweenLeases = () => { provider.Prepared = false; return Task.CompletedTask; };
                break;
            case "another-failure":
                download.DownloadStatusUpdateDt = download.DownloadStatusUpdateDt.AddSeconds(1);
                break;
            case "shared-directory":
                _db.DownloadTasks.Add(new() {DownloadPath = download.DownloadPath});
                break;
            case "overlapping-writer":
                _db.DownloadTasks.Add(new() {DownloadPath = Path.Combine(download.DownloadPath, "other"), Status = DownloadTaskDbModelStatus.InProgress});
                break;
            case "registered-output":
                _db.DownloadTaskFiles.Add(new() {DownloadTaskId = download.Id, Path = Path.Combine(download.DownloadPath, "original")});
                break;
        }
        await _db.SaveChangesAsync();
        var result = await service.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        Assert.AreEqual(scenario == "confirmed" ? 1 : 0, result.ContinuedTaskIds.Count);
        if (scenario == "confirmed")
        {
            CollectionAssert.AreEqual(new[] {download.Id}, starter.Calls);
            Assert.AreEqual(operation.Id, starter.PreparedRetryOperationId);
        }
        else
        {
            Assert.AreEqual(0, starter.Calls.Count);
            Assert.AreEqual(DownloadTaskDbModelStatus.Failed,
                (await _db.DownloadTasks.AsNoTracking().SingleAsync(d => d.Id == download.Id)).Status);
        }
        Assert.AreEqual("system placeholder", await File.ReadAllTextAsync(marker));
        Assert.AreEqual(1, await _db.CloudDownloadOperations.CountAsync());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Workflow_does_not_dispatch_a_saved_copy_via_an_older_or_different_confirmation(bool stale)
    {
        var (_, run, workset, _, operation, _, _) = await SeedRecoverableProvider();
        var target = workset.Targets.Single();
        var recovery = new CloudDownloadRecoveryService(_db, [new PreparedRecoveryProvider()], new UserStoragePolicy(false));
        using var services = new ServiceCollection().AddSingleton(recovery).BuildServiceProvider();
        var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = "{}", Services = services, Logger = NullLogger.Instance};
        var signal = new CloudSelectionSignal {ObserveExistingOnly = true, RevalidateExistingDownloads = true,
            ResumeTargetIds = [target.Id], PreparedDownloadRetries = stale ? new() {{target.Id, new(operation.Id, 1)}} : null};
        var outcome = await new AwaitCloudDownloadsActivity().ResumeAsync(context, workset, JsonSerializer.Serialize(signal, WorkflowJson.Options), default);
        Assert.AreEqual("RECOVERY_DOWNLOAD_IDENTITY_CHANGED", ((DownloadWorkset)outcome.Suspension!.Item!).Targets.Single().Problem);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
    }

    private async Task<(AcquisitionTaskDbModel Task, WorkflowRunDbModel Run, DownloadWorkset Workset,
        DownloadTaskDbModel Download, CloudOperationDbModel Operation, RecoverableProvider Provider,
        CloudDownloadRecoveryService Recovery)> SeedRecoverableProvider(CloudSource? testSource = null, string failure = "PIKPAK_ORIGINAL_FILE_UNAVAILABLE")
    {
        var (task, run, workset) = await Seed("download.awaitCompletion", WorkflowRunStatus.Waiting, error: null);
        var source = testSource ?? new CloudSource {Id = "source", ProviderId = "pikpak", Url = "https://mypikpak.com/s/fixture", Kind = "share"};
        var target = workset.Targets.Single() with {Sources = [source], AccountId = null, Problem = failure};
        var intent = DownloadArtifactCoordinator.DownloadIntent(target, _root);
        var request = new CloudDownloadRequest(intent, source, source.AccountId, null, Path.Combine(_root, "cloud-original"));
        var download = new DownloadTaskDbModel {Status = DownloadTaskDbModelStatus.Failed, Message = failure,
            ThirdPartyId = CloudSites.Site(source.ProviderId), DownloadPath = request.OutputDirectory,
            Options = JsonSerializer.Serialize(request, JsonSerializerOptions.Web)};
        _db.DownloadTasks.Add(download); await _db.SaveChangesAsync();
        var operation = new CloudOperationDbModel {Id = intent, TargetId = target.Id, WorkflowRunId = run.Id,
            Kind = "download", State = "started", DownloadTaskId = download.Id, RequestJson = download.Options};
        _db.CloudDownloadOperations.Add(operation);
        _db.ArtifactOperationBindings.Add(new() {Id = "original-download", OwnerKind = "workflow", OwnerId = run.Id.ToString(),
            Slot = target.Id + ":download", OperationId = intent});
        workset = workset with {NoRepeatExistingDownloads = true, Targets = [target with {DownloadTaskId = download.Id}]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        await _db.SaveChangesAsync();
        var provider = new RecoverableProvider();
        var recovery = new CloudDownloadRecoveryService(_db, [provider], new UserStoragePolicy(false));
        _diagnostics = new(_db, new UserStoragePolicy(false), _services.GetRequiredService<IArtifactOperationStore>(), downloads: recovery);
        return (task, run, workset, download, operation, provider, recovery);
    }

    [TestMethod]
    public async Task Provider_recovery_rechecks_readonly_and_authorizes_only_the_original_target_without_removing_no_repeat()
    {
        var (task, run, workset, download, operation, provider, _) = await SeedRecoverableProvider();
        var beforeTask = JsonSerializer.Serialize(task); var beforeRun = JsonSerializer.Serialize(run);
        var beforeOperation = JsonSerializer.Serialize(operation); var beforeDownload = JsonSerializer.Serialize(download);
        var cheap = await _diagnostics.DiagnoseAsync(task, run, default, verifyFiles: false);
        Assert.AreEqual("RECOVERY_RECHECK_SOURCE", cheap.Row.ReasonCode); Assert.AreEqual(0, provider.Checks);
        var result = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsTrue(result.Row.CanContinue); Assert.AreEqual(1, provider.Checks);
        Assert.AreEqual(1, provider.ReadinessChecks);
        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(result.SignalJson, JsonSerializerOptions.Web)!;
        CollectionAssert.AreEqual(new[] {workset.Targets.Single().Id}, signal.ResumeTargetIds!);
        Assert.IsTrue(signal.ObserveExistingOnly == true); Assert.IsTrue(signal.RevalidateExistingDownloads == true);
        Assert.IsTrue(workset.NoRepeatExistingDownloads == true);
        Assert.AreEqual(beforeTask, JsonSerializer.Serialize(task)); Assert.AreEqual(beforeRun, JsonSerializer.Serialize(run));
        Assert.AreEqual(beforeOperation, JsonSerializer.Serialize(operation)); Assert.AreEqual(beforeDownload, JsonSerializer.Serialize(download));
        Assert.IsFalse(Directory.Exists(download.DownloadPath)); Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("workflow", "resume")]
    [DataRow("downloads", "downloads")]
    public async Task PikPak_original_download_preview_reads_fresh_account_status_once_and_reports_cloud_capacity(
        string scope, string expectedAction)
    {
        var native = new PikPakCliTests.Native();
        var cliRoot = Path.Combine(_root, "pikpak");
        var accounts = new PikPakCliAccountService(cliRoot, native);
        async Task<PikPakCliAccountViewModel> AuthorizeAccount()
        {
            var session = await accounts.StartAsync(default);
            native.Approved = true;
            Assert.AreEqual("authorized", (await accounts.PollAsync(session.Id, default)).Status);
            return await accounts.GetAsync(default);
        }

        var account = await AuthorizeAccount();
        var source = new CloudSource {Id = "source", ProviderId = "pikpak", MethodId = "pikpak.share",
            Url = "https://mypikpak.com/s/fixture", Kind = "share",
            AccountId = account.AccountId, AccountVersion = account.AccountVersion};
        var (task, run, _, download, operation, _, _) = await SeedRecoverableProvider(source);
        var storage = new UserStoragePolicy(false);
        var provider = new PikPakCliCloudProvider(accounts, storage);
        var downloadRecovery = new CloudDownloadRecoveryService(_db, [provider], storage);
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, storage,
            _services.GetRequiredService<IArtifactOperationStore>(), downloads: downloadRecovery);
        var service = new AcquisitionRecoveryService(_db, diagnostics, new(), new RecordingHost());
        var before = JsonSerializer.Serialize(new {task, run, download, operation});
        async Task<AcquisitionRecoveryRow> Preview() =>
            (await service.PreviewAsync(new([task.Id], scope))).Rows.Single();

        native.Storage = """{"used":"3 B","total":"4 B"}""";
        native.Calls.Clear();
        var waiting = await Preview();
        Assert.IsTrue(waiting.CanContinue, waiting.ReasonCode);
        Assert.AreEqual("RECOVERY_WAIT_FOR_CLOUD_STORAGE", waiting.ReasonCode);
        Assert.AreEqual(expectedAction, waiting.Action);
        CollectionAssert.AreEqual(new[] {"auth status", "share get", "share get"},
            native.Calls.Select(command => string.Join(' ', command.Take(2))).ToArray());

        native.Storage = """{"used":"0 B","total":"2 B"}""";
        var oversized = await Preview();
        Assert.IsFalse(oversized.CanContinue);
        Assert.AreEqual("RECOVERY_SOURCE_STILL_BLOCKED", oversized.ReasonCode);
        Assert.AreEqual("PIKPAK_STORAGE_QUOTA_EXCEEDED", oversized.CurrentProblemCode);

        native.Storage = """{"used":"0 B","total":"4 B"}""";
        native.Calls.Clear();
        var ready = await Preview();
        Assert.IsTrue(ready.CanContinue, ready.ReasonCode);
        Assert.AreEqual(expectedAction, ready.Action);
        Assert.AreEqual(1, native.Calls.Count(command => command.SequenceEqual(new[] {"auth", "status"})));

        native.Approved = false;
        native.Calls.Clear();
        var offline = await Preview();
        Assert.IsFalse(offline.CanContinue);
        Assert.AreEqual("PIKPAK_ACCOUNT_REQUIRED", offline.CurrentProblemCode);
        CollectionAssert.AreEqual(new[] {"auth status"},
            native.Calls.Select(command => string.Join(' ', command.Take(2))).ToArray());

        var replacement = await AuthorizeAccount();
        Assert.AreNotEqual(account.AccountVersion, replacement.AccountVersion);
        native.Calls.Clear();
        var replaced = await Preview();
        Assert.IsFalse(replaced.CanContinue);
        Assert.AreEqual("PIKPAK_ACCOUNT_CHANGED", replaced.CurrentProblemCode);
        Assert.AreEqual(0, native.Calls.Count);

        Assert.AreEqual(0, native.Mkdirs);
        Assert.AreEqual(0, native.Restores);
        Assert.AreEqual(0, native.Downloads);
        Assert.IsFalse(Directory.Exists(Path.Combine(cliRoot, "operations")));
        Assert.AreEqual(before, JsonSerializer.Serialize(new {task, run, download, operation}));
    }

    [TestMethod]
    public async Task Provider_recovery_allows_empty_staging_directories_but_never_partial_files()
    {
        var (task, run, _, download, _, provider, _) = await SeedRecoverableProvider();
        var staging = Path.Combine(download.DownloadPath, ".bakabase-fixture", "nested");
        Directory.CreateDirectory(staging);
        Assert.IsTrue((await _diagnostics.DiagnoseAsync(task, run, default)).Row.CanContinue);
        Assert.AreEqual(1, provider.Checks);
        await File.WriteAllTextAsync(Path.Combine(staging, "unfinished.part"), "retain");
        Assert.AreEqual("RECOVERY_DOWNLOAD_HAS_OUTPUT", (await _diagnostics.DiagnoseAsync(task, run, default)).Row.ReasonCode);
        Assert.AreEqual(1, provider.Checks);
        Assert.AreEqual("retain", await File.ReadAllTextAsync(Path.Combine(staging, "unfinished.part")));
    }

    [DataTestMethod]
    [DataRow("unknown-failure")]
    [DataRow("unknown-operation")]
    [DataRow("unknown-transfer")]
    [DataRow("changed-source")]
    [DataRow("changed-options")]
    [DataRow("retained-file")]
    [DataRow("download-progress")]
    [DataRow("download-result")]
    [DataRow("checkpoint-output")]
    public async Task Provider_recovery_rejects_unknown_outcomes_changed_requests_and_retained_content(string mode)
    {
        var (task, run, workset, download, operation, provider, _) = await SeedRecoverableProvider();
        switch (mode)
        {
            case "unknown-failure": download.Message = "Unknown connection failure"; break;
            case "unknown-operation": operation.State = "unknown"; break;
            case "unknown-transfer":
                _db.CloudDownloadOperations.Add(new() {Id = "unknown-transfer", Kind = "transfer", State = "unknown",
                    WorkflowRunId = run.Id, TargetId = workset.Targets.Single().Id}); break;
            case "changed-source":
                var target = workset.Targets.Single();
                run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [target with
                    {Sources = [target.SelectedSource! with {Url = "https://mypikpak.com/s/other"}]}]}); break;
            case "changed-options": download.Options = "{}"; break;
            case "retained-file": Directory.CreateDirectory(download.DownloadPath);
                await File.WriteAllTextAsync(Path.Combine(download.DownloadPath, "official.bin"), "retained original bytes"); break;
            case "download-progress": download.Progress = 1; break;
            case "download-result": _db.DownloadResults.Add(new() {DownloadTaskId = download.Id, FilesJson = "[]"}); break;
            case "checkpoint-output": run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets =
                [workset.Targets.Single() with {DownloadArtifactId = "original-artifact"}]}); break;
        }
        await _db.SaveChangesAsync();
        var result = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsFalse(result.Row.CanContinue); Assert.AreEqual(0, provider.Checks);
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        if (mode == "retained-file") Assert.AreEqual("retained original bytes", await File.ReadAllTextAsync(Path.Combine(download.DownloadPath, "official.bin")));
    }

    [TestMethod]
    public async Task Provider_recovery_requires_current_access_and_rechecks_before_confirmation()
    {
        var (task, run, _, _, _, provider, _) = await SeedRecoverableProvider();
        provider.Available = false;
        var blocked = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsFalse(blocked.Row.CanContinue); Assert.AreEqual("RECOVERY_SOURCE_STILL_BLOCKED", blocked.Row.ReasonCode);
        Assert.AreEqual("PIKPAK_ORIGINAL_FILE_UNAVAILABLE", blocked.Row.CurrentProblemCode);
        provider.Available = true;
        var host = new RecordingHost {BeforeValidation = () => {provider.Available = false; return Task.CompletedTask;}};
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var preview = await service.PreviewAsync(new([task.Id])); Assert.AreEqual(1, preview.ContinuableCount);
        var result = await service.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        Assert.AreEqual(0, host.Calls); Assert.AreEqual("RECOVERY_SOURCE_STILL_BLOCKED", result.Skipped.Single().ReasonCode);
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Provider_recovery_revalidates_again_when_the_original_workflow_resumes()
    {
        var (_, run, workset, download, _, provider, recovery) = await SeedRecoverableProvider();
        provider.Available = false;
        using var services = new ServiceCollection().AddSingleton(recovery).BuildServiceProvider();
        var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = "{}",
            Services = services, Logger = NullLogger.Instance};
        var signal = JsonSerializer.Serialize(new CloudSelectionSignal {ObserveExistingOnly = true,
            ResumeTargetIds = [workset.Targets.Single().Id], RevalidateExistingDownloads = true}, JsonSerializerOptions.Web);
        var outcome = await new AwaitCloudDownloadsActivity().ResumeAsync(context, workset, signal, default);
        Assert.IsNotNull(outcome.Suspension); Assert.AreEqual(1, provider.Checks);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await _db.DownloadTasks.AsNoTracking().SingleAsync(d => d.Id == download.Id)).Status);
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(1, await _db.CloudDownloadOperations.CountAsync());
    }

    [TestMethod]
    public async Task Provider_recovery_selects_only_certified_target_and_preserves_completed_and_unknown_siblings()
    {
        var (task, run, workset, _, _, provider, recovery) = await SeedRecoverableProvider();
        var original = workset.Targets.Single();
        var unknownDownload = new DownloadTaskDbModel {DownloadPath = Path.Combine(_root, "unknown"),
            Status = DownloadTaskDbModelStatus.Failed, Message = "unknown failure"};
        _db.DownloadTasks.Add(unknownDownload); await _db.SaveChangesAsync();
        var unknown = original with {Id = "unknown", DownloadTaskId = unknownDownload.Id, Problem = "unknown failure"};
        var completed = original with {Id = "complete", DownloadTaskId = null, Problem = null,
            PublishedArtifactId = "retained-artifact", PublishedFiles = ["retained-file"]};
        workset = workset with {Targets = [unknown, original, completed]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset); await _db.SaveChangesAsync();
        var diagnosis = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsTrue(diagnosis.Row.CanContinue);
        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(diagnosis.SignalJson, JsonSerializerOptions.Web)!;
        CollectionAssert.AreEqual(new[] {original.Id}, signal.ResumeTargetIds!);
        provider.Available = false;
        using var services = new ServiceCollection().AddSingleton(recovery).BuildServiceProvider();
        var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = "{}", Services = services, Logger = NullLogger.Instance};
        var outcome = await new AwaitCloudDownloadsActivity().ResumeAsync(context, workset, diagnosis.SignalJson, default);
        Assert.IsNotNull(outcome.Suspension);
        var saved = (DownloadWorkset)outcome.Suspension.Item!;
        Assert.AreSame(unknown, saved.Targets[0]); Assert.AreSame(completed, saved.Targets[2]);
        Assert.IsTrue(saved.NoRepeatExistingDownloads == true);
        Assert.AreEqual(2, await _db.DownloadTasks.CountAsync()); Assert.AreEqual(1, await _db.CloudDownloadOperations.CountAsync());
    }

    [DataTestMethod]
    [DataRow("file")]
    [DataRow("options")]
    [DataRow("result")]
    public async Task Provider_probe_cannot_certify_a_local_snapshot_changed_during_network_access(string change)
    {
        var (task, run, _, download, _, provider, _) = await SeedRecoverableProvider();
        provider.DuringProbe = async () =>
        {
            if (change == "file")
            {
                Directory.CreateDirectory(download.DownloadPath);
                await File.WriteAllTextAsync(Path.Combine(download.DownloadPath, "partial.bin"), "keep");
            }
            else if (change == "options") download.Options = "{}";
            else _db.DownloadResults.Add(new() {DownloadTaskId = download.Id, FilesJson = "[]"});
            await _db.SaveChangesAsync();
        };
        var result = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsFalse(result.Row.CanContinue); Assert.AreEqual(1, provider.Checks);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
    }

    [TestMethod]
    public async Task Conditional_download_start_validates_under_the_same_action_lock_and_leaves_failed_guard_unchanged()
    {
        var (_, run, workset, download, _, _, recovery) = await SeedRecoverableProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherEdit = _services.GetRequiredService<DownloaderManager>().WithTaskActionLock(download.Id, async () =>
        {
            entered.SetResult(); await release.Task;
            download.Options = "{}"; await _db.SaveChangesAsync(); return true;
        });
        await entered.Task;
        var checkedAfterEdit = false;
        var conditional = _services.GetRequiredService<DownloadTaskService>().StartConditionally(download.Id, async ct =>
        {
            checkedAfterEdit = true;
            var local = await recovery.CheckAsync(run.Id, workset.Targets.Single(), workset.OutputDirectory, false, ct);
            if (!local.Candidate) throw new InvalidOperationException(local.ReasonCode);
        });
        Assert.IsFalse(checkedAfterEdit, "The final guard must wait for an existing task edit to complete.");
        release.SetResult(); await otherEdit;
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => conditional);
        Assert.IsTrue(checkedAfterEdit);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }
}
