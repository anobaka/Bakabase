using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private sealed class SharedProcessingVolume(long available) : IAcquisitionBatchCapacityReader
    {
        public long Available {get; set;} = available;
        public StorageVolumeCapacity Measure(string path) => new(path, Available, long.MaxValue / 2);
        public string PhysicalVolume {get; set;} = "same-physical-volume";
        public string Identity(StorageVolumeCapacity capacity) => PhysicalVolume;
    }

    [TestMethod]
    [DataRow("localOnly")]
    [DataRow("automatic")]
    public async Task BatchExtractionCapacityContinuesTheBoundGroupAndRetainsTheFullWorkflowEstimate(string mode)
    {
        var (f, batch, host, capacity, bytes, _) = await SeedExtractionCapacityBatch();
        var preview = await batch.PreviewAsync(new([f.Task.Id], mode));
        var row = preview.Rows.Single();
        Assert.IsTrue(row.CanQueue, row.ReasonCode);
        Assert.AreEqual(bytes, row.EstimatedBytes);
        var selected = await batch.PreviewCapacityAsync(new(preview.PreviewToken, [f.Task.Id]));
        foreach (var volume in new[] {preview.Volumes.Single(), selected.Volumes.Single()})
        {
            Assert.AreEqual(2 * bytes, volume.EstimatedAdditionalBytes);
            Assert.IsTrue(volume.ExceedsUsable);
            Assert.AreEqual(capacity.Available, volume.AvailableBytes);
        }
        Assert.IsFalse(preview.RequiresSpaceConfirmation);
        Assert.IsFalse(selected.RequiresSpaceConfirmation);
        var queue = await batch.ApplyAsync(new(preview.PreviewToken, [f.Task.Id]));
        await batch.TickAsync();
        Assert.AreEqual(1, host.Calls, (await batch.GetAsync(queue.BatchId)).Rows.Single().ReasonCode);
        var signal = JsonSerializer.Deserialize<CloudFileProcessingSignal>(host.Signal!, WorkflowJson.Options)!;
        Assert.AreEqual(new FileArtifactProcessingRecovery(f.OperationId, 1), signal.StorageRecovery);
        Assert.AreEqual(f.Run.Id, host.RunId);
        Assert.IsNull(signal.ExtractionPlanJson);
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await _db.AcquisitionTasks.AsNoTracking().SingleAsync()));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BatchExtractionCapacityUsesDeclaredSizeForOnlyTheCurrentGroup(bool oneByteShort)
    {
        const long expanded = 2 * 1024 * 1024;
        var (f, batch, host, capacity, bytes, reserve) = await SeedExtractionCapacityBatch(expandedBytes: expanded);
        capacity.Available = reserve + StorageWriteGuard.ReservationHeadroomBytes + expanded - (oneByteShort ? 1 : 0);
        var preview = await batch.PreviewAsync(new([f.Task.Id]));
        Assert.IsTrue(preview.Rows.Single().CanQueue);
        var selected = await batch.PreviewCapacityAsync(new(preview.PreviewToken, [f.Task.Id]));
        Assert.AreEqual(2 * bytes, selected.Volumes.Single().EstimatedAdditionalBytes);
        Assert.IsFalse(selected.Volumes.Single().ExceedsUsable);
        Assert.AreEqual(oneByteShort, selected.RequiresSpaceConfirmation);
        var queue = await batch.ApplyAsync(new(preview.PreviewToken, [f.Task.Id]));
        await batch.TickAsync();
        Assert.AreEqual(oneByteShort ? 0 : 1, host.Calls, (await batch.GetAsync(queue.BatchId)).Rows.Single().ReasonCode);
    }

    [TestMethod]
    [DataRow("noState")]
    [DataRow("emptyState")]
    [DataRow("foreignManifest")]
    [DataRow("detachedGroup")]
    [DataRow("externalOutput")]
    [DataRow("completed")]
    [DataRow("pendingInput")]
    [DataRow("unstarted")]
    [DataRow("renameOnly")]
    public async Task BatchExtractionCapacityRequiresTheExactUntouchedContinuation(string defect)
    {
        var (f, batch, host, _, _, _) = await SeedExtractionCapacityBatch(defect);
        var preview = await batch.PreviewAsync(new([f.Task.Id]));
        if (preview.Rows.Single().CanQueue)
        {
            var space = await batch.PreviewCapacityAsync(new(preview.PreviewToken, [f.Task.Id]));
            Assert.IsTrue(space.RequiresSpaceConfirmation, defect);
            var queue = await batch.ApplyAsync(new(preview.PreviewToken, [f.Task.Id]));
            await batch.TickAsync();
            Assert.AreEqual("spaceBlocked", (await batch.GetAsync(queue.BatchId)).Status, defect);
        }
        Assert.AreEqual(0, host.Calls, defect);
    }

    [TestMethod]
    public async Task BatchExtractionCapacityProtectsTheFrozenDeliveryPathDuringSubsetReview()
    {
        var (f, batch, host, _, _, _) = await SeedExtractionCapacityBatch();
        var preview = await batch.PreviewAsync(new([f.Task.Id]));
        var delivery = preview.Rows.Single().DeliveryDirectory;
        Assert.IsNotNull(delivery);
        var visited = new List<string?>();
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
            batch.PreviewCapacityAsync(new(preview.PreviewToken, [f.Task.Id]), guardDirectory: path =>
            {
                visited.Add(path);
                if (path == delivery) throw new UnauthorizedAccessException("Revoked delivery location.");
                return Task.CompletedTask;
            }));
        CollectionAssert.Contains(visited.ToArray(), delivery);
        Assert.AreEqual(0, host.Calls);
        Assert.IsNull(AcquisitionLocalRecoveryQueueEntry.Read(await _db.AcquisitionTasks.AsNoTracking().SingleAsync()));
    }

    [TestMethod]
    [DataRow("floor")]
    [DataRow("missingVolume")]
    [DataRow("replacedVolume")]
    [DataRow("removedState")]
    public async Task BatchExtractionCapacityRechecksTheLiveVolumeAndCheckpointBeforeDispatch(string change)
    {
        var (f, batch, host, capacity, _, reserve) = await SeedExtractionCapacityBatch();
        var preview = await batch.PreviewAsync(new([f.Task.Id]));
        Assert.IsTrue(preview.Rows.Single().CanQueue);
        var queue = await batch.ApplyAsync(new(preview.PreviewToken, [f.Task.Id]));
        if (change == "removedState") File.Delete(Path.Combine(f.Checkpoint, "state.json"));
        else if (change == "replacedVolume") capacity.PhysicalVolume = "replacement-volume";
        else capacity.Available = change == "floor" ? reserve : -1;
        await batch.TickAsync();
        Assert.AreEqual(0, host.Calls, change);
        Assert.AreNotEqual("started", (await batch.GetAsync(queue.BatchId)).Rows.Single().Status);
    }

    [TestMethod]
    [DataRow("queued", "automatic", "valid")]
    [DataRow("queued", "localOnly", "valid")]
    [DataRow("spaceBlocked", "automatic", "valid")]
    [DataRow("spaceBlocked", "localOnly", "valid")]
    [DataRow("queued", "automatic", "missingState")]
    [DataRow("queued", "automatic", "floor")]
    public async Task BatchExtractionCapacityRevalidatesQueuesStoredBeforeCurrentGroupEvidence(
        string previousStatus, string mode, string checkpoint)
    {
        var (f, batch, host, capacity, _, reserve) = await SeedExtractionCapacityBatch();
        var preview = await batch.PreviewAsync(new([f.Task.Id], mode));
        var queued = await batch.ApplyAsync(new(preview.PreviewToken, [f.Task.Id]));
        var persisted = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task.Id);
        var builder = JsonNode.Parse(persisted.BuilderStateJson!)!;
        var previous = builder["localRecoveryQueue"]!.AsObject();
        Assert.IsTrue(previous.Remove("checkpointWrite"));
        previous["status"] = previousStatus;
        if (previousStatus == "spaceBlocked") previous["reasonCode"] = StorageWriteGuard.ReserveLowCode;
        var legacyJson = builder.ToJsonString();
        await _db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, legacyJson));
        if (checkpoint == "missingState") File.Delete(Path.Combine(f.Checkpoint, "state.json"));
        if (checkpoint == "floor") capacity.Available = reserve;

        if (previousStatus == "spaceBlocked")
        {
            Assert.AreEqual("spaceBlocked", (await batch.GetAsync(queued.BatchId)).Status);
            await batch.CommandAsync(queued.BatchId, "resume");
        }
        await batch.TickAsync();
        var task = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task.Id);
        var entry = AcquisitionLocalRecoveryQueueEntry.Read(task)!;
        Assert.AreEqual(queued.BatchId, entry.BatchId);
        Assert.AreEqual(f.Run.Id, task.WorkflowRunId);
        Assert.AreEqual(f.Task.PlanRevision, task.PlanRevision);
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(task));
        Assert.AreEqual(1, (await _db.ArtifactOperations.AsNoTracking().SingleAsync(o => o.Id == f.OperationId)).Attempt);
        if (checkpoint != "valid")
        {
            Assert.AreEqual(0, host.Calls);
            CollectionAssert.Contains(new[] {"spaceBlocked", "attention"}, entry.Status);
            return;
        }
        Assert.AreEqual(1, host.Calls, entry.ReasonCode);
        Assert.AreEqual(f.Run.Id, host.RunId);
        var signal = JsonSerializer.Deserialize<CloudFileProcessingSignal>(host.Signal!, WorkflowJson.Options)!;
        Assert.AreEqual(new FileArtifactProcessingRecovery(f.OperationId, 1), signal.StorageRecovery);
        Assert.IsNull(signal.ExtractionPlanJson);
    }

    private async Task<(ProcessingFixture, AcquisitionLocalRecoveryBatchService, ProcessingRecoveryHost,
        SharedProcessingVolume, long, long)> SeedExtractionCapacityBatch(string? defect = null, long? expandedBytes = null)
    {
        var files = Artifacts.FileProcessingMaskedSevenZipTests.MakeVolumes(_root);
        var f = await SeedProcessingStorage(volumes: defect == "renameOnly" ? null : files);
        var key = FileProcessingPlanExecutor.StepKey("rename") + "-0";
        var state = new FileProcessingPlanExecutor.State {LineageVersion = 1, InputManifest = f.Input.Manifest};
        var members = new List<string>();
        foreach (var path in f.Input.Files)
        {
            var relative = Path.GetRelativePath(f.Input.RootDirectory, path).Replace('\\', '/');
            var content = f.Input.Manifest.Entries.Single(e => e.RelativePath == relative);
            var member = "inputs/" + relative;
            members.Add(member);
            var snapshotPath = Path.Combine(f.Checkpoint, member);
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            File.Copy(path, snapshotPath);
            state.Active.Add(new(snapshotPath, relative));
            state.FileContents[member] = content;
        }
        state.Operations[key] = new()
        {
            OutputDirectory = defect == "externalOutput" ? _root : Path.Combine(f.Checkpoint, "outputs", key),
            ExpandedBytes = expandedBytes,
            ArchiveInputs = defect == "detachedGroup" ? ["inputs/foreign.zip"] : members
        };
        if (defect == "unstarted") state.Operations.Clear();
        if (defect == "completed") state.Completed = true;
        if (defect == "pendingInput") state.PendingOperation = key;
        if (defect == "foreignManifest") state.InputManifest = new([], "foreign");
        var stateFile = Path.Combine(f.Checkpoint, "state.json");
        if (defect == "noState") File.Delete(stateFile);
        else await File.WriteAllTextAsync(stateFile, JsonSerializer.Serialize(defect == "emptyState" ?
            new FileProcessingPlanExecutor.State() : state, WorkflowJson.Options));

        var dest = Path.Combine(_root, "frozen-delivery");
        f.Task.PlanJson = JsonSerializer.Serialize(new AcquisitionPlan {Title = "Task", Parts = [new() {Id = "part", Processing = f.Plan}]}, WorkflowJson.Options);
        var frozen = WorkflowRunPlans.Read(f.Run.PlanSnapshotJson)!;
        var end = new FileArtifactPublicationConfig {TargetDirectories = new Dictionary<string, string> {{"part", dest}}};
        f.Run.PlanSnapshotJson = WorkflowRunPlans.Serialize(frozen with {Nodes = [frozen.Nodes[0],
            frozen.Nodes[1] with {ConfigJson = JsonSerializer.Serialize(end, WorkflowJson.Options)}]});
        var wait = JsonNode.Parse(f.Run.WaitPromptJson!)!;
        wait["message"] = StorageWriteGuard.ReserveLowCode;
        f.Run.WaitPromptJson = wait.ToJsonString();
        await _db.SaveChangesAsync();

        var policy = new UserStoragePolicy(false);
        var plans = new AcquisitionPlanService(_db, [], null!, null!, null!, policy);
        var options = _services.GetRequiredService<IBOptions<AcquisitionOptions>>();
        var bytes = f.Input.Manifest.Entries.Sum(e => e.Length);
        var reserve = AcquisitionSpaceReserve.Minimum(options.Value);
        var capacity = new SharedProcessingVolume(reserve + bytes + 1);
        var host = new ProcessingRecoveryHost();
        return (f, new(_db, new(_db, _diagnostics, new(), host), _diagnostics, null!, plans, options, policy, capacity, new()),
            host, capacity, bytes, reserve);
    }
}
