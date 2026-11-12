using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
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
    private async Task<(AcquisitionPlanTaskDetail Task, WorkflowRunDbModel Run, ArtifactRecord Input,
        ArtifactRecord Published, ArtifactRecord Download)> CompletedInput(bool frozenLayout = false, string? publicationWrapper = null)
    {
        var (task, run, download) = await Failed();
        var input = await ProcessedInput(run.Id, download);
        var row = await Db.AcquisitionTasks.FindAsync(task.Id);
        run.Status = WorkflowRunStatus.Success; run.ErrorMessage = null;
        row!.Status = AcquisitionStatus.Completed; row.Phase = "completed"; row.Error = null;
        var plan = task.Plan!;
        if (frozenLayout)
        {
            var settings = new AcquisitionDeliverySettings {RootDirectory = Path.Combine(_root, "frozen-delivery"),
                DirectoryTemplate = "custom/{Title} [A{TaskId}]"};
            var layout = new AcquisitionDeliveryDirectoryResolver(_services.GetRequiredService<Bakabase.Abstractions.Components.FileSystem.IUserStoragePolicy>()).Resolve(task.Id,
                plan.Title, plan.OutputDirectory, plan.Parts, settings);
            plan = plan with {DeliveryLayout = layout};
            row.PlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options);
            row.BuilderStateJson = JsonSerializer.Serialize(new AcquisitionPlanBuilderState
                {DeliverySettings = settings, DeliveryLayout = layout}, WorkflowJson.Options);
        }
        else
        {
            // Simulate an actual historical row, predating captured delivery settings.
            plan = plan with {DeliveryLayout = null};
            row.PlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options);
            row.BuilderStateJson = JsonSerializer.Serialize(new AcquisitionPlanBuilderState(), WorkflowJson.Options);
        }
        await Db.SaveChangesAsync();
        var target = AcquisitionPlanCompiler.Workset(task.Id, 1, plan, task.SourceRefs).Targets.Single();
        var oldDirectory = plan.DeliveryLayout?.PartDirectories["main"] ??
            PublishFileArtifactsActivity.CloudTargetDirectory(new() {OutputDirectory = _root, Targets = [target]}, target, new());
        Directory.CreateDirectory(oldDirectory);
        var oldFile = Path.Combine(oldDirectory, publicationWrapper ?? "", "inner.r");
        Directory.CreateDirectory(Path.GetDirectoryName(oldFile)!);
        await File.WriteAllTextAsync(oldFile, "already extracted first layer");
        var published = await Artifacts.AdoptAsync(new("old-publication", "old-publication", "files.publish", input.Fingerprint),
            new(oldDirectory, [oldFile], "publishedFiles", false, [input.Id]), "test");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "main:artifact.publish", published.OperationId);
        return ((await Plans.GetAsync(task.Id, default))!, run, input, published, download);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("wrongBinding")]
    [DataRow("wrongIntent")]
    [DataRow("differentArtifact")]
    [DataRow("changedFile")]
    public async Task Paused_republication_can_resume_or_reprocess_only_its_owned_borrowed_delivery(string? fault)
    {
        var (task, old, _, published, _) = await CompletedInput(true, "wrapper");
        var policy = new ArtifactDeliveryPolicy {CollapseSingleRootDirectory = true};
        var item = new AcquisitionPlanProcessingReplacement("main", new() {Requirement = "notRequired"},
            [published.Id], published.Id, FileArtifactDeliveryLayout.Preview(published, policy).ProjectionFingerprint);
        var revised = await Plans.ReplanAsync(task.Id, new(task.Task.PlanRevision, old.Id, "Re-deliver", [item],
            DeliveryContentLayout: policy), default);
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan!);
        var payload = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan!, revised.SourceRefs);
        var run = new WorkflowRunDbModel {StartedAt = DateTime.UtcNow, Status = WorkflowRunStatus.Running,
            PlanSnapshotJson = WorkflowRunPlans.Serialize(compiled), Sources = [new() {Module = "acquisition", EntityKind = "task",
                EntityId = task.Id.ToString(), Revision = revised.Task.PlanRevision.ToString()}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        var ctx = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = payload, ActivityConfigJson = compiled.Nodes[0].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        var continuation = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(ctx, payload, default);
        var workset = (DownloadWorkset)continuation.Replacement!;
        var target = workset.Targets.Single() with {PublicationWaitReason = "StorageUnavailable", Problem = "LOCAL_STORAGE_RESERVE_LOW"};
        if (fault == "wrongIntent") target = target with {ProcessingIntentKey = "unrelated"};
        if (fault == "differentArtifact") target = target with {ProcessedArtifactId = "unrelated"};
        if (fault == "wrongBinding") await Db.ArtifactOperationBindings.Where(b => b.OwnerId == run.Id.ToString() &&
            b.OperationId == published.OperationId).ExecuteDeleteAsync();
        if (fault == "changedFile") await File.AppendAllTextAsync(published.Files.Single(), "modified");
        run.Status = WorkflowRunStatus.Waiting; run.CurrentStepIndex = 1; run.WaitReason = "artifacts.publication";
        run.WaitPromptJson = JsonSerializer.Serialize(new {targetId = "main", reason = "StorageUnavailable", message = "LOCAL_STORAGE_RESERVE_LOW"});
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [target]});
        var row = await Db.AcquisitionTasks.FindAsync(task.Id);
        row!.WorkflowRunId = run.Id; row.Status = AcquisitionStatus.Waiting; row.Phase = "attention";
        await Db.SaveChangesAsync();
        var plans = CurrentStoragePlans(Directory.CreateDirectory(Path.Combine(_root, "current-work")).FullName,
            Directory.CreateDirectory(Path.Combine(_root, "new-destination")).FullName);
        var pending = (await plans.GetAsync(task.Id, default))!;
        var diagnostics = new AcquisitionRecoveryDiagnostics(Db, _services.GetRequiredService<Bakabase.Abstractions.Components.FileSystem.IUserStoragePolicy>(), Artifacts);
        var diagnosis = await diagnostics.DiagnoseAsync(row, run, default);
        Assert.AreEqual(fault == null, diagnosis.Row.CanContinue, diagnosis.Row.ReasonCode);
        if (fault != null)
        {
            if (fault != "changedFile") Assert.AreEqual(0, pending.ReplanArtifacts.Count);
            else await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                plans.ReplanAsync(task.Id, ContinuePublication(pending, run.Id, published.Id), default));
            return;
        }
        var candidate = pending.ReplanArtifacts.Single();
        Assert.AreEqual(published.Id, candidate.ArtifactId); Assert.IsTrue(candidate.CanRetryDelivery);
        var repair = ContinuePublication(pending, run.Id, published.Id);
        repair = repair with {Items = [repair.Items.Single() with {Plan = new() {Requirement = "required",
            Steps = [new() {Id = "rename", Op = "renameExtension", Extension = ".txt"}]}}]};
        var next = await plans.ReplanAsync(task.Id, repair, default);
        CollectionAssert.AreEqual(new[] {"acquisition.plan.continueFiles", "acquisition.plan.process", "fs.publishArtifacts"},
            AcquisitionPlanCompiler.Compile(next.Plan!).Nodes.Select(n => n.Kind).ToArray());
        Assert.AreEqual(published.Id, next.Plan!.Parts.Single().ContinueFrom!.ArtifactId);
        Assert.IsTrue(File.Exists(published.Files.Single()));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task OnlyTheSamePartsPublishedOutputIsEligibleForDeliveryOnlyRevision()
    {
        var (task, run, processed, published, _) = await CompletedInput(true);
        var directory = Directory.CreateDirectory(Path.Combine(_root, "borrowed-publication")).FullName;
        var file = Path.Combine(directory, "previous.txt");
        await File.WriteAllTextAsync(file, "preserved previous publication");
        var borrowed = await Artifacts.AdoptAsync(new("borrowed-publication", "borrowed-publication", "files.publish", processed.Fingerprint),
            new(directory, [file], "publishedFiles", false, [processed.Id]), "test");
        var bindings = new ArtifactOperationBindingService(Db);
        await bindings.BindAsync("workflow", run.Id.ToString(), "main:artifact.continuation", borrowed.OperationId);
        await bindings.BindAsync("workflow", run.Id.ToString(), "other:artifact.publish", borrowed.OperationId);

        var detail = (await Plans.GetAsync(task.Id, default))!;
        var forPart = detail.ReplanArtifacts.Where(candidate => candidate.PartId == "main").ToList();
        Assert.IsTrue(forPart.Any(candidate => candidate.ArtifactId == borrowed.Id), "Borrowed outputs remain usable for local processing.");
        CollectionAssert.AreEqual(new[] {published.Id}, forPart.Where(candidate => candidate.CanRepublish == true)
            .Select(candidate => candidate.ArtifactId).ToArray());
    }

    [TestMethod]
    public async Task LayoutRevisionPinsPreviewAndPublishesWithoutAProcessingSnapshot()
    {
        var (task, old, _, published, _) = await CompletedInput(true, "author");
        var policy = new ArtifactDeliveryPolicy {CollapseSingleRootDirectory = true};
        var preview = FileArtifactDeliveryLayout.Preview(published, policy);
        var item = new AcquisitionPlanProcessingReplacement("main", new() {Requirement = "notRequired"},
            [published.Id], published.Id, preview.ProjectionFingerprint);
        var request = new AcquisitionPlanReplanInput(task.Task.PlanRevision, old.Id, "Adjust delivery folders", [item],
            DeliveryContentLayout: policy);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, request with
            {Items = [item with {ExpectedDeliveryProjectionFingerprint = new string('a', 64)}]}, default));
        var revised = await Plans.ReplanAsync(task.Id, request, default);
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan!);
        CollectionAssert.AreEqual(new[] {"acquisition.plan.continueFiles", "fs.publishArtifacts"}, compiled.Nodes.Select(n => n.Kind).ToArray());
        Assert.IsNull(revised.Task.WorkflowRunId, "Saving a preview must not execute it.");
        var payload = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan!, revised.SourceRefs);
        var run = new WorkflowRunDbModel {StartedAt = DateTime.UtcNow, Status = WorkflowRunStatus.Running,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        WorkflowExecutionContext Context(int index) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = payload, ActivityConfigJson = compiled.Nodes[index].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        var continued = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), payload, default);
        var delivered = await new PublishFileArtifactsActivity().ProcessItemAsync(Context(1), continued.Replacement!, default);
        Assert.IsNull(delivered.Suspension, delivered.Suspension?.PromptJson);
        var result = ((DownloadWorkset)delivered.Replacement!).Targets.Single();
        Assert.AreEqual("inner.r", Path.GetRelativePath(result.PublishedDirectory!, result.PublishedFiles!.Single()));
        Assert.IsTrue(File.Exists(published.Files.Single()));
        Assert.AreNotEqual(published.Id, result.PublishedArtifactId);
        Assert.AreEqual(preview.OutputManifest.Fingerprint, (await Artifacts.GetArtifactAsync(result.PublishedArtifactId!))!.Fingerprint);
        Assert.AreEqual(published.Fingerprint, (await Artifacts.GetArtifactAsync(published.Id))!.Fingerprint);
        Assert.AreEqual(0, _continuationArchives.Extractions); Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Completed_revision_preserves_success_and_publication_and_runs_only_remaining_local_steps(bool frozenLayout)
    {
        var (task, old, input, published, _) = await CompletedInput(frozenLayout);
        Assert.IsTrue(task.ReplanArtifacts.Any(a => a.ArtifactId == input.Id));
        var oldSnapshot = old.PlanSnapshotJson; var oldOutput = old.OutputItemsJson;
        var request = ContinueInput(task, old.Id, input.Id);
        var revised = await Plans.ReplanAsync(task.Id, request, default);
        Assert.AreEqual(2, revised.Task.PlanRevision);
        Assert.AreEqual(AcquisitionStatus.Waiting, revised.Task.Status);
        Assert.IsNull(revised.Task.WorkflowRunId);
        Assert.IsNotNull(revised.Plan!.DeliveryLayout);
        Assert.AreEqual(new ArtifactDeliveryPolicy(), revised.Plan.DeliveryLayout.Settings.ContentLayout);
        var directory = revised.Plan.DeliveryLayout.TaskDirectory;
        StringAssert.EndsWith(directory, " [R2]");
        Assert.IsFalse(directory.StartsWith(published.RootDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.IsFalse(published.RootDirectory.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.AreNotEqual(published.RootDirectory, directory);
        Assert.IsFalse(Directory.Exists(directory), "Reviewing a revision must not create publication folders.");
        if (frozenLayout) Assert.AreEqual(task.Plan!.DeliveryLayout!.Settings with {ContentLayout = new()},
            revised.Plan.DeliveryLayout.Settings);
        Assert.AreEqual(JsonSerializer.Serialize(task.Plan!.Parts.Single().Sources), JsonSerializer.Serialize(revised.Plan.Parts.Single().Sources));
        Assert.AreEqual(old.Id, revised.PlanHistory.Last().PreviousRunId);
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan);
        CollectionAssert.AreEqual(new[] {AcquisitionPlanContinueFilesActivity.ActivityKind,
            AcquisitionPlanProcessingActivity.ActivityKind, AssessFileArtifactDeliveryActivity.ActivityKind, "fs.publishArtifacts"}, compiled.Nodes.Select(n => n.Kind).ToArray());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, request, default));
        var prepared = await Plans.PreflightAsync(task.Id, default);
        Assert.AreEqual(directory, prepared.Plan!.DeliveryLayout!.TaskDirectory, "Same revision retains its isolated destination.");
        var payload = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan, revised.SourceRefs);
        var run = new WorkflowRunDbModel {WorkflowDefinitionId = 0, Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        WorkflowExecutionContext Context(int node) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = payload, ActivityConfigJson = compiled.Nodes[node].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        var continued = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), payload, default);
        var processed = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(1), continued.Replacement!, default);
        Assert.IsNull(processed.Suspension, processed.Suspension?.PromptJson);
        var delivered = await new PublishFileArtifactsActivity().ProcessItemAsync(Context(compiled.Nodes.Count - 1), processed.Replacement!, default);
        Assert.IsNull(delivered.Suspension, delivered.Suspension?.PromptJson);
        var output = ((DownloadWorkset)delivered.Replacement!).Targets.Single();
        Assert.AreEqual(directory, output.PublishedDirectory);
        Assert.AreEqual("remaining extraction", await File.ReadAllTextAsync(output.PublishedFiles!.Single()));
        Assert.AreEqual("already extracted first layer", await File.ReadAllTextAsync(published.Files.Single()));
        Assert.AreEqual("already extracted first layer", await File.ReadAllTextAsync(input.Files.Single()));
        Assert.AreEqual(WorkflowRunStatus.Success, (await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == old.Id)).Status);
        Assert.AreEqual(oldSnapshot, old.PlanSnapshotJson); Assert.AreEqual(oldOutput, old.OutputItemsJson);
        Assert.AreEqual(1, _continuationArchives.Extractions); Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        // A further completed revision receives another distinct directory, while the
        // preceding success and both prior publications remain valid history.
        run.Status = WorkflowRunStatus.Success; run.CompletedAt = DateTime.UtcNow;
        var row = await Db.AcquisitionTasks.FindAsync(task.Id);
        row!.WorkflowRunId = run.Id; row.Status = AcquisitionStatus.Completed;
        await Db.SaveChangesAsync();
        var nextInput = new AcquisitionPlanReplanInput(2, run.Id, "A further explicit local correction.",
            [new("main", new() {Requirement = "required", Steps =
                [new() {Id = "rename", Op = "renameFile", Selector = "final.txt", TargetName = "renamed.txt"}]},
                [output.ProcessedArtifactId!], output.ProcessedArtifactId)]);
        var next = await Plans.ReplanAsync(task.Id, nextInput, default);
        Assert.AreNotEqual(directory, next.Plan!.DeliveryLayout!.TaskDirectory);
        StringAssert.EndsWith(next.Plan.DeliveryLayout.TaskDirectory, " [R3]");
        Assert.IsFalse(next.Plan.DeliveryLayout.TaskDirectory.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.AreEqual("remaining extraction", await File.ReadAllTextAsync(output.PublishedFiles.Single()));
    }

    [DataTestMethod]
    [DataRow("no-continuation")]
    [DataRow("unknown-with-steps")]
    [DataRow("empty-required")]
    [DataRow("all-keep")]
    [DataRow("download")]
    [DataRow("unbound-publication")]
    [DataRow("other-part")]
    [DataRow("other-task")]
    public async Task Completed_revision_rejects_implicit_acquisition_and_unreviewed_inputs(string fault)
    {
        var (task, run, input, published, download) = await CompletedInput();
        var request = ContinueInput(task, run.Id, input.Id); var item = request.Items[0];
        switch (fault)
        {
            case "no-continuation": item = item with {ContinueFromArtifactId = null}; break;
            case "unknown-with-steps": item = item with {Plan = item.Plan with {Requirement = "unknown"}}; break;
            case "empty-required": item = item with {Plan = new() {Requirement = "required"}}; break;
            case "all-keep": item = item with {Plan = new() {Requirement = "notRequired"}}; break;
            case "download": item = item with {ArtifactEvidenceIds = [download.Id], ContinueFromArtifactId = download.Id}; break;
            case "unbound-publication":
                await Db.Set<ArtifactOperationBindingDbModel>().Where(b => b.OperationId == published.OperationId).ExecuteDeleteAsync();
                item = item with {ArtifactEvidenceIds = [published.Id], ContinueFromArtifactId = published.Id}; break;
            case "other-part":
                await Db.Set<ArtifactOperationBindingDbModel>().Where(b => b.OperationId == input.OperationId).ExecuteDeleteAsync();
                await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "other:process:s1", input.OperationId); break;
            case "other-task": (await Db.Set<WorkflowRunSourceDbModel>().SingleAsync()).EntityId = (task.Id + 1).ToString(); await Db.SaveChangesAsync(); break;
        }
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Plans.ReplanAsync(task.Id, request with {Items = [item]}, default));
        var current = await Db.AcquisitionTasks.AsNoTracking().SingleAsync();
        Assert.AreEqual(AcquisitionStatus.Completed, current.Status); Assert.AreEqual(1, current.PlanRevision);
        Assert.AreEqual(WorkflowRunStatus.Success, run.Status); Assert.AreEqual(0, _continuationArchives.Extractions);
    }

    [DataTestMethod]
    [DataRow("missing")]
    [DataRow("modified")]
    [DataRow("collision")]
    [DataRow("run-running")]
    public async Task Completed_revision_fails_closed_before_mutation(string fault)
    {
        var (task, run, input, published, _) = await CompletedInput(true);
        switch (fault)
        {
            case "missing": File.Delete(input.Files.Single()); break;
            case "modified": await File.WriteAllTextAsync(input.Files.Single(), "changed bytes"); break;
            case "collision": Directory.CreateDirectory(task.Plan!.DeliveryLayout!.TaskDirectory + " [R2]"); break;
            case "run-running": run.Status = WorkflowRunStatus.Running; await Db.SaveChangesAsync(); break;
        }
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, ContinueInput(task, run.Id, input.Id), default));
        Assert.AreEqual(AcquisitionStatus.Completed, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
        Assert.AreEqual("already extracted first layer", await File.ReadAllTextAsync(published.Files.Single()));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Completed_multi_part_revision_requires_every_local_input_and_allows_explicit_keep_for_other_parts()
    {
        var (task, run, input, _, _) = await CompletedInput();
        var part = task.Plan!.Parts.Single();
        var second = part with {Id = "second", SelectedSourceId = "second-source",
            Sources = [part.Sources.Single() with {Id = "second-source"}]};
        var row = await Db.AcquisitionTasks.FindAsync(task.Id);
        row!.PlanJson = JsonSerializer.Serialize(task.Plan with {StageDirectories = null, Parts = [part, second]}, WorkflowJson.Options);
        await Db.SaveChangesAsync();
        var otherRoot = Directory.CreateDirectory(Path.Combine(_root, "other-processed")).FullName;
        var otherFile = Path.Combine(otherRoot, "retain.txt"); await File.WriteAllTextAsync(otherFile, "kept bytes");
        var other = await Artifacts.AdoptAsync(new("other-processed", "other-processed", "files.process", "input"),
            new(otherRoot, [otherFile], "processedFiles", true), "test");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "second:process:final", other.OperationId);
        task = (await Plans.GetAsync(task.Id, default))!;
        var request = ContinueInput(task, run.Id, input.Id);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Plans.ReplanAsync(task.Id, request, default));
        request = request with {Items = [request.Items[0], new("second", new() {Requirement = "notRequired"}, [other.Id], other.Id)]};
        var revised = await Plans.ReplanAsync(task.Id, request, default);
        Assert.IsTrue(revised.Plan!.Parts.All(p => p.ContinueFrom != null));
        Assert.AreEqual("notRequired", revised.Plan.Parts[1].Processing!.Requirement);
        CollectionAssert.AreEqual(new[] {AcquisitionPlanContinueFilesActivity.ActivityKind,
            AcquisitionPlanProcessingActivity.ActivityKind, AcquisitionPlanProcessingActivity.ActivityKind,
            AssessFileArtifactDeliveryActivity.ActivityKind, "fs.publishArtifacts"},
            AcquisitionPlanCompiler.Compile(revised.Plan).Nodes.Select(n => n.Kind).ToArray());
        Assert.AreEqual(2, revised.Plan.DeliveryLayout!.PartDirectories.Count);
        Assert.AreEqual(2, revised.Plan.DeliveryLayout.PartDirectories.Values.Distinct().Count());
        Assert.AreEqual("kept bytes", await File.ReadAllTextAsync(otherFile));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

}
