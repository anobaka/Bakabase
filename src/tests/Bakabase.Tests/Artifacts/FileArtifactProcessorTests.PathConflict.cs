using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactProcessorTests
{
    [TestMethod]
    public async Task Path_conflict_waits_with_evidence_and_revised_plan_reuses_the_same_download_artifact()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "split-download")).FullName;
        var main = Path.Combine(directory, "collection.mp4");
        var volume = Path.Combine(directory, "collection.z01");
        await File.WriteAllTextAsync(main, "entry"); await File.WriteAllTextAsync(volume, "volume");
        var input = await _store.AdoptAsync(new("download", "download-intent", "download", "split-source"),
            new(directory, [main, volume], Managed: true), "download-owner");
        var broad = new FileProcessingPlan {Requirement = "required", Steps =
            [new() {Id = "rename", Op = "renameExtension", Extension = ".zip"}]};
        var plan = JsonSerializer.Serialize(broad, Json);
        var waiting = await Execute(Intent(plan), input, plan);
        Assert.IsNull(waiting.Artifact);
        Assert.AreEqual("ProcessingPathConflict", waiting.WaitReason);
        Assert.AreEqual("PlannedTargetCollision", waiting.PathConflict?.Kind);
        Assert.AreEqual("rename", waiting.PathConflict?.StepId);
        var operation = await _db.Set<ArtifactOperationDbModel>().AsNoTracking().SingleAsync(o => o.Kind == "files.process");
        Assert.AreNotEqual(ArtifactOperationState.Failed, operation.State);
        Assert.AreEqual(0, _archives.Extractions);
        var revised = JsonSerializer.Serialize(broad with {Steps = [broad.Steps[0] with {Selector = "collection.mp4"}]}, Json);
        var result = await Execute(Intent(revised), input, revised, "workflow:revised:target");
        Assert.IsNotNull(result.Artifact, result.Message);
        CollectionAssert.AreEqual(new[] {input.Id}, result.Artifact.ParentArtifactIds.ToArray());
        Assert.AreEqual(1, await _db.Set<ArtifactOperationDbModel>().CountAsync(o => o.Kind == "download"));
        Assert.AreEqual("entry", await File.ReadAllTextAsync(main));
        Assert.AreEqual("volume", await File.ReadAllTextAsync(volume));
        CollectionAssert.AreEquivalent(new[] {"collection.zip", "collection.z01"}, result.Artifact.Files.Select(Path.GetFileName).ToArray());
    }
}
