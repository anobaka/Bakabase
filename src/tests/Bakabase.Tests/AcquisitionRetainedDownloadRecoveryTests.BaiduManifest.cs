using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Baidu_manifest_failure_rechecks_pinned_transfer_before_restarting_only_its_original_download(bool manifestChanges)
    {
        var (run, target, download, operation, transfer, provider, downloads) = await SeedRetainedDownload();
        download.Message = "BAIDU_MANIFEST_FAILED";
        download.Progress = 0;
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)! with
            {Targets = [target with {Problem = download.Message}]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        await _db.SaveChangesAsync();
        var task = await _db.AcquisitionTasks.SingleAsync(t => t.WorkflowRunId == run.Id);
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, new UserStoragePolicy(false),
            _services.GetRequiredService<IArtifactOperationStore>(), downloads: downloads);
        var starter = new OnlyDownloadStarter(this, run.Id);
        var host = new ProcessingRecoveryHost();
        var service = new AcquisitionRecoveryService(_db, diagnostics, new(), host,
            downloadOnly: OnlyDownloads(downloads, starter, new()));
        var before = JsonSerializer.Serialize(new {operation, transfer});

        var preview = await service.PreviewAsync(new([task.Id], "downloads"));
        var row = preview.Rows.Single();
        Assert.IsTrue(row.CanContinue, row.ReasonCode);
        Assert.AreEqual("RECOVERY_ORIGINAL_DOWNLOAD_REVALIDATED", row.ReasonCode);
        Assert.AreEqual("BAIDU_MANIFEST_FAILED", row.CurrentProblemCode);
        Assert.IsTrue(provider.Checks.Contains(true));
        Assert.AreEqual(0, starter.Calls.Count);
        if (manifestChanges) provider.Problem = "BAIDU_REMOTE_MANIFEST_CHANGED";

        var result = await service.ApplyAsync(new(preview.PreviewToken, [task.Id]));

        Assert.AreEqual(manifestChanges ? 0 : 1, result.ContinuedTaskIds.Count);
        CollectionAssert.AreEqual(manifestChanges ? Array.Empty<int>() : new[] {download.Id}, starter.Calls);
        Assert.AreEqual(0, provider.VerificationCalls);
        Assert.AreEqual(0, host.Calls);
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(before, JsonSerializer.Serialize(new {operation, transfer}));
    }

    [TestMethod]
    public async Task Baidu_workflow_manifest_message_cannot_override_the_original_files_verification_action()
    {
        var (run, target, download, _, _, provider, downloads) = await SeedRetainedDownload();
        download.Message = "BAIDU_LOCAL_FILE_CHANGED";
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)! with
            {Targets = [target with {Problem = "BAIDU_MANIFEST_FAILED"}]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        await _db.SaveChangesAsync();
        var task = await _db.AcquisitionTasks.SingleAsync(t => t.WorkflowRunId == run.Id);
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, new UserStoragePolicy(false),
            _services.GetRequiredService<IArtifactOperationStore>(), downloads: downloads);
        var starter = new OnlyDownloadStarter(this, run.Id);
        var host = new ProcessingRecoveryHost();
        var service = new AcquisitionRecoveryService(_db, diagnostics, new(), host,
            downloadOnly: OnlyDownloads(downloads, starter, new()));

        var isolated = await service.PreviewAsync(new([task.Id], "downloads"));
        Assert.AreEqual(1, isolated.ContinuableCount);
        Assert.AreEqual("RECOVERY_EXISTING_FILES_VERIFY_REQUIRED", isolated.Rows.Single().ReasonCode);
        Assert.AreEqual("downloads", isolated.Rows.Single().Action);
        var preview = await service.PreviewAsync(new([task.Id], "workflow"));
        var row = preview.Rows.Single();
        Assert.IsTrue(row.CanContinue, row.ReasonCode);
        Assert.AreEqual("RECOVERY_EXISTING_FILES_VERIFY_REQUIRED", row.ReasonCode);
        Assert.AreEqual("BAIDU_LOCAL_FILE_CHANGED", row.CurrentProblemCode);
        Assert.AreEqual(0, provider.VerificationCalls);

        var result = await service.ApplyAsync(new(preview.PreviewToken, [task.Id]));

        CollectionAssert.AreEqual(new[] {task.Id}, result.ContinuedTaskIds.ToArray());
        Assert.AreEqual(1, host.Calls);
        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(host.Signal!, WorkflowJson.Options)!;
        Assert.IsTrue(signal.VerifyExistingFilesOnly == true && signal.ObserveExistingOnly == true);
        Assert.IsFalse(signal.RevalidateExistingDownloads == true);
        CollectionAssert.AreEqual(new[] {target.Id}, signal.ResumeTargetIds!);
        Assert.AreEqual(0, provider.RetainedInspectionCalls);
        Assert.AreEqual(0, starter.Calls.Count);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
    }
}
