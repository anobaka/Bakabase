using Bakabase.InsideWorld.Business.Components.Downloader.Components;

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloadTimeEstimatorTests
{
    [TestMethod]
    public void RestoredProgressIsABaselineInsteadOfWorkCompletedInThisRun()
    {
        var clock = new ManualTimeProvider();
        var estimator = new DownloadTimeEstimator(clock);

        Assert.IsNull(estimator.EstimateRemainingSeconds());
        clock.AdvanceSeconds(60);
        estimator.Report(80);
        Assert.IsNull(estimator.EstimateRemainingSeconds());

        clock.AdvanceSeconds(10);
        estimator.Report(85);
        Assert.AreEqual(30d, estimator.EstimateRemainingSeconds());
    }

    [TestMethod]
    public void RequiresFiveSecondsOfObservedProgressBeforeEstimating()
    {
        var clock = new ManualTimeProvider();
        var estimator = new DownloadTimeEstimator(clock);
        estimator.Report(0);
        clock.AdvanceSeconds(4.999);
        estimator.Report(10);
        Assert.IsNull(estimator.EstimateRemainingSeconds());

        clock.AdvanceSeconds(0.001);
        estimator.Report(20);
        Assert.AreEqual(20d, estimator.EstimateRemainingSeconds());
    }

    [TestMethod]
    public void WaitingAfterAnImmediateProgressJumpDoesNotCompleteWarmup()
    {
        var clock = new ManualTimeProvider();
        var estimator = new DownloadTimeEstimator(clock);
        estimator.Report(0);
        estimator.Report(50);
        clock.AdvanceSeconds(10);
        estimator.Report(50);

        Assert.IsNull(estimator.EstimateRemainingSeconds());
        estimator.Report(60);
        Assert.AreEqual(7d, estimator.EstimateRemainingSeconds());
    }

    [TestMethod]
    public void UsesFractionalProgressAndRoundsRemainingSecondsUp()
    {
        var clock = new ManualTimeProvider();
        var estimator = new DownloadTimeEstimator(clock);
        estimator.Report(90.1m);
        clock.AdvanceSeconds(5);
        estimator.Report(92.4m);

        Assert.AreEqual(17d, estimator.EstimateRemainingSeconds());
    }

    [TestMethod]
    public void LatestProgressIsUsedEvenWhenReportsArriveWithinOneSecond()
    {
        var clock = new ManualTimeProvider();
        var estimator = new DownloadTimeEstimator(clock);
        estimator.Report(20);
        clock.AdvanceSeconds(5);
        estimator.Report(30);
        estimator.Report(40);

        Assert.AreEqual(15d, estimator.EstimateRemainingSeconds());
        clock.AdvanceSeconds(0.1);
        estimator.Report(50);
        Assert.AreEqual(9d, estimator.EstimateRemainingSeconds());
    }

    [TestMethod]
    public void WaitingForMoreProgressIncreasesTheEstimateInsteadOfCountingDown()
    {
        var clock = new ManualTimeProvider();
        var estimator = new DownloadTimeEstimator(clock);
        estimator.Report(0);
        clock.AdvanceSeconds(10);
        estimator.Report(10);
        Assert.AreEqual(90d, estimator.EstimateRemainingSeconds());

        clock.AdvanceSeconds(10);
        Assert.AreEqual(180d, estimator.EstimateRemainingSeconds());
    }

    [TestMethod]
    public void DuplicateProgressReportsDoNotKeepAStalledEstimateAlive()
    {
        var clock = new ManualTimeProvider();
        var estimator = new DownloadTimeEstimator(clock);
        estimator.Report(0);
        clock.AdvanceSeconds(10);
        estimator.Report(10);

        for (var second = 0; second < 120; second++)
        {
            clock.AdvanceSeconds(1);
            estimator.Report(10);
        }

        Assert.AreEqual(1170d, estimator.EstimateRemainingSeconds());
        clock.AdvanceSeconds(0.001);
        estimator.Report(10);
        Assert.IsNull(estimator.EstimateRemainingSeconds());
    }

    [TestMethod]
    public void RecentWindowAdaptsToFasterProgressAndKeepsItsPrecedingSample()
    {
        var clock = new ManualTimeProvider();
        var estimator = new DownloadTimeEstimator(clock);
        estimator.Report(0);
        clock.AdvanceSeconds(60);
        estimator.Report(3);
        clock.AdvanceSeconds(60);
        estimator.Report(6);
        clock.AdvanceSeconds(60);
        estimator.Report(30);
        clock.AdvanceSeconds(60);
        estimator.Report(54);
        clock.AdvanceSeconds(10);
        estimator.Report(58);

        // At 250 s, the sample at 120 s brackets the start of the two-minute window.
        // The old 0 s / 0% baseline would overestimate this as 182 s.
        Assert.AreEqual(105d, estimator.EstimateRemainingSeconds());
    }

    [TestMethod]
    public void ProgressGoingBackwardsStartsANewObservationPeriod()
    {
        var clock = new ManualTimeProvider();
        var estimator = new DownloadTimeEstimator(clock);
        estimator.Report(40);
        clock.AdvanceSeconds(10);
        estimator.Report(60);
        Assert.AreEqual(20d, estimator.EstimateRemainingSeconds());

        estimator.Report(10);
        Assert.IsNull(estimator.EstimateRemainingSeconds());
        clock.AdvanceSeconds(5);
        estimator.Report(20);
        Assert.AreEqual(40d, estimator.EstimateRemainingSeconds());
    }

    [TestMethod]
    public void ResetDiscardsThePreviousRunAndItsElapsedTime()
    {
        var clock = new ManualTimeProvider();
        var estimator = new DownloadTimeEstimator(clock);
        estimator.Report(0);
        clock.AdvanceSeconds(10);
        estimator.Report(20);
        Assert.AreEqual(40d, estimator.EstimateRemainingSeconds());

        estimator.Reset();
        Assert.IsNull(estimator.EstimateRemainingSeconds());
        clock.AdvanceSeconds(3600);
        estimator.Report(20);
        Assert.IsNull(estimator.EstimateRemainingSeconds());
        clock.AdvanceSeconds(10);
        estimator.Report(30);
        Assert.AreEqual(70d, estimator.EstimateRemainingSeconds());
    }

    [TestMethod]
    public void CompletedAndInvalidProgressClearTheEstimate()
    {
        foreach (var progress in new[] {-1m, decimal.MinValue, 100m, 101m, decimal.MaxValue})
        {
            var clock = new ManualTimeProvider();
            var estimator = new DownloadTimeEstimator(clock);
            estimator.Report(0);
            clock.AdvanceSeconds(10);
            estimator.Report(20);
            Assert.AreEqual(40d, estimator.EstimateRemainingSeconds());

            estimator.Report(progress);
            Assert.IsNull(estimator.EstimateRemainingSeconds());
            clock.AdvanceSeconds(10);
            estimator.Report(20);
            Assert.IsNull(estimator.EstimateRemainingSeconds());
            clock.AdvanceSeconds(10);
            estimator.Report(30);
            Assert.AreEqual(70d, estimator.EstimateRemainingSeconds());
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        // Deliberately different from TimeSpan ticks to verify monotonic timestamp conversion.
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _timestamp;

        public void AdvanceSeconds(double seconds) =>
            _timestamp += (long)Math.Round(seconds * TimestampFrequency);
    }
}
