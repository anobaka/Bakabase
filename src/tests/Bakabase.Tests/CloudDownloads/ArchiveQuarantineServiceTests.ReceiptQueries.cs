using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.CloudDownloads;

public sealed partial class ArchiveQuarantineServiceTests
{
    [TestMethod]
    public async Task Receipt_queries_stream_large_manifests_and_read_current_headers_without_caching()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var result = await Service.QuarantineAsync(source.Id, [output.Id]);
        Assert.IsTrue(result.Completed);
        var path = ReceiptPath(result.ReceiptId!);
        var receipt = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var entries = Enumerable.Range(0, 12000).Select(i =>
            new ArtifactManifestEntry($"folder/{i:D6}.bin", 123, new string('a', 64))).ToArray();
        // Put all query fields after a large valid value, beyond the streaming buffer.
        var reordered = new JsonObject { ["manifest"] = JsonSerializer.SerializeToNode(
            new ArtifactManifest(entries, "historical-manifest"), new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
        foreach (var pair in receipt.Where(p => p.Key != "manifest")) reordered[pair.Key] = pair.Value?.DeepClone();
        await File.WriteAllTextAsync(path, reordered.ToJsonString());
        Assert.IsTrue(new FileInfo(path).Length > 1024 * 1024);
        var header = (await Service.GetReceiptsAsync()).Single();
        Assert.AreEqual(result.ReceiptId, header.ReceiptId);
        Assert.AreEqual(source.Id, header.ArtifactId);
        Assert.AreEqual(source.RootDirectory, header.OriginalRoot);
        Assert.AreEqual(_store.Records[source.Id].RootDirectory, header.QuarantineRoot);
        CollectionAssert.AreEqual(new[] {output.Id}, header.OutputArtifactIds.ToArray());
        Assert.AreEqual("Quarantined", header.Status);
        reordered["status"] = "Restoring";
        reordered["message"] = "retry after interruption";
        await File.WriteAllTextAsync(path, reordered.ToJsonString());
        var changed = (await Service.GetReceiptsAsync()).Single();
        Assert.AreEqual("Restoring", changed.Status);
        Assert.AreEqual("retry after interruption", changed.Message);
        Assert.IsTrue(File.Exists(_store.Records[source.Id].Files.Single()), "Listing never mutates payloads.");
    }

    [TestMethod]
    [DataRow("mismatched-id")]
    [DataRow("empty-artifact")]
    [DataRow("null-outputs")]
    [DataRow("empty-output")]
    public async Task Malformed_receipt_headers_cannot_become_cleanup_evidence(string mutation)
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var result = await Service.QuarantineAsync(source.Id, [output.Id]);
        var path = ReceiptPath(result.ReceiptId!);
        var receipt = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        switch (mutation)
        {
            case "mismatched-id": receipt["id"] = Guid.NewGuid().ToString("N"); break;
            case "empty-artifact": receipt["artifactId"] = ""; break;
            case "null-outputs": receipt["outputIds"] = null; break;
            case "empty-output": receipt["outputIds"] = new JsonArray(""); break;
        }
        await File.WriteAllTextAsync(path, receipt.ToJsonString());
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Service.GetReceiptsAsync());
        Assert.IsFalse((await Service.QuarantineAsync(source.Id, [output.Id])).Completed);
        Assert.IsTrue(File.Exists(_store.Records[source.Id].Files.Single()));
    }

    [TestMethod]
    public async Task Receipt_query_checks_complete_json_even_in_unselected_history()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var result = await Service.QuarantineAsync(source.Id, [output.Id]);
        var path = ReceiptPath(result.ReceiptId!);
        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json[..^1] + ",\"ignoredHistory\":[" + new string(' ', 32000));
        await Assert.ThrowsExceptionAsync<JsonException>(() => Service.GetReceiptsAsync());
    }

    [TestMethod]
    public async Task Header_query_does_not_replace_full_manifest_validation_when_restoring()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var result = await Service.QuarantineAsync(source.Id, [output.Id]);
        var path = ReceiptPath(result.ReceiptId!);
        var receipt = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        receipt["manifest"] = "invalid manifest shape";
        await File.WriteAllTextAsync(path, receipt.ToJsonString());
        Assert.AreEqual("Quarantined", (await Service.GetReceiptsAsync()).Single().Status);
        await Assert.ThrowsExceptionAsync<JsonException>(() => Service.RestoreAsync(result.ReceiptId!));
        Assert.IsTrue(File.Exists(_store.Records[source.Id].Files.Single()));
        Assert.IsFalse(File.Exists(source.Files.Single()));
    }

    [TestMethod]
    public async Task Pending_receipt_lookup_materializes_only_the_matching_manifest()
    {
        var historicalSource = await Artifact("history", "archive.zip", "historical input");
        var historicalOutput = await Artifact("history-output", "archive.zip", "historical input", historicalSource.Id);
        var historical = await Service.QuarantineAsync(historicalSource.Id, [historicalOutput.Id]);
        Assert.IsTrue(historical.Completed);
        var path = ReceiptPath(historical.ReceiptId!);
        var receipt = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        // Valid JSON whose unrelated manifest must not be decoded as ArtifactManifest.
        // It is not cleanup authorization for the new artifact and remains untouched.
        receipt["manifest"] = new string('x', 1024 * 1024);
        await File.WriteAllTextAsync(path, receipt.ToJsonString());
        var originalReceipt = await File.ReadAllTextAsync(path);
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var result = await Service.QuarantineAsync(source.Id, [output.Id]);
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual(originalReceipt, await File.ReadAllTextAsync(path));
        Assert.AreEqual("historical input", await File.ReadAllTextAsync(_store.Records[historicalSource.Id].Files.Single()));

        // The same malformed manifest must still block when its own artifact is selected.
        var selected = await Service.QuarantineAsync(historicalSource.Id, [historicalOutput.Id]);
        Assert.IsFalse(selected.Completed);
        Assert.AreEqual(originalReceipt, await File.ReadAllTextAsync(path));
        Assert.AreEqual("historical input", await File.ReadAllTextAsync(_store.Records[historicalSource.Id].Files.Single()));
    }

    [TestMethod]
    public async Task Pending_receipt_lookup_refuses_truncated_unrelated_json_before_moving_files()
    {
        var historicalSource = await Artifact("history", "archive.zip", "historical input");
        var historicalOutput = await Artifact("history-output", "archive.zip", "historical input", historicalSource.Id);
        var historical = await Service.QuarantineAsync(historicalSource.Id, [historicalOutput.Id]);
        var path = ReceiptPath(historical.ReceiptId!);
        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json[..^1] + ",\"ignoredHistory\":[" + new string(' ', 32000));
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var result = await Service.QuarantineAsync(source.Id, [output.Id]);
        Assert.IsFalse(result.Completed);
        Assert.AreEqual("original", await File.ReadAllTextAsync(source.Files.Single()));
        Assert.AreEqual("historical input", await File.ReadAllTextAsync(_store.Records[historicalSource.Id].Files.Single()));
    }

    [TestMethod]
    public async Task Pending_receipt_lookup_reloads_status_after_restoration()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var service = Service;
        var first = await service.QuarantineAsync(source.Id, [output.Id]);
        Assert.IsTrue((await service.RestoreAsync(first.ReceiptId!)).Completed);
        var second = await service.QuarantineAsync(source.Id, [output.Id]);
        Assert.IsTrue(second.Completed, second.Message);
        Assert.AreNotEqual(first.ReceiptId, second.ReceiptId);
        Assert.AreEqual("original", await File.ReadAllTextAsync(_store.Records[source.Id].Files.Single()));
        var headers = await service.GetReceiptsAsync();
        Assert.AreEqual("Restored", headers.Single(r => r.ReceiptId == first.ReceiptId).Status);
        Assert.AreEqual("Quarantined", headers.Single(r => r.ReceiptId == second.ReceiptId).Status);
    }

    [TestMethod]
    public async Task Receipt_query_alias_snapshot_is_fresh_and_preserves_exact_directory_boundaries()
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var sibling = await Artifact("source-other", "archive.zip", "sibling");
        var siblingOutput = await Artifact("other-output", "archive.zip", "sibling", sibling.Id);
        var service = Service;
        var first = await service.QuarantineAsync(source.Id, [output.Id]);
        var second = await service.QuarantineAsync(sibling.Id, [siblingOutput.Id]);
        var mapPath = Path.Combine(_root, "artifact-workspaces", "relocations.json");
        Directory.CreateDirectory(Path.GetDirectoryName(mapPath)!);
        foreach (var destination in new[] {Path.Combine(_root, "first-storage"), Path.Combine(_root, "second-storage")})
        {
            await File.WriteAllTextAsync(mapPath, JsonSerializer.Serialize(
                new FileProcessingWorkspaceResolver.Relocations(1, [new(source.RootDirectory, destination)]),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var headers = await service.GetReceiptsAsync();
            var relocated = headers.Single(r => r.ReceiptId == first.ReceiptId);
            Assert.AreEqual(destination, relocated.OriginalRoot);
            Assert.AreEqual(Path.Combine(destination, ".bakabase-quarantine", first.ReceiptId!), relocated.QuarantineRoot);
            var unchanged = headers.Single(r => r.ReceiptId == second.ReceiptId);
            Assert.AreEqual(sibling.RootDirectory, unchanged.OriginalRoot);
            Assert.AreEqual(_store.Records[sibling.Id].RootDirectory, unchanged.QuarantineRoot);
        }
        Assert.IsTrue(File.Exists(_store.Records[source.Id].Files.Single()), "Queries never move payloads.");
    }

    [TestMethod]
    [DataRow("overlap")]
    [DataRow("cycle")]
    [DataRow("version")]
    public async Task Receipt_query_never_reuses_a_snapshot_after_alias_metadata_becomes_invalid(string mutation)
    {
        var source = await Artifact("source", "archive.zip", "original");
        var output = await Artifact("output", "archive.zip", "original", source.Id);
        var service = Service;
        var quarantined = await service.QuarantineAsync(source.Id, [output.Id]);
        var destination = Path.Combine(_root, "destination");
        var mappings = new List<FileProcessingWorkspaceResolver.Relocation> {new(source.RootDirectory, destination)};
        var mapPath = Path.Combine(_root, "artifact-workspaces", "relocations.json");
        Directory.CreateDirectory(Path.GetDirectoryName(mapPath)!);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await File.WriteAllTextAsync(mapPath, JsonSerializer.Serialize(new FileProcessingWorkspaceResolver.Relocations(1, mappings), jsonOptions));
        Assert.AreEqual(destination, (await service.GetReceiptsAsync()).Single().OriginalRoot);
        if (mutation == "overlap") mappings.Add(new(Path.Combine(source.RootDirectory, "child"), Path.Combine(_root, "other")));
        if (mutation == "cycle") mappings.Add(new(destination, source.RootDirectory));
        await File.WriteAllTextAsync(mapPath, JsonSerializer.Serialize(
            new FileProcessingWorkspaceResolver.Relocations(mutation == "version" ? 2 : 1, mappings), jsonOptions));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => service.GetReceiptsAsync());
        Assert.IsTrue(File.Exists(_store.Records[source.Id].Files.Single()));
        Assert.IsTrue(File.Exists(ReceiptPath(quarantined.ReceiptId!)));
    }

    private string ReceiptPath(string id) => Path.Combine(_root, "file-processing", "quarantine-receipts", id + ".json");
}
