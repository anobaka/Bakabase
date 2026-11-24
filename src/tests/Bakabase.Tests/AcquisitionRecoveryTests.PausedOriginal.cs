using System.Globalization;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
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
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

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
    [DataRow("slow-local-check", true)]
    [DataRow("checkpoint-changed-before-confirmation", true)]
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
        if (scenario == "slow-local-check")
        {
            var localChecks = 0;
            provider.DuringPausedProof = ct => ++localChecks == 2
                ? Task.Delay(TimeSpan.FromSeconds(13), ct) : Task.CompletedTask;
        }
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
        if (scenario == "checkpoint-changed-before-confirmation")
            host.BeforeValidation = () => {provider.HasCheckpoint = false; return Task.CompletedTask;};
        var remoteChecksBeforeConfirmation = provider.Checks;
        var outcome = await service.ApplyAsync(new(preview.PreviewToken, [task.Id], reserve));
        Assert.AreEqual(remoteChecksBeforeConfirmation, provider.Checks,
            "Confirmation should recheck the local checkpoint under the run gate without another remote probe.");
        if (scenario == "checkpoint-changed-before-confirmation")
        {
            Assert.AreEqual(CloudDownloadRecoveryService.PausedOriginalLocalReview, outcome.Skipped.Single().ReasonCode);
            Assert.AreEqual(0, host.Calls);
            Assert.IsNull(AcquisitionDownloadContinuationService.Read(await _db.AcquisitionTasks.AsNoTracking().SingleAsync())?.ApprovedPausedSignalHash);
            return;
        }
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

        if (hasCheckpoint && stoppedByDownloader)
        {
            var other = new AcquisitionTaskDbModel
                {Title = "An unrelated task", Status = AcquisitionStatus.Cancelled};
            _db.AcquisitionTasks.Add(other);
            await _db.SaveChangesAsync();
            provider.DuringPausedProof = ct => Task.Delay(TimeSpan.FromSeconds(5), ct);

            var multiple = await batchRecovery.PreviewAsync(new([task.Id, other.Id], "automatic"));
            var slow = multiple.Rows.Single(previewRow => previewRow.TaskId == task.Id);
            Assert.AreEqual("download.awaitCompletion", slow.CurrentStepKind);
            Assert.AreEqual(AcquisitionRecoveryDiagnostics.PausedOriginalCheckTimedOut, slow.ReasonCode);
            Assert.IsFalse(slow.CanQueue);
            Assert.AreEqual("RECOVERY_ALREADY_COMPLETED", multiple.Rows.Single(previewRow => previewRow.TaskId == other.Id).ReasonCode);
        }
        var preview = await batchRecovery.PreviewAsync(new([task.Id], "automatic"));
        provider.DuringPausedProof = null;
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
        Assert.AreEqual("configured", AcquisitionStorageOverrideGrant.Read(
            await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id))?.ReserveMode);
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task Automatic_list_continuation_preserves_other_paused_groups_until_their_own_confirmation(
        bool sourceRecoversDuringAdmission, bool cancelWhileChecking)
    {
        var provider = new PausedOriginalProvider {HasCheckpoint = true, UnavailableSourceId = "second",
            UnavailableProbesRemaining = sourceRecoversDuringAdmission ? 1 : int.MaxValue};
        (_services as IDisposable)?.Dispose();
        _services = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.RemoveAll<ICloudDownloadProvider>();
            services.AddSingleton<ICloudDownloadProvider>(provider);
            services.AddTransient(sp => new BakabaseLocalizer(sp.GetRequiredService<IStringLocalizer<SharedResource>>()));
            services.RemoveAll<IStorageWriteGuard>();
            services.AddSingleton<IStorageWriteGuard>(sp => new StorageWriteGuard(
                sp.GetRequiredService<IStorageWritePolicyProvider>(), _ => (_root, 1L << 40), TimeSpan.FromMilliseconds(500)));
        });
        _db = _services.GetRequiredService<BakabaseDbContext>();
        var (task, run, workset, firstDownload, firstOperation, _, _) = await SeedRecoverableProvider();
        var firstTarget = workset.Targets.Single();
        var secondSource = firstTarget.SelectedSource! with {Id = "second", Url = "https://mypikpak.com/s/second-fixture"};
        var secondTarget = firstTarget with {Id = "second", Sources = [secondSource], SelectedSourceId = secondSource.Id, DownloadTaskId = null};
        var secondIntent = DownloadArtifactCoordinator.DownloadIntent(secondTarget, _root);
        var secondRequest = new CloudDownloadRequest(secondIntent, secondSource, null, null, Path.Combine(_root, "second-original"));
        var secondDownload = new DownloadTaskDbModel {Key = "second", Type = 1, Status = DownloadTaskDbModelStatus.Disabled,
            ThirdPartyId = firstDownload.ThirdPartyId, DownloadPath = secondRequest.OutputDirectory,
            Options = JsonSerializer.Serialize(secondRequest, WorkflowJson.Options)};
        firstDownload.Key = "first";
        firstDownload.Type = 1;
        firstDownload.Status = DownloadTaskDbModelStatus.Disabled;
        firstDownload.Message = null;
        _db.DownloadTasks.Add(secondDownload);
        await _db.SaveChangesAsync();
        _db.CloudDownloadOperations.Add(new() {Id = secondIntent, TargetId = secondTarget.Id, WorkflowRunId = run.Id,
            Kind = "download", State = "started", DownloadTaskId = secondDownload.Id, RequestJson = secondDownload.Options});
        _db.ArtifactOperationBindings.Add(new() {Id = "second-download", OwnerKind = "workflow", OwnerId = run.Id.ToString(),
            Slot = secondTarget.Id + ":download", OperationId = secondIntent});
        workset = workset with {Id = $"acquisition:{task.Id}:{task.PlanRevision}",
            Targets = [firstTarget, secondTarget with {DownloadTaskId = secondDownload.Id}]};
        task.PlanJson = JsonSerializer.Serialize(new AcquisitionPlan {Title = task.Title!, OutputDirectory = _root,
            Parts = workset.Targets.Select(t => new AcquisitionPlanPart {Id = t.Id}).ToList()}, WorkflowJson.Options);
        task.BuilderStateJson = AcquisitionDownloadContinuationService.WithState(task.BuilderStateJson,
            new(run.Id, AcquisitionDownloadContinuationService.Paused, DateTimeOffset.UtcNow));
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        run.WaitReason = "cloud.cloudAttention";
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task",
            EntityId = task.Id.ToString(CultureInfo.InvariantCulture), Revision = task.PlanRevision.ToString(CultureInfo.InvariantCulture)});
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var storage = new UserStoragePolicy(false);
        var artifacts = _services.GetRequiredService<IArtifactOperationStore>();
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, storage, artifacts,
            downloads: _services.GetRequiredService<CloudDownloadRecoveryService>());
        var host = new RecordingHost();
        var recovery = new AcquisitionRecoveryService(_db, diagnostics, new(), host);
        var continuation = new AcquisitionContinuationService(_db, recovery, new(), null!, null!, artifacts);
        var queue = new AcquisitionLocalRecoveryBatchService(_db, recovery, diagnostics, null!, null!,
            new TestBOptionsManager<AcquisitionOptions>(new()), storage, new PinnedBatchCapacity(), new(), continuations: continuation);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        if (cancelWhileChecking)
        {
            var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.DuringProbe = () => {probeStarted.TrySetResult(); return finishProbe.Task;};
            var preview = await queue.PreviewAsync(new([task.Id], "automatic"), timeout.Token);
            var batch = await queue.ApplyAsync(new(preview.PreviewToken, [task.Id]), timeout.Token);
            var dispatch = queue.DispatchAsync(task.Id, timeout.Token);
            try
            {
                await probeStarted.Task.WaitAsync(timeout.Token);
                using var commandTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var cancelled = await queue.CommandAsync(batch.BatchId, "cancel", commandTimeout.Token);
                Assert.AreEqual("cancelled", cancelled.Rows.Single().Status);
                Assert.IsFalse(dispatch.IsCompleted);
            }
            finally
            {
                finishProbe.TrySetResult();
                await dispatch;
            }
            Assert.AreEqual("cancelled", (await queue.GetAsync(batch.BatchId)).Rows.Single().Status);
            Assert.AreEqual(0, host.Calls);
            Assert.AreEqual(0, provider.PreparedStarts.Count);
            Assert.IsTrue((await _db.DownloadTasks.AsNoTracking().ToArrayAsync()).All(d => d.Status == DownloadTaskDbModelStatus.Disabled));
            return;
        }

        async Task<WorkflowItemOutcome> QueueAndRun(string expectedTarget)
        {
            var preview = await queue.PreviewAsync(new([task.Id], "automatic"), timeout.Token);
            var row = preview.Rows.Single();
            Assert.IsTrue(row.CanQueue, row.ReasonCode);
            Assert.AreEqual(AcquisitionRecoveryDiagnostics.PausedOriginalReady, row.ReasonCode);
            var batch = await queue.ApplyAsync(new(preview.PreviewToken, [task.Id]), timeout.Token);
            var previousCalls = host.Calls;
            var previousChecks = provider.Checks;
            await queue.TickAsync(timeout.Token);
            Assert.AreEqual(previousCalls + 1, host.Calls, (await queue.GetAsync(batch.BatchId)).Rows.Single().ReasonCode);
            Assert.AreEqual(workset.Targets.Count(t => t.DownloadArtifactId == null), provider.Checks - previousChecks,
                "Successful queue admission probes each candidate once before acquiring the run gate.");
            var answer = JsonSerializer.Deserialize<CloudSelectionSignal>(host.Signal!, WorkflowJson.Options)!;
            CollectionAssert.AreEqual(new[] {expectedTarget}, answer.ResumeTargetIds);
            await _db.WorkflowRuns.Where(r => r.Id == run.Id).ExecuteUpdateAsync(s =>
                s.SetProperty(r => r.Status, WorkflowRunStatus.Running), timeout.Token);
            using var scope = _services.CreateScope();
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.AllowPayloadToFinish = finished.Task;
            var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0,
                TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = "{}",
                Services = scope.ServiceProvider, Logger = NullLogger.Instance,
                ReportStage = _ => {finished.TrySetResult(); return Task.CompletedTask;}};
            return await new AwaitCloudDownloadsActivity().ResumeAsync(context, workset, host.Signal, timeout.Token);
        }

        var first = await QueueAndRun(firstTarget.Id);
        Assert.AreEqual("cloud.cloudAttention", first.Suspension?.Reason);
        workset = (DownloadWorkset)first.Suspension!.Item!;
        Assert.IsNotNull(workset.Targets[0].DownloadArtifactId);
        Assert.IsNull(workset.Targets[1].DownloadArtifactId);
        Assert.AreEqual(DownloadTaskDbModelStatus.Disabled,
            (await _db.DownloadTasks.AsNoTracking().SingleAsync(d => d.Id == secondDownload.Id)).Status);
        Assert.AreEqual(AcquisitionDownloadContinuationService.Paused,
            AcquisitionDownloadContinuationService.Read(await _db.AcquisitionTasks.AsNoTracking().SingleAsync())?.Mode);
        CollectionAssert.AreEqual(new[] {firstOperation.Id}, provider.PreparedStarts.ToArray());

        var snapshot = WorkflowItemSnapshot.Capture(workset);
        await _db.WorkflowRuns.Where(r => r.Id == run.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Waiting)
            .SetProperty(r => r.CurrentItemJson, snapshot).SetProperty(r => r.WaitingSince, DateTime.Now), timeout.Token);
        await queue.TickAsync(timeout.Token);
        provider.UnavailableSourceId = null;
        var second = await QueueAndRun(secondTarget.Id);
        Assert.IsNull(second.Suspension);
        Assert.AreEqual(AcquisitionDownloadContinuationService.Automatic,
            AcquisitionDownloadContinuationService.Read(await _db.AcquisitionTasks.AsNoTracking().SingleAsync())?.Mode);
        CollectionAssert.AreEqual(new[] {firstOperation.Id, secondIntent}, provider.PreparedStarts.ToArray());
        Assert.AreEqual(2, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(2, await _db.DownloadResults.CountAsync());
        Assert.AreEqual(2, await _db.CloudDownloadOperations.CountAsync());
    }

    private sealed class PausedOriginalProvider : RecoverableProvider, ICloudDownloadRecoveryProvider, ICloudPreparedDownloadRetryProvider
    {
        public bool SupportsPausedPreparedDownload => true;
        public bool HasCheckpoint { get; set; }
        public Func<CancellationToken, Task>? DuringPausedProof { get; set; }
        public string? UnavailableSourceId { get; set; }
        public int UnavailableProbesRemaining { get; set; }
        public Task? AllowPayloadToFinish { get; set; }
        public List<string> PreparedStarts { get; } = [];
        Task<CloudReadiness> ICloudDownloadRecoveryProvider.RevalidateDownloadAsync(CloudDownloadRequest request, CancellationToken ct)
        {
            Available = request.Source.Id != UnavailableSourceId || UnavailableProbesRemaining <= 0;
            if (!Available) UnavailableProbesRemaining--;
            return base.RevalidateDownloadAsync(request, ct);
        }
        public Task<CloudReadiness> CheckPreparedDownloadRetryAsync(CloudDownloadRequest request, CancellationToken ct) =>
            Task.FromResult(new CloudReadiness(true));
        public async Task<CloudReadiness> CheckPausedPreparedDownloadAsync(CloudDownloadRequest request, CancellationToken ct)
        {
            if (DuringPausedProof != null) await DuringPausedProof(ct);
            return new(HasCheckpoint, HasCheckpoint ? null : "PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");
        }
        public async Task<CloudDownloadResult> RetryPreparedDownloadAsync(CloudDownloadRequest request,
            Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
        {
            Assert.IsNotNull(AllowPayloadToFinish, "Preview and guarded submission cannot read a payload.");
            PreparedStarts.Add(request.OperationId);
            await AllowPayloadToFinish.WaitAsync(ct);
            Directory.CreateDirectory(request.OutputDirectory);
            var file = Path.Combine(request.OutputDirectory, "downloaded.bin");
            await File.WriteAllTextAsync(file, request.Source.Id, ct);
            return new([file]);
        }
    }
}
