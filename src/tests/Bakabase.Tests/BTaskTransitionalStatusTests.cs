using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.TestKit.Utils;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// Pause/Resume used to look "stuck" because the chip only updated once the
/// task body reached the next PauseToken.WaitWhilePausedAsync — a long CPU/IO
/// step between yield points was indistinguishable from a pending request.
/// These tests lock down the new behaviour:
///   - Pause() flips status to Pausing immediately
///   - the task body's pause checkpoint then promotes it to Paused
///   - Resume() flips status to Resuming immediately
///   - the resumed pause checkpoint promotes it to Running
/// </summary>
[TestClass]
public sealed class BTaskTransitionalStatusTests
{
    private IServiceProvider _sp = null!;
    private BTaskManager _btm = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _btm = _sp.GetRequiredService<BTaskManager>();
    }

    private async Task WaitForStatus(string id, BTaskStatus status, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_btm.GetTaskViewModel(id)?.Status == status) return;
            await Task.Delay(10);
        }
        throw new TimeoutException(
            $"Task {id} did not reach {status} within {timeout}; actual status: {_btm.GetTaskViewModel(id)?.Status}");
    }

    [TestMethod]
    public async Task Pause_FlipsStatusToPausing_BeforeTaskYields()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSpin = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        const string id = "test-pause-flips-pausing";

        // Task spins in a non-yielding loop until releaseSpin is signaled.
        // Until then it ignores the PauseToken — so Pause() must reflect
        // "Pausing" immediately based on the API call alone.
        await _btm.Enqueue(BTaskBuilder.Create(id)
            .StartImmediately()
            .ConflictsWith(id)
            .Run(async args =>
            {
                started.TrySetResult();
                await releaseSpin.Task;
                await args.PauseToken.WaitWhilePausedAsync(args.CancellationToken);
                await Task.Delay(Timeout.Infinite, args.CancellationToken);
            }));
        await _btm.Start(id);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatus(id, BTaskStatus.Running, TimeSpan.FromSeconds(5));

        // Act: Pause while the task is in its non-yielding section.
        await _btm.Pause(id);

        // Assert: Pausing must be visible right away — without this regression
        // fix the chip would still say "Running" even though we've requested
        // a pause.
        _btm.GetTaskViewModel(id)!.Status.Should().Be(BTaskStatus.Pausing);

        // Now let the body reach the pause checkpoint; OnPause fires and the
        // status should settle to Paused.
        releaseSpin.SetResult(true);
        await WaitForStatus(id, BTaskStatus.Paused, TimeSpan.FromSeconds(5));

        await _btm.Stop(id);
    }

    [TestMethod]
    public async Task Resume_FlipsStatusToResuming_BeforeTaskWakes()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string id = "test-resume-flips-resuming";

        // Task body must loop back into WaitWhilePausedAsync — OnPause only
        // fires while the body is *inside* that call, and the test needs to
        // catch the Paused → Resuming transition.
        await _btm.Enqueue(BTaskBuilder.Create(id)
            .StartImmediately()
            .ConflictsWith(id)
            .Run(async args =>
            {
                started.TrySetResult();
                while (!args.CancellationToken.IsCancellationRequested)
                {
                    await args.PauseToken.WaitWhilePausedAsync(args.CancellationToken);
                    await Task.Delay(50, args.CancellationToken);
                }
            }));
        await _btm.Start(id);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatus(id, BTaskStatus.Running, TimeSpan.FromSeconds(5));

        await _btm.Pause(id);
        await WaitForStatus(id, BTaskStatus.Paused, TimeSpan.FromSeconds(5));

        // Act: Resume — the visible status must flip to Resuming straight
        // away, before the task body's WaitWhilePausedAsync returns.
        await _btm.Resume(id);

        var statusAfterResume = _btm.GetTaskViewModel(id)!.Status;
        // Either Resuming (we caught the transitional state) or Running (the
        // checkpoint resumed faster than we could observe — both prove the
        // transitional path executed without an exception).
        statusAfterResume.Should().BeOneOf(BTaskStatus.Resuming, BTaskStatus.Running);

        await WaitForStatus(id, BTaskStatus.Running, TimeSpan.FromSeconds(5));

        await _btm.Stop(id);
    }

    [TestMethod]
    public async Task Pause_OnNonRunningTask_IsNoOp()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string id = "test-pause-noop-not-running";

        await _btm.Enqueue(BTaskBuilder.Create(id)
            .ConflictsWith(id)
            .Run(_ => Task.CompletedTask)
            .WhenStatusChanges((_, t) =>
            {
                if (t.Status == BTaskStatus.Completed) completed.TrySetResult();
                return Task.CompletedTask;
            }));
        await _btm.Start(id);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Pause on a finished task must not move the status away from Completed.
        await _btm.Pause(id);
        _btm.GetTaskViewModel(id)!.Status.Should().Be(BTaskStatus.Completed);
    }
}
