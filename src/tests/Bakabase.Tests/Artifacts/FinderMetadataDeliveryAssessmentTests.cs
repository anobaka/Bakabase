using System.Buffers.Binary;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class FinderMetadataDeliveryAssessmentTests
{
    private string root = null!;

    [TestInitialize]
    public void Initialize() => Directory.CreateDirectory(root = Path.Combine(Path.GetTempPath(), "finder-delivery-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    private static byte[] FinderHeaderFile()
    {
        var bytes = new byte[6148];
        "\0\0\0\u0001Bud1"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 4096);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12, 4), 2048);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), 4096);
        return bytes;
    }

    private async Task<string> Write(string relative, byte[] bytes)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    private ArtifactRecord Artifact(params string[] files)
    {
        var entries = files.Select(path => new ArtifactManifestEntry(Path.GetRelativePath(root, path).Replace('\\', '/'),
            new FileInfo(path).Length, new string('0', 64))).ToArray();
        return new("artifact", "operation", "processedFiles", root, files,
            new(entries, ArtifactManifestFiles.Fingerprint(entries)), "fixture", ArtifactLocationState.Available, true, [], null);
    }

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(17)]
    public async Task Finished_media_with_valid_finder_metadata_passes_both_review_sizes_and_keeps_all_files(int count)
    {
        var metadata = await Write("audio/.DS_Store", FinderHeaderFile());
        var files = new List<string> {metadata};
        for (var i = 0; i < count; i++) files.Add(await Write($"audio/{i}.wav", "RIFF0000WAVE"u8.ToArray()));
        var result = await FileArtifactDeliveryAssessment.AssessAsync(Artifact(files.ToArray()), default, extractionWasReviewed: true);
        Assert.IsFalse(result.Uncertain);
        Assert.IsFalse(result.Automatic);
        Assert.AreEqual(0, result.Files.Count);
        CollectionAssert.AreEqual(FinderHeaderFile(), await File.ReadAllBytesAsync(metadata));
        Assert.IsTrue(files.All(File.Exists));
    }

    [TestMethod]
    public async Task Reserved_name_without_the_finder_header_does_not_make_unknown_content_final()
    {
        var media = await Write("cover.png", "PNG"u8.ToArray());
        foreach (var content in new[] {"ordinary user data"u8.ToArray(), "\0\0\0\u0001Bud1"u8.ToArray(),
                     new byte[40], "PK\u0003\u0004disguised archive"u8.ToArray()})
        {
            var metadata = await Write(".DS_Store", content);
            Assert.IsFalse(await FinderMetadataDeliveryProbe.IsMetadataAsync(metadata, default));
            var result = await FileArtifactDeliveryAssessment.AssessAsync(Artifact(media, metadata), default, extractionWasReviewed: true);
            Assert.IsTrue(result.Uncertain);
            Assert.IsTrue(result.Files.Contains(".DS_Store"));
            CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(metadata));
        }
    }

    [TestMethod]
    public async Task Finder_metadata_alone_is_not_a_finished_resource()
    {
        var metadata = await Write("empty-author-folder/.DS_Store", FinderHeaderFile());
        Assert.IsTrue(await FinderMetadataDeliveryProbe.IsMetadataAsync(metadata, default));
        var result = await FileArtifactDeliveryAssessment.AssessAsync(Artifact(metadata), default, extractionWasReviewed: true);
        Assert.IsTrue(result.Uncertain);
        Assert.AreEqual(1, result.Files.Count);
        Assert.IsTrue(File.Exists(metadata));
    }

    [TestMethod]
    public async Task Invalid_root_addresses_truncation_and_oversized_metadata_remain_unclassified()
    {
        var badOffset = FinderHeaderFile(); BinaryPrimitives.WriteUInt32BigEndian(badOffset.AsSpan(16, 4), 32);
        var overflow = FinderHeaderFile(); BinaryPrimitives.WriteUInt32BigEndian(overflow.AsSpan(12, 4), uint.MaxValue);
        var overlapping = FinderHeaderFile();
        BinaryPrimitives.WriteUInt32BigEndian(overlapping.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(overlapping.AsSpan(16, 4), 0);
        foreach (var content in new[] {badOffset, overflow, overlapping, FinderHeaderFile()[..100]})
        {
            var path = await Write(".DS_Store", content);
            Assert.IsFalse(await FinderMetadataDeliveryProbe.IsMetadataAsync(path, default));
        }
        var oversized = await Write(".DS_Store", FinderHeaderFile());
        await using (var file = new FileStream(oversized, FileMode.Open, FileAccess.Write))
            file.SetLength(FinderMetadataDeliveryProbe.MaximumBytes + 1L);
        Assert.IsFalse(await FinderMetadataDeliveryProbe.IsMetadataAsync(oversized, default));
    }

    [TestMethod]
    public async Task Valid_header_under_another_name_and_unknown_companions_are_not_whitelisted()
    {
        var metadata = await Write(".DS_Store", FinderHeaderFile());
        var unknown = await Write("notes.data", FinderHeaderFile());
        var media = await Write("cover.jpg", "JPEG"u8.ToArray());
        Assert.IsFalse(await FinderMetadataDeliveryProbe.IsMetadataAsync(unknown, default));
        Assert.IsTrue((await FileArtifactDeliveryAssessment.AssessAsync(Artifact(metadata, media, unknown), default,
            extractionWasReviewed: true)).Uncertain);
        Assert.IsTrue(File.Exists(unknown));
    }

    [TestMethod]
    public async Task Finder_metadata_does_not_hide_a_remaining_archive_or_authorize_unpacking()
    {
        var metadata = await Write(".DS_Store", FinderHeaderFile());
        var archive = await Write("remaining.zip", "PK\u0003\u0004outer archive"u8.ToArray());
        var result = await FileArtifactDeliveryAssessment.AssessAsync(Artifact(metadata, archive), default, extractionWasReviewed: true);
        Assert.IsTrue(result.Uncertain);
        Assert.IsFalse(result.Automatic);
        Assert.IsTrue(File.Exists(archive));
    }

    [TestMethod]
    public async Task Companion_budget_and_caller_cancellation_still_apply()
    {
        var files = new List<string> {await Write("image.jpg", "JPEG"u8.ToArray())};
        for (var i = 0; i <= FileArtifactDeliveryAssessment.MaximumCompanionProbeFiles; i++)
            files.Add(await Write($"{i}/.DS_Store", FinderHeaderFile()));
        Assert.IsTrue((await FileArtifactDeliveryAssessment.AssessAsync(Artifact(files.ToArray()), default,
            extractionWasReviewed: true)).Uncertain);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => FinderMetadataDeliveryProbe.IsMetadataAsync(files[1], cancelled.Token));
    }
}
