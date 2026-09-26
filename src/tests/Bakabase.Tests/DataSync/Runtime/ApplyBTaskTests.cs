using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Tests.DataSync.Runtime;

/// <summary>
/// §13.5 <c>ApplyBTaskTests</c> (spec §8.10.1, gate B3): fetching and applying are separate tasks, the apply waits for
/// the enhancer while the fetch does not, <c>EnqueueOnce</c> runs a fixed task id again in the same process, and a
/// cancel never lets a stale start write.
/// </summary>
[TestClass]
public class ApplyBTaskTests
{
    private static DataSyncStagedPull PullFor(DataSyncRuntimeHarness h, string peer, long maxSeq = 5) =>
        new(peer, "Peer " + peer,
            new DataSyncFeedManifest("snap", 120_000, peer, "epoch-1", "0123456789abcdef", 1, 1, "2.4.0", [], null,
                new DataSyncSourceAttention(false, 0, 0, false, 0)),
            [new DataSyncStagedKind(DataSyncKindIds.CustomProperty, 1, true, null, [], maxSeq, false)],
            h.Clock.UtcNow);

    [TestMethod]
    public async Task DataSync_fetches_while_Enhancement_runs_and_DataSyncApply_waits_for_it()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(daemon: true);
        var link = h.AddLink("nas", l => l.SetCursors(new Dictionary<string, long>
            {["extensionGroup"] = 3, ["customProperty"] = 2}));

        var releaseEnhancement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await h.Btm.Enqueue(BTaskBuilder.Create("Enhancement").ConflictsWith("Enhancement")
            .Run(async args => await releaseEnhancement.Task.WaitAsync(args.CancellationToken)));
        await h.Btm.Start("Enhancement");
        await h.WaitForStatusAsync("Enhancement", BTaskStatus.Running);

        await h.Scheduler.TickAsync(default); // startup: every link due in 5 s
        h.Clock.Advance(DataSyncSchedule.StartupDelay);
        await h.Scheduler.TickAsync(default);
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Completed);

        // The fetch ran and staged the pull although Enhancement is running.
        CollectionAssert.AreEqual(new[] {link.Id}, h.StagedPulls.LinksWaiting().ToArray());
        Assert.AreEqual(1, h.Peers.Peers["nas"].Manifests);

        // The apply waits behind Enhancement, and says why.
        await Task.Delay(1500);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Apply));
        Assert.IsNotNull(h.Btm.GetTaskViewModel(DataSyncTaskIds.Apply)!.ReasonForUnableToStart);
        Assert.IsTrue(h.Launcher.IsWriteTaskActiveOrPending(), "ApplyInProgress while the apply waits");
        Assert.AreEqual(0, h.Runner.AutoSyncs.Count);

        releaseEnhancement.SetResult();
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);
        var call = h.Runner.AutoSyncs.Single();
        Assert.AreEqual(link.Id, call.Context.LinkId);
        Assert.AreEqual(DataSyncKindIds.CustomProperty, call.Pull!.Kinds.Single().Kind);
        Assert.IsFalse(h.Launcher.IsWriteTaskActiveOrPending());
    }

    [TestMethod]
    public async Task Pause_all_pressed_while_the_apply_waits_behind_Enhancement_applies_nothing_until_unpaused()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(daemon: true);
        var link = h.AddLink("nas", l => l.SetCursors(new Dictionary<string, long>
            {["extensionGroup"] = 3, ["customProperty"] = 2}));

        var releaseEnhancement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await h.Btm.Enqueue(BTaskBuilder.Create("Enhancement").ConflictsWith("Enhancement")
            .Run(async args => await releaseEnhancement.Task.WaitAsync(args.CancellationToken)));
        await h.Btm.Start("Enhancement");
        await h.WaitForStatusAsync("Enhancement", BTaskStatus.Running);

        await h.Scheduler.TickAsync(default);
        h.Clock.Advance(DataSyncSchedule.StartupDelay);
        await h.Scheduler.TickAsync(default);
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Completed);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Apply), "the apply waits behind Enhancement");

        // "Pause all" (§8.7): the same state as pausing each link, although the apply was already enqueued.
        h.Store.LocalState!.AllPaused = true;
        releaseEnhancement.SetResult();
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);
        Assert.AreEqual(0, h.Runner.AutoSyncs.Count, "nothing was applied");
        CollectionAssert.AreEqual(new[] {link.Id}, h.StagedPulls.LinksWaiting().ToArray(), "the pull still waits");

        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.Completed, h.Status(DataSyncTaskIds.Apply), "not enqueued again while paused");

        // Unpaused: the pull that waited is applied.
        h.Store.LocalState!.AllPaused = false;
        await h.Scheduler.TickAsync(default);
        await DataSyncRuntimeHarness.WaitUntilAsync(() => h.Runner.AutoSyncs.Count == 1, "the waiting pull is applied");
        Assert.AreEqual(link.Id, h.Runner.AutoSyncs.Single().Context.LinkId);
    }

    [TestMethod]
    public async Task Pause_all_the_runner_finds_inside_the_gate_keeps_the_pull_and_records_nothing()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(daemon: true);
        var link = h.AddLink("nas", l => l.SetCursors(new Dictionary<string, long>
            {["extensionGroup"] = 3, ["customProperty"] = 2}));
        var synced = h.Link(link.Id).LastSyncedAtUtc;
        // Pressed after this task's own check, while the runner waited for the gate (§8.10.2).
        h.Runner.AutoSyncOutcome = _ => new DataSyncAutoSyncOutcome(null, DataSyncPauseReason.AllPaused, 0, 0, 0, [], []);

        await h.Scheduler.TickAsync(default);
        h.Clock.Advance(DataSyncSchedule.StartupDelay);
        await h.Scheduler.TickAsync(default);
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Completed);
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);

        Assert.AreEqual(1, h.Runner.AutoSyncs.Count);
        CollectionAssert.AreEqual(new[] {link.Id}, h.StagedPulls.LinksWaiting().ToArray(), "the pull still waits");
        Assert.AreEqual(synced, h.Link(link.Id).LastSyncedAtUtc, "nothing is recorded as synced");
        Assert.AreEqual(0, h.Observer.Count("paused:") + h.Observer.Count("applied:"));
    }

    [TestMethod]
    public async Task Two_consecutive_pulls_in_one_process_are_both_applied()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(daemon: true);
        var link = h.AddLink("nas", l => l.SetCursors(new Dictionary<string, long>
            {["extensionGroup"] = 3, ["customProperty"] = 2}));
        var peer = h.Peers.Peers["nas"];

        await h.Scheduler.TickAsync(default);
        h.Clock.Advance(DataSyncSchedule.StartupDelay);
        await h.Scheduler.TickAsync(default);
        await DataSyncRuntimeHarness.WaitUntilAsync(() => h.Runner.AutoSyncs.Count == 1, "the first pull is applied");
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Completed);

        // The runner moved the cursor; the peer changes again.
        h.Store.Edit(link.Id, l => l.SetCursors(new Dictionary<string, long>
            {["extensionGroup"] = 3, ["customProperty"] = 5}));
        peer.MaxSeq["customProperty"] = 9;
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        await h.Scheduler.TickAsync(default);

        await DataSyncRuntimeHarness.WaitUntilAsync(() => h.Runner.AutoSyncs.Count == 2, "the second pull is applied");
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);
        var calls = h.Runner.AutoSyncs.ToArray();
        Assert.AreEqual(5, calls[0].Pull!.Kinds.Single().MaxSeq);
        Assert.AreEqual(9, calls[1].Pull!.Kinds.Single().MaxSeq);
        Assert.AreNotEqual(calls[0].Attempt!.AttemptId, calls[1].Attempt!.AttemptId,
            "each enqueue registers its own attempt");
        Assert.IsTrue(h.Runner.AttemptCurrentAtGate.All(current => current),
            "the runner sees its attempt as current after entering the gate");
    }

    [TestMethod]
    public async Task An_enqueue_while_the_task_is_NotStarted_or_active_is_a_no_op()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        var link = h.AddLink("nas");
        h.StagedPulls.Put(link.Id, PullFor(h, "nas"));

        var first = await h.Launcher.EnqueueApplyAsync();
        Assert.IsNotNull(first);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Apply));
        Assert.IsNull(await h.Launcher.EnqueueApplyAsync(), "NotStarted: no-op");
        Assert.AreEqual(first, h.Registry.Current(DataSyncTaskIds.Apply));

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Runner.Hold = async ct =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
        };
        await h.Btm.Start(DataSyncTaskIds.Apply);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsNull(await h.Launcher.EnqueueApplyAsync(), "active: no-op");
        Assert.AreEqual(first, h.Registry.Current(DataSyncTaskIds.Apply));

        release.SetResult();
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);
        Assert.AreEqual(1, h.Runner.AutoSyncs.Count);

        // Finished: the next enqueue cleans it and enqueues a new attempt.
        var second = await h.Launcher.EnqueueApplyAsync();
        Assert.IsNotNull(second);
        Assert.AreNotEqual(first!.AttemptId, second.AttemptId);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Apply));
    }

    [TestMethod]
    public async Task A_second_restore_choice_and_a_retried_undo_after_an_Error_both_run()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(daemon: true);

        Assert.IsNotNull(await h.Launcher.EnqueueRestoreAsync(DataSyncRestoreChoice.ThisDeviceWins, null));
        await DataSyncRuntimeHarness.WaitUntilAsync(() => h.Runner.Restores.Count == 1, "the first restore ran");
        await h.WaitForStatusAsync(DataSyncTaskIds.Restore, BTaskStatus.Completed);
        Assert.IsNotNull(await h.Launcher.EnqueueRestoreAsync(DataSyncRestoreChoice.OthersWin, 4),
            "a second restore choice in one process is not refused");
        await DataSyncRuntimeHarness.WaitUntilAsync(() => h.Runner.Restores.Count == 2, "the second restore ran");
        CollectionAssert.AreEqual(
            new[] {(DataSyncRestoreChoice.ThisDeviceWins, (int?) null), (DataSyncRestoreChoice.OthersWin, (int?) 4)},
            h.Runner.Restores.ToArray());

        h.Runner.UndoErrors.Enqueue(new InvalidOperationException("boom"));
        var undoId = DataSyncTaskIds.Undo(7);
        Assert.IsNotNull(await h.Launcher.EnqueueUndoAsync(7));
        await h.WaitForStatusAsync(undoId, BTaskStatus.Error);
        Assert.IsNotNull(await h.Launcher.EnqueueUndoAsync(7), "an undo can be retried after an Error");
        await h.WaitForStatusAsync(undoId, BTaskStatus.Completed);
        CollectionAssert.AreEqual(new[] {7, 7}, h.Runner.Undos.ToArray());
    }

    [TestMethod]
    public async Task Cancel_of_a_waiting_apply_that_a_stale_daemon_list_starts_writes_nothing()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        var link = h.AddLink("nas");
        h.StagedPulls.Put(link.Id, PullFor(h, "nas"));
        await h.Launcher.EnqueueApplyAsync();
        var stale = h.Btm.Tasks.Single(t => t.Id == DataSyncTaskIds.Apply);

        Assert.AreEqual(DataSyncTaskCancelOutcome.Removed, await h.Launcher.CancelAsync(DataSyncTaskIds.Apply));
        Assert.IsNull(h.Btm.GetTaskViewModel(DataSyncTaskIds.Apply));

        // The daemon read its task list before the Clean and starts the old handler anyway.
        await stale.TryStartAutomatically();
        await DataSyncRuntimeHarness.WaitUntilAsync(() => stale.Task.Status == BTaskStatus.Completed,
            "the stale body exits");
        Assert.AreEqual(0, h.Runner.AutoSyncs.Count, "nothing was applied");
        CollectionAssert.AreEqual(new[] {link.Id}, h.StagedPulls.LinksWaiting().ToArray(), "the pull still waits");

        // A newer enqueue replaces the attempt: the old body still exits, the new one applies.
        var older = h.Registry.Register(DataSyncTaskIds.Apply);
        Assert.IsNotNull(await h.Launcher.EnqueueApplyAsync());
        Assert.IsFalse(h.Registry.ShouldRun(DataSyncTaskIds.Apply, older.AttemptId));
        await h.Btm.Start(DataSyncTaskIds.Apply);
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);
        Assert.AreEqual(1, h.Runner.AutoSyncs.Count);
    }

    [TestMethod]
    public async Task Stopping_a_running_apply_ends_it_Cancelled_and_keeps_its_pull()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        var link = h.AddLink("nas");
        var pull = PullFor(h, "nas");
        h.StagedPulls.Put(link.Id, pull);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Runner.Hold = async ct =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        };

        await h.Launcher.EnqueueApplyAsync();
        await h.Btm.Start(DataSyncTaskIds.Apply);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(0, h.StagedPulls.LinksWaiting().Count, "the apply took the pull");

        Assert.AreEqual(DataSyncTaskCancelOutcome.Stopping, await h.Launcher.CancelAsync(DataSyncTaskIds.Apply));
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Cancelled);
        Assert.AreSame(pull, h.StagedPulls.Peek(link.Id), "the next run applies it without a refetch");
        Assert.IsNull(h.Link(link.Id).LastErrorCode, "a stop is not a failure");
    }

    [TestMethod]
    public async Task While_the_actor_is_unverified_no_apply_holds_the_write_tasks_conflict_keys()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(daemon: true);
        h.Guard.IsVerified = false;
        var changed = h.AddLink("nas", l => l.SetCursors(new Dictionary<string, long>
            {["extensionGroup"] = 3, ["customProperty"] = 2}));
        var offline = h.AddLink("pc");
        h.Peers.Peers["pc"].BeforeHead = _ =>
            throw new DataSyncPeerException(DataSyncPeerErrorCode.Unreachable);

        await h.Scheduler.TickAsync(default);
        h.Clock.Advance(DataSyncSchedule.StartupDelay);
        await h.Scheduler.TickAsync(default);
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Completed);
        CollectionAssert.AreEqual(new[] {changed.Id}, h.StagedPulls.LinksWaiting().ToArray());
        Assert.AreEqual(nameof(DataSyncPeerErrorCode.Unreachable), h.Link(offline.Id).LastErrorCode);
        Assert.IsNull(h.Status(DataSyncTaskIds.Apply), "the fetch enqueues no apply before the actor is verified");

        // One Active peer never answered and two minutes have not passed: resource sync, path-mark sync and the
        // enhancer run meanwhile, because nothing sits on their conflict keys.
        await h.Btm.Enqueue(BTaskBuilder.Create("SyncResources").ConflictsWith("SyncResources")
            .Run(_ => Task.CompletedTask));
        await h.Btm.Start("SyncResources");
        await h.WaitForStatusAsync("SyncResources", BTaskStatus.Completed);

        // An apply enqueued anyway (a resolution's "Apply all") ends at once without applying; the pull waits.
        Assert.IsNotNull(await h.Launcher.EnqueueApplyAsync());
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);
        Assert.AreEqual(0, h.Runner.AutoSyncs.Count);
        CollectionAssert.AreEqual(new[] {changed.Id}, h.StagedPulls.LinksWaiting().ToArray());
        await h.Scheduler.TickAsync(default);
        Assert.IsFalse(h.Guard.IsVerified);
        Assert.AreEqual(BTaskStatus.Completed, h.Status(DataSyncTaskIds.Apply), "the scheduler waits for it too");

        // The tick that verifies enqueues it.
        h.Clock.Advance(DataSyncRuntimeState.VerificationWindow);
        await h.Scheduler.TickAsync(default);
        Assert.IsTrue(h.Guard.IsVerified, "verified two minutes after the start");
        await DataSyncRuntimeHarness.WaitUntilAsync(
            () => h.Runner.AutoSyncs.Any(c => c.Context.LinkId == changed.Id), "the pull is applied");
    }

    [TestMethod]
    public async Task An_apply_that_paused_leaves_the_once_flags_for_the_apply_after_the_resume()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        var link = h.AddLink("nas", l => l.SetOnceFlags(DataSyncMergeFlags.None with {SkipDeletionBreaker = true}));
        h.StagedPulls.Put(link.Id, PullFor(h, "nas"));
        h.Runner.AutoSyncOutcome = _ =>
            new DataSyncAutoSyncOutcome(null, DataSyncPauseReason.TooManyDecisions, 0, 0, 0, [], []);

        await h.Launcher.EnqueueApplyAsync();
        await h.Btm.Start(DataSyncTaskIds.Apply);
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);
        Assert.IsTrue(h.Runner.AutoSyncs.Single().Context.LinkFlags.SkipDeletionBreaker, "the apply used the flag");
        Assert.IsTrue(h.Link(link.Id).GetOnceFlags().SkipDeletionBreaker, "a paused apply consumed nothing");

        // After the resume, an apply that does not pause consumes it.
        h.Runner.AutoSyncOutcome = _ => new DataSyncAutoSyncOutcome(1, null, 0, 0, 1, [], []);
        h.StagedPulls.Put(link.Id, PullFor(h, "nas"));
        await h.Launcher.EnqueueApplyAsync();
        await h.Btm.Start(DataSyncTaskIds.Apply);
        await DataSyncRuntimeHarness.WaitUntilAsync(() => h.Runner.AutoSyncs.Count == 2, "the second apply ran");
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);
        Assert.AreEqual(DataSyncMergeFlags.None, h.Link(link.Id).GetOnceFlags());
    }
}
