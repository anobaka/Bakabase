using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Coverage for BTaskManager scheduling beyond the cancellation path: a task runs to
/// Completed, a throwing task ends in Error, ConflictKeys block a conflicting task while
/// one runs, and tasks with disjoint keys run concurrently.
/// </summary>
[TestClass]
public sealed class BTaskManagerTests
{
    private IServiceProvider _sp = null!;
    private BTaskManager _btm = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _btm = _sp.GetRequiredService<BTaskManager>();
    }

    private static BTaskHandlerBuilder Build(
        string id,
        Func<BTaskArgs, Task> run,
        HashSet<string>? conflictKeys = null,
        Action<BTaskStatus>? onStatus = null)
        => BTaskBuilder.Create(id)
            .ConflictsWith(conflictKeys ?? [id])
            .Run(run)
            .WhenStatusChanges((_, task) =>
            {
                onStatus?.Invoke(task.Status);
                return Task.CompletedTask;
            });

    private async Task WaitForRunning(string id, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_btm.GetTaskViewModel(id)?.Status == BTaskStatus.Running) return;
            await Task.Delay(10);
        }
        throw new TimeoutException($"Task {id} did not reach Running within {timeout}");
    }

    [TestMethod]
    public async Task Task_RunsToCompletion()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string id = "btm-completes";

        await _btm.Enqueue(Build(id, _ => Task.CompletedTask,
            onStatus: s => { if (s == BTaskStatus.Completed) completed.TrySetResult(); }));
        await _btm.Start(id);

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(BTaskStatus.Completed, _btm.GetTaskViewModel(id)!.Status);
    }

    [TestMethod]
    public async Task FailingTask_EndsInErrorStatus()
    {
        var errored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string id = "btm-fails";

        await _btm.Enqueue(Build(id, _ => throw new InvalidOperationException("boom"),
            onStatus: s => { if (s == BTaskStatus.Error) errored.TrySetResult(); }));
        await _btm.Start(id);

        await errored.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(BTaskStatus.Error, _btm.GetTaskViewModel(id)!.Status);
    }

    [TestMethod]
    public async Task ConflictingTask_DoesNotStartWhileAConflictingTaskRuns()
    {
        var startedA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string key = "shared-key";

        await _btm.Enqueue(Build("btm-conflict-a",
            async args =>
            {
                startedA.TrySetResult();
                await Task.Delay(Timeout.Infinite, args.CancellationToken);
            },
            conflictKeys: [key]));
        await _btm.Start("btm-conflict-a");
        await startedA.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForRunning("btm-conflict-a", TimeSpan.FromSeconds(5));

        await _btm.Enqueue(Build("btm-conflict-b",
            async args =>
            {
                startedB.TrySetResult();
                await Task.Delay(Timeout.Infinite, args.CancellationToken);
            },
            conflictKeys: [key]));
        await _btm.Start("btm-conflict-b");

        // B shares a conflict key with the running A, so it must not start.
        var bStarted = await Task.WhenAny(startedB.Task, Task.Delay(400)) == startedB.Task;
        Assert.IsFalse(bStarted);

        await _btm.Stop("btm-conflict-a");
    }

    [TestMethod]
    public async Task NonConflictingTasks_RunConcurrently()
    {
        var startedA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await _btm.Enqueue(Build("btm-free-a",
            async args =>
            {
                startedA.TrySetResult();
                await Task.Delay(Timeout.Infinite, args.CancellationToken);
            },
            conflictKeys: ["key-a"]));
        await _btm.Enqueue(Build("btm-free-b",
            async args =>
            {
                startedB.TrySetResult();
                await Task.Delay(Timeout.Infinite, args.CancellationToken);
            },
            conflictKeys: ["key-b"]));

        await _btm.Start("btm-free-a");
        await _btm.Start("btm-free-b");

        // Disjoint conflict keys — both must reach Running.
        await Task.WhenAll(
            startedA.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            startedB.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        await _btm.Stop("btm-free-a");
        await _btm.Stop("btm-free-b");
    }

    [TestMethod]
    public async Task ConcurrencyGroup_ConcurrentStartsDoNotExceedLimitAndOtherGroupsRemainIndependent()
    {
        var ids = Enumerable.Range(0, 12).Select(i => $"bounded-{i}").ToArray();
        foreach (var id in ids)
            await _btm.Enqueue(BTaskBuilder.Create(id).WithConcurrencyLimit("parser", () => 2)
                .Run(args => Task.Delay(Timeout.Infinite, args.CancellationToken)));
        try
        {
            await Task.WhenAll(ids.Select(id => _btm.Start(id)));
            Assert.AreEqual(2, _btm.Tasks.Count(t => t.Task.Status.IsActive()));
            Assert.AreEqual(10, _btm.Tasks.Count(t => t.Task.Status == BTaskStatus.NotStarted));
            var queued = _btm.Tasks.First(t => t.Task.Status == BTaskStatus.NotStarted);
            Assert.IsFalse(string.IsNullOrWhiteSpace(_btm.GetTaskViewModel(queued.Id)!.ReasonForUnableToStart));

            await _btm.Enqueue(BTaskBuilder.Create("independent").WithConcurrencyLimit("other", () => 1)
                .Run(args => Task.Delay(Timeout.Infinite, args.CancellationToken)));
            await _btm.Start("independent");
            Assert.AreEqual(BTaskStatus.Running, _btm.GetTaskViewModel("independent")!.Status);

            var stopped = _btm.Tasks.First(t => ids.Contains(t.Id) && t.Task.Status.IsActive());
            await _btm.Stop(stopped.Id);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (stopped.HasAttachedExecution) await Task.Delay(10, timeout.Token);
            await _btm.Start(queued.Id);
            Assert.AreEqual(BTaskStatus.Running, queued.Task.Status);
        }
        finally { await _btm.PrepareForShutdown(); }
    }

    [TestMethod]
    public async Task ConcurrencyGroup_DynamicLimitsApplyToDaemonWithoutRestartingActiveTasks()
    {
        var limit = 1;
        foreach (var id in new[] {"dynamic-a", "dynamic-b", "dynamic-c"})
            await _btm.Enqueue(BTaskBuilder.Create(id).WithConcurrencyLimit("dynamic", () => Volatile.Read(ref limit))
                .Run(args => Task.Delay(Timeout.Infinite, args.CancellationToken)));
        try
        {
            await _btm.Start("dynamic-a");
            await _btm.Initialize();
            Assert.AreEqual(BTaskStatus.NotStarted, _btm.GetTaskViewModel("dynamic-b")!.Status);
            Volatile.Write(ref limit, 2);
            await WaitForRunning("dynamic-b", TimeSpan.FromSeconds(5));
            Assert.AreEqual(BTaskStatus.NotStarted, _btm.GetTaskViewModel("dynamic-c")!.Status);

            Volatile.Write(ref limit, 1);
            await _btm.Stop("dynamic-a");
            await _btm.Start("dynamic-c");
            Assert.AreEqual(BTaskStatus.Running, _btm.GetTaskViewModel("dynamic-b")!.Status);
            Assert.AreEqual(BTaskStatus.NotStarted, _btm.GetTaskViewModel("dynamic-c")!.Status);
            await _btm.Stop("dynamic-b");
            await WaitForRunning("dynamic-c", TimeSpan.FromSeconds(5));
        }
        finally { await _btm.PrepareForShutdown(); }
    }

    [TestMethod]
    public async Task ConcurrencyGroup_PausingAndCancellingRetainTheSlotUntilTheBodyExits()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _btm.Enqueue(BTaskBuilder.Create("retained").WithConcurrencyLimit("retained", () => 1)
            .Run(async args =>
            {
                entered.TrySetResult();
                await release.Task;
                args.CancellationToken.ThrowIfCancellationRequested();
            }));
        await _btm.Enqueue(BTaskBuilder.Create("waiting").WithConcurrencyLimit("retained", () => 1)
            .Run(_ => Task.CompletedTask));
        try
        {
            await _btm.Start("retained");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var handler = _btm.Tasks.Single(t => t.Id == "retained");
            await handler.Pause();
            await _btm.Start("waiting");
            Assert.AreEqual(BTaskStatus.NotStarted, _btm.GetTaskViewModel("waiting")!.Status);
            await handler.Resume();
            await _btm.Stop("retained");
            Assert.AreEqual(BTaskStatus.Cancelling, handler.Task.Status);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _btm.Clean("retained"));
            await _btm.Start("waiting");
            Assert.AreEqual(BTaskStatus.NotStarted, _btm.GetTaskViewModel("waiting")!.Status);
            release.TrySetResult();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (handler.HasAttachedExecution) await Task.Delay(10, timeout.Token);
            await _btm.Start("waiting");
            Assert.AreNotEqual(BTaskStatus.NotStarted, _btm.GetTaskViewModel("waiting")!.Status);
        }
        finally
        {
            release.TrySetResult();
            await _btm.PrepareForShutdown();
        }
    }

    [TestMethod]
    public async Task CleanAndReplaceCannotRemoveATerminalTaskBeforeItsExecutionActuallyReturns()
    {
        var completing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _btm.Enqueue(BTaskBuilder.Create("finishing").WithConcurrencyLimit("finishing", () => 1)
            .Run(_ => Task.CompletedTask)
            .WhenStatusChanges(async (_, task) =>
            {
                if (task.Status != BTaskStatus.Completed) return;
                completing.TrySetResult();
                await release.Task;
            }));
        try
        {
            await _btm.Start("finishing");
            await completing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var handler = _btm.Tasks.Single(t => t.Id == "finishing");
            Assert.IsTrue(handler.HasAttachedExecution);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _btm.Clean("finishing"));
            await _btm.CleanInactive();
            Assert.IsNotNull(_btm.GetTaskViewModel("finishing"));
            await Assert.ThrowsExceptionAsync<Exception>(() => _btm.Enqueue(
                BTaskBuilder.Create("finishing").ReplaceIfExists().Run(_ => Task.CompletedTask)));

            release.TrySetResult();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (handler.HasAttachedExecution) await Task.Delay(10, timeout.Token);
            await _btm.CleanInactive();
            Assert.IsNull(_btm.GetTaskViewModel("finishing"));
        }
        finally { release.TrySetResult(); }
    }

    [TestMethod]
    public async Task NonGroupedTasksKeepTheExistingStopAndCleanLifecycle()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _btm.Enqueue(BTaskBuilder.Create("ordinary-clean").Run(async args =>
        {
            entered.TrySetResult();
            await release.Task;
            args.CancellationToken.ThrowIfCancellationRequested();
        }));
        var handler = _btm.Tasks.Single(t => t.Id == "ordinary-clean");
        try
        {
            await _btm.Start(handler.Id);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await _btm.Stop(handler.Id);
            Assert.IsTrue(handler.HasAttachedExecution);
            Assert.AreEqual(BTaskStatus.Cancelling, handler.Task.Status);
            await _btm.Clean(handler.Id);
            Assert.IsNull(_btm.GetTaskViewModel(handler.Id));
        }
        finally
        {
            release.TrySetResult();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (handler.HasAttachedExecution) await Task.Delay(10, timeout.Token);
        }
    }
}
