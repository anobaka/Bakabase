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
        using var writes = new WebCloudProvider.BridgeFileWriteObservation();

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
            mode: mode, observeFileIo: enabled));

        Assert.AreEqual(expected, json.RootElement.TryGetProperty("observeFileIo", out var observe));
        if (expected) Assert.IsTrue(observe.GetBoolean());
    }

    private static void Record(WebCloudProvider.BridgeFileWriteObservation writes, string json)
    {
        using var document = JsonDocument.Parse(json);
        writes.Record(document.RootElement);
    }
}
