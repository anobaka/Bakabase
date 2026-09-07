using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// The pump exists because the queue used to schedule its next task from inside the status-change
/// handler of the task that just finished. Draining a queue of tasks that each finish immediately
/// therefore nested a scheduling pass inside every completion, and re-entered a pass while an
/// earlier one was still choosing what to run.
///
/// What has to hold: never two passes at once, never a dropped request, and a burst collapses.
/// </summary>
[TestClass]
public class DownloadQueuePumpTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class TestPump : DownloadQueuePump
    {
        private readonly Func<TestPump, Task> _pass;

        public int Passes;
        public int Concurrent;
        public int MaxConcurrent;

        public TestPump(Func<TestPump, Task> pass)
            : base(null!, NullLogger<DownloadQueuePump>.Instance)
        {
            _pass = pass;
        }

        protected override async Task RunPassAsync()
        {
            MaxConcurrent = Math.Max(MaxConcurrent, Interlocked.Increment(ref Concurrent));

            try
            {
                Interlocked.Increment(ref Passes);
                await _pass(this);
            }
            finally
            {
                Interlocked.Decrement(ref Concurrent);
            }
        }
    }

    private static async Task WaitFor(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail(because);
    }

    [TestMethod]
    public async Task ASingleRequest_RunsOnePass()
    {
        var pump = new TestPump(_ => Task.CompletedTask);

        pump.Request();

        await WaitFor(() => pump.Passes == 1, "The requested pass never ran.");
        await Task.Delay(100);
        Assert.AreEqual(1, pump.Passes, "A single request must not keep the loop spinning.");
    }

    [TestMethod]
    public async Task RequestsArrivingDuringAPass_CollapseIntoOneFollowUp()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var pump = new TestPump(async p =>
        {
            if (p.Passes == 1)
            {
                started.TrySetResult();
                await release.Task;
            }
        });

        pump.Request();
        await started.Task.WaitAsync(Timeout);

        // A hundred completions landing while a pass is in flight is exactly the burst that used to
        // nest a hundred deep.
        for (var i = 0; i < 100; i++)
        {
            pump.Request();
        }

        release.SetResult();

        await WaitFor(() => pump.Passes == 2, "The queued requests were dropped instead of collapsing.");
        await Task.Delay(150);

        Assert.AreEqual(2, pump.Passes);
        Assert.AreEqual(1, pump.MaxConcurrent, "Two scheduling passes must never overlap.");
    }

    [TestMethod]
    public async Task ARequestMadeFromInsideAPass_IsStillServed()
    {
        // This is the shape the old code had: a pass completes a task, whose status handler asks for
        // another pass. It must be honoured, not swallowed as "already running".
        var pump = new TestPump(p =>
        {
            if (p.Passes == 1)
            {
                p.Request();
            }

            return Task.CompletedTask;
        });

        pump.Request();

        await WaitFor(() => pump.Passes == 2, "A request made from inside a pass was dropped.");
        Assert.AreEqual(1, pump.MaxConcurrent);
    }

    [TestMethod]
    public async Task AFailingPass_DoesNotStopLaterOnes()
    {
        var pump = new TestPump(p => p.Passes == 1
            ? Task.FromException(new InvalidOperationException("boom"))
            : Task.CompletedTask);

        pump.Request();
        await WaitFor(() => pump.Passes == 1, "The first pass never ran.");

        pump.Request();
        await WaitFor(() => pump.Passes == 2, "The pump stopped serving requests after a failed pass.");
    }
}
