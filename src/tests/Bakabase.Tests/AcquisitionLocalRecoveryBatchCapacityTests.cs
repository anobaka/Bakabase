using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Service.Components.Acquisition.Planning;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionLocalRecoveryBatchCapacityTests
{
    private const long GiB = 1024L * 1024 * 1024;
    private static string PathFor(string leaf) => Path.Combine(Path.GetTempPath(), "batch-capacity-preview", leaf);

    [TestMethod]
    public void SameVolumeCountsProcessingAndDeliveryWithoutCreditingCleanup()
    {
        var work = PathFor("processing"); var delivery = PathFor("delivery");
        var reader = new CapacityReader(new(work, 60 * GiB, 100 * GiB), new(delivery, 60 * GiB, 100 * GiB));
        var volumes = AcquisitionLocalRecoveryBatchCapacity.Inspect(
            [new([work, work], [delivery], 6 * GiB, false)], 50 * GiB, reader, new StoragePolicy());
        var volume = volumes.Single();
        Assert.AreEqual(10 * GiB, volume.UsableBytes);
        Assert.AreEqual(12 * GiB, volume.EstimatedAdditionalBytes);
        Assert.IsTrue(volume.ExceedsUsable, "Existing input and possible cleanup must not become free-space credit.");
        Assert.IsFalse(volume.EstimateIncomplete);
        CollectionAssert.AreEquivalent(new[] {work, delivery}, volume.Paths.ToArray());
    }

    [TestMethod]
    public void SeparateVolumesKeepTheirOwnReserveAndDoNotBorrowCapacity()
    {
        var work = PathFor("ssd"); var delivery = PathFor("nas");
        var reader = new CapacityReader(new(work, 60 * GiB, 100 * GiB), new(delivery, 53 * GiB, 1000 * GiB))
            {SameVolume = false};
        var volumes = AcquisitionLocalRecoveryBatchCapacity.Inspect(
            [new([work], [delivery], 5 * GiB, false)], 50 * GiB, reader, new StoragePolicy());
        Assert.AreEqual(2, volumes.Count);
        Assert.IsFalse(volumes.Single(v => v.VolumeId == work).ExceedsUsable);
        Assert.IsTrue(volumes.Single(v => v.VolumeId == delivery).ExceedsUsable);
        Assert.IsTrue(volumes.All(v => v.EstimatedAdditionalBytes == 5 * GiB));
    }

    [TestMethod]
    public void BindAliasesUseOneVolumeAndTheLowestObservedFreeSpace()
    {
        var first = PathFor("bind-a"); var second = PathFor("bind-b");
        var reader = new CapacityReader(new(first, 70 * GiB, 100 * GiB), new(second, 55 * GiB, 100 * GiB));
        var volume = AcquisitionLocalRecoveryBatchCapacity.Inspect(
            [new([first, second], [], 6 * GiB, false)], 50 * GiB, reader, new StoragePolicy()).Single();
        Assert.AreEqual(55 * GiB, volume.AvailableBytes);
        Assert.AreEqual(6 * GiB, volume.EstimatedAdditionalBytes, "Aliases are not separate working copies.");
        Assert.IsTrue(volume.ExceedsUsable);
    }

    [TestMethod]
    public void UnknownAndPartialEstimatesNeverBecomeKnownZero()
    {
        var work = PathFor("unknown");
        var reader = new CapacityReader(new StorageVolumeCapacity(work, 80 * GiB, 100 * GiB));
        var unknown = AcquisitionLocalRecoveryBatchCapacity.Inspect(
            [new([work], [], null, true)], 50 * GiB, reader, new StoragePolicy()).Single();
        Assert.IsNull(unknown.EstimatedAdditionalBytes);
        Assert.IsTrue(unknown.EstimateIncomplete);
        var partial = AcquisitionLocalRecoveryBatchCapacity.Inspect(
            [new([work], [], null, true), new([work], [], GiB, false)], 50 * GiB, reader, new StoragePolicy()).Single();
        Assert.AreEqual(GiB, partial.EstimatedAdditionalBytes);
        Assert.IsTrue(partial.EstimateIncomplete);
    }

    [TestMethod]
    public void ArithmeticOverflowRemainsBlockedAndIncomplete()
    {
        var work = PathFor("overflow"); var reader = new CapacityReader(new StorageVolumeCapacity(work, long.MaxValue - 1, long.MaxValue));
        var volume = AcquisitionLocalRecoveryBatchCapacity.Inspect(
            [new([work], [work], long.MaxValue / 2 + 1, false)], GiB, reader, new StoragePolicy()).Single();
        Assert.IsTrue(volume.ExceedsUsable);
        Assert.IsTrue(volume.EstimateIncomplete);
        Assert.AreEqual(long.MaxValue, volume.EstimatedAdditionalBytes);
    }

    [TestMethod]
    public void ReserveBoundaryDoesNotPermitAdditionalWork()
    {
        var work = PathFor("boundary"); var reader = new CapacityReader(new StorageVolumeCapacity(work, 50 * GiB, 100 * GiB));
        var volume = AcquisitionLocalRecoveryBatchCapacity.Inspect(
            [new([work], [], 0, false)], 50 * GiB, reader, new StoragePolicy()).Single();
        Assert.AreEqual(0L, volume.UsableBytes);
        Assert.IsTrue(volume.ExceedsUsable);
    }

    [TestMethod]
    public void UnavailableCapacityAndDisallowedPathsFailBeforeAdmission()
    {
        var work = PathFor("restricted"); var reader = new CapacityReader(new StorageVolumeCapacity(work, -1, 100 * GiB));
        Assert.ThrowsException<IOException>(() => AcquisitionLocalRecoveryBatchCapacity.Inspect(
            [new([work], [], 1, false)], 50 * GiB, reader, new StoragePolicy()));
        reader.Calls = 0;
        Assert.ThrowsException<UnauthorizedAccessException>(() => AcquisitionLocalRecoveryBatchCapacity.Inspect(
            [new([work], [], 1, false)], 50 * GiB, reader, new StoragePolicy {Allowed = false}));
        Assert.AreEqual(0, reader.Calls);
    }

    private sealed class CapacityReader(params StorageVolumeCapacity[] values) : IAcquisitionBatchCapacityReader
    {
        internal bool SameVolume = true;
        internal int Calls;
        public StorageVolumeCapacity Measure(string path)
        {
            Calls++;
            return values.Single(v => v.VolumePath == path);
        }
        public string Identity(StorageVolumeCapacity capacity) => SameVolume ? "test-volume" : capacity.VolumePath;
    }

    private sealed class StoragePolicy : IUserStoragePolicy
    {
        internal bool Allowed = true;
        public bool IsRestricted => !Allowed;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => Allowed;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {
            if (!Allowed) throw new UnauthorizedAccessException();
        }
        public void EnsureTreeMutationAllowed(string path) => EnsurePathAllowed(path);
    }
}
