using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [TestMethod]
    [DataRow("options")]
    [DataRow("operation")]
    public async Task Revised_transport_root_allocates_siblings_but_never_hides_concrete_paths_or_invalid_history(string kind)
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        var previous = Path.Combine(root, "old-download"); var destination = Path.Combine(root, "another-download");
        var revision = new CloudDownloadDestinationRevision(Guid.NewGuid().ToString("N"), previous, destination,
            root, DateTime.UtcNow, new('a', 64));
        var request = new CloudDownloadRequest("other-revised-download", new() {Id = "share", Url = "https://example.test/share", ProviderId = "baidu"},
            "account", null, previous) {WorkspaceDirectory = root, DestinationRevisions = [revision]};
        if (kind == "options") db.DownloadTasks.Add(new() {Id = 99, ThirdPartyId = CloudSites.Site("baidu"), DownloadPath = destination,
            Options = JsonSerializer.Serialize(request, Json)});
        else db.CloudDownloadOperations.Add(new() {Id = request.OperationId, WorkflowRunId = 2, Kind = "download", State = "started",
            RequestJson = JsonSerializer.Serialize(request, Json)});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        async Task Set(string json, string? actualDirectory = null)
        {
            if (kind == "options") await db.DownloadTasks.Where(d => d.Id == 99).ExecuteUpdateAsync(set =>
                set.SetProperty(d => d.Options, json).SetProperty(d => d.DownloadPath, actualDirectory ?? destination));
            else await db.CloudDownloadOperations.Where(o => o.Id == request.OperationId).ExecuteUpdateAsync(set => set.SetProperty(o => o.RequestJson, json));
        }
        async Task Block(CloudDownloadRequest changed, string reason, string? actualDirectory = null)
        {
            await Set(JsonSerializer.Serialize(changed, Json), actualDirectory);
            Assert.AreEqual("ReferencedPath", (await Preview(final)).Single().Reason, reason);
        }
        Assert.IsTrue((await Preview(final)).Single().Eligible,
            "A verified destination history root is not a claim on sibling downloads; the current path matches ExecutionDirectory, not frozen OutputDirectory.");
        await Block(request with {DestinationRevisions = [revision with {PreviousDirectory = Path.Combine(root, "wrong-old")} ]}, "Broken previous-directory chain stays protected.");
        await Block(request with {DestinationRevisions = [revision with {PreviousFingerprint = "invalid"}]}, "History must have a prior evidence digest.");
        await Block(request with {DestinationRevisions = [revision with {AppliedAt = default}]}, "An uncommitted revision grants no role.");
        await Block(request with {DestinationRevisions = [revision, revision]}, "Repeated revision IDs are not a chain.");
        await Block(request with {OutputDirectory = root, DestinationRevisions = [revision with {PreviousDirectory = root}]}, "Frozen source destinations remain concrete references.");
        await Block(request with {RemoteEntry = new("baidu", "account", root)}, "Actual nested remote paths do not inherit allocation semantics.");
        var inside = Path.Combine(source.RootDirectory, "new-download");
        await Block(request with {DestinationRevisions = [revision with {RootDirectory = source.RootDirectory, Directory = inside}]}, "An allocation equal to the candidate remains protected.", inside);
        var nested = Path.Combine(source.RootDirectory, "nested");
        await Block(request with {DestinationRevisions = [revision with {RootDirectory = nested, Directory = Path.Combine(nested, "download")}]}, "An allocation inside the candidate remains protected.", Path.Combine(nested, "download"));

        foreach (var field in new[] {"top", "revision"})
        {
            var node = JsonSerializer.SerializeToNode(request, Json)!;
            (field == "top" ? node : node["destinationRevisions"]![0]!)["unknown"] = true;
            await Set(node.ToJsonString());
            Assert.AreEqual("ReferencedPath", (await Preview(final)).Single().Reason, "Unknown " + field + " fields must fail closed.");
        }
        var nestedInput = JsonSerializer.SerializeToNode(request, Json)!;
        nestedInput["source"]!["additionalInput"] = root;
        await Set(nestedInput.ToJsonString());
        Assert.AreEqual("ReferencedPath", (await Preview(final)).Single().Reason, "The same root inside an unknown source input remains a concrete reference.");
        var duplicate = JsonSerializer.Serialize(request, Json).Replace("\"rootDirectory\":", "\"rootDirectory\":\"/unrelated\",\"ROOTDIRECTORY\":", StringComparison.Ordinal);
        await Set(duplicate);
        Assert.AreEqual("ReferencedPath", (await Preview(final)).Single().Reason, "Case-insensitive duplicate revision members are ambiguous.");
        if (kind == "options")
        {
            await Set(JsonSerializer.Serialize(request, Json), previous);
            Assert.AreEqual("ReferencedPath", (await Preview(final)).Single().Reason, "The native task must match the resolved physical destination.");
        }
        await Set(JsonSerializer.Serialize(request, Json));
        Assert.IsTrue((await Preview(final)).Single().Eligible);
        Assert.IsTrue(File.Exists(source.Files.Single())); Assert.IsTrue(File.Exists(final.Files.Single()));
    }
}
