using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Components.ResourceMove;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class BTaskSuspensionTests
{
    private BTaskManager _manager = null!;
    private IServiceProvider _provider = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _provider = await TestServiceBuilder.BuildServiceProvider();
        _manager = _provider.GetRequiredService<BTaskManager>();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_provider is IAsyncDisposable disposable) await disposable.DisposeAsync();
    }

    private async Task WaitFor(string id, BTaskStatus status)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (_manager.GetTaskViewModel(id)?.Status == status) return;
            await Task.Delay(10);
        }
        Assert.Fail($"Expected {id}: {status}; actual {_manager.GetTaskViewModel(id)?.Status}");
    }

    [TestMethod]
    public async Task Suspension_PreservesProgress_ReleasesSlot_RequiresExplicitRequeue()
    {
        var runs = 0;
        await _manager.Enqueue(BTaskBuilder.Create("suspended")
            .ConflictsWith("move-test")
            .Run(async args =>
            {
                if (++runs == 1)
                {
                    await args.UpdateTask(t => t.Percentage = 35);
                    throw new BTaskSuspendedException("Choose how to handle this file.");
                }
            }));
        await _manager.Start("suspended");
        await WaitFor("suspended", BTaskStatus.WaitingForInput);
        Assert.AreEqual(35, _manager.GetTaskViewModel("suspended")!.Percentage);

        await _manager.Start("suspended");
        await _manager.CleanInactive();
        Assert.AreEqual(BTaskStatus.WaitingForInput, _manager.GetTaskViewModel("suspended")!.Status);
        Assert.AreEqual(1, runs);

        await _manager.Enqueue(BTaskBuilder.Create("next").ConflictsWith("move-test").Run(_ => Task.CompletedTask));
        await _manager.Start("next");
        await WaitFor("next", BTaskStatus.Completed);

        await _manager.Requeue("suspended");
        await _manager.Start("suspended");
        await WaitFor("suspended", BTaskStatus.Completed);
        Assert.AreEqual(2, runs);
    }

    [TestMethod]
    public async Task DomainStop_CancelsQueuedTaskWithoutRunningIt()
    {
        var runs = 0;
        var stops = 0;
        await _manager.Enqueue(BTaskBuilder.Create("queued-domain")
            .OnStop(async () =>
            {
                stops++;
                await _manager.MarkCancelling("queued-domain");
                await _manager.MarkCancelled("queued-domain");
            })
            .Run(_ => { runs++; return Task.CompletedTask; }));

        await _manager.Stop("queued-domain");
        await WaitFor("queued-domain", BTaskStatus.Cancelled);
        await _manager.Start("queued-domain");
        Assert.AreEqual(1, stops);
        Assert.AreEqual(0, runs);
    }

    [TestMethod]
    public async Task DomainStop_WaitsForSafeBoundary_WithoutCancellingPhysicalOperation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = false;
        await _manager.Enqueue(BTaskBuilder.Create("safe-stop")
            .OnStop(() => _manager.MarkCancelling("safe-stop"))
            .Run(async args =>
            {
                started.SetResult();
                await release.Task;
                cancellationObserved = args.CancellationToken.IsCancellationRequested;
                await _manager.MarkCancelled("safe-stop");
            }));
        await _manager.Start("safe-stop");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _manager.Stop("safe-stop");
        Assert.AreEqual(BTaskStatus.Cancelling, _manager.GetTaskViewModel("safe-stop")!.Status);
        release.SetResult();
        await WaitFor("safe-stop", BTaskStatus.Cancelled);
        Assert.IsFalse(cancellationObserved);
        Assert.AreNotEqual(100, _manager.GetTaskViewModel("safe-stop")!.Percentage);
    }

    [TestMethod]
    public async Task OrdinaryTask_StopAfterCommit_NormalReturnStillCompletes()
    {
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishPostCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committedChanges = 0;
        var cancellationObservedAfterCommit = false;
        await _manager.Enqueue(BTaskBuilder.Create("committed-stop")
            .Run(async args =>
            {
                committedChanges++;
                await args.UpdateTask(t => t.Percentage = 75);
                committed.SetResult();
                // Like DataSync's post-commit work, this cannot roll the successful operation
                // back and intentionally finishes without throwing the requested cancellation.
                await finishPostCommit.Task;
                cancellationObservedAfterCommit = args.CancellationToken.IsCancellationRequested;
            }));
        await _manager.Start("committed-stop");
        try
        {
            await committed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await _manager.Stop("committed-stop");
            Assert.AreEqual(BTaskStatus.Cancelling, _manager.GetTaskViewModel("committed-stop")!.Status);
        }
        finally
        {
            finishPostCommit.TrySetResult();
        }
        await WaitFor("committed-stop", BTaskStatus.Completed);
        Assert.AreEqual(1, committedChanges);
        Assert.IsTrue(cancellationObservedAfterCommit);
        Assert.AreEqual(100, _manager.GetTaskViewModel("committed-stop")!.Percentage);
    }

    [TestMethod]
    public async Task SuspendedTask_CanBeCancelledWithoutResuming()
    {
        var runs = 0;
        await _manager.Enqueue(BTaskBuilder.Create("waiting-cancel")
            .OnStop(() => _manager.MarkCancelled("waiting-cancel"))
            .Run(_ => { runs++; throw new BTaskSuspendedException(); }));
        await _manager.Start("waiting-cancel");
        await WaitFor("waiting-cancel", BTaskStatus.WaitingForInput);
        await _manager.Stop("waiting-cancel");
        await WaitFor("waiting-cancel", BTaskStatus.Cancelled);
        Assert.AreEqual(1, runs);
    }

    [TestMethod]
    public async Task RetainedMoveReservation_BlocksLegacySync_ButAllowsOtherMoveBatches()
    {
        var guard = _provider.GetRequiredService<ResourceMoveGuard>();
        guard.TryReserve("held", [1], ["/move-test/source", "/move-test/target"], out _);
        guard.Retain("held");
        await _manager.Enqueue(BTaskBuilder.Create("sync")
            .ConflictsWith("SyncResources").Run(_ => Task.CompletedTask));
        await _manager.Start("sync");
        Assert.AreEqual(BTaskStatus.NotStarted, _manager.GetTaskViewModel("sync")!.Status);

        await _manager.Enqueue(BTaskBuilder.Create("other-move").OfType(BTaskType.MoveResources)
            .ConflictsWith("SyncResources").Run(_ => Task.CompletedTask));
        await _manager.Start("other-move");
        await WaitFor("other-move", BTaskStatus.Completed);
        guard.Release("held");
        await _manager.Start("sync");
        await WaitFor("sync", BTaskStatus.Completed);
    }

    [TestMethod]
    public async Task StopDuringDelayedPause_DoesNotRePauseSafeCancellation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pausing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowPause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishResource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _manager.Enqueue(BTaskBuilder.Create("pause-stop")
            .OnStop(() => _manager.MarkCancelling("pause-stop"))
            .WhenStatusChanges(async (_, task) =>
            {
                if (task.Status == BTaskStatus.Pausing)
                {
                    pausing.TrySetResult();
                    await allowPause.Task;
                }
            })
            .Run(async args =>
            {
                started.SetResult();
                await finishResource.Task;
                await args.PauseToken.WaitWhilePausedAsync(args.CancellationToken);
                await _manager.MarkCancelled("pause-stop");
            }));
        await _manager.Start("pause-stop");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pause = _manager.Pause("pause-stop");
        await pausing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = _manager.Stop("pause-stop");
        allowPause.SetResult();
        await Task.WhenAll(pause, stop).WaitAsync(TimeSpan.FromSeconds(5));
        finishResource.SetResult();
        await WaitFor("pause-stop", BTaskStatus.Cancelled);
    }

    [TestMethod]
    public async Task Stop_CancelsAutomaticRetryEvenWhenAnOldCancellationSourceExists()
    {
        await _manager.Enqueue(BTaskBuilder.Create("retry-stop")
            .WithRetry(new BTaskRetryPolicy { MaxRetries = 2, InitialDelay = TimeSpan.FromMinutes(1) })
            .Run(_ => throw new InvalidOperationException("Retry later")));
        await _manager.Start("retry-stop");
        await WaitFor("retry-stop", BTaskStatus.NotStarted);
        await _manager.Stop("retry-stop");
        await WaitFor("retry-stop", BTaskStatus.Cancelled);
    }

    [TestMethod]
    public async Task RecoveryCheckpoint_RemainsVisibleEvenAfterCancellationWasRequested()
    {
        await _manager.Enqueue(BTaskBuilder.Create("recovery-stop")
            .OnStop(() => _manager.MarkCancelling("recovery-stop"))
            .Run(async _ =>
            {
                await _manager.MarkCancelling("recovery-stop");
                throw new BTaskSuspendedException("Recover files first", preserveOnCancellation: true);
            }));
        await _manager.Start("recovery-stop");
        await WaitFor("recovery-stop", BTaskStatus.WaitingForInput);
        await _manager.CleanInactive();
        Assert.AreEqual(BTaskStatus.WaitingForInput, _manager.GetTaskViewModel("recovery-stop")!.Status);
    }
}
