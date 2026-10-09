using System.Collections.Concurrent;
using System.Reflection;
using Bakabase.App;
using Bakabase.Abstractions.Components.App;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DesktopSetupShutdownTests
{
    [TestMethod]
    public async Task OrdinarySecondLaunchActivatesExistingBusinessWithoutTakingItsDataLock()
    {
        var root = Path.Combine(Path.GetTempPath(), "bakabase-desktop-activation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var effective = Path.Combine(root, "existing library");
            Directory.CreateDirectory(effective);
            AnchorRedirect.Write(root, effective);
            using var held = DataDirectoryLock.TryAcquire(effective).Lock!;
            var before = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories);
            var message = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var channel = ActivationChannel.GetName(DataDirectoryIdentity.Normalize(effective));
            using var listener = ActivationServer.Start(channel, value => message.TrySetResult(value));
            Assert.IsTrue(await DesktopSetupBootstrap.TryActivateExistingAsync([], root));
            Assert.AreEqual(ActivationChannel.ShowMessage, await message.Task.WaitAsync(TimeSpan.FromSeconds(3)));
            CollectionAssert.AreEquivalent(before, Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories),
                "A second desktop launch must not initialize a database or setup state.");
            Assert.IsFalse(await DesktopSetupBootstrap.TryActivateExistingAsync([RestartHandoff.FormatArgument(123)], root),
                "A requested replacement must not disappear as an ordinary duplicate.");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task FirstDesktopLaunchDoesNotCreateDataWhileLookingForAnExistingWindow()
    {
        var missing = Path.Combine(Path.GetTempPath(), "bakabase-desktop-uninitialized-" + Guid.NewGuid().ToString("N"));
        Assert.IsFalse(await DesktopSetupBootstrap.TryActivateExistingAsync([], missing));
        Assert.IsFalse(Directory.Exists(missing));
    }

    [TestMethod]
    public async Task AnOldUnownedLockDoesNotDelayOrRewriteANormalDesktopStart()
    {
        var root = Path.Combine(Path.GetTempPath(), "bakabase-desktop-cold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var lockPath = Path.Combine(root, DataDirectoryLock.FileName);
            File.WriteAllText(lockPath, "old diagnostic owner");
            var activation = DesktopSetupBootstrap.TryActivateExistingAsync([], root);
            Assert.IsTrue(activation.IsCompleted, "An unowned lock must not wait for the three-second activation timeout.");
            Assert.IsFalse(await activation);
            Assert.AreEqual("old diagnostic owner", File.ReadAllText(lockPath));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task Teardown_cancels_and_drains_the_coordinator_without_the_stopped_UI_context()
    {
        var bootstrap = new DesktopSetupBootstrap();
        var stopping = (CancellationTokenSource)typeof(DesktopSetupBootstrap)
            .GetField("_stopping", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bootstrap)!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = false;
        var coordinator = Task.Run(async () =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, stopping.Token); }
            finally { drained = true; }
            return 0;
        });
        typeof(DesktopSetupBootstrap).GetField("_running", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(bootstrap, coordinator);
        await entered.Task;
        var context = new StoppedContext();
        var disposal = Task.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try { bootstrap.Dispose(); }
            finally { SynchronizationContext.SetSynchronizationContext(null); }
        });
        try
        {
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(drained, "Main must wait for coordinator cleanup before its process exits");
            Assert.IsTrue(coordinator.IsCompleted);
            Assert.AreEqual(0, context.PostCount,
                "shutdown must not depend on an Avalonia dispatcher that has stopped pumping");
        }
        finally
        {
            // A regression must fail without leaving a permanently blocked test thread.
            context.Drain();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class StoppedContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _pending = new();
        private int _postCount;
        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _postCount);
            _pending.Enqueue((callback, state));
        }

        public void Drain()
        {
            while (_pending.TryDequeue(out var item))
                _ = Task.Run(() => item.Callback(item.State));
        }
    }
}
