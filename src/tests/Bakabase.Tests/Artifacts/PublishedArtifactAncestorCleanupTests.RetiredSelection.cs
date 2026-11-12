using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [TestMethod]
    public async Task Task_deletion_previews_the_private_archive_selection_not_the_retired_output_manifest()
    {
        var f = await PrivateResultFixture(true);
        Assert.AreEqual("final.txt", Path.GetFileName(f.Source.Manifest.Entries.Single().RelativePath));
        Assert.AreEqual("archive.zip", Path.GetFileName(f.Private));
        var task = new AcquisitionTaskDbModel {Title = "Retired private selection", Status = AcquisitionStatus.Completed,
            WorkflowRunId = 1, PlanRevision = 1};
        db.AcquisitionTasks.Add(task); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1",
            "nested.part:artifact.publish", f.Final.OperationId));
        var service = new AcquisitionTaskDeletionCleanupService(db, store, f.Maintenance, cleanup, new Storage());
        var authorizedPaths = new HashSet<string>(ArtifactManifestFiles.PathComparer);

        var plan = await service.PreviewAsync(task, [1], true, true, default,
            path => {authorizedPaths.Add(path); return Task.CompletedTask;});
        var group = plan.Summary.Groups.Single(g => g.ArtifactId == f.Source.Id && g.Eligible);
        var candidate = plan.Candidates.Single(c => c.ArtifactId == f.Source.Id && c.PublishedArtifactId != null);
        Assert.AreEqual(Path.GetDirectoryName(f.Private), group.Directory);
        Assert.AreEqual(1, group.FileCount);
        Assert.AreEqual(f.Private, group.Files.Single().Path);
        Assert.AreEqual(new FileInfo(f.Private).Length, group.Bytes);
        Assert.AreEqual(candidate.Bytes, group.Bytes);
        Assert.IsTrue(authorizedPaths.Contains(f.Private), "Authorization must cover the actual selected file.");
        Assert.AreNotEqual("AlreadyAbsent", group.ReasonCode);
        Assert.IsFalse(plan.AlreadyAbsent!.Any(a => a.ArtifactId == f.Source.Id));
        Assert.AreEqual(candidate.ResultDirectory, f.Source.RootDirectory,
            "The display directory must not alter the cleanup's immutable source/checkpoint identity.");
        Assert.IsFalse(group.Files.Any(file => Path.GetFileName(file.Path) == "final.txt"));
        Assert.IsTrue(File.Exists(f.Private));
        Assert.IsTrue(f.Final.Files.All(File.Exists));
        Assert.IsTrue(f.Raw.Files.All(File.Exists));
        Assert.AreEqual(ArtifactLocationState.Retired, (await store.GetArtifactAsync(f.Source.Id))!.State);
    }
}
