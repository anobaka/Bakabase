using System.Text;
using System.Text.Json.Nodes;
using Bakabase.Service.Components.Acquisition.Planning;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [TestMethod]
    public async Task Retired_successful_boundary_can_be_absent_after_the_stopped_plan_did_not_finish()
    {
        var f = await EmptyRetiredWorkspaceFixture(boundary: true);
        var state = JsonNode.Parse(await File.ReadAllTextAsync(f.StateFile))!;
        state["completed"] = false;
        state["finalManifest"] = null;
        await File.WriteAllTextAsync(f.StateFile, state.ToJsonString());
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, SkipFileVerification: true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single();
        Assert.AreEqual("AlreadyAbsent", group.ReasonCode);
        Assert.IsTrue(group.Eligible);
        Assert.IsTrue(File.Exists(f.StateFile));
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }

    [TestMethod]
    public async Task Large_completed_state_is_retained_as_history_without_reporting_missing_payload_as_unverified()
    {
        var f = await EmptyRetiredWorkspaceFixture();
        var original = await File.ReadAllTextAsync(f.StateFile);
        // A legitimate executor ledger can exceed the old 64 MiB whole-document cap.
        // Padding keeps the fixture cheap while exercising the streamed projection.
        await using (var stream = new FileStream(f.StateFile, FileMode.Create, FileAccess.Write))
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(original[..^1]));
            var padding = Enumerable.Repeat((byte)' ', 1024 * 1024).ToArray();
            for (var i = 0; i < 65; i++) await stream.WriteAsync(padding);
            await stream.WriteAsync("}"u8.ToArray());
        }
        var length = new FileInfo(f.StateFile).Length;
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, SkipFileVerification: true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single();
        Assert.AreEqual("AlreadyAbsent", group.ReasonCode);
        Assert.IsTrue(group.Eligible);
        Assert.IsFalse(group.SizeUnknown);
        Assert.AreEqual(0, group.FileCount);
        Assert.AreEqual(length, new FileInfo(f.StateFile).Length);
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }
}
