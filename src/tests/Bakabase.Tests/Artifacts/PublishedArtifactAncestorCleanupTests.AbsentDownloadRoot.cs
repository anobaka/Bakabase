using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
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
    private sealed class ObservedMount(string path) : IUserStoragePolicy
    {
        internal bool Available = true;
        internal string Path = path;
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            Available ? [new(Path, "fixture-volume", "bind", false)] : [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => true;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) { }
        public void EnsureTreeMutationAllowed(string path) { }
    }

    private async Task<(AcquisitionTaskDbModel Task, ArtifactRecord Source, ArtifactRecord Final,
        AcquisitionTaskDeletionService Service, ObservedMount Mount)> AbsentDownloadRootFixture()
    {
        var volume = Directory.CreateDirectory(Path.Combine(root, "mounted-volume")).FullName;
        var sourceRoot = Path.Combine(volume, "downloads", "allocated-task");
        var nested = Directory.CreateDirectory(Path.Combine(sourceRoot, "wrapper", "author")).FullName;
        var file = Path.Combine(nested, "payload.bin"); await File.WriteAllTextAsync(file, "same bytes");
        var source = await store.AdoptAsync(new("raw", "raw", "download", "fixture"),
            new(sourceRoot, [file], "download", true, [], null), "fixture");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "part:artifact.download", source.OperationId));
        var final = await Artifact("publishedFiles", source);
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "part:artifact.publish", final.OperationId));
        var task = new AcquisitionTaskDbModel {Title = "Missing completed download", Status = AcquisitionStatus.Completed,
            WorkflowRunId = 1, PlanRevision = 1};
        db.AcquisitionTasks.Add(task); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var extraction = new NoDeletionExtraction();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root, null);
        var mounted = new ObservedMount(volume);
        var files = new AcquisitionTaskDeletionCleanupService(db, store, maintenance, cleanup, mounted);
        return (task, source, final, new(db, new FinishedDeletionActivity(), new(), files), mounted);
    }

    [TestMethod]
    public async Task Removed_nested_download_parents_with_readable_registered_root_are_an_audited_zero_deletion_noop()
    {
        var f = await AbsentDownloadRootFixture(); Directory.Delete(Path.Combine(f.Source.RootDirectory, "wrapper"), true);
        var original = await store.GetArtifactAsync(f.Source.Id);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single();
        Assert.AreEqual("AlreadyAbsent", group.ReasonCode); Assert.IsTrue(group.Eligible);
        Assert.AreEqual(0L, preview.CleanupBytes); Assert.AreEqual(0, preview.CleanupFileCount);
        var deleted = await f.Service.DeleteAsync(new(preview.PreviewId));
        CollectionAssert.AreEqual(new[] {f.Task.Id}, deleted.DeletedTaskIds.ToArray());
        Assert.AreEqual(0, deleted.RemovedFiles); Assert.AreEqual(0L, deleted.RemovedBytes);
        var after = await store.GetArtifactAsync(f.Source.Id);
        Assert.AreEqual(original!.RootDirectory, after!.RootDirectory);
        Assert.AreEqual(original.State, after.State); Assert.AreEqual(original.Fingerprint, after.Fingerprint);
        Assert.IsTrue(Directory.Exists(f.Source.RootDirectory)); Assert.IsTrue(File.Exists(f.Final.Files.Single()));
        var receipts = Directory.GetFiles(Path.Combine(root, "file-processing", "workspace-cleanup-receipts"), "*.json");
        var receipt = JsonSerializer.Deserialize<ArtifactWorkspaceCleanupJournal.Receipt>(await File.ReadAllTextAsync(receipts.Single()), Json)!;
        Assert.AreEqual("AlreadyAbsent", receipt.Status);
        Assert.AreEqual(0, receipt.RemovedFiles); Assert.AreEqual(1, receipt.AlreadyAbsentPaths!.Count);
    }

    [DataTestMethod]
    [DataRow("not-mounted")]
    [DataRow("parent-still-mounted")]
    [DataRow("root-is-mount")]
    [DataRow("filesystem-root-only")]
    [DataRow("no-acceptance")]
    [DataRow("no-skip")]
    public async Task Missing_registered_download_root_is_unavailable_even_when_a_parent_mount_remains(string scenario)
    {
        var f = await AbsentDownloadRootFixture(); Directory.Delete(f.Source.RootDirectory, true);
        if (scenario == "not-mounted") f.Mount.Available = false;
        if (scenario == "root-is-mount") f.Mount.Path = f.Source.RootDirectory;
        if (scenario == "filesystem-root-only")
        {
            if (OperatingSystem.IsWindows()) Assert.Inconclusive("A Windows drive root is an independently enumerated volume.");
            f.Mount.Path = "/";
        }
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true,
            scenario != "no-acceptance", scenario is not ("no-skip" or "no-acceptance")));
        Assert.IsFalse(preview.Rows.Single().Cleanup!.Groups.Single().Eligible);
        Assert.AreEqual(scenario is "no-acceptance" or "no-skip" ? "FILE_CLEANUP_COVERAGE_INCOMPLETE" : "WorkspaceUnavailable",
            preview.Rows.Single().Cleanup!.Groups.Single().ReasonCode);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count); Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }

    [DataTestMethod]
    [DataRow("unmounted")]
    [DataRow("registered-root-gone-parent-mounted")]
    [DataRow("reappeared")]
    public async Task Nested_absence_noop_rechecks_registered_root_and_current_files_at_execution(string change)
    {
        var f = await AbsentDownloadRootFixture(); Directory.Delete(Path.Combine(f.Source.RootDirectory, "wrapper"), true);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true));
        Assert.AreEqual("AlreadyAbsent", preview.Rows.Single().Cleanup!.Groups.Single().ReasonCode);
        if (change == "unmounted") Directory.Move(f.Mount.Path, f.Mount.Path + "-offline");
        if (change == "registered-root-gone-parent-mounted") Directory.Delete(f.Source.RootDirectory);
        if (change == "reappeared")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(f.Source.Files.Single())!);
            await File.WriteAllTextAsync(f.Source.Files.Single(), "new user content");
        }
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count); Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
        Assert.AreEqual(ArtifactLocationState.Available, (await store.GetArtifactAsync(f.Source.Id))!.State);
        if (change == "reappeared") Assert.AreEqual("new user content", await File.ReadAllTextAsync(f.Source.Files.Single()));
    }

    [TestMethod]
    public async Task Symlink_instead_of_removed_download_root_is_not_absence()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("The symbolic-link fixture requires Unix.");
        var f = await AbsentDownloadRootFixture(); Directory.Delete(f.Source.RootDirectory, true);
        var elsewhere = Directory.CreateDirectory(Path.Combine(root, "unowned")).FullName;
        Directory.CreateSymbolicLink(f.Source.RootDirectory, elsewhere);
        try
        {
            Assert.IsFalse(ArtifactCleanupAbsence.AllConfirmedAbsent(f.Source.RootDirectory, f.Source.Files));
            Assert.IsTrue(Directory.Exists(elsewhere));
        }
        finally { Directory.Delete(f.Source.RootDirectory); }
    }
}
