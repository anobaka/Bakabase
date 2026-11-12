using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OriginalDownloadRecoveryAppliesExplicitReserveBeforeStartingSameDownload(bool emergency)
    {
        var (task, run, _, download, _, _, recovery) = await SeedRecoverableProvider();
        var starter = new OnlyDownloadStarter(this, run.Id); var host = new RecordingHost();
        starter.BetweenLeases = async () =>
        {
            var current = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            var grant = AcquisitionStorageOverrideGrant.Read(current)!;
            Assert.IsNotNull(grant); Assert.AreEqual(emergency ? "emergency" : "configured", grant.ReserveMode);
            Assert.AreEqual(run.Id, grant.WorkflowRunId); Assert.AreEqual(task.PlanRevision, grant.PlanRevision);
        };
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host,
            downloadOnly: OnlyDownloads(recovery, starter, new()));
        var preview = await service.PreviewAsync(new([task.Id], "downloads"));
        Assert.IsTrue(preview.Rows.Single().CanOverrideStorage);
        var input = new AcquisitionRecoveryApplyInput(preview.PreviewToken, [task.Id], emergency);
        var result = await service.ApplyAsync(input);
        CollectionAssert.AreEqual(new[] {task.Id}, result.ContinuedTaskIds.ToArray());
        CollectionAssert.AreEqual(new[] {download.Id}, starter.Calls);
        await service.ApplyAsync(input); Assert.AreEqual(1, starter.Calls.Count); Assert.AreEqual(0, host.Calls);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ApplyAsync(input with {UseEmergencySpaceReserve = !emergency}));
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }
}
