using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
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
    private async Task<DownloadWorkset> AddEquivalentDownloadSibling(WorkflowRunDbModel run, int taskId,
        DownloadWorkset workset, string operationId, string scenario = "same")
    {
        var owner = workset.Targets.Single();
        var source = owner.SelectedSource! with {Id = "same-resource-different-selection"};
        if (scenario == "source-changed") source = source with {MethodId = "different-adapter"};
        var sibling = owner with {Id = "sibling", Sources = [source], SelectedSourceId = source.Id, Excluded = scenario == "excluded"};
        // Deliberately place the sibling first to ensure the durable creator selects the dispatch.
        workset = workset with {Targets = [sibling, owner]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        run.WaitReason = "cloud.cloudAttention";
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition",
            EntityKind = "task", EntityId = taskId.ToString(), Revision = "1"});
        if (scenario != "missing-binding")
            _db.ArtifactOperationBindings.Add(new() {Id = "sibling-download", OwnerKind = "workflow", OwnerId = run.Id.ToString(),
                Slot = sibling.Id + ":download", OperationId = operationId});
        if (scenario == "foreign-binding")
            _db.ArtifactOperationBindings.Add(new() {Id = "outside-download", OwnerKind = "workflow", OwnerId = (run.Id + 1).ToString(),
                Slot = "outside:download", OperationId = operationId});
        if (scenario == "foreign-consumer")
            _db.Set<ArtifactConsumerDbModel>().Add(new() {Id = "outside-consumer", OperationId = operationId,
                ConsumerKey = "outside", OwnerKind = "workflow", OwnerId = (run.Id + 1).ToString(), LeaseUntil = DateTime.UtcNow.AddMinutes(10)});
        await _db.SaveChangesAsync();
        return workset;
    }

    [DataTestMethod]
    [DataRow("same", true)]
    [DataRow("missing-binding", false)]
    [DataRow("source-changed", false)]
    [DataRow("excluded", false)]
    [DataRow("foreign-binding", false)]
    [DataRow("foreign-consumer", false)]
    public async Task Equivalent_original_download_preview_validates_every_current_binding_before_probing(string scenario, bool ready)
    {
        var (task, run, workset, download, operation, provider, _) = await SeedRecoverableProvider();
        workset = await AddEquivalentDownloadSibling(run, task.Id, workset, operation.Id, scenario);
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), new RecordingHost());
        var result = await service.PreviewAsync(new([task.Id], "downloads"));
        Assert.AreEqual(ready, result.Rows.Single().CanContinue, result.Rows.Single().ReasonCode);
        Assert.AreEqual(ready ? 1 : 0, provider.Checks);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, download.Status);
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(1, await _db.CloudDownloadOperations.CountAsync());
    }

    [TestMethod]
    public async Task Equivalent_original_download_apply_starts_once_and_observes_the_same_completed_receipt()
    {
        var (task, run, workset, download, operation, provider, recovery) = await SeedRecoverableProvider();
        workset = await AddEquivalentDownloadSibling(run, task.Id, workset, operation.Id);
        var starter = new OnlyDownloadStarter(this, run.Id);
        starter.AfterStart = async () =>
        {
            Directory.CreateDirectory(download.DownloadPath);
            var file = Path.Combine(download.DownloadPath, "completed.bin");
            await File.WriteAllTextAsync(file, "same dispatch, two dependent parts");
            var manifest = await ArtifactManifestFiles.CaptureAsync(download.DownloadPath, [file], CancellationToken.None);
            _db.DownloadResults.Add(new() {DownloadTaskId = download.Id, DownloadDirectory = download.DownloadPath,
                FilesJson = JsonSerializer.Serialize(new[] {file}), Fingerprint = manifest.Fingerprint});
            await _db.SaveChangesAsync();
        };
        var host = new RecordingHost();
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host, downloadOnly: OnlyDownloads(recovery, starter, new()));
        var beforeCheckpoint = run.CurrentItemJson;
        var preview = await service.PreviewAsync(new([task.Id], "downloads"));
        Assert.AreEqual(1, preview.ContinuableCount, preview.Rows.Single().ReasonCode);
        var result = await service.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        CollectionAssert.AreEqual(new[] {task.Id}, result.ContinuedTaskIds.ToArray(), result.Skipped.FirstOrDefault()?.ReasonCode);
        CollectionAssert.AreEqual(new[] {download.Id}, starter.Calls);
        Assert.AreEqual(2, starter.LeaseChecks);
        Assert.AreEqual(0, host.Calls);
        Assert.AreEqual(beforeCheckpoint, (await _db.WorkflowRuns.AsNoTracking().SingleAsync()).CurrentItemJson);

        var resumed = await service.PreviewAsync(new([task.Id]));
        Assert.AreEqual(1, resumed.ContinuableCount, resumed.Rows.Single().ReasonCode);
        Assert.AreEqual("RECOVERY_COMPLETED_DOWNLOAD_READY", resumed.Rows.Single().ReasonCode);
        await service.ApplyAsync(new(resumed.PreviewToken, [task.Id]));
        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(host.Signal!, WorkflowJson.Options)!;
        Assert.IsTrue(signal.ObserveExistingOnly == true);
        Assert.IsNull(signal.ResumeTargetIds);
        CollectionAssert.AreEquivalent(new[] {download.Id}, signal.CompletedDownloadReceipts!.Keys.ToArray());
        CollectionAssert.AreEqual(new[] {download.Id}, starter.Calls);
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.IsTrue(provider.Checks >= 2);
    }

    [TestMethod]
    public async Task Equivalent_original_download_apply_rejects_a_sibling_binding_changed_under_the_start_lease()
    {
        var (task, run, workset, download, operation, _, recovery) = await SeedRecoverableProvider();
        await AddEquivalentDownloadSibling(run, task.Id, workset, operation.Id);
        var starter = new OnlyDownloadStarter(this, run.Id) {BetweenLeases = async () =>
        {
            await _db.ArtifactOperationBindings.Where(b => b.Id == "sibling-download").ExecuteDeleteAsync();
        }};
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), new RecordingHost(),
            downloadOnly: OnlyDownloads(recovery, starter, new()));
        var preview = await service.PreviewAsync(new([task.Id], "downloads"));
        Assert.AreEqual(1, preview.ContinuableCount, preview.Rows.Single().ReasonCode);
        var result = await service.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        Assert.AreEqual(0, result.ContinuedTaskIds.Count);
        Assert.AreEqual("RECOVERY_DOWNLOAD_IDENTITY_CHANGED", result.Downloads!.Single().ReasonCode);
        Assert.AreEqual(0, starter.Calls.Count);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await _db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == download.Id)).Status);
    }

    [TestMethod]
    public async Task Equivalent_workflow_resume_rechecks_once_and_preserves_all_siblings_on_provider_failure()
    {
        var (task, run, workset, download, operation, provider, recovery) = await SeedRecoverableProvider();
        workset = await AddEquivalentDownloadSibling(run, task.Id, workset, operation.Id);
        var diagnosis = await _diagnostics.DiagnoseAsync(task, run, default);
        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(diagnosis.SignalJson, WorkflowJson.Options)!;
        CollectionAssert.AreEqual(new[] {"part"}, signal.ResumeTargetIds!);
        provider.Available = false;
        provider.Checks = 0;
        using var services = new ServiceCollection().AddSingleton(recovery).BuildServiceProvider();
        var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = "{}",
            Services = services, Logger = NullLogger.Instance};
        var outcome = await new AwaitCloudDownloadsActivity().ResumeAsync(context, workset, diagnosis.SignalJson, default);
        Assert.IsNotNull(outcome.Suspension);
        Assert.AreEqual(1, provider.Checks);
        var saved = (DownloadWorkset)outcome.Suspension.Item!;
        Assert.IsTrue(saved.Targets.All(t => t.DownloadTaskId == download.Id && t.Problem == "PIKPAK_ORIGINAL_FILE_UNAVAILABLE"));
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(1, await _db.CloudDownloadOperations.CountAsync());
    }
}
