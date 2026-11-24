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
    private async Task<AcquisitionPlanTaskDetail> LegacyManualSource(string name, bool twoGroups = false, bool unknown = false)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "incoming")).FullName;
        AcquisitionPlanPart Part(string id) => new()
        {
            Id = id, SelectedSourceId = "source-" + id,
            Sources = [new() {Id = "source-" + id, Url = "https://example.test/unsupported/" + id, ProviderId = "unknown"}],
            Processing = new() {Requirement = unknown ? "unknown" : "notRequired"}
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
        var started = await plans.RunAsync(task.Id, received.Task.PlanRevision, default);
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
        Assert.AreEqual(await File.ReadAllTextAsync(input), await File.ReadAllTextAsync(output.PublishedFiles.Single()));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.IsNull(started.CanReceiveLocalDownloads);
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
