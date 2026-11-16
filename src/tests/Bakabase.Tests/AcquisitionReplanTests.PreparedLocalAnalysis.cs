using System.Text.Json;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [TestMethod]
    public async Task Prepared_local_AI_runs_the_bound_files_without_a_second_download_or_change_to_source_history()
    {
        var (task, previous, original) = await Failed();
        var artifact = await ProcessedInput(previous.Id, original);
        var saved = await Plans.ReplanAsync(task.Id, ContinueInput(task, previous.Id, artifact.Id), default);
        _services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.UseEmergencySpaceReserve = true;
        var history = JsonSerializer.Serialize(saved.PlanHistory);
        var frozen = previous.PlanSnapshotJson;
        var migration = new AcquisitionAdaptiveMigrationService(Db, null!, Plans);
        var preview = await migration.PreviewAsync(new([task.Id], 4));
        Assert.IsTrue(preview.Rows.Single().CanMigrate, preview.Rows.Single().Reason);
        Assert.IsNull(preview.Rows.Single().WorkflowRunId);
        var second = await migration.PreviewAsync(new([task.Id]));
        var result = await migration.ApplyAsync(preview.Token, [task.Id]);
        CollectionAssert.AreEqual(new[] {task.Id}, result.MigratedTaskIds.ToArray(), result.Skipped.FirstOrDefault()?.Reason);
        var after = (await Plans.GetAsync(task.Id, default))!;
        Assert.AreEqual(saved.Task.PlanRevision + 1, after.Task.PlanRevision);
        Assert.AreEqual(history, JsonSerializer.Serialize(after.PlanHistory.Take(saved.PlanHistory.Count)));
        Assert.AreEqual(saved.Plan!.Parts.Single().ContinueFrom, after.Plan!.Parts.Single().ContinueFrom);
        Assert.AreEqual(JsonSerializer.Serialize(saved.Plan.Parts.Single().Sources), JsonSerializer.Serialize(after.Plan.Parts.Single().Sources));
        Assert.AreEqual(saved.Plan.Parts.Single().ProcessingSource, after.Plan.Parts.Single().ProcessingSource);
        Assert.AreEqual("unknown", after.Plan.Parts.Single().Processing!.Requirement);
        Assert.AreEqual(4, after.Plan.AdaptiveFileProcessingMaxIterations);
        var run = await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == after.Task.WorkflowRunId);
        var grant = AcquisitionStorageOverrideGrant.Read(await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id));
        Assert.AreEqual("configured", grant?.ReserveMode);
        Assert.AreEqual(run.Id, grant!.WorkflowRunId);
        var nodes = WorkflowRunPlans.Read(run.PlanSnapshotJson)!.Nodes;
        CollectionAssert.AreEqual(new[] {"acquisition.plan.continueFiles", "acquisition.plan.process",
            AssessFileArtifactDeliveryActivity.ActivityKind, "fs.publishArtifacts"}, nodes.Select(n => n.Kind).ToArray());
        Assert.IsTrue(File.Exists(artifact.Files.Single()));
        Assert.AreEqual(frozen, (await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == previous.Id)).PlanSnapshotJson);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual("ADAPTIVE_MIGRATION_CHANGED", (await migration.ApplyAsync(second.Token)).Skipped.Single().ReasonCode);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => migration.ApplyAsync(preview.Token));
    }

    [DataTestMethod]
    [DataRow("missing")] [DataRow("foreign")] [DataRow("mixed")] [DataRow("new-download")] [DataRow("already-started")]
    public async Task Prepared_local_AI_requires_every_part_and_rejects_unowned_missing_and_active_work(string condition)
    {
        var (task, previous, original) = await Failed();
        var artifact = await ProcessedInput(previous.Id, original);
        var saved = await Plans.ReplanAsync(task.Id, ContinueInput(task, previous.Id, artifact.Id), default);
        var row = await Db.AcquisitionTasks.SingleAsync(t => t.Id == task.Id);
        var plan = saved.Plan!; var part = plan.Parts.Single();
        switch (condition)
        {
            case "missing": File.Delete(artifact.Files.Single()); break;
            case "foreign": plan = plan with {Parts = [part with {ContinueFrom = part.ContinueFrom! with {TaskId = task.Id + 1}}]}; break;
            case "mixed": plan = plan with {Parts = [part, part with {Id = "other", ContinueFrom = null}]}; break;
            case "new-download": plan = plan with {Parts = [part with {ContinueFrom = null}]}; break;
            case "already-started": Db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = previous.Id, Module = "acquisition",
                EntityKind = "task", EntityId = task.Id.ToString(), Revision = row.PlanRevision.ToString()}); break;
        }
        row.PlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options); await Db.SaveChangesAsync();
        var service = new AcquisitionAdaptiveMigrationService(Db, null!, Plans);
        var preview = await service.PreviewAsync(new([task.Id]));
        Assert.IsFalse(preview.Rows.Single().CanMigrate);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Prepared_local_AI_start_failure_can_retry_the_same_saved_revision_after_input_is_restored()
    {
        var (task, old, original) = await Failed();
        var artifact = await ProcessedInput(old.Id, original);
        var path = artifact.Files.Single();
        var bytes = await File.ReadAllBytesAsync(path);
        var saved = await Plans.ReplanAsync(task.Id, ContinueInput(task, old.Id, artifact.Id), default);
        var service = new AcquisitionAdaptiveMigrationService(Db, null!, Plans);
        var preview = await service.PreviewAsync(new([task.Id]));
        await File.WriteAllBytesAsync(path, bytes.Select(b => (byte)(b ^ 1)).ToArray());
        var result = await service.ApplyAsync(preview.Token);
        Assert.AreEqual("PLAN_REPLAN_EVIDENCE_UNAVAILABLE", result.Skipped.Single().Reason);
        var interrupted = (await Plans.GetAsync(task.Id, default))!;
        Assert.AreEqual(saved.Task.PlanRevision + 1, interrupted.Task.PlanRevision);
        Assert.IsNull(interrupted.Task.WorkflowRunId);
        Assert.AreEqual("ready", interrupted.Task.Phase);
        await File.WriteAllBytesAsync(path, bytes);
        var again = await service.PreviewAsync(new([task.Id]));
        Assert.AreEqual(1, again.CanMigrateCount);
        CollectionAssert.AreEqual(new[] {task.Id}, (await service.ApplyAsync(again.Token)).MigratedTaskIds.ToArray());
        var finished = (await Plans.GetAsync(task.Id, default))!;
        Assert.IsNotNull(finished.Task.WorkflowRunId);
        Assert.AreEqual(interrupted.Task.PlanRevision, finished.Task.PlanRevision);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Prepared_local_AI_preview_is_stale_when_the_saved_plan_or_existing_files_change()
    {
        var (task, old, original) = await Failed();
        var artifact = await ProcessedInput(old.Id, original);
        var saved = await Plans.ReplanAsync(task.Id, ContinueInput(task, old.Id, artifact.Id), default);
        var service = new AcquisitionAdaptiveMigrationService(Db, null!, Plans);
        var preview = await service.PreviewAsync(new([task.Id]));
        Assert.IsTrue(preview.Rows.Single().CanMigrate);
        File.Delete(artifact.Files.Single());
        var skipped = (await service.ApplyAsync(preview.Token, [task.Id])).Skipped.Single();
        Assert.IsFalse(skipped.CanMigrate);
        var after = await Plans.GetAsync(task.Id, default);
        Assert.IsNull(after!.Task.WorkflowRunId);
        Assert.AreEqual(saved.Task.PlanRevision, after.Task.PlanRevision);
    }
}
