using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Confirmed_history_cleanup_can_finish_during_an_independent_verified_download_but_excludes_other_writers(bool activeWriter)
    {
        var sourceRoot = Directory.CreateDirectory(Path.Combine(root, "separate-download-volume")).FullName;
        var file = Path.Combine(sourceRoot, "original.zip");
        await File.WriteAllTextAsync(file, new string('x', 523));
        VerifiedStorageMount Volume(string path, IReadOnlyList<string> _) => new(path == sourceRoot ? "download" : "published", false);
        store = new(db, () => root, null, null, Volume);
        var f = await HistoricalPublicationWithoutLayout(true);
        var extra = Path.Combine(f.Delivery, ".DS_Store");
        await File.WriteAllBytesAsync(extra, DeliveryFinderMetadata());
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], CleanupFiles: false, RetryRetainedFiles: true,
            DeleteDeliveredFiles: true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single();
        Assert.IsTrue(group.RegisteredFilesOnly && group.Eligible, group.ReasonCode);
        var exact = new AcquisitionTaskDeleteInput(preview.PreviewId, RetryRetainedFiles: true, DeleteDeliveredFiles: true,
            SelectedCleanupDirectories: new Dictionary<int, IReadOnlyList<string>> {[f.Task.Id] = [f.Delivery]});
        var gate = ArtifactMaintenanceGate.For(root);
        var manifest = await ArtifactManifestFiles.CaptureAsync(sourceRoot, [file]);
        var source = ArtifactVerifiedFileScope.TryCapture(sourceRoot, manifest, [file], default, Volume)!;
        await using var longRead = await gate.EnterMutationAsync(default);
        longRead.BeginVerifiedRead(source);
        var writer = activeWriter ? await gate.EnterMutationAsync(default) : null;
        try
        {
            var confirmation = f.Service.DeleteAsync(exact);
            if (writer != null)
            {
                await Task.Delay(80);
                Assert.IsFalse(confirmation.IsCompleted);
                Assert.IsTrue(f.Publication.Files.All(File.Exists));
                await writer.DisposeAsync();
            }
            var outcome = await confirmation.WaitAsync(TimeSpan.FromSeconds(6));
            Assert.AreEqual(1, outcome.RemovedFiles);
            Assert.AreEqual(group.Bytes, outcome.RemovedBytes);
            Assert.IsFalse(File.Exists(f.Publication.Files.Single()));
            Assert.IsTrue(File.Exists(extra));
            Assert.IsTrue(File.Exists(file));
            Assert.IsTrue(f.HistoricalSource.Files.All(File.Exists));
        }
        finally
        {
            if (writer != null) await writer.DisposeAsync();
            await longRead.EndVerifiedReadAsync();
        }
    }
}
