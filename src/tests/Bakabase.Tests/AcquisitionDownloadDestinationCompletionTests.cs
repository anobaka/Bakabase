using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private async Task<(AcquisitionTaskDbModel Task, WorkflowRunDbModel Run, DownloadWorkset Workset,
        DownloadTaskDbModel Download, CloudOperationDbModel Operation)> SeedRevisedCompletedDownload(string kind, string? accountId = null)
    {
        var (task, run, original) = await Seed(kind, WorkflowRunStatus.Waiting,
            "Choose a folder inside a mounted storage location. previous destination");
        var oldRoot = Path.Combine(_root, "unavailable-old-root");
        var target = original.Targets.Single();
        if (accountId != null) target = target with {AccountId = accountId,
            Sources = target.Sources.Select(source => source with {AccountId = accountId}).ToList()};
        var (download, operation) = await RecordFixtureDownloadPart(run.Id, target, oldRoot, "task");
        var workset = original with {OutputDirectory = oldRoot, Targets = [target with {DownloadTaskId = download.Id}]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        await _db.SaveChangesAsync();
        return (task, run, workset, download, operation);
    }

    private async Task<(DownloadTaskDbModel Download, CloudOperationDbModel Operation)> RecordFixtureDownloadPart(
        int runId, CloudDownloadTarget target, string oldRoot, string folder)
    {
        var newRoot = Path.Combine(_root, "new-root");
        var directory = Directory.CreateDirectory(Path.Combine(newRoot, folder)).FullName;
        var intent = DownloadArtifactCoordinator.DownloadIntent(target, oldRoot);
        var request = new CloudDownloadRequest(intent, target.SelectedSource!, target.AccountId, null, Path.Combine(oldRoot, folder))
        {
            DestinationRevisions = [new(Guid.NewGuid().ToString("N"), Path.Combine(oldRoot, folder), directory,
                newRoot, DateTime.UtcNow, new string('a', 64))]
        };
        var download = new DownloadTaskDbModel {DownloadPath = directory, Status = DownloadTaskDbModelStatus.Complete,
            ThirdPartyId = CloudSites.Site("baidu"), Options = JsonSerializer.Serialize(request, JsonSerializerOptions.Web)};
        _db.DownloadTasks.Add(download); await _db.SaveChangesAsync();
        var operation = new CloudOperationDbModel {Id = intent, WorkflowRunId = runId, TargetId = target.Id,
            Kind = "download", State = "started", DownloadTaskId = download.Id, RequestJson = download.Options};
        _db.CloudDownloadOperations.Add(operation);
        _db.ArtifactOperationBindings.Add(new() {Id = Guid.NewGuid().ToString("N"), OwnerKind = "workflow",
            OwnerId = runId.ToString(), OperationId = intent, Slot = CloudOperationService.OperationSlot(target, "download")});
        var file = Path.Combine(directory, "official.bin"); await File.WriteAllTextAsync(file, "verified existing bytes");
        var manifest = await ArtifactManifestFiles.CaptureAsync(directory, [file]);
        _db.DownloadResults.Add(new() {DownloadTaskId = download.Id, DeduplicationKey = intent, DownloadDirectory = directory, Path = directory,
            FilesJson = JsonSerializer.Serialize(new[] {file}), Fingerprint = manifest.Fingerprint});
        await _db.SaveChangesAsync();
        return (download, operation);
    }

    [DataTestMethod]
    [DataRow("download.awaitCompletion")]
    [DataRow("download.enqueueSupported")]
    [DataRow("baidu.transfer")]
    public async Task RevisedCompletedDownloadUsesVerifiedPhysicalRootWhenSemanticRootIsGone(string kind)
    {
        var (task, run, workset, download, _) = await SeedRevisedCompletedDownload(kind);
        if (kind == "baidu.transfer") run.ErrorMessage = "Cannot access a disposed object. Object name: 'IServiceProvider'.";
        var before = JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().SingleAsync());
        Assert.IsFalse(Directory.Exists(workset.OutputDirectory));
        var result = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsTrue(result.Row.CanContinue, result.Row.ReasonCode);
        StringAssert.Contains(result.SignalJson, "observeExistingOnly");
        Assert.AreEqual(before, JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().SingleAsync()));
        await File.WriteAllTextAsync(Path.Combine(download.DownloadPath, "official.bin"), "changed bytes");
        Assert.IsFalse((await _diagnostics.DiagnoseAsync(task, run, default)).Row.CanContinue);
    }

    [DataTestMethod]
    [DataRow("missing-binding")]
    [DataRow("foreign-binding")]
    [DataRow("options-only")]
    [DataRow("operation-only")]
    [DataRow("changed-source")]
    [DataRow("changed-hint")]
    [DataRow("changed-physical-path")]
    [DataRow("original-outside-root")]
    [DataRow("unknown-operation")]
    public async Task CompletedDestinationOverrideCannotBypassIdentityOrBoundaryProof(string mismatch)
    {
        var (task, run, workset, download, operation) = await SeedRevisedCompletedDownload("download.awaitCompletion");
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(operation.RequestJson, JsonSerializerOptions.Web)!;
        switch (mismatch)
        {
            case "missing-binding": _db.ArtifactOperationBindings.RemoveRange(_db.ArtifactOperationBindings); break;
            case "foreign-binding": (await _db.ArtifactOperationBindings.SingleAsync()).OwnerId = "other"; break;
            case "options-only": operation.RequestJson = JsonSerializer.Serialize(request with {DestinationRevisions = null}, JsonSerializerOptions.Web); break;
            case "operation-only": download.Options = JsonSerializer.Serialize(request with {DestinationRevisions = null}, JsonSerializerOptions.Web); break;
            case "changed-source":
                request = request with {Source = request.Source with {Url = "https://pan.baidu.com/s/different"}};
                operation.RequestJson = download.Options = JsonSerializer.Serialize(request, JsonSerializerOptions.Web); break;
            case "changed-hint": run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with
                {Targets = [workset.Targets[0] with {DownloadOperationId = "different"}]}); break;
            case "changed-physical-path": download.DownloadPath = _root; break;
            case "original-outside-root":
                request = request with {OutputDirectory = Path.Combine(_root, "outside"), DestinationRevisions =
                    [request.DestinationRevisions![0] with {PreviousDirectory = Path.Combine(_root, "outside")}]};
                operation.RequestJson = download.Options = JsonSerializer.Serialize(request, JsonSerializerOptions.Web); break;
            case "unknown-operation": operation.State = "unknown"; break;
        }
        await _db.SaveChangesAsync();
        Assert.IsFalse((await _diagnostics.DiagnoseAsync(task, run, default)).Row.CanContinue);
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task CompletedOriginalDownloadCanObserveSuccessDespiteItsStaleTypedProviderError()
    {
        var (task, run, workset, _, operation) = await SeedRevisedCompletedDownload("download.awaitCompletion");
        run.ErrorMessage = null;
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [workset.Targets[0] with
            {Problem = "BAIDU_OUTPUT_DIRECTORY_FAILED"}]});
        var before = operation.RequestJson;
        var preview = await _diagnostics.DiagnoseAsync(task, run, default, verifyFiles: false);
        Assert.IsFalse(preview.Row.CanContinue); Assert.AreEqual("RECOVERY_VERIFY_FILES", preview.Row.ReasonCode);
        var ready = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsTrue(ready.Row.CanContinue, ready.Row.ReasonCode);
        Assert.AreEqual("{\"observeExistingOnly\":true}", ready.SignalJson);
        Assert.AreEqual(before, (await _db.CloudDownloadOperations.AsNoTracking().SingleAsync()).RequestJson);
    }

    [DataTestMethod]
    [DataRow("partial")]
    [DataRow("missing-result")]
    [DataRow("missing-binding")]
    [DataRow("changed-file")]
    [DataRow("other-target")]
    [DataRow("processing-review")]
    public async Task StaleDownloadErrorNeverBypassesIncompleteOrUnprovenTargets(string blocked)
    {
        var (task, run, workset, download, _) = await SeedRevisedCompletedDownload("download.awaitCompletion");
        run.ErrorMessage = null;
        var target = workset.Targets[0] with {Problem = "BAIDU_OUTPUT_DIRECTORY_FAILED"};
        switch (blocked)
        {
            case "partial": download.Status = DownloadTaskDbModelStatus.Failed; break;
            case "missing-result": _db.DownloadResults.RemoveRange(_db.DownloadResults); break;
            case "missing-binding": _db.ArtifactOperationBindings.RemoveRange(_db.ArtifactOperationBindings); break;
            case "changed-file": await File.WriteAllTextAsync(Path.Combine(download.DownloadPath, "official.bin"), "changed"); break;
            case "processing-review": target = target with {ProcessingWaitReason = "manual-review"}; break;
        }
        var targets = new List<CloudDownloadTarget> {target};
        if (blocked == "other-target") targets.Add(target with {Id = "unknown", DownloadTaskId = null,
            Sources = [target.SelectedSource! with {Url = "https://pan.baidu.com/s/unknown"}]});
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = targets});
        await _db.SaveChangesAsync();
        Assert.IsFalse((await _diagnostics.DiagnoseAsync(task, run, default)).Row.CanContinue);
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task RevisedSiblingCannotAuthorizeAnUnknownTargetsUnavailableStorage()
    {
        var (task, run, workset, _, _) = await SeedRevisedCompletedDownload("download.awaitCompletion");
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [workset.Targets[0],
            workset.Targets[0] with {Id = "unknown", DownloadTaskId = null,
                Sources = [workset.Targets[0].SelectedSource! with {Url = "https://pan.baidu.com/s/unattempted"}]}]});
        var result = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsFalse(result.Row.CanContinue); Assert.AreEqual("RECOVERY_STORAGE_UNAVAILABLE", result.Row.ReasonCode);
    }
}
