using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Tests;

public sealed partial class CloudWorkflowTests
{
    [TestMethod]
    public async Task Cloud_transfer_metrics_restart_after_verification_without_counting_reused_bytes_as_speed()
    {
        var output = Path.Combine(_root, "transfer-metrics");
        Directory.CreateDirectory(output);
        var source = Source("metrics", "googleDrive");
        var task = new DownloadTask
        {
            Id = 701, Key = source.Id, Type = 1, ThirdPartyId = ThirdPartyId.GoogleDrive, DownloadPath = output,
            Options = JsonSerializer.Serialize(new CloudDownloadRequest("fixture-metrics", source, null, null, output),
                JsonSerializerOptions.Web)
        };
        using var downloader = new GoogleCloudDownloader(_services);
        var baseType = typeof(AbstractDownloader<GoogleCloudTaskType>);
        baseType.GetProperty(nameof(downloader.Status))!.SetValue(downloader, DownloaderStatus.Downloading);
        var estimator = (DownloadSpeedEstimator)baseType.GetField("_speedEstimator", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(downloader)!;
        _google.DownloadResult = new([]); // No actual files; manifest rejection ends this callback-only fixture.
        _google.ProgressScript = async report =>
        {
            await report(new(null, Stage: "Preparing"));
            Assert.IsNull(downloader.TransferProgress);
            await report(new(10, 100, 1000, "Downloading"));
            Assert.IsFalse(estimator.HasRecentBytes, "The first report is an existing-byte baseline.");
            await report(new(20, 200, 1000, "Downloading"));
            Assert.IsTrue(estimator.HasRecentBytes);
            Assert.AreEqual(200L, downloader.TransferProgress!.CompletedBytes);

            await report(new(null, Stage: "Verifying"));
            Assert.IsNull(downloader.TransferProgress);
            Assert.IsNull(downloader.DownloadSpeedBytesPerSecond);
            Assert.IsFalse(estimator.HasRecentBytes, "Verification reads are not network traffic.");
            await report(new(null, Stage: "Reusing"));
            await report(new(80, 800, 1000, "Downloading"));
            Assert.IsFalse(estimator.HasRecentBytes, "Verified local files must start a fresh speed baseline.");
            await report(new(90, 900, 1000, "Downloading"));
            Assert.IsTrue(estimator.HasRecentBytes, "A new reporter must accept bytes after the previous phase reset.");
            Assert.AreEqual(900L, downloader.TransferProgress!.CompletedBytes);
        };
        var execute = typeof(GoogleCloudDownloader).GetMethod("StartCore", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => (Task)execute.Invoke(downloader, [task, CancellationToken.None])!);
        Assert.AreEqual(1, _google.DownloadCalls);
    }
}
