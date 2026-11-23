using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private const string StoppedDownloadProblem = "The cloud download stopped. Resume this workflow to retry it.";

    [TestMethod]
    public async Task ExplicitDownloadRecoveryResumesOnlyTheProvenStoppedOriginal()
    {
        var (run, target, download, _, _, provider, service) = await SeedRetainedDownload();
        download.Status = DownloadTaskDbModelStatus.Disabled;
        download.Message = null;
        provider.SupportsExplicitStoppedCheckpointResume = true;
        provider.ExistingInspectionFailure = "BAIDU_EXISTING_DOWNLOAD_UNVERIFIED";
        target = target with {Problem = StoppedDownloadProblem};
        var sibling = target with {Id = "other-target", DownloadTaskId = download.Id + 1};
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)! with {Targets = [target, sibling]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        await _db.SaveChangesAsync();
        var task = await _db.AcquisitionTasks.SingleAsync(t => t.WorkflowRunId == run.Id);
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, new UserStoragePolicy(false),
            _services.GetRequiredService<IArtifactOperationStore>(), downloads: service);
        var starter = new OnlyDownloadStarter(this, run.Id);
        var recovery = new AcquisitionRecoveryService(_db, diagnostics, new(), new RecordingHost(),
            downloadOnly: OnlyDownloads(service, starter, new()));

        var automatic = await service.CheckAsync(run.Id, target, _root, true, default);
        Assert.IsFalse(automatic.CanRetry);
        Assert.AreEqual(0, (await recovery.PreviewAsync(new([task.Id], "workflow"))).ContinuableCount);
        Assert.AreEqual(0, provider.RetainedInspectionCalls);

        var preview = await recovery.PreviewAsync(new([task.Id], "downloads"));
        Assert.AreEqual(1, preview.ContinuableCount, preview.Rows.Single().ReasonCode);
        Assert.AreEqual(0, starter.Calls.Count);
        Assert.IsTrue(provider.Checks.Contains(true));

        var result = await recovery.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        CollectionAssert.AreEqual(new[] {download.Id}, result.Downloads!.Single().StartedDownloadTaskIds.ToArray());
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("provider-not-opted-in", "RECOVERY_MANUAL_REVIEW")]
    [DataRow("unknown-stopped-message", "RECOVERY_MANUAL_REVIEW")]
    [DataRow("native-message-present", "RECOVERY_MANUAL_REVIEW")]
    [DataRow("missing-retained-files", "RECOVERY_DOWNLOAD_REVIEW_REQUIRED")]
    [DataRow("invalid-checkpoint", "RECOVERY_SOURCE_STILL_BLOCKED")]
    [DataRow("changed-remote-manifest", "RECOVERY_SOURCE_STILL_BLOCKED")]
    public async Task ExplicitStoppedResumeRequiresProviderProofOfTheOriginalFiles(string mode, string reason)
    {
        var (run, target, download, _, _, provider, service) = await SeedRetainedDownload();
        download.Status = DownloadTaskDbModelStatus.Disabled;
        download.Message = mode == "native-message-present" ? "Unclassified provider response" : null;
        target = target with {Problem = mode == "unknown-stopped-message" ? "Download stopped." : StoppedDownloadProblem};
        provider.SupportsExplicitStoppedCheckpointResume = mode != "provider-not-opted-in";
        if (mode == "missing-retained-files")
            File.Delete(Path.Combine(download.DownloadPath, "retained-fixture"));
        if (mode == "invalid-checkpoint")
            provider.Problem = "BAIDU_DOWNLOAD_CHECKPOINT_INVALID";
        if (mode == "changed-remote-manifest")
            provider.DuringRemote = () =>
            {
                provider.Problem = "BAIDU_REMOTE_MANIFEST_CHANGED";
                return Task.CompletedTask;
            };
        await _db.SaveChangesAsync();

        var check = await service.CheckAsync(run.Id, target, _root, mode == "changed-remote-manifest", default, includeStopped: true);

        Assert.IsFalse(check.CanRetry);
        Assert.IsFalse(check.Candidate);
        Assert.AreEqual(reason, check.ReasonCode);
        Assert.AreEqual(mode is "invalid-checkpoint" or "changed-remote-manifest" ? 1 : 0, provider.RetainedInspectionCalls);
    }
}
