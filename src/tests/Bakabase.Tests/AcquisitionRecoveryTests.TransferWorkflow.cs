using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [TestMethod]
    public async Task Continue_at_the_transfer_cursor_prepares_one_provider_checked_retry_without_a_checkbox()
    {
        var (task, run, workset) = await Seed();
        var owner = task.Id.ToString();
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition",
            EntityKind = "task", EntityId = owner, Revision = "1"});
        workset = workset with {Id = "acquisition:" + owner + ":1"};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        await _db.SaveChangesAsync();
        var provider = new CloudWorkflowTests.TransferRecoveryFixture
            {Evidence = _ => new(CloudTransferRecoveryDisposition.RequiresConfirmation)};
        var storage = new UserStoragePolicy(false);
        var operations = new CloudOperationService(_db, [provider], null!, null!, storage);
        var target = workset.Targets.Single();
        await Assert.ThrowsExceptionAsync<IOException>(() => operations.TransferAsync(run.Id, target, "account", "/Bakabase", default));
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, storage, _services.GetRequiredService<IArtifactOperationStore>(), transfers: operations);
        var host = new RecordingHost();
        var recovery = new AcquisitionRecoveryService(_db, diagnostics, new(), host, transfers: operations);
        var preview = await recovery.PreviewAsync(new([task.Id]), default);
        Assert.AreEqual(AcquisitionRecoveryDiagnostics.TransferRecoveryReady, preview.Rows.Single().ReasonCode);
        Assert.IsFalse((await diagnostics.DiagnoseTransferRetryAsync(task, run, default)).Row.CanContinue);
        Assert.HasCount(1, provider.Requests);
        var continued = await recovery.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        CollectionAssert.AreEqual(new[] {task.Id}, continued.ContinuedTaskIds.ToArray());
        Assert.AreEqual(1, host.Calls);
        Assert.AreEqual(1, await _db.CloudDownloadOperations.CountAsync(o => o.Kind == "transfer-recovery" && o.State == "planned"));
        Assert.HasCount(1, provider.Requests);
    }
    [TestMethod]
    public async Task A_later_enqueue_requires_explicit_transfer_selection_and_uses_a_separate_receipt()
    {
        var (task, run, workset) = await Seed();
        var owner = task.Id.ToString();
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition",
            EntityKind = "task", EntityId = owner, Revision = "1"});
        var provider = new CloudWorkflowTests.TransferRecoveryFixture {TransferError = null};
        var storage = new UserStoragePolicy(false);
        var operations = new CloudOperationService(_db, [provider], null!, null!, storage);
        var target = workset.Targets.Single();
        var original = await operations.TransferAsync(run.Id, target, "account", "/Bakabase", default);
        workset = workset with {Id = "acquisition:" + owner + ":1", Targets = [target with {RemoteEntry = original.Entry}]};
        run.CurrentStepIndex = 2;
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Later enqueue", TriggerKind = CloudWorkflow.Trigger, Nodes =
        [
            new() {NodeId = "save", Kind = "baidu.transfer"},
            new() {NodeId = "select", Kind = "download.selectSources"},
            new() {NodeId = "download", Kind = "download.enqueueSupported"}
        ]});
        await _db.SaveChangesAsync();
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, storage, _services.GetRequiredService<IArtifactOperationStore>(), transfers: operations);
        var host = new RecordingHost();
        var recovery = new AcquisitionRecoveryService(_db, diagnostics, new(), host, transfers: operations);
        var preview = await recovery.PreviewAsync(new([task.Id], "retryTransfer"), default);
        Assert.IsTrue(preview.Rows.Single().CanContinue);
        var reason = AcquisitionRecoveryDiagnostics.TransferRetryConfirmationRequired;
        var skipped = await recovery.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        Assert.AreEqual(reason, skipped.Skipped.Single().ReasonCode);
        Assert.HasCount(1, provider.Requests);
        Assert.IsFalse(await _db.CloudDownloadOperations.AnyAsync(o => o.Kind == CloudOperationService.ReplayKind));
        var retry = await recovery.PreviewAsync(new([task.Id], "retryTransfer"), default);
        var actual = await recovery.ApplyBatchAsync(new(retry.PreviewToken, [task.Id]), null, default, transferRetryConfirmed: true);
        CollectionAssert.AreEqual(new[] {task.Id}, actual.ContinuedTaskIds.ToArray());
        Assert.AreEqual(1, host.Calls);
        var replay = await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Kind == CloudOperationService.ReplayKind);
        Assert.AreEqual("planned", replay.State);
        var proof = System.Text.Json.JsonSerializer.Deserialize<CloudSelectionSignal>(host.Signal!,
            Bakabase.Modules.Workflow.Abstractions.Components.WorkflowJson.Options);
        Assert.AreEqual(replay.Id, proof!.TransferReplay!.Attempts[target.Id]);
        Assert.AreEqual(original.OperationId, (await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Kind == "transfer")).Id);
    }

}
