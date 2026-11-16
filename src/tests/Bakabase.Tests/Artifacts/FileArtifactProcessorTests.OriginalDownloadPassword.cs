using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactProcessorTests
{
    [TestMethod]
    public async Task SourcePasswordNeedsAnExactSourceAndUnchangedOriginalDownloadLineage()
    {
        var path = Path.Combine(_root, "original.7z");
        await File.WriteAllTextAsync(path, "download bytes");
        var raw = await _store.AdoptAsync(new("download", "download-intent", "download", "source-v1"),
            new(_root, [path], "download", true), "download-owner");
        var snapshot = raw with {Id = "stage", Kind = "processedFiles", ParentArtifactIds = [raw.Id]};
        var target = new CloudDownloadTarget {Id = "part", SelectedSourceId = "selected", DownloadArtifactId = raw.Id,
            Sources = [new() {Id = "selected", Url = "https://example.org/s", ArchivePassword = "source-password"}]};
        var evidence = new AcquisitionFileProcessingEvidence.BoundEvidence(null, "selected", "https://example.org/s",
            "source-password", raw.Id);
        Task<string?> Candidate(ArtifactRecord input, AcquisitionFileProcessingEvidence.BoundEvidence selected) =>
            AcquisitionFileProcessingEvidence.OriginalDownloadPasswordAsync(selected, target, input, _store, default);
        Assert.AreEqual("source-password", await Candidate(snapshot, evidence));
        Assert.AreEqual("source-password", await Candidate(raw, evidence with {ContinuedArtifactId = null}));
        Assert.IsNull(await Candidate(snapshot, evidence with {Url = "https://example.org/mirror"}));
        Assert.IsNull(await Candidate(snapshot, evidence with {SourceId = "alternative"}));
        Assert.IsNull(await Candidate(snapshot, evidence with {ArchivePassword = "unrelated"}));
        Assert.IsNull(await Candidate(snapshot, evidence with {ContinuedArtifactId = snapshot.Id}));
        Assert.IsNull(await Candidate(snapshot with {Fingerprint = "different-files"}, evidence));
        Assert.IsNull(await Candidate(snapshot with {ParentArtifactIds = ["unregistered"]}, evidence));
        Assert.IsNull(await Candidate(snapshot with {ParentArtifactIds = [raw.Id, "another"]}, evidence));
    }
}
