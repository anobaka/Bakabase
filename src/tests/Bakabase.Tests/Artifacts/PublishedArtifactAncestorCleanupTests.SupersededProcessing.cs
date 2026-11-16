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
    private async Task<(AcquisitionTaskDbModel Task, ArtifactRecord Parent, ArtifactRecord Old,
        ArtifactRecord Final, AcquisitionTaskDeletionService Service)> SupersededProcessingFixture()
    {
        var parent = await Artifact("processedFiles");
        var old = await Artifact("processedFiles", parent, contents: "old completed branch");
        var current = await Artifact("processedFiles", parent, contents: "new completed branch");
        var final = await Artifact("publishedFiles", current, contents: "accepted manual final");
        await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "part:artifact.publish", final.OperationId);
        var task = new AcquisitionTaskDbModel {Title = "Owned superseded processing", Status = AcquisitionStatus.Completed,
            WorkflowRunId = 1, PlanRevision = 3, DeletedAt = DateTime.UtcNow};
        db.AcquisitionTasks.Add(task); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var extraction = new NoDeletionExtraction();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root);
        return (task, parent, old, final, new(db, new FinishedDeletionActivity(), new(),
            new(db, store, maintenance, cleanup, new Storage())));
    }

    [TestMethod]
    public async Task Accepted_skipped_cleanup_reclaims_owned_superseded_branch_before_its_parent()
    {
        var f = await SupersededProcessingFixture();
        var unknown = Path.Combine(f.Old.RootDirectory, "user-added.key");
        await File.WriteAllTextAsync(unknown, "preserve unknown file");
        var strict = await f.Service.PreviewAsync(new([f.Task.Id], true, true, RetryRetainedFiles: true));
        Assert.IsFalse(strict.Rows.Single().Cleanup!.Groups.Any(g => g.ArtifactId == f.Old.Id && g.Eligible));
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, RetryRetainedFiles: true));
        var groups = preview.Rows.Single().Cleanup!.Groups;
        Assert.IsTrue(groups.Any(g => g.ArtifactId == f.Old.Id && g.Eligible));
        Assert.IsTrue(groups.Any(g => g.ArtifactId == f.Parent.Id && g.Eligible));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.IsTrue(result.CleanupResults!.Single().Completed,
            string.Join(",", result.CleanupResults.Single().ReasonCodes));
        Assert.IsFalse(File.Exists(f.Old.Files.Single()));
        Assert.IsFalse(File.Exists(f.Parent.Files.Single()));
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
        Assert.AreEqual("preserve unknown file", await File.ReadAllTextAsync(unknown));
        Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 1));
        Assert.AreEqual(f.Task.DeletedAt, await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).Select(t => t.DeletedAt).SingleAsync());
    }

    [DataTestMethod]
    [DataRow("foreign-owner")]
    [DataRow("foreign-descendant")]
    [DataRow("active-consumer")]
    public async Task Superseded_branch_keeps_external_owners_descendants_and_active_consumers(string protection)
    {
        var f = await SupersededProcessingFixture();
        if (protection == "foreign-owner")
            await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "outside", f.Old.OperationId);
        if (protection == "foreign-descendant")
            await Artifact("processedFiles", f.Old, owner: 2);
        if (protection == "active-consumer")
            await store.AttachConsumerAsync(f.Old.OperationId, "active-reader", TimeSpan.FromMinutes(5));
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, RetryRetainedFiles: true));
        var groups = preview.Rows.Single().Cleanup!.Groups;
        Assert.IsFalse(groups.Single(g => g.ArtifactId == f.Old.Id && g.Kind == "processedFiles").Eligible);
        Assert.IsFalse(groups.Single(g => g.ArtifactId == f.Parent.Id && g.Kind == "processedFiles").Eligible);
        await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.IsTrue(File.Exists(f.Old.Files.Single()));
        Assert.IsTrue(File.Exists(f.Parent.Files.Single()));
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }

    [TestMethod]
    public async Task New_external_binding_after_superseded_branch_preview_prevents_its_deletion()
    {
        var f = await SupersededProcessingFixture();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, RetryRetainedFiles: true));
        await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "late-reader", f.Old.OperationId);
        await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.IsTrue(File.Exists(f.Old.Files.Single()));
        Assert.IsTrue(File.Exists(f.Parent.Files.Single()));
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }
}
