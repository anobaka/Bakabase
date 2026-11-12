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
    private async Task<(AcquisitionTaskDbModel Task, ArtifactRecord Source, ArtifactRecord Final,
        AcquisitionTaskDeletionService Service)> ManuallyFinishedDeletionFixture(string sourceKind)
    {
        var source = await Artifact(sourceKind);
        // The manual edit has a real source relationship, but no automatic per-file
        // replacement receipt. User acceptance must not be confused with such a receipt.
        var final = await Artifact("publishedFiles", source, contents: "manually rebuilt final bytes");
        Assert.IsFalse((await ArtifactFileCoverage.EvaluateAsync(store, source, [final.Id])).Complete);
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1",
            "part:artifact.publish", final.OperationId));
        var task = new AcquisitionTaskDbModel
        {
            Title = "Manually finished files", Status = AcquisitionStatus.Completed,
            WorkflowRunId = 1, PlanRevision = 2, DeletedAt = DateTime.UtcNow,
        };
        db.AcquisitionTasks.Add(task);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var extraction = new NoDeletionExtraction();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root);
        var service = new AcquisitionTaskDeletionService(db, new FinishedDeletionActivity(), new(),
            new(db, store, maintenance, cleanup, new Storage()));
        return (task, source, final, service);
    }

    [TestMethod]
    [DataRow("download", false)]
    [DataRow("download", true)]
    [DataRow("processedFiles", false)]
    [DataRow("processedFiles", true)]
    public async Task Explicit_accepted_skip_reclaims_registered_inputs_after_manual_processing_without_claiming_automatic_coverage(
        string sourceKind, bool skip)
    {
        var f = await ManuallyFinishedDeletionFixture(sourceKind);
        var unknown = Path.Combine(f.Source.RootDirectory, "unregistered.key");
        await File.WriteAllTextAsync(unknown, "keep user attachment");
        var deliveredElsewhere = Path.Combine(root, "accepted-final.bin");
        File.Move(f.Final.Files.Single(), deliveredElsewhere);

        Assert.IsFalse((await Preview(f.Final)).Single().Eligible, "Background cleanup still requires its own proof.");
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, skip, RetryRetainedFiles: true));
        Assert.AreEqual(skip, preview.Rows.Single().CanDelete);
        if (!skip)
            Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.Any(g => g.ReasonCode == ArtifactFileCoverage.IncompleteCode));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.AreEqual(skip ? 1 : 0, result.RemovedFiles);
        Assert.AreEqual(!skip, File.Exists(f.Source.Files.Single()));
        Assert.AreEqual("manually rebuilt final bytes", await File.ReadAllTextAsync(deliveredElsewhere));
        Assert.AreEqual("keep user attachment", await File.ReadAllTextAsync(unknown));
        Assert.AreEqual(f.Task.DeletedAt, await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).Select(t => t.DeletedAt).SingleAsync());
        Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 1));
        Assert.IsNotNull(await store.GetArtifactAsync(f.Source.Id));
    }

    [TestMethod]
    [DataRow("foreign-binding")]
    [DataRow("active-consumer")]
    public async Task Accepted_skip_still_rechecks_new_owners_and_consumers_after_coverage_preview(string change)
    {
        var f = await ManuallyFinishedDeletionFixture("processedFiles");
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, RetryRetainedFiles: true));
        Assert.IsTrue(preview.Rows.Single().CanDelete);
        if (change == "foreign-binding")
            Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "late-reader", f.Source.OperationId));
        else
            Assert.IsTrue(await store.AttachConsumerAsync(f.Source.OperationId, "active-reader", TimeSpan.FromMinutes(5)));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsTrue(File.Exists(f.Source.Files.Single()));
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }

    [TestMethod]
    public async Task Skipping_replacement_proof_requires_the_accepted_publication_in_the_fixed_consent()
    {
        var source = await Artifact("processedFiles");
        var final = await Artifact("publishedFiles", source, contents: "manual output");
        var other = await Artifact("publishedFiles", source, contents: "another output");
        ArtifactDeliveryCleanupAuthorization Authorization(bool accepted) => new(
            new("fixed", DateTimeOffset.UtcNow, 1, 1, 1, accepted, [1],
                [new(final.Id, final.OperationId, final.Fingerprint, final.RootDirectory)], true),
            _ => Task.FromResult(true), store);
        Assert.IsFalse(await Authorization(false).SkipsReplacementProofAsync(source, [final.Id], default));
        Assert.IsFalse(await Authorization(true).SkipsReplacementProofAsync(source, [other.Id], default));
        Assert.IsFalse(await Authorization(true).SkipsReplacementProofAsync(source, [], default));
        Assert.IsTrue(await Authorization(true).SkipsReplacementProofAsync(source, [final.Id], default));
    }
}
