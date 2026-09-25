using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.DataSync.Runtime;

/// <summary>
/// The scheduler (§8.2): what it starts and enqueues, the startup, "Sync now" and session-online triggers, the actor's
/// verification (§5.6), grant events within a tick, the shutdown rule and the person-stop rule (§13.5
/// <c>ApplyBTaskTests</c>' scheduler rows).
/// </summary>
[TestClass]
public class DataSyncSchedulerTests
{
    private static async Task StartAsync(DataSyncRuntimeHarness h)
    {
        await h.Scheduler.TickAsync(default);
        h.Clock.Advance(DataSyncSchedule.StartupDelay);
    }

    [TestMethod]
    public async Task Nothing_runs_until_the_fetch_task_is_registered_then_every_link_is_due_in_five_seconds()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("nas", l => l.NextAttemptAtUtc = h.Clock.UtcNow.AddHours(5));

        await h.Scheduler.TickAsync(default);
        Assert.IsNull(h.Status(DataSyncTaskIds.Fetch));
        Assert.AreEqual(h.Clock.UtcNow.AddHours(5), h.Link(link.Id).NextAttemptAtUtc, "not ready: nothing is read");

        await h.RegisterFetchTaskAsync();
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(h.Clock.UtcNow + DataSyncSchedule.StartupDelay, h.Link(link.Id).NextAttemptAtUtc);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Fetch), "not due yet");

        h.Clock.Advance(DataSyncSchedule.StartupDelay);
        await h.Scheduler.TickAsync(default);
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Completed);
        Assert.AreEqual(1, h.Peers.Peers["nas"].HeadQueries.Count);
    }

    [TestMethod]
    public async Task The_scheduler_starts_nothing_after_ApplicationStopping()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        var link = h.AddLink("nas");
        await StartAsync(h);
        h.StagedPulls.Put(link.Id, new DataSyncStagedPull("nas", "Peer nas",
            h.Peers.Peers["nas"].Manifest(new DataSyncFeedQuery("twoWay", new Dictionary<string, long>(), null, null)),
            [], h.Clock.UtcNow));

        h.Lifetime.StopApplication();
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Fetch), "the due link started nothing");
        Assert.IsNull(h.Status(DataSyncTaskIds.Apply), "the staged pull enqueued nothing");
        Assert.IsNull(await h.Launcher.EnqueueApplyAsync());
        Assert.IsNull(await h.Launcher.EnqueueUndoAsync(3));
        Assert.IsNull(await h.Scheduler.SyncNowAsync(null, default));
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Fetch));
    }

    [TestMethod]
    public async Task The_scheduler_starts_nothing_once_the_task_manager_prepares_for_shutdown()
    {
        // The desktop app's exit calls PrepareForShutdown seconds before ApplicationStopping fires.
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        var link = h.AddLink("nas");
        await StartAsync(h);
        h.StagedPulls.Put(link.Id, new DataSyncStagedPull("nas", "Peer nas",
            h.Peers.Peers["nas"].Manifest(new DataSyncFeedQuery("twoWay", new Dictionary<string, long>(), null, null)),
            [], h.Clock.UtcNow));

        await h.Btm.PrepareForShutdown();
        Assert.IsTrue(h.Launcher.IsStopping);
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Fetch), "the due link started nothing");
        Assert.IsNull(h.Status(DataSyncTaskIds.Apply), "the staged pull enqueued nothing");
        Assert.IsNull(await h.Scheduler.SyncNowAsync(null, default));
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Fetch));
    }

    [TestMethod]
    public async Task Cancelling_the_waiting_DataSync_task_keeps_it_registered_and_holds_it_for_its_interval()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        h.AddLink("nas");
        await StartAsync(h);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Fetch));

        Assert.AreEqual(DataSyncTaskCancelOutcome.Held, await h.Launcher.CancelAsync(DataSyncTaskIds.Fetch));
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Fetch),
            "never removed: nothing would register it again before a restart");

        // The link is due, but the person's cancel holds like a stop, for the task's interval.
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Fetch));
        Assert.AreEqual(0, h.Peers.Peers["nas"].HeadQueries.Count);

        h.Clock.Advance(DataSyncRuntimeState.FetchInterval);
        await h.Scheduler.TickAsync(default);
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Completed);
        Assert.AreEqual(1, h.Peers.Peers["nas"].HeadQueries.Count);
    }

    [TestMethod]
    public async Task A_persons_cancel_of_the_apply_holds_it_until_the_next_pull_or_Sync_now()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        var link = h.AddLink("nas");
        var peer = h.Peers.Peers["nas"];
        await StartAsync(h);
        h.Store.Edit(link.Id, l => l.NextAttemptAtUtc = h.Clock.UtcNow.AddHours(1));
        h.StagedPulls.Put(link.Id, new DataSyncStagedPull("nas", "Peer nas",
            peer.Manifest(new DataSyncFeedQuery("twoWay", new Dictionary<string, long>(), null, null)), [],
            h.Clock.UtcNow));
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Apply));

        // Called off while it waits: not enqueued again for what already waited…
        Assert.AreEqual(DataSyncTaskCancelOutcome.Removed, await h.Launcher.CancelAsync(DataSyncTaskIds.Apply));
        await h.Scheduler.TickAsync(default);
        h.Clock.Advance(TimeSpan.FromMinutes(9));
        await h.Scheduler.TickAsync(default);
        Assert.IsNull(h.Status(DataSyncTaskIds.Apply));

        // …until the fetch interval passed,
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.Store.Edit(link.Id, l => l.NextAttemptAtUtc = h.Clock.UtcNow.AddHours(1));
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Apply));

        // …or a new pull arrived.
        Assert.AreEqual(DataSyncTaskCancelOutcome.Removed, await h.Launcher.CancelAsync(DataSyncTaskIds.Apply));
        await h.Scheduler.TickAsync(default);
        Assert.IsNull(h.Status(DataSyncTaskIds.Apply));
        peer.MaxSeq["customProperty"] = 9;
        h.Store.Edit(link.Id, l => l.NextAttemptAtUtc = h.Clock.UtcNow);
        await h.FetchOnceAsync();
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Apply), "new work ends the hold");

        // Stopped from the task list while it runs: held too, until "Sync now".
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Runner.Hold = async ct =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        };
        await h.Btm.Start(DataSyncTaskIds.Apply);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await h.Btm.Stop(DataSyncTaskIds.Apply);
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Cancelled);
        h.Runner.Hold = null;
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.Cancelled, h.Status(DataSyncTaskIds.Apply), "the stop is respected");
        Assert.AreEqual(1, h.StagedPulls.LinksWaiting().Count, "its pull waits");

        Assert.AreEqual(DataSyncTaskIds.Fetch, await h.Scheduler.SyncNowAsync(null, default));
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Completed);
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Apply));
    }

    [TestMethod]
    public async Task A_person_stopped_DataSync_is_not_restarted_until_its_interval_unless_Sync_now()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        h.AddLink("nas");
        var peer = h.Peers.Peers["nas"];
        var inHead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.BeforeHead = async ct =>
        {
            inHead.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        };
        await StartAsync(h);
        await h.Scheduler.TickAsync(default);
        await inHead.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await h.Btm.Stop(DataSyncTaskIds.Fetch);
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Cancelled);
        peer.BeforeHead = null;

        // The link is still due, but the person's stop holds for the task's interval.
        await h.Scheduler.TickAsync(default);
        h.Clock.Advance(TimeSpan.FromMinutes(9));
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.Cancelled, h.Status(DataSyncTaskIds.Fetch));

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await h.Scheduler.TickAsync(default);
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Completed);

        // Stopped again; "Sync now" overrides the hold at once.
        inHead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.BeforeHead = async ct =>
        {
            inHead.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        };
        Assert.AreEqual(DataSyncTaskIds.Fetch, await h.Scheduler.SyncNowAsync(null, default));
        await inHead.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await h.Btm.Stop(DataSyncTaskIds.Fetch);
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Cancelled);
        peer.BeforeHead = null;
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.Cancelled, h.Status(DataSyncTaskIds.Fetch));
        Assert.AreEqual(DataSyncTaskIds.Fetch, await h.Scheduler.SyncNowAsync(null, default));
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Completed);
    }

    [TestMethod]
    public async Task The_apply_is_enqueued_for_staged_pulls_and_pull_independent_once_flags_only()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        var later = h.Clock.UtcNow.AddHours(1);
        var deletions = h.AddLink("a", l =>
        {
            l.NextAttemptAtUtc = later;
            l.SetOnceFlags(DataSyncMergeFlags.None with {SkipDeletionBreaker = true});
        });
        await StartAsync(h);
        h.Store.Edit(deletions.Id, l => l.NextAttemptAtUtc = later);

        // Deletion flags act on the pull that tripped B2: they ride with the next fetch, never alone (N13).
        await h.Scheduler.TickAsync(default);
        Assert.IsNull(h.Status(DataSyncTaskIds.Apply));

        var applyAll = h.AddLink("b", l =>
        {
            l.NextAttemptAtUtc = later;
            l.SetOnceFlags(DataSyncMergeFlags.None with {SkipLargeChange = true});
        });
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Apply), "Apply all re-merges without a pull");

        // Run it: the pull-independent flag is consumed, the deletion flag of the other link is not touched.
        await h.Btm.Start(DataSyncTaskIds.Apply);
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, BTaskStatus.Completed);
        var call = h.Runner.AutoSyncs.Single();
        Assert.AreEqual(applyAll.Id, call.Context.LinkId);
        Assert.IsNull(call.Pull);
        Assert.IsTrue(call.Context.LinkFlags.SkipLargeChange);
        Assert.AreEqual(DataSyncMergeFlags.None, h.Link(applyAll.Id).GetOnceFlags());
        Assert.IsTrue(h.Link(deletions.Id).GetOnceFlags().SkipDeletionBreaker);

        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.Completed, h.Status(DataSyncTaskIds.Apply), "nothing more to enqueue");
    }

    [TestMethod]
    public async Task All_paused_and_an_unverified_actor_hold_the_tasks()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        var link = h.AddLink("nas");
        await StartAsync(h);
        h.StagedPulls.Put(link.Id, new DataSyncStagedPull("nas", "Peer nas",
            h.Peers.Peers["nas"].Manifest(new DataSyncFeedQuery("twoWay", new Dictionary<string, long>(), null, null)),
            [], h.Clock.UtcNow));

        h.Store.LocalState!.AllPaused = true;
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Fetch));
        Assert.IsNull(h.Status(DataSyncTaskIds.Apply));

        h.Store.LocalState!.AllPaused = false;
        h.Guard.IsVerified = false;
        h.Peers.Peers["nas"].BeforeHead = _ => throw new DataSyncPeerException(DataSyncPeerErrorCode.Unreachable);
        await h.Scheduler.TickAsync(default);
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Completed);
        Assert.IsNull(h.Status(DataSyncTaskIds.Apply), "no apply while the actor is unverified");
        Assert.IsFalse(h.Guard.IsVerified, "the Active link's peer never answered");
    }

    [TestMethod]
    public async Task A_head_verifies_the_actor_only_once_its_evidence_was_reported()
    {
        // §5.6: detection comes before any counter is reissued. b's head says a peer saw counter 1000 of this device's
        // actor; while the guard is still recording that (it rotates the actor), the scheduler's tick must not take
        // the answered head as verification and enqueue the apply of a's staged pull.
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        h.Guard.IsVerified = false;
        var a = h.AddLink("a", l => l.SetCursors(new Dictionary<string, long>
            { ["extensionGroup"] = 3, ["customProperty"] = 2 }));
        h.AddLink("b");
        h.Peers.Peers["b"].SeenCounter = 1000;
        var inEvidence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Guard.BeforeEvidence = async (peer, ct) =>
        {
            if (peer != "b") return;
            inEvidence.TrySetResult();
            await release.Task.WaitAsync(ct);
        };

        await StartAsync(h);
        await h.Scheduler.TickAsync(default);
        await inEvidence.Task.WaitAsync(TimeSpan.FromSeconds(10));
        CollectionAssert.AreEqual(new[] { a.Id }, h.StagedPulls.LinksWaiting().ToArray(), "a's pull waits");

        await h.Scheduler.TickAsync(default);
        Assert.IsFalse(h.Guard.IsVerified, "b's evidence is still being recorded");
        Assert.IsNull(h.Status(DataSyncTaskIds.Apply), "nothing may issue counters under the old actor yet");

        release.SetResult();
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Completed);
        Assert.AreEqual(("b", "a1a1a1a1a1a1a1a1", 1000L), h.Guard.Evidence.Single());
        Assert.IsTrue(h.Guard.IsVerified, "every Active link answered and its evidence was handled");
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Apply));
    }

    [TestMethod]
    public async Task Sync_now_makes_a_link_due_now_even_before_the_task_is_registered()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var later = h.Clock.UtcNow.AddHours(1);
        var a = h.AddLink("a", l => l.NextAttemptAtUtc = later);
        var b = h.AddLink("b", l => l.NextAttemptAtUtc = later);

        Assert.IsNull(await h.Scheduler.SyncNowAsync(b.Id, default), "the fetch task is not registered yet");
        Assert.AreEqual(h.Clock.UtcNow, h.Link(b.Id).NextAttemptAtUtc, "still due at the next cycle");
        Assert.AreEqual(later, h.Link(a.Id).NextAttemptAtUtc);
    }

    [TestMethod]
    public async Task A_link_whose_peer_session_came_online_is_fetched_at_once_and_the_tick_writes_nothing()
    {
        var sessions = new FakePeerSessions();
        await using var h = await DataSyncRuntimeHarness.CreateAsync(
            configure: s => s.AddSingleton<IDataSyncPeerSessions>(sessions));
        var a = h.AddLink("a");
        var b = h.AddLink("b");
        var stopped = h.AddLink("c", l =>
        {
            l.State = DataSyncLinkState.Stopped;
            l.Mode = DataSyncLinkMode.Off;
        });
        await StartAsync(h);

        // Both back off after failures: not due for ten minutes.
        foreach (var link in new[] { a, b })
        {
            h.Store.Edit(link.Id, l =>
            {
                l.ConsecutiveFailures = 4;
                l.NextAttemptAtUtc = h.Clock.UtcNow.AddMinutes(10);
            });
        }

        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.NotStarted, h.Status(DataSyncTaskIds.Fetch));

        // The person opened a's library: its federation session is verified again (§8.2).
        sessions.Online["a"] = 0;
        sessions.Online["c"] = 0;
        var inHead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Peers.Peers["a"].BeforeHead = async ct =>
        {
            inHead.TrySetResult();
            await release.Task.WaitAsync(ct);
        };
        var writes = h.Store.Writes;
        await h.Scheduler.TickAsync(default);
        await inHead.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(writes, h.Store.Writes, "the link was made due in memory, like a discovered peer");
        release.SetResult();
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Completed);
        Assert.AreEqual(1, h.Peers.Peers["a"].HeadQueries.Count);
        Assert.AreEqual(0, h.Peers.Peers["b"].HeadQueries.Count, "b's peer is still away");
        Assert.AreEqual(0, h.Peers.Peers["c"].HeadQueries.Count, "a stopped link is never pulled");

        // Still online on the next tick: that is not news.
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(BTaskStatus.Completed, h.Status(DataSyncTaskIds.Fetch));

        // Away and back again: news again.
        sessions.Online.TryRemove("a", out _);
        await h.Scheduler.TickAsync(default);
        sessions.Online["a"] = 0;
        await h.Scheduler.TickAsync(default);
        await DataSyncRuntimeHarness.WaitUntilAsync(() => h.Peers.Peers["a"].HeadQueries.Count == 2,
            "a is fetched again");
    }

    [TestMethod]
    public async Task A_granted_request_reaches_its_review_within_one_tick()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        var created = await h.Links.CreateAsync(new DataSyncLinkCreate("nas", null, null, DataSyncLinkMode.TwoWay, null),
            true, default);
        h.Grants.Readers.Add(new Bakabase.Modules.DataSync.Services.DataSyncGrantView("nas", "NAS", h.Clock.UtcNow));
        var link = created.Link!;
        Assert.AreEqual(DataSyncLinkState.AwaitingAccess, link.State);
        h.Peers.Add("nas");
        await StartAsync(h);
        h.Store.Edit(link.Id, l => l.NextAttemptAtUtc = h.Clock.UtcNow.AddHours(1));

        // The claim loop saw the grant and raised the event.
        h.Grants.Outbound.Add("nas");
        h.GrantEvents.OutboundGranted("nas");
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, h.Link(link.Id).State);
        await h.WaitForStatusAsync(DataSyncTaskIds.Fetch, BTaskStatus.Completed);

        Assert.AreEqual(1, h.Reviews.Staged.Count, "the same tick fetched and staged the review");
        Assert.AreEqual(1, h.Observer.Count("review:"));
    }
}
