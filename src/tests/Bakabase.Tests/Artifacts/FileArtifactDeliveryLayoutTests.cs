using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class FileArtifactDeliveryLayoutTests
{
    private static ArtifactRecord Input(string[] files, string[]? directories = null, ArtifactDirectoryEntry[]? evidence = null)
    {
        var entries = files.OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new ArtifactManifestEntry(p, 1, ArtifactManifestFiles.Hash(p))).ToArray();
        var manifest = new ArtifactManifest(entries, ArtifactManifestFiles.Fingerprint(entries, directories), directories);
        var metadata = evidence == null ? null : JsonSerializer.Serialize(new FileArtifactProcessingMetadata("input", "{}", null,
            DirectoryProvenance: FileArtifactDeliveryLayout.Bind(manifest, evidence)), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new("source", "op", "processedFiles", "/unused", [], manifest, manifest.Fingerprint,
            ArtifactLocationState.Available, true, [], metadata);
    }

    [TestMethod] public void Only_measured_outer_is_removed_and_optional_author_collapse_removes_one_layer()
    {
        const string file = "课件2939/Author/images/01.jpg", directory = "课件2939/Author/Save";
        var item = Input([file], [directory], [new(file, false, ["课件2939"]), new(directory, true, ["课件2939"])]);
        var automatic = FileArtifactDeliveryLayout.Preview(item, new());
        Assert.AreEqual("Author/images/01.jpg", automatic.FileMappings.Single().TargetRelativePath);
        Assert.AreEqual("Author/Save", automatic.DirectoryMappings.Single().TargetRelativePath);
        var oneMore = FileArtifactDeliveryLayout.Preview(item, new() {CollapseSingleRootDirectory = true});
        Assert.AreEqual("images/01.jpg", oneMore.FileMappings.Single().TargetRelativePath);
        Assert.AreEqual("Save", oneMore.DirectoryMappings.Single().TargetRelativePath);
        Assert.IsFalse(FileArtifactDeliveryLayout.Preview(item, new() {RemoveGeneratedDirectories = false, CollapseSingleRootDirectory = true}).Changed);
        Assert.IsFalse(FileArtifactDeliveryLayout.Preview(item, null).Changed);
    }

    [TestMethod] public void Missing_legacy_evidence_and_author_folders_that_look_managed_are_kept()
    {
        var item = Input(["bakabase-1234/课件/魔王勇者/book.pdf"]);
        Assert.IsFalse(FileArtifactDeliveryLayout.Preview(item, new()).Changed);
        Assert.AreEqual("课件/魔王勇者/book.pdf", FileArtifactDeliveryLayout.Preview(item,
            new() {CollapseSingleRootDirectory = true}).FileMappings.Single().TargetRelativePath);
    }

    [TestMethod] public void Unknown_sibling_under_a_measured_prefix_prevents_collapsing_that_directory()
    {
        var item = Input(["game/out.bin", "game/license.key"], evidence: [new("game/out.bin", false, ["game"])]);
        Assert.IsFalse(FileArtifactDeliveryLayout.Preview(item, new()).Changed);
        var withDirectory = Input(["game/out.bin"], ["game/user-save"], [new("game/out.bin", false, ["game"])]);
        Assert.IsFalse(FileArtifactDeliveryLayout.Preview(withDirectory, new()).Changed);
    }

    [TestMethod] public void Nested_generated_layers_are_removed_at_their_positions_not_in_authored_siblings()
    {
        var item = Input(["outer/Group/inner/x.bin", "outer/Group/license.key", "other/inner/x.bin"], evidence: [
            new("outer/Group/inner/x.bin", false, ["outer", "outer/Group/inner"]), new("outer/Group/license.key", false, ["outer"])]);
        var preview = FileArtifactDeliveryLayout.Preview(item, new());
        CollectionAssert.AreEquivalent(new[] {"Group/x.bin", "Group/license.key", "other/inner/x.bin"},
            preview.OutputManifest.Entries.Select(e => e.RelativePath).ToArray());
    }

    [TestMethod] public void Only_the_required_archive_wrapper_is_kept_for_duplicate_filenames()
    {
        var collision = Input(["outer/one/save.dat", "outer/two/save.dat", "outer/three/game.dat"], evidence: [
            new("outer/one/save.dat", false, ["outer", "outer/one"]), new("outer/two/save.dat", false, ["outer", "outer/two"]),
            new("outer/three/game.dat", false, ["outer", "outer/three"])]);
        var preview = FileArtifactDeliveryLayout.Preview(collision, new());
        CollectionAssert.AreEquivalent(new[] {"save.dat", "two/save.dat", "game.dat"},
            preview.OutputManifest.Entries.Select(e => e.RelativePath).ToArray());
        Assert.AreEqual(3, preview.RemovedGeneratedDirectories!.Count);
        Assert.AreEqual(preview.ProjectionFingerprint, FileArtifactDeliveryLayout.Preview(collision, new()).ProjectionFingerprint);
        var evidence = FileArtifactDeliveryLayout.ProjectProvenance(collision, preview)!;
        Assert.AreEqual("two", evidence.Entries.Single().GeneratedAncestors.Single());
        foreach (var map in preview.FileMappings)
            Assert.AreEqual(collision.Manifest.Entries.Single(e => e.RelativePath == map.SourceRelativePath).Sha256,
                preview.OutputManifest.Entries.Single(e => e.RelativePath == map.TargetRelativePath).Sha256);
    }

    [DataTestMethod]
    [DataRow("Game/a", "game/b")]
    [DataRow("café/a", "cafe\u0301/b")]
    [DataRow("game", "game/b")]
    public void Projection_keeps_one_separating_wrapper_for_portable_alias_and_file_directory_conflicts(string first, string second)
    {
        var source = Input(["one/" + first, "two/" + second], evidence: [
            new("one/" + first, false, ["one"]), new("two/" + second, false, ["two"])]);
        var preview = FileArtifactDeliveryLayout.Preview(source, new());
        CollectionAssert.AreEquivalent(new[] {first, "two/" + second}, preview.OutputManifest.Entries.Select(e => e.RelativePath).ToArray());
        Assert.IsFalse(FileArtifactDeliveryLayout.Preview(source, new() {RemoveGeneratedDirectories = false}).Changed);
    }

    [TestMethod] public void Colliding_empty_directories_keep_the_minimum_wrapper()
    {
        var source = Input([], ["group", "pack/group/empty"], [new("pack/group/empty", true, ["pack", "pack/group/empty"])]);
        var preview = FileArtifactDeliveryLayout.Preview(source, new());
        CollectionAssert.AreEquivalent(new[] {"group", "group/empty"}, preview.OutputManifest.Directories!.ToArray());
        CollectionAssert.AreEquivalent(new[] {"pack"}, preview.RemovedGeneratedDirectories!.ToArray());
    }

    [TestMethod] public void An_ambiguous_source_namespace_still_identifies_the_conflicting_inputs()
    {
        var alias = Input(["Game/a", "game/b"]);
        var error = Assert.ThrowsExactly<InvalidDataException>(() => FileArtifactDeliveryLayout.Preview(alias, new()));
        StringAssert.Contains(error.Message, "Game/a");
        StringAssert.Contains(error.Message, "game/b");
    }

    [TestMethod] public void Stale_or_malformed_provenance_cannot_remove_a_directory()
    {
        var item = Input(["pack/a"], evidence: [new("pack/a", false, ["pack"])]);
        var proof = FileArtifactDeliveryLayout.ReadProvenance(item)! with {ManifestFingerprint = ArtifactManifestFiles.Hash("old manifest")};
        var stale = item with {MetadataJson = JsonSerializer.Serialize(new {directoryProvenance = proof}, new JsonSerializerOptions(JsonSerializerDefaults.Web))};
        Assert.IsFalse(FileArtifactDeliveryLayout.Preview(stale, new()).Changed);
        Assert.IsFalse(FileArtifactDeliveryLayout.Preview(item with {MetadataJson = "not JSON"}, new()).Changed);
    }

    [TestMethod] public void Download_provenance_preserves_author_paths_and_input_identity()
    {
        const string file = "provider-container/Author/game.bin", directory = "provider-container/Author/Save";
        var original = Input([file], [directory], [new(file, false, ["provider-container"]), new(directory, true, ["provider-container"])]);
        var evidence = FileArtifactDeliveryLayout.ReadProvenance(original)!;
        var download = original with {Kind = "download", MetadataJson = JsonSerializer.Serialize(
            new DownloadArtifactMetadata(42, null, evidence), new JsonSerializerOptions(JsonSerializerDefaults.Web))};

        Assert.AreEqual(evidence.ManifestFingerprint, FileArtifactDeliveryLayout.ReadProvenance(download)!.ManifestFingerprint);
        var preview = FileArtifactDeliveryLayout.Preview(download, new());
        Assert.AreEqual("Author/game.bin", preview.FileMappings.Single().TargetRelativePath);
        Assert.AreEqual("Author/Save", preview.DirectoryMappings.Single().TargetRelativePath);
        Assert.AreEqual(original.Fingerprint, download.Fingerprint);
        Assert.AreSame(original.Manifest, download.Manifest);
        Assert.AreEqual(original.RootDirectory, download.RootDirectory);
        Assert.AreEqual(original.OperationId, download.OperationId);
    }

    [TestMethod] public void Unproven_or_partial_download_metadata_never_removes_a_container()
    {
        var item = Input(["bakabase-like/archive.zip", "bakabase-like/license.key"],
            evidence: [new("bakabase-like/archive.zip", false, ["bakabase-like"])]) with {Kind = "download"};
        Assert.IsFalse(FileArtifactDeliveryLayout.Preview(item, new()).Changed);
        Assert.IsFalse(FileArtifactDeliveryLayout.Preview(item with {MetadataJson = null}, new()).Changed);
        Assert.IsFalse(FileArtifactDeliveryLayout.Preview(item with {MetadataJson = "{\"directoryProvenance\":42}"}, new()).Changed);
        var stale = FileArtifactDeliveryLayout.ReadProvenance(item)! with {ManifestFingerprint = ArtifactManifestFiles.Hash("another download")};
        Assert.IsFalse(FileArtifactDeliveryLayout.Preview(item with {MetadataJson = JsonSerializer.Serialize(
            new DownloadArtifactMetadata(42, null, stale), new JsonSerializerOptions(JsonSerializerDefaults.Web))}, new()).Changed);
        Assert.IsNull(FileArtifactDeliveryLayout.ReadProvenance(item with {Kind = "untrusted-import"}));
    }

    [TestMethod] public void Null_policy_preserves_the_established_identity_and_explicit_layout_scopes_reuse()
    {
        var target = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "delivery"));
        Assert.AreEqual(ArtifactManifestFiles.Hash("publish-artifacts-v1\nopaque\n" + target + "\n"), FileArtifactPublisher.Intent("opaque", target));
        Assert.AreNotEqual(FileArtifactPublisher.Intent("opaque", target), FileArtifactPublisher.Intent("opaque", target, deliveryPolicy: new()));
        Assert.AreNotEqual(FileArtifactPublisher.Intent("opaque", target, deliveryPolicy: new()),
            FileArtifactPublisher.Intent("opaque", target, deliveryPolicy: new() {CollapseSingleRootDirectory = true}));
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("length")]
    [DataRow("sha")]
    [DataRow("omitted-file")]
    [DataRow("omitted-directory")]
    [DataRow("wrong-source")]
    public void Publication_receipt_requires_exact_source_coverage_and_preserved_bytes(string? fault)
    {
        var source = Input(["wrapper/a.zip", "wrapper/b.txt"], ["wrapper/empty"]);
        var preview = FileArtifactDeliveryLayout.Preview(source, new() {CollapseSingleRootDirectory = true});
        var manifest = preview.OutputManifest;
        var mappings = preview.FileMappings;
        var directories = preview.DirectoryMappings;
        if (fault is "length" or "sha")
        {
            var entries = manifest.Entries.ToArray();
            entries[0] = fault == "length" ? entries[0] with {Length = entries[0].Length + 1} :
                entries[0] with {Sha256 = ArtifactManifestFiles.Hash("different bytes")};
            manifest = new(entries, ArtifactManifestFiles.Fingerprint(entries, manifest.Directories), manifest.Directories);
        }
        if (fault == "omitted-file")
        {
            var entries = manifest.Entries.Skip(1).ToArray();
            mappings = mappings.Skip(1).ToArray();
            manifest = new(entries, ArtifactManifestFiles.Fingerprint(entries, manifest.Directories), manifest.Directories);
        }
        if (fault == "omitted-directory")
        {
            directories = [];
            manifest = new(manifest.Entries, ArtifactManifestFiles.Fingerprint(manifest.Entries, []), []);
        }
        var sourceId = fault == "wrong-source" ? "unrelated" : source.Id;
        // Even an internally consistent layout receipt cannot authorize a payload replacement
        // or omission. It must also prove a complete, byte-preserving map of the source receipt.
        var signature = ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new
        {
            Version = 1, Id = sourceId, Fingerprint = source.Fingerprint, Policy = preview.Policy,
            FileMappings = mappings, DirectoryMappings = directories, OutputFingerprint = manifest.Fingerprint
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var proof = new ArtifactDeliveryLayoutProof(1, sourceId, source.Fingerprint, preview.Policy!,
            manifest.Fingerprint, signature, mappings, directories);
        var output = source with {Id = "publication", Kind = "publishedFiles", Manifest = manifest, Fingerprint = manifest.Fingerprint};
        Assert.IsTrue(FileArtifactDeliveryLayout.ValidPublication(proof, manifest));
        Assert.AreEqual(fault == null, FileArtifactDeliveryLayout.PreservesSource(source, output, proof));
        Assert.IsFalse(FileArtifactDeliveryLayout.PreservesSource(source, output, null));
        Assert.IsTrue(FileArtifactDeliveryLayout.PreservesSource(source, source, null));
    }
}
