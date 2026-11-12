using Bakabase.Abstractions.Components.FileSystem;

namespace Bakabase.Tests;

[TestClass]
public sealed class StorageWriteGuardTests
{
    private static readonly StorageWriteContext Owner = new("workflow", "12");
    private static string Destination => Path.Combine(Path.GetTempPath(), "storage-guard-test", "output");

    [DataTestMethod]
    [DataRow(@"\\server\share\folder\future.bin", @"\\server\share\")]
    [DataRow(@"\\?\UNC\server\share\folder\future.bin", @"\\?\UNC\server\share\")]
    public void UnmappedWindowsShareQueriesCallerQuotaUsingCanonicalShareRoot(string path, string expectedQuery)
    {
        var observed = "";
        var measured = StorageWriteGuard.MeasureWindowsUnc(path, query => { observed = query; return 101UL * 1024 * 1024 * 1024; });
        Assert.AreEqual(expectedQuery, observed);
        Assert.AreEqual(@"\\server\share\", measured!.Value.Volume);
        Assert.AreEqual(101L * 1024 * 1024 * 1024, measured.Value.Available);
    }

    [TestMethod]
    public void WindowsShareFailureCannotFallBackToAHealthyLocalDrive()
    {
        Assert.ThrowsException<IOException>(() => StorageWriteGuard.MeasureWindowsUnc(@"\\server\share\future",
            _ => throw new IOException("unavailable")));
        Assert.IsNull(StorageWriteGuard.MeasureWindowsUnc(@"C:\work", _ => throw new AssertFailedException()));
        Assert.IsNull(StorageWriteGuard.MeasureWindowsUnc(@"\\?\C:\work", _ => throw new AssertFailedException()));
    }

    [TestMethod]
    public async Task Known_allocation_requires_its_complete_budget_above_the_live_reserve()
    {
        var policy = new Policy {Minimum = 100};
        var guard = new StorageWriteGuard(policy, _ => ("volume", 120), TimeSpan.FromMilliseconds(5));
        await guard.CheckAllocationAsync(Destination, 20, Owner);
        var error = await Assert.ThrowsExactlyAsync<LocalStorageException>(() => guard.CheckAllocationAsync(Destination, 21, Owner));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, error.Code);
        policy.Minimum = 110;
        await Assert.ThrowsExactlyAsync<LocalStorageException>(() => guard.CheckAllocationAsync(Destination, 20, Owner));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => guard.CheckAllocationAsync(Destination, -1, Owner));
    }

    [TestMethod]
    public async Task AdmissionRejectsBeforeWriterStartsAndIndependentOperationsKeepTheirPolicy()
    {
        var policy = new Policy { Minimum = 100 };
        var guard = new StorageWriteGuard(policy, _ => ("volume", 99), TimeSpan.FromMilliseconds(5));
        var invoked = false;
        var error = await Assert.ThrowsExceptionAsync<LocalStorageException>(() => guard.RunAsync([Destination], Owner,
            _ => { invoked = true; return Task.FromResult(true); }));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, error.Code);
        Assert.IsFalse(invoked);
        Assert.IsTrue(await guard.RunAsync([Destination], new("independentDownload", "1"), _ => Task.FromResult(true)));
    }

    [TestMethod, Timeout(10000)]
    public async Task RunningWriterIsCancelledAndDrainedBeforeReserveFailureReturns()
    {
        long available = 200;
        var guard = new StorageWriteGuard(new Policy { Minimum = 100 }, _ => ("volume", Interlocked.Read(ref available)), TimeSpan.FromMilliseconds(5));
        var drained = false;
        var error = await Assert.ThrowsExceptionAsync<LocalStorageException>(() => guard.RunAsync([Destination], Owner, async token =>
        {
            Interlocked.Exchange(ref available, 50);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { await Task.Delay(20); drained = true; }
            return true;
        }));
        Assert.IsTrue(drained);
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, error.Code);
    }

    [TestMethod, Timeout(10000)]
    public async Task CancellationCallbackFailureDoesNotHideReserveCauseOrDetachWriter()
    {
        long available = 200;
        var guard = new StorageWriteGuard(new Policy { Minimum = 100 }, _ => ("volume", Interlocked.Read(ref available)), TimeSpan.FromMilliseconds(5));
        var drained = false;
        var error = await Assert.ThrowsExceptionAsync<LocalStorageException>(() => guard.RunAsync([Destination], Owner, async token =>
        {
            using var callback = token.Register(() => throw new InvalidOperationException("transport cleanup callback failed"));
            Interlocked.Exchange(ref available, 50);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { await Task.Delay(20); drained = true; }
            return true;
        }));
        Assert.IsTrue(drained);
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, error.Code);
    }

    [TestMethod, Timeout(10000)]
    public async Task PolicyChangesApplyToRunningWriterWithoutChangingItsIdentity()
    {
        var policy = new Policy { Minimum = 50 };
        var guard = new StorageWriteGuard(policy, _ => ("volume", 100), TimeSpan.FromMilliseconds(5));
        var error = await Assert.ThrowsExceptionAsync<LocalStorageException>(() => guard.RunAsync([Destination], Owner, async token =>
        {
            policy.Minimum = 200;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return true;
        }));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, error.Code);
    }

    [TestMethod]
    public async Task CopyChecksNextChunkBeforeWritingBelowReserve()
    {
        var guard = new StorageWriteGuard(new Policy { Minimum = 100 }, _ => ("volume", 110), TimeSpan.FromMilliseconds(5));
        await using var source = new MemoryStream(new byte[11]);
        await using var target = new MemoryStream();
        var error = await Assert.ThrowsExceptionAsync<LocalStorageException>(() => guard.CopyAsync(source, target, Destination, Owner));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, error.Code);
        Assert.AreEqual(0, target.Length);
    }

    [TestMethod, Timeout(10000)]
    public async Task VolumeDisappearanceCannotFallBackToWritingOnAnotherMount()
    {
        var changed = false;
        var guard = new StorageWriteGuard(new Policy { Minimum = 100 }, _ => (changed ? "fallback-root" : "nas", 500), TimeSpan.FromMilliseconds(5));
        var error = await Assert.ThrowsExceptionAsync<LocalStorageException>(() => guard.RunAsync([Destination], Owner, async token =>
        {
            changed = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return true;
        }));
        Assert.AreEqual("LOCAL_STORAGE_UNAVAILABLE", error.Code);
    }

    [TestMethod, Timeout(10000)]
    public async Task CallerCancellationStaysCancellationAndNestedScopeIsRestored()
    {
        var guard = new StorageWriteGuard(new Policy { Minimum = 100 }, _ => ("volume", 500), TimeSpan.FromMilliseconds(5));
        using var outer = StorageWriteScope.Enter(Owner);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => guard.RunAsync([Destination], null, async token =>
        {
            Assert.AreEqual(Owner, StorageWriteScope.Current);
            using (StorageWriteScope.Enter(new("workflow", "13"))) Assert.AreEqual("13", StorageWriteScope.Current!.OwnerId);
            Assert.AreEqual(Owner, StorageWriteScope.Current);
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return true;
        }, cancellation.Token));
        Assert.AreEqual(Owner, StorageWriteScope.Current);
    }

    private sealed class Policy : IStorageWritePolicyProvider
    {
        public long Minimum;
        public Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default) =>
            Task.FromResult(context?.OwnerKind == "workflow" ? Interlocked.Read(ref Minimum) : 0);
    }
}
