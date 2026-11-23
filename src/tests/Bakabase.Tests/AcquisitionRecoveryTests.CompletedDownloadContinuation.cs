using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Completed_download_batch_dispatches_from_the_receipt_and_verifies_bytes_in_the_workflow(bool changed)
    {
        var (task, run, workset, download, _) = await SeedRevisedCompletedDownload("download.awaitCompletion");
        await CompletionIntent(task, "downloadsOnly");
        var file = Path.Combine(download.DownloadPath, "official.bin");
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var host = new RecordingHost();
        var recovery = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        if (changed) await File.WriteAllTextAsync(file, new string('x', (int)new FileInfo(file).Length));

        var preview = await recovery.PreviewAsync(new([task.Id], "workflow"));
        var decision = preview.Rows.Single();
        Assert.IsTrue(decision.CanContinue, decision.ReasonCode);
        Assert.AreEqual("RECOVERY_COMPLETED_DOWNLOAD_READY", decision.ReasonCode);
        var continued = await recovery.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        CollectionAssert.AreEqual(new[] {task.Id}, continued.ContinuedTaskIds.ToArray());
        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(host.Signal!, JsonSerializerOptions.Web)!;
        Assert.IsTrue(signal.ObserveExistingOnly == true);
        CollectionAssert.AreEqual(new[] {download.Id}, signal.CompletedDownloadReceipts!.Keys.ToArray());

        var storage = new UserStoragePolicy(false);
        var operations = new CloudOperationService(_db, [], null!, null!, storage);
        var coordinator = new DownloadArtifactCoordinator(_db, store, storage, _services.GetRequiredService<DownloadResultService>());
        using var services = new ServiceCollection().AddSingleton(_db).AddSingleton(operations).AddSingleton(coordinator)
            .AddSingleton<IUserStoragePolicy>(storage).BuildServiceProvider();
        var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger,
            Payload = workset, Services = services, ActivityConfigJson = "{}", Logger = NullLogger.Instance};
        var outcome = await new AwaitCloudDownloadsActivity().ResumeAsync(context, workset, host.Signal!, default);

        if (changed)
        {
            Assert.IsNotNull(outcome.Suspension);
            var stopped = ((DownloadWorkset)outcome.Suspension.Item!).Targets.Single();
            Assert.AreEqual("RECOVERY_FILES_UNVERIFIED", stopped.Problem);
            Assert.AreEqual(0, stopped.Files.Count);
            Assert.AreEqual(0, await _db.Artifacts.CountAsync());
        }
        else
        {
            Assert.IsNull(outcome.Suspension, JsonSerializer.Serialize(outcome.Suspension));
            var executed = ((DownloadWorkset)outcome.Replacement!).Targets.Single();
            Assert.IsNotNull(executed.DownloadArtifactId);
            CollectionAssert.AreEqual(new[] {file}, executed.Files.ToArray());
        }
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(1, await _db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(1, await _db.DownloadResults.CountAsync());
    }
}
