using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [TestMethod]
    public async Task Explicit_accepted_cleanup_reuses_executor_coverage_for_private_inputs_after_the_result_was_retired()
    {
        var f = await EmptyRetiredWorkspaceFixture(keepPrivateInputs: true);
        Assert.AreEqual(ArtifactLocationState.Retired, (await store.GetArtifactAsync(f.Source.Id))!.State);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, SkipFileVerification: true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single();
        Assert.IsTrue(group.Eligible, group.ReasonCode);
        Assert.IsTrue(group.Files.Count > 0, "This fixture must retain recorded private inputs after retiring its result.");
        var files = group.Files.Select(f => f.Path).ToArray();
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.IsTrue(result.CleanupResults!.Single().Completed,
            string.Join(",", result.CleanupResults.Single().ReasonCodes));
        Assert.AreEqual(files.Length, result.RemovedFiles);
        Assert.IsTrue(files.All(path => !File.Exists(path)));
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
        Assert.IsTrue(File.Exists(f.StateFile));
        Assert.AreEqual(ArtifactLocationState.Retired, (await store.GetArtifactAsync(f.Source.Id))!.State);
    }

    [DataTestMethod]
    [DataRow("unknown-file")]
    [DataRow("outside-reference")]
    public async Task Retired_private_input_cleanup_preserves_unknown_files_and_external_registered_readers(string protection)
    {
        var f = await EmptyRetiredWorkspaceFixture(keepPrivateInputs: true);
        var ready = await f.Service.PreviewAsync(new([f.Task.Id], true, true, SkipFileVerification: true));
        var files = ready.Rows.Single().Cleanup!.Groups.Single().Files.Select(f => f.Path).ToArray();
        Assert.IsTrue(files.Length > 0);
        if (protection == "unknown-file")
            await File.WriteAllTextAsync(Path.Combine(f.OperationRoot, "manual.key"), "keep unknown payload");
        else
        {
            var key = Guid.NewGuid().ToString("N");
            var outside = await store.AdoptAsync(new(key, key, "external", "input"),
                new(Path.GetDirectoryName(files[0])!, [files[0]], "externalFiles", false), "external-fixture");
            await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "outside", outside.OperationId);
        }
        var result = await f.Service.DeleteAsync(new(ready.PreviewId));
        Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsTrue(files.All(File.Exists));
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
        Assert.IsTrue(File.Exists(f.StateFile));
    }
}
