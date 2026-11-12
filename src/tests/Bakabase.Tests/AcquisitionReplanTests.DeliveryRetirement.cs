using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OnlyNewOptedInRevisionSurvivesRestartAsRetirementWork(bool keep)
    {
        var (task, old, _, publication, _) = await CompletedInput(true, "wrapper");
        var options = _services.GetRequiredService<IBOptions<AcquisitionOptions>>();
        options.Value.KeepSupersededDeliveryFiles = keep;
        var restarted = new AcquisitionDeliveryRetirementQueue(options);
        var worker = new AcquisitionDeliveryRetirementWorker(_services.GetRequiredService<IServiceScopeFactory>(), restarted,
            NullLogger<AcquisitionDeliveryRetirementWorker>.Instance);
        await worker.RestoreAsync(Db, default);
        Assert.AreEqual(0, restarted.Snapshot().Length, "Existing completed tasks must never enter a historical sweep.");

        var policy = new ArtifactDeliveryPolicy {CollapseSingleRootDirectory = true};
        var preview = FileArtifactDeliveryLayout.Preview(publication, policy);
        var updated = await Plans.ReplanAsync(task.Id, new(task.Task.PlanRevision, old.Id, "Remove wrapper", [
            new("main", new() {Requirement = "notRequired"}, [publication.Id], publication.Id, preview.ProjectionFingerprint)],
            DeliveryContentLayout: policy), default);
        var revision = updated.Task.PlanRevision;
        var state = await RetirementState(task.Id);
        var live = _services.GetRequiredService<AcquisitionDeliveryRetirementQueue>().Snapshot();
        await worker.RestoreAsync(Db, default);
        CollectionAssert.AreEquivalent(live, restarted.Snapshot());
        if (keep)
        {
            Assert.IsNull(state.PendingDeliveryRetirements);
            Assert.AreEqual(0, live.Length);
            return;
        }
        CollectionAssert.AreEqual(new[] {(task.Id, revision)}, live);
        var saved = state.PendingDeliveryRetirements!.Single();
        Assert.AreEqual(new AcquisitionDeliveryRetirement(revision, old.Id, "main", publication.Id), saved);
        Assert.IsFalse(await AcquisitionDeliveryRetirementWorker.ProcessAsync(_services, task.Id, revision, default),
            "A saved revision without a successful run must preserve its old files.");
        Assert.IsTrue(File.Exists(publication.Files.Single()));

        // Simulate the terminal run having no publication for the pinned part. Refusal is durable,
        // and neither repeated observations nor a restart can turn it into a cleanup of a different output.
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Success, StartedAt = DateTime.UtcNow};
        Db.WorkflowRuns.Add(run);
        await Db.SaveChangesAsync();
        await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(set =>
            set.SetProperty(t => t.Status, AcquisitionStatus.Completed).SetProperty(t => t.WorkflowRunId, run.Id));
        Assert.IsTrue(await AcquisitionDeliveryRetirementWorker.ProcessAsync(_services, task.Id, revision, default));
        Assert.IsTrue(await AcquisitionDeliveryRetirementWorker.ProcessAsync(_services, task.Id, revision, default));
        state = await RetirementState(task.Id);
        Assert.IsNull(state.PendingDeliveryRetirements);
        var receipt = state.DeliveryRetirementResults!.Single();
        Assert.AreEqual("publication_missing", receipt.Status);
        Assert.AreEqual(run.Id, receipt.RunId);
        Assert.AreEqual(publication.Id, receipt.PreviousArtifactId);
        restarted.Complete((task.Id, revision));
        await worker.RestoreAsync(Db, default);
        Assert.AreEqual(0, restarted.Snapshot().Length);
        Assert.IsTrue(File.Exists(publication.Files.Single()));
    }

    [TestMethod]
    public async Task StaleRetirementCannotConsumeNewRevisionsIntentOrPublication()
    {
        var (task, old, _, published, _) = await CompletedInput();
        var stale = new AcquisitionDeliveryRetirement(1, old.Id, "main", published.Id);
        var next = new AcquisitionDeliveryRetirement(2, old.Id, "main", published.Id);
        var state = await RetirementState(task.Id);
        var json = JsonSerializer.Serialize(state with {PendingDeliveryRetirements = [stale, next]}, WorkflowJson.Options);
        await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(set => set
            .SetProperty(t => t.PlanRevision, 2).SetProperty(t => t.BuilderStateJson, json));
        Assert.IsTrue(await AcquisitionDeliveryRetirementWorker.ProcessAsync(_services, task.Id, 1, default));
        state = await RetirementState(task.Id);
        Assert.AreEqual(next, state.PendingDeliveryRetirements!.Single());
        Assert.AreEqual("revision_changed", state.DeliveryRetirementResults!.Single().Status);
        Assert.IsTrue(File.Exists(published.Files.Single()));
    }

    private async Task<AcquisitionPlanBuilderState> RetirementState(int taskId) =>
        JsonSerializer.Deserialize<AcquisitionPlanBuilderState>((await Db.AcquisitionTasks.AsNoTracking()
            .SingleAsync(t => t.Id == taskId)).BuilderStateJson!, WorkflowJson.Options)!;
}
