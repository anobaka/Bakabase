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
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private async Task<(AcquisitionPlanTaskDetail Task, WorkflowRunDbModel Run, ArtifactRecord Input)>
        FailedLocalContinuation(bool bindCurrent = false)
    {
        var (task, originalRun, download) = await Failed();
        var input = await ProcessedInput(originalRun.Id, download);
        var revised = await Plans.ReplanAsync(task.Id, ContinueInput(task, originalRun.Id, input.Id), default);
        var run = new WorkflowRunDbModel {WorkflowDefinitionId = 0, Status = WorkflowRunStatus.Failed,
            StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, ErrorMessage = "Local processing failed",
            PlanSnapshotJson = WorkflowRunPlans.Serialize(AcquisitionPlanCompiler.Compile(revised.Plan!)),
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "2"}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        row.WorkflowRunId = run.Id; row.Status = AcquisitionStatus.Failed; row.Phase = "attention";
        row.Error = run.ErrorMessage; await Db.SaveChangesAsync();
        if (bindCurrent) await new ArtifactOperationBindingService(Db)
            .BindAsync("workflow", run.Id.ToString(), "main:artifact.continuation", input.OperationId);
        return ((await Plans.GetAsync(task.Id, default))!, run, input);
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Processing_edit_retains_local_input_when_omitted_or_repeated_even_before_current_run_binding(
        bool explicitSameInput, bool bindCurrent)
    {
        var (task, run, input) = await FailedLocalContinuation(bindCurrent);
        var originalInput = task.Plan!.Parts.Single().ContinueFrom!;
        var oldHistory = JsonSerializer.Serialize(task.PlanHistory, WorkflowJson.Options);
        var oldRuns = await Db.WorkflowRuns.AsNoTracking().OrderBy(r => r.Id).Select(r => r.PlanSnapshotJson).ToListAsync();
        Assert.IsTrue(task.ReplanArtifacts.Any(a => a.ArtifactId == input.Id), "An earlier-run input remains visible before the new run binds it.");
        var request = ContinueInput(task, run.Id, input.Id);
        if (!explicitSameInput) request = request with {Items = [request.Items[0] with {ContinueFromArtifactId = null}]};
        var revised = await Plans.ReplanAsync(task.Id, request, default);
        Assert.AreEqual(bindCurrent ? originalInput with {PreviousRunId = run.Id} : originalInput,
            revised.Plan!.Parts.Single().ContinueFrom);
        Assert.IsFalse(AcquisitionPlanCompiler.Compile(revised.Plan).Nodes.Any(n =>
            n.Kind.StartsWith("download.") || n.Kind.StartsWith("baidu.")), "Omitted optional input must not create network steps.");
        Assert.AreEqual(oldHistory, JsonSerializer.Serialize(revised.PlanHistory.Take(task.PlanHistory.Count), WorkflowJson.Options));
        Assert.AreEqual(input.Id, revised.PlanHistory.Last().ArtifactEvidence!.Single().ArtifactId);
        CollectionAssert.AreEqual(oldRuns, await Db.WorkflowRuns.AsNoTracking().OrderBy(r => r.Id).Select(r => r.PlanSnapshotJson).ToListAsync());
        Assert.AreEqual("already extracted first layer", await File.ReadAllTextAsync(input.Files.Single()));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Retained_input_missing_or_changed_blocks_replan_instead_of_returning_to_download(bool missing)
    {
        var (task, run, input) = await FailedLocalContinuation();
        var before = await Db.AcquisitionTasks.AsNoTracking().SingleAsync();
        if (missing) File.Delete(input.Files.Single()); else await File.WriteAllTextAsync(input.Files.Single(), "changed local input");
        var request = ContinueInput(task, run.Id, input.Id);
        request = request with {Items = [request.Items[0] with {ContinueFromArtifactId = null}]};
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, request, default));
        Assert.AreEqual("PLAN_REPLAN_EVIDENCE_UNAVAILABLE", error.Message);
        var after = await Db.AcquisitionTasks.AsNoTracking().SingleAsync();
        Assert.AreEqual(before.PlanJson, after.PlanJson); Assert.AreEqual(before.PlanHistoryJson, after.PlanHistoryJson);
        Assert.AreEqual(before.WorkflowRunId, after.WorkflowRunId); Assert.AreEqual(before.Status, after.Status);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Retained_input_requires_its_frozen_owner_and_part_binding()
    {
        var (task, run, input) = await FailedLocalContinuation(true);
        var original = task.Plan!.Parts.Single().ContinueFrom!;
        await Db.Set<ArtifactOperationBindingDbModel>().Where(b => b.OwnerId == original.PreviousRunId.ToString())
            .ExecuteDeleteAsync();
        var request = ContinueInput(task, run.Id, input.Id);
        request = request with {Items = [request.Items[0] with {ContinueFromArtifactId = null}]};
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Plans.ReplanAsync(task.Id, request, default));
        Assert.AreEqual(task.Task.PlanRevision, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
    }

    private async Task<ArtifactRecord> ReplacementInput(int runId)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "replacement-input")).FullName;
        var file = Path.Combine(directory, "replacement.r"); await File.WriteAllTextAsync(file, "another verified input");
        var artifact = await Artifacts.AdoptAsync(new("replacement-op", "replacement-intent", "files.process", "replacement"),
            new(directory, [file], "processedFiles", true), "test");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", runId.ToString(), "main:process:replacement", artifact.OperationId);
        return artifact;
    }

    [TestMethod]
    public async Task Selecting_other_revision_evidence_does_not_replace_or_omit_the_saved_local_input()
    {
        var (task, run, input) = await FailedLocalContinuation();
        var inspected = await ReplacementInput(run.Id);
        var revised = await Plans.ReplanAsync(task.Id, Input(task, run, inspected.Id), default);
        Assert.AreEqual(task.Plan!.Parts.Single().ContinueFrom, revised.Plan!.Parts.Single().ContinueFrom);
        CollectionAssert.AreEquivalent(new[] {input.Id, inspected.Id},
            revised.PlanHistory.Last().ArtifactEvidence!.Select(a => a.ArtifactId).ToArray());
        Assert.IsFalse(AcquisitionPlanCompiler.Compile(revised.Plan).Nodes.Any(n => n.Kind.StartsWith("download.")));
    }

    [TestMethod]
    public async Task Explicit_different_input_uses_current_run_validation_and_does_not_silently_keep_previous_input()
    {
        var (task, run, _) = await FailedLocalContinuation();
        var replacement = await ReplacementInput(run.Id);
        var revised = await Plans.ReplanAsync(task.Id, ContinueInput(task, run.Id, replacement.Id), default);
        Assert.AreEqual(replacement.Id, revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId);
        Assert.AreEqual(run.Id, revised.Plan.Parts.Single().ContinueFrom!.PreviousRunId);
        Assert.IsFalse(AcquisitionPlanCompiler.Compile(revised.Plan).Nodes.Any(n => n.Kind.StartsWith("download.")));
    }

    [TestMethod]
    public async Task Different_input_bound_only_to_an_earlier_run_is_not_inherited_as_an_explicit_replacement()
    {
        var (task, run, _) = await FailedLocalContinuation();
        var replacement = await ReplacementInput(task.Plan!.Parts.Single().ContinueFrom!.PreviousRunId);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Plans.ReplanAsync(task.Id,
            ContinueInput(task, run.Id, replacement.Id), default));
        Assert.AreEqual(task.Task.PlanRevision, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
    }

    [TestMethod]
    public async Task Current_source_plan_without_continuation_does_not_resurrect_one_from_old_history()
    {
        var (task, run, _) = await FailedLocalContinuation();
        var evidence = await ReplacementInput(run.Id);
        var part = task.Plan!.Parts.Single();
        var sourcePlan = task.Plan with {Parts = [part with {ContinueFrom = null,
            Sources = [part.Sources.Single() with {Url = "https://different-source.test/current"}]}]};
        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        row.PlanJson = JsonSerializer.Serialize(sourcePlan, WorkflowJson.Options); await Db.SaveChangesAsync();
        var revised = await Plans.ReplanAsync(task.Id, Input(task, run, evidence.Id), default);
        Assert.IsNull(revised.Plan!.Parts.Single().ContinueFrom);
        Assert.AreEqual("https://different-source.test/current", revised.Plan.Parts.Single().Sources.Single().Url);
        Assert.IsNotNull(revised.PlanHistory[1].Plan.Parts.Single().ContinueFrom, "The old local revision is preserved as history only.");
    }
}
