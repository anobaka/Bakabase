using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Tests.Utils;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// Regression tests for issue #1098 — clicking "stop" on a long-running task
/// used to leave the chip stuck on "Running" while the toast claimed "Stopped",
/// because BTaskHandler.Stop() only triggered cancellation and never reflected
/// the intent until the task itself observed the token.
///
/// After the fix:
///   - Stop() flips status to <c>Cancelling</c> immediately.
///   - The task eventually exits and the catch block flips it to <c>Cancelled</c>.
///   - <c>Cancelling</c> is treated as "busy" across BTaskManager — Start is a
///     no-op, the task counts for conflict detection, etc.
/// </summary>
[TestClass]
public class BTaskCancellingStatusTests
{
    private IServiceProvider _sp = null!;
    private BTaskManager _btm = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _btm = _sp.GetRequiredService<BTaskManager>();
    }

    /// <summary>
    /// Build a task that signals when it starts and respects the cancellation
    /// token. The optional <paramref name="cancelObservedAt"/> defers the
    /// ct check so callers can verify the Cancelling-then-Cancelled transition.
    /// </summary>
    private static BTaskHandlerBuilder BuildLongTask(
        string id,
        TaskCompletionSource started,
        TaskCompletionSource cancelledReached,
        TimeSpan? cancelObservedAt = null,
        Func<BTaskHandlerBuilder, BTaskHandlerBuilder>? customize = null)
    {
        var builder = new BTaskHandlerBuilder
        {
            Id = id,
            Type = BTaskType.Any,
            ResourceType = BTaskResourceType.Any,
            GetName = () => id,
            IsPersistent = false,
            StartNow = true,
            ConflictKeys = new HashSet<string> { id },
            Run = async args =>
            {
                started.TrySetResult();
                if (cancelObservedAt.HasValue)
                {
                    // Sleep without watching ct so the test can observe the
                    // task lingering in Cancelling for a window.
                    await Task.Delay(cancelObservedAt.Value);
                }
                await Task.Delay(Timeout.Infinite, args.CancellationToken);
            },
            OnStatusChange = (_, task) =>
            {
                if (task.Status == BTaskStatus.Cancelled)
                {
                    cancelledReached.TrySetResult();
                }
                return Task.CompletedTask;
            }
        };
        return customize?.Invoke(builder) ?? builder;
    }

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
    public async Task Stop_FlipsStatusToCancelling_Synchronously()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string id = "test-stop-flips-cancelling";

        await _btm.Enqueue(BuildLongTask(id, started, cancelled,
            // Hold the task body in a non-cancellable sleep for 2s so the chip
            // *must* sit in Cancelling for a real, observable window — exactly
            // the scenario the bug report screenshot showed (Stop clicked but
            // task still says "Running" at 54%).
            cancelObservedAt: TimeSpan.FromSeconds(2)));
        await _btm.Start(id);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForRunning(id, TimeSpan.FromSeconds(5));

        // Act
        await _btm.Stop(id);

        // Assert: the moment Stop returns, the chip is already Cancelling —
        // no more "Running at 54% but toast says stopped" UX.
        _btm.GetTaskViewModel(id)!.Status.Should().Be(BTaskStatus.Cancelling);
    }

    [TestMethod]
    public async Task Stop_EventuallyTransitionsCancellingToCancelled()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string id = "test-cancelling-to-cancelled";

        await _btm.Enqueue(BuildLongTask(id, started, cancelled));
        await _btm.Start(id);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForRunning(id, TimeSpan.FromSeconds(5));

        await _btm.Stop(id);

        // The task is awaiting Task.Delay(Infinite, ct) — it should observe
        // the cancel within a few ms and transition to Cancelled.
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _btm.GetTaskViewModel(id)!.Status.Should().Be(BTaskStatus.Cancelled);
    }

    [TestMethod]
    public async Task Start_OnCancellingTask_IsNoOp()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string id = "test-start-noop-while-cancelling";

        await _btm.Enqueue(BuildLongTask(id, started, cancelled,
            cancelObservedAt: TimeSpan.FromSeconds(2)));
        await _btm.Start(id);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForRunning(id, TimeSpan.FromSeconds(5));

        await _btm.Stop(id);
        _btm.GetTaskViewModel(id)!.Status.Should().Be(BTaskStatus.Cancelling);

        // Act: try to start the same task while it's still cancelling. Without
        // the Cancelling-aware switch in BTaskManager.Start this would have
        // fallen through to the default ArgumentOutOfRangeException branch.
        var act = async () => await _btm.Start(id);
        await act.Should().NotThrowAsync();

        // Status must still be Cancelling — we did not restart the task.
        _btm.GetTaskViewModel(id)!.Status.Should().Be(BTaskStatus.Cancelling);
    }

    [TestMethod]
    public async Task CancellingTask_BlocksConflictingTask_FromStarting()
    {
        var startedA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string idA = "test-conflict-a";
        const string idB = "test-conflict-b";
        const string sharedKey = "shared-conflict-key";

        await _btm.Enqueue(BuildLongTask(idA, startedA, cancelledA,
            cancelObservedAt: TimeSpan.FromSeconds(2),
            customize: b => b with { ConflictKeys = new HashSet<string> { sharedKey } }));
        await _btm.Start(idA);
        await startedA.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForRunning(idA, TimeSpan.FromSeconds(5));

        await _btm.Stop(idA);
        _btm.GetTaskViewModel(idA)!.Status.Should().Be(BTaskStatus.Cancelling);

        // Enqueue B with overlapping ConflictKey. Try to start it while A is
        // still in Cancelling — must be rejected by the conflict check.
        await _btm.Enqueue(BuildLongTask(idB, startedB, cancelledB,
            customize: b => b with
            {
                ConflictKeys = new HashSet<string> { sharedKey },
                StartNow = false,
            }));
        await _btm.Start(idB);

        // B should NOT start while A is still cancelling.
        var bStartedQuickly = await Task.WhenAny(startedB.Task, Task.Delay(300)) == startedB.Task;
        bStartedQuickly.Should().BeFalse(
            "Cancelling must count as 'busy' in conflict detection — otherwise " +
            "a sibling task with the same ConflictKey could start while the old " +
            "one is still tearing down");
    }
}
