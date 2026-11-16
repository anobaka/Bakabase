using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private ArtifactDeliveryCleanupAuthorization BoundaryAuthorization(ArtifactRecord publication) => new(
        new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, 1, 1, 1, true, [1],
            [new(publication.Id, publication.OperationId, publication.Fingerprint, publication.RootDirectory)]),
        _ => Task.FromResult(true), store, boundaryCoverage: cleanup.HasBoundaryCoverageAsync);

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Deletion_boundary_coverage_reclaims_four_stage_lineage_in_one_reviewed_plan(bool skip)
    {
        var f = await BoundaryFixture();
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "part:artifact.publish", f.Publication.OperationId));
        var task = new AcquisitionTaskDbModel {Title = "Four-stage fixture", Status = AcquisitionStatus.Completed, WorkflowRunId = 1, PlanRevision = 1};
        db.AcquisitionTasks.Add(task); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var extraction = new NoDeletionExtraction();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root);
        var service = new AcquisitionTaskDeletionService(db, new FinishedDeletionActivity(), new(),
            new(db, store, maintenance, cleanup, new Storage()));
        var preview = await service.PreviewAsync(new([task.Id], true, true, SkipFileVerification: skip));
        var groups = preview.Rows.Single().Cleanup!.Groups;
        Assert.IsFalse(groups.Any(g => !g.Eligible), string.Join(",", groups.Where(g => !g.Eligible).Select(g => g.ReasonCode)));
        Assert.IsTrue(groups.Any(g => g.ArtifactId == f.Boundary.Id && g.Eligible));
        Assert.IsTrue(groups.Any(g => g.ArtifactId == f.Raw.Id && g.Eligible));
        var result = await service.DeleteAsync(new(preview.PreviewId));
        CollectionAssert.AreEqual(new[] {task.Id}, result.DeletedTaskIds.ToArray());
        foreach (var artifact in new[] {f.Raw, f.Boundary, f.Anchor})
        {
            Assert.AreEqual(ArtifactLocationState.Retired, (await store.GetArtifactAsync(artifact.Id))!.State);
            Assert.IsFalse(File.Exists(artifact.Files.Single()));
        }
        Assert.IsTrue(File.Exists(f.Publication.Files.Single()));
        Assert.IsTrue(File.Exists(f.State));
        Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 1));
    }

    [TestMethod]
    public async Task Planned_boundary_prerequisite_requires_actual_durable_retirement_not_a_missing_file_noop()
    {
        var f = await BoundaryFixture(); var authorization = BoundaryAuthorization(f.Publication);
        var rows = await cleanup.PreviewForDeletionAsync([f.Publication.Id], [1], authorization, default);
        var raw = rows.Single(r => r.ArtifactId == f.Raw.Id);
        Assert.IsTrue(raw.Eligible, raw.Reason);
        Assert.AreEqual(f.Boundary.Id, authorization.DependenciesFor(f.Raw.Id).Single().ArtifactId);
        Assert.AreEqual("ReferencedDescendant", (await cleanup.ReclaimForDeletionAsync(raw, [1], authorization, default)).Reason);
        // Neither an unavailable file nor a bare Retired row can impersonate the fixed
        // boundary's normal deletion receipt. No new deletion follows such a no-op.
        File.Delete(f.Boundary.Files.Single());
        Assert.AreEqual("ReferencedDescendant", (await cleanup.ReclaimForDeletionAsync(raw, [1], authorization, default)).Reason);
        await db.Artifacts.Where(a => a.Id == f.Boundary.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ArtifactLocationState.Retired));
        Assert.AreEqual("ReferencedDescendant", (await cleanup.ReclaimForDeletionAsync(raw, [1], authorization, default)).Reason);
        Assert.IsTrue(File.Exists(f.Raw.Files.Single()));
    }

    [TestMethod]
    public async Task Planned_boundary_dependencies_survive_authorization_recreation_but_never_ignore_shared_children()
    {
        var f = await BoundaryFixture(); var previewAuthorization = BoundaryAuthorization(f.Publication);
        var rows = await cleanup.PreviewForDeletionAsync([f.Publication.Id], [1], previewAuthorization, default);
        var executionAuthorization = BoundaryAuthorization(f.Publication);
        executionAuthorization.RestoreRetirementDependencies(previewAuthorization.RetirementDependencies);
        foreach (var row in rows.Where(r => r.Eligible))
            Assert.IsTrue((await cleanup.ReclaimForDeletionAsync(row, [1], executionAuthorization, default)).Completed, row.ArtifactId);
        Assert.AreEqual(ArtifactLocationState.Retired, (await store.GetArtifactAsync(f.Raw.Id))!.State);
    }

    [TestMethod]
    public async Task Shared_or_changed_boundary_is_not_a_planned_retirement_exception()
    {
        var f = await BoundaryFixture();
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "borrowed", f.Boundary.OperationId));
        var authorization = BoundaryAuthorization(f.Publication);
        var rows = await cleanup.PreviewForDeletionAsync([f.Publication.Id], [1], authorization, default);
        Assert.AreEqual("SharedOwner", rows.Single(r => r.ArtifactId == f.Boundary.Id).Reason);
        Assert.AreEqual("ReferencedDescendant", rows.Single(r => r.ArtifactId == f.Raw.Id).Reason);
        Assert.AreEqual(0, authorization.DependenciesFor(f.Raw.Id).Count);
        Assert.IsTrue(File.Exists(f.Raw.Files.Single()));
    }

    [TestMethod]
    public async Task Private_boundary_coverage_requires_the_exact_executor_ledger_even_after_delivery_acceptance()
    {
        var f = await BoundaryFixture(); var authorization = BoundaryAuthorization(f.Publication);
        Assert.IsTrue(await authorization.CoversAsync(f.Boundary, default));
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(f.State), Json)!;
        state.FileTransitions.Clear();
        await File.WriteAllTextAsync(f.State, JsonSerializer.Serialize(state, Json));
        Assert.IsFalse(await authorization.CoversAsync(f.Boundary, default));
        var rows = await cleanup.PreviewForDeletionAsync([f.Publication.Id], [1], authorization, default);
        Assert.IsFalse(rows.Single(r => r.ArtifactId == f.Boundary.Id).Eligible);
        Assert.AreEqual("ReferencedDescendant", rows.Single(r => r.ArtifactId == f.Raw.Id).Reason);
    }
}
