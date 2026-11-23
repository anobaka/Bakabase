using System.Security.Cryptography;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.CloudDownloads.Baidu;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow("BAIDU_OUTPUT_FILE_READ_FAILED")]
    [DataRow("BAIDU_LOCAL_FILE_CHANGED")]
    [DataRow("BAIDU_DOWNLOAD_CHECKSUM_MISMATCH")]
    public async Task Historical_local_failures_offer_existing_verification_without_transport_retry(string code)
    {
        var (run, target, download, operation, transfer, provider, service) = await SeedRetainedDownload();
        download.Message = code; download.Progress = 0;
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)! with
            {Targets = [target with {Problem = code}]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset); await _db.SaveChangesAsync();
        var task = await _db.AcquisitionTasks.SingleAsync(t => t.WorkflowRunId == run.Id);
        var before = JsonSerializer.Serialize(new {task, run, download, operation, transfer});
        _diagnostics = new(_db, new UserStoragePolicy(false), _services.GetRequiredService<IArtifactOperationStore>(), downloads: service);

        var cheap = await _diagnostics.DiagnoseAsync(task, run, default, verifyFiles: false);
        Assert.AreEqual("RECOVERY_RECHECK_SOURCE", cheap.Row.ReasonCode);
        var diagnosis = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsTrue(diagnosis.Row.CanContinue, diagnosis.Row.ReasonCode);
        Assert.AreEqual("RECOVERY_EXISTING_FILES_VERIFY_REQUIRED", diagnosis.Row.ReasonCode);
        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(diagnosis.SignalJson, JsonSerializerOptions.Web)!;
        Assert.IsTrue(signal.VerifyExistingFilesOnly == true);
        Assert.IsTrue(signal.ObserveExistingOnly == true);
        Assert.IsFalse(signal.RevalidateExistingDownloads == true);
        CollectionAssert.AreEqual(new[] {target.Id}, signal.ResumeTargetIds!);
        Assert.AreEqual(0, provider.RetainedInspectionCalls);
        Assert.AreEqual(0, provider.VerificationCalls, "Preview is structural inspection, never a full content read.");
        Assert.AreEqual(before, JsonSerializer.Serialize(new {task, run, download, operation, transfer}));
    }

    [DataTestMethod]
    [DataRow("BAIDU_EXISTING_DOWNLOAD_UNVERIFIED")]
    [DataRow("BAIDU_DOWNLOAD_CHECKPOINT_INVALID")]
    [DataRow("BAIDU_REMOTE_MANIFEST_CHANGED")]
    public async Task Incomplete_existing_tree_does_not_fall_back_to_a_transport_capability(string failure)
    {
        var (run, target, download, _, _, provider, service) = await SeedRetainedDownload();
        // This provider also permits retained transport recovery for this code. Failed verification
        // must remain blocked, even if that alternative could start the original downloader.
        download.Message = "BAIDU_OUTPUT_FILE_READ_FAILED"; provider.Problem = failure;
        await _db.SaveChangesAsync();
        var check = await service.CheckAsync(run.Id, target, _root, true, default);
        Assert.IsFalse(check.CanRetry); Assert.IsFalse(check.Candidate);
        Assert.AreEqual(failure, check.FailureCode);
        Assert.AreEqual(0, provider.RetainedInspectionCalls);
        Assert.AreEqual(0, provider.VerificationCalls);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
    }

    [TestMethod]
    public async Task Downloads_only_can_resume_the_original_incomplete_local_file_change()
    {
        var (run, target, download, operation, transfer, provider, service) = await SeedRetainedDownload();
        download.Message = "BAIDU_LOCAL_FILE_CHANGED";
        download.Progress = 75;
        provider.ExistingInspectionFailure = "BAIDU_EXISTING_DOWNLOAD_UNVERIFIED";
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)! with
            {Targets = [target with {Problem = download.Message}]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        await _db.SaveChangesAsync();
        var task = await _db.AcquisitionTasks.SingleAsync(t => t.WorkflowRunId == run.Id);
        var original = JsonSerializer.Serialize(new {operation, transfer});
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, new UserStoragePolicy(false),
            _services.GetRequiredService<IArtifactOperationStore>(), downloads: service);
        var starter = new OnlyDownloadStarter(this, run.Id);
        var recovery = new AcquisitionRecoveryService(_db, diagnostics, new(), new RecordingHost(),
            downloadOnly: OnlyDownloads(service, starter, new()));

        var workflow = await recovery.PreviewAsync(new([task.Id], "workflow"));
        Assert.AreEqual("RECOVERY_SOURCE_STILL_BLOCKED", workflow.Rows.Single().ReasonCode);
        Assert.AreEqual(0, workflow.ContinuableCount);
        Assert.AreEqual(0, provider.RetainedInspectionCalls);
        var preview = await recovery.PreviewAsync(new([task.Id], "downloads"));
        Assert.AreEqual("RECOVERY_ORIGINAL_DOWNLOAD_REVALIDATED", preview.Rows.Single().ReasonCode);
        Assert.AreEqual(1, preview.ContinuableCount);
        Assert.AreEqual(0, starter.Calls.Count);
        var result = await recovery.ApplyAsync(new(preview.PreviewToken, [task.Id]));

        CollectionAssert.AreEqual(new[] {download.Id}, result.Downloads!.Single().StartedDownloadTaskIds.ToArray());
        CollectionAssert.AreEqual(new[] {download.Id}, starter.Calls.ToArray());
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
        Assert.AreEqual(0, provider.VerificationCalls);
        Assert.IsTrue(provider.Checks.Contains(true), "The original remote manifest must be revalidated.");
        Assert.AreEqual(original, JsonSerializer.Serialize(new {operation, transfer}));
        Assert.AreEqual("bytes", await File.ReadAllTextAsync(Path.Combine(download.DownloadPath, "retained-fixture")));
    }

    [DataTestMethod]
    [DataRow("BAIDU_DOWNLOAD_CHECKSUM_MISMATCH", "BAIDU_EXISTING_DOWNLOAD_UNVERIFIED", false)]
    [DataRow("BAIDU_VERIFIED_FILE_MISSING", "BAIDU_EXISTING_DOWNLOAD_UNVERIFIED", true)]
    [DataRow("BAIDU_VERIFIED_FILE_REPLACED", "BAIDU_DOWNLOAD_CHECKPOINT_INVALID", false)]
    [DataRow("BAIDU_LOCAL_FILE_CHANGED", "BAIDU_DOWNLOAD_CHECKPOINT_INVALID", false)]
    [DataRow("BAIDU_LOCAL_FILE_CHANGED", "BAIDU_EXISTING_DOWNLOAD_UNVERIFIED", true)]
    public async Task Explicit_partial_resume_rejects_other_failures_and_empty_locations(string failure,
        string inspectionFailure, bool removeFile)
    {
        var (run, target, download, _, _, provider, service) = await SeedRetainedDownload();
        download.Message = failure;
        provider.ExistingInspectionFailure = inspectionFailure;
        if (removeFile) File.Delete(Path.Combine(download.DownloadPath, "retained-fixture"));
        await _db.SaveChangesAsync();

        var check = await service.CheckAsync(run.Id, target, _root, true, default, includeStopped: true);

        Assert.IsFalse(check.CanRetry);
        Assert.IsFalse(check.Candidate);
        Assert.AreEqual(0, provider.RetainedInspectionCalls);
        Assert.AreEqual(0, provider.VerificationCalls);
    }

    [TestMethod]
    public async Task Complete_files_still_require_verification_if_they_appear_during_partial_resume_revalidation()
    {
        var (run, target, download, _, _, provider, service) = await SeedRetainedDownload();
        download.Message = "BAIDU_LOCAL_FILE_CHANGED";
        provider.ExistingInspectionFailure = "BAIDU_EXISTING_DOWNLOAD_UNVERIFIED";
        provider.DuringRemote = () => { provider.ExistingInspectionFailure = null; return Task.CompletedTask; };
        await _db.SaveChangesAsync();

        var check = await service.CheckAsync(run.Id, target, _root, true, default, includeStopped: true);

        Assert.IsFalse(check.CanRetry);
        Assert.IsTrue(check.VerifyExistingOnly);
        Assert.AreEqual("RECOVERY_EXISTING_FILES_VERIFY_REQUIRED", check.ReasonCode);
        Assert.AreEqual(1, provider.RetainedInspectionCalls);
        Assert.AreEqual(0, provider.VerificationCalls);
    }

    [DataTestMethod]
    [DataRow("BAIDU_LOCAL_FILE_CHANGED", true)]
    [DataRow("BAIDU_DOWNLOAD_CHECKSUM_MISMATCH", false)]
    public async Task Only_full_content_proof_registers_original_download_and_preserves_history(string code, bool validContent)
    {
        var (run, target, download, operation, transfer, provider, service) = await SeedRetainedDownload();
        download.Message = code; download.Key = "original"; await _db.SaveChangesAsync();
        var file = Path.Combine(download.DownloadPath, "retained-fixture");
        var bytes = await File.ReadAllBytesAsync(file);
        var originalOperation = JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id));
        var originalTransfer = JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == transfer.Id));
        var history = (await _db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanHistoryJson;
        provider.ExistingProof = new([new(file, bytes.Length, validContent
            ? Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() : new string('0', 64))]);
        var verify = () => service.VerifyExistingAsync(run.Id, target, _root,
            _services.GetRequiredService<DownloadTaskService>(), default);
        if (validContent) await verify();
        else await Assert.ThrowsExactlyAsync<InvalidOperationException>(verify);
        var after = await _db.DownloadTasks.AsNoTracking().SingleAsync();
        Assert.AreEqual(validContent ? DownloadTaskDbModelStatus.Complete : DownloadTaskDbModelStatus.Failed, after.Status);
        Assert.AreEqual(code, after.Message, "The original failure remains historical evidence after successful registration.");
        Assert.AreEqual(validContent ? 1 : 0, await _db.DownloadResults.CountAsync());
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(1, provider.VerificationCalls);
        Assert.AreEqual(0, provider.RetainedInspectionCalls);
        Assert.AreEqual(originalOperation, JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id)));
        Assert.AreEqual(originalTransfer, JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == transfer.Id)));
        Assert.AreEqual(history, (await _db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanHistoryJson);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(file));
    }

    [TestMethod]
    public async Task Provider_checksum_rejection_stays_waiting_without_restarting_any_download()
    {
        var (run, target, download, _, _, provider, service) = await SeedRetainedDownload();
        download.Message = "BAIDU_DOWNLOAD_CHECKSUM_MISMATCH";
        provider.VerificationFailure = "BAIDU_EXISTING_DOWNLOAD_UNVERIFIED";
        await _db.SaveChangesAsync();
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)! with {Targets = [target]};
        using var services = new ServiceCollection().AddSingleton(service)
            .AddSingleton(_services.GetRequiredService<DownloadTaskService>()).BuildServiceProvider();
        var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = "{}",
            Services = services, Logger = NullLogger.Instance};
        var signal = JsonSerializer.Serialize(new CloudSelectionSignal {ObserveExistingOnly = true,
            ResumeTargetIds = [target.Id], VerifyExistingFilesOnly = true}, JsonSerializerOptions.Web);
        var outcome = await new AwaitCloudDownloadsActivity().ResumeAsync(context, workset, signal, default);
        Assert.IsNotNull(outcome.Suspension);
        StringAssert.Contains(outcome.Suspension.PromptJson, provider.VerificationFailure);
        Assert.AreEqual(1, provider.VerificationCalls); Assert.AreEqual(0, provider.RetainedInspectionCalls);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
    }

    [DataTestMethod]
    [DataRow("shared-download")]
    [DataRow("unknown-transfer")]
    [DataRow("changed-request")]
    public async Task Historical_verification_keeps_original_binding_guards(string change)
    {
        var (run, target, download, operation, transfer, provider, service) = await SeedRetainedDownload();
        download.Message = "BAIDU_LOCAL_FILE_CHANGED";
        if (change == "shared-download") _db.ArtifactOperationBindings.Add(new() {Id = "another-owner",
            OperationId = operation.Id, OwnerKind = "workflow", OwnerId = "another", Slot = "other:download"});
        if (change == "unknown-transfer") transfer.State = "unknown";
        if (change == "changed-request") download.Options = "{}";
        await _db.SaveChangesAsync();
        var check = await service.CheckAsync(run.Id, target, _root, true, default);
        Assert.IsFalse(check.CanRetry); Assert.IsFalse(check.Candidate);
        Assert.AreEqual(0, provider.Checks.Count); Assert.AreEqual(0, provider.VerificationCalls);
    }

    [DataTestMethod]
    [DataRow("BAIDU_OUTPUT_FILE_READ_FAILED", true)]
    [DataRow("BAIDU_LOCAL_FILE_CHANGED", true)]
    [DataRow("BAIDU_DOWNLOAD_CHECKSUM_MISMATCH", true)]
    [DataRow("BAIDU_OUTPUT_DIRECTORY_FAILED", false)]
    [DataRow("LOCAL_STORAGE_FULL", false)]
    [DataRow("BAIDU_DOWNLOAD_FAILED", false)]
    [DataRow("unknown", false)]
    public void Provider_explicitly_selects_verification_errors_without_broadening_transport_retries(string code, bool expected)
    {
        var provider = new BaiduCloudDownloadProvider(null!, null!, null!);
        Assert.AreEqual(expected, provider.CanVerifyExistingDownloadFailure(code));
    }
}
