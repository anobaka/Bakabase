using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow("Accepted the nine available original archives; the tenth is missing.")]
    [DataRow(null)]
    public async Task LocalFiles_keep_prepared_saves_only_the_task_scope_and_runs_only_after_confirmation(string? note)
    {
        var f = await LocalFilesFixture(dependencyMissing: true);
        var first = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        await f.Recovery.ApplyAsync(new(first.PreviewToken, [f.TaskId], PrepareOnly: true));
        var previous = (await f.Plans.GetAsync(f.TaskId, default))!;
        Assert.AreEqual(true, previous.CanKeepPreparedFiles);
        var download = JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().SingleAsync());
        var receipt = (await _db.DownloadResults.AsNoTracking().SingleAsync()).Fingerprint;
        var originalFile = JsonSerializer.Deserialize<string[]>((await _db.DownloadResults.AsNoTracking().SingleAsync()).FilesJson)!.Single();
        var input = new AcquisitionKeepPreparedFilesInput(2, note);
        var guards = new List<string?>();
        var kept = await f.Plans.KeepPreparedFilesAsync(f.TaskId, input, default, p => {guards.Add(p); return Task.CompletedTask;});
        CollectionAssert.Contains(guards.ToArray(), Path.GetDirectoryName(originalFile));
        Assert.IsNull(kept.Task.WorkflowRunId);
        Assert.AreEqual(0, f.Host.Calls);
        Assert.AreEqual(3, kept.Task.PlanRevision);
        Assert.AreEqual(note ?? "delivery.keepPreparedFiles", kept.PlanHistory.Last().Reason);
        Assert.AreEqual(JsonSerializer.Serialize(previous.PlanHistory), JsonSerializer.Serialize(kept.PlanHistory.Take(2)));
        var plan = kept.Plan!;
        Assert.AreEqual(previous.Plan!.Parts.Single().ContinueFrom, plan.Parts.Single().ContinueFrom);
        Assert.AreEqual(previous.Plan.DeliveryLayout!.TaskDirectory, plan.DeliveryLayout!.TaskDirectory);
        Assert.AreEqual("extractArchive", kept.PlanHistory[1].Plan.Parts.Single().Processing!.Steps.Single().Op);
        Assert.IsFalse(plan.PublishRetainedFiles || plan.AdaptiveFileProcessing);
        Assert.AreEqual(false, plan.Parts.Single().UseAdaptiveAssessment);
        var compiled = AcquisitionPlanCompiler.Compile(plan);
        CollectionAssert.AreEqual(new[] {"acquisition.plan.continueFiles", "acquisition.plan.process", "fs.publishArtifacts"},
            compiled.Nodes.Select(n => n.Kind).ToArray());
        var process = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(compiled.Nodes[1].ConfigJson!, WorkflowJson.Options)!;
        Assert.IsTrue(process.UseVerifiedInput);
        Assert.AreEqual("notRequired", process.Plan.Requirement);
        Assert.AreEqual(0, process.Plan.Steps.Count);
        Assert.AreEqual(3, (await f.Plans.KeepPreparedFilesAsync(f.TaskId, input, default)).Task.PlanRevision);
        Assert.AreEqual(3, (await f.Plans.KeepPreparedFilesAsync(f.TaskId, input with {ExpectedRevision = 3}, default)).Task.PlanRevision);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            f.Plans.KeepPreparedFilesAsync(f.TaskId, new(2, "Different scope"), default));
        Assert.AreEqual(1, await _db.WorkflowRuns.CountAsync());
        Assert.IsFalse(Directory.Exists(plan.DeliveryLayout.TaskDirectory));
        var running = await f.Plans.RunAsync(f.TaskId, 3, default);
        Assert.AreEqual(1, f.Host.Calls);
        Assert.IsNotNull(running.Task.WorkflowRunId);
        Assert.IsNull(running.CanKeepPreparedFiles);
        Assert.AreEqual(download, JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().SingleAsync()));
        Assert.AreEqual(receipt, (await _db.DownloadResults.AsNoTracking().SingleAsync()).Fingerprint);
        Assert.AreEqual("verified existing bytes", await File.ReadAllTextAsync(originalFile));
    }

    [DataTestMethod]
    [DataRow("originalRun")]
    [DataRow("missing")]
    [DataRow("scope")]
    [DataRow("note")]
    public async Task LocalFiles_keep_prepared_rejects_unavailable_foreign_or_unapproved_inputs(string fault)
    {
        var f = await LocalFilesFixture();
        if (fault != "originalRun")
        {
            var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
            await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId], PrepareOnly: true));
        }
        var row = await _db.AcquisitionTasks.SingleAsync();
        if (fault == "missing")
        {
            var result = await _db.DownloadResults.AsNoTracking().SingleAsync();
            File.Delete(JsonSerializer.Deserialize<string[]>(result.FilesJson)!.Single());
        }
        if (fault == "scope")
        {
            var plan = JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson!, WorkflowJson.Options)!;
            var p = plan.Parts.Single();
            row.PlanJson = JsonSerializer.Serialize(plan with {Parts = [p with {ContinueFrom = p.ContinueFrom! with {TaskId = row.Id + 1}}]}, WorkflowJson.Options);
            await _db.SaveChangesAsync();
        }
        var before = row.PlanHistoryJson;
        if (fault == "note")
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Plans.KeepPreparedFilesAsync(f.TaskId, new(row.PlanRevision, new string('x', 1001)), default));
        else
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Plans.KeepPreparedFilesAsync(f.TaskId, new(row.PlanRevision), default));
        Assert.AreEqual(before, (await _db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanHistoryJson);
        Assert.AreEqual(0, f.Host.Calls);
        Assert.AreEqual(1, await _db.WorkflowRuns.CountAsync());
    }
}
