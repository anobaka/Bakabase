using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class AsyncConcurrencyGateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task LiveLimitIncreaseWakesWaitersAndDecreaseWaitsForExistingWork()
    {
        var limit = 1;
        var gate = new AsyncConcurrencyGate(() => Volatile.Read(ref limit));
        using var first = await gate.EnterAsync();
        var secondTask = gate.EnterAsync();
        Assert.IsFalse(secondTask.IsCompleted);
        Volatile.Write(ref limit, 2);
        using var second = await secondTask.WaitAsync(Timeout);
        Volatile.Write(ref limit, 1);
        var thirdTask = gate.EnterAsync();
        first.Dispose();
        await Task.Delay(350);
        Assert.IsFalse(thirdTask.IsCompleted, "Lowering the limit must not allow a new request while one remains active.");
        second.Dispose();
        using var third = await thirdTask.WaitAsync(Timeout);
    }

    [TestMethod]
    public async Task CancelledWaiterAndRepeatedDisposalCannotLeakOrInventCapacity()
    {
        var gate = new AsyncConcurrencyGate(() => 1);
        using var first = await gate.EnterAsync();
        using var cancellation = new CancellationTokenSource();
        var cancelled = gate.EnterAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => cancelled);
        first.Dispose();
        first.Dispose();
        using var second = await gate.EnterAsync().WaitAsync(Timeout);
        var thirdTask = gate.EnterAsync();
        Assert.IsFalse(thirdTask.IsCompleted);
        second.Dispose();
        using var third = await thirdTask.WaitAsync(Timeout);
    }

    [TestMethod]
    public async Task WaitingObserverRunsOnlyWhenBlockedAndItsFailureDoesNotReserveCapacity()
    {
        var gate = new AsyncConcurrencyGate(() => 0);
        var waiting = 0;
        using var first = await gate.EnterAsync(onWaiting: () => { waiting++; return Task.CompletedTask; });
        Assert.AreEqual(0, waiting);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => gate.EnterAsync(onWaiting: () =>
            throw new InvalidOperationException("observer failed")));
        first.Dispose();
        using var next = await gate.EnterAsync().WaitAsync(Timeout);
    }
}
