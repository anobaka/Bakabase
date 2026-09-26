using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.EntityFrameworkCore;
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
    private Runtime.RecordingObserver _observer = null!;

    [TestInitialize]
    public async Task Setup()
    {
        var registry = new ObservedTaskRegistry();
        var observer = new Runtime.RecordingObserver();
        _f = await CreateAsync(s =>
        {
            s.AddSingleton<IDataSyncTaskRegistry>(registry);
            s.AddSingleton<IDataSyncRuntimeObserver>(observer);
        });
        _observer = observer;
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

    #region The DataSyncApply task over the real runner

    private IDataSyncStagedPullStore Pulls => _f.Services.GetRequiredService<IDataSyncStagedPullStore>();

    /// <summary>
    /// A link of extension groups (a kind the runtime knows) whose first contact waits for its first pull, with a once
    /// flag set for that apply.
    /// </summary>
    private async Task<DataSyncLinkDbModel> FirstContactLinkAsync(DataSyncPeer peer)
    {
        var link = await _f.LinkAsync(peer, DataSyncLinkMode.TwoWay, false, Groups);
        var db = _f.NewDb();
        var row = await db.DataSyncLinks.SingleAsync(l => l.Id == link.Id);
        row.SetOnceFlags(DataSyncMergeFlags.None with { SkipDeletionBreaker = true });
        await db.SaveChangesAsync();
        // The peer's comparison forms, as the fetch half records them from its head (§8.4 row A2).
        _f.Services.GetRequiredService<DataSyncRuntimeState>().RecordHead(row.Id,
            new DataSyncFeedHead(peer.NodeId, "epoch-1", peer.ActorId, DataSyncContract.Version,
                DataSyncContract.MinimumPeerVersion, "2.0.0", peer.Seq,
                [new DataSyncFeedKindHead(Groups, 1, peer.Seq, false, ExtensionGroupCodec.Instance.ComparisonFormVersion)],
                new DataSyncSourceAttention(false, 0, 0, false, 0), null, null), _f.Now);
        return row;
    }

    /// <summary>A pull of one new extension group.</summary>
    private DataSyncStagedPull GroupPull(DataSyncPeer peer, string name) =>
        _f.Pull(peer, (Groups, peer.Record([SyncKey.New().Value], peer.Next(), GroupContent(name, ".tif"))));

    private async Task<bool> HasGroupAsync(string name) =>
        (await _f.ExtensionGroups.GetAll()).Any(g => g.Name == name);

    /// <summary><c>DataSyncApply</c>'s body as the launcher runs it: its attempt registered and ambient.</summary>
    private async Task RunApplyTaskAsync()
    {
        var attempt = _registry.Register(DataSyncTaskIds.Apply);
        using (DataSyncTaskAttempts.Enter(attempt))
        {
            await _f.Services.GetRequiredService<DataSyncApplyTask>().RunAsync(_f.Args(DataSyncTaskIds.Apply), attempt);
        }
    }

    /// <summary>Nothing an apply that applied nothing may record: not synced, the once flag kept, no first contact.</summary>
    private static void AssertNothingRecordedAsSynced(DataSyncLinkDbModel row)
    {
        Assert.IsNull(row.LastSyncedAtUtc, "not recorded as synced");
        Assert.IsTrue(row.GetOnceFlags().SkipDeletionBreaker, "the once flag waits for an apply that commits");
        Assert.IsNull(row.FirstContactCompletedAtUtc, "the first contact is not complete");
        Assert.AreEqual(0, row.GetFirstContactKinds().Count);
    }

    [TestMethod]
    public async Task An_apply_task_that_failed_keeps_ApplyFailed_and_a_growing_backoff_and_consumes_nothing()
    {
        // Every write of the extension group adapter fails.
        var failed = 0;
        var injector = new DataSyncFailureInjector { FailOperation = (_, _) => ++failed > 0 };
        var (registry, observer) = (_registry, _observer);
        _f = await CreateAsync(s =>
        {
            s.AddSingleton<IDataSyncTaskRegistry>(registry);
            s.AddSingleton<IDataSyncRuntimeObserver>(observer);
            FailingDataSyncKind.AddExtensionGroups(s, injector);
        }, extensionGroups: false);
        var peer = new DataSyncPeer("NAS");
        var link = await FirstContactLinkAsync(peer);

        for (var run = 1; run <= 2; run++)
        {
            Pulls.Put(link.Id, GroupPull(peer, "Scans" + run));

            await RunApplyTaskAsync();

            var row = await _f.LinkRowAsync(link.Id);
            Assert.AreEqual(DataSyncApplyRunner.ApplyFailedCode, row.LastErrorCode, $"run {run}: the failure stands");
            Assert.AreEqual(run, row.ConsecutiveFailures);
            Assert.AreEqual((TimeSpan?) TimeSpan.FromMinutes(run), row.NextAttemptAtUtc - row.LastAttemptAtUtc,
                "the backoff grows (1, 2, 5, 10 minutes)");
            AssertNothingRecordedAsSynced(row);
            Assert.AreEqual(0, Pulls.LinksWaiting().Count, "a failed pull is dropped");
            Assert.IsFalse(await HasGroupAsync("Scans" + run));
        }

        Assert.AreEqual(2, failed, "the adapter was asked to write once per run");
        Assert.AreEqual(0, _observer.Count("applied:"), "never announced as a sync");
    }

    [TestMethod]
    public async Task An_apply_task_whose_attempt_ended_at_the_gate_keeps_its_pull_and_records_nothing()
    {
        var peer = new DataSyncPeer("NAS");
        var link = await FirstContactLinkAsync(peer);
        var pull = GroupPull(peer, "Scans");
        Pulls.Put(link.Id, pull);

        Task run;
        using (await _f.Gate.EnterAsync(null, default))
        {
            run = RunApplyTaskAsync();
            await Runtime.DataSyncRuntimeHarness.WaitUntilAsync(() => Pulls.LinksWaiting().Count == 0,
                "the task took the pull");
            // Stopped while the runner waits for the gate: it applies nothing once it holds it.
            _registry.RequestCancel(DataSyncTaskIds.Apply);
        }

        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreSame(pull, Pulls.Peek(link.Id), "the next run applies it without a refetch");
        Assert.IsFalse(await HasGroupAsync("Scans"));
        var row = await _f.LinkRowAsync(link.Id);
        Assert.IsNull(row.LastErrorCode);
        AssertNothingRecordedAsSynced(row);
        Assert.AreEqual(0, _observer.Count("applied:"));
    }

    [TestMethod]
    public async Task An_apply_task_that_committed_the_first_pull_records_the_first_sync()
    {
        var peer = new DataSyncPeer("NAS");
        var link = await FirstContactLinkAsync(peer);
        Pulls.Put(link.Id, GroupPull(peer, "Scans"));

        await RunApplyTaskAsync();

        var row = await _f.LinkRowAsync(link.Id);
        Assert.IsNull(row.LastErrorCode);
        Assert.IsNotNull(row.LastSyncedAtUtc);
        Assert.IsNotNull(row.FirstContactCompletedAtUtc);
        CollectionAssert.AreEqual(new[] { Groups }, row.GetFirstContactKinds().ToArray());
        Assert.AreEqual(DataSyncMergeFlags.None, row.GetOnceFlags(), "consumed by the apply that committed");
        Assert.IsTrue(await HasGroupAsync("Scans"));
        // The runner's final transaction completed the first contact already: the task still announces it (§8.3).
        Assert.AreEqual(1, _observer.Count($"applied:{link.Id}:first"));
        var entry = (await _f.HistoryAsync()).Single();
        Assert.AreEqual((DataSyncHistoryKind.FirstLink, (int?) link.Id), (entry.Kind, entry.LinkId),
            "its history entry is the first sync with the peer (§8.3)");
    }

    #endregion

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
