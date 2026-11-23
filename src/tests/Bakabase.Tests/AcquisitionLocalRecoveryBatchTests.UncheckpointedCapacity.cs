using Bakabase.Service.Components.Acquisition.Planning;

namespace Bakabase.Tests;

public sealed partial class AcquisitionLocalRecoveryBatchTests
{
    [TestMethod]
    [DataRow("continue")]
    [DataRow("retryTransfer")]
    public async Task UncheckpointedWorkStillRequiresItsProjectedBytesAboveTheReserve(string action)
    {
        using var f = await Fixture.CreateAsync();
        f.Capacity.Available = f.Options.Value.MinimumFreeSpaceBytes + 20;
        var recovery = new AcquisitionRecoveryService(f.Db, f.Diagnostics, new(), f.Host);
        var continuation = new AcquisitionContinuationService(f.Db, recovery, new(), null!, null!, null!);
        var service = new AcquisitionLocalRecoveryBatchService(f.Db, recovery, f.Diagnostics, null!, null!,
            f.Options, new StoragePolicy(), f.Capacity, f.Previews, continuations: continuation,
            actionProviders: [new AcquisitionTransferReplayAction(f.Diagnostics)]);
        f.Diagnostics.OptionalTransferTask = 1;
        var preview = await service.PreviewAsync(new([1], "automatic"));
        Assert.IsTrue(preview.Rows.Single().CanQueue);
        AcquisitionBatchActionSelection[] selected = [new(1, action)];
        var space = await service.PreviewCapacityAsync(new(preview.PreviewToken, Selections: selected));
        Assert.AreEqual(20L, space.Volumes.Single().UsableBytes);
        Assert.IsTrue(space.RequiresSpaceConfirmation);
        var queue = await service.ApplyAsync(new(preview.PreviewToken, Selections: selected));
        await service.TickAsync();
        Assert.AreEqual("spaceBlocked", (await service.GetAsync(queue.BatchId)).Status);
        Assert.AreEqual(0, f.Host.Calls.Count);
    }
}
