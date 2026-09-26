using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Tests.DataSync.Runtime;
using Microsoft.Extensions.DependencyInjection;
using static Bakabase.Tests.DataSync.Api.DataSyncApiHarness;

namespace Bakabase.Tests.DataSync.Api;

/// <summary>
/// Data sync's notifications (§9.4), through the runtime's observer: desktop only, the sources and routes, at most one
/// per link per cycle with the hourly and daily limits, one per restore detection and per staged review, and read
/// state that follows the items it announced.
/// </summary>
[TestClass]
public class DataSyncNotifierTests
{
    private static IDataSyncRuntimeObserver Observer(DataSyncApiHarness h) =>
        h.Provider.GetRequiredService<IDataSyncRuntimeObserver>();

    private static DataSyncAutoSyncOutcome Outcome(int newItems = 0, int? logId = null,
        IReadOnlyList<DataSyncMergeNote>? notes = null, IReadOnlyList<long>? closed = null) =>
        new(logId, null, newItems, closed?.Count ?? 0, 1, notes ?? [], closed ?? [], DataSyncAutoSyncEnd.Committed);

    [TestMethod]
    public async Task A_headless_install_creates_none()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        h.HostKind.IsHeadless = true;
        var link = h.AddLink("node-pc");
        h.AddItem(DataSyncInboxItemType.FieldConflict, link.Id, Key(1));

        await Observer(h).AutoSyncAppliedAsync(link, Outcome(newItems: 1), false, default);
        await Observer(h).LinkPausedAsync(link with { State = DataSyncLinkState.Paused,
            PausedReason = DataSyncPauseReason.MassDeletion }, default);

        Assert.AreEqual(0, h.Notifications.Records.Count);
        Assert.IsNull(h.Store.Item(1).NotificationId);
    }

    [TestMethod]
    public async Task New_decisions_are_announced_once_an_hour_and_join_an_unread_announcement()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        var link = h.AddLink("node-pc", l => l.PeerName = "PC-2");
        var first = h.AddItem(DataSyncInboxItemType.FieldConflict, link.Id, Key(1));

        await Observer(h).AutoSyncAppliedAsync(link, Outcome(newItems: 1), false, default);
        var announced = h.Notifications.Records.Single();
        Assert.AreEqual(DataSyncNotifier.SourceOf("node-pc"), announced.Source);
        Assert.AreEqual(DataSyncNotifier.NeedsYouCase, FakeNotificationService.CaseOf(announced));
        Assert.AreEqual("/data-sync?tab=inbox&peer=node-pc", FakeNotificationService.RouteOf(announced));
        Assert.AreEqual("DataSync_Notify_NeedsYou_Title(PC-2|1)", announced.Title);
        Assert.AreEqual(AppNotificationSeverity.Info, announced.Severity);
        Assert.AreEqual(announced.Id, h.Store.Item(first.Id).NotificationId);

        // Another cycle within the hour: its items join the unread announcement.
        h.Clock.Advance(TimeSpan.FromMinutes(20));
        var second = h.AddItem(DataSyncInboxItemType.DeletedThere, link.Id, Key(2), "");
        await Observer(h).AutoSyncAppliedAsync(link, Outcome(newItems: 1), false, default);
        Assert.AreEqual(1, h.Notifications.Records.Count);
        Assert.AreEqual(announced.Id, h.Store.Item(second.Id).NotificationId);

        // An hour later a new one goes out.
        h.Clock.Advance(TimeSpan.FromHours(1));
        h.AddItem(DataSyncInboxItemType.DeletedThere, link.Id, Key(3), "");
        await Observer(h).AutoSyncAppliedAsync(link, Outcome(newItems: 1), false, default);
        Assert.AreEqual(2, h.Notifications.Records.Count);

        // Nothing new, nothing sent.
        await Observer(h).AutoSyncAppliedAsync(link, Outcome(newItems: 0), false, default);
        Assert.AreEqual(2, h.Notifications.Records.Count);
    }

    [TestMethod]
    public async Task Decisions_never_join_an_announcement_of_something_else()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        var link = h.AddLink("node-pc");
        await Observer(h).LinkPausedAsync(link with { State = DataSyncLinkState.Paused,
            PausedReason = DataSyncPauseReason.TooManyDecisions }, default);
        var item = h.AddItem(DataSyncInboxItemType.FieldConflict, link.Id, Key(1));

        // Within the hour of the pause nothing else goes out, and the item waits for its own announcement.
        await Observer(h).AutoSyncAppliedAsync(link, Outcome(newItems: 1), false, default);
        Assert.AreEqual(1, h.Notifications.Records.Count);
        Assert.IsNull(h.Store.Item(item.Id).NotificationId);

        h.Clock.Advance(TimeSpan.FromHours(1));
        await Observer(h).AutoSyncAppliedAsync(link, Outcome(newItems: 1), false, default);
        Assert.AreEqual(h.Notifications.Records.Last().Id, h.Store.Item(item.Id).NotificationId);
    }

    [TestMethod]
    public async Task An_announcement_is_read_once_every_item_it_announced_has_closed_here_or_elsewhere()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        var link = h.AddLink("node-pc");
        var first = h.AddItem(DataSyncInboxItemType.FieldConflict, link.Id, Key(1));
        var second = h.AddItem(DataSyncInboxItemType.DeletedThere, link.Id, Key(2), "");
        await Observer(h).AutoSyncAppliedAsync(link, Outcome(newItems: 2), false, default);
        var announcement = h.Notifications.Records.Single();

        h.Store.CloseWhere(i => i.Id == first.Id, DataSyncInboxClosure.ResolvedElsewhere);
        await Observer(h).AutoSyncAppliedAsync(link, Outcome(closed: [first.Id]), false, default);
        Assert.IsFalse(h.Notifications.Records.Single().IsRead, "one item still waits");

        h.Store.CloseWhere(i => i.Id == second.Id, DataSyncInboxClosure.ResolvedHere);
        await Observer(h).WriteAppliedAsync(DataSyncHistoryKind.Resolution, 1, null, default);
        Assert.IsTrue(h.Notifications.Records.Single(r => r.Id == announcement.Id).IsRead);

        // A sweep that finds nothing never asks to mark "everything" read (the fake refuses an empty list).
        await h.Notifier.SweepAsync(default);
        Assert.IsTrue(h.Notifications.MarkedRead.All(ids => ids.Length > 0));
    }

    [TestMethod]
    public async Task The_approvers_first_sync_reports_its_counts_and_announces_its_items()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        var link = h.AddLink("node-pc", l => l.PeerName = "PC-2");
        var suggestion = h.AddItem(DataSyncInboxItemType.LinkSuggestion, link.Id, Key(1), "");
        var logId = await h.Store.AddHistoryAsync(new DataSyncApplyLogDbModel
        {
            Kind = DataSyncHistoryKind.AutoSync, AppliedAtUtc = h.Clock.UtcNow,
            SummaryJson = DataSyncHistoryJson.WriteSummary(new DataSyncHistoryCounts(94, 0, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0)),
            ResultJson = "{}", PreImageJson = "{}",
        }, default);

        await Observer(h).AutoSyncAppliedAsync(link, Outcome(newItems: 1, logId: logId), true, default);

        var record = h.Notifications.Records.Single();
        Assert.AreEqual(DataSyncNotifier.FirstSyncCase, FakeNotificationService.CaseOf(record));
        Assert.AreEqual("DataSync_Notify_FirstSync_Title(PC-2|94|6|1)", record.Title);
        Assert.AreEqual($"/data-sync?link={link.Id}", FakeNotificationService.RouteOf(record));
        Assert.AreEqual(record.Id, h.Store.Item(suggestion.Id).NotificationId);
    }

    [TestMethod]
    public async Task Follow_overrides_are_announced_at_most_once_a_day()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        var link = h.AddLink("node-pc", l => l.Mode = DataSyncLinkMode.Follow);
        var notes = new[]
        {
            new DataSyncMergeNote(DataSyncKindIds.CustomProperty, "12", "Genre", DataSyncNotifier.FollowOverrideNote,
                new Dictionary<string, string> { ["count"] = "2" }),
            new DataSyncMergeNote(DataSyncKindIds.CustomProperty, "13", "Mood", DataSyncNotifier.FollowOverrideNote, null),
        };

        await Observer(h).AutoSyncAppliedAsync(link, Outcome(notes: notes), false, default);
        await Observer(h).AutoSyncAppliedAsync(link, Outcome(notes: notes), false, default);
        var record = h.Notifications.Records.Single();
        Assert.AreEqual($"DataSync_Notify_FollowOverride_Title({link.PeerName}|3)", record.Title);

        h.Clock.Advance(TimeSpan.FromDays(1));
        await Observer(h).AutoSyncAppliedAsync(link, Outcome(notes: notes), false, default);
        Assert.AreEqual(2, h.Notifications.Records.Count);
    }

    [TestMethod]
    public async Task A_breaker_says_why_and_a_persons_own_pause_says_nothing()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        var link = h.AddLink("node-pc", l => l.PeerName = "PC-2");

        await Observer(h).LinkPausedAsync(link with { State = DataSyncLinkState.Paused,
            PausedReason = DataSyncPauseReason.ByUser }, default);
        Assert.AreEqual(0, h.Notifications.Records.Count);

        await Observer(h).LinkPausedAsync(link with { State = DataSyncLinkState.Paused,
            PausedReason = DataSyncPauseReason.MassDeletion, PausedDetail = "deletions=182;kind=customProperty" }, default);
        var record = h.Notifications.Records.Single();
        Assert.AreEqual(AppNotificationSeverity.Warning, record.Severity);
        Assert.AreEqual("DataSync_Notify_Paused_Title(PC-2|DataSync_Notify_PauseReason_MassDeletion(182))",
            record.Title);
        Assert.AreEqual($"/data-sync?link={link.Id}", FakeNotificationService.RouteOf(record));

        await Observer(h).LinkPausedAsync(link with { State = DataSyncLinkState.Paused,
            PausedReason = DataSyncPauseReason.PeerReset, PausedDetail = DataSyncLinkService.RestoredDetail }, default);
        StringAssert.Contains(h.Notifications.Records.Last().Title, "DataSync_Notify_PauseReason_PeerRestored");
    }

    [TestMethod]
    public async Task A_local_restore_is_announced_once_per_detection_even_across_a_restart()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        var nas = h.AddLink("node-nas");
        var pc = h.AddLink("node-pc");
        h.Store.LocalState = h.Store.LocalState! with
        {
            RestoreReason = DataSyncPauseReason.LocalRestoreDetected,
            RestoreDetectedAtUtc = DateTime.SpecifyKind(h.Clock.UtcNow, DateTimeKind.Unspecified),
        };

        foreach (var link in new[] { nas, pc })
        {
            await Observer(h).LinkPausedAsync(link with { State = DataSyncLinkState.Paused,
                PausedReason = DataSyncPauseReason.LocalRestoreDetected }, default);
        }

        await Observer(h).LocalStateSeenAsync(h.Store.LocalState, default);
        var record = h.Notifications.Records.Single();
        Assert.AreEqual(DataSyncNotifier.Source, record.Source);
        Assert.AreEqual("/data-sync?restore=1", FakeNotificationService.RouteOf(record));
        Assert.AreEqual(AppNotificationSeverity.Warning, record.Severity);

        // A new process with the choice still waiting finds the announcement it already made.
        var restarted = ActivatorUtilities.CreateInstance<DataSyncNotifier>(h.Provider);
        await restarted.LocalStateSeenAsync(h.Store.LocalState, default);
        Assert.AreEqual(1, h.Notifications.Records.Count);

        // A later detection is a new one.
        h.Clock.Advance(TimeSpan.FromHours(3));
        h.Store.LocalState = h.Store.LocalState with { RestoreDetectedAtUtc = h.Clock.UtcNow };
        await restarted.LocalStateSeenAsync(h.Store.LocalState, default);
        Assert.AreEqual(2, h.Notifications.Records.Count);
    }

    [TestMethod]
    public async Task A_review_is_announced_once_and_routes_to_its_link()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        var link = h.AddLink("node-pc", l => l.State = DataSyncLinkState.AwaitingReview);
        var review = new DataSyncReviewEntry("review-7", link.Id, false, new DataSyncStagedPull("node-pc", "PC-2",
            new DataSyncFeedManifest("s", 1, "node-pc", "e", "0123456789abcdef", 1, 1, "2.4.0", [], null,
                new DataSyncSourceAttention(false, 0, 0, false, 0)), [], h.Clock.UtcNow), null, null, null,
            h.Clock.UtcNow, false);

        await Observer(h).ReviewReadyAsync(link, review, default);
        await Observer(h).ReviewReadyAsync(link, review, default);

        var record = h.Notifications.Records.Single();
        Assert.AreEqual(DataSyncNotifier.ReviewReadyCase, FakeNotificationService.CaseOf(record));
        Assert.AreEqual($"/data-sync?link={link.Id}&review=1", FakeNotificationService.RouteOf(record));
    }

    [TestMethod]
    public async Task A_links_review_is_announced_once_however_often_it_is_staged_again()
    {
        // The real review store, and more links awaiting a review than it used to keep (§8.3, §9.4).
        await using var h = await DataSyncApiHarness.CreateAsync(s => s.AddSingleton<IDataSyncReviewStore>(sp =>
            new Bakabase.InsideWorld.Business.Components.DataSync.Apply.DataSyncReviewStore(
                new ClockTime(sp.GetRequiredService<IDataSyncClock>()))));
        var peers = new[] { "node-a", "node-b", "node-c", "node-d" };
        var links = peers.Select(peer => h.AddLink(peer, l =>
        {
            l.State = DataSyncLinkState.AwaitingReview;
            l.FirstContactCompletedAtUtc = null;
            l.FirstContactKindsJson = null;
            l.NextAttemptAtUtc = h.Clock.UtcNow;
        })).ToList();

        for (var cycle = 0; cycle < 3; cycle++)
        {
            await FetchOnceAsync(h);
            h.Clock.Advance(DataSyncSchedule.PollInterval);
        }

        Assert.AreEqual(peers.Length, h.Notifications.Records.Count);
        Assert.IsTrue(h.Notifications.Records.All(r =>
            FakeNotificationService.CaseOf(r) == DataSyncNotifier.ReviewReadyCase));
        CollectionAssert.AreEquivalent(links.Select(l => $"/data-sync?link={l.Id}&review=1").ToArray(),
            h.Notifications.Records.Select(FakeNotificationService.RouteOf).ToArray());

        // Nobody read them for an hour: each idled out and was fetched again — the same question, not announced again.
        h.Clock.Advance(TimeSpan.FromMinutes(61));
        await FetchOnceAsync(h);
        Assert.IsTrue(peers.All(peer => h.Peers.Peers[peer].Manifests == 2));
        Assert.AreEqual(peers.Length, h.Notifications.Records.Count);

        // Nor by a new process while its announcement is unread.
        var restarted = ActivatorUtilities.CreateInstance<DataSyncNotifier>(h.Provider);
        var link = h.Store.Get(links[0].Id)!;
        var review = h.Provider.GetRequiredService<IDataSyncReviewStore>().PeekForLink(link.Id)!;
        await restarted.ReviewReadyAsync(link, review, default);
        Assert.AreEqual(peers.Length, h.Notifications.Records.Count);

        // A link that went on to work and later waits for a review of other kinds is announced again.
        await Observer(h).LinkChangedAsync(link with
        {
            State = DataSyncLinkState.Active, FirstContactCompletedAtUtc = h.Clock.UtcNow,
            FirstContactKindsJson = "[\"extensionGroup\",\"customProperty\"]",
        }, default);
        var added = review.Pull.Kinds.Where(k => k.Kind == DataSyncKindIds.CustomProperty).ToList();
        await Observer(h).ReviewReadyAsync(link, review with { Pull = review.Pull with { Kinds = added } }, default);
        Assert.AreEqual(peers.Length + 1, h.Notifications.Records.Count);
    }

    /// <summary>The runtime's clock as the review store reads time.</summary>
    private sealed class ClockTime(IDataSyncClock clock) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc));
    }

    [TestMethod]
    public async Task A_headless_sources_waiting_decisions_are_announced_once_a_day()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        var link = h.AddLink("node-nas", l =>
        {
            l.PeerName = "NAS";
            l.SetPeerAttention(new DataSyncSourceAttention(true, 2, 1, false, 0));
        });

        await Observer(h).LinkChangedAsync(link, default);
        await Observer(h).LinkChangedAsync(h.Store.Get(link.Id)!, default);

        var record = h.Notifications.Records.Single();
        Assert.AreEqual("DataSync_Notify_Attention_Title(NAS|3)", record.Title);
        Assert.IsNotNull(h.Store.Get(link.Id)!.AttentionNotifiedAtUtc);

        // A desktop source's attention is shown on its own map node, not announced.
        var desktop = h.AddLink("node-pc", l => l.SetPeerAttention(new DataSyncSourceAttention(false, 5, 0, false, 0)));
        await Observer(h).LinkChangedAsync(desktop, default);
        Assert.AreEqual(1, h.Notifications.Records.Count);

        h.Clock.Advance(TimeSpan.FromDays(1));
        await Observer(h).LinkChangedAsync(h.Store.Get(link.Id)!, default);
        Assert.AreEqual(2, h.Notifications.Records.Count);
    }

    private static Task FetchOnceAsync(DataSyncApiHarness h) =>
        h.Provider.GetRequiredService<DataSyncFetcher>().RunCycleAsync(new Bakabase.Abstractions.Components.Tasks.BTaskArgs(
            new Bootstrap.Components.Tasks.PauseTokenSource().Token, CancellationToken.None,
            new Bakabase.Abstractions.Models.Domain.BTask("test-fetch", () => "test"), _ => Task.CompletedTask,
            h.Provider));

    [TestMethod]
    public async Task A_first_link_to_a_source_already_waiting_on_decisions_says_only_that_its_review_is_ready()
    {
        // §9.4: at most one notification per link per cycle. The head's attention and the staged review come in one
        // fetch; the review asks for something, the attention only informs.
        await using var h = await DataSyncApiHarness.CreateAsync();
        var link = h.AddLink("node-nas", l =>
        {
            l.PeerName = "NAS";
            l.State = DataSyncLinkState.AwaitingReview;
            l.FirstContactCompletedAtUtc = null;
            l.FirstContactKindsJson = null;
            l.NextAttemptAtUtc = h.Clock.UtcNow;
        });
        var nas = h.Peers.Peers["node-nas"];
        nas.Attention = new DataSyncSourceAttention(true, 2, 0, false, 0);

        await FetchOnceAsync(h);

        var record = h.Notifications.Records.Single();
        Assert.AreEqual(DataSyncNotifier.ReviewReadyCase, FakeNotificationService.CaseOf(record));
        Assert.IsNull(h.Store.Get(link.Id)!.AttentionNotifiedAtUtc, "not announced, so not claimed for the day");

        // A later cycle with nothing else to say announces what the source waits on.
        h.Clock.Advance(DataSyncSchedule.PollInterval);
        nas.Attention = new DataSyncSourceAttention(true, 3, 0, false, 0);
        await FetchOnceAsync(h);
        Assert.AreEqual(2, h.Notifications.Records.Count);
        Assert.AreEqual("DataSync_Notify_Attention_Title(NAS|3)", h.Notifications.Records.Last().Title);
    }

    [TestMethod]
    public async Task A_cycle_whose_fetch_and_apply_both_only_inform_makes_one_notification()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        var link = h.AddLink("node-nas", l =>
        {
            l.PeerName = "NAS";
            l.Mode = DataSyncLinkMode.Follow;
            l.SetPeerAttention(new DataSyncSourceAttention(true, 2, 0, false, 0));
        });
        var overrides = new[]
        {
            new DataSyncMergeNote(DataSyncKindIds.CustomProperty, "12", "Genre", DataSyncNotifier.FollowOverrideNote,
                null),
        };

        await Observer(h).LinkCycleStartedAsync(link.Id, default);
        await Observer(h).LinkChangedAsync(link, default);
        await Observer(h).LinkFetchEndedAsync(link.Id, true, default);
        Assert.AreEqual(0, h.Notifications.Records.Count, "held until the pull the fetch staged is applied");

        await Observer(h).AutoSyncAppliedAsync(link, Outcome(notes: overrides), false, default);
        var record = h.Notifications.Records.Single();
        Assert.AreEqual(DataSyncNotifier.FollowOverrideCase, FakeNotificationService.CaseOf(record));

        // A cycle that only fetches, with the attention alone: it goes out when the fetch ends.
        h.Clock.Advance(TimeSpan.FromDays(1));
        await Observer(h).LinkCycleStartedAsync(link.Id, default);
        await Observer(h).LinkChangedAsync(link, default);
        Assert.AreEqual(1, h.Notifications.Records.Count);
        await Observer(h).LinkFetchEndedAsync(link.Id, false, default);
        Assert.AreEqual(DataSyncNotifier.AttentionCase, FakeNotificationService.CaseOf(h.Notifications.Records.Last()));
    }

    [TestMethod]
    public async Task A_device_that_started_reading_on_its_own_is_announced_once()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        h.AddLink("node-linked");
        h.Notifier.NoteApproved("node-approved");
        foreach (var node in new[] { "node-code", "node-linked", "node-approved" })
        {
            h.Store.Readers.Add(new DataSyncReaderDbModel
            {
                NodeId = node, Name = node.ToUpperInvariant(), FirstReadAtUtc = h.Clock.UtcNow,
                LastReadAtUtc = h.Clock.UtcNow, Mode = "follow",
            });
        }

        h.Store.Readers.Add(new DataSyncReaderDbModel
        {
            NodeId = "node-quiet", Name = "Quiet", FirstReadAtUtc = h.Clock.UtcNow, LastReadAtUtc = h.Clock.UtcNow,
        });

        await Observer(h).LocalStateSeenAsync(h.Store.LocalState, default);
        await Observer(h).LocalStateSeenAsync(h.Store.LocalState, default);

        var record = h.Notifications.Records.Single();
        Assert.AreEqual("DataSync_Notify_NewReader_Title(NODE-CODE)", record.Title);
        Assert.AreEqual(DataSyncNotifier.Source, record.Source);
        Assert.IsTrue(h.Store.Readers.Where(r => r.Mode is not null).All(r => r.NotifiedAtUtc is not null));
        Assert.IsNull(h.Store.Readers.Single(r => r.NodeId == "node-quiet").NotifiedAtUtc,
            "a reader that declared no mode has not started syncing");
    }
}
