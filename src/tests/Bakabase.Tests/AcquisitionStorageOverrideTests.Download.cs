using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionStorageOverrideTests
{
    private static async Task DownloadStep(Fixture fixture)
    {
        var plan = WorkflowRunPlans.Serialize(new() {Name = "download", TriggerKind = "manual",
            Nodes = [new() {NodeId = "download", Kind = "download.awaitCompletion"}]});
        await fixture.Db.WorkflowRuns.Where(r => r.Id == 11).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.CurrentStepIndex, 0).SetProperty(r => r.PlanSnapshotJson, plan)
            .SetProperty(r => r.WaitPromptJson, "{\"message\":\"Provider confirmation required\"}"));
    }

    [DataTestMethod]
    [DataRow("{}")]
    [DataRow("{\"observeExistingOnly\":true,\"resumeTargetIds\":[\"finished\"]}")]
    [DataRow("{\"observeExistingOnly\":true,\"resumeTargetIds\":[\"foreign\"]}")]
    [DataRow("{\"observeExistingOnly\":true,\"resumeTargetIds\":[]}")]
    public async Task Multiple_original_downloads_require_a_scoped_or_observation_only_signal(string signal)
    {
        using var f = await Fixture.CreateAsync();
        await DownloadStep(f);
        var source = new CloudSource {Id = "original", ProviderId = "gofile", Url = "https://gofile.io/d/sample"};
        var finished = new CloudDownloadTarget {Id = "finished", Sources = [source], SelectedSourceId = source.Id,
            DownloadTaskId = 987, DownloadArtifactId = "retained"};
        var workset = new DownloadWorkset {Targets = [finished, finished with {Id = "missing", DownloadTaskId = 988, DownloadArtifactId = null}]};
        var snapshot = WorkflowItemSnapshot.Capture(workset);
        await f.Db.WorkflowRuns.Where(r => r.Id == 11).ExecuteUpdateAsync(u => u.SetProperty(r => r.CurrentItemJson, snapshot));
        var preview = await f.Service.PreviewAsync(1, scope: "download");
        Assert.AreEqual(2, preview.DownloadTargets!.Count);
        Assert.IsTrue(preview.DownloadTargets.All(row => !row.CanRetry));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Service.ApplyAsync(1, new(preview.Token, true,
            UseEmergencySpaceReserve: false, SignalJson: signal)));
        Assert.AreEqual(0, f.Host.Calls);
        var next = await f.Service.PreviewAsync(1, scope: "download");
        var result = await f.Service.ApplyAsync(1, new(next.Token, true, UseEmergencySpaceReserve: false,
            SignalJson: "{\"observeExistingOnly\":true}"));
        Assert.IsTrue(result.ContinuationAccepted);
        Assert.AreEqual(1, f.Host.Calls);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InheritedContinuationRestoresTheLiveGlobalReserveAcrossBothDirections(bool emergency)
    {
        using var f = await Fixture.CreateAsync(); await DownloadStep(f);
        f.Options.Value.UseEmergencySpaceReserve = emergency;
        var p = await f.Service.PreviewAsync(1, scope: "download");
        await f.Service.ApplyAsync(1, new(p.Token, true, UseEmergencySpaceReserve: !emergency, SignalJson: "{}"));
        Assert.AreEqual(!emergency ? Floor : 100 * Floor, await f.Provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
        var recovery = new AcquisitionRecoveryService(f.Db, f.Diagnostics, new(), f.Host);
        var next = await recovery.PreviewAsync(new([1]));
        Assert.IsTrue((await recovery.ApplyAsync(new(next.PreviewToken, [1]))).ContinuedTaskIds.Contains(1));
        Assert.AreEqual("inherit", AcquisitionStorageOverrideGrant.Read(await f.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1))!.ReserveMode);
        Assert.AreEqual(emergency ? Floor : 100 * Floor, await f.Provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
        f.Options.Value.UseEmergencySpaceReserve = !emergency;
        Assert.AreEqual(!emergency ? Floor : 100 * Floor, await f.Provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
    }

    [TestMethod]
    public async Task DownloadContinuationWithSmallConfiguredReserveStillKeepsItsOriginalFormUsable()
    {
        using var f = await Fixture.CreateAsync(); await DownloadStep(f); f.Options.Value.MinimumFreeSpaceBytes = 0;
        var p = await f.Service.PreviewAsync(1, scope: "download");
        var result = await f.Service.ApplyAsync(1, new(p.Token, true, UseEmergencySpaceReserve: true, SignalJson: "{\"confirm\":true}"));
        Assert.IsTrue(result.ContinuationAccepted); Assert.AreEqual(0L, await f.Provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DownloadContinuationPreservesTheOriginalAnswerAndExplicitChoiceOverridesGlobal(bool emergency)
    {
        using var f = await Fixture.CreateAsync(); await DownloadStep(f);
        f.Options.Value.UseEmergencySpaceReserve = !emergency;
        const string signal = "{\"confirm\":true,\"resumeTargetIds\":[\"existing\"],\"revalidateExistingDownloads\":true}";
        var p = await f.Service.PreviewAsync(1, scope: "download");
        Assert.AreEqual("download", p.Scope);
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await f.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1)));
        var input = new AcquisitionStorageOverrideApplyInput(p.Token, true, UseEmergencySpaceReserve: emergency, SignalJson: signal);
        f.Host.OnAccepted = async () =>
        {
            var current = await f.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1);
            Assert.IsNotNull(AcquisitionStorageOverrideGrant.Read(current));
            Assert.AreEqual("automatic", AcquisitionDownloadContinuationService.Read(current)!.Mode);
        };
        var result = await f.Service.ApplyAsync(1, input);
        Assert.IsTrue(result.ContinuationAccepted); Assert.AreEqual(signal, f.Host.LastSignal);
        Assert.AreEqual(emergency ? Floor : 100 * Floor, await f.Provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
        Assert.AreEqual(!emergency, f.Options.Value.UseEmergencySpaceReserve);
        Assert.AreEqual(!emergency ? Floor : 100 * Floor, await f.Provider.GetMinimumFreeBytesAsync(new("workflow", "22")));
        var task = await f.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1);
        var grant = AcquisitionStorageOverrideGrant.Read(task)!;
        Assert.AreEqual(11, grant.WorkflowRunId); Assert.AreEqual(1, grant.PlanRevision);
        Assert.AreEqual("automatic", AcquisitionDownloadContinuationService.Read(task)!.Mode);
        Assert.AreEqual(result, await f.Service.ApplyAsync(1, input)); Assert.AreEqual(1, f.Host.Calls);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Service.ApplyAsync(1, input with {SignalJson = "{}"}));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Service.ApplyAsync(1, input with {UseEmergencySpaceReserve = !emergency}));
        Assert.AreEqual(0, await f.Db.DownloadTasks.CountAsync(), "Reserve selection never manufactures a download.");
    }

    [DataTestMethod]
    [DataRow("changed")]
    [DataRow("pending")]
    [DataRow("paths")]
    [DataRow("invalid")]
    [DataRow("missing-choice")]
    public async Task DownloadContinuationRejectsStaleOrUnconfirmedChoicesBeforeGrant(string variant)
    {
        using var f = await Fixture.CreateAsync(); await DownloadStep(f);
        var p = await f.Service.PreviewAsync(1, scope: "download");
        if (variant == "changed") await f.Db.AcquisitionTasks.Where(t => t.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(t => t.PlanRevision, 2));
        if (variant == "pending") await f.Db.WorkflowRuns.Where(r => r.Id == 11).ExecuteUpdateAsync(s => s.SetProperty(r => r.PendingSignalJson, "{}"));
        var input = new AcquisitionStorageOverrideApplyInput(p.Token, true, UseEmergencySpaceReserve: variant == "missing-choice" ? null : true,
            SignalJson: variant == "invalid" ? "{\"confirm\":true,\"CONFIRM\":false}" : "{\"confirm\":true,\"outputDirectory\":\"/restricted\"}");
        try
        {
            await f.Service.ApplyAsync(1, input, guardDirectory: variant == "paths"
                ? _ => throw new UnauthorizedAccessException("CONTINUATION_PATH_FORBIDDEN") : null);
            Assert.Fail("Rejected input must not continue.");
        }
        catch (Exception error) when (error is ArgumentException or UnauthorizedAccessException) { }
        Assert.AreEqual(0, f.Host.Calls);
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await f.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1)));
    }

    [TestMethod]
    public async Task DownloadContinuationLostResponseDoesNotReplayOrWithdrawActiveReserve()
    {
        using var f = await Fixture.CreateAsync(); await DownloadStep(f); f.Host.ThrowAfterContinuation = true;
        var p = await f.Service.PreviewAsync(1, scope: "download");
        var input = new AcquisitionStorageOverrideApplyInput(p.Token, true, UseEmergencySpaceReserve: true, SignalJson: "{\"confirm\":true}");
        var result = await f.Service.ApplyAsync(1, input);
        Assert.AreEqual("STORAGE_OVERRIDE_OUTCOME_UNKNOWN", result.ReasonCode);
        Assert.AreEqual(result, await f.Service.ApplyAsync(1, input)); Assert.AreEqual(1, f.Host.Calls);
        Assert.AreEqual(Floor, await f.Provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
    }
}
