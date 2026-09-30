using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;

namespace Bakabase.Tests;

[TestClass]
public sealed class ExHentaiOriginalImagePolicyTests
{
    [DataTestMethod]
    [DataRow(1, 15, true)]
    [DataRow(100, 13, true)]
    [DataRow(100, 14, false)]
    [DataRow(100, 20, false)]
    [DataRow(100, 21, true)]
    [DataRow(400, 21, false)]
    public void FreeWindows_UseConservativeAgeAndPeakBoundaries(int days, int hour, bool expected)
    {
        var now = new DateTime(2026, 9, 30, hour, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual(expected, ExHentaiOriginalImagePolicy.IsPubliclyFree(now.AddDays(-days), now));
    }

    [TestMethod]
    public void FreeWindows_RejectFutureUnknownAndSundayPeak()
    {
        var now = new DateTime(2026, 9, 27, 6, 0, 0, DateTimeKind.Utc);
        Assert.IsFalse(ExHentaiOriginalImagePolicy.IsPubliclyFree(now.AddDays(-100), now));
        Assert.IsFalse(ExHentaiOriginalImagePolicy.IsPubliclyFree(now.AddDays(1), now));
        Assert.IsFalse(ExHentaiOriginalImagePolicy.IsPubliclyFree(DateTime.SpecifyKind(now, DateTimeKind.Unspecified), now));
    }

    [TestMethod]
    public void Reservation_ProtectsReplenishmentAndUnknownFiq()
    {
        Assert.AreEqual(1_000L, ExHentaiOriginalImagePolicy.EstimateGpReservation(1));
        Assert.AreEqual(1_000L, ExHentaiOriginalImagePolicy.EstimateGpReservation(50_000_000));
        Assert.AreEqual(2_000L, ExHentaiOriginalImagePolicy.EstimateGpReservation(50_000_001));
        Assert.ThrowsException<ExHentaiOriginalImageSafetyException>(() => ExHentaiOriginalImagePolicy.EstimateGpReservation(null));
        Assert.ThrowsException<ExHentaiOriginalImageSafetyException>(() => ExHentaiOriginalImagePolicy.CheckBalance(10_000, 1_000, 10_000));
        ExHentaiOriginalImagePolicy.CheckBalance(11_000, 1_000, 10_000);
    }

    [TestMethod]
    public async Task Ledger_BudgetSurvivesInstancesAndConcurrentReservations()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExHentaiLedger_" + Guid.NewGuid().ToString("N"));
        try
        {
            var a = new ExHentaiDownloadLedger(() => root);
            await a.ReserveGpAsync(10, 1_000, 2_000, CancellationToken.None);
            var b = new ExHentaiDownloadLedger(() => root);
            var successes = 0;
            async Task Reserve()
            {
                try { await b.ReserveGpAsync(10, 1_000, 2_000, CancellationToken.None); Interlocked.Increment(ref successes); }
                catch (ExHentaiOriginalImageSafetyException) { }
            }
            await Task.WhenAll(Reserve(), Reserve());
            Assert.AreEqual(1, successes);
            await Assert.ThrowsExceptionAsync<ExHentaiOriginalImageSafetyException>(() =>
                a.ReserveGpAsync(10, 1_000, 2_000, CancellationToken.None));
            await a.ReserveGpAsync(11, 1_000, 1_000, CancellationToken.None);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
