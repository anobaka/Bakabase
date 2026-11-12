using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactProcessorTests
{
    [TestMethod]
    public async Task Verified_download_container_composes_with_nested_processing_and_reuses_the_same_result()
    {
        var directory = Path.Combine(_root, "verified-download");
        var author = Path.Combine(directory, "provider-container", "Author");
        Directory.CreateDirectory(author);
        var archive = Path.Combine(author, "outer.zip");
        var companion = Path.Combine(author, "license.key");
        await File.WriteAllTextAsync(archive, "outer");
        await File.WriteAllTextAsync(companion, "keep this companion");
        var manifest = await ArtifactManifestFiles.CaptureAsync(directory, [archive, companion]);
        var evidence = FileArtifactDeliveryLayout.Bind(manifest, manifest.Entries.Select(entry =>
            new ArtifactDirectoryEntry(entry.RelativePath, false, ["provider-container"])));
        var metadata = JsonSerializer.Serialize(new DownloadArtifactMetadata(42, null, evidence), Json);
        var input = await _store.AdoptAsync(new("owned-download", "owned-download-intent", "download", "source"),
            new(directory, [archive, companion], "download", true, MetadataJson: metadata), "download-owner");
        var plan = JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "outer", Op = "extractArchive", Selector = "*.zip"},
            new() {Id = "inner", Input = "outer", Op = "extractArchive", Selector = "inner.zip", Password = "correct"}]}, Json);
        var intent = Intent(plan);

        var first = (await Execute(intent, input, plan)).Artifact!;
        var projection = FileArtifactDeliveryLayout.Preview(first, new());
        CollectionAssert.AreEquivalent(new[] {"Author/payload.txt", "Author/license.key"},
            projection.OutputManifest.Entries.Select(entry => entry.RelativePath).ToArray());
        Assert.IsTrue(File.Exists(companion), "Directory layout evidence must never authorize deleting a companion.");
        Assert.AreEqual(2, _archives.Extractions);

        var reused = (await Execute(intent, input, plan, "workflow:another:target")).Artifact!;
        Assert.AreEqual(first.Id, reused.Id);
        Assert.AreEqual(first.Fingerprint, reused.Fingerprint);
        Assert.AreEqual(2, _archives.Extractions, "Reusing corrected input metadata must not repeat extraction.");
        Assert.AreEqual(projection.ProjectionFingerprint, FileArtifactDeliveryLayout.Preview(reused, new()).ProjectionFingerprint);
        Assert.AreEqual(manifest.Fingerprint, input.Fingerprint);
    }
}
