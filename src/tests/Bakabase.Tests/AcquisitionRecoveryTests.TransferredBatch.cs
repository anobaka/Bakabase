using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.TestKit.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private async Task VerifyTransferredDownloadBatchAsync(int taskId, int runId, TestBOptions<AcquisitionOptions> options,
        UserStoragePolicy storage, AcquisitionRecoveryDiagnostics diagnostics, AcquisitionRecoveryDiagnosis diagnosis,
        bool filesAppear)
    {
        var artifacts = _services.GetRequiredService<IArtifactOperationStore>();
        var plans = new AcquisitionPlanService(_db, [], null!, null!, null!, storage, artifacts: artifacts,
            deliveryDirectories: new(storage, options), storageDefaults: new(options, storage));
        var local = new AcquisitionLocalFilesRecoveryService(_db, plans, storage, artifacts, null!, _services);
        var host = new ProcessingRecoveryHost();
        var recovery = new AcquisitionRecoveryService(_db, diagnostics, new(), host, plans, localFiles: local);
        var continuation = new AcquisitionContinuationService(_db, recovery, new(), null!, null!, artifacts);
        var batch = new AcquisitionLocalRecoveryBatchService(_db, recovery, diagnostics, local, plans, options,
            storage, new PinnedBatchCapacity(), new(), continuations: continuation);
        var capacity = await batch.PreviewAsync(new([taskId], "capacityOnly"));
        CollectionAssert.AreEquivalent(diagnosis.InitialDownloadTargets.ToArray(), capacity.Volumes.SelectMany(v => v.Paths).ToArray());
        var preview = await batch.PreviewAsync(new([taskId], "automatic"));
        var row = preview.Rows.Single();
        Assert.IsTrue(row.CanQueue, row.ReasonCode);
        Assert.AreEqual("workflow", row.ActionCategory);
        Assert.AreEqual(diagnosis.FirstDownloadDirectory, row.WorkingDirectory);
        Assert.AreEqual(diagnosis.FirstDeliveryDirectory, row.DeliveryDirectory);
        CollectionAssert.AreEquivalent(diagnosis.InitialDownloadDirectories.Distinct().ToArray(),
            preview.Volumes.SelectMany(v => v.Paths).Distinct().ToArray());
        var queue = await batch.ApplyAsync(new(preview.PreviewToken, [taskId]));
        if (filesAppear)
        {
            Directory.CreateDirectory(row.WorkingDirectory!);
            await File.WriteAllTextAsync(Path.Combine(row.WorkingDirectory!, "user.bin"), "keep");
        }
        await batch.TickAsync();
        var outcome = (await batch.GetAsync(queue.BatchId)).Rows.Single();
        if (filesAppear)
        {
            Assert.AreEqual(0, host.Calls, outcome.ReasonCode);
            Assert.AreEqual("keep", await File.ReadAllTextAsync(Path.Combine(row.WorkingDirectory!, "user.bin")));
        }
        else
        {
            Assert.AreEqual(1, host.Calls, outcome.ReasonCode);
            Assert.AreEqual(runId, host.RunId);
            var expected = JsonSerializer.Deserialize<CloudSelectionSignal>(diagnosis.SignalJson, WorkflowJson.Options);
            var resumed = JsonSerializer.Deserialize<CloudSelectionSignal>(host.Signal!, WorkflowJson.Options);
            Assert.AreEqual(expected!.TransferredDownloadPlacement, resumed!.TransferredDownloadPlacement);
        }
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(1, await _db.CloudDownloadOperations.CountAsync());
    }
}
