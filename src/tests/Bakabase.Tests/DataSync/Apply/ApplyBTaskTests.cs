using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.Apply.DataSyncApplyFixture;

namespace Bakabase.Tests.DataSync.Apply;

/// <summary>
/// The runner's half of <c>ApplyBTaskTests</c> (§8.10.1, gate note 4, v3.1 B2 hardened): a task body checks, after
/// <c>YieldAsync()</c> and again after entering the gate, that no cancel was requested and that its attempt is still
/// the registered one, and exits without writing otherwise; a stop through the task's token ends it
/// <c>Cancelled</c> with the cancellation rethrown unchanged. The enqueueing, the scheduler and <c>EnqueueOnce</c>
/// belong to the task layer.
/// </summary>
[TestClass]
public class ApplyBTaskTests
{
    private DataSyncApplyFixture _f = null!;
    private DataSyncPeer _peer = null!;
    private DataSyncLinkDbModel _link = null!;
    private ObservedTaskRegistry _registry = null!;

    [TestInitialize]
    public async Task Setup()
    {
        var registry = new ObservedTaskRegistry();
        _f = await CreateAsync(s => s.AddSingleton<IDataSyncTaskRegistry>(registry));
        _registry = registry;
        _peer = new DataSyncPeer("PC-1");
        _link = await _f.LinkAsync(_peer);
    }

    /// <summary>A conflict to resolve: renamed here and there.</summary>
    private async Task<DataSyncInboxItemDbModel> ConflictAsync()
    {
        var key = SyncKey.New().Value;
        var v1 = _peer.Next();
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], v1, Content("Genre"), "a0")));
        var localKey = _f.Kind.KeyOf("Genre");
        _f.Kind.Definitions[localKey] = _f.Kind[localKey].With(name: "Style");
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], _peer.Next(v1), Content("Kind"), "a0")));
        return (await _f.OpenItemsAsync()).Single();
    }

    private Task<int?> ResolveAs(string taskId, DataSyncInboxItemDbModel item) =>
        _f.Runner.RunResolutionsAsync([new DataSyncResolveInput(item.Id, DataSyncInboxAction.UseRemote, item.Token, null, null, null, null)],
            new DataSyncApplyOptions(false), _f.Args(taskId));

    [TestMethod]
    public async Task A_cancel_requested_before_the_body_starts_writes_nothing()
    {
        var item = await ConflictAsync();
        const string taskId = "DataSyncResolve:1";
        var attempt = _registry.Register(taskId);
        _registry.RequestCancel(taskId);

        int? logId;
        using (DataSyncTaskAttempts.Enter(attempt)) logId = await ResolveAs(taskId, item);

        Assert.IsNull(logId);
        Assert.IsNull((await _f.OpenItemsAsync()).Single().ClosedAtUtc);
        Assert.AreEqual("Style", _f.Kind.Definitions.Values.Single().Name);
    }

    [TestMethod]
    public async Task Without_an_ambient_attempt_the_current_attempts_cancel_flag_still_stops_the_body()
    {
        // The registry as registered in production: only it knows the current attempt of a task.
        _f = await CreateAsync();
        _link = await _f.LinkAsync(_peer);
        var item = await ConflictAsync();
        const string taskId = "DataSyncResolve:2";
        _f.Registry.Register(taskId);
        _f.Registry.RequestCancel(taskId);

        Assert.IsNull(await ResolveAs(taskId, item));
        Assert.AreEqual(1, (await _f.OpenItemsAsync()).Count);
    }

    [TestMethod]
    public async Task A_body_started_from_a_stale_daemon_list_writes_nothing_and_the_current_attempt_runs()
    {
        var item = await ConflictAsync();
        const string taskId = "DataSyncResolve:3";
        var stale = _registry.Register(taskId);
        var current = _registry.Register(taskId);

        using (DataSyncTaskAttempts.Enter(stale)) Assert.IsNull(await ResolveAs(taskId, item));
        Assert.AreEqual(1, (await _f.OpenItemsAsync()).Count, "the replaced attempt exits without writing");

        int? logId;
        using (DataSyncTaskAttempts.Enter(current)) logId = await ResolveAs(taskId, item);
        Assert.IsNotNull(logId);
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
    }

    [TestMethod]
    public async Task A_cancel_requested_while_the_body_waits_for_the_gate_writes_nothing()
    {
        var record = _peer.Record([SyncKey.New().Value], _peer.Next(), Content("Genre"), "a0");
        const string taskId = "DataSync:link-1";
        var attempt = _registry.Register(taskId);
        var passedFirstCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.OnShouldRun = _ => passedFirstCheck.TrySetResult();
        var linkRow = await _f.LinkRowAsync(_link.Id);

        Task<DataSyncAutoSyncOutcome> body;
        using (var held = await _f.Gate.EnterAsync(null, default))
        {
            using (DataSyncTaskAttempts.Enter(attempt))
                body = _f.Runner.RunAutoSyncAsync(Context(linkRow, _peer), _f.Pull(_peer, (Item, record)), _f.Args(taskId));
            await passedFirstCheck.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(body.IsCompleted, "the body waits for the gate");
            _registry.RequestCancel(taskId);
        }

        var outcome = await body.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual((0, (int?) null), (outcome.Applied, outcome.ApplyLogId));
        Assert.AreEqual(0, _f.Kind.Definitions.Count);
        Assert.AreEqual(linkRow.CursorsJson, (await _f.LinkRowAsync(_link.Id)).CursorsJson);
        Assert.AreEqual(2, _registry.ShouldRunCalls, "checked after the yield and again after entering the gate");
    }

    [TestMethod]
    public async Task A_stop_through_the_tasks_token_rethrows_the_cancellation_with_nothing_written()
    {
        var key = SyncKey.New().Value;
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], _peer.Next(), Content("Genre"), "a0")));
        var logId = (await _f.HistoryAsync()).Single().Id;
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            _f.Runner.RunUndoAsync(logId, _f.Args("DataSyncUndo:" + logId, stop.Token)));

        Assert.IsTrue(_f.Kind.Definitions.Values.Any(d => d.Name == "Genre"));
        Assert.IsNull((await _f.HistoryAsync()).Single().UndoneAtUtc);
    }

    /// <summary>The real registry, observed: how often the runner asked, and a hook on each question.</summary>
    private sealed class ObservedTaskRegistry : IDataSyncTaskRegistry
    {
        private readonly DataSyncTaskRegistry _inner = new();

        public Action<string>? OnShouldRun { get; set; }
        public int ShouldRunCalls { get; private set; }

        public DataSyncTaskAttempt Register(string taskId) => _inner.Register(taskId);

        public bool RequestCancel(string taskId) => _inner.RequestCancel(taskId);

        public bool ShouldRun(string taskId, Guid attemptId)
        {
            var answer = _inner.ShouldRun(taskId, attemptId);
            ShouldRunCalls++;
            OnShouldRun?.Invoke(taskId);
            return answer;
        }
    }
}
