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
/// Data sync's notifications (§9.4), through the runtime's observer: desktop only, the sources and routes, the hourly
/// and daily limits, one per restore detection and per first sync ready, and read state that follows the items it
/// announced.
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
            PausedReason = DataSyncPauseReason.PeerIdentityDuplicated }, default);

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
        // One item: the title's form for one ("1 change needs you").
        Assert.AreEqual("DataSync_Notify_NeedsYou_Title_One(PC-2|1)", announced.Title);
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
    public async Task Only_an_unread_decision_prompt_holds_new_decisions_back()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        var link = h.AddLink("node-pc");
        await Observer(h).LinkPausedAsync(link with { State = DataSyncLinkState.Paused,
            PausedReason = DataSyncPauseReason.PeerIdentityDuplicated }, default);
        var item = h.AddItem(DataSyncInboxItemType.FieldConflict, link.Id, Key(1));

        // An unread pause announces no decision: the item gets its own announcement at once, never the pause's.
        await Observer(h).AutoSyncAppliedAsync(link, Outcome(newItems: 1), false, default);
        Assert.AreEqual(2, h.Notifications.Records.Count);
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
        var logId = h.Store.AddHistory(new DataSyncApplyLogDbModel
        {
            Kind = DataSyncHistoryKind.AutoSync, AppliedAtUtc = h.Clock.UtcNow,
            SummaryJson = DataSyncHistoryJson.WriteSummary(new DataSyncHistoryCounts(94, 0, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0)),
            ResultJson = "{}", PreImageJson = "{}",
        });

        await Observer(h).AutoSyncAppliedAsync(link, Outcome(newItems: 1, logId: logId), true, default);

        var record = h.Notifications.Records.Single();
        Assert.AreEqual(DataSyncNotifier.FirstSyncCase, FakeNotificationService.CaseOf(record));
        Assert.AreEqual("DataSync_Notify_FirstSync_Title_One(PC-2|94|6|1)", record.Title);
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
            PausedReason = DataSyncPauseReason.PeerIdentityDuplicated }, default);
        var record = h.Notifications.Records.Single();
        Assert.AreEqual(AppNotificationSeverity.Warning, record.Severity);
        Assert.AreEqual("DataSync_Notify_Paused_Title(PC-2|DataSync_Notify_PauseReason_PeerIdentityDuplicated)",
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
    public async Task A_first_sync_is_announced_once_and_routes_to_its_link()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        var link = h.AddLink("node-pc", l => l.State = DataSyncLinkState.AwaitingReview);

        await Observer(h).ReviewReadyAsync(link, default);
        await Observer(h).ReviewReadyAsync(link, default);

        var record = h.Notifications.Records.Single();
        Assert.AreEqual(DataSyncNotifier.ReviewReadyCase, FakeNotificationService.CaseOf(record));
        Assert.AreEqual($"/data-sync?link={link.Id}&review=1", FakeNotificationService.RouteOf(record));
    }

    private static Task FetchOnceAsync(DataSyncApiHarness h) =>
        h.Provider.GetRequiredService<DataSyncFetcher>().RunCycleAsync(new Bakabase.Abstractions.Components.Tasks.BTaskArgs(
            new Bootstrap.Components.Tasks.PauseTokenSource().Token, CancellationToken.None,
            new Bakabase.Abstractions.Models.Domain.BTask("test-fetch", () => "test"), _ => Task.CompletedTask,
            h.Provider));

    [TestMethod]
    public async Task Each_links_first_sync_is_announced_once_and_again_only_after_it_was_applied()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        var peers = new[] { "node-a", "node-b", "node-c", "node-d" };
        var links = peers.Select(peer => h.AddLink(peer, l =>
        {
            l.State = DataSyncLinkState.AwaitingReview;
            l.FirstContactCompletedAtUtc = null;
        })).ToList();

        for (var cycle = 0; cycle < 3; cycle++)
        {
            await FetchOnceAsync(h);
            h.Clock.Advance(DataSyncSchedule.PollInterval);
        }

        Assert.IsTrue(peers.All(peer => h.Peers.Peers[peer].Manifests == 1), "one snapshot each, kept for the Start");
        Assert.IsTrue(h.Notifications.Records.All(r =>
            FakeNotificationService.CaseOf(r) == DataSyncNotifier.ReviewReadyCase));
        CollectionAssert.AreEquivalent(links.Select(l => $"/data-sync?link={l.Id}&review=1").ToArray(),
            h.Notifications.Records.Select(FakeNotificationService.RouteOf).ToArray());

        // A new process stages it again: the same question, not announced while its announcement is unread.
        var restarted = ActivatorUtilities.CreateInstance<DataSyncNotifier>(h.Provider);
        var link = h.Store.Get(links[0].Id)!;
        await restarted.ReviewReadyAsync(link, default);
        Assert.AreEqual(peers.Length, h.Notifications.Records.Count);

        // Read, then applied (the runner moves the link on in its own transaction), then a copy once onto the stopped
        // row waits for its Start: announced again.
        await h.Notifications.MarkAsReadAsync(h.Notifications.Records.Select(r => r.Id).ToArray());
        await Observer(h).AutoSyncAppliedAsync(link with { State = DataSyncLinkState.Stopped },
            new DataSyncAutoSyncOutcome(7, null, 0, 0, 1, [], [], DataSyncAutoSyncEnd.Committed, true), true, default);
        await Observer(h).ReviewReadyAsync(link, default);
        Assert.AreEqual(peers.Length + 1, h.Notifications.Records.Count(r =>
            FakeNotificationService.CaseOf(r) == DataSyncNotifier.ReviewReadyCase));
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

        // A desktop source's attention is shown on its own map node, not announced.
        var desktop = h.AddLink("node-pc", l => l.SetPeerAttention(new DataSyncSourceAttention(false, 5, 0, false, 0)));
        await Observer(h).LinkChangedAsync(desktop, default);
        Assert.AreEqual(1, h.Notifications.Records.Count);

        h.Clock.Advance(TimeSpan.FromDays(1));
        await Observer(h).LinkChangedAsync(h.Store.Get(link.Id)!, default);
        Assert.AreEqual(2, h.Notifications.Records.Count);
    }

    [TestMethod]
    public async Task A_device_that_started_reading_on_its_own_is_announced_once()
    {
        await using var h = await DataSyncApiHarness.CreateAsync();
        h.AddLink("node-linked");
        h.Notifier.NoteApproved("node-approved");
        foreach (var node in new[] { "node-code", "node-linked", "node-approved" })
        {
            h.Store.AddReader(new DataSyncReaderDbModel
            {
                NodeId = node, Name = node.ToUpperInvariant(), FirstReadAtUtc = h.Clock.UtcNow,
                LastReadAtUtc = h.Clock.UtcNow, Mode = "follow",
            });
        }

        h.Store.AddReader(new DataSyncReaderDbModel
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
