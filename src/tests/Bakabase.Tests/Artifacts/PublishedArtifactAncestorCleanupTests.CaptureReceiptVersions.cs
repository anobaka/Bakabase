using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [TestMethod]
    public async Task Valid_stage_capture_without_prior_input_does_not_crash_or_authorize_inherited_cleanup()
    {
        var f = await CapturedAddition();
        var directory = Directory.CreateDirectory(Path.Combine(root, "current-stage")).FullName;
        var file = Path.Combine(directory, "current.key");
        await File.WriteAllTextAsync(file, "manual stage file");
        var binding = new AcquisitionStageDirectory(directory, "borrowed", "userDirectoryRevision")
            {BindingId = Guid.NewGuid().ToString("N")};
        var plan = new AcquisitionPlan
        {
            Parts = [new() {Id = "part"}],
            StageDirectories = new() {TaskId = f.Task.Id, Parts = new()
                {["part"] = new() {Processing = binding}}}
        };
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.PlanJson, JsonSerializer.Serialize(plan, Json)));
        var capture = await AcquisitionCurrentDirectoryFiles.CaptureStageAsync(db, store, new Storage(),
            new FileProcessingWorkspaceResolver(root, new Storage()), f.Task.Id, 2, 1, "part", "processing",
            binding, () => Task.CompletedTask, default);
        Assert.IsTrue(await AcquisitionCurrentDirectoryFiles.IsOwnedAsync(db, store, capture, f.Task.Id, 1, "part", default));
        Assert.AreEqual(0, capture.ParentArtifactIds.Count);

        var candidates = await CapturedPreview(f);

        // The ordinary version 1 capture remains independently eligible. The valid stage
        // binding must neither fail the preview nor inherit that older cleanup authority.
        Assert.AreEqual(f.Capture.Id, candidates.Single().ArtifactId);
        Assert.IsTrue(candidates.Single().Group.Eligible);
        Assert.AreEqual("manual stage file", await File.ReadAllTextAsync(file));
        Assert.IsTrue(File.Exists(f.AddedFile));
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
    }

    [DataTestMethod]
    [DataRow("null-receipt")]
    [DataRow("missing-prior-input")]
    [DataRow("null-prior-input")]
    [DataRow("malformed-prior-input")]
    [DataRow("unknown-version")]
    public async Task Invalid_capture_receipt_never_grants_inherited_cleanup(string scenario)
    {
        var f = await CapturedAddition();
        var receipt = JsonNode.Parse(f.Capture.MetadataJson!)!.AsObject();
        switch (scenario)
        {
            case "missing-prior-input": receipt.Remove("priorInput"); break;
            case "null-prior-input": receipt["priorInput"] = null; break;
            case "malformed-prior-input": receipt["priorInput"] = "invalid"; break;
            case "unknown-version": receipt["version"] = 99; break;
        }
        var changed = scenario == "null-receipt" ? "null" : receipt.ToJsonString();
        await db.Artifacts.Where(a => a.Id == f.Capture.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.MetadataJson, changed));

        Assert.AreEqual(0, (await CapturedPreview(f)).Count);
        Assert.IsTrue(File.Exists(f.AddedFile));
        Assert.IsTrue(f.Source.Files.All(File.Exists));
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
        Assert.AreEqual(changed, (await store.GetArtifactAsync(f.Capture.Id))!.MetadataJson);
    }
}
