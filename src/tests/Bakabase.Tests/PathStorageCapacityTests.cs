using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Service.Services;

namespace Bakabase.Tests;

[TestClass]
public sealed class PathStorageCapacityTests
{
    private static string NewPath(string suffix = "future") =>
        Path.Combine(Path.GetTempPath(), "capacity-" + Guid.NewGuid().ToString("N"), suffix);

    private static PathStorageCapacityService Create(Policy policy, Func<string, StorageVolumeCapacity> measure,
        int maximumQueries = 4, int deadlineMilliseconds = 1000) =>
        new(policy, measure, TimeSpan.FromMilliseconds(deadlineMilliseconds), TimeSpan.FromSeconds(5), maximumQueries);

    [TestMethod]
    public async Task ConfiguredFutureDirectoryCanBeMeasuredWithoutCreatingItAndReusesShortCache()
    {
        var path = NewPath();
        var policy = new Policy();
        var calls = 0;
        var service = Create(policy, measuredPath =>
        {
            Assert.AreEqual(Path.GetFullPath(path), measuredPath);
            Interlocked.Increment(ref calls);
            return new("actual-server-volume", 500L * 1024 * 1024 * 1024, 2L * 1024 * 1024 * 1024 * 1024);
        });

        var first = await service.GetAsync(path);
        var second = await service.GetAsync(Path.Combine(path, "..", Path.GetFileName(path)));

        Assert.AreEqual("available", first.Status);
        Assert.AreEqual("actual-server-volume", first.VolumePath);
        Assert.AreEqual(500L * 1024 * 1024 * 1024, first.AvailableBytes);
        Assert.AreEqual(2L * 1024 * 1024 * 1024 * 1024, first.TotalBytes);
        Assert.AreEqual(first, second);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(1, policy.Checks);
        Assert.IsFalse(Directory.Exists(path));
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(path)));
    }

    [TestMethod]
    public async Task RejectedAndInvalidPathsAreNeverMeasured()
    {
        var service = Create(new Policy { Reject = true }, _ => throw new AssertFailedException("Rejected paths cannot be measured."));
        var denied = await service.GetAsync(NewPath());
        Assert.AreEqual("unavailable", denied.Status);
        Assert.AreEqual("STORAGE_CAPACITY_PATH_NOT_ALLOWED", denied.ErrorCode);
        Assert.IsNull(denied.AvailableBytes);
        Assert.IsNull(denied.TotalBytes);

        foreach (var path in new[] { "", "relative/folder", "\0" })
        {
            var invalid = await service.GetAsync(path);
            Assert.AreEqual("unknown", invalid.Status);
            Assert.AreEqual("STORAGE_CAPACITY_PATH_INVALID", invalid.ErrorCode);
            Assert.IsNull(invalid.AvailableBytes);
        }
    }

    [TestMethod]
    public async Task DisconnectedOrDeniedStorageCannotLookLikeAnEmptyFullDrive()
    {
        var disconnected = await Create(new Policy(), _ => throw new IOException("Private NAS details"))
            .GetAsync(NewPath());
        Assert.AreEqual("unavailable", disconnected.Status);
        Assert.AreEqual("STORAGE_CAPACITY_UNAVAILABLE", disconnected.ErrorCode);
        Assert.IsNull(disconnected.AvailableBytes);
        Assert.IsNull(disconnected.TotalBytes);
        Assert.IsNull(disconnected.VolumePath);

        var denied = await Create(new Policy(), _ => throw new UnauthorizedAccessException("Private account details"))
            .GetAsync(NewPath());
        Assert.AreEqual("STORAGE_CAPACITY_ACCESS_DENIED", denied.ErrorCode);
        Assert.IsNull(denied.AvailableBytes);
    }

    [DataTestMethod]
    [DataRow(-1L, 100L)]
    [DataRow(0L, 0L)]
    [DataRow(101L, 100L)]
    public async Task InvalidNativeCapacityIsUnknownRatherThanDisplayedAsFull(long available, long total)
    {
        var result = await Create(new Policy(), _ => new("volume", available, total)).GetAsync(NewPath());
        Assert.AreEqual("unknown", result.Status);
        Assert.AreEqual("STORAGE_CAPACITY_UNKNOWN", result.ErrorCode);
        Assert.IsNull(result.AvailableBytes);
        Assert.IsNull(result.TotalBytes);
    }

    [TestMethod, Timeout(10000)]
    public async Task StuckNativeReadsHaveDeadlinesAndBoundedSlotsWithoutDuplicatingSamePath()
    {
        using var release = new ManualResetEventSlim();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstPath = NewPath("first");
        var secondPath = NewPath("second");
        var reads = 0;
        var service = Create(new Policy(), path =>
        {
            Interlocked.Increment(ref reads);
            var first = path == firstPath;
            (first ? firstStarted : secondStarted).TrySetResult();
            try { release.Wait(); return new("volume", 50, 100); }
            finally { (first ? firstExited : secondExited).TrySetResult(); }
        }, maximumQueries: 2, deadlineMilliseconds: 60);

        try
        {
            var first = service.GetAsync(firstPath);
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var repeated = service.GetAsync(firstPath);
            Assert.AreEqual("STORAGE_CAPACITY_TIMED_OUT", (await first).ErrorCode);
            Assert.AreEqual("STORAGE_CAPACITY_TIMED_OUT", (await repeated).ErrorCode);
            Assert.AreEqual(1, Volatile.Read(ref reads));

            var second = service.GetAsync(secondPath);
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual("STORAGE_CAPACITY_TIMED_OUT", (await second).ErrorCode);
            var busy = await service.GetAsync(NewPath("third"));
            Assert.AreEqual("unknown", busy.Status);
            Assert.AreEqual("STORAGE_CAPACITY_BUSY", busy.ErrorCode);
            Assert.IsNull(busy.AvailableBytes);
            Assert.AreEqual(2, Volatile.Read(ref reads));
        }
        finally
        {
            release.Set();
            if (firstStarted.Task.IsCompleted) await firstExited.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (secondStarted.Task.IsCompleted) await secondExited.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [TestMethod, Timeout(10000)]
    public async Task ClientCancellationDoesNotFreeTheNativeSlotBeforeReadActuallyStops()
    {
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Create(new Policy(), _ =>
        {
            started.TrySetResult();
            try { release.Wait(); return new("volume", 0, 100); }
            finally { exited.TrySetResult(); }
        }, maximumQueries: 1, deadlineMilliseconds: 5000);
        try
        {
            var query = service.GetAsync(NewPath(), cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => query);
            Assert.AreEqual("STORAGE_CAPACITY_BUSY", (await service.GetAsync(NewPath())).ErrorCode);
        }
        finally
        {
            release.Set();
            if (started.Task.IsCompleted) await exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [DataTestMethod]
    [DataRow(@"\\server\share\folder\future", @"\\server\share\")]
    [DataRow(@"\\?\UNC\server\share\folder\future", @"\\?\UNC\server\share\")]
    public void UnmappedWindowsShareCapacityPreservesCallerQuotaAndLargeTotals(string path, string expectedQuery)
    {
        var observed = "";
        var capacity = StorageVolumes.MeasureWindowsUnc(path, query =>
        {
            observed = query;
            return (500UL * 1024 * 1024 * 1024, 2UL * 1024 * 1024 * 1024 * 1024);
        });
        Assert.AreEqual(expectedQuery, observed);
        Assert.AreEqual(@"\\server\share\", capacity!.VolumePath);
        Assert.AreEqual(500L * 1024 * 1024 * 1024, capacity.AvailableBytes);
        Assert.AreEqual(2L * 1024 * 1024 * 1024 * 1024, capacity.TotalBytes);
    }

    private sealed class Policy : IUserStoragePolicy
    {
        public bool Reject;
        public int Checks;
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => !Reject;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {
            Interlocked.Increment(ref Checks);
            if (Reject) throw new UserStoragePathException("Outside allowed mount");
        }
        public void EnsureTreeMutationAllowed(string path) => throw new AssertFailedException("A capacity read must not mutate storage.");
    }
}
