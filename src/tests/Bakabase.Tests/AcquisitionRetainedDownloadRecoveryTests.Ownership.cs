using System.Text.Json;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [TestMethod]
    [DataRow("target")]
    [DataRow("configured")]
    [DataRow("operation")]
    [DataRow("transfer")]
    public async Task Retained_recovery_ignores_only_owned_container_layout_metadata_without_rewriting_history(string location)
    {
        var (run, target, download, operation, transfer, provider, service) = await SeedRetainedDownload();
        var entry = target.RemoteEntry! with {OwnedContainer = new("transfer", target.RemoteEntry!.Path)};
        var originalIntent = DownloadArtifactCoordinator.DownloadIntent(target, _root);
        switch (location)
        {
            case "target": target = target with {RemoteEntry = entry}; break;
            case "configured":
                var configured = JsonSerializer.Deserialize<CloudDownloadRequest>(download.Options!, JsonSerializerOptions.Web)!;
                download.Options = JsonSerializer.Serialize(configured with {RemoteEntry = entry}, JsonSerializerOptions.Web); break;
            case "operation":
                var saved = JsonSerializer.Deserialize<CloudDownloadRequest>(operation.RequestJson, JsonSerializerOptions.Web)!;
                operation.RequestJson = JsonSerializer.Serialize(saved with {RemoteEntry = entry}, JsonSerializerOptions.Web); break;
            case "transfer":
                transfer.ReceiptJson = JsonSerializer.Serialize(new CloudTransferReceipt("transfer", entry), JsonSerializerOptions.Web); break;
        }
        await _db.SaveChangesAsync();
        var before = JsonSerializer.Serialize(new {download, operation, transfer});

        var preview = await service.CheckAsync(run.Id, target, _root, true, default);

        Assert.IsTrue(preview.CanRetry, preview.ReasonCode);
        Assert.AreEqual(originalIntent, DownloadArtifactCoordinator.DownloadIntent(target, _root));
        CollectionAssert.AreEqual(new[] {true, false}, provider.Checks);
        Assert.AreEqual(before, JsonSerializer.Serialize(new {download, operation, transfer}));
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(2, await _db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual("bytes", await File.ReadAllTextAsync(Path.Combine(download.DownloadPath, "retained-fixture")));
    }
}
