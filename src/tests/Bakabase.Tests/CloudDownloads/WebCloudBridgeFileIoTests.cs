using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.WebProviders;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class WebCloudBridgeFileIoTests
{
    [TestMethod]
    public void Only_open_writers_provide_measurements_and_each_file_has_its_own_baseline()
    {
        var provider = new FileIoObservationProvider();
        using var scope = FileIoObservationScope.Enter(provider, new("downloadTask", "test-task"));
        using var writes = WebCloudProvider.ObserveFileWrites();

        Record(writes, """{"state":"written","writtenBytes":28}""");
        Assert.AreEqual(0, provider.Drain().Count);

        Record(writes, """{"state":"opened"}""");
        CheckWrites(0);
        Record(writes, """{"state":"written","writtenBytes":28}""");
        Record(writes, """{"state":"written","writtenBytes":28}""");
        Record(writes, """{"state":"written","writtenBytes":10}""");
        Record(writes, """{"state":"opened"}""");
        Record(writes, """{"state":"written","writtenBytes":31}""");
        CheckWrites(31);
        Record(writes, """{"state":"closed"}""");
        CheckWrites(0);
        Assert.AreEqual(0, provider.Drain().Count);

        Record(writes, """{"state":"opened"}""");
        Record(writes, """{"state":"written","writtenBytes":3}""");
        writes.Dispose();
        CheckWrites(3);
        Assert.AreEqual(0, provider.Drain().Count);

        void CheckWrites(long expected)
        {
            var sample = provider.Drain().Single();
            Assert.AreEqual("test-task", sample.OwnerId);
            Assert.IsTrue(sample.WriteObserved);
            Assert.IsFalse(sample.ReadObserved);
            Assert.AreEqual(expected, sample.WrittenBytes);
        }
    }

    [TestMethod]
    public void Network_payload_is_attributed_to_its_site_once_across_retries()
    {
        var provider = new FileIoObservationProvider();
        using var scope = FileIoObservationScope.Enter(provider, new("downloadTask", "71"), "site:71");
        using var receives = WebCloudProvider.ObserveNetworkReceives(FileIoObservationScope.NetworkDownloadId!);
        Record(receives, """{"state":"opened"}""");
        Record(receives, """{"state":"received","receivedBytes":14}""");
        Record(receives, """{"state":"received","receivedBytes":14}""");
        Record(receives, """{"state":"closed"}""");
        Record(receives, """{"state":"opened"}""");
        Record(receives, """{"state":"received","receivedBytes":4}""");
        Record(receives, """{"state":"closed"}""");
        var sample = provider.Drain().Single();
        Assert.AreEqual(new FileIoObservationSample("downloadTask", "71", 0, 0, false, false,
            NetworkReceivedBytes: 18, NetworkObserved: true, NetworkDownloadId: "site:71"), sample);
        Assert.AreEqual(0, provider.Drain().Count);
    }

    [TestMethod]
    [DataRow("pixeldrain", null, true, true)]
    [DataRow("gofile", null, true, true)]
    [DataRow("googleDrive", null, true, false)]
    [DataRow("pixeldrain", "inspectReuseSource", true, false)]
    [DataRow("pixeldrain", null, false, false)]
    public void Only_supported_download_requests_opt_in(string type, string? mode, bool enabled, bool expected)
    {
        var source = new CloudSource {Id = "source", ProviderId = type, Url = "https://example.invalid/share"};
        var request = new CloudDownloadRequest("operation", source, null, null, Path.GetTempPath());
        using var json = JsonDocument.Parse(WebCloudProvider.SerializeRequest(type, request, Path.GetTempPath(), null,
            mode: mode, observeFileIo: enabled, observeNetwork: enabled));

        Assert.AreEqual(expected, json.RootElement.TryGetProperty("observeFileIo", out var observe));
        Assert.AreEqual(expected, json.RootElement.TryGetProperty("observeNetwork", out var network));
        if (expected) Assert.IsTrue(observe.GetBoolean() && network.GetBoolean());
    }

    private static void Record<T>(WebCloudProvider.BridgeByteObservation<T> writes, string json) where T : class, IDisposable
    {
        using var document = JsonDocument.Parse(json);
        writes.Record(document.RootElement);
    }
}
