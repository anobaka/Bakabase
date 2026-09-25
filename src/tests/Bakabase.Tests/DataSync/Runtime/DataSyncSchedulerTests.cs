using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Tests.DataSync.Runtime;

/// <summary>
/// The scheduler (§8.2): what it starts and enqueues, the startup and "Sync now" triggers, grant events within a
/// tick, the shutdown rule and the person-stop rule (§13.5 <c>ApplyBTaskTests</c>' scheduler rows).
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
    public async Task Sync_now_and_a_discovered_peer_make_links_due_now()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var later = h.Clock.UtcNow.AddHours(1);
        var a = h.AddLink("a", l => l.NextAttemptAtUtc = later);
        var b = h.AddLink("b", l => l.NextAttemptAtUtc = later);
        var stopped = h.AddLink("c", l =>
        {
            l.State = DataSyncLinkState.Stopped;
            l.Mode = DataSyncLinkMode.Off;
            l.NextAttemptAtUtc = null;
        });

        await h.Links.MarkPeersDueAsync(["a", "c"], default);
        Assert.AreEqual(h.Clock.UtcNow, h.Link(a.Id).NextAttemptAtUtc);
        Assert.AreEqual(later, h.Link(b.Id).NextAttemptAtUtc);
        Assert.IsNull(h.Link(stopped.Id).NextAttemptAtUtc, "a stopped link is never pulled");

        Assert.IsNull(await h.Scheduler.SyncNowAsync(b.Id, default), "the fetch task is not registered yet");
        Assert.AreEqual(h.Clock.UtcNow, h.Link(b.Id).NextAttemptAtUtc, "still due at the next cycle");
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
