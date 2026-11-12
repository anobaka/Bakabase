using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [TestMethod]
    public async Task Typed_processing_conflict_can_revise_from_existing_download_without_mutating_waiting_history()
    {
        var (task, run, artifact) = await DownloadedInput();
        run.Status = WorkflowRunStatus.Waiting;
        run.CompletedAt = null;
        run.WaitReason = "cloud.fileProcessing";
        run.WaitPromptJson = "{\"reason\":\"ProcessingPathConflict\"}";
        run.CurrentStepIndex = WorkflowRunPlans.Read(run.PlanSnapshotJson)!.Nodes
            .Select((node, index) => (node, index)).First(item => item.node.Kind == AcquisitionPlanProcessingActivity.ActivityKind).index;
        var row = await Db.AcquisitionTasks.FindAsync(task.Id);
        row!.Status = AcquisitionStatus.Waiting;
        await Db.SaveChangesAsync();
        var originalPlan = run.PlanSnapshotJson;
        var originalPrompt = run.WaitPromptJson;

        var detail = (await Plans.GetAsync(task.Id, default))!;
        Assert.IsTrue(detail.ReplanArtifacts.Any(a => a.ArtifactId == artifact.Id));
        var revised = await Plans.ReplanAsync(task.Id, ContinueDownload(detail, run.Id, artifact.Id), default);

        Assert.IsNull(revised.Task.WorkflowRunId);
        Assert.AreEqual("ready", revised.Task.Phase);
        Assert.AreEqual(artifact.Id, revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId);
        var saved = await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.AreEqual(WorkflowRunStatus.Waiting, saved.Status);
        Assert.AreEqual(originalPlan, saved.PlanSnapshotJson);
        Assert.AreEqual(originalPrompt, saved.WaitPromptJson);
        var retained = await AcquisitionPlanArtifactInputGuard.RequireAsync(Db, Artifacts, task.Id, "main",
            revised.Plan.Parts.Single().ContinueFrom!, default);
        Assert.AreEqual(artifact.Id, retained.Id, "The unchanged historical wait remains valid provenance for the continuation.");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new AcquisitionWorkflowRunMutationGuard(Db).EnsureCanMutateAsync(run.Id));
        Assert.IsTrue(AcquisitionPlanCompiler.Compile(revised.Plan).Nodes
            .All(node => !node.Kind.StartsWith("download.") && node.Kind != "baidu.transfer"));
        Assert.AreEqual("original bytes", await File.ReadAllTextAsync(artifact.Files.Single()));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("password")]
    [DataRow("different-node")]
    [DataRow("pending-answer")]
    [DataRow("malformed-prompt")]
    [DataRow("no-retained-input")]
    public async Task Waiting_replan_does_not_bypass_other_answers_or_restart_downloads(string fault)
    {
        var (task, run, artifact) = await DownloadedInput();
        run.Status = WorkflowRunStatus.Waiting;
        run.WaitReason = "cloud.fileProcessing";
        run.WaitPromptJson = "{\"reason\":\"ProcessingPathConflict\"}";
        run.CurrentStepIndex = WorkflowRunPlans.Read(run.PlanSnapshotJson)!.Nodes
            .Select((node, index) => (node, index)).First(item => item.node.Kind == AcquisitionPlanProcessingActivity.ActivityKind).index;
        var row = await Db.AcquisitionTasks.FindAsync(task.Id);
        row!.Status = AcquisitionStatus.Waiting;
        if (fault == "password") run.WaitPromptJson = "{\"reason\":\"PasswordRequired\"}";
        if (fault == "different-node") run.CurrentStepIndex = WorkflowRunPlans.Read(run.PlanSnapshotJson)!.Nodes
            .Select((node, index) => (node, index)).First(item => item.node.Kind != AcquisitionPlanProcessingActivity.ActivityKind).index;
        if (fault == "pending-answer") run.PendingSignalJson = "{}";
        if (fault == "malformed-prompt") run.WaitPromptJson = "[]";
        await Db.SaveChangesAsync();
        var request = ContinueDownload(task, run.Id, artifact.Id);
        if (fault == "no-retained-input")
            request = request with {Items = request.Items.Select(item => item with {ContinueFromArtifactId = null}).ToArray()};

        if (fault == "no-retained-input")
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => Plans.ReplanAsync(task.Id, request, default));
        else
        {
            var candidates = (await Plans.GetAsync(task.Id, default))!.ReplanArtifacts;
            if (fault == "password")
                Assert.IsTrue(candidates.Single().CanReadCurrentDirectory, "A new explicit current-directory input is allowed; the legacy signal is not bypassed.");
            else Assert.AreEqual(0, candidates.Count);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, request, default));
        }
        Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).PlanRevision);
        Assert.AreEqual("original bytes", await File.ReadAllTextAsync(artifact.Files.Single()));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }
}
