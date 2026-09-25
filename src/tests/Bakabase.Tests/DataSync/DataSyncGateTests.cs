using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync;

/// <summary>The process-wide gate and its lease (spec §2.9; v3.1 §2.7).</summary>
[TestClass]
public class DataSyncGateTests
{
    [TestMethod]
    public async Task One_holder_at_a_time_and_the_next_one_enters_when_the_lease_is_disposed()
    {
        var gate = new DataSyncGate();
        var first = await gate.EnterAsync(null, default);
        Assert.IsTrue(first.IsHeld && gate.IsHeld);

        var second = gate.EnterAsync(null, default);
        await Task.Delay(50);
        Assert.IsFalse(second.IsCompleted, "the gate is not reentrant");

        first.Dispose();
        Assert.IsFalse(first.IsHeld);
        using var lease = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(lease.IsHeld);

        // Disposing twice releases once: the gate stays held by the second lease.
        first.Dispose();
        Assert.IsTrue(gate.IsHeld);
    }

    [TestMethod]
    public async Task A_caller_with_a_limit_gets_busy_and_a_cancelled_wait_holds_nothing()
    {
        var gate = new DataSyncGate();
        using var held = await gate.EnterAsync(null, default);

        var busy = await Assert.ThrowsExceptionAsync<DataSyncGateTimeoutException>(() =>
            gate.EnterAsync(TimeSpan.FromMilliseconds(20), default));
        Assert.AreEqual(TimeSpan.FromMilliseconds(20), busy.Waited);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        var cancelled = false;
        try
        {
            await gate.EnterAsync(null, cts.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        Assert.IsTrue(cancelled);

        held.Dispose();
        using var next = await gate.EnterAsync(TimeSpan.FromSeconds(1), default);
        Assert.IsTrue(next.IsHeld);
    }
}
