using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.CloudDownloads;

public sealed partial class ArchiveQuarantineServiceTests
{
    [TestMethod]
    public async Task Explicit_skip_interrupted_before_purge_resumes_from_receipt_without_rehashing_or_overwriting_unknown_files()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        source = _store.Records[source.Id] = source with {Kind = "download"};
        output = _store.Records[output.Id] = output with {Kind = "publishedFiles"};
        _store.PublicationOperations = true;
        var authorization = new ArtifactDeliveryCleanupAuthorization(new("consent", DateTimeOffset.UtcNow, 42, 1, 1, true, [1],
            [new(output.Id, output.OperationId, output.Fingerprint, output.RootDirectory)], true), _ => Task.FromResult(true), _store);
        var unknown = Path.Combine(source.RootDirectory, "user-key.txt"); await File.WriteAllTextAsync(unknown, "retain");
        _store.OnMaintenance = () => throw new OperationCanceledException();
        var interrupted = await Service.DeleteManagedAncestorAfterDeliveryAsync(source.Id, [output.Id],
            (_, _) => Task.FromResult(true), default, authorization);
        Assert.IsFalse(interrupted.Completed);
        var receipt = (await Service.GetReceiptsAsync()).Single();
        Assert.AreEqual("Quarantined", receipt.Status);
        Assert.AreEqual(source.RootDirectory, _store.Records[source.Id].RootDirectory);
        Assert.AreEqual(ArtifactLocationState.Quarantined, _store.Records[source.Id].State);
        Assert.IsFalse(File.Exists(source.Files.Single()));
        Assert.AreEqual("original", await File.ReadAllTextAsync(Path.Combine(receipt.QuarantineRoot, "archive.zip")));
        Assert.AreEqual((1, 8L), await Service.PendingDeletionSizeAsync(receipt, source.Manifest, default));
        _store.OnMaintenance = null;
        var completed = await Service.DeleteManagedAncestorAfterDeliveryAsync(source.Id, [output.Id],
            (_, _) => Task.FromResult(true), default, authorization);
        Assert.IsTrue(completed.Completed, completed.Message); Assert.AreEqual(1, completed.RemovedFiles);
        Assert.AreEqual(ArtifactLocationState.Retired, _store.Records[source.Id].State);
        Assert.AreEqual(receipt.QuarantineRoot, _store.Records[source.Id].RootDirectory);
        Assert.AreEqual("retain", await File.ReadAllTextAsync(unknown)); Assert.IsTrue(File.Exists(output.Files.Single()));
        Assert.AreEqual(0, _store.ResolveCount);
    }

    [TestMethod]
    public async Task Explicit_skip_still_rejects_a_source_changed_after_snapshot_before_quarantine_move()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        source = _store.Records[source.Id] = source with {Kind = "download"};
        output = _store.Records[output.Id] = output with {Kind = "publishedFiles"};
        _store.PublicationOperations = true;
        var authorization = new ArtifactDeliveryCleanupAuthorization(new("consent", DateTimeOffset.UtcNow, 42, 1, 1, true, [1],
            [new(output.Id, output.OperationId, output.Fingerprint, output.RootDirectory)], true), _ => Task.FromResult(true), _store);
        _store.OnQuarantine = () => File.SetLastWriteTimeUtc(source.Files.Single(), DateTime.UtcNow.AddMinutes(1));
        var outcome = await Service.DeleteManagedAncestorAfterDeliveryAsync(source.Id, [output.Id],
            (_, _) => Task.FromResult(true), default, authorization);
        Assert.IsFalse(outcome.Completed);
        Assert.AreEqual("original", await File.ReadAllTextAsync(source.Files.Single()));
        Assert.IsTrue(File.Exists(output.Files.Single()));
        Assert.AreEqual(0, _store.ResolveCount, "The accepted metadata mode must not resolve/hash payloads.");
    }

    private async Task<ArtifactRecord> TwoFiles(string id, params string[] parents)
    {
        var first = await Artifact(id, "a.zip", "original", parents);
        var second = Path.Combine(first.RootDirectory, "b.zip"); await File.WriteAllTextAsync(second, "second");
        var manifest = await ArtifactManifestFiles.CaptureAsync(first.RootDirectory, [first.Files[0], second]);
        return _store.Records[id] = first with {Files = [first.Files[0], second], Manifest = manifest, Fingerprint = manifest.Fingerprint};
    }

    private async Task<(string Id, string Root, string ReceiptPath)> MoveFirstAfterDurableIntent(ArtifactRecord source, ArtifactRecord output)
    {
        var id = Guid.NewGuid().ToString("N");
        var target = Path.Combine(source.RootDirectory, ".bakabase-quarantine", id);
        var receipt = new ArchiveQuarantineService.Receipt {Id = id, ArtifactId = source.Id, Managed = true,
            OriginalRoot = "appdata:" + Path.GetRelativePath(_root, source.RootDirectory).Replace('\\', '/'),
            QuarantineRoot = "appdata:" + Path.GetRelativePath(_root, target).Replace('\\', '/'),
            Manifest = source.Manifest, OutputIds = [output.Id], Status = "Moving"};
        var receipts = Path.Combine(_root, "file-processing", "quarantine-receipts"); Directory.CreateDirectory(receipts);
        var path = Path.Combine(receipts, id + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var saved = await File.ReadAllBytesAsync(path);
        _store.Records[source.Id] = source with {State = ArtifactLocationState.Quarantined};
        Directory.CreateDirectory(target);
        File.Move(source.Files[0], Path.Combine(target, "a.zip"), overwrite: false);
        // Model a real interruption after this move, with no per-file receipt rewrite.
        CollectionAssert.AreEqual(saved, await File.ReadAllBytesAsync(path));
        return (id, target, path);
    }

    [TestMethod]
    public async Task Partial_move_recovers_from_original_intent_without_per_file_progress_receipts()
    {
        var source = await TwoFiles("source"); var output = await TwoFiles("output", source.Id);
        var paused = await MoveFirstAfterDurableIntent(source, output);
        var unknown = Path.Combine(source.RootDirectory, "key.txt"); await File.WriteAllTextAsync(unknown, "retain");
        var done = await Service.QuarantineAsync(source.Id, [output.Id]);
        Assert.IsTrue(done.Completed, done.Message); Assert.AreEqual(paused.Id, done.ReceiptId);
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(paused.Root, source.Manifest)).Available);
        Assert.AreEqual("retain", await File.ReadAllTextAsync(unknown));
        Assert.IsTrue((await Service.RestoreAsync(paused.Id)).Completed);
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(source.RootDirectory, source.Manifest)).Available);
    }

    [TestMethod]
    public async Task Partial_move_recovery_rejects_changed_committed_destination_without_overwrite()
    {
        var source = await TwoFiles("source"); var output = await TwoFiles("output", source.Id);
        var paused = await MoveFirstAfterDurableIntent(source, output);
        var destination = Path.Combine(paused.Root, "a.zip");
        var timestamp = File.GetLastWriteTimeUtc(destination);
        await File.WriteAllTextAsync(destination, "tampered"); File.SetLastWriteTimeUtc(destination, timestamp);
        var done = await Service.QuarantineAsync(source.Id, [output.Id]);
        Assert.IsFalse(done.Completed); Assert.AreEqual(paused.Id, done.ReceiptId);
        Assert.AreEqual("tampered", await File.ReadAllTextAsync(destination));
        Assert.AreEqual("second", await File.ReadAllTextAsync(source.Files[1]));
    }

    [TestMethod]
    public async Task Partial_move_recovery_rejects_symlink_at_committed_destination()
    {
        if (OperatingSystem.IsWindows()) return;
        var source = await TwoFiles("source"); var output = await TwoFiles("output", source.Id);
        var paused = await MoveFirstAfterDurableIntent(source, output);
        var destination = Path.Combine(paused.Root, "a.zip");
        var retained = Path.Combine(_root, "retained.zip"); File.Move(destination, retained);
        File.CreateSymbolicLink(destination, retained);
        Assert.IsFalse((await Service.QuarantineAsync(source.Id, [output.Id])).Completed);
        Assert.AreEqual("original", await File.ReadAllTextAsync(retained));
        Assert.IsTrue(File.Exists(source.Files[1]));
    }

    [TestMethod]
    public async Task Source_changed_after_initial_manifest_with_same_length_and_mtime_is_not_moved()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var timestamp = File.GetLastWriteTimeUtc(source.Files[0]);
        _store.OnQuarantine = () => { File.WriteAllText(source.Files[0], "tampered"); File.SetLastWriteTimeUtc(source.Files[0], timestamp); };
        Assert.IsFalse((await Service.QuarantineAsync(source.Id, [output.Id])).Completed);
        Assert.AreEqual("tampered", await File.ReadAllTextAsync(source.Files[0]));
    }

    [TestMethod]
    public async Task Full_output_validation_still_runs_after_metadata_checks()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        _store.OnGetOperation = () => File.WriteAllText(output.Files[0], "tampered");
        Assert.IsFalse((await Service.QuarantineAsync(source.Id, [output.Id])).Completed);
        Assert.AreEqual("original", await File.ReadAllTextAsync(source.Files[0]));
        Assert.AreEqual(1, _store.ResolveCount);
    }

    [TestMethod]
    public async Task Final_output_is_freshly_verified_after_relocation_and_again_inside_delete_fence()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        _store.Records[output.Id] = output with {Kind = "publishedFiles"};
        Assert.IsTrue((await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id])).Completed);
        Assert.AreEqual(5, _store.ResolveCount, "All original safety phases retain their fresh full validation.");
        Assert.IsTrue(File.Exists(output.Files[0]));
    }

    [TestMethod]
    public async Task Output_change_at_the_delete_fence_preserves_quarantined_source()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        _store.Records[output.Id] = output with {Kind = "publishedFiles"};
        Assert.IsTrue((await Service.QuarantineAsync(source.Id, [output.Id])).Completed);
        var quarantined = _store.Records[source.Id].Files.Single();
        _store.OnMaintenance = () => File.WriteAllText(output.Files[0], "tampered");
        Assert.IsFalse((await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id])).Completed);
        Assert.AreEqual("original", await File.ReadAllTextAsync(quarantined));
        Assert.AreEqual("Quarantined", (await Service.GetReceiptsAsync()).Single().Status);
    }

    [TestMethod]
    [DataRow("operation")]
    [DataRow("kind")]
    [DataRow("managed")]
    [DataRow("parents")]
    [DataRow("metadata")]
    [DataRow("root")]
    [DataRow("length")]
    [DataRow("state")]
    public async Task Full_validation_cannot_substitute_different_provenance_or_manifest(string change)
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        _store.ResolvedIdentity = record => change switch
        {
            "operation" => record with {OperationId = "different"},
            "kind" => record with {Kind = "different"},
            "managed" => record with {Managed = false},
            "parents" => record with {ParentArtifactIds = ["different"]},
            "metadata" => record with {MetadataJson = "{}"},
            "root" => record with {RootDirectory = Path.Combine(_root, "different")},
            "length" => record with {Manifest = record.Manifest with {Entries = [record.Manifest.Entries.Single() with {Length = 1}]}},
            _ => record with {State = ArtifactLocationState.Quarantined}
        };
        Assert.IsFalse((await Service.QuarantineAsync(source.Id, [output.Id])).Completed);
        Assert.AreEqual("original", await File.ReadAllTextAsync(source.Files[0]));
    }
}
