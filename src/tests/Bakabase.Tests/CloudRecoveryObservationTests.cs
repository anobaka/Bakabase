using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class CloudWorkflowTests
{
    [TestMethod]
    public async Task Diagnostic_observation_never_restarts_a_failed_download_even_if_it_changed_after_preview()
    {
        var target = Chosen(Source("recovery-failed", "googleDrive")); var item = Workset(target.SelectedSource!);
        target = target with {DownloadTaskId = await Operations.EnqueueAsync(7201, target, item.OutputDirectory, default)};
        await _services.GetRequiredService<DownloadTaskService>().Stop(t => t.Id == target.DownloadTaskId);
        await Db.DownloadTasks.Where(t => t.Id == target.DownloadTaskId).ExecuteUpdateAsync(update => update
            .SetProperty(t => t.Status, DownloadTaskDbModelStatus.Failed).SetProperty(t => t.Message, "Retained failure"));
        item = item with {Targets = [target]};
        var result = await new AwaitCloudDownloadsActivity().ResumeAsync(Context(item), item, "{\"observeExistingOnly\":true}", default);
        Assert.IsNotNull(result.Suspension);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await Db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(1, await Db.DownloadTasks.CountAsync()); Assert.AreEqual(0, _google.DownloadCalls);
    }

    [TestMethod]
    public async Task Diagnostic_enqueue_requires_existing_verified_result_and_does_not_create_a_new_download()
    {
        var target = Chosen(Source("recovery-new", "googleDrive")); var item = Workset(target.SelectedSource!) with {Targets = [target]};
        var result = await new EnqueueCloudSourcesActivity().ResumeAsync(Context(item), item, "{\"observeExistingOnly\":true}", default);
        Assert.IsNotNull(result.Suspension); StringAssert.Contains(Item(result).Targets.Single().Problem!, "RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync()); Assert.AreEqual(0, await Db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(0, _google.DownloadCalls);
    }

    [TestMethod]
    public async Task Diagnostic_enqueue_does_not_reacquire_a_completed_generation_whose_file_changed()
    {
        var target = Chosen(Source("recovery-changed", "googleDrive")); var item = Workset(target.SelectedSource!);
        var id = await Operations.EnqueueAsync(7201, target, item.OutputDirectory, default);
        await _services.GetRequiredService<DownloadTaskService>().Stop(t => t.Id == id);
        var row = await Db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        Directory.CreateDirectory(row.DownloadPath); var file = Path.Combine(row.DownloadPath, "official.bin");
        await File.WriteAllTextAsync(file, "original"); var manifest = await ArtifactManifestFiles.CaptureAsync(row.DownloadPath, [file]);
        Db.DownloadResults.Add(new DownloadResultDbModel {DownloadTaskId = id, DownloadDirectory = row.DownloadPath,
            Path = row.DownloadPath, FilesJson = JsonSerializer.Serialize(new[] {file}), Fingerprint = manifest.Fingerprint});
        await Db.SaveChangesAsync(); await Db.DownloadTasks.Where(t => t.Id == id).ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, DownloadTaskDbModelStatus.Complete));
        await File.WriteAllTextAsync(file, "user modified original"); item = item with {Targets = [target]};
        var result = await new EnqueueCloudSourcesActivity().ResumeAsync(Context(item), item, "{\"observeExistingOnly\":true}", default);
        Assert.IsNotNull(result.Suspension); Assert.AreEqual(1, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual(id, (await Db.CloudDownloadOperations.AsNoTracking().SingleAsync()).DownloadTaskId);
        Assert.AreEqual("user modified original", await File.ReadAllTextAsync(file));
    }

    [TestMethod]
    public async Task Diagnostic_constraint_survives_transfer_and_prevents_later_enqueue_replacement_without_a_signal()
    {
        var target = Chosen(Source("recovery-later", "googleDrive")); var item = Workset(target.SelectedSource!);
        var id = await Operations.EnqueueAsync(7201, target, item.OutputDirectory, default);
        await _services.GetRequiredService<DownloadTaskService>().Stop(t => t.Id == id);
        await Db.DownloadTasks.Where(t => t.Id == id).ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, DownloadTaskDbModelStatus.Complete));
        // A completed transport without its expected files must not be replaced by another generation.
        item = item with {Targets = [target]};
        var transferred = Item(await new BaiduTransferActivity().ResumeAsync(Context(item), item, "{\"observeExistingOnly\":true}", default));
        Assert.IsTrue(transferred.NoRepeatExistingDownloads == true);
        var restored = (DownloadWorkset)Bakabase.Modules.Workflow.Components.WorkflowItemSnapshot.Restore(
            Bakabase.Modules.Workflow.Components.WorkflowItemSnapshot.Capture(transferred));
        var next = await new EnqueueCloudSourcesActivity().ProcessItemAsync(Context(restored), restored, default);
        Assert.IsNotNull(next.Suspension); Assert.AreEqual(1, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual(id, (await Db.CloudDownloadOperations.AsNoTracking().SingleAsync()).DownloadTaskId);
        Assert.IsTrue(Item(next).NoRepeatExistingDownloads == true);
    }

    [TestMethod]
    public async Task Persistent_recovery_policy_allows_first_download_and_reuses_that_identity_without_restarting_it()
    {
        var target = Chosen(Source("recovery-first", "googleDrive"));
        var item = Workset(target.SelectedSource!) with {Targets = [target], NoRepeatExistingDownloads = true};
        var first = Item(await new EnqueueCloudSourcesActivity().ProcessItemAsync(Context(item), item, default));
        Assert.IsNotNull(first.Targets[0].DownloadTaskId); Assert.AreEqual(1, await Db.DownloadTasks.CountAsync());
        var second = Item(await new EnqueueCloudSourcesActivity().ProcessItemAsync(Context(first), first, default));
        Assert.AreEqual(first.Targets[0].DownloadTaskId, second.Targets[0].DownloadTaskId);
        Assert.AreEqual(1, await Db.DownloadTasks.CountAsync());
        await _services.GetRequiredService<DownloadTaskService>().Stop(t => t.Id == first.Targets[0].DownloadTaskId);
        var stopped = await new EnqueueCloudSourcesActivity().ProcessItemAsync(Context(first), first, default);
        Assert.IsNotNull(stopped.Suspension); Assert.AreEqual(1, await Db.DownloadTasks.CountAsync());
    }
}
