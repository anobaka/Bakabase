using Bakabase.Service.Components.Acquisition.Planning;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionRecoveryDiagnosisSessionsTests
{
    [TestMethod]
    public async Task CancelBeforeRegistrationLeavesTombstone()
    {
        var sessions = new AcquisitionRecoveryDiagnosisSessions(); var id = Guid.NewGuid().ToString("N");
        Assert.AreEqual("stopped", (await sessions.CancelAsync(id, default)).Status);
        Assert.ThrowsException<ArgumentException>(() => sessions.Begin(id, default));
    }
    [TestMethod]
    public async Task CancelAcknowledgesActualFinallyNotJustTokenCancellation()
    {
        var sessions = new AcquisitionRecoveryDiagnosisSessions(); var id = Guid.NewGuid().ToString("N");
        var active = sessions.Begin(id, default);
        var stopped = sessions.CancelAsync(id, default);
        Assert.IsTrue(active.Token.IsCancellationRequested);
        Assert.IsFalse(stopped.IsCompleted, "A filesystem operation that has not exited cannot be reported stopped.");
        active.Dispose(); Assert.AreEqual("stopped", (await stopped).Status);
        Assert.ThrowsException<ArgumentException>(() => sessions.Begin(id, default));
    }
    [TestMethod]
    public async Task FullRegistryStillAllowsCancellingExistingRequests()
    {
        var sessions = new AcquisitionRecoveryDiagnosisSessions(); var active = new List<(string, AcquisitionRecoveryDiagnosisSessions.Lease)>();
        try
        {
            for (var i = 0; i < 128; i++) {var id = Guid.NewGuid().ToString("N"); active.Add((id, sessions.Begin(id, default)));}
            var cancelled = sessions.CancelAsync(active[127].Item1, default);
            Assert.IsTrue(active[127].Item2.Token.IsCancellationRequested);
            active[127].Item2.Dispose(); active.RemoveAt(127);
            Assert.AreEqual("stopped", (await cancelled).Status);
        }
        finally {foreach (var (_, lease) in active) lease.Dispose();}
    }
    [TestMethod]
    public async Task AbortedCancelWaitDoesNotRetractCancellation()
    {
        var sessions = new AcquisitionRecoveryDiagnosisSessions(); var id = Guid.NewGuid().ToString("N");
        var active = sessions.Begin(id, default); using var wait = new CancellationTokenSource();
        var cancelled = sessions.CancelAsync(id, wait.Token); wait.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await cancelled);
        Assert.IsTrue(active.Token.IsCancellationRequested); active.Dispose();
        Assert.AreEqual("stopped", (await sessions.CancelAsync(id, default)).Status);
    }
}
