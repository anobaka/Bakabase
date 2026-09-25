using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;

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
        h.Store.OpenItems[link.Id] = 3;

        await h.FetchOnceAsync();

        var query = peer.HeadQueries.Single();
        Assert.AreEqual("follow", query.Mode, "the counterpart was not known yet");
        Assert.AreEqual(3, query.Since["extensionGroup"]);
        Assert.AreEqual(5, query.Since["customProperty"]);
        Assert.AreEqual("a1a1a1a1a1a1a1a1", query.ReaderActorId);
        Assert.AreEqual("needsYou:3", query.ReaderState);
        Assert.AreEqual(0, peer.Manifests, "nothing new for this link's kinds");
        Assert.AreEqual(h.Clock.UtcNow + DataSyncSchedule.PollInterval, h.Link(link.Id).NextAttemptAtUtc);
        Assert.AreEqual(("nas", "a1a1a1a1a1a1a1a1", 12L), h.Guard.Evidence.Single());
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
        var pull = h.StagedPulls.Peek(link.Id)!;
        Assert.AreEqual("customProperty", pull.Kinds.Single().Kind);
        Assert.IsFalse(pull.Kinds.Single().FullReconciliation);
        Assert.AreEqual(Bakabase.Abstractions.Models.Domain.Constants.BTaskStatus.NotStarted,
            h.Status(DataSyncTaskIds.Apply), "the fetch enqueued the apply");
    }

    [TestMethod]
    public async Task An_equal_staged_pull_is_not_fetched_again_and_a_newer_one_replaces_it()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("nas", l => l.SetCursors(Cursors(3, 2)));
        var peer = h.Peers.Peers["nas"];

        await h.FetchOnceAsync();
        var first = h.StagedPulls.Peek(link.Id);
        Assert.IsNotNull(first);

        // The apply waits (e.g. behind Enhancement): the head still says 5, so nothing is fetched again.
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        await h.FetchOnceAsync();
        Assert.AreEqual(1, peer.Manifests);
        Assert.AreSame(first, h.StagedPulls.Peek(link.Id));

        // Another kind changes: the new pull replaces the waiting one and carries its kind too.
        peer.MaxSeq["extensionGroup"] = 4;
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        await h.FetchOnceAsync();
        Assert.AreEqual(2, peer.Manifests);
        CollectionAssert.AreEquivalent(new[] {"extensionGroup", "customProperty"},
            peer.ManifestQueries.Last().Since.Keys.ToArray());
        var second = h.StagedPulls.Peek(link.Id)!;
        CollectionAssert.AreEquivalent(new[] {"extensionGroup", "customProperty"},
            second.Kinds.Select(k => k.Kind).ToArray());
    }

    [TestMethod]
    public async Task B1_pauses_when_the_peer_answers_as_another_node_or_epoch()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var epoch = h.AddLink("a");
        var node = h.AddLink("b");
        var reported = h.AddLink("c");
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
        Assert.IsTrue(h.StagedPulls.Peek(link.Id)!.Kinds.All(k => k.FullReconciliation));
    }

    [TestMethod]
    public async Task Peer_errors_set_their_states_and_retry_intervals_and_an_answer_ends_them()
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

        Assert.AreEqual(DataSyncLinkState.AccessRevoked, h.Link(revoked.Id).State);
        Assert.AreEqual(now + DataSyncSchedule.AccessRetry, h.Link(revoked.Id).NextAttemptAtUtc);
        Assert.AreEqual(DataSyncLinkState.PeerTooOld, h.Link(tooOld.Id).State);
        Assert.AreEqual(now + DataSyncSchedule.VersionRetry, h.Link(tooOld.Id).NextAttemptAtUtc);
        Assert.AreEqual(DataSyncLinkState.PeerRemoteAccessOff, h.Link(remoteOff.Id).State);
        Assert.AreEqual(DataSyncLinkState.Active, h.Link(restorePending.Id).State, "attention, not a failure");
        Assert.AreEqual(nameof(DataSyncPeerErrorCode.PeerRestorePending), h.Link(restorePending.Id).LastErrorCode);
        Assert.AreEqual(0, h.Link(restorePending.Id).ConsecutiveFailures);
        Assert.AreEqual(now + DataSyncSchedule.AccessRetry, h.Link(restorePending.Id).NextAttemptAtUtc);

        // An hour later the peer answers again: back to Active.
        h.Clock.Advance(DataSyncSchedule.AccessRetry);
        await h.FetchOnceAsync();
        Assert.AreEqual(DataSyncLinkState.Active, h.Link(revoked.Id).State);
        Assert.IsNull(h.Link(revoked.Id).LastErrorCode);
        Assert.AreEqual(DataSyncLinkState.PeerTooOld, h.Link(tooOld.Id).State, "retried every 6 h");
    }

    [TestMethod]
    public async Task This_build_too_old_for_the_peer_is_ThisTooOld()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        var link = h.AddLink("nas");
        h.Peers.Peers["nas"].MinimumPeerContract = DataSyncContract.Version + 1;

        await h.FetchOnceAsync();
        Assert.AreEqual(DataSyncLinkState.ThisTooOld, h.Link(link.Id).State);
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
            Assert.AreEqual(h.Clock.UtcNow.AddMinutes(minutes), h.Link(link.Id).NextAttemptAtUtc);
            h.Clock.Advance(TimeSpan.FromMinutes(minutes));
        }

        Assert.AreEqual(DataSyncLinkState.Active, h.Link(link.Id).State, "offline is shown grey, never as a state");
        peer.HeadErrors.Enqueue(new DataSyncPeerException(DataSyncPeerErrorCode.Busy, null, 30));
        await h.FetchOnceAsync();
        Assert.AreEqual(h.Clock.UtcNow.AddSeconds(30), h.Link(link.Id).NextAttemptAtUtc, "Retry-After wins");

        h.Clock.Advance(TimeSpan.FromSeconds(30));
        await h.FetchOnceAsync();
        Assert.AreEqual(0, h.Link(link.Id).ConsecutiveFailures);
        Assert.IsNull(h.Link(link.Id).LastErrorCode);
        Assert.AreEqual(h.Clock.UtcNow + DataSyncSchedule.PollInterval, h.Link(link.Id).NextAttemptAtUtc);
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

        Assert.IsNull(h.StagedPulls.Peek(corrupted.Id), "never planned from an incomplete snapshot");
        Assert.AreEqual(nameof(DataSyncPeerErrorCode.InvalidResponse), h.Link(corrupted.Id).LastErrorCode);
        Assert.AreEqual(1, h.Link(corrupted.Id).ConsecutiveFailures);
        Assert.AreEqual(0, h.Link(corrupted.Id).GetCursors()["customProperty"], "the cursor did not move");

        Assert.AreEqual(2, h.Peers.Peers["b"].Manifests, "restarted once with a new manifest");
        Assert.IsNotNull(h.StagedPulls.Peek(expired.Id));
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
        h.StagedPulls.Take(link.Id);

        // More than 24 h after the last full reconciliation: every kind from 0 (§8.8).
        h.Clock.Advance(TimeSpan.FromHours(24));
        await h.FetchOnceAsync();
        Assert.IsTrue(peer.ManifestQueries.Last().Since.Values.All(s => s == 0));
        Assert.IsTrue(h.StagedPulls.Peek(link.Id)!.Kinds.All(k => k.FullReconciliation));
    }

    [TestMethod]
    public async Task The_actor_is_verified_once_every_active_link_answered_or_two_minutes_passed()
    {
        await using var h = await DataSyncRuntimeHarness.CreateAsync(registerFetchTask: false);
        h.Guard.IsVerified = false;
        h.AddLink("a");
        h.AddLink("b");
        h.AddLink("waiting", l => l.State = DataSyncLinkState.AwaitingAccess);
        h.Peers.Peers["b"].HeadErrors.Enqueue(new DataSyncPeerException(DataSyncPeerErrorCode.Unreachable));
        await h.FetchOnceAsync();
        Assert.IsFalse(h.Guard.IsVerified, "b's peer has not answered yet");

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await h.FetchOnceAsync();
        Assert.IsTrue(h.Guard.IsVerified, "every Active link answered one head; a link waiting for access does not count");
    }
}
