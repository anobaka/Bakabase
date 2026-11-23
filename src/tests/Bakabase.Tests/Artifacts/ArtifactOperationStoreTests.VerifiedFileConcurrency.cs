using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Tests.Artifacts;

public sealed partial class ArtifactOperationStoreTests
{
    [DataTestMethod]
    [DataRow("independent")]
    [DataRow("same-root")]
    [DataRow("same-volume")]
    [DataRow("unidentified-volume")]
    [DataRow("same-content")]
    [DataRow("native-same-content")]
    [DataRow("native-empty")]
    [DataRow("remote-empty")]
    [DataRow("same-length")]
    [DataRow("parent")]
    [DataRow("unverified")]
    [DataRow("different-file-at-confirmation")]
    [DataRow("recursive")]
    [DataRow("cancel")]
    public async Task Published_file_removal_overlaps_only_a_verified_independent_parentless_download_read(string variant)
    {
        var publication = await Files("publication");
        var source = variant == "same-root" ? publication : await Files("remote-download");
        if (variant is "native-empty" or "remote-empty")
        {
            await File.WriteAllBytesAsync(publication.Files[0], []);
            await File.WriteAllBytesAsync(source.Files[0], []);
        }
        else if (variant is "same-content" or "same-length" or "native-same-content")
        {
            var original = await File.ReadAllBytesAsync(publication.Files[0]);
            if (variant == "same-length") original[0] ^= 127;
            await File.WriteAllBytesAsync(source.Files[0], original);
        }
        var published = await Adopt("publication", publication with {Kind = "publishedFiles"});
        var checkedFiles = await ArtifactManifestFiles.CaptureAsync(source.RootDirectory, source.Files.ToArray());
        var sourceBytes = await File.ReadAllBytesAsync(source.Files[0]);
        if (variant == "different-file-at-confirmation") await File.AppendAllTextAsync(publication.Files[0], "changed");
        VerifiedStorageMount? Volume(string root, IReadOnlyList<string> _) => variant == "unidentified-volume" ? null :
            new(variant is "same-volume" or "cancel" || root == publication.RootDirectory ? "delivery" : "download",
                NativeFileIdentities: variant.StartsWith("native-", StringComparison.Ordinal));

        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var continueReading = new ManualResetEventSlim();
        var clock = new AdoptionClock(sample =>
        {
            if (sample != 2) return;
            reading.TrySetResult();
            if (!continueReading.Wait(TimeSpan.FromSeconds(12))) throw new TimeoutException();
        });
        await using var downloadDb = OpenDb();
        await using var cleanupDb = OpenDb();
        var download = new ArtifactOperationStore(downloadDb, () => _appData, null, clock, Volume);
        var cleanup = new ArtifactOperationStore(cleanupDb, () => _appData, null, null, Volume);
        var register = Task.Run(() => download.AdoptAsync(Request("download") with {Kind = "download"},
            source with {Kind = "download", ExpectedManifestFingerprint = checkedFiles.Fingerprint,
                VerifiedDownloadFiles = variant == "unverified" ? null : checkedFiles,
                ParentArtifactIds = variant == "parent" ? [published.Id] : null}, "owner"));
        using var cancellation = new CancellationTokenSource();
        try
        {
            await reading.Task.WaitAsync(TimeSpan.FromSeconds(8));
            var removal = variant == "recursive" ? cleanup.AcquireMaintenanceAsync(cancellation.Token) :
                cleanup.AcquirePublishedFilesMaintenanceAsync(published.Id, published.RootDirectory, published.Fingerprint,
                    published.Files, cancellation.Token);
            if (variant is "independent" or "same-length" or "native-same-content" or "native-empty")
            {
                var fence = await removal.WaitAsync(TimeSpan.FromSeconds(4));
                await using var consumerDb = OpenDb();
                var consumer = Store(consumerDb).AttachConsumerAsync(published.OperationId, "later-consumer", LeaseTime);
                try
                {
                    continueReading.Set();
                    await Task.Delay(60);
                    Assert.IsFalse(register.IsCompleted, "Adoption must regain the general fence before committing.");
                    Assert.IsFalse(consumer.IsCompleted, "New consumers must wait for the final references and unlink.");
                    File.Delete(published.Files[0]);
                }
                finally { await fence.DisposeAsync(); }
                // A stale output is still resolved through the existing store contract.
                await consumer;
                Assert.IsFalse(File.Exists(published.Files[0]));
            }
            else
            {
                await Task.Delay(60);
                Assert.IsFalse(removal.IsCompleted, variant);
                cancellation.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await removal);
                Assert.IsTrue(File.Exists(published.Files[0]));
            }
        }
        finally { continueReading.Set(); }
        var result = await register.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.AreEqual(checkedFiles.Fingerprint, result.Fingerprint);
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(source.Files[0]));
        await using var available = await cleanup.AcquireMaintenanceAsync().WaitAsync(TimeSpan.FromSeconds(3));
    }
}
