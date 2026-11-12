using System.Text.Json;
using Bakabase.Service.Components.CloudDownloads.Baidu;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class BaiduBridgeProgressTests
{
    [TestMethod]
    public void VerificationActivityNeverBecomesDownloadedBytesOrCompletedProgress()
    {
        using var update = JsonDocument.Parse("""{"type":"progress","stage":"verifying","verifiedBytes":100,"verificationTotalBytes":100}""");
        var progress = BaiduCloudDownloadProvider.ParseProgress(update.RootElement);
        Assert.AreEqual("Verifying", progress.Stage);
        Assert.IsNull(progress.Percentage);
        Assert.IsNull(progress.CompletedBytes);
        Assert.IsNull(progress.TotalBytes);
    }

    [DataTestMethod]
    [DataRow("""{"completedBytes":25,"totalBytes":100}""")]
    [DataRow("""{"stage":"downloading","completedBytes":25,"totalBytes":100}""")]
    public void OldAndNewDownloadEventsPreserveRealAccounting(string json)
    {
        using var update = JsonDocument.Parse(json);
        var progress = BaiduCloudDownloadProvider.ParseProgress(update.RootElement);
        Assert.AreEqual("Downloading", progress.Stage);
        Assert.AreEqual(25d, progress.Percentage);
        Assert.AreEqual(25L, progress.CompletedBytes);
        Assert.AreEqual(100L, progress.TotalBytes);
    }
}
