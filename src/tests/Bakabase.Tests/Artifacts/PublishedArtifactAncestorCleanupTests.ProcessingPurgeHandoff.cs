using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [TestMethod]
    [DataRow("independent")]
    [DataRow("held")]
    [DataRow("held-from-ancestor")]
    [DataRow("replacement")]
    [DataRow("consumer")]
    [DataRow("lineage")]
    public async Task Processing_purge_admits_an_independent_registration_and_preserves_new_references(string mutation)
    {
        var f = await StageDeletion();
        var stage = f.Processing;
        var count = AcquisitionProcessingWorkspaceRemoval.PurgeBatchFiles + 2;
        var files = Enumerable.Range(0, count).Select(i => Path.Combine(stage, $"file{i:D4}")).ToArray();
        foreach (var file in files) await File.WriteAllTextAsync(file, "x");
        var canonical = await store.AdoptAsync(new("canonical", "canonical", "files.process", "fixture"),
            new(stage, files, "processedFiles", true), "fixture");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "nested.part:processed", canonical.OperationId));
        var binding = Guid.NewGuid().ToString("N");
        var receipts = Path.Combine(root, "receipts");
        var selection = (await AcquisitionProcessingWorkspaceRemoval.InspectAsync(receipts, "nested.part", binding,
            stage, "generation", canonical.Manifest.Entries, default))!;
        var held = Path.Combine(stage, ".bakabase-reclaim-" + selection.Token);
        var lastSlot = Path.Combine(held, (count - 1).ToString("X8"));
        var outside = Directory.CreateDirectory(Path.Combine(root, "other-task")).FullName;
        var outsideFile = Path.Combine(outside, "own.bin");
        await File.WriteAllTextAsync(outsideFile, "independent");
        await using var externalDb = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite(db.Database.GetConnectionString()!).Options);
        var external = new ArtifactOperationStore(externalDb, () => root);
        var references = new AcquisitionStageDirectoryReferenceGuard(db, store, new Storage(), new(root, new Storage()), reuseManifestReads: true);
        Task Check(CancellationToken ct) => references.RequireAsync(f.Task, [1], "nested.part", stage, ct,
            explicitTaskCleanup: true, privateReceiptDirectory: held);
        await using var fence = await store.AcquireMaintenanceAsync();
        await Check(default);
        var handoff = new AcquisitionProcessingWorkspaceHandoff(db, fence, [canonical.Id, f.Publication.Id], Check);
        await handoff.CaptureAsync(default);
        Task? waiting = null;
        var boundaries = 0;
        async Task Batch(CancellationToken ct)
        {
            if (++boundaries == 2)
            {
                Assert.IsFalse(File.Exists(Path.Combine(held, "00000000")));
                Assert.IsTrue(File.Exists(lastSlot));
                await File.WriteAllTextAsync(files[^1], "new occupant", ct);
                if (mutation == "consumer")
                    waiting = external.AttachConsumerAsync(canonical.OperationId, "other-reader", TimeSpan.FromMinutes(5), ct);
                else
                {
                    var file = mutation switch {"held" or "held-from-ancestor" => lastSlot, "replacement" => files[^1], _ => outsideFile};
                    var dir = mutation == "held-from-ancestor" ? Path.GetDirectoryName(stage)! : Path.GetDirectoryName(file)!;
                    waiting = external.AdoptAsync(new("new-reader", "new-reader", "files.capture", "fixture"),
                        new(dir, [file], ParentArtifactIds: mutation == "lineage" ? [canonical.Id] : null), "independent", ct);
                }
                Assert.IsFalse(waiting.IsCompleted);
            }
            await handoff.YieldAsync(ct);
            if (boundaries == 2)
            {
                Assert.IsTrue(waiting!.IsCompletedSuccessfully);
                Assert.IsTrue(File.Exists(lastSlot), "Registration completed while confirmed files remained to purge.");
            }
        }
        var result = await AcquisitionProcessingWorkspaceRemoval.RemoveAsync(receipts, selection, default,
            retire: handoff.RequireBeforeRetirementAsync, beforePurgeBatch: Batch);
        Assert.IsTrue(waiting!.IsCompletedSuccessfully);
        Assert.AreEqual(mutation == "independent", result.Completed, result.Reason);
        Assert.AreEqual(mutation == "independent" ? count : AcquisitionProcessingWorkspaceRemoval.PurgeBatchFiles, result.RemovedFiles);
        Assert.AreEqual(mutation != "independent", File.Exists(lastSlot));
        Assert.AreEqual("new occupant", await File.ReadAllTextAsync(files[^1]));
        Assert.AreEqual("independent", await File.ReadAllTextAsync(outsideFile));
        if (mutation is "held" or "held-from-ancestor" or "replacement" or "consumer") Assert.AreEqual("artifact_busy", result.Reason);
        if (mutation == "lineage") Assert.AreEqual("files_changed", result.Reason);
        if (!result.Completed)
        {
            var resumed = await AcquisitionProcessingWorkspaceRemoval.InspectAsync(receipts, "nested.part", binding,
                stage, "generation", canonical.Manifest.Entries, default);
            Assert.AreEqual("purging", resumed!.Phase);
            Assert.AreEqual(2, resumed.Remaining.Count);
        }
    }

    [TestMethod]
    public async Task Resuming_purge_rejects_a_same_run_registration_of_a_held_slot()
    {
        var f = await StageDeletion();
        var privateDirectory = Directory.CreateDirectory(Path.Combine(f.Processing, ".bakabase-reclaim-" + Guid.NewGuid().ToString("N"))).FullName;
        var slot = Path.Combine(privateDirectory, "00000003");
        await File.WriteAllTextAsync(slot, "held");
        var owner = await store.AdoptAsync(new("saved-slot", "saved-slot", "capture", "fixture"),
            new(f.Processing, [slot]), "same-task");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "nested.part:saved", owner.OperationId));
        var references = new AcquisitionStageDirectoryReferenceGuard(db, store, new Storage(), new(root, new Storage()));
        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => references.RequireAsync(
            f.Task, [1], "nested.part", f.Processing, default, explicitTaskCleanup: true,
            privateReceiptDirectory: privateDirectory));
        Assert.AreEqual("STAGE_DIRECTORY_SHARED", failure.Message);
        Assert.AreEqual("held", await File.ReadAllTextAsync(slot));
    }

    [TestMethod]
    public async Task Registration_queued_after_final_purge_handoff_stays_blocked_until_receipt_completion()
    {
        var f = await StageDeletion();
        var file = Path.Combine(f.Processing, "manual.key");
        var manifest = await ArtifactManifestFiles.CaptureAsync(f.Processing, [file]);
        var receipts = Path.Combine(root, "receipts");
        var binding = Guid.NewGuid().ToString("N");
        var selection = (await AcquisitionProcessingWorkspaceRemoval.InspectAsync(receipts, "nested.part", binding,
            f.Processing, "generation", manifest.Entries, default))!;
        await using var otherDb = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite(db.Database.GetConnectionString()!).Options);
        var other = new ArtifactOperationStore(otherDb, () => root);
        var separate = Path.Combine(root, "separate.bin");
        await File.WriteAllTextAsync(separate, "separate");
        Task<ArtifactRecord>? waiting = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using (var writer = await store.AcquireMaintenanceAsync(timeout.Token))
        {
            var handoff = new AcquisitionProcessingWorkspaceHandoff(db, writer, [f.Publication.Id], _ => Task.CompletedTask);
            await handoff.CaptureAsync(timeout.Token);
            var result = await AcquisitionProcessingWorkspaceRemoval.RemoveAsync(receipts, selection, timeout.Token, async ct =>
            {
                waiting = other.AdoptAsync(new("late-adopt", "late-adopt", "capture", "file"), new(root, [separate]), "other", ct);
                await handoff.RequireBeforeRetirementAsync(ct);
                Assert.IsFalse(waiting.IsCompleted);
            }, handoff.YieldAsync);
            Assert.IsTrue(result.Completed);
            Assert.IsFalse(waiting!.IsCompleted);
            Assert.IsNull(await AcquisitionProcessingWorkspaceRemoval.InspectAsync(receipts, "nested.part", binding,
                f.Processing, "generation", manifest.Entries, timeout.Token));
        }
        Assert.IsNotNull(await waiting!.WaitAsync(timeout.Token));
    }

    [TestMethod]
    public async Task Cancelled_purge_handoff_leaves_the_durable_receipt_reusable_and_releases_the_fence()
    {
        var f = await StageDeletion();
        var file = Path.Combine(f.Processing, "manual.key");
        var evidence = (await ArtifactManifestFiles.CaptureAsync(f.Processing, [file])).Entries;
        var receipts = Path.Combine(root, "receipts");
        var binding = Guid.NewGuid().ToString("N");
        Task<ProcessingWorkspaceSelection?> Inspect() => AcquisitionProcessingWorkspaceRemoval.InspectAsync(receipts,
            "nested.part", binding, f.Processing, "generation", evidence, default);
        var selection = (await Inspect())!;
        var gate = ArtifactMaintenanceGate.For(root);
        using var cancel = new CancellationTokenSource();
        await using (var writer = await store.AcquireMaintenanceAsync())
        {
            var admission = gate.EnterMutationAsync(default);
            var queuedMaintenance = store.AcquireMaintenanceAsync();
            var handoff = new AcquisitionProcessingWorkspaceHandoff(db, writer, [f.Publication.Id], _ => Task.CompletedTask);
            await handoff.CaptureAsync(default);
            var purging = AcquisitionProcessingWorkspaceRemoval.RemoveAsync(receipts, selection, cancel.Token,
                beforePurgeBatch: handoff.YieldAsync);
            await using var ordinaryMutation = await admission.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(queuedMaintenance.IsCompleted);
            cancel.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await purging);
            await ordinaryMutation.DisposeAsync();
            await writer.DisposeAsync();
            await using var nextWriter = await queuedMaintenance.WaitAsync(TimeSpan.FromSeconds(5));
        }
        await File.WriteAllTextAsync(file, "new at original path");
        var resume = (await Inspect())!;
        Assert.AreEqual("purging", resume.Phase);
        Assert.AreEqual(1, resume.Remaining.Count);
        await using var retry = await store.AcquireMaintenanceAsync();
        var finished = await AcquisitionProcessingWorkspaceRemoval.RemoveAsync(receipts, resume, default);
        Assert.IsTrue(finished.Completed);
        Assert.AreEqual(1, finished.RemovedFiles);
        Assert.AreEqual("new at original path", await File.ReadAllTextAsync(file));
    }
}
