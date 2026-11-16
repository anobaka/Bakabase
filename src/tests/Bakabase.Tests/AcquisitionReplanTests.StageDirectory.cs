using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private async Task<AcquisitionPlanTaskDetail> BindCurrentStage(AcquisitionPlanTaskDetail task, string stage, string path)
    {
        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson!, WorkflowJson.Options)!;
        var parts = new Dictionary<string, AcquisitionPartDirectories>(plan.StageDirectories?.Parts ?? []);
        var part = parts.GetValueOrDefault("main") ?? new();
        var binding = new AcquisitionStageDirectory(path, "borrowed", "userDirectoryRevision") {BindingId = Guid.NewGuid().ToString("N")};
        parts["main"] = stage switch {"download" => part with {Download = binding}, "delivery" => part with {Delivery = binding}, _ => part with {Processing = binding}};
        row.PlanJson = JsonSerializer.Serialize(plan with {StageDirectories = new() {TaskId = task.Id, Parts = parts}}, WorkflowJson.Options);
        await Db.SaveChangesAsync(); return (await Plans.GetAsync(task.Id, default))!;
    }

    private static AcquisitionPlanReplanInput StageRequest(AcquisitionPlanTaskDetail task, int runId, string stage) =>
        new(task.Task.PlanRevision, runId, "Use the selected stage's current files.",
            [new("main", new() {Requirement = "unknown"}, [], UseAdaptiveAssessment: true,
                InputMode: "stageDirectory", InputStage: stage)], EnableAdaptiveAssessment: true);

    [DataTestMethod]
    [DataRow("download")]
    [DataRow("processing")]
    [DataRow("delivery")]
    public async Task Stage_directory_capture_uses_binding_current_contents_not_old_artifact(string stage)
    {
        var (task, run, old) = await CurrentDirectoryInput();
        var fresh = Directory.CreateDirectory(Path.Combine(_root, "explicit-" + stage)).FullName;
        var current = Path.Combine(fresh, "user-added.key"); await File.WriteAllTextAsync(current, "current content");
        task = await BindCurrentStage(task, stage, fresh);
        var previousManifest = (await Db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == old.Id)).ManifestJson;
        var revised = await Plans.ReplanAsync(task.Id, StageRequest(task, run.Id, stage), default);
        var input = revised.Plan!.Parts.Single().ContinueFrom!;
        var captured = (await Artifacts.GetArtifactAsync(input.ArtifactId))!;
        Assert.AreEqual(fresh, captured.RootDirectory);
        CollectionAssert.AreEqual(new[] {current}, captured.Files.ToArray());
        Assert.IsFalse(captured.Managed);
        Assert.IsTrue(await AcquisitionCurrentDirectoryFiles.IsOwnedAsync(Db, Artifacts, captured, task.Id, run.Id, "main", default));
        Assert.AreEqual(captured.Id, (await AcquisitionPlanArtifactInputGuard.RequireAsync(Db, Artifacts, task.Id, "main", input, default)).Id);
        Assert.AreEqual(previousManifest, (await Db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == old.Id)).ManifestJson);
        Assert.IsTrue(File.Exists(old.Files.Single()));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.IsFalse(AcquisitionPlanCompiler.Compile(revised.Plan).Nodes.Any(n => n.Kind.StartsWith("download.") || n.Kind == "baidu.transfer"));
    }

    [TestMethod]
    public async Task Empty_stage_binding_never_falls_back_to_present_old_files()
    {
        var (task, run, old) = await CurrentDirectoryInput();
        var empty = Directory.CreateDirectory(Path.Combine(_root, "empty-stage")).FullName;
        task = await BindCurrentStage(task, "processing", empty);
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Plans.ReplanAsync(task.Id, StageRequest(task, run.Id, "processing"), default));
        Assert.AreEqual("PLAN_CURRENT_DIRECTORY_EMPTY", error.Message);
        Assert.IsTrue(File.Exists(old.Files.Single()));
        Assert.AreEqual(task.Task.PlanRevision, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).PlanRevision);
    }

    [TestMethod]
    public async Task Stage_binding_survives_replan_and_compiler_uses_explicit_delivery()
    {
        var (task, run, _) = await CurrentDirectoryInput();
        var processing = Directory.CreateDirectory(Path.Combine(_root, "work-stage")).FullName;
        await File.WriteAllTextAsync(Path.Combine(processing, "ready.jpg"), "content");
        task = await BindCurrentStage(task, "processing", processing);
        var delivery = Directory.CreateDirectory(Path.Combine(_root, "final-stage")).FullName;
        task = await BindCurrentStage(task, "delivery", delivery);
        var revised = await Plans.ReplanAsync(task.Id, StageRequest(task, run.Id, "processing"), default);
        Assert.AreEqual(processing, revised.StageDirectories!.Parts["main"].Processing!.Directory);
        Assert.AreEqual(delivery, revised.StageDirectories.Parts["main"].Delivery!.Directory);
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan!);
        var node = compiled.Nodes.Single(n => n.Kind == "fs.publishArtifacts");
        Assert.AreEqual(delivery, JsonDocument.Parse(node.ConfigJson).RootElement.GetProperty("targetDirectories").GetProperty("main").GetString());
    }

    [TestMethod]
    public async Task Stage_directory_requires_valid_stage_and_no_client_artifact_override()
    {
        var (task, run, old) = await CurrentDirectoryInput();
        var request = StageRequest(task, run.Id, "processing");
        foreach (var item in new[] {request.Items.Single() with {InputStage = "arbitrary"},
                     request.Items.Single() with {ContinueFromArtifactId = old.Id, ArtifactEvidenceIds = [old.Id]}})
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => Plans.ReplanAsync(task.Id,
                request with {Items = [item]}, default));
    }
}
