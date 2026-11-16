using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [DataTestMethod]
    [DataRow("empty", true)]
    [DataRow("empty", false)]
    [DataRow("payload", true)]
    [DataRow("missing-locator", true)]
    [DataRow("offline-storage", true)]
    [DataRow("changed-boundary", true)]
    [DataRow("added-after-preview", true)]
    public async Task Recorded_plan_identity_only_acknowledges_an_independently_proven_empty_checkpoint(string scenario, bool skip)
    {
        var f = await EmptyRetiredWorkspaceFixture(boundary: true, recordedPlanIdentity: true);
        var stateBefore = await File.ReadAllTextAsync(f.StateFile);
        var extra = Path.Combine(f.OperationRoot, "unknown.key");
        if (scenario == "payload") await File.WriteAllTextAsync(extra, "must remain");
        if (scenario == "missing-locator")
            foreach (var file in Directory.GetFiles(Path.Combine(root, "artifact-workspaces", "locations"), "checkpoints-*.json")) File.Delete(file);
        if (scenario == "offline-storage") Directory.Move(Path.Combine(root, "processing"), Path.Combine(root, "offline"));
        if (scenario == "changed-boundary")
        {
            var state = JsonNode.Parse(stateBefore)!;
            state["boundaryResults"]!["first"]!["manifest"]!["fingerprint"] = new string('0', 64);
            await File.WriteAllTextAsync(f.StateFile, state.ToJsonString());
        }
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, SkipFileVerification: skip));
        var group = preview.Rows.Single().Cleanup!.Groups.Single();
        var expected = scenario is "empty" or "added-after-preview";
        Assert.AreEqual(expected, group.Eligible, group.ReasonCode);
        Assert.AreEqual(expected ? "AlreadyAbsent" : "CLEANUP_RETIRED_WORKSPACE_REQUIRES_REVIEW", group.ReasonCode);
        Assert.AreEqual(0, group.Files.Count);
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
        if (!expected) { if (scenario == "payload") Assert.IsTrue(File.Exists(extra)); return; }
        if (scenario == "added-after-preview") await File.WriteAllTextAsync(extra, "added after the exact preview");
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(scenario == "empty" ? 1 : 0, result.DeletedTaskIds.Count);
        Assert.AreEqual(0, result.RemovedFiles); Assert.AreEqual(0L, result.RemovedBytes);
        Assert.AreEqual(stateBefore, await File.ReadAllTextAsync(f.StateFile));
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
        if (scenario == "added-after-preview") Assert.IsTrue(File.Exists(extra));
    }
}
