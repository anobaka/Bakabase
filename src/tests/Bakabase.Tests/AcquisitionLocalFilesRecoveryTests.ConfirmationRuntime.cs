using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private async Task<AcquisitionRuntimeOverview> ConfirmationOverview(int taskId)
    {
        await using var scope = _services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<AcquisitionRuntimeOverviewService>().GetAsync([taskId])).Single();
    }

    private static void AssertFileConfirmation(AcquisitionRuntimeOverview row, int revision, int? runId)
    {
        Assert.AreEqual(revision, row.PlanRevision);
        Assert.AreEqual(runId, row.WorkflowRunId);
        Assert.AreEqual("verifyingFiles", row.Stage);
        Assert.AreEqual("confirmation", row.ProgressScope);
        Assert.AreEqual("fileProcessing", row.Capability?.Id);
        Assert.AreEqual("running", row.Capability?.Status);
        Assert.IsNull(row.Progress);
        Assert.IsNull(row.CompletedBytes);
        Assert.IsNull(row.TotalBytes);
        Assert.IsNull(row.SpeedBytesPerSecond);
        Assert.IsNull(row.EstimatedRemainingSeconds);
        Assert.IsFalse(row.BytesPartial);
    }

    [DataTestMethod]
    [DataRow("prepare")]
    [DataRow("changed")]
    [DataRow("cancel")]
    public async Task LocalFilesConfirmationRuntimeFollowsTheAcceptedPreparation(string outcome)
    {
        var runtime = _services.GetRequiredService<AcquisitionConfirmationRuntime>();
        var f = await LocalFilesFixture(confirmations: runtime);
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"), default, _ =>
        {
            Assert.IsNull(runtime.Read(f.TaskId, 1, f.RunId));
            return Task.CompletedTask;
        });
        Assert.IsTrue(preview.Rows.Single().CanContinue);
        var original = await ConfirmationOverview(f.TaskId);
        if (outcome == "changed")
        {
            var file = JsonSerializer.Deserialize<string[]>((await _db.DownloadResults.AsNoTracking().SingleAsync()).FilesJson)![0];
            var bytes = await File.ReadAllBytesAsync(file);
            bytes[0] ^= 1;
            await File.WriteAllBytesAsync(file, bytes);
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var applying = f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId], PrepareOnly: true), cancellation.Token,
            async _ => {entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20));});
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var observed = await ConfirmationOverview(f.TaskId);
            AssertFileConfirmation(observed, 1, f.RunId);
            Assert.AreEqual("waiting", observed.WorkflowStatus);
            Assert.AreEqual(original.CurrentNodeKind, observed.CurrentNodeKind);
            Assert.AreEqual(original.CompletedDownloadCount, observed.CompletedDownloadCount);
            if (outcome == "cancel")
            {
                cancellation.Cancel();
                Assert.IsNull(runtime.Read(f.TaskId, 1, f.RunId));
            }
        }
        finally { release.TrySetResult(); }

        if (outcome == "cancel") await Assert.ThrowsAsync<OperationCanceledException>(async () => await applying);
        else
        {
            var result = await applying;
            if (outcome == "prepare") Assert.AreEqual("prepared", result.LocalFiles!.Single().Status);
            else Assert.AreEqual("RECOVERY_FILES_UNVERIFIED", result.Skipped.Single().ReasonCode);
        }
        Assert.IsNull(runtime.Read(f.TaskId, 1, f.RunId));
        Assert.AreNotEqual("confirmation", (await ConfirmationOverview(f.TaskId)).ProgressScope);
        Assert.AreEqual(0, f.Host.Calls);
    }

    private sealed class InspectingFileStorage : IUserStoragePolicy
    {
        private readonly UserStoragePolicy inner = new(false);
        public Action<string>? Inspect { get; set; }
        public bool IsRestricted => inner.IsRestricted;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose) => inner.GetRoots(purpose);
        public bool IsPathAllowed(string path, UserStoragePurpose purpose) => inner.IsPathAllowed(path, purpose);
        public void EnsureTreeMutationAllowed(string path) => inner.EnsureTreeMutationAllowed(path);
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose)
        {
            inner.EnsurePathAllowed(path, purpose);
            Inspect?.Invoke(path);
        }
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ReadyRunConfirmationRuntimeCoversOnlyTheCurrentRevisionBeforeLaunching(bool validInput)
    {
        var runtime = _services.GetRequiredService<AcquisitionConfirmationRuntime>();
        var storage = new InspectingFileStorage();
        var f = await LocalFilesFixture(confirmations: runtime, storagePolicy: storage);
        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        var prepare = await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId], PrepareOnly: true));
        var revision = prepare.LocalFiles!.Single().PlanRevision;
        var file = JsonSerializer.Deserialize<string[]>((await _db.DownloadResults.AsNoTracking().SingleAsync()).FilesJson)![0];
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.Inspect = path =>
        {
            if (path != file) return;
            entered.TrySetResult();
            release.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        };
        var run = Task.Run(() => f.Plans.RunAsync(f.TaskId, revision, default));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var observed = await ConfirmationOverview(f.TaskId);
            AssertFileConfirmation(observed, revision, null);
            Assert.IsNull(observed.WorkflowStatus);
            Assert.IsNull(observed.CurrentNodeKind);
            Assert.IsNull(runtime.Read(f.TaskId, 1, f.RunId));
            Assert.AreEqual(0, f.Host.Calls);
            if (!validInput) await File.WriteAllTextAsync(file, "changed during confirmation");
        }
        finally { release.TrySetResult(); }
        if (validInput) Assert.IsNotNull((await run).Task.WorkflowRunId);
        else
        {
            var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await run);
            Assert.AreEqual("PLAN_REPLAN_EVIDENCE_UNAVAILABLE", failure.Message);
        }
        Assert.IsNull(runtime.Read(f.TaskId, revision, null));
        Assert.AreEqual(validInput ? 1 : 0, f.Host.Calls);
    }

    [TestMethod]
    public void ConcurrentConfirmationObserversExpireIndependentlyAcrossTaskRevisionAndRun()
    {
        var runtime = new AcquisitionConfirmationRuntime();
        var task = new AcquisitionTaskDbModel {Id = 3, PlanRevision = 7};
        var first = new WorkflowActivityRuntimeSnapshot {Stage = "first"};
        var second = new WorkflowActivityRuntimeSnapshot {Stage = "second"};
        using var cancellation = new CancellationTokenSource();
        using var earlier = runtime.Observe(task, first, cancellation.Token);
        using var later = runtime.Observe(task, second, default);
        task.WorkflowRunId = 12;
        using var running = runtime.Observe(task, first, default);
        task.PlanRevision++;
        using var next = runtime.Observe(task, second, default);
        task.Id++;
        using var other = runtime.Observe(task, second, default);
        cancellation.Cancel();
        earlier.Dispose();
        Assert.AreSame(second, runtime.Read(3, 7, null));
        Assert.AreSame(first, runtime.Read(3, 7, 12));
        Assert.AreSame(second, runtime.Read(3, 8, 12));
        Assert.AreSame(second, runtime.Read(4, 8, 12));
        later.Dispose();
        Assert.IsNull(runtime.Read(3, 7, null));
        Assert.IsNull(runtime.Read(4, 8, null));
        Assert.ThrowsExactly<OperationCanceledException>(() => runtime.Observe(task, first, cancellation.Token));
        Assert.AreSame(second, runtime.Read(4, 8, 12));
    }
}
