using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private async Task<AcquisitionPlanTaskDetail> LegacyManualSource(string name, bool twoGroups = false, bool unknown = false,
        bool explicitlyKeepFiles = false, FileProcessingPlan? processing = null)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "incoming")).FullName;
        AcquisitionPlanPart Part(string id) => new()
        {
            Id = id, SelectedSourceId = "source-" + id,
            Sources = [new() {Id = "source-" + id, Url = "https://example.test/unsupported/" + id, ProviderId = "unknown"}],
            Processing = processing ?? new() {Requirement = unknown ? "unknown" : "notRequired"},
            UseAdaptiveAssessment = explicitlyKeepFiles ? false : null
        };
        var plan = new AcquisitionPlan {Title = name, OutputDirectory = directory, Parts = twoGroups ? [Part("a"), Part("b")] : [Part("a")]};
        var task = await Plans.CreatePreparedPlanAsync(plan, [new("test", "post", name, "1", "0")]);
        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        row.PlanJson = JsonSerializer.Serialize(task.Plan! with {StageDirectories = null}, WorkflowJson.Options);
        row.Phase = "attention";
        var state = JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(row.BuilderStateJson!, WorkflowJson.Options)!;
        row.BuilderStateJson = JsonSerializer.Serialize(state with {Issues = [new("SOURCE_UNSUPPORTED", "No downloader")]},
            WorkflowJson.Options);
        await Db.SaveChangesAsync();
        return (await Plans.GetAsync(task.Id, default))!;
    }

    [TestMethod]
    public async Task Local_download_reception_preserves_the_original_plan_and_publishes_on_normal_run()
    {
        var task = await LegacyManualSource("Manual transfer");
        Assert.IsTrue(task.CanReceiveLocalDownloads);
        Assert.IsNull(task.Plan!.StageDirectories);
        Assert.IsNull(task.StageDirectories!.Parts["a"].Download);
        var initial = await Plans.PreviewLocalDownloadsAsync(task.Id, new(task.Task.PlanRevision), null, default);
        Assert.IsTrue(initial.CanPrepare);
        Assert.IsFalse(initial.CanApply);
        Assert.IsFalse(Directory.Exists(initial.Parts.Single().Directory), "The first preview must not create folders.");

        var prepared = await Plans.PrepareLocalDownloadsAsync(task.Id, new(initial.Revision), null, default);
        Assert.IsFalse(prepared.CanPrepare);
        Assert.AreEqual("LOCAL_DOWNLOAD_FILES_MISSING", prepared.ReasonCode);
        Assert.AreEqual(prepared.Revision,
            (await Plans.PrepareLocalDownloadsAsync(task.Id, new(initial.Revision), null, default)).Revision);
        var folder = prepared.Parts.Single().Directory!;
        var input = Path.Combine(folder, "file.txt");
        await File.WriteAllTextAsync(input, "bytes from a manual download");
        var approved = await Plans.PreviewLocalDownloadsAsync(task.Id, new(prepared.Revision), null, default);
        Assert.IsTrue(approved.CanApply);
        Assert.AreEqual("file.txt", approved.Parts.Single().Files.Single().RelativePath);
        Assert.AreEqual(new FileInfo(input).Length, approved.Parts.Single().Bytes);

        var request = new AcquisitionLocalDownloadsApplyInput(approved.Revision, approved.Token!);
        var received = await Plans.ApplyLocalDownloadsAsync(task.Id, request, null, default);
        var part = received.Plan!.Parts.Single();
        Assert.AreEqual(task.Plan.Parts.Single().Sources.Single(), part.Sources.Single());
        Assert.AreEqual(task.Plan.Parts.Single().Processing!.Requirement, part.Processing!.Requirement);
        Assert.AreEqual(task.Plan.Parts.Single().SelectedSourceId, part.SelectedSourceId);
        Assert.AreEqual("ready", received.Task.Phase);
        Assert.AreEqual("download.receiveLocalFiles", received.PlanHistory.Last().Reason);
        Assert.IsNull(received.Task.WorkflowRunId);
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
        Assert.IsTrue(part.ContinueFrom!.ManualDownload);
        Assert.AreEqual(0, part.ContinueFrom.PreviousRunId);
        Assert.AreEqual(received.Task.PlanRevision, (await Plans.ApplyLocalDownloadsAsync(task.Id, request, null, default)).Task.PlanRevision);
        Assert.AreEqual(1, await Db.Artifacts.CountAsync());
        Assert.IsFalse((await Artifacts.GetArtifactAsync(part.ContinueFrom.ArtifactId))!.Managed);

        var (_, output) = await PublishReceivedFilesAsync(received);
        Assert.AreEqual(await File.ReadAllTextAsync(input), await File.ReadAllTextAsync(output.PublishedFiles.Single()));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    private async Task<(int RunId, CloudDownloadTarget Output)> PublishReceivedFilesAsync(AcquisitionPlanTaskDetail received)
    {
        WorkflowRunPlan? snapshot = null;
        DownloadWorkset? workset = null;
        var runner = new ReserveRunHost(async (plan, payload, refs, ct) =>
        {
            snapshot = plan;
            workset = (DownloadWorkset)payload;
            var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
                Sources = refs!.Select(r => new WorkflowRunSourceDbModel {Module = r.Module, EntityKind = r.EntityKind,
                    EntityId = r.EntityId, Revision = r.Revision ?? "", ResultKey = r.ResultKey ?? ""}).ToList()};
            Db.WorkflowRuns.Add(run);
            await Db.SaveChangesAsync(ct);
            return new WorkflowRun {Id = run.Id, Status = run.Status};
        });
        var plans = new AcquisitionPlanService(Db, [], _services.GetRequiredService<IAcquisitionService>(), runner,
            _services.GetRequiredService<DownloadIntentService>(), _services.GetRequiredService<IUserStoragePolicy>(),
            artifacts: Artifacts, workspaces: _services.GetRequiredService<FileProcessingWorkspaceResolver>());
        var started = await plans.RunAsync(received.Id, received.Task.PlanRevision, default);
        Assert.IsNotNull(started.Task.WorkflowRunId);
        Assert.IsNotNull(snapshot);
        Assert.IsNotNull(workset);
        Assert.IsTrue(snapshot.Nodes.All(n => !n.Kind.StartsWith("download.") && n.Kind != "baidu.transfer"));
        WorkflowExecutionContext Context(int i) => new()
        {
            RunId = started.Task.WorkflowRunId!.Value, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger,
            Payload = workset, ActivityConfigJson = snapshot.Nodes[i].ConfigJson, Services = _services, Logger = NullLogger.Instance
        };
        var attached = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), workset, default);
        var processor = _services.GetServices<IWorkflowActivity>().OfType<AcquisitionPlanProcessingActivity>().Single();
        var processed = await processor.ProcessItemAsync(Context(1), attached.Replacement!, default);
        Assert.IsNull(processed.Suspension, processed.Suspension?.PromptJson);
        var delivered = await new PublishFileArtifactsActivity().ProcessItemAsync(Context(2), processed.Replacement!, default);
        Assert.IsNull(delivered.Suspension, delivered.Suspension?.PromptJson);
        var output = ((DownloadWorkset)delivered.Replacement!).Targets.Single();
        Assert.IsNotNull(output.PublishedArtifactId);
        Assert.IsNull(started.CanReceiveLocalDownloads);
        return (started.Task.WorkflowRunId.Value, output);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Local_download_reclamation_only_removes_the_verified_input_of_a_completed_delivery(bool automaticRemoval)
    {
        var task = await LegacyManualSource("Received download to reclaim", explicitlyKeepFiles: true);
        task = await Plans.UpdateAsync(task.Id, new(task.Task.PlanRevision,
            StoragePolicy: new() {SourceCleanup = automaticRemoval ? "delete" : "keep"}), default);
        var prepared = await Plans.PrepareLocalDownloadsAsync(task.Id, new(task.Task.PlanRevision), null, default);
        var folder = prepared.Parts.Single().Directory!;
        var input = Path.Combine(folder, "received.txt");
        const string payload = "verified received file";
        await File.WriteAllTextAsync(input, payload);
        var size = new FileInfo(input).Length;
        var intake = await Plans.PreviewLocalDownloadsAsync(task.Id, new(prepared.Revision), null, default);
        Assert.IsTrue(intake.CanApply, intake.ReasonCode);
        var received = await Plans.ApplyLocalDownloadsAsync(task.Id, new(intake.Revision, intake.Token!), null, default);
        var originalId = received.Plan!.Parts.Single().ContinueFrom!.ArtifactId;
        var original = (await Artifacts.GetArtifactAsync(originalId))!;
        Assert.IsFalse(original.Managed);
        var (runId, output) = await PublishReceivedFilesAsync(received);
        var publication = (await Artifacts.GetArtifactAsync(output.PublishedArtifactId!))!;
        CollectionAssert.AreEqual(new[] {originalId}, publication.ParentArtifactIds.ToArray());
        var deliveredFile = publication.Files.Single();
        Assert.AreNotEqual(input, deliveredFile);
        await Db.WorkflowRuns.Where(run => run.Id == runId).ExecuteUpdateAsync(update => update
            .SetProperty(run => run.Status, WorkflowRunStatus.Success));
        await Db.AcquisitionTasks.Where(row => row.Id == task.Id).ExecuteUpdateAsync(update => update
            .SetProperty(row => row.Status, AcquisitionStatus.Completed).SetProperty(row => row.Phase, "completed"));
        var completed = await Db.AcquisitionTasks.AsNoTracking().SingleAsync(row => row.Id == task.Id);
        var unrelated = Path.Combine(folder, "user-added.txt");
        await File.WriteAllTextAsync(unrelated, "independent user file");

        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var maintenance = _services.GetRequiredService<ArtifactWorkspaceMaintenance>();
        var storage = _services.GetRequiredService<IUserStoragePolicy>();
        var ancestors = new PublishedArtifactAncestorCleanup(Db, Artifacts,
            new ArchiveQuarantineService(Artifacts, Path.Combine(_root, "appdata"), storage),
            workspaces: workspaces, maintenance: maintenance);
        var previews = new AcquisitionStoragePolicyPreviewStore();
        Task<AcquisitionWorkspaceCleanupPreview> Preview(bool explicitCleanup, bool preserveOriginal = false) =>
            Plans.PreviewWorkspaceCleanupAsync(new([task.Id], OverrideRetention: explicitCleanup,
                PreserveOriginalDownloads: preserveOriginal), previews, maintenance, _ => Task.CompletedTask, default, ancestors);
        Task<AcquisitionWorkspaceCleanupResult> Apply(AcquisitionWorkspaceCleanupPreview preview) =>
            Plans.ReclaimWorkspacesAsync(new(preview.PreviewId, [task.Id]), previews, maintenance, _ => Task.CompletedTask, default, ancestors);

        var respectPolicy = (await Preview(false)).Rows.Single();
        Assert.AreEqual(automaticRemoval, respectPolicy.ArtifactIds.Contains(originalId), respectPolicy.ReasonCode);
        Assert.IsFalse((await Preview(true, preserveOriginal: true)).Rows.Single().ArtifactIds.Contains(originalId));
        var selection = await Preview(!automaticRemoval);
        var candidate = selection.Rows.Single();
        Assert.IsTrue(candidate.Eligible, candidate.ReasonCode);
        CollectionAssert.AreEqual(new[] {originalId}, candidate.ArtifactIds.ToArray());
        Assert.AreEqual(1, candidate.FileCount);
        Assert.AreEqual(size, candidate.Bytes);
        Assert.AreEqual(payload, await File.ReadAllTextAsync(input));

        // The receipt identifies a specific server-issued binding; changing only that
        // value must stop an already-previewed application as well as new previews.
        var json = JsonSerializerOptions.Web;
        var receipt = JsonSerializer.Deserialize<AcquisitionManualDownloadCaptureReceipt>(original.MetadataJson!, json)!;
        var forged = JsonSerializer.Serialize(receipt with {BindingId = Guid.NewGuid().ToString("N")}, json);
        await Db.Artifacts.Where(artifact => artifact.Id == originalId).ExecuteUpdateAsync(update => update
            .SetProperty(artifact => artifact.MetadataJson, forged));
        Assert.AreEqual(0, (await Apply(selection)).RemovedFiles);
        Assert.IsFalse((await Preview(true)).Rows.Single().ArtifactIds.Contains(originalId));
        Assert.AreEqual(payload, await File.ReadAllTextAsync(input));
        await Db.Artifacts.Where(artifact => artifact.Id == originalId).ExecuteUpdateAsync(update => update
            .SetProperty(artifact => artifact.MetadataJson, original.MetadataJson));

        selection = await Preview(!automaticRemoval);
        Assert.IsTrue(selection.Rows.Single().Eligible, selection.Rows.Single().ReasonCode);
        Db.ResourcesV2.Add(new() {Path = input, IsFile = true});
        await Db.SaveChangesAsync();
        Assert.AreEqual(0, (await Apply(selection)).RemovedFiles);
        Assert.AreEqual(payload, await File.ReadAllTextAsync(input));
        await Db.ResourcesV2.Where(resource => resource.Path == input).ExecuteDeleteAsync();

        selection = await Preview(!automaticRemoval);
        Assert.IsTrue(selection.Rows.Single().Eligible, selection.Rows.Single().ReasonCode);
        var result = await Apply(selection);
        CollectionAssert.AreEqual(new[] {task.Id}, result.CompletedTaskIds.ToArray());
        Assert.AreEqual(1, result.RemovedFiles);
        Assert.AreEqual(size, result.RemovedBytes);
        Assert.IsFalse(File.Exists(input));
        Assert.AreEqual(payload, await File.ReadAllTextAsync(deliveredFile));
        Assert.AreEqual("independent user file", await File.ReadAllTextAsync(unrelated));
        Assert.IsNotNull(await Artifacts.ResolveByIdAsync(publication.Id));
        Assert.AreEqual(ArtifactLocationState.Retired, (await Artifacts.GetArtifactAsync(originalId))!.State);
        Assert.IsFalse((await Preview(true)).Rows.Single().ArtifactIds.Contains(originalId));
        var after = await Db.AcquisitionTasks.AsNoTracking().SingleAsync(row => row.Id == task.Id);
        Assert.AreEqual(AcquisitionStatus.Completed, after.Status);
        Assert.IsNull(after.DeletedAt);
        Assert.AreEqual(completed.WorkflowRunId, after.WorkflowRunId);
        Assert.AreEqual(completed.PlanRevision, after.PlanRevision);
        Assert.AreEqual(completed.PlanHistoryJson, after.PlanHistoryJson);
    }

    [TestMethod]
    public async Task Local_download_reclamation_uses_retired_processing_lineage()
    {
        var plan = new FileProcessingPlan {Requirement = "required", Steps =
            [new() {Id = "rename", Op = "renameFile", Selector = "received.txt", TargetName = "delivered.txt"}]};
        var task = await LegacyManualSource("Received input with processing", processing: plan);
        task = await Plans.UpdateAsync(task.Id, new(task.Task.PlanRevision, StoragePolicy: new() {SourceCleanup = "delete"}), default);
        var prepared = await Plans.PrepareLocalDownloadsAsync(task.Id, new(task.Task.PlanRevision), null, default);
        var file = Path.Combine(prepared.Parts.Single().Directory!, "received.txt");
        await File.WriteAllTextAsync(file, "verified received contents");
        var intake = await Plans.PreviewLocalDownloadsAsync(task.Id, new(prepared.Revision), null, default);
        var received = await Plans.ApplyLocalDownloadsAsync(task.Id, new(intake.Revision, intake.Token!), null, default);
        var source = received.Plan!.Parts.Single().ContinueFrom!.ArtifactId;
        var (runId, output) = await PublishReceivedFilesAsync(received);
        Assert.IsNotNull(output.ProcessedArtifactId);
        Assert.AreNotEqual(source, output.ProcessedArtifactId);
        await Db.WorkflowRuns.Where(run => run.Id == runId).ExecuteUpdateAsync(update => update
            .SetProperty(run => run.Status, WorkflowRunStatus.Success));
        await Db.AcquisitionTasks.Where(row => row.Id == task.Id).ExecuteUpdateAsync(update => update
            .SetProperty(row => row.Status, AcquisitionStatus.Completed).SetProperty(row => row.Phase, "completed"));
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var maintenance = _services.GetRequiredService<ArtifactWorkspaceMaintenance>();
        var storage = _services.GetRequiredService<IUserStoragePolicy>();
        var ancestors = new PublishedArtifactAncestorCleanup(Db, Artifacts,
            new ArchiveQuarantineService(Artifacts, Path.Combine(_root, "appdata"), storage), workspaces: workspaces, maintenance: maintenance);
        var previews = new AcquisitionStoragePolicyPreviewStore();
        Task<AcquisitionWorkspaceCleanupPreview> Preview(bool onlyIntermediate) =>
            Plans.PreviewWorkspaceCleanupAsync(new([task.Id], OverrideRetention: onlyIntermediate, PreserveOriginalDownloads: onlyIntermediate),
                previews, maintenance, _ => Task.CompletedTask, default, ancestors);
        Task<AcquisitionWorkspaceCleanupResult> Apply(AcquisitionWorkspaceCleanupPreview preview) =>
            Plans.ReclaimWorkspacesAsync(new(preview.PreviewId, [task.Id]), previews, maintenance, _ => Task.CompletedTask, default, ancestors);
        var intermediates = await Preview(true);
        Assert.IsTrue(intermediates.Rows.Single().Eligible, intermediates.Rows.Single().ReasonCode);
        Assert.IsFalse(intermediates.Rows.Single().ArtifactIds.Contains(source));
        Assert.IsTrue((await Apply(intermediates)).CompletedTaskIds.Contains(task.Id));
        Assert.AreEqual(ArtifactLocationState.Retired, (await Artifacts.GetArtifactAsync(output.ProcessedArtifactId!))!.State);
        Assert.IsTrue(File.Exists(file));
        var original = await Preview(false);
        CollectionAssert.AreEqual(new[] {source}, original.Rows.Single().ArtifactIds.ToArray(), original.Rows.Single().ReasonCode);
        Assert.AreEqual(1, (await Apply(original)).RemovedFiles);
        Assert.IsFalse(File.Exists(file));
        Assert.AreEqual("verified received contents", await File.ReadAllTextAsync(output.PublishedFiles.Single()));
    }

    [TestMethod]
    public async Task Local_download_reception_needs_every_group_and_rejects_changed_bytes_at_confirmation()
    {
        var task = await LegacyManualSource("Several groups", twoGroups: true, unknown: true);
        var prepared = await Plans.PrepareLocalDownloadsAsync(task.Id, new(task.Task.PlanRevision), null, default);
        var first = Path.Combine(prepared.Parts[0].Directory!, "part.txt");
        var second = Path.Combine(prepared.Parts[1].Directory!, "part.txt");
        await File.WriteAllTextAsync(first, "same length a");
        await File.WriteAllTextAsync(second + ".crdownload", "incomplete");
        var partial = await Plans.PreviewLocalDownloadsAsync(task.Id, new(prepared.Revision), null, default);
        Assert.IsFalse(partial.CanApply);
        Assert.IsNull(partial.Token);
        Assert.AreEqual("LOCAL_DOWNLOAD_PARTIAL_FILES", partial.Parts[1].ReasonCode);
        File.Move(second + ".crdownload", second);
        var reviewed = await Plans.PreviewLocalDownloadsAsync(task.Id, new(prepared.Revision), null, default);
        Assert.IsTrue(reviewed.CanApply);

        await File.WriteAllTextAsync(first, "same length b");
        var rejected = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Plans.ApplyLocalDownloadsAsync(task.Id, new(reviewed.Revision, reviewed.Token!), null, default));
        Assert.AreEqual("LOCAL_DOWNLOAD_FILES_CHANGED", rejected.Message);
        Assert.AreEqual(0, await Db.Artifacts.CountAsync());
        Assert.AreEqual(prepared.Revision, (await Plans.GetAsync(task.Id, default))!.Task.PlanRevision);

        var latest = await Plans.PreviewLocalDownloadsAsync(task.Id, new(prepared.Revision), null, default);
        var received = await Plans.ApplyLocalDownloadsAsync(task.Id, new(latest.Revision, latest.Token!), null, default);
        Assert.AreEqual(2, received.Plan!.Parts.Select(p => p.ContinueFrom!.ArtifactId).Distinct().Count());
        Assert.IsTrue(received.Plan.Parts.All(p => p.Processing!.Requirement == "unknown"));
        Assert.IsTrue(received.Issues.Any(i => i.Code == "PROCESSING_CONFIRMATION_REQUIRED"));
        Assert.IsTrue(received.Steps.Any(s => s.Kind == AcquisitionPlanProcessingActivity.ActivityKind));
        Assert.AreEqual("same length b", await File.ReadAllTextAsync(first));
        Assert.AreEqual("incomplete", await File.ReadAllTextAsync(second));
    }

    [TestMethod]
    public async Task Local_download_reception_never_adopts_a_preexisting_borrowed_folder()
    {
        var task = await LegacyManualSource("Borrowed folder");
        var foreign = Directory.CreateDirectory(Path.Combine(_root, "unrelated")).FullName;
        var unrelated = Path.Combine(foreign, "user.txt");
        await File.WriteAllTextAsync(unrelated, "independent user file");
        var mapped = await Plans.ChangeStageDirectoriesAsync(task.Id, new(task.Task.PlanRevision,
            [new("a", Download: new(foreign, false))]), null, default);
        var details = (await Plans.GetAsync(task.Id, default))!;
        Assert.IsNull(details.CanReceiveLocalDownloads);
        var refused = await Plans.PreviewLocalDownloadsAsync(task.Id, new(mapped.Revision), null, default);
        Assert.IsFalse(refused.CanPrepare);
        Assert.IsFalse(refused.CanApply);
        Assert.AreEqual("independent user file", await File.ReadAllTextAsync(unrelated));
        Assert.AreEqual(0, await Db.Artifacts.CountAsync());
    }

    [TestMethod]
    public async Task Local_download_reception_only_offers_its_own_allocated_tree_for_whole_task_deletion()
    {
        var task = await LegacyManualSource("Received files");
        var sibling = await LegacyManualSource("Independent same file name");
        var first = await Plans.PrepareLocalDownloadsAsync(task.Id, new(task.Task.PlanRevision), null, default);
        var next = await Plans.PrepareLocalDownloadsAsync(sibling.Id, new(sibling.Task.PlanRevision), null, default);
        var directory = first.Parts.Single().Directory!;
        var unrelated = next.Parts.Single().Directory!;
        await File.WriteAllTextAsync(Path.Combine(directory, "payload.bin"), "registered reception");
        var siblingFile = Path.Combine(unrelated, "payload.bin");
        await File.WriteAllTextAsync(siblingFile, "independent files");
        var reviewed = await Plans.PreviewLocalDownloadsAsync(task.Id, new(first.Revision), null, default);
        await Plans.ApplyLocalDownloadsAsync(task.Id, new(reviewed.Revision, reviewed.Token!), null, default);
        var workspace = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var maintenance = _services.GetRequiredService<ArtifactWorkspaceMaintenance>();
        var storage = _services.GetRequiredService<IUserStoragePolicy>();
        var ancestors = new PublishedArtifactAncestorCleanup(Db, Artifacts,
            new ArchiveQuarantineService(Artifacts, Path.Combine(_root, "appdata"), storage), workspaces: workspace, maintenance: maintenance);
        var cleanup = new AcquisitionTaskDeletionCleanupService(Db, Artifacts, maintenance, ancestors, storage, workspaces: workspace);
        var deletion = new AcquisitionTaskDeletionService(Db, new NoManualDownloadWriters(), new(), cleanup);
        var preview = await deletion.PreviewAsync(new([task.Id], true, true, true, DeleteDeliveredFiles: true));
        var groups = preview.Rows.Single().Cleanup!.Groups;
        Assert.IsTrue(groups.Any(g => g.Directory == directory && g.Kind == "download" && g.Eligible));
        Assert.IsFalse(groups.Any(g => g.Directory == unrelated));
        Assert.AreEqual("independent files", await File.ReadAllTextAsync(siblingFile));
        Assert.IsTrue(File.Exists(Path.Combine(directory, "payload.bin")), "A preview cannot remove a file.");
    }

    private sealed class NoManualDownloadWriters : IAcquisitionDeletionActivityReader
    {
        public Task<IReadOnlyDictionary<int, AcquisitionDeletionActivity>> ReadAsync(IReadOnlyList<AcquisitionTaskDbModel> tasks,
            CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<int, AcquisitionDeletionActivity>>(
            tasks.ToDictionary(t => t.Id, _ => new AcquisitionDeletionActivity([], "idle", null)));
    }
}
