using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private async Task<(AcquisitionPlanTaskDetail Task, WorkflowRunDbModel Run, ArtifactRecord Artifact)> DownloadedInput()
    {
        var (task, run, artifact) = await Failed();
        var workset = AcquisitionPlanCompiler.Workset(task.Id, 1, task.Plan!, task.SourceRefs);
        var target = workset.Targets.Single() with {DownloadArtifactId = artifact.Id,
            DownloadOperationId = artifact.OperationId, Files = artifact.Files.ToList(), DownloadDirectory = artifact.RootDirectory};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [target]});
        // The generic completed artifact operation is the durable download receipt. No
        // provider queue/result rows exist: their later deletion must not strand valid files.
        var operation = await Db.Set<ArtifactOperationDbModel>().SingleAsync(o => o.Id == artifact.OperationId);
        operation.InputFingerprint = DownloadArtifactCoordinator.Source(target);
        await Db.SaveChangesAsync();
        return (task, run, artifact);
    }

    private static AcquisitionPlanReplanInput ContinueDownload(AcquisitionPlanTaskDetail task, int runId, string artifactId) =>
        new(task.Task.PlanRevision, runId, "Continue only the completed, registered download; retain its original source and files.",
            [new("main", new() {Requirement = "required", Steps =
                [new() {Id = "extract", Op = "extractArchive", Selector = "original.bin"}]}, [artifactId], artifactId)]);

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Completed_download_continues_through_processing_and_publication_without_downloader_or_replacement(
        bool useCurrentStorage, bool keepOriginal)
    {
        var (task, old, input) = await DownloadedInput();
        _continuationArchives.ExpectedInput = "original bytes";
        var plans = useCurrentStorage ? CurrentStoragePlans(Directory.CreateDirectory(Path.Combine(_root, "new-work")).FullName,
            Directory.CreateDirectory(Path.Combine(_root, "new-delivery")).FullName) : Plans;
        var request = ContinueDownload(task, old.Id, input.Id) with {UseCurrentStorageDefaults = useCurrentStorage};
        if (keepOriginal) request = request with {Items = [request.Items[0] with
            {Plan = new() {Requirement = "notRequired"}, UseAdaptiveAssessment = false}]};
        var revised = await plans.ReplanAsync(task.Id, request, default);
        var preflight = await plans.PreflightAsync(task.Id, default);
        Assert.IsFalse(preflight.Issues.Any(i => i.Blocking));
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan!);
        string[] nodes = keepOriginal
            ? [AcquisitionPlanContinueFilesActivity.ActivityKind, AcquisitionPlanProcessingActivity.ActivityKind, "fs.publishArtifacts"]
            : [AcquisitionPlanContinueFilesActivity.ActivityKind, AcquisitionPlanProcessingActivity.ActivityKind,
                AssessFileArtifactDeliveryActivity.ActivityKind, "fs.publishArtifacts"];
        CollectionAssert.AreEqual(nodes, compiled.Nodes.Select(n => n.Kind).ToArray());
        var workset = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan!, revised.SourceRefs);
        var run = new WorkflowRunDbModel {WorkflowDefinitionId = 0, Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = revised.Task.PlanRevision.ToString()}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowRunId, run.Id));
        WorkflowExecutionContext Context(int node) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = compiled.Nodes[node].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        var restored = (DownloadWorkset)(await new AcquisitionPlanContinueFilesActivity()
            .ProcessItemAsync(Context(0), workset, default)).Replacement!;
        Assert.IsNull(restored.Targets.Single().DownloadTaskId, "Continuation must not reattach a downloader.");
        var processed = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(1), restored, default);
        Assert.IsNull(processed.Suspension);
        var assessed = keepOriginal ? processed : await new AssessFileArtifactDeliveryActivity().ProcessItemAsync(Context(2), processed.Replacement!, default);
        Assert.IsNull(assessed.Suspension, assessed.Suspension?.PromptJson);
        var published = await new PublishFileArtifactsActivity().ProcessItemAsync(Context(nodes.Length - 1), assessed.Replacement!, default);
        Assert.IsNull(published.Suspension, published.Suspension?.PromptJson);
        var target = ((DownloadWorkset)published.Replacement!).Targets.Single();
        Assert.IsNotNull(target.PublishedArtifactId);
        Assert.AreEqual(keepOriginal ? "original bytes" : "remaining extraction", await File.ReadAllTextAsync(target.PublishedFiles!.Single()));
        Assert.AreEqual("original bytes", await File.ReadAllTextAsync(input.Files.Single()));
        Assert.AreEqual(keepOriginal ? 0 : 1, _continuationArchives.Extractions);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual(0, await Db.DownloadResults.CountAsync());
        Assert.AreEqual(input.Id, (await Artifacts.GetOperationAsync(input.OperationId))!.OutputArtifactId);
        Assert.AreEqual(WorkflowRunStatus.Failed, (await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == old.Id)).Status);
        if (keepOriginal)
        {
            // Publication borrowed the staged result. Its continuation pin remains valid
            // until the runner records the successful terminal state.
            Assert.IsTrue(await Artifacts.HasActiveConsumersAsync(input.OperationId));
            await Db.WorkflowRuns.Where(r => r.Id == run.Id).ExecuteUpdateAsync(s =>
                s.SetProperty(r => r.Status, WorkflowRunStatus.Success).SetProperty(r => r.CompletedAt, DateTime.UtcNow));
        }
        Assert.IsFalse(await Artifacts.HasActiveConsumersAsync(input.OperationId));
    }

    [DataTestMethod]
    [DataRow("other-part")]
    [DataRow("other-task")]
    [DataRow("other-source")]
    [DataRow("missing-checkpoint")]
    [DataRow("different-files")]
    public async Task Download_continuation_requires_exact_task_part_and_registered_source(string fault)
    {
        var (task, run, input) = await DownloadedInput();
        switch (fault)
        {
            case "other-part":
                await Db.Set<ArtifactOperationBindingDbModel>().ExecuteDeleteAsync();
                await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "other:artifact.download", input.OperationId);
                break;
            case "other-task":
                (await Db.Set<WorkflowRunSourceDbModel>().SingleAsync()).EntityId = (task.Id + 1).ToString();
                break;
            case "other-source":
                (await Db.Set<ArtifactOperationDbModel>().SingleAsync(o => o.Id == input.OperationId)).InputFingerprint = "another source";
                break;
            case "missing-checkpoint": run.CurrentItemJson = null; break;
            case "different-files":
                var workset = (DownloadWorkset)WorkflowItemSnapshot.Restore(run.CurrentItemJson!);
                run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [workset.Targets[0] with {Files = []}]});
                break;
        }
        await Db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Plans.ReplanAsync(task.Id, ContinueDownload(task, run.Id, input.Id), default));
        Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
        Assert.AreEqual(0, _continuationArchives.Extractions);
    }

    [DataTestMethod]
    [DataRow("unfinished")]
    [DataRow("missing-receipt")]
    [DataRow("replacement")]
    [DataRow("missing-file")]
    [DataRow("changed-file")]
    public async Task Download_continuation_rejects_incomplete_or_replaced_registration_and_unverified_files(string fault)
    {
        var (task, run, input) = await DownloadedInput();
        var operation = await Db.Set<ArtifactOperationDbModel>().SingleAsync(o => o.Id == input.OperationId);
        switch (fault)
        {
            case "unfinished": operation.State = ArtifactOperationState.Running; break;
            case "missing-receipt": operation.OutputArtifactId = null; break;
            case "replacement": operation.OutputArtifactId = "a different registered generation"; break;
            case "missing-file": File.Delete(input.Files.Single()); break;
            case "changed-file": await File.WriteAllTextAsync(input.Files.Single(), "different bytes"); break;
        }
        await Db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, ContinueDownload(task, run.Id, input.Id), default));
        Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
        Assert.AreEqual(1, await Db.WorkflowRuns.CountAsync());
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Download_continuation_rejects_missing_registered_manifest()
    {
        var (task, run, input) = await DownloadedInput();
        var stored = await Db.Set<ArtifactDbModel>().SingleAsync(a => a.Id == input.Id);
        stored.ManifestJson = JsonSerializer.Serialize(new ArtifactManifest([], input.Fingerprint));
        await Db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Plans.ReplanAsync(task.Id, ContinueDownload(task, run.Id, input.Id), default));
        Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
    }

    [TestMethod]
    public async Task Download_continuation_revalidates_bytes_at_execution_and_releases_its_pin()
    {
        var (task, old, input) = await DownloadedInput();
        var revised = await Plans.ReplanAsync(task.Id, ContinueDownload(task, old.Id, input.Id), default);
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan!);
        var run = new WorkflowRunDbModel {WorkflowDefinitionId = 0, Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        await File.WriteAllTextAsync(input.Files.Single(), "changed after replan");
        var workset = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan!, revised.SourceRefs);
        var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = compiled.Nodes[0].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new AcquisitionPlanContinueFilesActivity()
            .ProcessItemAsync(context, workset, default));
        Assert.IsFalse(await Artifacts.HasActiveConsumersAsync(input.OperationId));
        Assert.AreEqual(0, _continuationArchives.Extractions);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Validated_download_continuation_remains_available_after_a_later_processing_run_stops()
    {
        var (task, old, input) = await DownloadedInput();
        var revised = await Plans.ReplanAsync(task.Id, ContinueDownload(task, old.Id, input.Id), default);
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan!);
        var workset = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan!, revised.SourceRefs);
        var run = new WorkflowRunDbModel {WorkflowDefinitionId = 0, Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = compiled.Nodes[0].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        var restored = (DownloadWorkset)(await new AcquisitionPlanContinueFilesActivity()
            .ProcessItemAsync(context, workset, default)).Replacement!;
        Assert.IsNull(restored.Targets.Single().DownloadArtifactId);
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(restored);
        run.Status = WorkflowRunStatus.Failed; run.CompletedAt = DateTime.UtcNow;
        var row = await Db.AcquisitionTasks.FindAsync(task.Id);
        row!.WorkflowRunId = run.Id; row.Status = Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants.AcquisitionStatus.Failed;
        await Db.SaveChangesAsync();
        var current = (await Plans.GetAsync(task.Id, default))!;
        var continued = await Plans.ReplanAsync(task.Id, ContinueDownload(current, run.Id, input.Id), default);
        Assert.AreEqual(3, continued.Task.PlanRevision);
        Assert.AreEqual(run.Id, continued.Plan!.Parts.Single().ContinueFrom!.PreviousRunId);
        Assert.AreEqual(input.Id, continued.Plan.Parts.Single().ContinueFrom!.ArtifactId);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual("original bytes", await File.ReadAllTextAsync(input.Files.Single()));
    }
}
