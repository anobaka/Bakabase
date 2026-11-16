using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private async Task<AcquisitionPlanTaskDetail> BindCurrentStage(AcquisitionPlanTaskDetail task, string stage, string path,
        bool taskAllocation = false)
    {
        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson!, WorkflowJson.Options)!;
        var parts = new Dictionary<string, AcquisitionPartDirectories>(plan.StageDirectories?.Parts ?? []);
        var part = parts.GetValueOrDefault("main") ?? new();
        var binding = taskAllocation
            ? new AcquisitionStageDirectory(path, "exclusive", "taskAllocation") {BindingId = Guid.NewGuid().ToString("N"),
                Allocation = new("acquisition", task.Id.ToString(), stage + "-main", _root, path)}
            : new AcquisitionStageDirectory(path, "borrowed", "userDirectoryRevision") {BindingId = Guid.NewGuid().ToString("N")};
        parts["main"] = stage switch {"download" => part with {Download = binding}, "delivery" => part with {Delivery = binding}, _ => part with {Processing = binding}};
        row.PlanJson = JsonSerializer.Serialize(plan with {StageDirectories = new() {TaskId = task.Id, Parts = parts}}, WorkflowJson.Options);
        await Db.SaveChangesAsync(); return (await Plans.GetAsync(task.Id, default))!;
    }

    private static AcquisitionPlanReplanInput StageRequest(AcquisitionPlanTaskDetail task, int runId, string stage) =>
        new(task.Task.PlanRevision, runId, "Use the selected stage's current files.",
            [new("main", new() {Requirement = "unknown"}, [], UseAdaptiveAssessment: true,
                InputMode: "stageDirectory", InputStage: stage)], EnableAdaptiveAssessment: true);

    [DataTestMethod]
    [DataRow("owned")]
    [DataRow("foreign-owner")]
    [DataRow("other-part")]
    [DataRow("concrete-file")]
    [DataRow("borrowed")]
    [DataRow("delivery")]
    public async Task Stage_capture_allows_only_its_completed_downloads_processing_allocation(string scenario)
    {
        var (task, run, download) = await CurrentDirectoryInput();
        var stage = scenario == "delivery" ? "delivery" : "processing";
        var directory = Directory.CreateDirectory(Path.Combine(_root, "current-" + stage)).FullName;
        var file = Path.Combine(directory, "input.txt");
        await File.WriteAllTextAsync(file, "existing task payload");
        task = await BindCurrentStage(task, stage, directory, taskAllocation: scenario != "borrowed");
        var transport = new CloudDownloadRequest("owned-transport", new() {Id = "share", ProviderId = "baidu",
            Url = "https://pan.baidu.com/s/test"}, "account", null, download.RootDirectory) {WorkspaceDirectory = directory};
        Db.DownloadTasks.Add(new() {Id = 888, Key = "completed-download", ThirdPartyId = ThirdPartyId.BaiduNetdisk,
            DownloadPath = download.RootDirectory, Status = DownloadTaskDbModelStatus.Complete,
            Options = JsonSerializer.Serialize(transport, WorkflowJson.Options)});
        Db.CloudDownloadOperations.Add(new() {Id = transport.OperationId, WorkflowRunId = run.Id, DownloadTaskId = 888,
            TargetId = scenario == "other-part" ? "other" : "main", Kind = "download", State = "completed"});
        if (scenario == "foreign-owner")
            Db.DownloadResultOwners.Add(new() {DownloadTaskId = 888, AcquisitionTaskId = task.Id + 1, WorkflowRunId = run.Id});
        if (scenario == "concrete-file")
            Db.DownloadTaskFiles.Add(new() {DownloadTaskId = 888, Path = file});
        await Db.SaveChangesAsync();

        var request = StageRequest(task, run.Id, stage);
        if (scenario == "owned")
        {
            var revised = await Plans.ReplanAsync(task.Id, request, default);
            var capture = (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
            CollectionAssert.AreEqual(new[] {file}, capture.Files.ToArray());
        }
        else
        {
            var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, request, default));
            Assert.AreEqual("STAGE_DIRECTORY_REFERENCED", failure.Message);
        }
        Assert.IsTrue(File.Exists(file));
        Assert.IsTrue(download.Files.All(File.Exists));
        Assert.AreEqual(1, await Db.DownloadTasks.CountAsync());
    }

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
