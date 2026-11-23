using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private const string OldDownloadMountError = "Reconnect the mounted storage location. ";

    [TestMethod]
    public async Task Failed_download_wait_reuses_its_verified_completed_original_after_the_mount_returns()
    {
        var (task, run, _, download, operation, _, provider, _) = await SeedHistoricalDownloadStorage();
        var host = new RecordingHost();
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var downloads = await service.PreviewAsync(new([task.Id], "downloads"));
        Assert.AreEqual(0, downloads.ContinuableCount);

        var preview = await service.PreviewAsync(new([task.Id], "workflow"));
        var row = preview.Rows.Single();
        Assert.IsTrue(row.CanContinue, row.ReasonCode);
        Assert.AreEqual("retry", row.Action);
        Assert.AreEqual("RECOVERY_FILES_VERIFIED", row.ReasonCode);
        Assert.AreEqual("RECOVERY_STORAGE_UNAVAILABLE", row.CurrentProblemCode);
        Assert.AreEqual(run.Id, row.WorkflowRunId);

        var result = await service.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        CollectionAssert.AreEqual(new[] {task.Id}, result.ContinuedTaskIds.ToArray());
        Assert.AreEqual(1, host.Calls);
        Assert.AreEqual("{\"observeExistingOnly\":true}", host.Signal);
        Assert.AreEqual(0, provider.Checks);
        Assert.AreEqual(download.Id, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Id);
        Assert.AreEqual(DownloadTaskDbModelStatus.Complete, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(operation.Id, (await _db.CloudDownloadOperations.AsNoTracking().SingleAsync()).Id);
        Assert.AreEqual(run.CurrentItemJson, (await _db.WorkflowRuns.AsNoTracking().SingleAsync()).CurrentItemJson);
    }

    [DataTestMethod]
    [DataRow("changedBytes")]
    [DataRow("unfinishedDownload")]
    [DataRow("staleWorkset")]
    [DataRow("staleRunRevision")]
    [DataRow("sharedRunOwner")]
    [DataRow("sharedDownloadBinding")]
    [DataRow("outsideMount")]
    [DataRow("relativeMount")]
    [DataRow("noncanonicalMount")]
    [DataRow("differentError")]
    [DataRow("laterDownload")]
    public async Task Failed_download_wait_requires_exact_ownership_storage_and_completion_proof(string fault)
    {
        var (task, run, workset, download, operation, owner, provider, file) = await SeedHistoricalDownloadStorage();
        switch (fault)
        {
            case "changedBytes": await File.WriteAllTextAsync(file, "changed!"); break;
            case "unfinishedDownload": download.Status = DownloadTaskDbModelStatus.Failed; break;
            case "staleWorkset": run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with
                {Id = $"acquisition:{task.Id}:0"}); break;
            case "staleRunRevision": owner.Revision = "0"; break;
            case "sharedRunOwner":
                _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id,
                    Module = "acquisition", EntityKind = "task", EntityId = "999999", Revision = "1"});
                break;
            case "sharedDownloadBinding":
                _db.ArtifactOperationBindings.Add(new() {OperationId = operation.Id, OwnerKind = "workflow",
                    OwnerId = "999999", Slot = "part:download"});
                break;
            case "outsideMount": run.ErrorMessage = OldDownloadMountError + _root + "-outside"; break;
            case "relativeMount": run.ErrorMessage = OldDownloadMountError + "relative"; break;
            case "noncanonicalMount": run.ErrorMessage = OldDownloadMountError + Path.Combine(_root, "link", ".."); break;
            case "differentError": run.ErrorMessage = "Remote error mentioned: " + OldDownloadMountError + _root; break;
            case "laterDownload":
                run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Historical download",
                    TriggerKind = CloudWorkflow.Trigger, Nodes = [new() {NodeId = "wait", Kind = "download.awaitCompletion"},
                        new() {NodeId = "second-downloader", Kind = "download.enqueueSupported"}]});
                break;
        }
        await _db.SaveChangesAsync();

        var diagnosis = await _diagnostics.DiagnoseAsync(task, run, default, verifyContent: true);
        Assert.IsFalse(diagnosis.Row.CanContinue, diagnosis.Row.ReasonCode);
        Assert.AreEqual(0, provider.Checks);
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(1, await _db.CloudDownloadOperations.CountAsync());
    }

    [TestMethod]
    public async Task Failed_download_wait_requires_the_recorded_mount_to_be_available_in_restricted_storage()
    {
        var (task, run, _, _, _, _, _, _) = await SeedHistoricalDownloadStorage();
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, new UnmountedHistoricalStorage(),
            _services.GetRequiredService<IArtifactOperationStore>());

        var result = await diagnostics.DiagnoseAsync(task, run, default);

        Assert.IsFalse(result.Row.CanContinue);
        Assert.AreEqual("RECOVERY_STORAGE_UNAVAILABLE", result.Row.ReasonCode);
    }

    private async Task<(AcquisitionTaskDbModel Task, WorkflowRunDbModel Run, DownloadWorkset Workset, DownloadTaskDbModel Download,
        CloudOperationDbModel Operation, WorkflowRunSourceDbModel Owner, RecoverableProvider Provider, string File)>
        SeedHistoricalDownloadStorage()
    {
        var (task, run, workset, download, operation, provider, _) = await SeedRecoverableProvider();
        var owner = new WorkflowRunSourceDbModel {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task",
            EntityId = task.Id.ToString(), Revision = task.PlanRevision.ToString()};
        _db.Set<WorkflowRunSourceDbModel>().Add(owner);
        workset = workset with {Id = $"acquisition:{task.Id}:{task.PlanRevision}",
            SourceRefs = [new WorkflowSourceRef(owner.Module, owner.EntityKind, owner.EntityId, owner.Revision)]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Historical download",
            TriggerKind = CloudWorkflow.Trigger, Nodes = [new() {NodeId = "wait", Kind = "download.awaitCompletion"},
                new() {NodeId = "publish", Kind = "fs.publishArtifacts"}]});
        run.Status = WorkflowRunStatus.Failed;
        run.ErrorMessage = OldDownloadMountError + _root;
        download.Status = DownloadTaskDbModelStatus.Complete;
        Directory.CreateDirectory(download.DownloadPath);
        var file = Path.Combine(download.DownloadPath, "payload.bin");
        await File.WriteAllTextAsync(file, "original");
        var manifest = await ArtifactManifestFiles.CaptureAsync(download.DownloadPath, [file]);
        _db.DownloadResults.Add(new() {DownloadTaskId = download.Id, DownloadDirectory = download.DownloadPath,
            Path = download.DownloadPath, FilesJson = JsonSerializer.Serialize(new[] {file}), Fingerprint = manifest.Fingerprint});
        await _db.SaveChangesAsync();
        return (task, run, workset, download, operation, owner, provider, file);
    }

    private sealed class UnmountedHistoricalStorage : IUserStoragePolicy
    {
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => false;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            throw new AssertFailedException("Verification must wait for the recorded mount.");
        public void EnsureTreeMutationAllowed(string path) => throw new AssertFailedException("Diagnosis must stay read-only.");
    }
}
