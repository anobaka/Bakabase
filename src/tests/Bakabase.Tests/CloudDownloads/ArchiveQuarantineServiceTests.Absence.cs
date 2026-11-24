using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Tests.CloudDownloads;

public sealed partial class ArchiveQuarantineServiceTests
{
    [TestMethod]
    public async Task Retrying_a_completed_unlink_with_an_SMB_placeholder_does_not_reclaim_the_same_bytes_twice()
    {
        var source = await Artifact("source", "member.bin", "verified contents");
        var published = await Artifact("published", "member.bin", "verified contents", source.Id);
        _store.Records[published.Id] = published with { Kind = "publishedFiles" };
        Assert.IsTrue((await Service.QuarantineAsync(source.Id, [published.Id])).Completed);
        _store.FailNextLocation = true;
        var interrupted = await Service.DeleteAfterDeliveryAsync(source.Id, [published.Id]);
        Assert.IsFalse(interrupted.Completed);
        Assert.AreEqual(1, interrupted.RemovedFiles);

        var receipt = (await Service.GetReceiptsAsync()).Single();
        var held = Path.Combine(receipt.QuarantineRoot, ".smbdeleteAAA59fc4.4");
        await File.WriteAllTextAsync(held, "still held");
        var forecast = await Service.PendingDeletionSizeAsync(receipt, source.Manifest, default);
        Assert.AreEqual((0, 0L), forecast);
        var retry = await Service.DeleteAfterDeliveryAsync(source.Id, [published.Id]);
        Assert.IsTrue(retry.Completed, retry.Message);
        Assert.AreEqual(0, retry.RemovedFiles);
        Assert.AreEqual(0L, retry.RemovedBytes);
        Assert.AreEqual("still held", await File.ReadAllTextAsync(held));
        Assert.AreEqual("verified contents", await File.ReadAllTextAsync(published.Files.Single()));
    }

    [TestMethod]
    public async Task Interrupted_mixed_quarantine_previews_and_removes_only_the_remaining_receipt_member()
    {
        var source = await Artifact("source", "first.bin", "one");
        var second = Path.Combine(source.RootDirectory, "second.bin");
        await File.WriteAllTextAsync(second, "two");
        var paths = source.Files.Append(second).ToArray();
        var manifest = await ArtifactManifestFiles.CaptureAsync(source.RootDirectory, paths);
        _store.Records[source.Id] = source = source with {Files = paths, Manifest = manifest, Fingerprint = manifest.Fingerprint};
        var published = await Artifact("published", "first.bin", "one", source.Id);
        var publishedSecond = Path.Combine(published.RootDirectory, "second.bin");
        File.Copy(second, publishedSecond);
        var outputFiles = published.Files.Append(publishedSecond).ToArray();
        var outputManifest = await ArtifactManifestFiles.CaptureAsync(published.RootDirectory, outputFiles);
        _store.Records[published.Id] = published with
            {Kind = "publishedFiles", Files = outputFiles, Manifest = outputManifest, Fingerprint = outputManifest.Fingerprint};
        var quarantine = await Service.QuarantineAsync(source.Id, [published.Id]);
        Assert.IsTrue(quarantine.Completed, quarantine.Message);
        var receipt = (await Service.GetReceiptsAsync()).Single();

        // Model the committed deletion intent and the first completed unlink before a restart.
        var record = ReceiptPath(receipt.ReceiptId);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(record))!;
        json["status"] = "Deleting";
        await File.WriteAllTextAsync(record, json.ToJsonString());
        File.Delete(Path.Combine(receipt.QuarantineRoot, "first.bin"));
        var marker = Path.Combine(receipt.QuarantineRoot, ".smbdeleteAAA01234.4");
        await File.WriteAllTextAsync(marker, "still open elsewhere");
        receipt = (await Service.GetReceiptsAsync()).Single();

        var preview = await Service.PreviewPendingDeletionFilesAsync(receipt, manifest, default);
        Assert.IsNotNull(preview);
        Assert.AreEqual((Path.Combine(receipt.QuarantineRoot, "second.bin"), 3L), preview.Single());
        var result = await Service.DeleteAfterDeliveryAsync(source.Id, [published.Id]);
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual(receipt.ReceiptId, result.ReceiptId);
        Assert.AreEqual(1, result.RemovedFiles);
        Assert.AreEqual(3L, result.RemovedBytes);
        Assert.AreEqual("still open elsewhere", await File.ReadAllTextAsync(marker));
        Assert.AreEqual("one", await File.ReadAllTextAsync(published.Files.Single()));
        Assert.AreEqual("two", await File.ReadAllTextAsync(publishedSecond));
    }

    [TestMethod]
    public async Task Interrupted_deletion_does_not_retire_a_missing_quarantine_mount()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        _store.Records[output.Id] = output with { Kind = "publishedFiles" };
        Assert.IsTrue((await Service.QuarantineAsync(source.Id, [output.Id])).Completed);
        _store.FailNextLocation = true;
        var first = await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id]);
        Assert.IsFalse(first.Completed); Assert.AreEqual(1, first.RemovedFiles);
        var quarantineRoot = _store.Records[source.Id].RootDirectory;
        Directory.Delete(quarantineRoot);
        var retry = await Service.DeleteAfterDeliveryAsync(source.Id, [output.Id]);
        Assert.IsFalse(retry.Completed); Assert.AreEqual(0, retry.RemovedFiles); Assert.AreEqual(0L, retry.RemovedBytes);
        Assert.AreEqual(ArtifactLocationState.Quarantined, _store.Records[source.Id].State);
        Assert.AreEqual("Deleting", (await Service.GetReceiptsAsync()).Single().Status);
        Assert.IsTrue(File.Exists(output.Files.Single()));
    }
}
