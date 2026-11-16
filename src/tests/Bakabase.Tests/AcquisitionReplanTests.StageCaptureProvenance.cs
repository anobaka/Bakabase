using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [TestMethod]
    public async Task Stage_capture_preserves_owned_generated_layout_across_repeated_current_file_views()
    {
        var (task, run, original) = await ProvenanceInput();
        task = await BindCurrentStage(task, "processing", original.RootDirectory);
        var originalMetadata = original.MetadataJson;
        var revised = await Plans.ReplanAsync(task.Id, StageRequest(task, run.Id, "processing"), default);
        var captured = (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
        Assert.AreEqual(2, JsonDocument.Parse(captured.MetadataJson!).RootElement.GetProperty("version").GetInt32());
        var projected = FileArtifactDeliveryLayout.Preview(captured, new() {RemoveGeneratedDirectories = true});
        CollectionAssert.AreEquivalent(new[] {"recorded-transfer", "recorded-transfer/recorded-extraction"}, projected.RemovedDirectories.ToArray());
        Assert.AreEqual("original-author/content.pdf", projected.OutputManifest.Entries.Single().RelativePath);
        var second = await AcquisitionCurrentDirectoryFiles.CaptureStageAsync(Db, Artifacts, new CompositeStorage(),
            _services.GetRequiredService<FileProcessingWorkspaceResolver>(), task.Id, revised.Task.PlanRevision, run.Id,
            "main", "processing", revised.Plan.StageDirectories!.Parts["main"].Processing!, () => Task.CompletedTask, default);
        Assert.AreEqual("original-author/content.pdf", FileArtifactDeliveryLayout.Preview(second,
            new() {RemoveGeneratedDirectories = true}).OutputManifest.Entries.Single().RelativePath);
        Assert.AreEqual(originalMetadata, (await Artifacts.GetArtifactAsync(original.Id))!.MetadataJson);
        Assert.IsTrue(File.Exists(original.Files.Single())); Assert.IsFalse(captured.Managed);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("added-file")]
    [DataRow("modified-file")]
    [DataRow("removed-binding")]
    [DataRow("other-task")]
    [DataRow("other-part")]
    [DataRow("retired")]
    [DataRow("different-root")]
    [DataRow("no-evidence")]
    public async Task Stage_capture_never_infers_layout_from_names_or_unrelated_history(string scenario)
    {
        var (task, run, original) = await ProvenanceInput();
        var directory = original.RootDirectory;
        if (scenario == "added-file")
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(original.Files.Single())!, "user.key"), "keep unknown key");
        if (scenario == "modified-file") await File.WriteAllTextAsync(original.Files.Single(), "user replaced this file");
        if (scenario == "removed-binding") await Db.ArtifactOperationBindings.Where(b => b.OperationId == original.OperationId).ExecuteDeleteAsync();
        if (scenario is "other-task" or "other-part")
            await new ArtifactOperationBindingService(Db).BindAsync("workflow", scenario == "other-task" ? "9999" : run.Id.ToString(),
                scenario == "other-part" ? "other:process:foreign" : "main:process:foreign", original.OperationId);
        if (scenario == "retired") await Db.Artifacts.Where(a => a.Id == original.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ArtifactLocationState.Retired));
        if (scenario == "no-evidence") await Db.Artifacts.Where(a => a.Id == original.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.MetadataJson, (string?)null));
        if (scenario == "different-root")
        {
            directory = Path.Combine(_root, "another-stage");
            var file = Path.Combine(directory, original.Manifest.Entries.Single().RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.Copy(original.Files.Single(), file);
        }
        task = await BindCurrentStage(task, "processing", directory);
        // The surrounding directory-reference guard is tested separately. This isolated
        // capture checks the stricter proof projection, even if the caller permits reading.
        var captured = await AcquisitionCurrentDirectoryFiles.CaptureStageAsync(Db, Artifacts, new CompositeStorage(),
            _services.GetRequiredService<FileProcessingWorkspaceResolver>(), task.Id, task.Task.PlanRevision + 1, run.Id,
            "main", "processing", task.Plan!.StageDirectories!.Parts["main"].Processing!, () => Task.CompletedTask, default);
        var preview = FileArtifactDeliveryLayout.Preview(captured, new() {RemoveGeneratedDirectories = true});
        Assert.IsFalse(preview.Changed, scenario);
        Assert.IsTrue(preview.OutputManifest.Entries.All(e => e.RelativePath.StartsWith("recorded-transfer/")));
        Assert.IsTrue(File.Exists(original.Files.Single()));
    }
}
