using System.Text.Json;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private sealed class CompletionHost(AcquisitionRecoveryTests fixture) : IWorkflowRunResumer
    {
        public int Calls;
        public Func<Task>? BeforeValidation;
        public Task ResumeAsync(int runId, string signalJson, CancellationToken ct = default) => throw new AssertFailedException("Use conditional continuation.");
        public Task RequeueAsync(int runId, CancellationToken ct = default) => throw new AssertFailedException("Never retry or replace a run.");
        public Task UpdateWaitAsync(int runId, string reason, string? promptJson, CancellationToken ct = default) => throw new AssertFailedException();
        public async Task ContinueConditionallyAsync(int runId, bool waiting, string signalJson, Func<CancellationToken, Task> validate, CancellationToken ct = default)
        {
            Assert.IsTrue(waiting); Assert.AreEqual("{\"observeExistingOnly\":true}", signalJson);
            var gate = WorkflowRunMutationGate.For(runId); await gate.WaitAsync(ct);
            try
            {
                if (BeforeValidation != null) await BeforeValidation();
                await validate(ct); Calls++;
                await fixture._db.WorkflowRuns.Where(r => r.Id == runId).ExecuteUpdateAsync(s =>
                    s.SetProperty(r => r.Status, WorkflowRunStatus.Pending).SetProperty(r => r.PendingSignalJson, signalJson), ct);
            }
            finally {gate.Release();}
        }
    }

    private AcquisitionDownloadCompletionCoordinator CompletionCoordinator(IWorkflowRunResumer host) => new(_db,
        _diagnostics, host, new(_db), _services.GetRequiredService<BTaskManager>());

    private async Task<AcquisitionTaskDbModel> CompletionIntent(AcquisitionTaskDbModel task, string? mode)
    {
        task.BuilderStateJson = "{\"query\":\"keep this\",\"unknownFutureState\":{\"kept\":true}}";
        if (mode != null) task.BuilderStateJson = AcquisitionDownloadContinuationService.WithState(task.BuilderStateJson,
            new(task.WorkflowRunId!.Value, mode, DateTimeOffset.UtcNow));
        var run = await _db.WorkflowRuns.SingleAsync(r => r.Id == task.WorkflowRunId);
        run.WaitReason = "cloud.cloudAttention"; run.ErrorMessage = null;
        await _db.SaveChangesAsync(); return task;
    }

    [TestMethod]
    public async Task CompletedDownloadAutomaticallyContinuesOnlyItsAuthorizedOriginalRunOnce()
    {
        var (task, run, _, _, operation) = await SeedRevisedCompletedDownload("download.awaitCompletion");
        await CompletionIntent(task, "automatic");
        var checkpoint = run.CurrentItemJson;
        var operationJson = JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id));
        var host = new CompletionHost(this);
        Assert.IsTrue(await CompletionCoordinator(host).ReconcileAsync(task.Id, default));
        // New coordinator represents both a duplicate completion event and the next startup pass.
        Assert.IsFalse(await CompletionCoordinator(host).ReconcileAsync(task.Id, default));
        Assert.AreEqual(1, host.Calls);
        var saved = await _db.WorkflowRuns.AsNoTracking().SingleAsync();
        Assert.AreEqual(run.Id, saved.Id); Assert.AreEqual(WorkflowRunStatus.Pending, saved.Status);
        Assert.AreEqual(checkpoint, saved.CurrentItemJson);
        Assert.AreEqual("{\"observeExistingOnly\":true}", saved.PendingSignalJson);
        Assert.AreEqual(operationJson, JsonSerializer.Serialize(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync()));
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync()); Assert.AreEqual(1, await _db.WorkflowRuns.CountAsync());
        var owner = await _db.AcquisitionTasks.AsNoTracking().SingleAsync();
        StringAssert.Contains(owner.BuilderStateJson, "unknownFutureState"); StringAssert.Contains(owner.BuilderStateJson, "keep this");
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("downloadsOnly")]
    [DataRow("paused")]
    [DataRow("unknown-mode")]
    public async Task CompletedDownloadCannotOverrideMissingOrHeldContinuationIntent(string? mode)
    {
        var (task, run, _, _, _) = await SeedRevisedCompletedDownload("download.awaitCompletion");
        await CompletionIntent(task, mode);
        var before = JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync());
        var host = new CompletionHost(this);
        Assert.IsFalse(await CompletionCoordinator(host).ReconcileAsync(task.Id, default));
        Assert.AreEqual(0, host.Calls); Assert.AreEqual(before, JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync()));
    }

    [DataTestMethod]
    [DataRow("changed-file")]
    [DataRow("missing-binding")]
    [DataRow("another-step")]
    [DataRow("cancelled")]
    [DataRow("pending-answer")]
    [DataRow("newer-run")]
    public async Task AutomaticCompletionRequiresOriginalVerifiedEvidenceAndCurrentWait(string change)
    {
        var (task, run, _, download, _) = await SeedRevisedCompletedDownload("download.awaitCompletion");
        await CompletionIntent(task, "automatic");
        switch (change)
        {
            case "changed-file": await File.WriteAllTextAsync(Path.Combine(download.DownloadPath, "official.bin"), "changed"); break;
            case "missing-binding": _db.ArtifactOperationBindings.RemoveRange(_db.ArtifactOperationBindings); break;
            case "another-step": run.CurrentStepIndex = 10; break;
            case "cancelled": run.Status = WorkflowRunStatus.Cancelled; break;
            case "pending-answer": run.PendingSignalJson = "{\"userAnswer\":true}"; break;
            case "newer-run": task.BuilderStateJson = AcquisitionDownloadContinuationService.WithState(task.BuilderStateJson,
                new(run.Id + 1, "automatic", DateTimeOffset.UtcNow)); break;
        }
        await _db.SaveChangesAsync();
        var host = new CompletionHost(this);
        Assert.IsFalse(await CompletionCoordinator(host).ReconcileAsync(task.Id, default));
        Assert.AreEqual(0, host.Calls); Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
        if (change == "changed-file")
        {
            Assert.IsTrue(AcquisitionDownloadContinuationService.Read(await _db.AcquisitionTasks.AsNoTracking().SingleAsync())!.NextCheckAt > DateTimeOffset.UtcNow);
            Assert.IsFalse(await CompletionCoordinator(host).ReconcileAsync(task.Id, default));
        }
    }

    [TestMethod]
    public async Task PauseArrivingDuringCompletionValidationPreventsContinuation()
    {
        var (task, _, _, _, _) = await SeedRevisedCompletedDownload("download.awaitCompletion");
        await CompletionIntent(task, "automatic");
        var host = new CompletionHost(this) {BeforeValidation = async () =>
        {
            var current = await _db.AcquisitionTasks.AsNoTracking().SingleAsync();
            var json = AcquisitionDownloadContinuationService.WithState(current.BuilderStateJson,
                new(current.WorkflowRunId!.Value, "paused", DateTimeOffset.UtcNow));
            await _db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, json));
        }};
        Assert.IsFalse(await CompletionCoordinator(host).ReconcileAsync(task.Id, default));
        Assert.AreEqual(0, host.Calls); Assert.AreEqual(WorkflowRunStatus.Waiting, (await _db.WorkflowRuns.AsNoTracking().SingleAsync()).Status);
    }

    [TestMethod]
    public async Task ExplicitWorkflowRecoveryReleasesDownloadsOnlyHoldAfterAcceptance()
    {
        var (task, _, _, _, _) = await SeedRevisedCompletedDownload("download.awaitCompletion");
        await CompletionIntent(task, "downloadsOnly");
        var host = new RecordingHost();
        var recovery = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var preview = await recovery.PreviewAsync(new([task.Id]));
        await recovery.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        Assert.AreEqual("automatic", AcquisitionDownloadContinuationService.Read(await _db.AcquisitionTasks.AsNoTracking().SingleAsync())!.Mode);
    }

    [TestMethod]
    public async Task RejectedWorkflowRecoveryDoesNotReleaseDownloadsOnlyHold()
    {
        var (task, _, _, download, _) = await SeedRevisedCompletedDownload("download.awaitCompletion");
        await CompletionIntent(task, "downloadsOnly");
        var host = new RecordingHost {BeforeValidation = () => File.WriteAllTextAsync(Path.Combine(download.DownloadPath, "official.bin"), "changed")};
        var recovery = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var preview = await recovery.PreviewAsync(new([task.Id]));
        var result = await recovery.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        Assert.AreEqual(0, result.ContinuedTaskIds.Count);
        Assert.AreEqual("downloadsOnly", AcquisitionDownloadContinuationService.Read(await _db.AcquisitionTasks.AsNoTracking().SingleAsync())!.Mode);
    }

    [DataTestMethod]
    [DataRow("paused", false)]
    [DataRow("downloadsOnly", false)]
    [DataRow("automatic", true)]
    public async Task PersistedHoldControlsStartupWithoutBlockingExplicitWorkflowActions(string mode, bool allowed)
    {
        var (task, run, _, _, _) = await SeedRevisedCompletedDownload("download.awaitCompletion");
        await CompletionIntent(task, mode);
        Assert.AreEqual(allowed, await new AcquisitionDownloadContinuationStartupPolicy(_db).CanResumeOnStartupAsync(run.Id));
        await new AcquisitionDownloadContinuationService(_db).SetBackgroundTaskIntentAsync("workflow.run." + run.Id, "automatic");
        Assert.IsTrue(await new AcquisitionDownloadContinuationStartupPolicy(_db).CanResumeOnStartupAsync(run.Id));
    }
}
