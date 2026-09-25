using System.Collections;
using System.Reflection;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync.Api;

/// <summary>
/// The canned data the page is built against covers every state it has to draw (spec §14.1 E0), and keeps the rules
/// the real service keeps: every time is UTC (§2.10).
/// </summary>
[TestClass]
public class FakeDataSyncServiceTests
{
    /// <summary>§9.1: merger-derived items belong to one link; state-derived ones are tied to local state.</summary>
    private static readonly DataSyncInboxItemType[] StateDerived =
    [
        DataSyncInboxItemType.ChildDeletedInUse, DataSyncInboxItemType.MassChildDeletion,
        DataSyncInboxItemType.SuspectedLostUpdate, DataSyncInboxItemType.LargeChange,
    ];

    [TestMethod]
    public async Task The_inbox_has_every_item_type_with_its_origin()
    {
        var inbox = (await new FakeDataSyncService().GetInboxAsync(new DataSyncInboxQuery(OpenOnly: false), default))
            .Items;

        CollectionAssert.AreEquivalent(Enum.GetValues<DataSyncInboxItemType>(),
            inbox.Where(i => i.ClosedAt == null).Select(i => i.Type).Distinct().ToArray());
        foreach (var item in inbox)
        {
            var origin = StateDerived.Contains(item.Type) ? DataSyncInboxItemOrigin.State : DataSyncInboxItemOrigin.Merger;
            Assert.AreEqual(origin, item.Origin, $"item {item.Id} ({item.Type})");
            CollectionAssert.AllItemsAreUnique(item.AllowedActions.ToArray(), $"item {item.Id}");
            Assert.IsTrue(item.AllowedActions.Count > 0, $"item {item.Id}");
        }

        // Both identity conflict rows (one record, two entities; two records, one entity), an item of no link, and
        // one closed on another device.
        var identity = inbox.Where(i => i.Type == DataSyncInboxItemType.IdentityConflict).ToList();
        Assert.IsTrue(identity.Any(i => i.Payload.Candidates?.Count > 1));
        Assert.IsTrue(identity.Any(i => i.Payload.Records?.Count > 1));
        Assert.IsTrue(inbox.Any(i => i.LinkId == null && i.Type == DataSyncInboxItemType.SuspectedLostUpdate));
        Assert.IsTrue(inbox.Any(i => i.Closure == DataSyncInboxClosure.ResolvedElsewhere));

        var openOnly = await new FakeDataSyncService().GetInboxAsync(new DataSyncInboxQuery(), default);
        Assert.IsTrue(openOnly.Items.All(i => i.ClosedAt == null));
        Assert.AreEqual(openOnly.OpenTotal, openOnly.Total);
    }

    [TestMethod]
    public async Task The_links_have_every_state_and_every_kind_of_attention()
    {
        var fake = new FakeDataSyncService();
        var links = await fake.GetLinksAsync(default);

        CollectionAssert.AreEquivalent(Enum.GetValues<DataSyncLinkState>(),
            links.Select(l => l.State).Distinct().ToArray());
        CollectionAssert.AllItemsAreUnique(links.Select(l => l.Id).ToArray());

        var attention = links.Select(l => l.PeerAttention).ToList();
        Assert.IsTrue(attention.Any(a => a == null), "a source that reports none");
        Assert.IsTrue(attention.Any(a => a is {Headless: true, OpenDecisions: > 0}), "decisions waiting at a hub");
        Assert.IsTrue(attention.Any(a => a is {PausedLinks: > 0}), "paused links there");
        Assert.IsTrue(attention.Any(a => a is {RestorePending: true}), "a restore waiting there");
        Assert.IsTrue(attention.Any(a => a is {AwaitingReview: > 0}), "a review waiting there");

        // The map draws the same links, plus this device's own requests that ended.
        var map = await fake.GetMapAsync(default);
        Assert.AreEqual(links.Count, map.Peers.Count);
        CollectionAssert.AreEquivalent(new[] {"awaitingApproval", "rejected", "expired"},
            map.Outgoing.Select(o => o.Outcome).ToArray());
        Assert.IsTrue(map.Requests.Any(r => r.ClaimsKnownDevice));
    }

    [TestMethod]
    public async Task The_review_has_every_plan_item_type()
    {
        var review = await new FakeDataSyncService().GetReviewAsync(FakeDataSyncService.ReviewId, default);

        Assert.IsNull(review.Problem);
        Assert.AreEqual(DataSyncReviewState.Staged, review.State);
        CollectionAssert.AreEquivalent(Enum.GetValues<DataSyncPlanItemType>(),
            review.Plan!.Kinds.SelectMany(k => k.Items).Select(i => i.Type).Distinct().ToArray());
        Assert.AreEqual(DataSyncProblemCode.ReviewExpired,
            (await new FakeDataSyncService().GetReviewAsync("gone", default)).Problem?.Code);
    }

    [TestMethod]
    public async Task Every_time_it_answers_is_utc()
    {
        var fake = new FakeDataSyncService();
        object?[] answers =
        [
            await fake.GetOverviewAsync(default),
            await fake.GetMapAsync(default),
            await fake.GetPeersAsync(true, default),
            await fake.GetLinksAsync(default),
            await fake.GetRequestsAsync(default),
            await fake.GetReadersAsync(default),
            await fake.CreateInvitationAsync(new DataSyncInvitationInput(true), default),
            await fake.GetInboxAsync(new DataSyncInboxQuery(OpenOnly: false), default),
            await fake.GetReviewAsync(FakeDataSyncService.ReviewId, default),
            await fake.GetEntitiesAsync(Bakabase.Modules.DataSync.Abstractions.DataSyncKindIds.CustomProperty, default),
            await fake.GetHistoryAsync(default),
            await fake.GetHistoryEntryAsync(1, default),
            await fake.GetRestoreAsync(default),
        ];

        var times = new List<(string Path, DateTime Value)>();
        foreach (var answer in answers)
        {
            CollectTimes(answer, answer?.GetType().Name ?? "null", times);
        }

        Assert.IsTrue(times.Count > 50, $"only {times.Count} times checked");
        var local = times.Where(t => t.Value.Kind != DateTimeKind.Utc).Select(t => t.Path).ToArray();
        Assert.AreEqual(0, local.Length, string.Join(Environment.NewLine, local));
    }

    private static void CollectTimes(object? value, string path, List<(string, DateTime)> times)
    {
        switch (value)
        {
            case null or string:
                return;
            case DateTime time:
                times.Add((path, time));
                return;
            case IEnumerable items:
            {
                var index = 0;
                foreach (var item in items)
                {
                    CollectTimes(item, $"{path}[{index++}]", times);
                }

                return;
            }
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type.Namespace?.StartsWith("Bakabase", StringComparison.Ordinal) != true)
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length == 0)
            {
                CollectTimes(property.GetValue(value), $"{path}.{property.Name}", times);
            }
        }
    }
}
