using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.CloudDownloads.Baidu;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class CloudDownloadPayloadRootTests
{
    private string directory = null!;

    [TestInitialize]
    public void Initialize() => directory = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "cloud-payload-root-" + Guid.NewGuid().ToString("N"))).FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(directory, true);

    [TestMethod]
    public async Task Verified_transfer_container_changes_only_logical_root_and_preserves_author_tree_and_all_files()
    {
        var (entry, journal) = await TransferFixture("bakabase-authored/album/001.jpg", "key.dat");
        var container = Path.Combine(directory, "download", entry.Path.Split('/')[^1]);
        var authored = Directory.CreateDirectory(Path.Combine(container, "bakabase-authored", "album")).FullName;
        var image = Path.Combine(authored, "001.jpg");
        var key = Path.Combine(container, "key.dat");
        await File.WriteAllBytesAsync(image, [1, 2, 3]);
        await File.WriteAllTextAsync(key, "key");
        var files = new[] {image, key};
        var originalBytes = await File.ReadAllBytesAsync(journal);

        var candidate = await BaiduOwnedDownloadRoot.ResolveAsync(directory, entry,
            Path.Combine(directory, "download"), default);
        var root = CloudDownloadPayloadRoot.Select(Path.Combine(directory, "download"), candidate, files);
        var manifest = await ArtifactManifestFiles.CaptureAsync(root, files, ct: default);

        Assert.AreEqual(container, root);
        CollectionAssert.AreEquivalent(new[] {"bakabase-authored/album/001.jpg", "key.dat"},
            manifest.Entries.Select(file => file.RelativePath.Replace('\\', '/')).ToArray());
        Assert.AreEqual(2, manifest.Entries.Count);
        Assert.IsTrue(files.All(File.Exists), "No physical path is renamed or removed; download checkpoints remain valid.");
        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(journal), "Inspecting ownership cannot alter transfer history.");
    }

    [TestMethod]
    public async Task Layout_changes_only_baidu_logical_root_and_artifact_identity()
    {
        var child = Directory.CreateDirectory(Path.Combine(directory, "owned")).FullName;
        var authored = Directory.CreateDirectory(Path.Combine(child, "author")).FullName;
        var file = Path.Combine(authored, "image.jpg");
        await File.WriteAllBytesAsync(file, [1]);
        var source = new CloudSource {Id = "source", ProviderId = "baidu", Url = "https://example.com/share"};
        var target = new CloudDownloadTarget {Id = "target", Sources = [source], SelectedSourceId = source.Id};
        var separate = target with {DownloadLayout = "separateDirectory"};
        Assert.AreEqual(child, CloudDownloadPayloadRoot.SelectLayout(directory, child, [file], target));
        Assert.AreEqual(directory, CloudDownloadPayloadRoot.SelectLayout(directory, child, [file], separate));
        Assert.AreEqual(DownloadArtifactCoordinator.DownloadIntent(target, directory), DownloadArtifactCoordinator.DownloadIntent(separate, directory));
        Assert.AreNotEqual(DownloadArtifactCoordinator.ArtifactIntent(target, directory), DownloadArtifactCoordinator.ArtifactIntent(separate, directory));
        var manifest = await ArtifactManifestFiles.CaptureAsync(directory, [file], ct: default);
        Assert.AreEqual("owned/author/image.jpg", manifest.Entries.Single().RelativePath.Replace('\\', '/'));
        var http = separate with {Sources = [source with {ProviderId = "http"}]};
        Assert.AreEqual(DownloadArtifactCoordinator.DownloadIntent(http, directory), DownloadArtifactCoordinator.ArtifactIntent(http, directory));
    }

    [TestMethod]
    public async Task Old_request_and_name_only_guess_keep_original_root_even_when_a_receipt_exists()
    {
        var (entry, _) = await TransferFixture();
        // Simulate the frozen JSON of a download created before ownership metadata existed.
        var old = JsonSerializer.Deserialize<CloudRemoteEntry>(JsonSerializer.Serialize(new
            {entry.ProviderId, entry.AccountId, entry.Path, entry.ManifestJson}))!;
        var child = Directory.CreateDirectory(Path.Combine(directory, entry.Path.Split('/')[^1])).FullName;
        var file = Path.Combine(child, "image.jpg");
        await File.WriteAllBytesAsync(file, [1]);

        Assert.IsNull(old.OwnedContainer);
        var candidate = await BaiduOwnedDownloadRoot.ResolveAsync(directory, old, directory, default);
        Assert.IsNull(candidate);
        Assert.AreEqual(directory, CloudDownloadPayloadRoot.Select(directory, candidate, [file]));
    }

    [TestMethod]
    [DataRow("missingJournal")]
    [DataRow("wrongOperation")]
    [DataRow("differentOwnerPath")]
    [DataRow("differentRemotePath")]
    [DataRow("unfinishedTransfer")]
    [DataRow("differentManifest")]
    [DataRow("malformedReceipt")]
    [DataRow("differentProvider")]
    public async Task Unproven_ownership_is_optional_and_never_changes_the_root(string mutation)
    {
        var (entry, journal) = await TransferFixture();
        switch (mutation)
        {
            case "missingJournal": File.Delete(journal); break;
            case "wrongOperation": entry = entry with {OwnedContainer = entry.OwnedContainer! with {TransferOperationId = "other"}}; break;
            case "differentOwnerPath": entry = entry with {OwnedContainer = entry.OwnedContainer! with {Path = "/other"}}; break;
            case "differentRemotePath":
                await File.WriteAllTextAsync(journal, (await File.ReadAllTextAsync(journal)).Replace(entry.Path, "/unowned")); break;
            case "unfinishedTransfer":
                await File.WriteAllTextAsync(journal, (await File.ReadAllTextAsync(journal)).Replace("completed", "prepared")); break;
            case "differentManifest": entry = entry with {ManifestJson = "[]"}; break;
            case "malformedReceipt": await File.WriteAllTextAsync(journal, "{"); break;
            case "differentProvider": entry = entry with {ProviderId = "other"}; break;
        }
        Assert.IsNull(await BaiduOwnedDownloadRoot.ResolveAsync(directory, entry, directory, default));
    }

    [TestMethod]
    public async Task Root_must_contain_every_file_including_sidecars_outside_the_candidate()
    {
        var child = Directory.CreateDirectory(Path.Combine(directory, "container")).FullName;
        var content = Path.Combine(child, "archive.zip");
        var sidecar = Path.Combine(directory, "key.dat");
        await File.WriteAllBytesAsync(content, [1]);
        await File.WriteAllBytesAsync(sidecar, [2]);
        var selected = CloudDownloadPayloadRoot.Select(directory, child, [content, sidecar]);
        Assert.AreEqual(directory, selected);
        var manifest = await ArtifactManifestFiles.CaptureAsync(selected, [content, sidecar], ct: default);
        CollectionAssert.AreEquivalent(new[] {"container/archive.zip", "key.dat"},
            manifest.Entries.Select(file => file.RelativePath.Replace('\\', '/')).ToArray());
    }

    [TestMethod]
    public async Task Sibling_prefix_outside_root_and_relative_candidates_are_rejected()
    {
        var physical = Directory.CreateDirectory(Path.Combine(directory, "download")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(directory, "download-other")).FullName;
        var file = Path.Combine(outside, "image.jpg");
        await File.WriteAllBytesAsync(file, [1]);
        foreach (var candidate in new[] {outside, directory, "relative", Path.Combine(physical, "..", "download-other")})
        {
            Assert.IsFalse(CloudDownloadPayloadRoot.TryValidate(physical, candidate, [file], out _));
            Assert.AreEqual(physical, CloudDownloadPayloadRoot.Select(physical, candidate, [file]));
        }
    }

    [TestMethod]
    public async Task Missing_files_and_duplicate_manifests_do_not_select_a_narrower_root()
    {
        var root = Directory.CreateDirectory(Path.Combine(directory, "root")).FullName;
        var file = Path.Combine(root, "image.jpg");
        Assert.AreEqual(directory, CloudDownloadPayloadRoot.Select(directory, root, [file]));
        await File.WriteAllBytesAsync(file, [1]);
        Assert.AreEqual(directory, CloudDownloadPayloadRoot.Select(directory, root, [file, file]));
        Assert.AreEqual(directory, CloudDownloadPayloadRoot.Select(directory, root, [file, Path.Combine(root, ".", "image.jpg")]));
        Assert.AreEqual(directory, CloudDownloadPayloadRoot.Select(directory, root, []));
    }

    [TestMethod]
    public async Task Symbolic_links_cannot_prove_a_payload_root_or_a_transfer_receipt()
    {
        var (entry, journal) = await TransferFixture();
        var target = Directory.CreateDirectory(Path.Combine(directory, "actual")).FullName;
        var file = Path.Combine(target, "image.jpg");
        await File.WriteAllBytesAsync(file, [1]);
        var link = Path.Combine(directory, "linked");
        var journalTarget = Path.Combine(directory, "journal-copy.json");
        File.Move(journal, journalTarget);
        try
        {
            Directory.CreateSymbolicLink(link, target);
            File.CreateSymbolicLink(journal, journalTarget);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        { Assert.Inconclusive("This test requires permission to create symbolic links: " + error.GetType().Name); }
        Assert.AreEqual(directory, CloudDownloadPayloadRoot.Select(directory, link, [Path.Combine(link, "image.jpg")]));
        Assert.IsNull(await BaiduOwnedDownloadRoot.ResolveAsync(directory, entry, directory, default));
    }

    [TestMethod]
    public async Task Account_directory_links_cannot_authorize_an_owned_container()
    {
        var (entry, _) = await TransferFixture();
        var link = Path.Combine(directory, "account-link");
        try { Directory.CreateSymbolicLink(link, directory); }
        catch (Exception error) when (error is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        { Assert.Inconclusive("This test requires permission to create symbolic links: " + error.GetType().Name); }
        Assert.IsNull(await BaiduOwnedDownloadRoot.ResolveAsync(link, entry, directory, default));
    }

    [TestMethod]
    public async Task Semantic_manifest_equality_accepts_different_json_property_order_but_not_changed_content()
    {
        var (entry, _) = await TransferFixture();
        entry = entry with {ManifestJson = JsonSerializer.Serialize(new[]
            {new {isDirectory = false, size = 3, path = entry.Path + "/author/001.jpg", id = 1}})};
        Assert.IsNotNull(await BaiduOwnedDownloadRoot.ResolveAsync(directory, entry, directory, default));
        entry = entry with {ManifestJson = entry.ManifestJson.Replace("\"size\":3", "\"size\":4")};
        Assert.IsNull(await BaiduOwnedDownloadRoot.ResolveAsync(directory, entry, directory, default));
    }

    [TestMethod]
    public async Task Existing_download_receipt_binds_provenance_without_changing_manifest_or_author_folders()
    {
        var (entry, journal) = await TransferFixture("Author/game.bin", "license.key");
        var wrapper = entry.Path.Split('/')[^1];
        var entries = new[] {new ArtifactManifestEntry(wrapper + "/Author/game.bin", 3, new string('a', 64)),
            new ArtifactManifestEntry(wrapper + "/license.key", 3, new string('b', 64))};
        var manifest = new ArtifactManifest(entries, ArtifactManifestFiles.Fingerprint(entries));
        var before = await File.ReadAllTextAsync(journal);
        var evidence = await BaiduCloudDownloadProvider.ReadDirectoryEvidenceAsync(directory, entry, directory, default);
        var proof = evidence!.Bind(manifest);
        Assert.IsNotNull(proof);
        Assert.AreEqual(manifest.Fingerprint, proof.ManifestFingerprint);
        CollectionAssert.AreEqual(new[] {wrapper}, proof.Entries[0].GeneratedAncestors.ToArray());
        Assert.AreEqual(wrapper + "/Author/game.bin", manifest.Entries[0].RelativePath);
        Assert.AreEqual(before, await File.ReadAllTextAsync(journal));
        var changed = manifest with {Entries = [entries[0] with {Length = 4}, entries[1]]};
        Assert.IsNull(evidence.Bind(changed));
        Assert.IsNull(evidence.Bind(manifest with {Entries = [entries[0], entries[1], new("outside.key", 3, new string('c', 64))]}));
        Assert.IsNull(evidence.Bind(manifest with {Entries = [entries[0]]}));
    }

    [TestMethod]
    public async Task Existing_download_without_typed_owner_or_complete_receipt_retains_original_layout()
    {
        var (entry, journal) = await TransferFixture();
        Assert.IsNull(await BaiduCloudDownloadProvider.ReadDirectoryEvidenceAsync(directory,
            entry with {OwnedContainer = null}, directory, default));
        await File.WriteAllTextAsync(journal, "{}");
        Assert.IsNull(await BaiduCloudDownloadProvider.ReadDirectoryEvidenceAsync(directory, entry, directory, default));
    }

    [TestMethod]
    public async Task Existing_portable_path_map_is_bound_to_account_operation_destination_and_all_members()
    {
        var (entry, _) = await TransferFixture("Author/long name.zip");
        var request = new CloudDownloadRequest("download-op", new CloudSource {Id = "s", Url = "https://example.com/s", ProviderId = "baidu"},
            "account", entry, directory);
        var wrapper = entry.Path.Split('/')[^1];
        var mapped = wrapper + "/Author/long~0123456789abcdef.zip";
        using var remoteManifest = JsonDocument.Parse(entry.ManifestJson!);
        var fingerprint = BaiduDownloadDirectoryEvidence.Fingerprint(request.OperationId, "123", directory, entry.Path, remoteManifest.RootElement);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.OperationId))).ToLowerInvariant();
        var map = Path.Combine(directory, "operations", key + ".paths-v1.json");
        await File.WriteAllTextAsync(map, JsonSerializer.Serialize(new {version = 1, uid = "123", fingerprint,
            paths = new[] {new {Remote = entry.Path + "/Author/long name.zip", Relative = mapped}}}));
        var localEntries = new[] {new ArtifactManifestEntry(mapped, 3, new string('a', 64))};
        var localManifest = new ArtifactManifest(localEntries, ArtifactManifestFiles.Fingerprint(localEntries));
        var proof = await BaiduDownloadDirectoryEvidence.ReadAsync(directory, "123", request, directory, default);
        Assert.IsNotNull(proof?.Bind(localManifest));
        Assert.IsNull(await BaiduDownloadDirectoryEvidence.ReadAsync(directory, "wrong-account", request, directory, default));
        Assert.IsNull(await BaiduDownloadDirectoryEvidence.ReadAsync(directory, "123", request with {OutputDirectory = directory + "-changed"}, directory, default));
        await File.WriteAllTextAsync(map, JsonSerializer.Serialize(new {version = 1, uid = "123", fingerprint,
            paths = new[] {new {Remote = entry.Path + "/Author/long name.zip", Relative = "../outside.zip"}}}));
        Assert.IsNull(await BaiduDownloadDirectoryEvidence.ReadAsync(directory, "123", request, directory, default));
    }

    [TestMethod]
    public void Path_map_fingerprint_matches_bridge_UTF8_JSON_contract()
    {
        using var document = JsonDocument.Parse("""
            [{"id":2,"path":"/transfer/根/😀.txt","size":4,"isDirectory":false},
             {"id":1,"path":"/transfer/根/a\u2028.txt","size":3,"isDirectory":false}]
            """);
        Assert.AreEqual("9ea53e7fd3aa9d855081f44793f0c03b71d17348c8fc3c079935880909173f24",
            BaiduDownloadDirectoryEvidence.Fingerprint("operation", "123", "/下载/<>&😀", "/transfer/根", document.RootElement));
    }

    private async Task<(CloudRemoteEntry Entry, string Journal)> TransferFixture(params string[] relativePaths)
    {
        const string operation = "transfer-operation";
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operation))).ToLowerInvariant();
        var remotePath = "/transfers/bakabase-" + key[..24];
        if (relativePaths.Length == 0) relativePaths = ["author/001.jpg"];
        var manifest = relativePaths.Select((relative, index) =>
            new {id = index + 1, path = remotePath + "/" + relative, size = 3, isDirectory = false}).ToArray();
        var journal = Path.Combine(Directory.CreateDirectory(Path.Combine(directory, "operations")).FullName, key + ".json");
        await File.WriteAllTextAsync(journal, JsonSerializer.Serialize(new {stage = "completed", remotePath, manifest}));
        return (new("baidu", "account", remotePath, JsonSerializer.Serialize(manifest))
            {OwnedContainer = new(operation, remotePath)}, journal);
    }
}
