using System.Text.Json.Nodes;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [DataTestMethod]
    [DataRow("empty-accepted")]
    [DataRow("private-payload")]
    [DataRow("unknown-payload")]
    [DataRow("content-checks-required")]
    public async Task Pending_retired_history_allows_only_an_explicitly_accepted_empty_workspace_noop(string scenario)
    {
        var privatePayload = scenario == "private-payload";
        var f = await EmptyRetiredWorkspaceFixture(boundary: !privatePayload, keepPrivateInputs: privatePayload);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(f.StateFile))!;
        if (!privatePayload) { json["completed"] = false; json["finalManifest"] = null; }
        json["pendingOperation"] = "a-later-operation-that-did-not-finish";
        await File.WriteAllTextAsync(f.StateFile, json.ToJsonString());
        if (scenario == "unknown-payload") await File.WriteAllTextAsync(Path.Combine(f.OperationRoot, "manual.key"), "preserve");
        var remaining = Directory.GetFiles(f.OperationRoot, "*", SearchOption.AllDirectories).ToArray();
        if (privatePayload) Assert.IsTrue(remaining.Length > 1);
        var history = await File.ReadAllTextAsync(f.StateFile);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true,
            SkipFileVerification: scenario != "content-checks-required"));
        var group = preview.Rows.Single().Cleanup!.Groups.Single();
        Assert.AreEqual(scenario == "empty-accepted", group.Eligible, group.ReasonCode);
        if (scenario == "empty-accepted") Assert.AreEqual("AlreadyAbsent", group.ReasonCode);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsTrue(remaining.All(File.Exists));
        Assert.AreEqual(history, await File.ReadAllTextAsync(f.StateFile));
        Assert.IsTrue(f.Final.Files.All(File.Exists));
    }
}
