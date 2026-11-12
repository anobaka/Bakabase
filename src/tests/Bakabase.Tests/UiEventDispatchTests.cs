using System.Reflection;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.Service.Components.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class UiEventDispatchTests
{
    private ServiceProvider services = null!;
    [TestInitialize] public void Initialize() => services = new ServiceCollection().AddLogging().BuildServiceProvider();
    [TestCleanup] public void Cleanup() => services.Dispose();
    private static TaskCompletionSource Barrier() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private BTaskViewModel View(string id, BTaskStatus status) => new(
        new BTaskHandler(_ => Task.CompletedTask, new BTask(id, () => id), services), (string?)null)
        {Status = status, ResourceType = BTaskResourceType.FileSystemEntry, ResourceKeys = ["/files/" + id]};

    [TestMethod]
    public async Task Completion_arriving_during_send_survives_and_overlapping_timer_cannot_reorder_it()
    {
        var entered = Barrier(); var release = Barrier(); var sent = new List<BTaskStatus>();
        var hub = new Hub(async (method, args) =>
        {
            Assert.AreEqual("GetIncrementalData", method);
            var task = (BTaskViewModel)args[1]!; sent.Add(task.Status);
            if (task.Status == BTaskStatus.Running) { entered.TrySetResult(); await release.Task; }
        });
        using var handler = new BTaskEventHandler(hub, NullLogger<BTaskEventHandler>.Instance, null, false);
        await handler.OnTaskChange(View("archive", BTaskStatus.Running));
        var first = handler.FlushPendingEventsAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await handler.OnTaskChange(View("archive", BTaskStatus.Completed));
        await handler.FlushPendingEventsAsync();
        CollectionAssert.AreEqual(new[] {BTaskStatus.Running}, sent);
        release.SetResult(); await first;
        await handler.FlushPendingEventsAsync(); await handler.FlushPendingEventsAsync();
        CollectionAssert.AreEqual(new[] {BTaskStatus.Running, BTaskStatus.Completed}, sent);
    }

    [TestMethod]
    public async Task Full_snapshot_and_incremental_are_drained_together_without_losing_a_later_error()
    {
        var entered = Barrier(); var release = Barrier(); var sent = new List<(string Method, BTaskStatus Status)>();
        var hub = new Hub(async (method, args) =>
        {
            var task = method == "GetData" ? ((IEnumerable<BTaskViewModel>)args[1]!).Single() : (BTaskViewModel)args[1]!;
            sent.Add((method, task.Status));
            if (method == "GetData") { entered.TrySetResult(); await release.Task; }
        });
        using var handler = new BTaskEventHandler(hub, NullLogger<BTaskEventHandler>.Instance, null, false);
        await handler.OnAllTasksChange([View("archive", BTaskStatus.NotStarted)]);
        await handler.OnTaskChange(View("archive", BTaskStatus.Running));
        var first = handler.FlushPendingEventsAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await handler.OnTaskChange(View("archive", BTaskStatus.Error));
        release.SetResult(); await first; await handler.FlushPendingEventsAsync();
        CollectionAssert.AreEqual(new[] {("GetData", BTaskStatus.Running), ("GetIncrementalData", BTaskStatus.Error)}, sent);
    }

    [TestMethod]
    public async Task Full_snapshot_supersedes_older_incremental_progress()
    {
        BTaskViewModel[]? delivered = null;
        var hub = new Hub((method, args) =>
        {
            Assert.AreEqual("GetData", method);
            delivered = ((IEnumerable<BTaskViewModel>)args[1]!).ToArray();
            return Task.CompletedTask;
        });
        using var handler = new BTaskEventHandler(hub, NullLogger<BTaskEventHandler>.Instance, null, false);
        await handler.OnTaskChange(View("archive", BTaskStatus.Running));
        await handler.OnAllTasksChange([View("archive", BTaskStatus.Completed)]);
        await handler.FlushPendingEventsAsync();
        Assert.AreEqual(BTaskStatus.Completed, delivered!.Single().Status);
    }

    [TestMethod]
    public async Task Full_snapshot_removing_task_does_not_replay_its_older_incremental_update()
    {
        var batches = new List<BTaskViewModel[]>();
        var hub = new Hub((method, args) =>
        {
            Assert.AreEqual("GetData", method);
            batches.Add(((IEnumerable<BTaskViewModel>)args[1]!).ToArray());
            return Task.CompletedTask;
        });
        using var handler = new BTaskEventHandler(hub, NullLogger<BTaskEventHandler>.Instance, null, false);
        await handler.OnTaskChange(View("removed", BTaskStatus.Completed));
        await handler.OnAllTasksChange([]);
        await handler.FlushPendingEventsAsync();
        await handler.FlushPendingEventsAsync();
        Assert.AreEqual(1, batches.Count);
        Assert.AreEqual(0, batches[0].Length);
    }

    [TestMethod]
    public async Task Unsent_terminal_update_is_retried_after_hub_failure()
    {
        var calls = 0; var sent = new List<BTaskStatus>();
        var hub = new Hub((_, args) =>
        {
            if (++calls == 1) throw new IOException("synthetic transport failure");
            sent.Add(((BTaskViewModel)args[1]!).Status); return Task.CompletedTask;
        });
        using var handler = new BTaskEventHandler(hub, NullLogger<BTaskEventHandler>.Instance, null, false);
        await handler.OnTaskChange(View("archive", BTaskStatus.Completed));
        await handler.FlushPendingEventsAsync(); await handler.FlushPendingEventsAsync(); await handler.FlushPendingEventsAsync();
        Assert.AreEqual(2, calls); CollectionAssert.AreEqual(new[] {BTaskStatus.Completed}, sent);
    }

    [TestMethod]
    public async Task Failed_partial_send_does_not_retry_sent_items_or_overwrite_newer_terminal_state()
    {
        var entered = Barrier(); var release = Barrier(); var fail = true; var sent = new List<(string, BTaskStatus)>();
        var hub = new Hub(async (_, args) =>
        {
            var task = (BTaskViewModel)args[1]!;
            if (task.Id == "b" && fail) { entered.TrySetResult(); await release.Task; fail = false; throw new IOException("synthetic"); }
            sent.Add((task.Id, task.Status));
        });
        using var handler = new BTaskEventHandler(hub, NullLogger<BTaskEventHandler>.Instance, null, false);
        foreach (var id in new[] {"a", "b", "c"}) await handler.OnTaskChange(View(id, BTaskStatus.Running));
        var first = handler.FlushPendingEventsAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await handler.OnTaskChange(View("b", BTaskStatus.Completed)); release.SetResult(); await first;
        await handler.FlushPendingEventsAsync();
        CollectionAssert.AreEqual(new[] {("a", BTaskStatus.Running), ("b", BTaskStatus.Completed), ("c", BTaskStatus.Running)}, sent);
    }

    [TestMethod]
    public async Task Newer_full_snapshot_supersedes_failed_old_snapshot_without_resurrecting_removed_task()
    {
        var entered = Barrier(); var release = Barrier(); var fail = true; var sent = new List<string[]>();
        var hub = new Hub(async (method, args) =>
        {
            Assert.AreEqual("GetData", method);
            if (fail) { entered.TrySetResult(); await release.Task; fail = false; throw new IOException("synthetic"); }
            sent.Add(((IEnumerable<BTaskViewModel>)args[1]!).Select(t => t.Id).ToArray());
        });
        using var handler = new BTaskEventHandler(hub, NullLogger<BTaskEventHandler>.Instance, null, false);
        await handler.OnAllTasksChange([View("old", BTaskStatus.Completed)]);
        var first = handler.FlushPendingEventsAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await handler.OnAllTasksChange([]); release.SetResult(); await first; await handler.FlushPendingEventsAsync();
        Assert.AreEqual(1, sent.Count); Assert.AreEqual(0, sent[0].Length);
    }

    [TestMethod]
    public async Task Failed_full_snapshot_retries_with_a_newer_incremental_update()
    {
        var entered = Barrier(); var release = Barrier(); var fail = true; BTaskViewModel[]? delivered = null;
        var hub = new Hub(async (method, args) =>
        {
            Assert.AreEqual("GetData", method);
            if (fail) { entered.TrySetResult(); await release.Task; fail = false; throw new IOException("synthetic"); }
            delivered = ((IEnumerable<BTaskViewModel>)args[1]!).ToArray();
        });
        using var handler = new BTaskEventHandler(hub, NullLogger<BTaskEventHandler>.Instance, null, false);
        await handler.OnAllTasksChange([View("archive", BTaskStatus.Running)]);
        var first = handler.FlushPendingEventsAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await handler.OnTaskChange(View("archive", BTaskStatus.Error)); release.SetResult(); await first; await handler.FlushPendingEventsAsync();
        Assert.AreEqual(BTaskStatus.Error, delivered!.Single().Status);
    }

    [TestMethod]
    public async Task Disposing_during_send_does_not_throw_or_schedule_another_batch()
    {
        var entered = Barrier(); var release = Barrier(); var calls = 0;
        var hub = new Hub(async (_, _) => { calls++; entered.TrySetResult(); await release.Task; });
        var handler = new BTaskEventHandler(hub, NullLogger<BTaskEventHandler>.Instance, null, false);
        await handler.OnTaskChange(View("archive", BTaskStatus.Running));
        var first = handler.FlushPendingEventsAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        handler.Dispose(); await handler.OnTaskChange(View("archive", BTaskStatus.Completed));
        release.SetResult(); await first; await handler.FlushPendingEventsAsync();
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task First_filesystem_batch_and_changes_arriving_during_send_are_both_delivered()
    {
        var entered = Barrier(); var release = Barrier(); var batches = new List<string[]>();
        var hub = new Hub(async (method, args) =>
        {
            Assert.AreEqual("IwFsEntriesChange", method);
            var events = (List<IwFsEntryChangeEvent>)args[0]!; batches.Add(events.Select(e => e.Path).ToArray());
            if (batches.Count == 1) { entered.TrySetResult(); await release.Task; }
        });
        var watcher = new IwFsWatcher(NullLogger<IwFsWatcher>.Instance, hub);
        watcher.AddEvents(new IwFsEntryChangeEvent(IwFsEntryChangeType.Created, "/files/first"));
        var first = watcher.FlushPendingEventsAsync(default); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        watcher.AddEvents(new IwFsEntryChangeEvent(IwFsEntryChangeType.Created, "/files/second"));
        release.SetResult(); await first; await watcher.FlushPendingEventsAsync(default); await watcher.FlushPendingEventsAsync(default);
        Assert.AreEqual(2, batches.Count); CollectionAssert.AreEqual(new[] {"/files/first"}, batches[0]);
        CollectionAssert.AreEqual(new[] {"/files/second"}, batches[1]);
    }

    [TestMethod]
    public async Task Failed_filesystem_batch_keeps_its_order_and_newer_events_for_next_flush()
    {
        var fail = true; var received = new List<IwFsEntryChangeEvent>(); IwFsWatcher? watcher = null;
        var hub = new Hub((_, args) =>
        {
            if (fail) { fail = false; watcher!.AddEvents(new IwFsEntryChangeEvent(IwFsEntryChangeType.Deleted, "/files/item") {ChangedAt = DateTime.UnixEpoch.AddSeconds(1)}); throw new IOException("synthetic"); }
            received.AddRange((List<IwFsEntryChangeEvent>)args[0]!); return Task.CompletedTask;
        });
        watcher = new IwFsWatcher(NullLogger<IwFsWatcher>.Instance, hub);
        watcher.AddEvents(new IwFsEntryChangeEvent(IwFsEntryChangeType.Created, "/files/item") {ChangedAt = DateTime.UnixEpoch});
        await watcher.FlushPendingEventsAsync(default); await watcher.FlushPendingEventsAsync(default);
        CollectionAssert.AreEqual(new[] {IwFsEntryChangeType.Created, IwFsEntryChangeType.Deleted}, received.Select(e => e.Type).ToArray());
    }

    public class CallbackProxy : DispatchProxy
    {
        internal Func<MethodInfo, object?[], object?> Callback = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Callback(method!, args ?? []);
    }
    private sealed class Hub : IHubContext<WebGuiHub, IWebGuiClient>
    {
        public IHubClients<IWebGuiClient> Clients { get; }
        public IGroupManager Groups => throw new AssertFailedException("No group mutation is required.");
        internal Hub(Func<string, object?[], Task> callback)
        {
            var client = DispatchProxy.Create<IWebGuiClient, CallbackProxy>();
            ((CallbackProxy)(object)client).Callback = (method, args) => callback(method.Name, args);
            Clients = DispatchProxy.Create<IHubClients<IWebGuiClient>, CallbackProxy>();
            ((CallbackProxy)(object)Clients).Callback = (method, _) => method.Name == "get_All" ? client : throw new AssertFailedException("Expected broadcast only.");
        }
    }
}
