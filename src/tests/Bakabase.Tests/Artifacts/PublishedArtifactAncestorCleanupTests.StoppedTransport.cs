using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [TestMethod]
    [DataRow("failed", true)]
    [DataRow("disabled", true)]
    [DataRow("foreign", false)]
    [DataRow("unproven", false)]
    [DataRow("running", false)]
    public async Task Completed_task_cleanup_validates_older_stopped_download_writer(string variant, bool allowed)
    {
        var f = await StageDeletion();
        var earlier = Directory.CreateDirectory(Path.Combine(f.Plan.OutputDirectory, "old-attempt")).FullName;
        var partialFile = Path.Combine(earlier, "partial.bin");
        await File.WriteAllTextAsync(partialFile, "earlier failed download");
        var request = new CloudDownloadRequest("older-native-operation", new()
            {ProviderId = "pixeldrain", Id = "file", Url = "https://pixeldrain.com/u/example"}, null, null, earlier);
        var raw = JsonSerializer.Serialize(request, JsonSerializerOptions.Web);
        db.DownloadTasks.Add(new() {Id = 77, DownloadPath = earlier, Options = raw,
            Status = variant switch {"running" => DownloadTaskDbModelStatus.InProgress,
                "disabled" => DownloadTaskDbModelStatus.Disabled, _ => DownloadTaskDbModelStatus.Failed}});
        db.CloudDownloadOperations.Add(new() {Id = request.OperationId, TargetId = "nested.part", Kind = "download",
            WorkflowRunId = 1, State = "started", DownloadTaskId = 77, RequestJson = variant == "unproven" ? "{}" : raw});
        if (variant == "foreign")
            db.DownloadResultOwners.Add(new() {DownloadTaskId = 77, WorkflowRunId = 2, AcquisitionTaskId = 5000});
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await f.Service.DeleteAsync(DirectDelete(f.Task.Id));

        Assert.AreEqual(allowed, !File.Exists(partialFile), JsonSerializer.Serialize(result.CleanupResults));
        Assert.AreEqual(allowed, result.CleanupResults?.SingleOrDefault()?.Completed == true);
        Assert.AreEqual(allowed, result.DeletedTaskIds.Contains(f.Task.Id));
        Assert.IsTrue(await db.DownloadTasks.AnyAsync(d => d.Id == 77));
    }

    [DataTestMethod]
    [DataRow("failed")]
    [DataRow("disabled")]
    [DataRow("unproven")]
    [DataRow("foreign")]
    [DataRow("running")]
    [DataRow("record-only")]
    public async Task Abandonment_releases_only_matching_exclusive_and_stopped_native_downloads(string variant)
    {
        var f = await UnfinishedStageDeletion();
        var request = new CloudDownloadRequest("native-operation", new()
            {ProviderId = "pixeldrain", Id = "file", Url = "https://pixeldrain.com/u/example"}, null, null, f.Download);
        var raw = JsonSerializer.Serialize(request, JsonSerializerOptions.Web);
        db.DownloadTasks.Add(new() {Id = 77, DownloadPath = f.Download, Options = raw,
            Status = variant == "running" ? DownloadTaskDbModelStatus.InProgress :
                variant == "disabled" ? DownloadTaskDbModelStatus.Disabled : DownloadTaskDbModelStatus.Failed});
        db.CloudDownloadOperations.Add(new() {Id = request.OperationId, TargetId = "nested.part",
            Kind = "download", WorkflowRunId = 1, State = "started", DownloadTaskId = 77,
            RequestJson = variant == "unproven" ? "{}" : raw});
        if (variant == "foreign")
            db.DownloadResultOwners.Add(new() {DownloadTaskId = 77, WorkflowRunId = 2, AcquisitionTaskId = 5000});
        // A stopped native task may also have registered a file in its own processing stage.
        // This must not grant ordinary per-artifact cleanup the same reclamation capability.
        db.DownloadTaskFiles.Add(new() {DownloadTaskId = 77, Path = Path.Combine(f.Processing, "manual.key"), Size = 1});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var plan = await f.Service.PreviewAsync(new([f.Task.Id], CleanupFiles: variant != "record-only", SkipFileVerification: variant != "record-only"));
        if (variant == "record-only")
        {
            Assert.IsNull(plan.Rows.Single().Cleanup);
            Assert.IsTrue(Directory.Exists(f.Download)); Assert.IsTrue(Directory.Exists(f.Processing));
            return;
        }
        var groups = plan.Rows.Single().Cleanup?.Groups;
        var expected = variant is "disabled" or "failed";
        Assert.AreEqual(expected, groups?.Any(g => g.Directory == f.Download && g.Eligible) == true);
        Assert.AreEqual(expected, groups?.Any(g => g.Directory == f.Processing && g.Eligible) == true);
        if (!expected) return;
        var result = await f.Service.DeleteAsync(new(plan.PreviewId));
        Assert.IsTrue(result.DeletedTaskIds.Contains(f.Task.Id), JsonSerializer.Serialize(result.CleanupResults));
        Assert.IsFalse(Directory.Exists(f.Download)); Assert.IsFalse(Directory.Exists(f.Processing));
        Assert.IsTrue(await db.DownloadTasks.AnyAsync(d => d.Id == 77));
        Assert.AreEqual(WorkflowRunStatus.Cancelled, await db.WorkflowRuns.Where(r => r.Id == 1).Select(r => r.Status).SingleAsync());
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
    }
}
