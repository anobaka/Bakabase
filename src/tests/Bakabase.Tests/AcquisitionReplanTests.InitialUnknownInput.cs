using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private async Task<(AcquisitionPlanTaskDetail Task, WorkflowRunDbModel Run, ArtifactRecord Artifact)> InitialUnknownInput()
    {
        var (task, run, input) = await DownloadedInput();
        var plan = WorkflowRunPlans.Read(run.PlanSnapshotJson)!;
        var cursor = plan.Nodes.Select((node, index) => (node, index))
            .First(n => n.node.Kind == AcquisitionPlanProcessingActivity.ActivityKind).index;
        var nodes = plan.Nodes.ToArray();
        var process = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(nodes[cursor].ConfigJson, WorkflowJson.Options)!;
        nodes[cursor] = nodes[cursor] with {ConfigJson = JsonSerializer.Serialize(
            process with {Plan = new() {Requirement = "unknown"}, StopAfterStepId = null, ContinueFrom = null, UseVerifiedInput = false}, WorkflowJson.Options)};
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(plan with {Nodes = nodes});
        run.Status = WorkflowRunStatus.Waiting; run.CompletedAt = null; run.CurrentStepIndex = cursor;
        run.WaitReason = "cloud.fileProcessing";
        run.WaitPromptJson = "{\"reason\":\"ExtractionPlanUnknown\",\"targetId\":\"main\",\"canEditPlan\":true,\"allowedActions\":[]}";
        var workset = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson)!;
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [workset.Targets.Single() with
            {ProcessingWaitReason = "ExtractionPlanUnknown", ProcessingPlanJson = "{\"requirement\":\"unknown\",\"steps\":[]}"}]});
        (await Db.AcquisitionTasks.FindAsync(task.Id))!.Status = AcquisitionStatus.Waiting;
        await Db.SaveChangesAsync(); return (task, run, input);
    }

    [DataTestMethod]
    [DataRow(AcquisitionStatus.Waiting)]
    [DataRow(AcquisitionStatus.Running)]
    [DataRow(AcquisitionStatus.Pending)]
    public async Task Initial_unknown_wait_without_prompt_artifact_can_prepare_local_AI_from_verified_download(AcquisitionStatus projectedStatus)
    {
        var (task, run, input) = await InitialUnknownInput();
        (await Db.AcquisitionTasks.FindAsync(task.Id))!.Status = projectedStatus;
        await Db.SaveChangesAsync();
        var original = (run.PlanSnapshotJson, run.CurrentItemJson, run.WaitPromptJson);
        var detail = (await Plans.GetAsync(task.Id, default))!;
        Assert.AreEqual(input.Id, detail.ReplanArtifacts.Single().ArtifactId);
        var request = new AcquisitionPlanReplanInput(task.Task.PlanRevision, run.Id, "Analyze the retained files.",
            [new("main", new() {Requirement = "unknown"}, [input.Id], input.Id, UseAdaptiveAssessment: true)],
            EnableAdaptiveAssessment: true, MaxAdaptiveIterations: 3);
        var revised = await Plans.ReplanAsync(task.Id, request, default);
        Assert.AreEqual(input.Id, revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId);
        Assert.IsTrue(revised.Plan.Parts.Single().UseAdaptiveAssessment);
        Assert.IsTrue(AcquisitionPlanCompiler.Compile(revised.Plan).Nodes.All(n => !n.Kind.StartsWith("download.") && n.Kind != "baidu.transfer"));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        var saved = await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.AreEqual(original, (saved.PlanSnapshotJson, saved.CurrentItemJson, saved.WaitPromptJson));
        Assert.AreEqual("original bytes", await File.ReadAllTextAsync(input.Files.Single()));
    }

    [DataTestMethod]
    [DataRow("target")]
    [DataRow("pending")]
    [DataRow("running")]
    [DataRow("later-result")]
    [DataRow("source")]
    [DataRow("binding")]
    [DataRow("checkpoint-files")]
    public async Task Initial_unknown_fallback_rejects_stale_checkpoint_or_unowned_input(string fault)
    {
        var (task, run, input) = await InitialUnknownInput();
        if (fault is "running" or "pending")
            (await Db.AcquisitionTasks.FindAsync(task.Id))!.Status = AcquisitionStatus.Running;
        if (fault == "target") run.WaitPromptJson = "{\"reason\":\"ExtractionPlanUnknown\",\"targetId\":\"other\"}";
        if (fault == "pending") run.PendingSignalJson = "{}";
        if (fault == "running") run.Status = WorkflowRunStatus.Running;
        if (fault == "source") (await Db.ArtifactOperations.SingleAsync(o => o.Id == input.OperationId)).InputFingerprint = "changed";
        if (fault == "binding") await Db.ArtifactOperationBindings.ExecuteDeleteAsync();
        if (fault is "later-result" or "checkpoint-files")
        {
            var workset = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson)!;
            var target = workset.Targets.Single();
            run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [fault == "later-result"
                ? target with {ProcessedArtifactId = "later-output", ProcessingOperationId = "later-operation"}
                : target with {Files = []}]});
        }
        await Db.SaveChangesAsync();
        Assert.AreEqual(0, (await Plans.GetAsync(task.Id, default))!.ReplanArtifacts.Count);
        var failed = false;
        try {await Plans.ReplanAsync(task.Id, ContinueDownload(task, run.Id, input.Id), default);}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) {failed = true;}
        Assert.IsTrue(failed); Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).PlanRevision);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Initial_unknown_local_replan_requires_all_parts_and_revalidates_file_bytes()
    {
        var (task, run, input) = await InitialUnknownInput();
        await File.WriteAllTextAsync(input.Files.Single(), "changed original bytes");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, ContinueDownload(task, run.Id, input.Id), default));
        await File.WriteAllTextAsync(input.Files.Single(), "original bytes");
        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        var layout = task.Plan!.DeliveryLayout!;
        row.PlanJson = JsonSerializer.Serialize(task.Plan with
        {StageDirectories = null, Parts = [task.Plan.Parts.Single(), task.Plan.Parts.Single() with
            {Id = "other", SelectedSourceId = "other-" + task.Plan.Parts.Single().SelectedSourceId,
                Sources = task.Plan.Parts.Single().Sources.Select(s => s with {Id = "other-" + s.Id}).ToList()}],
            DeliveryLayout = layout with {PartDirectories = new()
            {{"main", Path.Combine(layout.TaskDirectory, "main")}, {"other", Path.Combine(layout.TaskDirectory, "other")}}}}, WorkflowJson.Options);
        var workset = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson)!;
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [workset.Targets.Single(), workset.Targets.Single() with
            {Id = "other", SelectedSourceId = "other-" + workset.Targets.Single().SelectedSourceId,
                Sources = workset.Targets.Single().Sources.Select(s => s with {Id = "other-" + s.Id}).ToList(),
                DownloadArtifactId = null, DownloadOperationId = null, Files = [], ProcessingWaitReason = null}]});
        await Db.SaveChangesAsync();
        var detail = (await Plans.GetAsync(task.Id, default))!;
        Assert.AreEqual(1, detail.ReplanArtifacts.Count); Assert.AreEqual("main", detail.ReplanArtifacts.Single().PartId);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Plans.ReplanAsync(task.Id, ContinueDownload(task, run.Id, input.Id), default));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }
}
