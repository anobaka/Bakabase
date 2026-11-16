using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private async Task<(ArtifactRecord Source, ArtifactRecord Final, ArtifactDeliveryCleanupAuthorization Authorization,
        ArchiveQuarantineService Quarantine)> PartiallyRemovedInput(bool skip = true)
    {
        var directory = Path.Combine(root, "partial-input"); Directory.CreateDirectory(directory);
        var gone = Path.Combine(directory, "already-removed.zip");
        var present = Path.Combine(directory, "retained.bin");
        await File.WriteAllTextAsync(gone, "old archive"); await File.WriteAllTextAsync(present, "retained input");
        var source = await store.AdoptAsync(new("partial-source", "partial-source", "files.process", "input"),
            new(directory, [gone, present], "processedFiles", true), "fixture");
        var output = Path.Combine(root, "partial-final"); Directory.CreateDirectory(output);
        foreach (var file in source.Files) File.Copy(file, Path.Combine(output, Path.GetFileName(file)));
        var final = await store.AdoptAsync(new("partial-publication", "partial-publication", "files.publish", source.Fingerprint),
            new(output, Directory.GetFiles(output), "publishedFiles", false, [source.Id]), "fixture");
        var consent = new ArtifactDeliveryCleanupConsent("partial-consent", DateTimeOffset.UtcNow, 1, 1, 1,
            true, [1], [new(final.Id, final.OperationId, final.Fingerprint, final.RootDirectory)], skip);
        File.Delete(gone);
        return (source, final, new(consent, _ => Task.FromResult(true), store),
            new ArchiveQuarantineService(store, root, new Storage()));
    }

    [TestMethod]
    public async Task Explicit_accepted_cleanup_retires_remaining_files_and_records_already_absent_entries()
    {
        var f = await PartiallyRemovedInput();
        var outcome = await f.Quarantine.DeleteManagedAncestorAfterDeliveryAsync(f.Source.Id, [f.Final.Id],
            (_, _) => Task.FromResult(true), default, f.Authorization);
        Assert.IsTrue(outcome.Completed, outcome.Message);
        Assert.AreEqual(1, outcome.RemovedFiles);
        Assert.IsTrue(f.Source.Files.All(p => !File.Exists(p)));
        Assert.IsTrue(f.Final.Files.All(File.Exists));
        var retired = (await store.GetArtifactAsync(f.Source.Id))!;
        Assert.AreEqual(ArtifactLocationState.Retired, retired.State);
        Assert.AreEqual(f.Source.Fingerprint, retired.Fingerprint);
        var path = Path.Combine(root, "file-processing", "quarantine-receipts", outcome.ReceiptId + ".json");
        var receipt = JsonSerializer.Deserialize<ArchiveQuarantineService.Receipt>(await File.ReadAllTextAsync(path), Json)!;
        CollectionAssert.AreEqual(new[] {"already-removed.zip"}, receipt.AbsentBeforeDeletion);
        Assert.AreEqual(2, receipt.Manifest.Entries.Count);
        Assert.IsFalse((await f.Quarantine.RestoreAsync(outcome.ReceiptId!)).Completed);
        var repeated = await f.Quarantine.DeleteManagedAncestorAfterDeliveryAsync(f.Source.Id, [f.Final.Id],
            (_, _) => Task.FromResult(true), default, f.Authorization);
        Assert.IsTrue(repeated.Completed, repeated.Message); Assert.AreEqual(0, repeated.RemovedFiles);
    }

    [DataTestMethod]
    [DataRow("content-checks")]
    [DataRow("missing-root")]
    [DataRow("symlink")]
    [DataRow("changed-length")]
    public async Task Partial_absence_never_weakens_storage_link_or_present_file_guards(string scenario)
    {
        var f = await PartiallyRemovedInput(skip: scenario != "content-checks");
        var present = f.Source.Files.Single(p => p.EndsWith("retained.bin"));
        if (scenario == "missing-root") Directory.Move(f.Source.RootDirectory, f.Source.RootDirectory + "-offline");
        if (scenario == "symlink") File.CreateSymbolicLink(f.Source.Files.Single(p => p.EndsWith(".zip")), f.Final.Files.First());
        if (scenario == "changed-length") await File.WriteAllTextAsync(present, "changed");
        var outcome = await f.Quarantine.DeleteManagedAncestorAfterDeliveryAsync(f.Source.Id, [f.Final.Id],
            (_, _) => Task.FromResult(true), default, f.Authorization);
        Assert.IsFalse(outcome.Completed, scenario);
        Assert.AreEqual(0, outcome.RemovedFiles);
        Assert.IsTrue(f.Final.Files.All(File.Exists));
        Assert.AreNotEqual(ArtifactLocationState.Retired, (await store.GetArtifactAsync(f.Source.Id))!.State);
    }

    [TestMethod]
    public async Task A_recorded_missing_entry_reappearing_is_preserved_and_prevents_retirement()
    {
        var f = await PartiallyRemovedInput(); var calls = 0;
        var missing = f.Source.Files.Single(p => p.EndsWith(".zip"));
        var outcome = await f.Quarantine.DeleteManagedAncestorAfterDeliveryAsync(f.Source.Id, [f.Final.Id],
            async (_, _) => { if (++calls == 2) await File.WriteAllTextAsync(missing, "new user file"); return true; },
            default, f.Authorization);
        Assert.IsFalse(outcome.Completed);
        Assert.AreEqual("new user file", await File.ReadAllTextAsync(missing));
        Assert.AreEqual(0, outcome.RemovedFiles);
        Assert.IsTrue(f.Final.Files.All(File.Exists));
    }

    [TestMethod]
    public async Task Accepted_private_cleanup_can_validate_a_partially_removed_result_without_reading_missing_bytes()
    {
        var f = await PartiallyRemovedInput();
        Assert.IsFalse(ArtifactCleanupPreviewFiles.MatchesManifest(f.Source, default));
        Assert.IsTrue(ArtifactCleanupPreviewFiles.MatchesManifest(f.Source, default, allowConfirmedAbsent: true));
        var present = f.Source.Files.Single(p => p.EndsWith("retained.bin"));
        await File.WriteAllTextAsync(present, "changed");
        Assert.IsFalse(ArtifactCleanupPreviewFiles.MatchesManifest(f.Source, default, allowConfirmedAbsent: true));
    }
}
