using Bakabase.InsideWorld.Business.Components.Downloader.Components;

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloadSpeedEstimatorTests
{
    [TestMethod]
    public void ResumedBytesAndRepeatedHeartbeatsAreOnlyABaseline()
    {
        var clock = new ManualTimeProvider();
        var speed = new DownloadSpeedEstimator(clock);
        var report = speed.CreateTransferReporter();

        Assert.IsFalse(report(8_000_000)); // Existing .part bytes from a previous run.
        clock.AdvanceSeconds(1);
        Assert.IsFalse(report(8_000_000)); // CDN retry heartbeat, no new bytes.
        Assert.IsNull(speed.EstimateBytesPerSecond());

        Assert.IsTrue(report(8_001_000));
        Assert.IsNull(speed.EstimateBytesPerSecond()); // Wait for an observation interval.
        clock.AdvanceSeconds(1);
        Assert.AreEqual(1_000d, speed.EstimateBytesPerSecond());
    }

    [TestMethod]
    public void ParallelStreamsContributeOnlyTheirOwnPositiveDeltas()
    {
        var clock = new ManualTimeProvider();
        var speed = new DownloadSpeedEstimator(clock);
        var video = speed.CreateTransferReporter();
        var audio = speed.CreateTransferReporter();

        Assert.IsFalse(video(50_000));
        Assert.IsFalse(audio(5_000));
        Assert.IsTrue(video(51_000));
        Assert.IsTrue(audio(5_500));
        clock.AdvanceSeconds(1);
        Assert.AreEqual(1_500d, speed.EstimateBytesPerSecond());
    }

    [TestMethod]
    public void DiscardingAPartialFileDoesNotCreateANegativeOrRepeatedJump()
    {
        var clock = new ManualTimeProvider();
        var speed = new DownloadSpeedEstimator(clock);
        var report = speed.CreateTransferReporter();

        Assert.IsFalse(report(10_000));
        Assert.IsTrue(report(11_000));
        Assert.IsFalse(report(100)); // Range mismatch discarded the old .part file.
        Assert.IsTrue(report(300));
        clock.AdvanceSeconds(1);
        Assert.AreEqual(1_200d, speed.EstimateBytesPerSecond());
    }

    [TestMethod]
    public void StalledTransfersExpireWithoutAnotherCallback()
    {
        var clock = new ManualTimeProvider();
        var speed = new DownloadSpeedEstimator(clock);
        var report = speed.CreateTransferReporter();
        report(0);
        report(3_000);

        clock.AdvanceSeconds(1);
        Assert.AreEqual(3_000d, speed.EstimateBytesPerSecond());
        clock.AdvanceSeconds(2);
        Assert.IsNull(speed.EstimateBytesPerSecond());
        Assert.IsFalse(speed.HasRecentBytes);
    }

    [TestMethod]
    public void ResetRejectsLateReportsFromAnOldTransfer()
    {
        var clock = new ManualTimeProvider();
        var speed = new DownloadSpeedEstimator(clock);
        var oldTransfer = speed.CreateTransferReporter();
        oldTransfer(0);
        oldTransfer(1_000);
        clock.AdvanceSeconds(1);
        Assert.AreEqual(1_000d, speed.EstimateBytesPerSecond());

        speed.Reset();
        Assert.IsNull(speed.EstimateBytesPerSecond());
        Assert.IsFalse(oldTransfer(2_000));

        var resumedTransfer = speed.CreateTransferReporter();
        Assert.IsFalse(resumedTransfer(2_000));
        Assert.IsTrue(resumedTransfer(2_500));
        clock.AdvanceSeconds(1);
        Assert.AreEqual(500d, speed.EstimateBytesPerSecond());
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1_000;
        public override long GetTimestamp() => _timestamp;
        public void AdvanceSeconds(double seconds) => _timestamp += (long) Math.Round(seconds * TimestampFrequency);
    }
}
