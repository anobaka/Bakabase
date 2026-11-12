using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class CloudWorkflowTests
{
    [TestMethod]
    public async Task Owned_container_metadata_keeps_legacy_transfer_and_download_operation_ids_and_frozen_requests()
    {
        var target = Chosen(Source("ownership-metadata", "baidu"), "account-a");
        var receipt = await Operations.TransferAsync(7201, target, "account-a", "/Bakabase", default);
        target = target with {RemoteEntry = receipt.Entry};
        var output = Path.Combine(_root, "ownership-metadata");
        var first = await Operations.EnqueueAsync(7201, target, output, default);
        var before = await Db.CloudDownloadOperations.AsNoTracking().OrderBy(o => o.Id)
            .Select(o => new {o.Id, o.RequestJson, o.ReceiptJson}).ToArrayAsync();
        var enriched = target with {RemoteEntry = target.RemoteEntry! with
            {OwnedContainer = new(receipt.OperationId, receipt.Entry.Path)}};

        Assert.AreEqual(DownloadArtifactCoordinator.DownloadIntent(target, output),
            DownloadArtifactCoordinator.DownloadIntent(enriched, output));
        var reusedTransfer = await Operations.TransferAsync(7201, enriched, "account-a", "/Bakabase", default);
        Assert.AreEqual(receipt.OperationId, reusedTransfer.OperationId);
        Assert.AreEqual(first, await Operations.EnqueueAsync(7201, enriched, output, default));
        Assert.AreEqual(1, _baidu.TransferRequests.Count);
        Assert.AreEqual(0, _baidu.DownloadCalls);
        Assert.AreEqual(1, await Db.DownloadTasks.CountAsync());
        var after = await Db.CloudDownloadOperations.AsNoTracking().OrderBy(o => o.Id)
            .Select(o => new {o.Id, o.RequestJson, o.ReceiptJson}).ToArrayAsync();
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
    }

    [TestMethod]
    public async Task Download_checkpoint_rejects_changed_source_and_destination_but_accepts_same_values()
    {
        var first = Source("checkpoint-first", "baidu", "group");
        var other = Source("checkpoint-other", "baidu", "group");
        var directory = Path.Combine(_root, "checkpoint");
        var target = Chosen(first, "account-a") with {Sources = [first, other], DownloadTaskId = 123};
        var workset = new DownloadWorkset {OutputDirectory = directory, Targets = [target]};
        var changed = await new SelectCloudSourcesActivity().ProcessItemAsync(
            Context(workset, new() {SourceIds = [other.Id]}), workset, default);
        Assert.IsNotNull(changed.Suspension);
        Assert.AreEqual(first.Id, Item(changed).Targets.Single().SelectedSourceId);
        var differentDirectory = await new BaiduTransferActivity().ProcessItemAsync(
            Context(workset, new() {OutputDirectory = Path.Combine(_root, "other")}), workset, default);
        Assert.IsNotNull(differentDirectory.Suspension);
        Assert.AreEqual(directory, Item(differentDirectory).OutputDirectory);
        DownloadWorksetRouting.ValidateOutputDirectory(workset, directory + Path.DirectorySeparatorChar);
        Assert.AreNotEqual(DownloadArtifactCoordinator.DownloadIntent(target, directory),
            DownloadArtifactCoordinator.DownloadIntent(target with {Sources = [other], SelectedSourceId = other.Id}, directory));
        Assert.AreNotEqual(DownloadArtifactCoordinator.DownloadIntent(target, directory),
            DownloadArtifactCoordinator.DownloadIntent(target, Path.Combine(_root, "other")));
    }

    [TestMethod]
    public async Task Existing_artifact_checkpoint_cannot_silently_change_its_recorded_layout()
    {
        var source = Source("recorded-layout", "baidu");
        var target = Chosen(source, "account-a") with {DownloadArtifactId = "old-artifact"};
        var workset = new DownloadWorkset {OutputDirectory = Path.Combine(_root, "download"), Targets = [target]};
        var result = await new BaiduTransferActivity().ProcessItemAsync(
            Context(workset, new() {DownloadLayout = "separateDirectory"}), workset, default);
        Assert.IsNotNull(result.Suspension);
        Assert.AreEqual("old-artifact", Item(result).Targets.Single().DownloadArtifactId);
        Assert.IsNull(Item(result).Targets.Single().DownloadLayout);
        Assert.AreEqual(0, _baidu.TransferRequests.Count);
    }

    [TestMethod]
    [DataRow("proven-content-root")]
    [DataRow("legacy-physical-root")]
    [DataRow("outside-sidecar")]
    public async Task Completed_download_continues_with_saved_content_root_without_restarting_or_dropping_files(string mode)
    {
        var physical = Directory.CreateDirectory(Path.Combine(_root, "download")).FullName;
        var payload = Directory.CreateDirectory(Path.Combine(physical, "owned-container")).FullName;
        var authored = Directory.CreateDirectory(Path.Combine(payload, "author-album")).FullName;
        var file = Path.Combine(authored, "image.jpg");
        await File.WriteAllBytesAsync(file, [1, 2, 3]);
        var files = new List<string> {file};
        if (mode == "outside-sidecar")
        {
            var sidecar = Path.Combine(physical, "key.dat");
            await File.WriteAllBytesAsync(sidecar, [4, 5, 6]);
            files.Add(sidecar);
        }
        var task = new DownloadTaskDbModel
        {
            Key = "content-root", ThirdPartyId = ThirdPartyId.GoogleDrive, Type = 1,
            DownloadPath = physical, Status = DownloadTaskDbModelStatus.Complete
        };
        Db.DownloadTasks.Add(task);
        await Db.SaveChangesAsync();
        var savedRoot = mode == "legacy-physical-root" ? physical : payload;
        Db.DownloadResults.Add(new DownloadResultDbModel
        {
            DownloadTaskId = task.Id, ThirdPartyId = task.ThirdPartyId, SourceKey = task.Key,
            Name = "Fixture", Kind = DownloadResultKind.LocalFiles, Path = savedRoot, DownloadDirectory = savedRoot,
            DeduplicationKey = "payload-root-" + mode, FilesJson = JsonSerializer.Serialize(files)
        });
        await Db.SaveChangesAsync();
        var target = Chosen(Source("content-root", "googleDrive")) with {DownloadTaskId = task.Id};
        var workset = new DownloadWorkset {OutputDirectory = physical, Targets = [target]};

        var result = await new AwaitCloudDownloadsActivity().ProcessItemAsync(Context(workset), workset, default);

        Assert.IsNull(result.Suspension);
        var continued = Item(result).Targets.Single();
        Assert.AreEqual(mode == "proven-content-root" ? payload : physical, continued.DownloadDirectory);
        CollectionAssert.AreEqual(files, continued.Files);
        Assert.IsTrue(files.All(File.Exists));
        Assert.AreEqual(1, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual(0, _google.DownloadCalls);
        Assert.AreEqual(physical, (await Db.DownloadTasks.AsNoTracking().SingleAsync()).DownloadPath);
    }
}
