using Bakabase.Abstractions.Components.FileSystem;

namespace Bakabase.Tests;

[TestClass]
public sealed class StorageWriteReservationTests
{
    private const long MiB = 1024 * 1024;
    private const long Floor = 100 * MiB;
    private const long Payload = 10 * MiB;
    private const long Budget = StorageWriteGuard.ReservationHeadroomBytes + Payload;
    private static readonly StorageWriteContext Owner = new("workflow", "reservation-test");
    // These are measurement keys only; no fixture file or directory is created.
    private static string Destination(string name) => Path.Combine(Path.GetTempPath(), "storage-reservation-test", name);
    private static StorageWriteBudget[] Allocation(string name = "one", long bytes = Payload) => [new(Destination(name), bytes)];
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static StorageWriteGuard Guard(Func<string, (string Volume, long Available)> measure, TimeSpan? interval = null) =>
        new(new Policy(), measure, interval ?? TimeSpan.FromMilliseconds(5));

    [TestMethod, Timeout(10000)]
    public async Task TwoAdmittedWritersCanConsumeTheirBudgetsWithoutCountingTheirOwnReservationTwice()
    {
        long free = Floor + 2 * Budget;
        var guard = Guard(_ => ("same-volume", Interlocked.Read(ref free)));
        var firstStarted = Signal(); var secondStarted = Signal(); var write = Signal(); var finish = Signal();
        async Task<bool> Writer(TaskCompletionSource started, CancellationToken ct)
        {
            started.SetResult();
            await write.Task.WaitAsync(ct);
            Interlocked.Add(ref free, -Payload);
            await finish.Task.WaitAsync(ct);
            return true;
        }
        using var cancellation = new CancellationTokenSource();
        var first = guard.RunReservedAsync(Allocation("first"), Owner, ct => Writer(firstStarted, ct), ct: cancellation.Token);
        var second = guard.RunReservedAsync(Allocation("second"), Owner, ct => Writer(secondStarted, ct), ct: cancellation.Token);
        try
        {
            await Task.WhenAll(firstStarted.Task, secondStarted.Task).WaitAsync(TimeSpan.FromSeconds(3));
            write.SetResult();
            await Task.Delay(40);
            Assert.IsFalse(first.IsCompleted); Assert.IsFalse(second.IsCompleted);
            Assert.AreEqual(Floor + 2 * StorageWriteGuard.ReservationHeadroomBytes, Interlocked.Read(ref free));
            finish.SetResult();
            Assert.IsTrue(await first); Assert.IsTrue(await second);
        }
        finally { cancellation.Cancel(); write.TrySetResult(); finish.TrySetResult(); }
    }

    [TestMethod, Timeout(10000)]
    public async Task CompetingLeaseWaitsAndReportsActivityWhileCancellationLeavesTheCurrentLeaseIntact()
    {
        var guard = Guard(_ => ("same-volume", Floor + Budget));
        var release = Signal(); var firstStarted = Signal(); var waiting = Signal();
        using var stopFirst = new CancellationTokenSource(); using var stopWaiter = new CancellationTokenSource();
        var first = guard.RunReservedAsync(Allocation(), Owner, async ct =>
        { firstStarted.SetResult(); await release.Task.WaitAsync(ct); return true; }, ct: stopFirst.Token);
        var waiterInvoked = false;
        try
        {
            await firstStarted.Task;
            var second = guard.RunReservedAsync(Allocation("alias"), Owner, _ =>
                { waiterInvoked = true; return Task.FromResult(true); },
                () => { waiting.TrySetResult(); return Task.CompletedTask; }, stopWaiter.Token);
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsFalse(waiterInvoked);
            stopWaiter.Cancel();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => second);
            Assert.IsFalse(first.IsCompleted, "Cancelling an admission waiter must not release another writer's capacity.");
            release.SetResult(); await first;
            Assert.IsTrue(await guard.RunReservedAsync(Allocation("after-release"), Owner, _ => Task.FromResult(true)));
        }
        finally { stopFirst.Cancel(); stopWaiter.Cancel(); release.TrySetResult(); }
    }

    [TestMethod, Timeout(10000)]
    public async Task WriterCancellationRetainsCapacityUntilItsCleanupHasDrained()
    {
        var guard = Guard(_ => ("same-volume", Floor + Budget));
        var started = Signal(); var draining = Signal(); var drain = Signal(); var waiting = Signal();
        using var stop = new CancellationTokenSource(); using var stopWaiter = new CancellationTokenSource();
        var first = guard.RunReservedAsync(Allocation(), Owner, async ct =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { draining.SetResult(); await drain.Task; }
            return true;
        }, ct: stop.Token);
        var secondStarted = false;
        try
        {
            await started.Task; stop.Cancel(); await draining.Task;
            var second = guard.RunReservedAsync(Allocation("second"), Owner, _ =>
                { secondStarted = true; return Task.FromResult(true); },
                () => { waiting.TrySetResult(); return Task.CompletedTask; }, stopWaiter.Token);
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsFalse(secondStarted);
            drain.SetResult();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => first);
            Assert.IsTrue(await second.WaitAsync(TimeSpan.FromSeconds(3)));
        }
        finally { stop.Cancel(); stopWaiter.Cancel(); drain.TrySetResult(); }
    }

    [TestMethod]
    public async Task AliasDestinationsShareOneVolumeBudgetAndOneHeadroomAllowance()
    {
        long free = Floor + StorageWriteGuard.ReservationHeadroomBytes + 2 * Payload - 1;
        var guard = Guard(_ => ("resolved-volume", Interlocked.Read(ref free)));
        StorageWriteBudget[] aliases = [new(Destination("physical"), Payload), new(Destination("symlink"), Payload)];
        var invoked = false;
        var error = await Assert.ThrowsExactlyAsync<LocalStorageException>(() => guard.RunReservedAsync(aliases, Owner,
            _ => { invoked = true; return Task.FromResult(true); }));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, error.Code); Assert.IsFalse(invoked);
        Interlocked.Increment(ref free);
        Assert.IsTrue(await guard.RunReservedAsync(aliases, Owner, _ => Task.FromResult(true)));
    }

    [TestMethod, Timeout(10000)]
    public async Task MultiVolumeWaiterHoldsNoPartialReservation()
    {
        var guard = Guard(path => (path.EndsWith("volume-b", StringComparison.Ordinal) ? "b" : "a", Floor + Budget));
        var releaseA = Signal(); var startedA = Signal(); var waiting = Signal();
        using var stopA = new CancellationTokenSource(); using var stopBoth = new CancellationTokenSource();
        var first = guard.RunReservedAsync(Allocation("volume-a"), Owner, async ct =>
        { startedA.SetResult(); await releaseA.Task.WaitAsync(ct); return true; }, ct: stopA.Token);
        try
        {
            await startedA.Task;
            StorageWriteBudget[] both = [new(Destination("volume-a"), Payload), new(Destination("volume-b"), Payload)];
            var waiter = guard.RunReservedAsync(both, Owner, _ => Task.FromResult(true),
                () => { waiting.TrySetResult(); return Task.CompletedTask; }, stopBoth.Token);
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsTrue(await guard.RunReservedAsync(Allocation("volume-b"), Owner, _ => Task.FromResult(true))
                .WaitAsync(TimeSpan.FromSeconds(3)), "Waiting for A must not reserve B.");
            releaseA.SetResult(); await first;
            Assert.IsTrue(await waiter.WaitAsync(TimeSpan.FromSeconds(3)));
        }
        finally { stopA.Cancel(); stopBoth.Cancel(); releaseA.TrySetResult(); }
    }

    [TestMethod]
    public async Task InsufficientSecondVolumeFailsBeforeWritingAndDoesNotLeakTheFirstVolume()
    {
        var guard = Guard(path => (path.EndsWith("volume-b", StringComparison.Ordinal) ? "b" : "a",
            path.EndsWith("volume-b", StringComparison.Ordinal) ? Floor + Budget - 1 : Floor + Budget));
        var invoked = false;
        StorageWriteBudget[] both = [new(Destination("volume-a"), Payload), new(Destination("volume-b"), Payload)];
        var error = await Assert.ThrowsExactlyAsync<LocalStorageException>(() => guard.RunReservedAsync(both, Owner,
            _ => { invoked = true; return Task.FromResult(true); }));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, error.Code); Assert.IsFalse(invoked);
        Assert.IsTrue(await guard.RunReservedAsync(Allocation("volume-a"), Owner, _ => Task.FromResult(true))
            .WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [TestMethod]
    public async Task VolumeReplacementBetweenAdmissionAndWriteRejectsTheWriterAndReleasesCapacity()
    {
        var measurements = 0;
        var guard = Guard(_ => (Interlocked.Increment(ref measurements) == 1 ? "original" : "replacement", Floor + Budget));
        var invoked = false;
        var error = await Assert.ThrowsExactlyAsync<LocalStorageException>(() => guard.RunReservedAsync(Allocation(), Owner,
            _ => { invoked = true; return Task.FromResult(true); }));
        Assert.AreEqual("LOCAL_STORAGE_UNAVAILABLE", error.Code); Assert.IsFalse(invoked);
        Assert.IsTrue(await guard.RunReservedAsync(Allocation(), Owner, _ => Task.FromResult(true)));
    }

    [TestMethod]
    public async Task FastWriterStillChecksFinalFreeSpaceBeforeReportingSuccess()
    {
        long free = Floor + Budget;
        var guard = Guard(_ => ("volume", Interlocked.Read(ref free)), TimeSpan.FromHours(1));
        var error = await Assert.ThrowsExactlyAsync<LocalStorageException>(() => guard.RunAsync([Destination("fast")], Owner, _ =>
        {
            Interlocked.Exchange(ref free, Floor - 1);
            return Task.FromResult(true);
        }));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, error.Code);
    }

    [TestMethod]
    public async Task OverflowCannotTurnAnImpossibleBudgetIntoAnAdmission()
    {
        var guard = Guard(_ => ("volume", long.MaxValue));
        var invoked = false;
        var error = await Assert.ThrowsExactlyAsync<LocalStorageException>(() => guard.RunReservedAsync(Allocation(bytes: long.MaxValue), Owner,
            _ => { invoked = true; return Task.FromResult(true); }));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, error.Code); Assert.IsFalse(invoked);
    }

    [TestMethod]
    public async Task DifferentLinuxMountPathsOnTheSameDeviceShareTheirCapacity()
    {
        if (OperatingSystem.IsWindows()) return;
        string[] mounts =
        [
            "61 25 0:37 /data /data rw,relatime - virtiofs mac rw",
            "62 25 0:37 /work/acquisitions /work/acquisitions rw,relatime shared:10 - virtiofs mac rw",
            "63 25 8:2 / /other\\040disk rw,relatime - ext4 /dev/sda2 rw"
        ];
        Assert.AreEqual(StorageVolumes.LinuxCapacityIdentity("/data", mounts),
            StorageVolumes.LinuxCapacityIdentity("/work/acquisitions", mounts));
        Assert.AreNotEqual(StorageVolumes.LinuxCapacityIdentity("/data", mounts),
            StorageVolumes.LinuxCapacityIdentity("/other disk", mounts));
        var guard = Guard(path => (StorageVolumes.LinuxCapacityIdentity(path, mounts), Floor + Budget));
        var invoked = false;
        var error = await Assert.ThrowsExactlyAsync<LocalStorageException>(() => guard.RunReservedAsync(
            [new("/data", Payload), new("/work/acquisitions", Payload)], Owner,
            _ => { invoked = true; return Task.FromResult(true); }));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, error.Code); Assert.IsFalse(invoked);
        Assert.ThrowsException<IOException>(() => StorageVolumes.LinuxCapacityIdentity("/missing", mounts));
    }

    [TestMethod]
    public void ValidatedMacBindMountsSeparateCapacityAndKeepAliasesTogether()
    {
        if (OperatingSystem.IsWindows()) return;
        string[] mounts =
        [
            "60 25 0:36 /host/app /data rw,relatime - virtiofs mac rw",
            "61 25 0:36 /host/work /work/acquisitions rw,relatime - virtiofs mac rw",
            "62 25 0:36 /host/nas /media/nas rw,relatime - virtiofs mac rw",
            "63 25 0:36 /host/nas/acceptance /nas\\040acceptance rw,relatime - virtiofs mac rw",
            "64 25 0:36 /host/other-nas /media/other rw,relatime - virtiofs mac rw",
            "65 25 8:2 /host/native /native rw,relatime - ext4 /dev/sda2 rw"
        ];
        const string metadata = """
            {"schemaVersion":1,"mounts":[
               {"type":"bind","target":"/data","hostCapacity":{"root":"/host/app","device":10,"totalBytes":10000}},
               {"type":"bind","target":"/work/acquisitions","hostCapacity":{"root":"/host/work","device":10,"totalBytes":10000}},
               {"type":"bind","target":"/media/nas","hostCapacity":{"root":"/host/nas","device":20,"totalBytes":20000}},
               {"type":"bind","target":"/nas acceptance","hostCapacity":{"root":"/host/nas/acceptance","device":20,"totalBytes":20000}},
               {"type":"bind","target":"/media/other","hostCapacity":{"root":"/host/other-nas","device":30,"totalBytes":30000}},
               {"type":"bind","target":"/native","hostCapacity":{"root":"/host/native","device":40,"totalBytes":40000}}
            ]}
            """;
        string Identity(string path, long bytes, string? claims = metadata) =>
            StorageVolumes.LinuxCapacityIdentity(path, mounts, bytes, claims);
        Assert.AreEqual(Identity("/data", 10000), Identity("/work/acquisitions", 10000));
        Assert.AreEqual(Identity("/media/nas", 20000), Identity("/nas acceptance", 20000));
        Assert.AreNotEqual(Identity("/data", 10000), Identity("/media/nas", 20000));
        Assert.AreNotEqual(Identity("/media/nas", 20000), Identity("/media/other", 30000));
        Assert.AreEqual(Identity("/native", 40000, null), Identity("/native", 1));
        Assert.ThrowsException<IOException>(() => Identity("/media/nas", 1));

        var missingPeer = metadata.Replace("/media/other", "/stale-target", StringComparison.Ordinal);
        var stalePeer = metadata.Replace("/host/other-nas", "/stale-source", StringComparison.Ordinal);
        Assert.ThrowsException<IOException>(() => Identity("/media/nas", 1, missingPeer));
        foreach (var invalidClaims in new[] { null, missingPeer, stalePeer })
        {
            Assert.AreEqual(Identity("/data", 10000, null), Identity("/media/nas", 20000, invalidClaims));
            Assert.AreEqual(Identity("/data", 10000, null), Identity("/data", 10000, invalidClaims));
        }
        var sharedTotal = metadata.Replace("30000", "20000", StringComparison.Ordinal);
        Assert.AreEqual(Identity("/media/nas", 20000, sharedTotal), Identity("/media/other", 20000, sharedTotal));
    }

    private sealed class Policy : IStorageWritePolicyProvider
    {
        public Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default) => Task.FromResult(Floor);
    }
}
