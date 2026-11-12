using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private async Task<(AcquisitionPlanTaskDetail Task, WorkflowRunDbModel Run, ArtifactRecord[] Inputs)>
        StoppedDownloads(int count)
    {
        var (task, run, first) = await DownloadedInput();
        var inputs = new List<ArtifactRecord> {first};
        if (count == 1) return (task, run, inputs.ToArray());
        var plan = task.Plan! with {DeliveryLayout = null, StageDirectories = null, Parts = task.Plan!.Parts.Append(task.Plan.Parts[0] with
            {Id = "second", Title = "Second independent part", SelectedSourceId = "second-source", Sources = [task.Plan.Parts[0].Sources[0] with
                {Id = "second-source", Url = "https://example.test/second"}]}).ToList()};
        var directory = Directory.CreateDirectory(Path.Combine(_root, "second-input")).FullName;
        var file = Path.Combine(directory, "original.bin"); await File.WriteAllTextAsync(file, "second original bytes");
        var workset = AcquisitionPlanCompiler.Workset(task.Id, task.Task.PlanRevision, plan, task.SourceRefs);
        var target = workset.Targets.Single(t => t.Id == "second");
        var second = await Artifacts.AdoptAsync(new("download-op-second",
                DownloadArtifactCoordinator.DownloadIntent(target, directory), "download", DownloadArtifactCoordinator.Source(target)),
            new(directory, [file], "download", true), "test");
        inputs.Add(second);
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "second:artifact.download", second.OperationId);
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = workset.Targets.Select((t, index) => t with
            {DownloadArtifactId = inputs[index].Id, DownloadOperationId = inputs[index].OperationId,
                Files = inputs[index].Files.ToList(), DownloadDirectory = inputs[index].RootDirectory}).ToList()});
        var row = await Db.AcquisitionTasks.FindAsync(task.Id);
        row!.PlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options);
        await Db.SaveChangesAsync();
        return ((await Plans.GetAsync(task.Id, default))!, run, inputs.ToArray());
    }

    private static AcquisitionPlanReplanInput ContinueAllDownloads(AcquisitionPlanTaskDetail task, int runId,
        IReadOnlyList<ArtifactRecord> inputs) => new(task.Task.PlanRevision, runId,
        "Use each verified existing input with current local placement; retain previous files and history.",
        task.Plan!.Parts.Select((part, index) => new AcquisitionPlanProcessingReplacement(part.Id,
            new() {Requirement = "notRequired", Steps = []}, [inputs[index].Id], inputs[index].Id)).ToList(),
        UseCurrentStorageDefaults: true);

    [DataTestMethod]
    [DataRow(1, false)]
    [DataRow(2, true)]
    public async Task Stopped_local_storage_revision_requires_all_inputs_and_preserves_prior_files_and_history(int parts, bool cancel)
    {
        var (task, run, inputs) = await StoppedDownloads(parts);
        var row = await Db.AcquisitionTasks.FindAsync(task.Id);
        if (cancel)
        {
            await _services.GetRequiredService<IAcquisitionService>().CancelAsync(task.Id);
            await Db.Entry(run).ReloadAsync(); await Db.Entry(row!).ReloadAsync();
            Assert.AreEqual(WorkflowRunStatus.Cancelled, run.Status);
            Assert.AreEqual(AcquisitionStatus.Cancelled, row!.Status);
        }
        // This is the historical shape that previously resolved storage back to the old download volume.
        var state = JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(row!.BuilderStateJson!, WorkflowJson.Options)!;
        row.BuilderStateJson = JsonSerializer.Serialize(state with {StoragePolicy = null}, WorkflowJson.Options);
        row.PlanJson = JsonSerializer.Serialize(task.Plan! with {StoragePolicy = null}, WorkflowJson.Options);
        await Db.SaveChangesAsync();
        async Task<string> RunRecord() => JsonSerializer.Serialize(await Db.WorkflowRuns.AsNoTracking().Where(r => r.Id == run.Id)
            .Select(r => new {r.Id, r.Status, r.StartedAt, r.CompletedAt, r.PlanSnapshotJson, r.CurrentItemJson,
                r.OutputItemsJson, r.CurrentStepIndex, r.ErrorMessage}).SingleAsync(), WorkflowJson.Options);
        var history = row.PlanHistoryJson; var oldRun = await RunRecord();
        var work = Directory.CreateDirectory(Path.Combine(_root, "current-work")).FullName;
        var delivery = Directory.CreateDirectory(Path.Combine(_root, "current-delivery")).FullName;
        var revised = await CurrentStoragePlans(work, delivery).ReplanAsync(task.Id,
            ContinueAllDownloads(task, run.Id, inputs), default);
        Assert.AreEqual(AcquisitionStatus.Waiting, revised.Task.Status); Assert.IsNull(revised.Task.WorkflowRunId);
        Assert.AreEqual(2, revised.Task.PlanRevision); Assert.AreEqual(parts, revised.Plan!.Parts.Count);
        Assert.AreEqual(work, revised.Plan.StoragePolicy!.ProcessingWorkspaceDirectory);
        Assert.AreEqual("keep", revised.Plan.StoragePolicy.SourceCleanup);
        Assert.AreEqual(delivery, revised.Plan.DeliveryLayout!.RootDirectory);
        StringAssert.EndsWith(revised.Plan.DeliveryLayout.TaskDirectory, " [R2]");
        Assert.IsFalse(Directory.Exists(revised.Plan.DeliveryLayout.TaskDirectory));
        foreach (var part in revised.Plan.Parts)
        {
            Assert.IsNotNull(part.ContinueFrom); Assert.AreEqual(task.Id, part.ContinueFrom.TaskId);
            Assert.AreEqual(run.Id, part.ContinueFrom.PreviousRunId); Assert.IsNull(part.StoragePolicy?.SourceCleanup);
        }
        var nodes = AcquisitionPlanCompiler.Compile(revised.Plan).Nodes;
        Assert.IsTrue(nodes.All(n => n.Kind is AcquisitionPlanContinueFilesActivity.ActivityKind or
            AcquisitionPlanProcessingActivity.ActivityKind or AssessFileArtifactDeliveryActivity.ActivityKind or "fs.publishArtifacts"));
        Assert.AreEqual(oldRun, await RunRecord());
        var previous = JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(history!, WorkflowJson.Options)!;
        Assert.AreEqual(JsonSerializer.Serialize(previous), JsonSerializer.Serialize(revised.PlanHistory.Take(previous.Count)));
        foreach (var input in inputs) Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(input.RootDirectory, input.Manifest, default)).Available);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("missing-part")]
    [DataRow("missing-continuation")]
    [DataRow("wrong-part")]
    [DataRow("other-task")]
    [DataRow("ambiguous-owner")]
    [DataRow("running")]
    [DataRow("waiting")]
    [DataRow("partial-receipt")]
    [DataRow("collision")]
    public async Task Current_storage_cannot_relocate_unfinished_network_work_or_unverified_local_inputs(string fault)
    {
        var (task, run, inputs) = await StoppedDownloads(2);
        var request = ContinueAllDownloads(task, run.Id, inputs);
        var work = Directory.CreateDirectory(Path.Combine(_root, "current-work")).FullName;
        var delivery = Directory.CreateDirectory(Path.Combine(_root, "current-delivery")).FullName;
        switch (fault)
        {
            case "missing-part": request = request with {Items = request.Items.Take(1).ToList()}; break;
            case "missing-continuation": request = request with {Items = request.Items.Select((i, index) =>
                index == 0 ? i : i with {ContinueFromArtifactId = null}).ToList()}; break;
            case "wrong-part": request = request with {Items = request.Items.Select((i, index) => index == 0 ? i :
                i with {ContinueFromArtifactId = inputs[0].Id, ArtifactEvidenceIds = [inputs[0].Id]}).ToList()}; break;
            case "other-task": (await Db.Set<WorkflowRunSourceDbModel>().SingleAsync()).EntityId = (task.Id + 1).ToString(); break;
            case "ambiguous-owner": Db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id,
                Module = "acquisition", EntityKind = "task", EntityId = (task.Id + 1).ToString()}); break;
            case "running": run.Status = WorkflowRunStatus.Running; break;
            case "waiting": run.Status = WorkflowRunStatus.Waiting; break;
            case "partial-receipt": (await Db.Set<ArtifactOperationDbModel>().SingleAsync(o => o.Id == inputs[1].OperationId)).OutputArtifactId = null; break;
            case "collision":
                var layout = new AcquisitionDeliveryDirectoryResolver(_services.GetRequiredService<Bakabase.Abstractions.Components.FileSystem.IUserStoragePolicy>())
                    .Resolve(task.Id, task.Plan!.Title, task.Plan.OutputDirectory, task.Plan.Parts, new() {RootDirectory = delivery});
                Directory.CreateDirectory(layout.TaskDirectory + " [R2]"); break;
        }
        await Db.SaveChangesAsync();
        var before = await Db.AcquisitionTasks.AsNoTracking().SingleAsync();
        Exception? failure = null;
        try {await CurrentStoragePlans(work, delivery).ReplanAsync(task.Id, request, default);}
        catch (Exception error) {failure = error;}
        Assert.IsTrue(failure is ArgumentException or InvalidOperationException, "Unsafe storage revision must be rejected: " + fault);
        var after = await Db.AcquisitionTasks.AsNoTracking().SingleAsync();
        Assert.AreEqual(before.PlanJson, after.PlanJson); Assert.AreEqual(before.PlanHistoryJson, after.PlanHistoryJson);
        Assert.AreEqual(before.BuilderStateJson, after.BuilderStateJson); Assert.AreEqual(1, after.PlanRevision);
        Assert.AreEqual(run.Id, after.WorkflowRunId); Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        foreach (var input in inputs) Assert.IsTrue(File.Exists(input.Files.Single()));
    }

    [TestMethod]
    public async Task Stopped_storage_revision_retains_saved_cleanup_preferences_and_ignores_payload_deletion_overrides()
    {
        var (task, run, inputs) = await StoppedDownloads(1);
        var row = await Db.AcquisitionTasks.FindAsync(task.Id);
        var policy = new ArtifactStoragePolicy {ProcessingWorkspaceDirectory = _root, SourceCleanup = "quarantine",
            CleanupIntermediates = false, Materialization = "copy"};
        var savedStep = task.Plan!.Parts.Single().Processing!.Steps.Single() with
            {StoragePolicy = new() {CleanupIntermediates = false, Materialization = "copy", SourceCleanup = "keep"}};
        var stored = task.Plan with {StoragePolicy = policy, Parts = [task.Plan.Parts.Single() with
            {StoragePolicy = new() {SourceCleanup = "quarantine"},
                Processing = new() {Requirement = "required", Steps = [savedStep]}}]};
        row!.PlanJson = JsonSerializer.Serialize(stored, WorkflowJson.Options);
        var state = JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(row.BuilderStateJson!, WorkflowJson.Options)!;
        row.BuilderStateJson = JsonSerializer.Serialize(state with {StoragePolicy = policy}, WorkflowJson.Options);
        await Db.SaveChangesAsync();
        var request = ContinueDownload(task, run.Id, inputs[0].Id) with {UseCurrentStorageDefaults = true};
        request = request with {Items = [request.Items.Single() with {Plan = new() {Requirement = "required", Steps =
            [savedStep with {StoragePolicy = new() {CleanupIntermediates = true, Materialization = "auto", SourceCleanup = "delete",
                ProcessingWorkspaceDirectory = Path.Combine(_root, "payload-work")}}]}}]};
        var work = Directory.CreateDirectory(Path.Combine(_root, "current-work")).FullName;
        var revised = await CurrentStoragePlans(work, Directory.CreateDirectory(Path.Combine(_root, "current-delivery")).FullName)
            .ReplanAsync(task.Id, request, default);
        Assert.AreEqual(work, revised.Plan!.StoragePolicy!.ProcessingWorkspaceDirectory);
        Assert.AreEqual("quarantine", revised.Plan.StoragePolicy.SourceCleanup);
        Assert.IsFalse(revised.Plan.StoragePolicy.CleanupIntermediates); Assert.AreEqual("copy", revised.Plan.StoragePolicy.Materialization);
        var part = revised.Plan.Parts.Single(); var step = part.Processing!.Steps.Single();
        Assert.AreEqual("quarantine", part.StoragePolicy!.SourceCleanup); Assert.AreEqual("keep", step.StoragePolicy!.SourceCleanup);
        Assert.AreEqual(false, step.StoragePolicy.CleanupIntermediates); Assert.AreEqual("copy", step.StoragePolicy.Materialization);
        Assert.IsNull(step.StoragePolicy.ProcessingWorkspaceDirectory);
        Assert.IsTrue(File.Exists(inputs[0].Files.Single()));
    }

    [TestMethod]
    public async Task Stopped_part_publication_is_offered_as_existing_local_input_and_preserved()
    {
        var (task, run, _, published, _) = await CompletedInput(true);
        var row = await Db.AcquisitionTasks.FindAsync(task.Id);
        row!.Status = AcquisitionStatus.Failed; run.Status = WorkflowRunStatus.Failed;
        await Db.SaveChangesAsync();
        var detail = (await Plans.GetAsync(task.Id, default))!;
        Assert.IsTrue(detail.ReplanArtifacts.Any(a => a.ArtifactId == published.Id && a.Kind == "publishedFiles"));
        var plans = CurrentStoragePlans(Directory.CreateDirectory(Path.Combine(_root, "new-work")).FullName,
            Directory.CreateDirectory(Path.Combine(_root, "new-delivery")).FullName);
        var next = await plans.ReplanAsync(task.Id, ContinueAllDownloads(detail, run.Id, [published]), default);
        Assert.AreEqual(published.Id, next.Plan!.Parts.Single().ContinueFrom!.ArtifactId);
        Assert.IsTrue(File.Exists(published.Files.Single()));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }
}
