using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Tests.CloudDownloads;

public sealed partial class ArchiveQuarantineServiceTests
{
    private async Task<ArtifactRecord> WithEmptyDirectories(ArtifactRecord artifact)
    {
        var directories = new[] {"empty/nested", "other-empty"}.Select(path => Path.Combine(artifact.RootDirectory, path)).ToArray();
        foreach (var directory in directories) Directory.CreateDirectory(directory);
        var manifest = await ArtifactManifestFiles.CaptureAsync(artifact.RootDirectory, artifact.Files.ToArray(), directories: directories);
        return _store.Records[artifact.Id] = artifact with {Manifest = manifest, Fingerprint = manifest.Fingerprint};
    }

    [TestMethod]
    public async Task Quarantine_and_restore_preserve_manifest_directories_and_prune_only_empty_receipt_owned_paths()
    {
        var source = await WithEmptyDirectories(await Artifact("source", "archive.zip", "original"));
        var output = await WithEmptyDirectories(await Artifact("output", "archive.zip", "original", source.Id));
        var userFile = Path.Combine(source.RootDirectory, "empty", "nested", "user.key");
        await File.WriteAllTextAsync(userFile, "retain");
        var unknownDirectory = Path.Combine(source.RootDirectory, "user-empty"); Directory.CreateDirectory(unknownDirectory);
        var result = await Service.QuarantineAsync(source.Id, [output.Id]);
        Assert.IsTrue(result.Completed, result.Message);
        var quarantine = _store.Records[source.Id].RootDirectory;
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(quarantine, source.Manifest)).Available);
        Assert.IsFalse(File.Exists(Path.Combine(quarantine, "empty", "nested", "user.key")));
        Assert.AreEqual("retain", await File.ReadAllTextAsync(userFile));
        // Restore must recreate a registered empty directory, not depend on the old shell remaining.
        Directory.Delete(Path.Combine(source.RootDirectory, "other-empty"));
        var restored = await Service.RestoreAsync(result.ReceiptId!);
        Assert.IsTrue(restored.Completed, restored.Message);
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(source.RootDirectory, source.Manifest)).Available);
        Assert.AreEqual("retain", await File.ReadAllTextAsync(userFile)); Assert.IsTrue(Directory.Exists(unknownDirectory));
        Assert.IsFalse(Directory.Exists(quarantine));
        Assert.IsFalse(Directory.Exists(Path.Combine(source.RootDirectory, ".bakabase-quarantine")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Restore_preserves_history_and_creates_no_directories_when_both_locations_are_unavailable(bool temporarilyOffline)
    {
        var source = await WithEmptyDirectories(await Artifact("source", "archive.zip", "original"));
        var output = await WithEmptyDirectories(await Artifact("output", "archive.zip", "original", source.Id));
        var held = await Service.QuarantineAsync(source.Id, [output.Id]);
        Assert.IsTrue(held.Completed, held.Message);
        var id = held.ReceiptId!;
        var receipt = await File.ReadAllBytesAsync(ReceiptPath(id));
        var registered = _store.Records[source.Id];
        var offline = Path.Combine(_root, "detached-volume");
        if (temporarilyOffline) Directory.Move(source.RootDirectory, offline);
        else Directory.Delete(source.RootDirectory, recursive: true);

        var attempt = await Service.RestoreAsync(id);
        Assert.IsFalse(attempt.Completed);
        Assert.AreEqual("RecoveryLocationUnavailable", attempt.Status);
        Assert.AreEqual(id, attempt.ReceiptId);
        Assert.IsFalse(Directory.Exists(source.RootDirectory));
        Assert.IsFalse(Directory.Exists(registered.RootDirectory));
        CollectionAssert.AreEqual(receipt, await File.ReadAllBytesAsync(ReceiptPath(id)));
        Assert.AreSame(registered, _store.Records[source.Id]);

        if (temporarilyOffline)
        {
            Directory.Move(offline, source.RootDirectory);
            var restored = await Service.RestoreAsync(id);
            Assert.IsTrue(restored.Completed, restored.Message);
            Assert.AreEqual("original", await File.ReadAllTextAsync(source.Files.Single()));
            Assert.IsTrue(Directory.Exists(Path.Combine(source.RootDirectory, "other-empty")));
        }
    }

    [TestMethod]
    public async Task Restore_of_an_unavailable_unfenced_source_does_not_change_its_artifact_state()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        _store.Active = true;
        var blocked = await Service.QuarantineAsync(source.Id, [output.Id]);
        Assert.AreEqual("Blocked", blocked.Status);
        _store.Active = false;
        Directory.Delete(source.RootDirectory, recursive: true);

        var attempt = await Service.RestoreAsync(blocked.ReceiptId!);
        Assert.AreEqual("RecoveryLocationUnavailable", attempt.Status);
        Assert.AreSame(source, _store.Records[source.Id]);
        Assert.AreEqual("Blocked", (await Service.GetReceiptsAsync()).Single().Status);
    }

    [TestMethod]
    public async Task Restore_accepts_verified_files_already_at_the_original_location_when_quarantine_is_gone()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var held = await Service.QuarantineAsync(source.Id, [output.Id]);
        Assert.IsTrue(held.Completed, held.Message);
        var registered = _store.Records[source.Id];
        File.Move(registered.Files.Single(), source.Files.Single());
        Directory.Delete(registered.RootDirectory);

        var restored = await Service.RestoreAsync(held.ReceiptId!);
        Assert.IsTrue(restored.Completed, restored.Message);
        Assert.AreEqual(ArtifactLocationState.Available, _store.Records[source.Id].State);
        Assert.AreEqual("original", await File.ReadAllTextAsync(source.Files.Single()));
        Assert.IsFalse(Directory.Exists(registered.RootDirectory));
    }

    [TestMethod]
    public async Task Legacy_moving_receipt_with_missing_empty_directories_resumes_and_purges_exact_files()
    {
        var source = await WithEmptyDirectories(await TwoFiles("source"));
        var output = await WithEmptyDirectories(await TwoFiles("output", source.Id));
        _store.Records[output.Id] = output with {Kind = "publishedFiles"};
        var interrupted = await MoveFirstAfterDurableIntent(source, output);
        Assert.IsFalse(Directory.Exists(Path.Combine(interrupted.Root, "empty", "nested")));
        var result = await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id]);
        Assert.IsTrue(result.Completed, result.Message); Assert.AreEqual(interrupted.Id, result.ReceiptId);
        Assert.AreEqual(2, result.RemovedFiles);
        Assert.AreEqual("Purged", (await Service.GetReceiptsAsync()).Single().Status);
        Assert.IsFalse(Directory.Exists(interrupted.Root));
        Assert.IsFalse(Directory.Exists(Path.Combine(source.RootDirectory, ".bakabase-quarantine")));
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(output.RootDirectory, output.Manifest)).Available);
        Assert.IsTrue(File.Exists(interrupted.ReceiptPath), "The cleanup history remains durable.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_file_or_link_at_a_recorded_directory_blocks_resume_before_remaining_payload_moves(bool link)
    {
        if (link && OperatingSystem.IsWindows()) return;
        var source = await WithEmptyDirectories(await TwoFiles("source"));
        var output = await WithEmptyDirectories(await TwoFiles("output", source.Id));
        var interrupted = await MoveFirstAfterDurableIntent(source, output);
        var conflict = Path.Combine(interrupted.Root, "other-empty");
        var outside = Path.Combine(_root, "unrelated"); Directory.CreateDirectory(outside);
        if (link) Directory.CreateSymbolicLink(conflict, outside);
        else await File.WriteAllTextAsync(conflict, "retain");
        var result = await Service.QuarantineAsync(source.Id, [output.Id]);
        Assert.IsFalse(result.Completed);
        Assert.IsTrue(File.Exists(source.Files[1]), "A directory conflict must be rejected before moving the next payload.");
        Assert.IsTrue(File.Exists(Path.Combine(interrupted.Root, "a.zip")));
        if (!link) Assert.AreEqual("retain", await File.ReadAllTextAsync(conflict));
        Assert.IsTrue(Directory.Exists(outside));
    }

    [TestMethod]
    public async Task Purge_retains_unknown_files_inside_a_receipt_manifest_directory()
    {
        var source = await WithEmptyDirectories(await Artifact("source", "archive.zip", "original"));
        var output = await WithEmptyDirectories(await Artifact("output", "archive.zip", "original", source.Id));
        _store.Records[output.Id] = output with {Kind = "publishedFiles"};
        var quarantine = await Service.QuarantineAsync(source.Id, [output.Id]); Assert.IsTrue(quarantine.Completed, quarantine.Message);
        var quarantineRoot = _store.Records[source.Id].RootDirectory;
        var unknown = Path.Combine(quarantineRoot, "empty", "nested", "user.key"); await File.WriteAllTextAsync(unknown, "retain");
        var result = await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id]);
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual("retain", await File.ReadAllTextAsync(unknown));
        Assert.IsFalse(Directory.Exists(Path.Combine(quarantineRoot, "other-empty")));
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(output.RootDirectory, output.Manifest)).Available);
    }
}
