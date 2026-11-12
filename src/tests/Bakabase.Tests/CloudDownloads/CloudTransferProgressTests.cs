using System;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class CloudTransferProgressTests
{
    [TestMethod]
    public void VerificationAndUnknownCountersDoNotBecomeZeroOrCarryDownloadBytes()
    {
        var stamp = DateTime.UtcNow;
        var transfer = BaiduCloudDownloader.TransferSnapshot(new(25, 25, 100, "Downloading"), stamp);
        Assert.AreEqual(25L, transfer!.CompletedBytes);
        Assert.AreEqual(100L, transfer.TotalBytes);
        Assert.AreEqual(stamp, transfer.ObservedAt);
        Assert.IsNull(BaiduCloudDownloader.TransferSnapshot(new(null, null, null, "Verifying"), stamp));
        var unknown = BaiduCloudDownloader.TransferSnapshot(new(30, 30, null), stamp);
        Assert.AreEqual(30L, unknown!.CompletedBytes);
        Assert.IsNull(unknown.TotalBytes);
    }

    [TestMethod]
    public void ContradictoryTotalsAndNegativeSentinelsRemainUnknown()
    {
        var transfer = BaiduCloudDownloader.TransferSnapshot(new(80, 120, 100), DateTime.UtcNow);
        Assert.AreEqual(120L, transfer!.CompletedBytes);
        Assert.IsNull(transfer.TotalBytes);
        Assert.IsNull(BaiduCloudDownloader.TransferSnapshot(new(null, -1, -1), DateTime.UtcNow));
    }
}
