using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionProcessingWorkspaceRemovalTests
{
    private readonly DirectoryInfo fixture = Directory.CreateTempSubdirectory("processing-reclaim-");
    private readonly string binding = Guid.NewGuid().ToString("N");
    private string Root => Path.Combine(fixture.FullName, "stage");
    private string Receipts => Path.Combine(fixture.FullName, "metadata");
    private string FileAt(string relative) => Path.Combine(Root, relative);
    private IReadOnlyList<ArtifactManifestEntry> evidence = [];

    [TestInitialize]
    public async Task Prepare()
    {
        Directory.CreateDirectory(Root);
        await File.WriteAllTextAsync(FileAt("a.bin"), "aaa");
        await File.WriteAllTextAsync(FileAt("b.bin"), "bb");
        evidence = (await ArtifactManifestFiles.CaptureAsync(Root, [FileAt("a.bin"), FileAt("b.bin")])).Entries;
    }

    [TestCleanup]
    public void Clean() => fixture.Delete(true);

    private Task<ProcessingWorkspaceSelection?> Inspect() =>
        AcquisitionProcessingWorkspaceRemoval.InspectAsync(Receipts, "part", binding, Root, "revision", evidence, default);

    [TestMethod]
    public async Task UnlinksOnlyPreviewedDataAndPreservesUnknownFilesAndCompletedReceipt()
    {
        var selected = (await Inspect())!;
        Assert.IsFalse(Directory.Exists(Receipts));
        await File.WriteAllTextAsync(FileAt("manual.txt"), "my file");
        Assert.IsTrue(AcquisitionProcessingWorkspaceRemoval.Same(selected, (await Inspect())!));
        var result = await AcquisitionProcessingWorkspaceRemoval.RemoveAsync(Receipts, selected, default);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(2, result.RemovedFiles);
        Assert.AreEqual(5L, result.RemovedBytes);
        Assert.AreEqual("my file", await File.ReadAllTextAsync(FileAt("manual.txt")));
        Assert.IsFalse(File.Exists(FileAt("a.bin")));
        await File.WriteAllTextAsync(FileAt("b.bin"), "bb");
        Assert.IsNull(await Inspect());
        Assert.AreEqual("bb", await File.ReadAllTextAsync(FileAt("b.bin")));
    }

    [TestMethod]
    public async Task MultipleBatchesRemoveOnlyTheirVerifiedFilesAndKeepOriginalPathReplacements()
    {
        var count = AcquisitionProcessingWorkspaceAbsence.BatchSize + 10;
        var paths = Enumerable.Range(0, count).Select(i => FileAt($"member-{i:D4}")).ToArray();
        foreach (var path in paths) await File.WriteAllTextAsync(path, "x");
        evidence = (await ArtifactManifestFiles.CaptureAsync(Root, paths)).Entries;
        var selected = (await Inspect())!;
        var retired = false;
        var result = await AcquisitionProcessingWorkspaceRemoval.RemoveAsync(Receipts, selected, default,
            async token =>
            {
                Assert.IsTrue(paths.All(path => !File.Exists(path)));
                await File.WriteAllTextAsync(paths[0], "new original-location file", token);
                retired = true;
            });
        Assert.IsTrue(retired && result.Completed);
        Assert.AreEqual(count, result.RemovedFiles);
        Assert.AreEqual((long) count, result.RemovedBytes);
        Assert.AreEqual("new original-location file", await File.ReadAllTextAsync(paths[0]));
        Assert.AreEqual("aaa", await File.ReadAllTextAsync(FileAt("a.bin")));
        Assert.AreEqual("bb", await File.ReadAllTextAsync(FileAt("b.bin")));
        Assert.IsNull(await Inspect());
    }

    [TestMethod]
    public async Task ChangedBytesFailBeforeAnyDeletionEvenWhenSizeAndTimestampMatch()
    {
        var selected = (await Inspect())!;
        File.WriteAllText(FileAt("b.bin"), "BB");
        File.SetLastWriteTimeUtc(FileAt("b.bin"), new DateTime(selected.Files[1].LastWriteUtcTicks, DateTimeKind.Utc));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Inspect());
        var attempted = await AcquisitionProcessingWorkspaceRemoval.RemoveAsync(Receipts, selected, default);
        Assert.IsFalse(attempted.Completed);
        Assert.AreEqual(0, attempted.RemovedFiles);
        Assert.AreEqual("BB", File.ReadAllText(FileAt("b.bin")));
        Assert.AreEqual("aaa", File.ReadAllText(FileAt(".bakabase-reclaim-" + selected.Token + "/00000000")));
        await File.WriteAllTextAsync(FileAt("a.bin"), "user replacement");
        await File.WriteAllTextAsync(FileAt("b.bin"), "bb");
        File.SetLastWriteTimeUtc(FileAt("b.bin"), new DateTime(selected.Files[1].LastWriteUtcTicks, DateTimeKind.Utc));
        var resumed = (await Inspect())!;
        Assert.AreEqual("moving", resumed.Phase);
        var finished = await AcquisitionProcessingWorkspaceRemoval.RemoveAsync(Receipts, resumed, default);
        Assert.IsTrue(finished.Completed);
        Assert.AreEqual(5L, finished.RemovedBytes);
        Assert.AreEqual("user replacement", File.ReadAllText(FileAt("a.bin")));
    }

    [TestMethod]
    public async Task InterruptedPurgeCountsRemainingHeldFileAndNeverReopensCanonicalNames()
    {
        var selected = (await Inspect())!;
        // A failed second move gives us the real durable receipt. Simulate power loss
        // after both renames succeeded and the first confirmed file was unlinked.
        var saved = Path.Combine(fixture.FullName, "b.saved");
        File.Move(FileAt("b.bin"), saved);
        Assert.IsFalse((await AcquisitionProcessingWorkspaceRemoval.RemoveAsync(Receipts, selected, default)).Completed);
        var hidden = FileAt(".bakabase-reclaim-" + selected.Token);
        var second = Path.Combine(hidden, "00000001");
        File.Move(saved, second);
        File.Delete(Path.Combine(hidden, "00000000"));
        var path = Path.Combine(Receipts, binding + ".json");
        var receipt = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        receipt["phase"] = "purging";
        await File.WriteAllTextAsync(path, receipt.ToJsonString());
        await File.WriteAllTextAsync(FileAt("a.bin"), "new user file");
        var reopened = (await Inspect())!;
        Assert.AreEqual(1, reopened.Remaining.Count);
        var finished = await AcquisitionProcessingWorkspaceRemoval.RemoveAsync(Receipts, reopened, default);
        Assert.IsTrue(finished.Completed);
        Assert.AreEqual(1, finished.RemovedFiles);
        Assert.AreEqual(2L, finished.RemovedBytes);
        Assert.AreEqual("new user file", await File.ReadAllTextAsync(FileAt("a.bin")));
    }

    [TestMethod]
    public async Task File_created_in_a_receipt_slot_during_the_final_handoff_survives_and_blocks_retirement()
    {
        var selection = (await Inspect())!;
        var slot = Path.Combine(selection.QuarantineDirectory, "00000000");
        var batches = 0;
        var retired = false;
        var outcome = await AcquisitionProcessingWorkspaceRemoval.RemoveAsync(Receipts, selection, default,
            _ => { retired = true; return Task.CompletedTask; }, async ct =>
            {
                if (++batches == 2) await File.WriteAllTextAsync(slot, "independent file", ct);
            });
        Assert.IsFalse(outcome.Completed);
        Assert.IsFalse(retired);
        Assert.AreEqual("independent file", await File.ReadAllTextAsync(slot));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Inspect());
    }

    [TestMethod]
    public async Task InterruptedRetirementRechecksPublicEvidenceAndLeavesRecreatedFilesAlone()
    {
        var selected = (await Inspect())!;
        var failed = await AcquisitionProcessingWorkspaceRemoval.RemoveAsync(Receipts, selected, default,
            _ => throw new IOException("temporarily offline"));
        Assert.IsFalse(failed.Completed);
        Assert.AreEqual(5L, failed.RemovedBytes);
        await File.WriteAllTextAsync(FileAt("a.bin"), "user restored");
        var retry = (await Inspect())!;
        Assert.AreEqual(0, retry.Remaining.Count);
        Assert.IsFalse(AcquisitionProcessingWorkspaceRemoval.Same(selected, retry));
        var finalized = await AcquisitionProcessingWorkspaceRemoval.RemoveAsync(Receipts, retry, default);
        Assert.IsTrue(finalized.Completed);
        Assert.AreEqual(0L, finalized.RemovedBytes);
        Assert.AreEqual("user restored", await File.ReadAllTextAsync(FileAt("a.bin")));
    }

    [TestMethod]
    public async Task ReplacingASelectedFileWithIdenticalBytesAndTimestampRequiresNewConfirmation()
    {
        var old = (await Inspect())!;
        File.Move(FileAt("a.bin"), Path.Combine(fixture.FullName, "old"));
        File.WriteAllText(FileAt("a.bin"), "aaa");
        File.SetLastWriteTimeUtc(FileAt("a.bin"), new DateTime(old.Files[0].LastWriteUtcTicks, DateTimeKind.Utc));
        var refreshed = (await Inspect())!;
        Assert.IsFalse(AcquisitionProcessingWorkspaceRemoval.Same(old, refreshed));
        Assert.IsFalse((await AcquisitionProcessingWorkspaceRemoval.RemoveAsync(Receipts, old, default)).Completed);
        Assert.AreEqual("aaa", File.ReadAllText(FileAt("a.bin")));
    }

    [TestMethod]
    public async Task NewSymlinkAndUnrelatedEvidenceCannotBeReclaimed()
    {
        var selected = (await Inspect())!;
        var external = Path.Combine(fixture.FullName, "elsewhere");
        File.WriteAllText(external, "aaa");
        File.Delete(FileAt("a.bin"));
        File.CreateSymbolicLink(FileAt("a.bin"), external);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Inspect());
        var failed = await AcquisitionProcessingWorkspaceRemoval.RemoveAsync(Receipts, selected, default);
        Assert.IsFalse(failed.Completed);
        Assert.AreEqual(0L, failed.RemovedBytes);
        Assert.AreEqual("aaa", File.ReadAllText(external));
        evidence = [evidence[1]];
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Inspect());
    }
}
