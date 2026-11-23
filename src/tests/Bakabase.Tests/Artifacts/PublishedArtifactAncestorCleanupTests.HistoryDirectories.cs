using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private async Task DeleteStageRecordOnlyAsync(StageDeletionFixture f)
    {
        var preview = await f.Service.PreviewAsync(new([f.Task.Id]));
        var deleted = await f.Service.DeleteAsync(new(preview.PreviewId));
        CollectionAssert.AreEqual(new[] {f.Task.Id}, deleted.DeletedTaskIds.ToArray());
    }

    [TestMethod]
    public async Task History_directory_selection_only_removes_the_reviewed_directory_and_keeps_history_incomplete_until_the_rest_is_gone()
    {
        var f = await StageDeletion();
        await DeleteStageRecordOnlyAsync(f);
        var options = new AcquisitionTaskDeletePreviewInput([f.Task.Id], CleanupFiles: true,
            AcceptDeliveredFiles: true, SkipFileVerification: true, RetryRetainedFiles: true);
        var preview = await f.Service.PreviewAsync(options);
        var groups = preview.Rows.Single().Cleanup!.Groups;
        Assert.IsTrue(groups.Any(g => g.Directory == f.Download && g.Eligible));
        Assert.IsTrue(groups.Any(g => g.Directory == f.Processing && g.Eligible));
        var input = new AcquisitionTaskDeleteInput(preview.PreviewId, RetryRetainedFiles: true,
            SelectedCleanupDirectories: new Dictionary<int, IReadOnlyList<string>> {[f.Task.Id] = [f.Processing]});
        var result = await f.Service.DeleteAsync(input);
        var outcome = result.CleanupResults!.Single();
        Assert.IsTrue(outcome.Completed, string.Join(",", outcome.ReasonCodes));
        var selected = groups.Single(g => g.Directory == f.Processing);
        Assert.AreEqual(selected.FileCount, result.RemovedFiles);
        Assert.AreEqual(selected.Bytes, result.RemovedBytes);
        CollectionAssert.Contains(outcome.UnselectedDirectories.ToArray(), f.Download);
        Assert.IsFalse(Directory.Exists(f.Processing));
        Assert.IsTrue(File.Exists(Path.Combine(f.Download, "original.bin")));
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
        var saved = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task.Id);
        Assert.AreEqual("incomplete", saved.DeletedIntermediateCleanup);
        Assert.AreEqual("notRequested", saved.DeletedDeliveryCleanup);
        Assert.IsNotNull(saved.DeletedAt);
        var replay = await f.Service.DeleteAsync(input);
        Assert.AreEqual(result.RemovedBytes, replay.RemovedBytes);
        var changed = await Assert.ThrowsExceptionAsync<ArgumentException>(() => f.Service.DeleteAsync(input with
            { SelectedCleanupDirectories = new Dictionary<int, IReadOnlyList<string>> {[f.Task.Id] = [f.Download]} }));
        Assert.AreEqual("ACQUISITION_DELETE_OPTIONS_CHANGED", changed.Message);
        var rest = await f.Service.PreviewAsync(options);
        var followUp = await f.Service.DeleteAsync(input with { PreviewId = rest.PreviewId,
            SelectedCleanupDirectories = new Dictionary<int, IReadOnlyList<string>> {[f.Task.Id] = [f.Download]} });
        Assert.IsTrue(followUp.CleanupResults!.Single().Completed);
        Assert.IsFalse(Directory.Exists(f.Download));
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
        var after = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task.Id);
        Assert.AreEqual("completed", after.DeletedIntermediateCleanup);
        Assert.AreEqual(saved.DeletedAt, after.DeletedAt);
    }

    [DataTestMethod]
    [DataRow("parent")]
    [DataRow("child")]
    [DataRow("foreignTask")]
    [DataRow("duplicate")]
    [DataRow("empty")]
    [DataRow("delivery")]
    [DataRow("direct")]
    [DataRow("activeRecord")]
    public async Task History_directory_selection_rejects_unreviewed_targets_without_consuming_confirmation(string sample)
    {
        var f = await StageDeletion();
        if (sample != "activeRecord") await DeleteStageRecordOnlyAsync(f);
        var retry = sample != "activeRecord";
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], CleanupFiles: true,
            AcceptDeliveredFiles: true, SkipFileVerification: true, RetryRetainedFiles: retry));
        IReadOnlyList<string> paths = sample switch
        {
            "parent" => [Path.GetDirectoryName(f.Download)!],
            "child" => [Path.Combine(f.Download, "original.bin")],
            "duplicate" => [f.Download, f.Download],
            "empty" => [],
            "delivery" => [f.Delivery],
            _ => [f.Download]
        };
        var selection = new Dictionary<int, IReadOnlyList<string>> {[sample == "foreignTask" ? f.Task.Id + 100 : f.Task.Id] = paths};
        var input = new AcquisitionTaskDeleteInput(preview.PreviewId, RetryRetainedFiles: retry,
            TaskIds: sample == "direct" ? [f.Task.Id] : null, SelectedCleanupDirectories: selection);
        var invalid = await Assert.ThrowsExceptionAsync<ArgumentException>(() => f.Service.DeleteAsync(input));
        Assert.AreEqual(AcquisitionHistoryCleanupSelection.Invalid, invalid.Message);
        Assert.AreEqual("preview", f.Service.GetProgress(preview.PreviewId).State);
        Assert.IsTrue(File.Exists(Path.Combine(f.Download, "original.bin")));
        Assert.IsTrue(Directory.Exists(f.Processing));
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
    }

    [TestMethod]
    public async Task History_directory_selection_rechecks_the_selected_files_and_access_before_any_removal()
    {
        var f = await StageDeletion();
        await DeleteStageRecordOnlyAsync(f);
        var options = new AcquisitionTaskDeletePreviewInput([f.Task.Id], CleanupFiles: true,
            AcceptDeliveredFiles: true, SkipFileVerification: true, RetryRetainedFiles: true);
        var preview = await f.Service.PreviewAsync(options);
        var selection = new Dictionary<int, IReadOnlyList<string>> {[f.Task.Id] = [f.Download]};
        var confirmed = new AcquisitionTaskDeleteInput(preview.PreviewId, RetryRetainedFiles: true,
            SelectedCleanupDirectories: selection);
        var denied = await f.Service.DeleteAsync(confirmed, guardDirectory: path =>
            path == f.Download ? throw new UnauthorizedAccessException() : Task.CompletedTask);
        Assert.AreEqual(0, denied.RemovedFiles);
        Assert.IsTrue(denied.Skipped.Any());
        Assert.IsTrue(Directory.Exists(f.Download));
        var next = await f.Service.PreviewAsync(options);
        await File.WriteAllTextAsync(Path.Combine(f.Download, "additional.bin"), "added after preview");
        var changed = await f.Service.DeleteAsync(confirmed with {PreviewId = next.PreviewId});
        Assert.IsFalse(changed.CleanupResults!.Single().Completed);
        Assert.IsTrue(File.Exists(Path.Combine(f.Download, "additional.bin")));
        Assert.IsTrue(File.Exists(Path.Combine(f.Download, "original.bin")));
        Assert.IsTrue(Directory.Exists(f.Processing));
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
    }

    [DataTestMethod]
    [DataRow("differentVolume")]
    [DataRow("blockedSelected")]
    [DataRow("blockedChild")]
    [DataRow("outsideFile")]
    [DataRow("ambiguousId")]
    public void History_directory_selection_uses_complete_server_groups_and_omits_an_unselected_tasks_waits(string scenario)
    {
        var selectedRoot = Path.Combine(root, "firstVolume");
        var otherRoot = Path.Combine(root, "secondVolume");
        var file = Path.Combine(scenario == "outsideFile" ? otherRoot : selectedRoot, "payload");
        var selected = new AcquisitionTaskDeleteCleanupGroup("selected", "intermediate", selectedRoot,
            scenario != "blockedSelected", null, [], [new(file, 1)], 1, 1);
        var other = new AcquisitionTaskDeleteCleanupGroup(scenario == "ambiguousId" ? "selected" : "other",
            "download", scenario == "blockedChild" ? Path.Combine(selectedRoot, "shared") : otherRoot,
            false, "ReferencedPath", [], [], 0, 0, Shared: true);
        var cleanup = new AcquisitionTaskDeleteCleanup(1, 1, [selected, other]);
        var plan = new AcquisitionTaskDeletionCleanupService.Plan(cleanup, "one", null, [], RetryRetainedFiles: true);
        var otherWait = new AcquisitionTaskDeleteSupersededRun(7, 1, 1);
        var waiting = plan with {Summary = cleanup with {SupersededRuns = [otherWait]}, TaskVersion = "two"};
        var preview = new AcquisitionTaskDeletePreview(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(1), 2,
            2, 0, [new(1, null, true, null, cleanup), new(2, null, true, null, waiting.Summary)],
            CleanupFiles: true, RetryRetainedFiles: true);
        var store = new AcquisitionTaskDeletePreviewStore();
        store.Add(new(preview, new Dictionary<int, string> {[1] = "one", [2] = "two"},
            new Dictionary<int, IReadOnlyList<int>> {[1] = [], [2] = [7]},
            new Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan> {[1] = plan, [2] = waiting}));
        var input = new AcquisitionTaskDeleteInput(preview.PreviewId, RetryRetainedFiles: true,
            SelectedCleanupDirectories: new Dictionary<int, IReadOnlyList<string>> {[1] = [selectedRoot], [2] = []});
        if (scenario == "differentVolume")
        {
            var entry = store.Claim(input);
            Assert.AreEqual(1, entry.Progress.FinishedTasks);
            CollectionAssert.AreEqual(new[] {1}, entry.SelectedPlans!.Keys.ToArray());
            var narrowed = entry.SelectedPlans[1];
            CollectionAssert.AreEqual(new[] {otherRoot}, narrowed.UnselectedDirectories.ToArray());
            Assert.AreEqual(1, narrowed.Summary.Groups.Count);
            Assert.IsNull(narrowed.Summary.SupersededRuns);
        }
        else
        {
            var failure = Assert.ThrowsException<ArgumentException>(() => store.Claim(input));
            Assert.AreEqual(AcquisitionHistoryCleanupSelection.Invalid, failure.Message);
            Assert.AreEqual("preview", store.ReadProgress(preview.PreviewId).State);
        }
    }
}
