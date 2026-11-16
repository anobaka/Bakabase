using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.TestKit.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow("first-download")]
    [DataRow("remounted-parent")]
    [DataRow("old-partial-file")]
    [DataRow("batch-late-files")]
    [DataRow("foreign-same-url")]
    [DataRow("already-dispatched")]
    [DataRow("bad-receipt")]
    [DataRow("wrong-account")]
    [DataRow("files-appear")]
    [DataRow("competing-dispatch")]
    public async Task Transferred_share_recovers_only_the_first_enqueue_of_the_original_run(string scenario)
    {
        var (task, run, saved) = await Seed("download.enqueueSupported");
        var storage = new UserStoragePolicy(false);
        var target = saved.Targets.Single();
        var newRoot = Directory.CreateDirectory(Path.Combine(_root, "new-downloads")).FullName;
        var old = Path.Combine(_root, "old-volume", "Downloads");
        var source = target.SelectedSource!;
        var key = CloudOperationService.Key("transfer.v2", DownloadArtifactCoordinator.Source(target), "/Bakabase");
        var remote = new CloudRemoteEntry("baidu", "account", "/Bakabase/" + key);
        var workset = saved with {OutputDirectory = old, Targets = [target with {RemoteEntry = remote}]};
        run.CurrentStepIndex = 1;
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "transfer then enqueue", TriggerKind = CloudWorkflow.Trigger,
            Nodes = [new() {NodeId = "transfer", Kind = "baidu.transfer"}, new() {NodeId = "download", Kind = "download.enqueueSupported"}]});
        task.PlanJson = JsonSerializer.Serialize(new AcquisitionPlan {Title = "Fixture", OutputDirectory = old,
            Parts = [new() {Id = target.Id, Title = "Payload", SelectedSourceId = target.SelectedSourceId, Sources = [new()
                {Id = source.Id, Url = source.Url, ProviderId = source.ProviderId, AccessCode = source.AccessCode, AccountId = source.AccountId}]}]}, WorkflowJson.Options);
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()});
        var operation = _db.CloudDownloadOperations.Add(new() {Id = key, WorkflowRunId = run.Id, TargetId = target.Id, Kind = "transfer", State = "succeeded",
            RequestJson = JsonSerializer.Serialize(new CloudTransferRequest(key, source, "account", remote.Path), WorkflowJson.Options),
            ReceiptJson = JsonSerializer.Serialize(new CloudTransferReceipt(key, remote), WorkflowJson.Options)}).Entity;
        await _db.SaveChangesAsync();
        Assert.IsTrue(await new ArtifactOperationBindingService(_db).BindAsync("workflow", run.Id.ToString(), target.Id + ":transfer", key));
        var options = new TestBOptions<AcquisitionOptions>(new() {DownloadRootDirectory = newRoot,
            ProcessingWorkspaceDirectory = Directory.CreateDirectory(Path.Combine(_root, "processing")).FullName});
        var resolver = new FileProcessingWorkspaceResolver(_root, storage);
        var first = new AcquisitionTransferredDownloadRecovery(_db, storage,
            new(new TestBOptions<DownloaderGlobalOptions>(new()), options), new(storage, options), new(options, storage), resolver);
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, storage, _services.GetRequiredService<IArtifactOperationStore>(), transferredDownloads: first);
        void AddNative() => _db.CloudDownloadOperations.Add(new()
        {
            Id = "existing", WorkflowRunId = scenario == "foreign-same-url" ? 999 : run.Id, TargetId = "other", Kind = "download",
            State = "starting", RequestJson = JsonSerializer.Serialize(new {Source = source}, WorkflowJson.Options)
        });
        if (scenario is "foreign-same-url" or "already-dispatched") AddNative();
        if (scenario is "remounted-parent" or "old-partial-file")
        {
            Directory.CreateDirectory(old);
            await File.WriteAllTextAsync(Path.Combine(old, "other-task.bin"), "unrelated");
            if (scenario == "old-partial-file")
            {
                var intent = DownloadArtifactCoordinator.DownloadIntent(workset.Targets.Single(), old, resolver);
                var prior = Directory.CreateDirectory(Path.Combine(old, "cloud-" + intent[..16]));
                await File.WriteAllTextAsync(Path.Combine(prior.FullName, "volume.partial"), "do not redownload");
            }
        }
        if (scenario == "bad-receipt") operation.ReceiptJson = JsonSerializer.Serialize(new CloudTransferReceipt(key, remote with {Path = "/different"}), WorkflowJson.Options);
        if (scenario == "wrong-account") operation.RequestJson = JsonSerializer.Serialize(new CloudTransferRequest(key, source, "other", remote.Path), WorkflowJson.Options);
        await _db.SaveChangesAsync();
        var diagnosis = await diagnostics.DiagnoseAsync(task, run, default);
        var accepted = scenario is "first-download" or "remounted-parent" or "batch-late-files" or "files-appear" or "competing-dispatch";
        Assert.AreEqual(accepted, diagnosis.Row.CanContinue, diagnosis.Row.ReasonCode);
        if (!accepted) return;
        Assert.AreEqual("RECOVERY_TRANSFERRED_FIRST_DOWNLOAD", diagnosis.Row.ReasonCode);
        var proof = JsonSerializer.Deserialize<CloudSelectionSignal>(diagnosis.SignalJson, WorkflowJson.Options)!.TransferredDownloadPlacement!;
        var stage = diagnosis.InitialDownloadDirectories.First();
        Assert.IsFalse(Directory.Exists(stage));
        if (scenario == "files-appear")
        {
            Directory.CreateDirectory(stage); await File.WriteAllTextAsync(Path.Combine(stage, "original.bin"), "preserve");
        }
        if (scenario == "competing-dispatch") {AddNative(); await _db.SaveChangesAsync();}
        if (scenario is "files-appear" or "competing-dispatch")
        {
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => first.RestoreAsync(run.Id, workset, proof, default));
            return;
        }
        var restored = await first.RestoreAsync(run.Id, workset, proof, default);
        Assert.AreEqual(newRoot, restored.OutputDirectory);
        Assert.AreEqual(remote, restored.Targets.Single().RemoteEntry);
        Assert.AreEqual(true, restored.NoRepeatExistingDownloads);
        Assert.AreEqual(stage, restored.Targets.Single().DownloadDirectoryScope!.Directory);
        Assert.IsTrue(stage.StartsWith(newRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(1, await _db.CloudDownloadOperations.CountAsync());
        await VerifyTransferredDownloadBatchAsync(task.Id, run.Id, options, storage, diagnostics, diagnosis,
            scenario == "batch-late-files");
    }
}
