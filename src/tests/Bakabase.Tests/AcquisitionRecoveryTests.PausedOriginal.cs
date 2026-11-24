using System.Globalization;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.TestKit.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow("waiting", true)]
    [DataRow("cancelled", true)]
    [DataRow("interrupted", true)]
    [DataRow("admission-failed", true)]
    [DataRow("stale-approved-answer", true)]
    [DataRow("cancelled-configured", true)]
    [DataRow("cancelled-emergency", true)]
    [DataRow("cancelled-inherit", true)]
    [DataRow("no-checkpoint", false)]
    [DataRow("explicit-cancel", false)]
    [DataRow("stale-revision", false)]
    [DataRow("retired-owner", false)]
    [DataRow("no-pause", false)]
    public async Task Paused_original_can_only_continue_its_confirmed_current_download(string scenario, bool ready)
    {
        var (task, run, workset, download, operation, _, _) = await SeedRecoverableProvider();
        var cancelled = scenario.StartsWith("cancelled", StringComparison.Ordinal) || scenario == "explicit-cancel";
        run.Status = cancelled ? WorkflowRunStatus.Cancelled : scenario == "interrupted" ? WorkflowRunStatus.Interrupted : WorkflowRunStatus.Waiting;
        run.ErrorMessage = scenario == "explicit-cancel" ? "Cancelled: the acquisition was cancelled" : null;
        task.Status = cancelled ? AcquisitionStatus.Cancelled : AcquisitionStatus.Waiting;
        task.DeletedAt = scenario == "retired-owner" ? DateTime.UtcNow : null;
        if (scenario != "no-pause")
            task.BuilderStateJson = AcquisitionDownloadContinuationService.WithState(task.BuilderStateJson,
                new(run.Id, AcquisitionDownloadContinuationService.Paused, DateTimeOffset.UtcNow));
        if (scenario == "cancelled-inherit")
        {
            var now = DateTimeOffset.UtcNow;
            task.BuilderStateJson = AcquisitionStorageOverrideGrant.WithState(task.BuilderStateJson,
                new(run.Id, task.PlanRevision, "old-reserve", now, now.AddHours(24), ReserveMode: "emergency"));
        }
        download.Status = DownloadTaskDbModelStatus.Disabled;
        download.Message = null;
        download.Progress = 24;
        workset = workset with
        {
            Id = FormattableString.Invariant($"acquisition:{task.Id}:{task.PlanRevision}"),
            Targets = [workset.Targets.Single() with {Problem = "The cloud download stopped. Resume this workflow to retry it."}]
        };
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        _db.Set<WorkflowRunSourceDbModel>().Add(new()
        {
            WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task",
            EntityId = task.Id.ToString(CultureInfo.InvariantCulture),
            Revision = (scenario == "stale-revision" ? task.PlanRevision + 1 : task.PlanRevision).ToString(CultureInfo.InvariantCulture)
        });
        await _db.SaveChangesAsync();
        var provider = new PausedOriginalProvider {HasCheckpoint = scenario != "no-checkpoint"};
        var recovery = new CloudDownloadRecoveryService(_db, [provider], new UserStoragePolicy(false));
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, new UserStoragePolicy(false),
            _services.GetRequiredService<IArtifactOperationStore>(), downloads: recovery);
        var host = new RecordingHost();
        var service = new AcquisitionRecoveryService(_db, diagnostics, new(), host);
        var continuation = new AcquisitionDownloadContinuationService(_db);
        var preview = await service.PreviewAsync(new([task.Id], "workflow"));
        Assert.AreEqual(ready, preview.Rows.Single().CanContinue, preview.Rows.Single().ReasonCode);
        if (!ready)
        {
            Assert.AreEqual(0, provider.Checks);
            Assert.AreEqual(0, host.Calls);
            return;
        }

        Assert.AreEqual(AcquisitionRecoveryDiagnostics.PausedOriginalReady, preview.Rows.Single().ReasonCode);
        var expected = await diagnostics.DiagnoseAsync(task, run, default);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            continuation.ConsumePausedSignalAsync(run.Id, workset.Id, expected.SignalJson, default));
        var reserve = scenario == "cancelled-emergency" ? true :
            scenario == "cancelled-configured" ? false : (bool?)null;
        var outcome = await service.ApplyAsync(new(preview.PreviewToken, [task.Id], reserve));
        CollectionAssert.AreEqual(new[] { task.Id }, outcome.ContinuedTaskIds.ToArray());
        Assert.AreEqual(1, host.Calls);
        if (scenario is "cancelled-configured" or "cancelled-emergency" or "cancelled-inherit")
        {
            var current = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            Assert.AreEqual(reserve == null ? "inherit" : reserve.Value ? "emergency" : "configured",
                AcquisitionStorageOverrideGrant.Read(current)?.ReserveMode);
        }
        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(host.Signal!, WorkflowJson.Options)!;
        var approved = signal.PreparedDownloadRetries!.Values.Single();
        Assert.IsTrue(approved.WasPaused);
        Assert.AreEqual(operation.Id, approved.OperationId);
        await recovery.ValidatePreparedAdmissionAsync(run.Id, workset.Targets.Single(), workset.OutputDirectory, approved, default);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            continuation.ConsumePausedSignalAsync(run.Id, workset.Id, host.Signal!, default));
        if (scenario is "admission-failed" or "stale-approved-answer")
        {
            if (scenario == "stale-approved-answer")
            {
                run.Status = WorkflowRunStatus.Interrupted;
                run.PendingSignalJson = host.Signal;
                await _db.SaveChangesAsync();
            }
            var retry = await service.PreviewAsync(new([task.Id], "workflow"));
            Assert.IsTrue(retry.Rows.Single().CanContinue, retry.Rows.Single().ReasonCode);
        }
        run.Status = WorkflowRunStatus.Running;
        run.PendingSignalJson = null;
        await _db.SaveChangesAsync();
        var admission = await continuation.ConsumePausedSignalAsync(run.Id, workset.Id, host.Signal!, default);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            continuation.ConsumePausedSignalAsync(run.Id, workset.Id, host.Signal!, default));
        if (scenario == "admission-failed")
        {
            run.Status = WorkflowRunStatus.Waiting;
            await _db.SaveChangesAsync();
            var retryAfterAdmission = await service.PreviewAsync(new([task.Id], "workflow"));
            Assert.IsTrue(retryAfterAdmission.Rows.Single().CanContinue, retryAfterAdmission.Rows.Single().ReasonCode);
        }
        else
        {
            await continuation.RequirePausedAdmissionWhileLockedAsync(run.Id, admission, default);
            run.Status = WorkflowRunStatus.Cancelled;
            run.ErrorMessage = "Cancelled: the acquisition was cancelled";
            await _db.SaveChangesAsync();
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                continuation.RequirePausedAdmissionWhileLockedAsync(run.Id, admission, default));
        }
        Assert.AreEqual(download.Id, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Id);
        Assert.AreEqual(operation.Id, (await _db.CloudDownloadOperations.AsNoTracking().SingleAsync()).Id);
    }

    [DataTestMethod]
    [DataRow(true, true)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task Acquisition_list_can_queue_a_paused_original_without_reopening_a_cancelled_task_arbitrarily(
        bool hasCheckpoint, bool stoppedByDownloader)
    {
        var (task, run, workset, download, operation, _, _) = await SeedRecoverableProvider();
        var source = workset.Targets.Single().SelectedSource!;
        task.PlanJson = JsonSerializer.Serialize(new AcquisitionPlan {Title = task.Title!, OutputDirectory = _root,
            Parts = [new() {Id = workset.Targets.Single().Id, SelectedSourceId = source.Id,
                Sources = [new() {Id = source.Id, Url = source.Url, ProviderId = source.ProviderId, Kind = source.Kind}]}]}, WorkflowJson.Options);
        task.Status = AcquisitionStatus.Cancelled;
        task.BuilderStateJson = AcquisitionDownloadContinuationService.WithState(task.BuilderStateJson,
            new(run.Id, AcquisitionDownloadContinuationService.Paused, DateTimeOffset.UtcNow));
        run.Status = WorkflowRunStatus.Cancelled;
        run.ErrorMessage = stoppedByDownloader ? null : "Cancelled: the acquisition was cancelled";
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Id = $"acquisition:{task.Id}:{task.PlanRevision}"});
        download.Status = DownloadTaskDbModelStatus.Disabled;
        download.Message = null;
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task",
            EntityId = task.Id.ToString(CultureInfo.InvariantCulture), Revision = task.PlanRevision.ToString(CultureInfo.InvariantCulture)});
        await _db.SaveChangesAsync();

        var storage = new UserStoragePolicy(false);
        var provider = new PausedOriginalProvider {HasCheckpoint = hasCheckpoint};
        var downloads = new CloudDownloadRecoveryService(_db, [provider], storage);
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, storage,
            _services.GetRequiredService<IArtifactOperationStore>(), downloads: downloads);
        var host = new RecordingHost();
        var recovery = new AcquisitionRecoveryService(_db, diagnostics, new(), host);
        var continuation = new AcquisitionContinuationService(_db, recovery, new(), null!, null!, null!);
        var options = new TestBOptionsManager<AcquisitionOptions>(new() {UseEmergencySpaceReserve = false});
        var batchRecovery = new AcquisitionLocalRecoveryBatchService(_db, recovery, diagnostics, null!, null!, options, storage,
            new PinnedBatchCapacity(), new(), continuations: continuation);

        var preview = await batchRecovery.PreviewAsync(new([task.Id], "automatic"));
        var row = preview.Rows.Single();
        Assert.AreEqual(hasCheckpoint && stoppedByDownloader, row.CanQueue, row.ReasonCode);
        Assert.AreEqual(0, provider.Checks);
        if (!row.CanQueue) return;
        Assert.AreEqual(AcquisitionRecoveryDiagnostics.PausedOriginalReady, row.ReasonCode);
        var batch = await batchRecovery.ApplyAsync(new(preview.PreviewToken, [task.Id], ReserveMode: "configured"));
        await batchRecovery.TickAsync();
        Assert.AreEqual(1, host.Calls, (await batchRecovery.GetAsync(batch.BatchId)).Rows.Single().ReasonCode);
        Assert.AreEqual(operation.Id, JsonSerializer.Deserialize<CloudSelectionSignal>(host.Signal!, WorkflowJson.Options)!
            .PreparedDownloadRetries!.Values.Single().OperationId);
        Assert.AreEqual(download.Id, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Id);
        Assert.AreEqual("configured", AcquisitionStorageOverrideGrant.Read(await _db.AcquisitionTasks.AsNoTracking().SingleAsync())?.ReserveMode);
    }

    private sealed class PausedOriginalProvider : RecoverableProvider, ICloudPreparedDownloadRetryProvider
    {
        public bool HasCheckpoint { get; init; }
        public Task<CloudReadiness> CheckPreparedDownloadRetryAsync(CloudDownloadRequest request, CancellationToken ct) =>
            Task.FromResult(new CloudReadiness(true));
        public Task<CloudReadiness> CheckPausedPreparedDownloadAsync(CloudDownloadRequest request, CancellationToken ct) =>
            Task.FromResult(new CloudReadiness(HasCheckpoint));
        public Task<CloudDownloadResult> RetryPreparedDownloadAsync(CloudDownloadRequest request,
            Func<CloudDownloadProgress, Task> progress, CancellationToken ct) =>
            throw new AssertFailedException("Preview and guarded submission cannot read a payload.");
    }
}
