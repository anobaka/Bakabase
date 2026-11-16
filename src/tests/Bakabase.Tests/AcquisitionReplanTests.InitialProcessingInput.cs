using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
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
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private async Task<(AcquisitionPlanTaskDetail Task, WorkflowRunDbModel Run, DownloadWorkset Files,
        WorkflowExecutionContext Context, ArtifactRecord Source)> UninitializedProcessingInput(bool legacyDownload)
    {
        var (task, old, _) = await Failed();
        var download = Directory.CreateDirectory(Path.Combine(_root, "selected-download")).FullName;
        var file = Path.Combine(download, "package.bin"); await File.WriteAllTextAsync(file, "downloaded bytes");
        var source = await Artifacts.AdoptAsync(new("selected-download", "selected-download", "download", "input"),
            new(download, [file], "download", false), "test");
        await Db.ArtifactOperationBindings.Where(b => b.OwnerKind == "workflow" && b.OwnerId == old.Id.ToString()).ExecuteDeleteAsync();
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", old.Id.ToString(), "main:artifact.download", source.OperationId);
        var priorWorkset = AcquisitionPlanCompiler.Workset(task.Id, task.Task.PlanRevision, task.Plan!, task.SourceRefs);
        var priorTarget = priorWorkset.Targets.Single() with {DownloadArtifactId = source.Id, DownloadOperationId = source.OperationId,
            DownloadDirectory = download, Files = source.Files.ToList()};
        old.CurrentItemJson = WorkflowItemSnapshot.Capture(priorWorkset with {Targets = [priorTarget]});
        (await Db.ArtifactOperations.SingleAsync(o => o.Id == source.OperationId)).InputFingerprint = DownloadArtifactCoordinator.Source(priorTarget);
        task = await BindCurrentStage(task, "download", download);
        var processing = Path.Combine(_root, "visible-processing");
        task = await BindCurrentStage(task, "processing", processing);
        var plan = task.Plan!;
        var directories = plan.StageDirectories!.Parts["main"];
        var allocation = new ArtifactDirectoryScope("acquisition", task.Id.ToString(), "processing-main", _root, processing);
        await _services.GetRequiredService<FileProcessingWorkspaceResolver>().EnsureScopedDirectoryAsync(allocation, default);
        plan.StageDirectories.Parts["main"] = directories with
        {
            Processing = directories.Processing! with {Allocation = allocation, Ownership = "exclusive", Evidence = "taskAllocation"},
            Download = legacyDownload ? directories.Download! with {BindingId = null} : directories.Download
        };
        (await Db.AcquisitionTasks.FindAsync(task.Id))!.PlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options);
        await Db.SaveChangesAsync();
        task = (await Plans.GetAsync(task.Id, default))!;
        task = await Plans.ReplanAsync(task.Id, StageRequest(task, old.Id, "download"), default);
        var compiled = AcquisitionPlanCompiler.Compile(task.Plan!);
        var payload = AcquisitionPlanCompiler.Workset(task.Id, task.Task.PlanRevision, task.Plan!, task.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            PlanSnapshotJson = WorkflowRunPlans.Serialize(compiled), Sources = [new() {Module = "acquisition", EntityKind = "task",
                EntityId = task.Id.ToString(), Revision = task.Task.PlanRevision.ToString()}]};
        Db.Add(run); await Db.SaveChangesAsync();
        await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowRunId, run.Id)
            .SetProperty(t => t.Status, AcquisitionStatus.Running));
        WorkflowExecutionContext Context(int node) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = payload, ActivityConfigJson = compiled.Nodes[node].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        var continued = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), payload, default);
        return (task, run, (DownloadWorkset)continued.Replacement!, Context(1), source);
    }

    private async Task<(AcquisitionPlanTaskDetail Task, WorkflowRunDbModel Run, ArtifactRecord Input)> WaitBeforeInitialProcessing(bool legacyDownload)
    {
        var (task, run, files, _, _) = await UninitializedProcessingInput(legacyDownload);
        var selected = (await Artifacts.GetArtifactAsync(task.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
        // Model the former pass-through: AI can stop before the first file operation,
        // while the task already points at a processing directory which is still empty.
        var target = files.Targets.Single() with {ProcessedArtifactId = selected.Id, ProcessingOperationId = selected.OperationId,
            ProcessedDirectory = selected.RootDirectory, ProcessedFiles = selected.Files.ToList(), ProcessingWaitReason = "PasswordUnknown"};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(files with {Targets = [target]});
        run.CurrentStepIndex = WorkflowRunPlans.Read(run.PlanSnapshotJson)!.Nodes.Select((n, i) => (n, i))
            .Single(x => x.n.Kind == AssessFileArtifactDeliveryActivity.ActivityKind).i;
        run.Status = WorkflowRunStatus.Waiting; run.WaitReason = "cloud.fileProcessing";
        run.WaitPromptJson = JsonSerializer.Serialize(new {targetId = "main", reason = "PasswordUnknown", artifactId = selected.Id});
        await Db.SaveChangesAsync();
        await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AcquisitionStatus.Waiting));
        return ((await Plans.GetAsync(task.Id, default))!, run, selected);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Initial_processing_materializes_selected_download_before_AI_can_suspend(bool legacyDownload)
    {
        var (task, run, files, context, original) = await UninitializedProcessingInput(legacyDownload);
        var result = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(context, files, default);
        Assert.IsNull(result.Suspension, result.Suspension?.PromptJson);
        var target = ((DownloadWorkset)result.Replacement!).Targets.Single();
        var current = (await Artifacts.GetArtifactAsync(target.ProcessedArtifactId!))!;
        Assert.AreEqual(task.Plan!.StageDirectories!.Parts["main"].Processing!.Directory, current.RootDirectory);
        Assert.AreEqual("downloaded bytes", await File.ReadAllTextAsync(current.Files.Single()));
        Assert.AreEqual("downloaded bytes", await File.ReadAllTextAsync(original.Files.Single()));
        Assert.AreEqual(0, _continuationArchives.Extractions);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Initial_processing_retry_repairs_only_current_unprocessed_captured_download(bool legacyDownload)
    {
        var (task, run, selected) = await WaitBeforeInitialProcessing(legacyDownload);
        var originalHistory = (run.CurrentItemJson, run.PlanSnapshotJson, run.WaitPromptJson);
        var revised = await Plans.ReplanAsync(task.Id, StageRequest(task, run.Id, "processing"), default);
        var current = (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
        Assert.AreEqual(task.Plan!.StageDirectories!.Parts["main"].Processing!.Directory, current.RootDirectory);
        Assert.AreEqual("downloaded bytes", await File.ReadAllTextAsync(current.Files.Single()));
        Assert.IsTrue(File.Exists(selected.Files.Single()));
        Assert.AreEqual(0, _continuationArchives.Extractions); Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        var saved = await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.AreEqual(originalHistory, (saved.CurrentItemJson, saved.PlanSnapshotJson, saved.WaitPromptJson));
    }

    [DataTestMethod]
    [DataRow("processed")]
    [DataRow("completed-operation")]
    [DataRow("changed-binding")]
    public async Task Initial_processing_retry_does_not_restore_download_after_processing_or_rebinding(string fault)
    {
        var (task, run, selected) = await WaitBeforeInitialProcessing(false);
        if (fault == "processed")
        {
            var workset = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson)!;
            run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [workset.Targets.Single() with
                {ProcessingStepArtifacts = new() {{"extract", selected.Id}}}]});
        }
        if (fault == "completed-operation")
        {
            var operation = await Db.ArtifactOperations.SingleAsync(o => o.Id == selected.OperationId);
            operation.Kind = "files.process";
        }
        if (fault == "changed-binding")
            task = await BindCurrentStage(task, "processing", task.Plan!.StageDirectories!.Parts["main"].Processing!.Directory);
        await Db.SaveChangesAsync();
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id,
            StageRequest(task, run.Id, "processing"), default));
        Assert.AreEqual("PLAN_CURRENT_DIRECTORY_EMPTY", error.Message);
        Assert.IsTrue(File.Exists(selected.Files.Single())); Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Initial_processing_retry_respects_files_user_added_to_current_stage()
    {
        var (task, run, _) = await WaitBeforeInitialProcessing(false);
        var currentFile = Path.Combine(task.Plan!.StageDirectories!.Parts["main"].Processing!.Directory, "user.key");
        await File.WriteAllTextAsync(currentFile, "new current files");
        var revised = await Plans.ReplanAsync(task.Id, StageRequest(task, run.Id, "processing"), default);
        var current = (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
        CollectionAssert.AreEqual(new[] {currentFile}, current.Files.ToArray());
        Assert.IsFalse(File.Exists(Path.Combine(current.RootDirectory, "package.bin")));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Initial_processing_retry_requires_the_selected_download_to_still_exist_unchanged(bool removed)
    {
        var (task, run, selected) = await WaitBeforeInitialProcessing(false);
        if (removed) File.Delete(selected.Files.Single());
        else await File.WriteAllTextAsync(selected.Files.Single(), "replacement supplied outside the task");
        await Assert.ThrowsAsync<Exception>(() => Plans.ReplanAsync(task.Id,
            StageRequest(task, run.Id, "processing"), default));
        Assert.IsFalse(File.Exists(Path.Combine(task.Plan!.StageDirectories!.Parts["main"].Processing!.Directory, "package.bin")));
        if (!removed) Assert.AreEqual("replacement supplied outside the task", await File.ReadAllTextAsync(selected.Files.Single()));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync()); Assert.AreEqual(0, _continuationArchives.Extractions);
    }
}
