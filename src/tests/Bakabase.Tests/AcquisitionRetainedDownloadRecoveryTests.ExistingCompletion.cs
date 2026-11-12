using Bakabase.Modules.Workflow.Abstractions.Components;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Security.Cryptography;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.Extensions.DependencyInjection;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [TestMethod]
    public async Task CompletionRegistrationFailureOffersVerificationOnlyWithoutRetryOrTrustingProgress()
    {
        var (run, target, download, _, _, provider, service) = await SeedRetainedDownload();
        download.Message = "System.IO.IOException: An artifact file changed while its manifest was being recorded.\n at DownloadResultService.RecordFilesAsync\n at CloudSiteDownloader.StartCore";
        download.Progress = 73; await _db.SaveChangesAsync();
        var result = await service.CheckAsync(run.Id, target, _root, true, default);
        Assert.IsTrue(result.CanRetry); Assert.IsTrue(result.VerifyExistingOnly); Assert.IsTrue(result.Candidate);
        Assert.AreEqual("RECOVERY_EXISTING_FILES_VERIFY_REQUIRED", result.ReasonCode);
        CollectionAssert.AreEqual(new[] {false}, provider.Checks);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
    }

    [TestMethod]
    public async Task CompletionVerificationRejectsSharedOwnershipAndUnrelatedFailures()
    {
        var (run, target, download, operation, _, provider, service) = await SeedRetainedDownload();
        download.Message = "An artifact file changed while its manifest was being recorded."; await _db.SaveChangesAsync();
        Assert.IsFalse((await service.CheckAsync(run.Id, target, _root, true, default)).CanRetry);
        download.Message += " DownloadResultService.RecordFilesAsync CloudSiteDownloader";
        _db.ArtifactOperationBindings.Add(new() {Id = "other-owner", OperationId = operation.Id, OwnerKind = "workflow", OwnerId = "other", Slot = "other:download"});
        await _db.SaveChangesAsync();
        Assert.IsFalse((await service.CheckAsync(run.Id, target, _root, true, default)).CanRetry);
        Assert.AreEqual(0, provider.Checks.Count);
    }

    [TestMethod]
    public async Task DiagnosisOffersOnlyExistingFileVerificationAndCompletionNeverEnqueuesAnotherDownload()
    {
        var (run, target, download, operation, transfer, provider, service) = await SeedRetainedDownload();
        download.Key = "original";
        download.Message = "An artifact file changed while its manifest was being recorded. DownloadResultService.RecordFilesAsync CloudSiteDownloader";
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)! with {Targets = [target with {Problem = download.Message}]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset); await _db.SaveChangesAsync();
        var task = await _db.AcquisitionTasks.SingleAsync(t => t.WorkflowRunId == run.Id);
        _diagnostics = new(_db, new UserStoragePolicy(false), _services.GetRequiredService<IArtifactOperationStore>(), downloads: service);
        var diagnosis = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.IsTrue(diagnosis.Row.CanContinue, diagnosis.Row.ReasonCode);
        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(diagnosis.SignalJson, JsonSerializerOptions.Web)!;
        Assert.IsTrue(signal.VerifyExistingFilesOnly == true); Assert.IsFalse(signal.RevalidateExistingDownloads == true);
        CollectionAssert.AreEqual(new[] {target.Id}, signal.ResumeTargetIds!);
        var file = Path.Combine(download.DownloadPath, "retained-fixture");
        var bytes = await File.ReadAllBytesAsync(file);
        provider.ExistingProof = new([new(file, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant())]);
        var originalOperation = JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id));
        var originalTransfer = JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == transfer.Id));
        await service.VerifyExistingAsync(run.Id, target, _root, _services.GetRequiredService<DownloadTaskService>(), default);
        Assert.AreEqual(1, provider.VerificationCalls);
        Assert.AreEqual(DownloadTaskDbModelStatus.Complete, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(1, await _db.DownloadResults.CountAsync()); Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(originalOperation, JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id)));
        Assert.AreEqual(originalTransfer, JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == transfer.Id)));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.VerifyExistingAsync(run.Id, target, _root,
            _services.GetRequiredService<DownloadTaskService>(), default));
        Assert.AreEqual(1, provider.VerificationCalls); Assert.IsTrue(File.Exists(file));
    }

    [TestMethod]
    public async Task VerificationCandidateCannotBeReinterpretedAsPermissionToRestartDownloader()
    {
        var (run, target, download, _, _, provider, service) = await SeedRetainedDownload();
        download.Message = "An artifact file changed while its manifest was being recorded. DownloadResultService.RecordFilesAsync CloudSiteDownloader";
        await _db.SaveChangesAsync();
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)! with {Targets = [target]};
        using var services = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
        var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = "{}",
            Services = services, Logger = NullLogger.Instance};
        var signal = JsonSerializer.Serialize(new CloudSelectionSignal {ObserveExistingOnly = true,
            ResumeTargetIds = [target.Id], RevalidateExistingDownloads = true}, JsonSerializerOptions.Web);
        var outcome = await new AwaitCloudDownloadsActivity().ResumeAsync(context, workset, signal, default);
        Assert.IsNotNull(outcome.Suspension); Assert.AreEqual(0, provider.VerificationCalls);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
    }
}
