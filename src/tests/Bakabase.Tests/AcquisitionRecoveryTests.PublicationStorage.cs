using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow("processed", null)]
    [DataRow("download", null)]
    [DataRow("processed", "missingBinding")]
    [DataRow("processed", "otherOwner")]
    [DataRow("processed", "unfinishedOperation")]
    [DataRow("processed", "changedFile")]
    [DataRow("processed", "wrongIntent")]
    [DataRow("processed", "missingIntent")]
    [DataRow("processed", "wrongProducer")]
    [DataRow("download", "wrongIntent")]
    [DataRow("download", "wrongProducer")]
    [DataRow("download", "wrongSource")]
    public async Task PublicationUsesItsOwnedArtifactWithoutRevivingHistoricalDownloadStorage(string source, string? damage)
    {
        var (task, run, workset) = await Seed("fs.publishArtifacts", WorkflowRunStatus.Waiting, error: null);
        var missingOriginal = Path.Combine(_root, "disconnected-old-download-root");
        var path = Path.Combine(_root, "subtitle.lrc");
        await File.WriteAllTextAsync(path, "[00:01.00] retained local subtitle");
        var manifest = await ArtifactManifestFiles.CaptureAsync(_root, [path]);
        _db.ArtifactOperations.Add(new() {Id = "owned-output", IntentKey = source == "download"
                ? damage == "wrongIntent" ? "another-intent" : DownloadArtifactCoordinator.ArtifactIntent(workset.Targets[0], missingOriginal)
                : "completed-intent",
            InputFingerprint = damage == "wrongSource" ? "another-source" : DownloadArtifactCoordinator.Source(workset.Targets[0]),
            Kind = source == "download" ? "download" : "files.process", OutputArtifactId = "local-output",
            State = damage == "unfinishedOperation" ? ArtifactOperationState.Running : ArtifactOperationState.Completed,
            UpdatedAt = DateTime.UtcNow});
        _db.Set<ArtifactDbModel>().Add(new() {Id = "local-output", OperationId = "owned-output", RootPath = _root,
            ManifestJson = JsonSerializer.Serialize(manifest, JsonSerializerOptions.Web), Fingerprint = manifest.Fingerprint,
            State = ArtifactLocationState.Available, UpdatedAt = DateTime.UtcNow});
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task",
            EntityId = (damage == "otherOwner" ? task.Id + 1 : task.Id).ToString()});
        if (damage != "missingBinding") _db.ArtifactOperationBindings.Add(new() {OwnerKind = "workflow",
            OwnerId = run.Id.ToString(), Slot = source == "download" ? "part:artifact.download" : "part:process:final",
            OperationId = "owned-output"});
        await _db.SaveChangesAsync();
        var destination = Path.Combine(_root, "delivery");
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Publication on current storage", TriggerKind = CloudWorkflow.Trigger,
            Nodes = [new() {NodeId = "publish", Kind = "fs.publishArtifacts", ConfigJson = JsonSerializer.Serialize(new
                {source, targetDirectory = destination})}]});
        run.WaitReason = "artifacts.publication";
        run.WaitPromptJson = JsonSerializer.Serialize(new {targetId = "part", reason = "StorageUnavailable", message = "LOCAL_STORAGE_RESERVE_LOW"});
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {OutputDirectory = missingOriginal,
            Targets = [workset.Targets[0] with {DownloadArtifactId = source == "download" ? "local-output" : null,
                DownloadOperationId = damage == "wrongProducer" ? "another-output" : "owned-output",
                ProcessedArtifactId = source == "processed" ? "local-output" : null,
                ProcessingOperationId = damage == "wrongProducer" ? "another-output" : "owned-output",
                ProcessingIntentKey = damage == "wrongIntent" ? "another-intent" : damage == "missingIntent" ? null : "completed-intent",
                PublicationWaitReason = "StorageUnavailable", Problem = "LOCAL_STORAGE_RESERVE_LOW"}]});
        if (damage == "changedFile") await File.WriteAllTextAsync(path, "changed");
        var diagnosis = await _diagnostics.DiagnoseAsync(task, run, default);
        // A missing derived processing intent is recoverable when the bound completed
        // operation and artifact are otherwise exact; the activity can derive the intent
        // from that operation. Conflicting identity/binding damage remains blocked.
        Assert.AreEqual(damage is null or "missingIntent", diagnosis.Row.CanContinue, diagnosis.Row.ReasonCode);
        Assert.IsFalse(Directory.Exists(missingOriginal));
        Assert.IsFalse(Directory.Exists(destination));
        Assert.AreEqual(0, _db.CloudDownloadOperations.Count());
        Assert.AreEqual(0, _db.DownloadTasks.Count());
    }
}
