using System.Collections.Concurrent;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.DataSync.Runtime;

/// <summary>
/// The fetch half of a cycle (§8.10.2): what the head asks and what it decides, breakers B1/B1b (§8.7), the peer
/// error states and their retry intervals (§8.1, §8.2), whole-pull failures, and no refetch while an equal staged
/// pull waits (N6).
/// </summary>
[TestClass]
public class DataSyncFetchHalfTests
{
    private static Dictionary<string, long> Cursors(long extensionGroup, long customProperty) =>
        new() {["extensionGroup"] = extensionGroup, ["customProperty"] = customProperty};

    [TestMethod]
    public async Task The_head_declares_mode_cursors_actor_and_state_and_nothing_new_needs_no_manifest()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("nas", l => l.Mode = DataSyncLinkMode.Follow);
        var peer = h.Peers.Peers["nas"];
        peer.Counterpart = new DataSyncFeedCounterpart("follow", true, DataSyncKindIds.All);
        peer.SeenCounter = 12;
        h.Store.SetOpenItems(link.Id, 3);

        await h.FetchOnceAsync();

        var query = peer.HeadQueries.Single();
        Assert.AreEqual("follow", query.Mode, "the counterpart was not known yet");
        Assert.AreEqual(3, query.Since["extensionGroup"]);
        Assert.AreEqual(5, query.Since["customProperty"]);
        Assert.AreEqual(h.SelfActor, query.ReaderActorId);
        Assert.AreEqual("needsYou:3", query.ReaderState);
        Assert.AreEqual(0, peer.Manifests, "nothing new for this link's kinds");
        Assert.AreEqual(h.Clock.UtcNow + DataSyncSchedule.PollInterval, h.State.GetAttempt(link.Id).Due);
        Assert.AreEqual(("nas", h.SelfActor, 12L), h.Guard.Evidence.Single());
        Assert.AreEqual("follow", h.Link(link.Id).GetCounterpart()!.Mode);

        // Both devices follow each other: the link works as two-way (§8.1).
        Assert.AreEqual(DataSyncLinkMode.TwoWay, h.Link(link.Id).GetEffectiveMode());
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        await h.FetchOnceAsync();
        Assert.AreEqual("twoWay", peer.HeadQueries.Last().Mode);
    }

    [TestMethod]
    public async Task Only_kinds_with_something_new_are_pulled_and_every_page_is_read()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("nas", l => l.SetCursors(Cursors(3, 2)));
        var peer = h.Peers.Peers["nas"];
        peer.PagesPerKind = 3;

        await h.FetchOnceAsync();

        var manifestQuery = peer.ManifestQueries.Single();
        CollectionAssert.AreEqual(new[] {"customProperty"}, manifestQuery.Since.Keys.ToArray());
        Assert.AreEqual(2, manifestQuery.Since["customProperty"]);
        Assert.AreEqual(3, peer.Pages);
        var pull = h.State.PeekPull(link.Id)!;
        Assert.AreEqual("customProperty", pull.Kinds.Single().Kind);
        Assert.IsFalse(pull.Kinds.Single().FullReconciliation);
        Assert.AreEqual(Bakabase.Abstractions.Models.Domain.Constants.BTaskStatus.NotStarted,
            h.Status(DataSyncTaskIds.Apply), "the fetch enqueued the apply");
    }

    [TestMethod]
    public async Task Pause_all_pressed_during_a_cycle_stages_nothing_more_and_fetches_no_further_link()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var a = h.AddLink("a", l => l.SetCursors(Cursors(3, 2)));
        h.AddLink("b", l => l.SetCursors(Cursors(3, 2)));

        // Pressed while a's pages are read: a's pull is not staged, and b is not asked at all.
        h.Peers.Peers["a"].OnPage = () => h.Store.EditLocalState(s => s.AllPaused = true);
        await h.FetchOnceAsync();

        Assert.AreEqual(1, h.Peers.Peers["a"].Pages);
        Assert.IsNull(h.State.PeekPull(a.Id));
        Assert.IsNull(h.Status(DataSyncTaskIds.Apply), "nothing to apply");
        Assert.AreEqual(0, h.Peers.Peers["b"].HeadQueries.Count);
    }

    [TestMethod]
    public async Task An_equal_staged_pull_is_not_fetched_again_and_a_newer_one_replaces_it()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("nas", l => l.SetCursors(Cursors(3, 2)));
        var peer = h.Peers.Peers["nas"];

        await h.FetchOnceAsync();
        var first = h.State.PeekPull(link.Id);
        Assert.IsNotNull(first);

        // The apply waits (e.g. behind Enhancement): the head still says 5, so nothing is fetched again.
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        await h.FetchOnceAsync();
        Assert.AreEqual(1, peer.Manifests);
        Assert.AreSame(first, h.State.PeekPull(link.Id));

        // Another kind changes: the new pull replaces the waiting one and carries its kind too.
        peer.MaxSeq["extensionGroup"] = 4;
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        await h.FetchOnceAsync();
        Assert.AreEqual(2, peer.Manifests);
        CollectionAssert.AreEquivalent(new[] {"extensionGroup", "customProperty"},
            peer.ManifestQueries.Last().Since.Keys.ToArray());
        var second = h.State.PeekPull(link.Id)!;
        CollectionAssert.AreEquivalent(new[] {"extensionGroup", "customProperty"},
            second.Kinds.Select(k => k.Kind).ToArray());
    }

    [TestMethod]
    public async Task B1_pauses_when_the_peer_answers_as_another_node_or_epoch()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var epoch = h.AddLink("a");
        var node = h.AddLink("b");
        // What the grant the reset revoked read as, before the peer's new identity was seen.
        var reported = h.AddLink("c", l => l.LastErrorCode = nameof(DataSyncPeerErrorCode.AccessRevoked));
        h.Peers.Peers["a"].Epoch = "epoch-2";
        h.Peers.Peers["b"].ServedNodeId = "someone-else";
        h.Peers.Peers["c"].HeadErrors.Enqueue(new DataSyncPeerException(DataSyncPeerErrorCode.IdentityConflict));

        await h.FetchOnceAsync();

        Assert.AreEqual(DataSyncLinkState.Paused, h.Link(epoch.Id).State);
        Assert.AreEqual(DataSyncPauseReason.PeerReset, h.Link(epoch.Id).PausedReason);
        Assert.AreEqual("epochChanged", h.Link(epoch.Id).PausedDetail);
        Assert.AreEqual("nodeChanged", h.Link(node.Id).PausedDetail);
        Assert.AreEqual(DataSyncPauseReason.PeerReset, h.Link(reported.Id).PausedReason);
        Assert.AreEqual(3, h.Observer.Count("paused:"));
        Assert.IsTrue(h.Peers.Peers.Values.All(p => p.Manifests == 0), "a pull is checked before anything is fetched");
        Assert.IsNull(h.Link(epoch.Id).LastErrorCode, "a pause is never an error");
        Assert.IsNull(h.Link(reported.Id).LastErrorCode, "the reset explains the revoked grant: no error stays with it");

        // A paused link is not fetched.
        h.Clock.Advance(TimeSpan.FromHours(1));
        await h.FetchOnceAsync();
        Assert.AreEqual(1, h.Peers.Peers["a"].HeadQueries.Count);
    }

    [TestMethod]
    public async Task B1b_pauses_a_restored_peer_and_its_resume_is_a_full_reconciliation()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("nas", l => l.SetCursors(Cursors(3, 9)));
        var peer = h.Peers.Peers["nas"];

        await h.FetchOnceAsync();
        Assert.AreEqual(DataSyncPauseReason.PeerReset, h.Link(link.Id).PausedReason);
        Assert.AreEqual(DataSyncLinkService.RestoredDetail, h.Link(link.Id).PausedDetail);

        await h.Links.ResumeAsync(link.Id, Bakabase.Modules.DataSync.Services.DataSyncResumeAction.Resume, true,
            default);
        await h.FetchOnceAsync();
        var query = peer.ManifestQueries.Single();
        Assert.IsTrue(query.Since.Values.All(s => s == 0));
        Assert.AreEqual(2, query.Since.Count);
        Assert.IsTrue(h.State.PeekPull(link.Id)!.Kinds.All(k => k.FullReconciliation));
    }

    [TestMethod]
    public async Task A_peer_that_found_the_cursor_ahead_of_its_own_serves_from_0_and_is_not_paused_as_restored()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("nas", l => l.SetCursors(Cursors(3, 9)));
        // The source detected its own restore through this device's cursor and says so (§7.5.1 step 1).
        h.Peers.Peers["nas"].Superseded.Add("customProperty");

        await h.FetchOnceAsync();

        Assert.AreEqual(DataSyncLinkState.Active, h.Link(link.Id).State, "no B1b: the peer serves the kind from 0");
        Assert.IsTrue(h.State.PeekPull(link.Id)!.Kinds.Single(k => k.Kind == "customProperty").FullReconciliation,
            "the superseded kind comes from 0, which lowers the cursor");
    }

    [TestMethod]
    public async Task Peer_errors_set_their_codes_and_retry_intervals_and_an_answer_ends_them()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var revoked = h.AddLink("a");
        var tooOld = h.AddLink("b");
        var remoteOff = h.AddLink("c");
        var restorePending = h.AddLink("d");
        h.Peers.Peers["a"].HeadErrors.Enqueue(new DataSyncPeerException(DataSyncPeerErrorCode.AccessRevoked));
        h.Peers.Peers["b"].Contract = 0;
        h.Peers.Peers["c"].HeadErrors.Enqueue(new DataSyncPeerException(DataSyncPeerErrorCode.PeerRemoteAccessOff));
        h.Peers.Peers["d"].MaxSeq["customProperty"] = 6;
        h.Peers.Peers["d"].ManifestErrors.Enqueue(new DataSyncPeerException(DataSyncPeerErrorCode.PeerRestorePending));
        var now = h.Clock.UtcNow;

        await h.FetchOnceAsync();

        // The link keeps its state and carries the code; a new code is a transition, so the status says it.
        Assert.AreEqual((DataSyncLinkState.Active, nameof(DataSyncPeerErrorCode.AccessRevoked), true),
            (h.Link(revoked.Id).State, h.Link(revoked.Id).LastErrorCode, h.Link(revoked.Id).HasPeerError()));
        Assert.AreEqual(1, h.Observer.Count($"changed:{revoked.Id}:"));
        Assert.AreEqual(now + DataSyncSchedule.AccessRetry, h.State.GetAttempt(revoked.Id).Due);
        Assert.AreEqual(nameof(DataSyncPeerErrorCode.PeerTooOld), h.Link(tooOld.Id).LastErrorCode);
        Assert.AreEqual(now + DataSyncSchedule.VersionRetry, h.State.GetAttempt(tooOld.Id).Due);
        Assert.AreEqual(nameof(DataSyncPeerErrorCode.PeerRemoteAccessOff), h.Link(remoteOff.Id).LastErrorCode);
        Assert.AreEqual(nameof(DataSyncPeerErrorCode.PeerRestorePending), h.Link(restorePending.Id).LastErrorCode);
        Assert.IsFalse(h.Link(restorePending.Id).HasPeerError(), "attention, not a refusal");
        Assert.AreEqual(0, h.Link(restorePending.Id).ConsecutiveFailures);
        Assert.AreEqual(now + DataSyncSchedule.AccessRetry, h.State.GetAttempt(restorePending.Id).Due);

        // An hour later the peer answers again: the code goes.
        h.Clock.Advance(DataSyncSchedule.AccessRetry);
        await h.FetchOnceAsync();
        Assert.IsNull(h.Link(revoked.Id).LastErrorCode);
        Assert.AreEqual(nameof(DataSyncPeerErrorCode.PeerTooOld), h.Link(tooOld.Id).LastErrorCode, "retried every 6 h");
    }

    [TestMethod]
    public async Task This_build_too_old_for_the_peer_is_ThisTooOld()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("nas");
        h.Peers.Peers["nas"].MinimumPeerContract = DataSyncContract.Version + 1;

        await h.FetchOnceAsync();
        Assert.AreEqual(nameof(DataSyncPeerErrorCode.ThisTooOld), h.Link(link.Id).LastErrorCode);
    }

    [TestMethod]
    public async Task Unreachable_and_busy_back_off_and_success_resets_the_backoff()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("nas");
        var peer = h.Peers.Peers["nas"];
        var expected = new[] {1, 2, 5, 10, 10};
        foreach (var minutes in expected)
        {
            peer.HeadErrors.Enqueue(new DataSyncPeerException(DataSyncPeerErrorCode.Unreachable));
            await h.FetchOnceAsync();
            Assert.AreEqual(h.Clock.UtcNow.AddMinutes(minutes), h.State.GetAttempt(link.Id).Due);
            h.Clock.Advance(TimeSpan.FromMinutes(minutes));
        }

        Assert.AreEqual(DataSyncLinkState.Active, h.Link(link.Id).State, "offline is shown grey, never as a state");
        peer.HeadErrors.Enqueue(new DataSyncPeerException(DataSyncPeerErrorCode.Busy, null, 30));
        await h.FetchOnceAsync();
        Assert.AreEqual(h.Clock.UtcNow.AddSeconds(30), h.State.GetAttempt(link.Id).Due, "Retry-After wins");

        h.Clock.Advance(TimeSpan.FromSeconds(30));
        await h.FetchOnceAsync();
        Assert.AreEqual(0, h.Link(link.Id).ConsecutiveFailures);
        Assert.IsNull(h.Link(link.Id).LastErrorCode);
        Assert.AreEqual(h.Clock.UtcNow + DataSyncSchedule.PollInterval, h.State.GetAttempt(link.Id).Due);
    }

    /// <summary>
    /// One fetch per peer at a time (§7.6): while one fetch reads a peer, a second fetch of the same peer waits, and past
    /// its wait is Busy without reaching the peer; another peer is never held up, and once released the peer is free.
    /// </summary>
    [TestMethod]
    public async Task One_fetch_per_peer_at_a_time()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var nas = h.AddLink("nas");
        var pc = h.AddLink("pc");
        h.Fetcher.FetchWait = TimeSpan.FromMilliseconds(200);
        var peer = h.Peers.Peers["nas"];
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.BeforeHead = async _ =>
        {
            peer.BeforeHead = null;
            holding.SetResult();
            await release.Task;
        };

        var first = h.Fetcher.FetchLinkAsync(h.Link(nas.Id), false, null, default);
        await holding.Task;
        await h.Fetcher.FetchLinkAsync(h.Link(nas.Id), false, null, default);
        Assert.AreEqual(nameof(DataSyncPeerErrorCode.Busy), h.Link(nas.Id).LastErrorCode);
        Assert.AreEqual(0, peer.HeadQueries.Count, "the second fetch never reached the peer (the first is still held)");
        await h.Fetcher.FetchLinkAsync(h.Link(pc.Id), false, null, default);
        Assert.AreEqual(1, h.Peers.Peers["pc"].HeadQueries.Count, "another peer is not held up");

        release.SetResult();
        await first;
        Assert.AreEqual(1, peer.HeadQueries.Count);
        await h.Fetcher.FetchLinkAsync(h.Link(nas.Id), false, null, default);
        Assert.AreEqual(2, peer.HeadQueries.Count);
        Assert.IsNull(h.Link(nas.Id).LastErrorCode);
    }

    [TestMethod]
    public async Task A_corrupted_page_discards_the_whole_pull_and_an_expired_snapshot_restarts_once()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var corrupted = h.AddLink("a", l => l.SetCursors(Cursors(0, 0)));
        var expired = h.AddLink("b", l => l.SetCursors(Cursors(3, 2)));
        h.Peers.Peers["a"].BadPageOf = "customProperty";
        h.Peers.Peers["b"].PageErrors.Enqueue(new DataSyncPeerException(DataSyncPeerErrorCode.SnapshotExpired));

        await h.FetchOnceAsync();

        Assert.IsNull(h.State.PeekPull(corrupted.Id), "never planned from an incomplete snapshot");
        Assert.AreEqual(nameof(DataSyncPeerErrorCode.InvalidResponse), h.Link(corrupted.Id).LastErrorCode);
        Assert.AreEqual(1, h.Link(corrupted.Id).ConsecutiveFailures);
        Assert.AreEqual(0, h.Link(corrupted.Id).GetCursors()["customProperty"], "the cursor did not move");

        Assert.AreEqual(2, h.Peers.Peers["b"].Manifests, "restarted once with a new manifest");
        Assert.IsNotNull(h.State.PeekPull(expired.Id));
        Assert.IsNull(h.Link(expired.Id).LastErrorCode);
    }

    [TestMethod]
    public async Task The_fallback_pull_and_the_daily_full_reconciliation()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("nas");
        var peer = h.Peers.Peers["nas"];

        await h.FetchOnceAsync();
        Assert.AreEqual(0, peer.Manifests);

        // Every fetch interval, a link with cursors pulls incrementally even without a head change (§8.2).
        h.Clock.Advance(DataSyncRuntimeState.FetchInterval);
        await h.FetchOnceAsync();
        Assert.AreEqual(1, peer.Manifests);
        Assert.AreEqual(3, peer.ManifestQueries.Last().Since["extensionGroup"]);
        h.State.TakePull(link.Id);

        // More than 24 h after the last full reconciliation: every kind from 0 (§8.8).
        h.Clock.Advance(TimeSpan.FromHours(24));
        await h.FetchOnceAsync();
        Assert.IsTrue(peer.ManifestQueries.Last().Since.Values.All(s => s == 0));
        Assert.IsTrue(h.State.PeekPull(link.Id)!.Kinds.All(k => k.FullReconciliation));
    }

    /// <summary>
    /// "Comparing everything with {{name}}…" (§11.6): the link view says a full reconciliation runs while its pages are
    /// read, while the pull waits for <c>DataSyncApply</c> and while it is applied, and not for an incremental pull.
    /// </summary>
    [TestMethod]
    public async Task A_link_says_its_full_reconciliation_runs_from_the_fetch_to_the_end_of_the_apply()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        var link = h.AddLink("nas", l => l.LastFullReconciliationAtUtc = h.Clock.UtcNow - TimeSpan.FromHours(25));
        var peer = h.Peers.Peers["nas"];
        async Task<bool> RunningAsync()
        {
            await using var scope = h.Provider.CreateAsyncScope();
            var view = await new DataSyncViews(scope.ServiceProvider).GetLinkAsync(link.Id, default);
            return view!.FullReconciliationRunning;
        }

        Assert.IsFalse(await RunningAsync());
        var whileRead = new ConcurrentQueue<bool>();
        peer.OnPage = () => whileRead.Enqueue(h.Provider.GetRequiredService<DataSyncRuntimeState>()
            .IsFullReconciliationRunning(link.Id));
        await h.FetchOnceAsync();
        Assert.IsTrue(whileRead.Count > 0 && whileRead.All(r => r), "while its pages are read");
        Assert.IsTrue(h.State.PeekPull(link.Id)!.Kinds.All(k => k.FullReconciliation));
        Assert.IsTrue(await RunningAsync(), "while it waits for the apply");

        var whileApplied = new ConcurrentQueue<bool>();
        h.Runner.Hold = async _ => whileApplied.Enqueue(await RunningAsync());
        await h.Btm.Start(DataSyncTaskIds.Apply);
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, Bakabase.Abstractions.Models.Domain.Constants.BTaskStatus.Completed);
        CollectionAssert.AreEqual(new[] { true }, whileApplied.ToArray(), "while it is applied");
        Assert.IsFalse(await RunningAsync(), "applied");

        // The next pull is incremental: nothing says it compares everything.
        h.Runner.Hold = null;
        whileRead.Clear();
        peer.MaxSeq["customProperty"] = 6;
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        await h.FetchOnceAsync();
        Assert.IsTrue(whileRead.Count > 0 && whileRead.All(r => !r));
        Assert.IsFalse(h.State.PeekPull(link.Id)!.Kinds.Single().FullReconciliation);
        Assert.IsFalse(await RunningAsync());
    }

    [TestMethod]
    public async Task The_actor_is_verified_by_a_cycle_that_asked_every_active_link()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        h.Guard.IsVerified = false;
        h.AddLink("a");
        var b = h.AddLink("b");
        h.AddLink("waiting", l => l.State = DataSyncLinkState.AwaitingAccess);
        h.State.RecordAttempt(b.Id, h.Clock.UtcNow, h.Clock.UtcNow + TimeSpan.FromHours(1));
        await h.FetchOnceAsync();
        Assert.IsFalse(h.Guard.IsVerified, "b was not due, so its peer was not asked");

        await h.Links.MarkDueAsync(null, default);
        h.Peers.Peers["b"].HeadErrors.Enqueue(new DataSyncPeerException(DataSyncPeerErrorCode.Unreachable));
        await h.FetchOnceAsync();
        Assert.IsTrue(h.Guard.IsVerified, "every Active link was asked, one unreachable; waiting for access is not Active");
    }

    [TestMethod]
    public async Task A_kind_added_to_a_link_is_merged_from_its_start_without_a_preview()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("nas", l => l.SetCursors(new Dictionary<string, long> {["customProperty"] = 5}));

        await h.FetchOnceAsync();

        Assert.IsNull(h.State.PeekPreview(link.Id), "only a link's first sync is previewed (§8.3)");
        Assert.AreEqual(0, h.Peers.Peers["nas"].ManifestQueries.Single().Since[DataSyncKindIds.ExtensionGroup]);
        CollectionAssert.Contains(h.State.PeekPull(link.Id)!.Kinds.Select(k => k.Kind).ToList(),
            DataSyncKindIds.ExtensionGroup);
    }

    [TestMethod]
    public async Task Pending_records_that_wait_for_a_re_merge_are_applied_without_a_pull()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        var retrying = h.AddLink("nas");
        var quiet = h.AddLink("pc");
        // A ChangedDuringApply record (Retry) waits on nas: nothing is new at either peer (§8.4 condition 3).
        var key = SyncKey.New();
        var record = new DataSyncWireRecord([key.Value], "nas", 4, DataSyncVersionVector.Empty, null, false, 1, null,
            new System.Text.Json.Nodes.JsonObject {["name"] = "Genre"}, null, null);
        h.Store.SetBases(retrying.Id, new DataSyncPeerBase(DataSyncKindIds.CustomProperty, key, DataSyncBaseState.Normal,
            null, null, new Dictionary<string, string>(), new DataSyncPendingRecord(record, "sha256:r",
                DataSyncPendingReason.Retry, DataSyncMergeFlags.None), null, []));

        await h.FetchOnceAsync();
        Assert.AreEqual(0, h.Peers.Peers["nas"].Manifests, "no pull: nothing new at the peer");
        Assert.AreEqual(0, h.State.StagedLinks().Count);

        // The scheduler hands the re-merge to DataSyncApply on its next tick, not at the next fallback pull.
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(Bakabase.Abstractions.Models.Domain.Constants.BTaskStatus.NotStarted,
            h.Status(DataSyncTaskIds.Apply));
        await h.Btm.Start(DataSyncTaskIds.Apply);
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, Bakabase.Abstractions.Models.Domain.Constants.BTaskStatus.Completed);
        var call = h.Runner.AutoSyncs.Single();
        Assert.AreEqual(retrying.Id, call.Link.Id);
        Assert.IsNull(call.Pull, "re-merged from the pending records alone");
        Assert.AreNotEqual(quiet.Id, call.Link.Id);

        // Taken by that apply: nothing is enqueued again until the next head finds records waiting.
        await h.Scheduler.TickAsync(default);
        Assert.AreEqual(Bakabase.Abstractions.Models.Domain.Constants.BTaskStatus.Completed,
            h.Status(DataSyncTaskIds.Apply));
    }

    [TestMethod]
    public async Task A_record_waiting_for_a_person_is_re_merged_once_after_the_start_and_then_waits()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync();
        var link = h.AddLink("nas");
        var key = SyncKey.New();
        var record = new DataSyncWireRecord([key.Value], "nas", 4, DataSyncVersionVector.Empty, null, false, 1, null,
            new System.Text.Json.Nodes.JsonObject {["name"] = "Genre"}, null, null);
        h.Store.SetBases(link.Id, new DataSyncPeerBase(DataSyncKindIds.CustomProperty, key, DataSyncBaseState.Normal,
            null, null, new Dictionary<string, string>(), new DataSyncPendingRecord(record, "sha256:r",
                DataSyncPendingReason.Conflict, DataSyncMergeFlags.None), null, []));

        // The first cycle since the start merges it again once (an upgrade may read it differently now).
        await h.FetchOnceAsync();
        Assert.IsTrue(h.State.IsReMergeRequested(link.Id));
        await h.Scheduler.TickAsync(default);
        await h.Btm.Start(DataSyncTaskIds.Apply);
        await h.WaitForStatusAsync(DataSyncTaskIds.Apply, Bakabase.Abstractions.Models.Domain.Constants.BTaskStatus.Completed);
        Assert.AreEqual(1, h.Runner.AutoSyncs.Count);

        // Nothing new at the peer and nothing changed here: every later cycle leaves it to its person.
        for (var cycle = 2; cycle <= 4; cycle++)
        {
            h.Clock.Advance(DataSyncSchedule.PollInterval);
            await h.FetchOnceAsync();
            Assert.AreEqual(cycle, h.Peers.Peers["nas"].HeadQueries.Count, "the cycle asked the peer");
            Assert.IsFalse(h.State.IsReMergeRequested(link.Id), $"cycle {cycle} applies nothing");
        }
    }

    [TestMethod]
    public async Task A_pause_takes_effect_between_the_pages_of_a_snapshot()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("nas", l => l.SetCursors(Cursors(3, 2)));
        var peer = h.Peers.Peers["nas"];
        peer.PagesPerKind = 3;
        var pause = new Bootstrap.Components.Tasks.PauseTokenSource();
        peer.OnPage = () =>
        {
            if (peer.Pages == 1) pause.Pause();
        };

        var fetch = h.FetchOnceAsync(pause);
        await DataSyncHarness.WaitUntilAsync(() => peer.Pages >= 1, "the first page is read");
        await Task.Delay(200);
        Assert.AreEqual(1, peer.Pages, "no further page is read while the task is paused");
        Assert.IsFalse(fetch.IsCompleted);

        pause.Resume();
        await fetch.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(3, peer.Pages);
        Assert.IsNotNull(h.State.PeekPull(link.Id));
    }

    /// <summary>
    /// E fix 1: the fetch half writes its bookkeeping in a write transaction that reads the link row afresh, so it never
    /// puts back a row the apply committed meanwhile — with the runtime's row transactions held by the test, and with a
    /// real apply transaction holding SQLite's write lock.
    /// </summary>
    [TestMethod]
    [DataRow(false, DisplayName = "row transactions held")]
    [DataRow(true, DisplayName = "a real transaction on SQLite")]
    public async Task A_head_poll_reads_the_link_only_after_an_open_apply_transaction_committed(bool real)
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false,
            heldRowTransactions: !real);
        var link = h.AddLink("nas", l =>
        {
            l.State = DataSyncLinkState.AwaitingReview;
            l.FirstContactCompletedAtUtc = null;
            l.CursorsJson = "{}";
        });
        var peer = h.Peers.Peers["nas"];
        // The first sync is being applied, so the cycle stages none (§8.3).
        h.State.StagePreview(link.Id, new DataSyncStagedPull("nas", "NAS",
            peer.Manifest(new DataSyncFeedQuery("twoWay", new Dictionary<string, long>(), null, null)), [],
            h.Clock.UtcNow));
        void Applied(Bakabase.Modules.DataSync.Models.Db.DataSyncLinkDbModel l)
        {
            l.State = DataSyncLinkState.Active;
            l.SetCursors(Cursors(3, 5));
            l.FirstContactCompletedAtUtc = h.Clock.UtcNow;
        }

        // The first sync's apply is in its transaction when the head answers.
        Task fetch;
        if (real)
        {
            await using var scope = h.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Bakabase.InsideWorld.Business.BakabaseDbContext>();
            await using var apply = await db.Database.BeginTransactionAsync();
            Applied(db.DataSyncLinks.Single(l => l.Id == link.Id));
            await db.SaveChangesAsync();
            fetch = Task.Run(() => h.FetchOnceAsync());
            await DataSyncHarness.WaitUntilAsync(() => peer.HeadQueries.Count == 1, "the head answered");
            await Task.Delay(300);
            Assert.IsFalse(fetch.IsCompleted, "the head's answer waits for the open transaction");
            await apply.CommitAsync();
        }
        else
        {
            using (h.RowTransactions.Hold())
            {
                fetch = h.FetchOnceAsync();
                await DataSyncHarness.WaitUntilAsync(() => h.RowTransactions.Waiting == 1,
                    "the head's answer waits for the open transaction");
                Assert.AreEqual(1, peer.HeadQueries.Count);
                h.Store.Edit(link.Id, Applied);
            }
        }

        await fetch.WaitAsync(TimeSpan.FromSeconds(10));
        var after = h.Link(link.Id);
        Assert.AreEqual(DataSyncLinkState.Active, after.State, "the committed first sync is never undone by the head");
        Assert.AreEqual(5, after.GetCursors()["customProperty"]);
        Assert.IsNotNull(after.FirstContactCompletedAtUtc);
        Assert.AreEqual(0, peer.ManifestQueries.Count, "the fresh cursors say nothing is new");
    }
}
