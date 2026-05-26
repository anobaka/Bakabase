using System;
using System.Collections.Generic;
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
}
