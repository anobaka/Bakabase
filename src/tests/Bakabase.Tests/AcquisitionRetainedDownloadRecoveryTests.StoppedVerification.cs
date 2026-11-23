using System.Security.Cryptography;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Stopped_download_workflow_recovery_only_registers_files_after_matching_content_proof(bool validContent)
    {
        var (run, target, download, operation, transfer, provider, downloads) = await SeedRetainedDownload();
        download.Status = DownloadTaskDbModelStatus.Disabled;
        download.Message = null;
        download.Progress = 0;
        download.Key = "original";
        var file = Path.Combine(download.DownloadPath, "retained-fixture");
        await File.WriteAllBytesAsync(file, "safe"u8.ToArray());
        provider.ExistingProof = new([new(file, 4, Convert.ToHexString(SHA256.HashData(validContent ? "safe"u8 : "else"u8)))]);
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)! with {Targets = [target]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        await _db.SaveChangesAsync();
        var task = await _db.AcquisitionTasks.SingleAsync(t => t.WorkflowRunId == run.Id);
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, new UserStoragePolicy(false),
            _services.GetRequiredService<IArtifactOperationStore>(), downloads: downloads);
        var host = new RecordingHost();
        var recovery = new AcquisitionRecoveryService(_db, diagnostics, new(), host);

        Assert.IsFalse((await diagnostics.DiagnoseAsync(task, run, default)).Row.CanContinue,
            "A background diagnostic does not opt a stopped original into verification.");
        Assert.AreEqual(0, (await recovery.PreviewAsync(new([task.Id], "downloads"))).ContinuableCount);
        var preview = await recovery.PreviewAsync(new([task.Id], "workflow"));
        Assert.AreEqual(1, preview.ContinuableCount, preview.Rows.Single().ReasonCode);
        Assert.AreEqual("RECOVERY_EXISTING_FILES_VERIFY_REQUIRED", preview.Rows.Single().ReasonCode);
        var applied = await recovery.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        CollectionAssert.AreEqual(new[] {task.Id}, applied.ContinuedTaskIds.ToArray());
        Assert.AreEqual(0, provider.VerificationCalls, "The recovery handoff must not block on payload reads.");
        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(host.Signal!, WorkflowJson.Options)!;
        Assert.IsTrue(signal.VerifyExistingFilesOnly == true && signal.ObserveExistingOnly == true);
        Assert.IsFalse(signal.RevalidateExistingDownloads == true);
        CollectionAssert.AreEqual(new[] {target.Id}, signal.ResumeTargetIds!);

        using var services = new ServiceCollection().AddSingleton(downloads).AddSingleton<BakabaseDbContext>(_db)
            .AddSingleton(_services.GetRequiredService<DownloadTaskService>()).BuildServiceProvider();
        var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = "{}",
            Services = services, Logger = NullLogger.Instance};
        var outcome = await new AwaitCloudDownloadsActivity().ResumeAsync(context, workset, host.Signal!, default);
        var original = await _db.DownloadTasks.AsNoTracking().SingleAsync();
        Assert.AreEqual(download.Id, original.Id);
        Assert.AreEqual(download.Options, original.Options);
        Assert.AreEqual(1, provider.VerificationCalls);
        Assert.AreEqual(0, provider.RetainedInspectionCalls);
        Assert.IsNull(original.Message);
        Assert.AreEqual("safe", await File.ReadAllTextAsync(file));
        CollectionAssert.AreEquivalent(new[] {operation.Id, transfer.Id}, await _db.CloudDownloadOperations.Select(o => o.Id).ToArrayAsync());
        if (validContent)
        {
            Assert.IsNull(outcome.Suspension, outcome.Suspension?.PromptJson);
            Assert.AreEqual(DownloadTaskDbModelStatus.Complete, original.Status);
            Assert.IsNotNull(original.CompletedAt);
            var receipt = await _db.DownloadResults.AsNoTracking().SingleAsync();
            Assert.AreEqual(original.Id, receipt.DownloadTaskId);
            Assert.AreEqual((await ArtifactManifestFiles.CaptureAsync(download.DownloadPath, [file])).Fingerprint, receipt.Fingerprint);
        }
        else
        {
            Assert.IsNotNull(outcome.Suspension);
            StringAssert.Contains(outcome.Suspension.PromptJson, "RECOVERY_EXISTING_FILES_UNVERIFIED");
            Assert.AreEqual(DownloadTaskDbModelStatus.Disabled, original.Status);
            Assert.IsNull(original.CompletedAt);
            Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
        }
    }

    [DataTestMethod]
    [DataRow("partial")]
    [DataRow("changed-request")]
    [DataRow("not-started")]
    public async Task Stopped_verification_blocks_before_content_when_the_owned_original_is_incomplete_or_changed(string change)
    {
        var (run, target, download, operation, _, provider, downloads) = await SeedRetainedDownload();
        download.Status = DownloadTaskDbModelStatus.Disabled;
        download.Message = null;
        target = target with {Problem = "LOCAL_STORAGE_FULL"};
        if (change == "partial") provider.ExistingInspectionFailure = "BAIDU_EXISTING_DOWNLOAD_UNVERIFIED";
        if (change == "changed-request") download.Options = "{}";
        if (change == "not-started") operation.State = "queued";
        await _db.SaveChangesAsync();

        var check = await downloads.CheckAsync(run.Id, target, _root, true, default, includeStoppedForVerification: true);
        Assert.IsFalse(check.CanRetry);
        Assert.IsFalse(check.Candidate);
        Assert.AreEqual(change == "partial" ? 1 : 0, provider.Checks.Count);
        var tasks = _services.GetRequiredService<DownloadTaskService>();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => downloads.VerifyExistingAsync(run.Id, target, _root, tasks, default));
        Assert.AreEqual(0, provider.VerificationCalls);
        Assert.AreEqual(0, provider.RetainedInspectionCalls);
        Assert.AreEqual(DownloadTaskDbModelStatus.Disabled, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
    }
}
