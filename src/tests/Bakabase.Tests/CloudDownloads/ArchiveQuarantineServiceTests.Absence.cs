using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Tests.CloudDownloads;

public sealed partial class ArchiveQuarantineServiceTests
{
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
