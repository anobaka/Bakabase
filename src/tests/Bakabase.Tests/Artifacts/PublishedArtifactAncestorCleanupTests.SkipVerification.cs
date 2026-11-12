using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Accepted_permanent_cleanup_interruption_keeps_quarantine_fence_and_receipt_for_resume_or_restore(bool restore)
    {
        var source = await Artifact("download"); var final = await Artifact("publishedFiles", source);
        var quarantine = new ArchiveQuarantineService(store, root, new Storage());
        var authorization = new ArtifactDeliveryCleanupAuthorization(new("consent", DateTimeOffset.UtcNow, 42, 1, 1, true, [1],
            [new(final.Id, final.OperationId, final.Fingerprint, final.RootDirectory)], true), _ => Task.FromResult(true), store);
        var checks = 0;
        var interrupted = await quarantine.DeleteManagedAncestorAfterDeliveryAsync(source.Id, [final.Id],
            (_, _) => Task.FromResult(++checks == 1), default, authorization);
        Assert.IsFalse(interrupted.Completed);
        var receipt = (await quarantine.GetReceiptsAsync()).Single();
        Assert.AreEqual("Quarantined", receipt.Status);
        var fenced = (await store.GetArtifactAsync(source.Id))!;
        Assert.AreEqual(source.RootDirectory, fenced.RootDirectory);
        Assert.AreEqual(ArtifactLocationState.Quarantined, fenced.State);
        Assert.IsFalse(await store.AttachConsumerAsync(source.OperationId, "must-not-reuse", TimeSpan.FromMinutes(1)));
        Assert.IsFalse(File.Exists(source.Files.Single()));
        Assert.IsTrue(File.Exists(Path.Combine(receipt.QuarantineRoot, source.Manifest.Entries.Single().RelativePath)));
        var outcome = restore ? await quarantine.RestoreAsync(receipt.ReceiptId)
            : await quarantine.DeleteManagedAncestorAfterDeliveryAsync(source.Id, [final.Id], (_, _) => Task.FromResult(true), default, authorization);
        Assert.IsTrue(outcome.Completed, outcome.Message);
        var terminal = (await store.GetArtifactAsync(source.Id))!;
        Assert.AreEqual(restore ? ArtifactLocationState.Available : ArtifactLocationState.Retired, terminal.State);
        Assert.AreEqual(restore ? source.RootDirectory : receipt.QuarantineRoot, terminal.RootDirectory);
        Assert.AreEqual(restore, File.Exists(source.Files.Single()));
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(final.RootDirectory, final.Manifest)).Available);
        if (restore) Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(source.RootDirectory, source.Manifest)).Available);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Explicit_skip_avoids_source_content_reads_but_preserves_final_unknown_files_and_history(bool skip)
    {
        var f = await DeletionFixture();
        var unknown = Path.Combine(f.Source.RootDirectory, "user-key.txt");
        await File.WriteAllTextAsync(unknown, "keep this unknown attachment");
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, skip));
        Assert.IsTrue(preview.Rows.Single().CanDelete);
        // Equal-size bytes deliberately differ from the recorded SHA. This proves the
        // explicitly selected mode does not read/hash contents, rather than relying on timing.
        var source = f.Source.Files.Single();
        var stamp = File.GetLastWriteTimeUtc(source);
        await File.WriteAllTextAsync(source, new string('x', (int)new FileInfo(source).Length));
        File.SetLastWriteTimeUtc(source, stamp);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(skip ? 1 : 0, result.RemovedFiles, System.Text.Json.JsonSerializer.Serialize(result));
        Assert.AreEqual(!skip, File.Exists(source));
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(f.Final.RootDirectory, f.Final.Manifest)).Available);
        Assert.AreEqual("keep this unknown attachment", await File.ReadAllTextAsync(unknown));
        Assert.IsNotNull(await store.GetArtifactAsync(f.Source.Id));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Explicit_skip_reaches_private_plan_and_recursive_cleanup_without_reading_payloads(bool skip, bool retired)
    {
        var f = await PrivateResultFixture(retired);
        var authorization = new ArtifactDeliveryCleanupAuthorization(new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            42, 1, 1, true, [1], [new(f.Final.Id, f.Final.OperationId, f.Final.Fingerprint, f.Final.RootDirectory)], skip),
            _ => Task.FromResult(true), store);
        var preview = retired
            ? (await cleanup.PreviewForDeletionAsync([f.Final.Id], [1], authorization, default)).Single(p => p.ArtifactId == f.Source.Id)
            : (await f.Maintenance.PreviewForDeletionAsync([f.Source.Id], authorization, default)).Single();
        Assert.IsTrue(preview.Eligible, preview.Reason);
        var stamp = File.GetLastWriteTimeUtc(f.Private);
        await File.WriteAllTextAsync(f.Private, new string('x', (int)new FileInfo(f.Private).Length));
        File.SetLastWriteTimeUtc(f.Private, stamp);
        var result = retired
            ? await cleanup.ReclaimForDeletionAsync(preview, [1], authorization, default)
            : (await f.Maintenance.ReclaimForDeletionAsync([f.Source.Id], authorization,
                new Dictionary<string, string> {{f.Source.Id, preview.SelectionFingerprint!}}, default)).Single();
        Assert.AreEqual(skip, result.Completed, result.Reason);
        Assert.AreEqual(!skip, File.Exists(f.Private));
        Assert.IsTrue(f.Raw.Files.All(File.Exists)); Assert.IsTrue(f.Final.Files.All(File.Exists));
        Assert.IsTrue(File.Exists(f.PlanState)); Assert.IsTrue(File.Exists(f.RecursiveState));
    }

    [TestMethod]
    [DataRow("changed-size")]
    [DataRow("changed-time")]
    [DataRow("directory")]
    [DataRow("link")]
    [DataRow("missing-storage")]
    public async Task Metadata_skip_snapshot_rejects_changes_and_unavailable_storage(string change)
    {
        if (OperatingSystem.IsWindows() && change == "link") return;
        var f = await Artifact("download");
        var authorization = new ArtifactDeliveryCleanupAuthorization(new("consent", DateTimeOffset.UtcNow, 42, 1, 1, true, [], [], true),
            _ => Task.FromResult(true), store);
        var snapshot = ArtifactCleanupFileSnapshot.For(authorization)!;
        var path = f.Files.Single(); var length = new FileInfo(path).Length;
        snapshot.Observe(path, length, default);
        switch (change)
        {
            case "changed-size": await File.AppendAllTextAsync(path, "new bytes"); break;
            case "changed-time": File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1)); break;
            case "directory": File.Delete(path); Directory.CreateDirectory(path); break;
            case "link": File.Move(path, path + ".retained"); File.CreateSymbolicLink(path, path + ".retained"); break;
            default: Directory.Move(f.RootDirectory, f.RootDirectory + ".disconnected"); break;
        }
        try { snapshot.Matches(path, length, default); Assert.Fail("Changed or unavailable files must not be accepted."); }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException) { }
    }
}
