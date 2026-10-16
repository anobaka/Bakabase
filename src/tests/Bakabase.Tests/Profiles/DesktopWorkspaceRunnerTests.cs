using System.Collections.Concurrent;
using System.Threading.Channels;
using Bakabase.Infrastructures.Components.App.Profiles;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Tests.SingleInstance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Profiles;

[TestClass]
public sealed class DesktopWorkspaceRunnerTests
{
    private string _root = null!;
    [TestInitialize] public void Initialize() => _root = Path.Combine(Path.GetTempPath(), "bakabase-workspace-switch-" + Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private DesktopWorkspaceContext Context(string id) => new(id, Path.Combine(_root, id), false, ["--profile=" + id]);

    [TestMethod]
    public async Task A_to_B_to_A_waits_for_each_real_process_to_exit_and_preserves_data()
    {
        var a = Context("A"); var b = Context("B");
        Directory.CreateDirectory(a.AnchorDirectory); Directory.CreateDirectory(b.AnchorDirectory);
        File.WriteAllText(Path.Combine(a.AnchorDirectory, "library.txt"), "A data");
        File.WriteAllText(Path.Combine(b.AnchorDirectory, "library.txt"), "B data");
        var runner = new DesktopWorkspaceRunner(a);
        var starts = Channel.CreateUnbounded<(string Id, int Pid)>();
        var active = 0; var maximum = 0;
        using var stop = new CancellationTokenSource();
        var running = runner.RunAsync(async (context, token) =>
        {
            using var process = await InstanceProbe.StartAsync("hold", context.AnchorDirectory);
            Assert.AreEqual("LOCK Acquired", process.FirstLine);
            maximum = Math.Max(maximum, Interlocked.Increment(ref active));
            runner.Ready();
            await starts.Writer.WriteAsync((context.ProfileId!, process.Id));
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { process.Stop(); Interlocked.Decrement(ref active); }
            return 0;
        }, stop.Token);
        try
        {
            var first = await starts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual("A", first.Id);
            runner.PrepareSwitch(b, () => { });
            Assert.AreEqual("A", runner.Current.ProfileId, "Preparation must not stop the host before its HTTP response completes.");
            Assert.AreEqual(1, active);
            runner.CommitSwitch("B");
            var second = await starts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual("B", second.Id);
            using (var unlocked = DataDirectoryLock.TryAcquire(a.AnchorDirectory).Lock) Assert.IsNotNull(unlocked);
            runner.RequestSwitch(a, () => { });
            var third = await starts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual("A", third.Id); Assert.AreNotEqual(first.Pid, third.Pid);
            Assert.AreEqual(1, maximum);
            Assert.AreEqual("A data", File.ReadAllText(Path.Combine(a.AnchorDirectory, "library.txt")));
            Assert.AreEqual("B data", File.ReadAllText(Path.Combine(b.AnchorDirectory, "library.txt")));
        }
        finally { stop.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.AreEqual(0, active);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task Failed_or_cancelled_target_startup_reopens_previous_after_releasing_target_process(int targetExit)
    {
        var runner = new DesktopWorkspaceRunner(Context("A"));
        var ready = Channel.CreateUnbounded<string>();
        var history = new ConcurrentQueue<string>();
        var restored = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.RestoredAfterFailure += error => restored.TrySetResult(error);
        using var stop = new CancellationTokenSource();
        var running = runner.RunAsync(async (context, token) =>
        {
            using var process = await InstanceProbe.StartAsync("hold", context.AnchorDirectory);
            Assert.AreEqual("LOCK Acquired", process.FirstLine);
            history.Enqueue(context.ProfileId!);
            if (context.ProfileId == "B") { process.Stop(); return targetExit; }
            runner.Ready(); await ready.Writer.WriteAsync(context.ProfileId!);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            process.Stop(); return 0;
        }, stop.Token);
        try
        {
            await ready.Reader.ReadAsync();
            runner.RequestSwitch(Context("B"), () => { });
            Assert.AreEqual("A", await ready.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(5));
            CollectionAssert.AreEqual(new[] { "A", "B", "A" }, history.ToArray());
            using var unlocked = DataDirectoryLock.TryAcquire(Context("B").AnchorDirectory).Lock;
            Assert.IsNotNull(unlocked);
        }
        finally { stop.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [TestMethod]
    public async Task Already_ready_target_does_not_revert_after_maintenance_marks_it_unavailable_and_it_exits()
    {
        var runner = new DesktopWorkspaceRunner(Context("A"));
        var ready = Channel.CreateUnbounded<string>();
        var finishB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var history = new ConcurrentQueue<string>();
        var running = runner.RunAsync(async (context, token) =>
        {
            history.Enqueue(context.ProfileId!); runner.Ready(); await ready.Writer.WriteAsync(context.ProfileId!);
            if (context.ProfileId == "B") { await finishB.Task; runner.Unavailable(); return 1; }
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            return 0;
        });
        await ready.Reader.ReadAsync(); runner.RequestSwitch(Context("B"), () => { });
        Assert.AreEqual("B", await ready.Reader.ReadAsync());
        finishB.TrySetResult();
        Assert.AreEqual(1, await running.WaitAsync(TimeSpan.FromSeconds(5)));
        CollectionAssert.AreEqual(new[] { "A", "B" }, history.ToArray());
    }

    [TestMethod]
    public async Task Slow_shutdown_never_starts_target_early_and_validation_or_setup_rejection_keeps_source()
    {
        var runner = new DesktopWorkspaceRunner(Context("A"));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = runner.RunAsync(async (context, token) =>
        {
            if (context.ProfileId == "B") { target.TrySetResult(); runner.Ready(); return 0; }
            runner.Ready(); ready.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            stopping.TrySetResult(); await release.Task; return 0;
        });
        try
        {
            await ready.Task;
            Assert.ThrowsException<IOException>(() => runner.PrepareSwitch(Context("B"), () => throw new IOException("storage unavailable")));
            runner.Busy();
            Assert.ThrowsException<InvalidOperationException>(() => runner.RequestSwitch(Context("B"), () => { }));
            runner.Ready(); runner.RequestSwitch(Context("B"), () => { });
            await stopping.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(target.Task.IsCompleted);
            Assert.AreEqual("A", runner.Current.ProfileId);
            release.TrySetResult();
            Assert.AreEqual(0, await running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(target.Task.IsCompletedSuccessfully);
        }
        finally { release.TrySetResult(); }
    }
    [TestMethod]
    public void Prepared_switch_blocks_setup_and_restart_even_after_ready_callback()
    {
        var runner = new DesktopWorkspaceRunner(Context("A"));
        runner.Ready(); runner.PrepareSwitch(Context("B"), () => { }); runner.Ready();
        Assert.ThrowsException<InvalidOperationException>(runner.BeginSetup);
        Assert.ThrowsException<InvalidOperationException>(() => runner.ReserveRestart(false));
        var commit = runner.PrepareCommit("B");
        runner.Ready();
        Assert.ThrowsException<InvalidOperationException>(runner.BeginSetup);
        Assert.ThrowsException<InvalidOperationException>(() => runner.ReserveRestart(false));
        commit();
        Assert.ThrowsException<InvalidOperationException>(runner.BeginSetup);
    }

    [TestMethod]
    public void Restart_or_update_reservation_blocks_switch_and_setup_until_cancelled()
    {
        var runner = new DesktopWorkspaceRunner(Context("A")); runner.Ready();
        runner.ReserveRestart(false); runner.Ready();
        Assert.ThrowsException<InvalidOperationException>(() => runner.PrepareSwitch(Context("B"), () => { }));
        Assert.ThrowsException<InvalidOperationException>(runner.BeginSetup);
        runner.ReserveRestart(true);
        runner.PrepareSwitch(Context("B"), () => { });
    }

    [TestMethod]
    public void Abandoned_preparation_expires_and_late_commit_cannot_switch_a_new_operation()
    {
        var clock = new Clock();
        var runner = new DesktopWorkspaceRunner(Context("A"), clock); runner.Ready();
        runner.PrepareSwitch(Context("B"), () => { }); var staleCommit = runner.PrepareCommit("B");
        clock.Now += TimeSpan.FromSeconds(31);
        runner.BeginSetup(); staleCommit();
        Assert.ThrowsException<InvalidOperationException>(() => runner.PrepareSwitch(Context("C"), () => { }));
        runner.Ready(); runner.PrepareSwitch(Context("C"), () => { });
        Assert.ThrowsException<InvalidOperationException>(() => runner.PrepareCommit("B"));
        runner.CommitSwitch("C");
    }

    [TestMethod]
    public async Task Source_shutdown_failure_reopens_source_without_starting_target()
    {
        var runner = new DesktopWorkspaceRunner(Context("A"));
        var starts = Channel.CreateUnbounded<string>(); var count = 0;
        using var stop = new CancellationTokenSource();
        var running = runner.RunAsync(async (context, token) =>
        {
            runner.Ready(); await starts.Writer.WriteAsync(context.ProfileId!);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            if (Interlocked.Increment(ref count) == 1) throw new IOException("shutdown failed after child exit");
            return 0;
        }, stop.Token);
        try
        {
            Assert.AreEqual("A", await starts.Reader.ReadAsync());
            runner.RequestSwitch(Context("B"), () => { });
            Assert.AreEqual("A", await starts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { stop.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

}
