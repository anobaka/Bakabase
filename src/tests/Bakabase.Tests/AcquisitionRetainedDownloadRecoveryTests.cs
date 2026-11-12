using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private sealed class RetainedProvider : ICloudDownloadProvider, ICloudRetainedDownloadRecoveryProvider, ICloudExistingDownloadVerificationProvider
    {
        public string ProviderId => "baidu";
        public List<bool> Checks = [];
        public string? Problem;
        public Func<Task>? DuringRemote;
        public int RetainedInspectionCalls;
        public bool CanVerifyExistingDownloadFailure(string code) => code is
            "BAIDU_OUTPUT_FILE_READ_FAILED" or "BAIDU_LOCAL_FILE_CHANGED" or "BAIDU_DOWNLOAD_CHECKSUM_MISMATCH";
        public bool CanRevalidateRetainedDownloadFailure(string code) => code is "LOCAL_STORAGE_FULL" or "LOCAL_STORAGE_UNAVAILABLE" or "BAIDU_OUTPUT_DIRECTORY_FAILED" or
            "BAIDU_OUTPUT_FILE_INVALID" or "BAIDU_LOCAL_PATH_TOO_LONG" or "BAIDU_OUTPUT_FILE_READ_FAILED";
        public Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct) => throw new AssertFailedException("Use read-only pinned inspection.");
        public async Task<CloudReadiness> InspectRetainedDownloadAsync(CloudDownloadRequest request, bool verifyRemote, CancellationToken ct)
        {
            RetainedInspectionCalls++;
            Checks.Add(verifyRemote);
            if (verifyRemote && DuringRemote != null) await DuringRemote();
            return new(Problem == null, Problem);
        }
        public Task<CloudReadiness> InspectExistingDownloadAsync(CloudDownloadRequest request, CancellationToken ct)
        { Checks.Add(false); return Task.FromResult(new CloudReadiness(Problem == null, Problem)); }
        public CloudExistingDownloadVerification? ExistingProof;
        public string? VerificationFailure;
        public int VerificationCalls;
        public Task<CloudExistingDownloadVerification> VerifyExistingDownloadAsync(CloudDownloadRequest request, CancellationToken ct)
        {
            VerificationCalls++;
            if (VerificationFailure != null) throw new InvalidOperationException(VerificationFailure);
            return Task.FromResult(ExistingProof ?? throw new AssertFailedException("Preview must not perform content verification."));
        }
        public Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request, Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
            => throw new AssertFailedException("Recovery inspection must never download.");
        public Task<CloudTransferReceipt> TransferAsync(CloudTransferRequest request, CancellationToken ct)
            => throw new AssertFailedException("Recovery inspection must never transfer.");
    }

    private async Task<(WorkflowRunDbModel Run, CloudDownloadTarget Target, DownloadTaskDbModel Download,
        CloudOperationDbModel DownloadOperation, CloudOperationDbModel Transfer, RetainedProvider Provider,
        CloudDownloadRecoveryService Service)> SeedRetainedDownload()
    {
        var (_, run, workset) = await Seed("download.awaitCompletion", WorkflowRunStatus.Waiting, error: null);
        var target = workset.Targets[0] with {RemoteEntry = new("baidu", "account", "/saved/transfer",
            "[{\"id\":1,\"path\":\"/saved/transfer/fixture\",\"size\":4,\"isDirectory\":false}]")};
        var intent = DownloadArtifactCoordinator.DownloadIntent(target, _root);
        var request = new CloudDownloadRequest(intent, target.SelectedSource!, "account", target.RemoteEntry,
            Path.Combine(_root, "original-download"));
        var download = new DownloadTaskDbModel {ThirdPartyId = CloudSites.Site("baidu"), Status = DownloadTaskDbModelStatus.Failed,
            Message = "LOCAL_STORAGE_FULL", Progress = 25, DownloadPath = request.OutputDirectory,
            Options = JsonSerializer.Serialize(request, JsonSerializerOptions.Web)};
        _db.DownloadTasks.Add(download); await _db.SaveChangesAsync();
        var operation = new CloudOperationDbModel {Id = intent, Kind = "download", State = "started", DownloadTaskId = download.Id,
            WorkflowRunId = run.Id, TargetId = target.Id, RequestJson = download.Options};
        var transfer = new CloudOperationDbModel {Id = "transfer", Kind = "transfer", State = "succeeded", WorkflowRunId = run.Id,
            TargetId = target.Id, RequestJson = JsonSerializer.Serialize(new CloudTransferRequest("transfer", target.SelectedSource!,
                "account", "/saved"), JsonSerializerOptions.Web),
            ReceiptJson = JsonSerializer.Serialize(new CloudTransferReceipt("transfer", target.RemoteEntry), JsonSerializerOptions.Web)};
        _db.CloudDownloadOperations.AddRange(operation, transfer);
        _db.ArtifactOperationBindings.AddRange(new() {Id = "download-binding", OwnerKind = "workflow", OwnerId = run.Id.ToString(),
            Slot = CloudOperationService.OperationSlot(target, "download"), OperationId = intent},
            new() {Id = "transfer-binding", OwnerKind = "workflow", OwnerId = run.Id.ToString(),
                Slot = CloudOperationService.OperationSlot(target, "transfer"), OperationId = transfer.Id});
        await _db.SaveChangesAsync();
        target = target with {DownloadTaskId = download.Id, DownloadOperationId = intent};
        Directory.CreateDirectory(request.OutputDirectory);
        await File.WriteAllTextAsync(Path.Combine(request.OutputDirectory, "retained-fixture"), "bytes");
        var provider = new RetainedProvider();
        return (run, target, download, operation, transfer, provider, new(_db, [provider], new UserStoragePolicy(false)));
    }

    [TestMethod]
    public async Task Retained_recovery_preserves_original_ids_bytes_and_receipts_and_revalidates_under_action_guard()
    {
        var (run, target, download, operation, transfer, provider, service) = await SeedRetainedDownload();
        var before = JsonSerializer.Serialize(new {download, operation, transfer});
        var transferRequest = JsonSerializer.Deserialize<CloudTransferRequest>(transfer.RequestJson, JsonSerializerOptions.Web)!;
        var transferReceipt = JsonSerializer.Deserialize<CloudTransferReceipt>(transfer.ReceiptJson!, JsonSerializerOptions.Web)!;
        Assert.AreNotEqual(transferRequest.RemoteDirectory, transferReceipt.Entry.Path,
            "A provider-owned child directory must be accepted without rewriting the frozen receipt.");
        var preview = await service.CheckAsync(run.Id, target, _root, true, default);
        Assert.IsTrue(preview.CanRetry, preview.ReasonCode); Assert.IsTrue(preview.Candidate);
        CollectionAssert.AreEqual(new[] {true, false}, provider.Checks);
        var locked = await service.CheckAsync(run.Id, target, _root, false, default);
        Assert.IsFalse(locked.CanRetry); Assert.IsTrue(locked.Candidate);
        Assert.AreEqual(before, JsonSerializer.Serialize(new {download, operation, transfer}));
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual("bytes", await File.ReadAllTextAsync(Path.Combine(download.DownloadPath, "retained-fixture")));
    }

    [TestMethod]
    public async Task Retained_legacy_mount_failure_uses_the_original_target_and_preserves_the_historical_error()
    {
        var (run, target, download, operation, transfer, provider, service) = await SeedRetainedDownload();
        download.Message = "Choose a folder inside a mounted storage location. " + download.DownloadPath;
        target = target with {Problem = download.Message};
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)! with {Targets = [target]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        await _db.SaveChangesAsync();
        var task = await _db.AcquisitionTasks.SingleAsync(t => t.WorkflowRunId == run.Id);
        var before = JsonSerializer.Serialize(new {task, run, download, operation, transfer});
        _diagnostics = new(_db, new UserStoragePolicy(false), _services.GetRequiredService<IArtifactOperationStore>(), downloads: service);

        var cheap = await _diagnostics.DiagnoseAsync(task, run, default, verifyFiles: false);
        Assert.AreEqual("RECOVERY_RECHECK_SOURCE", cheap.Row.ReasonCode);
        CollectionAssert.AreEqual(new[] {false}, provider.Checks);
        provider.Checks.Clear();
        var result = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsTrue(result.Row.CanContinue, result.Row.ReasonCode);
        Assert.AreEqual("RECOVERY_ORIGINAL_DOWNLOAD_REVALIDATED", result.Row.ReasonCode);
        CollectionAssert.AreEqual(new[] {true, false}, provider.Checks);
        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(result.SignalJson, JsonSerializerOptions.Web)!;
        CollectionAssert.AreEqual(new[] {target.Id}, signal.ResumeTargetIds!);
        Assert.IsTrue(signal.ObserveExistingOnly == true); Assert.IsTrue(signal.RevalidateExistingDownloads == true);
        Assert.AreEqual(before, JsonSerializer.Serialize(new {task, run, download, operation, transfer}));
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual("bytes", await File.ReadAllTextAsync(Path.Combine(download.DownloadPath, "retained-fixture")));
    }

    [DataTestMethod]
    [DataRow("provider-prefix")]
    [DataRow("quoted")]
    [DataRow("different-path")]
    [DataRow("path-prefix")]
    [DataRow("trailing-text")]
    [DataRow("new-line")]
    [DataRow("missing-path")]
    [DataRow("different-case")]
    public async Task Retained_legacy_mount_compatibility_rejects_provider_text_or_another_destination(string mode)
    {
        var (run, target, download, _, _, provider, service) = await SeedRetainedDownload();
        const string prefix = "Choose a folder inside a mounted storage location. ";
        var exact = prefix + download.DownloadPath;
        download.Message = mode switch
        {
            "provider-prefix" => "Provider response: " + exact,
            "quoted" => "\"" + exact + "\"",
            "different-path" => prefix + Path.Combine(_root, "another-download"),
            "path-prefix" => exact + "-other",
            "trailing-text" => exact + " retry now",
            "new-line" => exact + "\n",
            "missing-path" => prefix.TrimEnd(),
            "different-case" => exact.ToUpperInvariant(),
            _ => throw new AssertFailedException("Unknown fixture")
        };
        await _db.SaveChangesAsync();
        var result = await service.CheckAsync(run.Id, target, _root, true, default);
        Assert.IsFalse(result.CanRetry); Assert.IsFalse(result.Candidate); Assert.AreEqual(0, provider.Checks.Count);
    }

    [DataTestMethod]
    [DataRow("unknown-transfer")]
    [DataRow("shared-download")]
    [DataRow("registered-output")]
    public async Task Retained_legacy_mount_compatibility_never_bypasses_original_ownership_and_receipts(string mode)
    {
        var (run, target, download, operation, transfer, provider, service) = await SeedRetainedDownload();
        download.Message = "Choose a folder inside a mounted storage location. " + download.DownloadPath;
        switch (mode)
        {
            case "unknown-transfer": transfer.State = "unknown"; break;
            case "shared-download": _db.ArtifactOperationBindings.Add(new() {Id = "other", OwnerKind = "workflow",
                OwnerId = "other-run", Slot = "other:download", OperationId = operation.Id}); break;
            case "registered-output": _db.DownloadResults.Add(new() {DownloadTaskId = download.Id, FilesJson = "[]"}); break;
        }
        await _db.SaveChangesAsync();
        var result = await service.CheckAsync(run.Id, target, _root, true, default);
        Assert.IsFalse(result.CanRetry); Assert.IsFalse(result.Candidate); Assert.AreEqual(0, provider.Checks.Count);
    }

    [TestMethod]
    public async Task Legacy_mount_compatibility_does_not_opt_in_a_provider_without_retained_recovery()
    {
        var (_, run, workset, download, _, provider, service) = await SeedRecoverableProvider();
        download.Message = "Choose a folder inside a mounted storage location. " + download.DownloadPath;
        await _db.SaveChangesAsync();
        var result = await service.CheckAsync(run.Id, workset.Targets.Single(), _root, true, default);
        Assert.IsFalse(result.CanRetry); Assert.IsFalse(result.Candidate); Assert.AreEqual(0, provider.Checks);
    }

    [DataTestMethod]
    [DataRow("unknown-transfer")]
    [DataRow("remote-entry")]
    [DataRow("receipt-manifest")]
    [DataRow("account")]
    [DataRow("source-url")]
    [DataRow("parameters")]
    [DataRow("shared-download")]
    [DataRow("shared-transfer")]
    [DataRow("same-directory")]
    [DataRow("registered-output")]
    [DataRow("source-correction")]
    [DataRow("completed")]
    [DataRow("checkpoint")]
    public async Task Retained_recovery_rejects_changed_identity_outputs_or_additional_owners_before_inspection(string mode)
    {
        var (run, target, download, operation, transfer, provider, service) = await SeedRetainedDownload();
        switch (mode)
        {
            case "unknown-transfer": transfer.State = "unknown"; break;
            case "remote-entry": target = target with {RemoteEntry = target.RemoteEntry! with {Path = "/different"}}; break;
            case "receipt-manifest": transfer.ReceiptJson = JsonSerializer.Serialize(new CloudTransferReceipt("transfer", target.RemoteEntry! with {ManifestJson = "[]"}), JsonSerializerOptions.Web); break;
            case "account": target = target with {AccountId = "different"}; break;
            case "source-url": target = target with {Sources = [target.SelectedSource! with {Url = "https://pan.baidu.com/s/other"}]}; break;
            case "parameters": download.Options = "{}"; break;
            case "shared-download":
            case "shared-transfer": _db.ArtifactOperationBindings.Add(new() {Id = "other", OwnerKind = "workflow", OwnerId = "other-run", Slot = "other:download", OperationId = mode == "shared-download" ? operation.Id : transfer.Id}); break;
            case "same-directory": _db.DownloadTasks.Add(new() {DownloadPath = download.DownloadPath}); break;
            case "registered-output": _db.DownloadResults.Add(new() {DownloadTaskId = download.Id, FilesJson = "[]"}); break;
            case "source-correction": target = target with {Sources = [target.SelectedSource! with {AccessCodeRevisions = [new() {CorrectionId = "change", ExpectedSourceIdentity = "old", Basis = "manual"}]}]}; break;
            case "completed": download.CompletedAt = DateTime.Now; break;
            case "checkpoint": download.Checkpoint = "foreign persisted checkpoint"; break;
        }
        await _db.SaveChangesAsync();
        var result = await service.CheckAsync(run.Id, target, _root, true, default);
        Assert.IsFalse(result.CanRetry, mode); Assert.IsFalse(result.Candidate, mode); Assert.AreEqual(0, provider.Checks.Count, mode);
    }

    [TestMethod]
    public async Task Retained_recovery_rejects_probe_failure_and_racing_operation_changes()
    {
        var (run, target, _, operation, _, provider, service) = await SeedRetainedDownload();
        provider.Problem = "BAIDU_DOWNLOAD_PARTIAL_UNVERIFIED";
        var blocked = await service.CheckAsync(run.Id, target, _root, true, default);
        Assert.IsFalse(blocked.CanRetry); Assert.AreEqual(provider.Problem, blocked.FailureCode);
        provider.Problem = null;
        provider.DuringRemote = async () => {operation.State = "unknown"; await _db.SaveChangesAsync();};
        Assert.IsFalse((await service.CheckAsync(run.Id, target, _root, true, default)).CanRetry);
    }

    [TestMethod]
    public async Task Retained_recovery_does_not_infer_disk_full_from_legacy_directory_or_remote_quota()
    {
        var (run, target, download, _, _, provider, service) = await SeedRetainedDownload();
        download.Message = "BAIDU_OUTPUT_DIRECTORY_FAILED"; await _db.SaveChangesAsync();
        var legacy = await service.CheckAsync(run.Id, target, _root, true, default);
        Assert.IsTrue(legacy.CanRetry); Assert.AreEqual("BAIDU_OUTPUT_DIRECTORY_FAILED", legacy.FailureCode);
        provider.Checks.Clear(); download.Message = "BAIDU_STORAGE_QUOTA_EXCEEDED"; await _db.SaveChangesAsync();
        Assert.IsFalse((await service.CheckAsync(run.Id, target, _root, true, default)).CanRetry);
        Assert.AreEqual(0, provider.Checks.Count);
    }

    [DataTestMethod]
    [DataRow("BAIDU_OUTPUT_FILE_INVALID")]
    [DataRow("BAIDU_LOCAL_PATH_TOO_LONG")]
    public async Task Retained_long_name_recovery_requires_provider_proof_and_keeps_original_download_and_transfer(string code)
    {
        var (run, target, download, operation, transfer, provider, service) = await SeedRetainedDownload();
        download.Message = code; await _db.SaveChangesAsync();
        var before = JsonSerializer.Serialize(new {download, operation, transfer});
        provider.Problem = "BAIDU_LOCAL_PATH_MAPPING_CONFLICT";
        var blocked = await service.CheckAsync(run.Id, target, _root, true, default);
        Assert.IsFalse(blocked.CanRetry); Assert.AreEqual(provider.Problem, blocked.FailureCode);
        provider.Problem = null;
        var ready = await service.CheckAsync(run.Id, target, _root, true, default);
        Assert.IsTrue(ready.CanRetry, ready.ReasonCode);
        Assert.AreEqual(before, JsonSerializer.Serialize(new {download, operation, transfer}));
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual("bytes", await File.ReadAllTextAsync(Path.Combine(download.DownloadPath, "retained-fixture")));
    }

    [DataTestMethod]
    [DataRow("BAIDU_OUTPUT_FILE_INVALID", true)]
    [DataRow("BAIDU_LOCAL_PATH_TOO_LONG", true)]
    [DataRow("BAIDU_LOCAL_PATH_MAPPING_CONFLICT", false)]
    [DataRow("BAIDU_LOCAL_PATH_MAPPING_INVALID", false)]
    public void Baidu_provider_allows_inspection_but_never_blind_retry_of_path_mapping_errors(string code, bool inspectable)
    {
        var provider = new Bakabase.Service.Components.CloudDownloads.Baidu.BaiduCloudDownloadProvider(null!, null!, null!);
        Assert.AreEqual(inspectable, provider.CanRevalidateRetainedDownloadFailure(code));
        CollectionAssert.Contains(Bakabase.Service.Components.Workflow.ExecutionProblemClassifier.Classify([code]).ToArray(), "localStorage");
        Assert.AreNotEqual("confirmedUnavailable", Bakabase.Service.Components.Workflow.ExecutionProblemClassifier.AvailabilityStatus(code));
    }
}

public sealed partial class AcquisitionRecoveryTests
{
    [TestMethod]
    [DoNotParallelize]
    public async Task Baidu_inspection_profile_reads_without_account_lock_creation_or_metadata_updates()
    {
        var app = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Bakabase.Infrastructures.Components.App.AppService>(_services);
        var root = Path.Combine(app.AppDataDirectory, "cloud-downloads", "baidu");
        var id = Guid.NewGuid().ToString("N"); var directory = Path.Combine(root, id);
        Directory.CreateDirectory(root);
        var metadataFile = Path.Combine(root, "accounts.json");
        var previous = File.Exists(metadataFile) ? await File.ReadAllBytesAsync(metadataFile) : null;
        try
        {
            var account = new Bakabase.Service.Components.CloudDownloads.Baidu.BaiduPcsAccountViewModel(id, "Fixture", "123", true, true, null);
            var metadata = JsonSerializer.Serialize(new[] {account}, JsonSerializerOptions.Web);
            await File.WriteAllTextAsync(metadataFile, metadata);
            var store = new Bakabase.Service.Components.CloudDownloads.Baidu.BaiduPcsAccountStore(app, null!);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadInspectionProfileAsync(id, default));
            Assert.IsFalse(Directory.Exists(directory), "Inspection must not recreate a missing profile.");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "pcs_config.json"), "fixture-profile");
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var held = store.WithAccountAsync(id, async (_, _) => {entered.SetResult(true); return await release.Task;}, default);
            try
            {
                await entered.Task;
                var profile = await store.ReadInspectionProfileAsync(id, default).WaitAsync(TimeSpan.FromSeconds(2));
                Assert.AreEqual(account, profile.Account); Assert.AreEqual(directory, profile.Directory);
                Assert.AreEqual(metadata, await File.ReadAllTextAsync(metadataFile));
                Assert.AreEqual("fixture-profile", await File.ReadAllTextAsync(Path.Combine(directory, "pcs_config.json")));
            }
            finally {release.TrySetResult(true); await held;}
        }
        finally
        {
            if (previous == null) File.Delete(metadataFile); else await File.WriteAllBytesAsync(metadataFile, previous);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
