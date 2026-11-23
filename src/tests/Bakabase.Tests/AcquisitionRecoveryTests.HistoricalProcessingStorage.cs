using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private async Task<ProcessingFixture> SeedHistoricalProcessingStorage(string? scenario = null)
    {
        var f = await SeedProcessingStorage();
        var download = new DownloadTaskDbModel {DownloadPath = _root,
            Status = scenario == "downloadNotComplete" ? DownloadTaskDbModelStatus.Failed : DownloadTaskDbModelStatus.Complete};
        _db.DownloadTasks.Add(download);
        await _db.SaveChangesAsync();
        var target = f.Workset.Targets.Single();
        f.Task.Status = AcquisitionStatus.Failed;
        f.Run.Status = WorkflowRunStatus.Failed;
        f.Run.CompletedAt = DateTime.UtcNow;
        f.Run.WaitPromptJson = null;
        f.Run.WaitReason = null;
        f.Run.WaitingSince = null;
        f.Run.ErrorMessage = "Reconnect the mounted storage location. " + (scenario == "outsideMount" ? _root + "-unrelated" : _root);
        f.Run.CurrentItemJson = WorkflowItemSnapshot.Capture(f.Workset with {Id = $"acquisition:{f.Task.Id}:{f.Task.PlanRevision}",
            Targets = [target with {DownloadTaskId = download.Id,
                ProcessingPlanJson = scenario == "planChanged" ? "{\"requirement\":\"notRequired\"}" : target.ProcessingPlanJson}]});
        var node = WorkflowRunPlans.Read(f.Run.PlanSnapshotJson)!.Nodes[0];
        if (scenario != "noPreviousWait")
            _db.Set<WorkflowNodeExecutionDbModel>().Add(new() {WorkflowRunId = f.Run.Id, NodeId = node.NodeId,
                Kind = node.Kind, StepIndex = 0, Attempt = 1, Status = "waiting", WaitReason = "cloud.fileProcessing",
                CompletedAt = DateTime.UtcNow});
        _db.Set<WorkflowNodeExecutionDbModel>().Add(new() {WorkflowRunId = f.Run.Id, NodeId = node.NodeId,
            Kind = node.Kind, StepIndex = 0, Attempt = 2, Status = "failed", ResumeKind = "signal", CompletedAt = f.Run.CompletedAt});
        if (scenario == "sharedRun")
            _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = f.Run.Id, Module = "acquisition", EntityKind = "task",
                EntityId = (f.Task.Id + 1).ToString(), Revision = "1"});
        if (scenario == "missingCheckpoint") File.Delete(Path.Combine(f.Checkpoint, "state.json"));
        await _db.SaveChangesAsync();
        return f;
    }

    [TestMethod]
    public async Task HistoricalProcessingStorageRetriesOnlyTheOriginalSuspendedAttempt()
    {
        var f = await SeedHistoricalProcessingStorage();
        var host = new ProcessingRecoveryHost();
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var preview = await service.PreviewAsync(new([f.Task.Id]));
        var row = preview.Rows.Single();
        Assert.IsTrue(row.CanContinue, row.ReasonCode);
        Assert.AreEqual("retry", row.Action);
        Assert.AreEqual("RECOVERY_STORAGE_UNAVAILABLE", row.CurrentProblemCode);
        Assert.AreEqual("RECOVERY_FILES_VERIFIED", row.ReasonCode);
        var applied = await service.ApplyAsync(new(preview.PreviewToken, [f.Task.Id]));
        CollectionAssert.AreEqual(new[] {f.Task.Id}, applied.ContinuedTaskIds.ToArray());
        Assert.AreEqual(1, host.Calls);
        Assert.AreEqual(f.Run.Id, host.RunId);
        Assert.IsFalse(host.Waiting);
        var signal = JsonSerializer.Deserialize<CloudFileProcessingSignal>(host.Signal!, WorkflowJson.Options)!;
        Assert.AreEqual("part", signal.TargetId);
        Assert.AreEqual(new FileArtifactProcessingRecovery(f.OperationId, 1), signal.StorageRecovery);
        Assert.IsNull(signal.Password);
        Assert.IsNull(signal.ExtractionPlanJson);
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(0, await _db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(1, (await _db.ArtifactOperations.AsNoTracking().SingleAsync(o => o.Id == f.OperationId)).Attempt);
    }

    [DataTestMethod]
    [DataRow("noPreviousWait")]
    [DataRow("downloadNotComplete")]
    [DataRow("outsideMount")]
    [DataRow("planChanged")]
    [DataRow("sharedRun")]
    [DataRow("missingCheckpoint")]
    public async Task HistoricalProcessingStorageRejectsUnprovenContinuation(string scenario)
    {
        var f = await SeedHistoricalProcessingStorage(scenario);
        var diagnosis = await _diagnostics.DiagnoseAsync(f.Task, f.Run, default, verifyContent: true);
        Assert.IsFalse(diagnosis.Row.CanContinue, scenario);
    }

    [TestMethod]
    public async Task HistoricalProcessingStorageRejectsAnUnapprovedMount()
    {
        var f = await SeedHistoricalProcessingStorage();
        var policy = new RecordingProcessingStoragePolicy(_root);
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, policy,
            _services.GetRequiredService<IArtifactOperationStore>(), workspaces: f.Workspaces);
        var diagnosis = await diagnostics.DiagnoseAsync(f.Task, f.Run, default);
        Assert.AreEqual("RECOVERY_STORAGE_UNAVAILABLE", diagnosis.Row.ReasonCode);
        Assert.IsFalse(diagnosis.Row.CanContinue);
    }
}
