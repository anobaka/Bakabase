using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests.DataSync.TwoHost;

/// <summary>
/// The lost-update guard over two real hosts (§6.5): an edit dialog on B opened before a sync apply saves its stale
/// options afterwards, while A — B's only reader — is offline for longer than the guard's window. B holds the stale
/// write, and A keeps the option the stale write took away: the silent revert on every device the guard exists to
/// prevent.
/// </summary>
[TestClass]
public class DataSyncTwoHostLostUpdateTests
{
    private const string Rock = "00000000-0000-4000-8000-00000000b001";
    private const string Pop = "00000000-0000-4000-8000-00000000b002";
    private const string SciFi = "00000000-0000-4000-8000-00000000b003";
    private const string Horror = "00000000-0000-4000-8000-00000000b004";

    private TwoHostClock _clock = null!;
    private TwoHostNetwork _network = null!;
    private TwoHostNode _a = null!;
    private TwoHostNode _b = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _clock = new TwoHostClock(DateTime.UtcNow.AddTicks(-(DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond)));
        _network = new TwoHostNetwork(_clock);
        _a = await TwoHostNode.StartAsync(_network, Device("PC-A"));
        _b = await TwoHostNode.StartAsync(_network, Device("PC-B"));
        await _a.InScopeAsync(sp => sp.GetRequiredService<ICustomPropertyService>().Add(Genre((Rock, "Rock"), (Pop, "Pop"))));
    }

    /// <param name="bKeepsCycling">
    /// B's own loop keeps ticking meanwhile (the scheduler refreshes once the window has closed), or nothing on B runs
    /// until A reads it again (the Refresh A's head asks for meets the stale write half an hour late).
    /// </param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    [Timeout(60_000)]
    public async Task A_stale_write_no_reader_sees_within_the_window_is_held_and_never_reverts_the_peer(bool bKeepsCycling)
    {
        // B keeps in step with A both ways; A reads B back.
        var reviewId = await PairTwoWayAsync();
        await ApplyReviewAsync(reviewId);
        await PullAsync(_a);
        Assert.AreEqual(DataSyncLinkState.Active, (await _a.RequireLinkToAsync(_b)).State);

        // B's edit dialog opens: it reads Genre as it is now.
        var stale = (await PropertiesAsync(_b)).Single(p => p.Name == "Genre");

        // A adds Sci-fi; B pulls it and applies it.
        await _a.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<ICustomPropertyService>();
            var genre = (await service.GetAll()).Single(p => p.Name == "Genre");
            await service.Put(genre.Id, Genre((Rock, "Rock"), (Pop, "Pop"), (SciFi, "Sci-fi")));
        });
        await PullAsync(_b);
        CollectionAssert.AreEqual(new[] { "Rock", "Pop", "Sci-fi" }, Labels(await GenreAsync(_b)), "B applied Sci-fi");

        // Two minutes later the dialog saves what it read, plus Horror: Sci-fi is gone again on B.
        _clock.Advance(TimeSpan.FromMinutes(2));
        await _b.InScopeAsync(sp => sp.GetRequiredService<ICustomPropertyService>()
            .Put(stale.Id, Genre((Rock, "Rock"), (Pop, "Pop"), (Horror, "Horror"))));

        // A is offline both ways for longer than the window.
        _a.Client.SetReachable(_b.NodeId, false);
        _b.Client.SetReachable(_a.NodeId, false);
        if (bKeepsCycling)
        {
            for (var i = 0; i < 12; i++) await PullAsync(_b);
            Assert.AreEqual(DataSyncInboxItemType.SuspectedLostUpdate, (await _b.ItemsAsync()).Single().Type,
                "B's own loop met the stale write once the window had closed, with no reader asking");
        }
        else
        {
            _clock.Advance(TimeSpan.FromMinutes(30));
        }

        // A is back and pulls B: B's Refresh (the one A's head asks for, if B's loop did not run) holds the stale
        // write, so A keeps its version.
        _a.Client.SetReachable(_b.NodeId, true);
        _b.Client.SetReachable(_a.NodeId, true);
        await PullAsync(_a);

        var item = (await _b.ItemsAsync()).Single();
        Assert.AreEqual(DataSyncInboxItemType.SuspectedLostUpdate, item.Type);
        var row = (await _b.EntitiesAsync()).Single(e => e.Kind == DataSyncKindIds.CustomProperty &&
                                                        e.LocalKey == stale.Id.ToString());
        Assert.IsTrue(row.PublishHeld, "held, not published as a newer local revision");
        CollectionAssert.AreEqual(new[] { "Rock", "Pop", "Sci-fi" }, Labels(await GenreAsync(_a)),
            "A keeps Sci-fi: the stale write reverted nothing there");
        Assert.AreEqual(DataSyncLinkState.Active, (await _a.RequireLinkToAsync(_b)).State);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    /// <summary>B asks A to keep in step both ways, A approves reading B back, and B's review is staged (§8.3).</summary>
    private async Task<string> PairTwoWayAsync()
    {
        var created = await _b.CallAsync(s => s.CreateLinkAsync(
            new DataSyncLinkCreateInput(_a.NodeId, null, null, DataSyncLinkMode.TwoWay, []), true, default));
        Assert.IsNull(created.Problem, created.Problem?.Code.ToString());
        var request = (await _a.CallAsync(s => s.GetRequestsAsync(default)))
            .Single(r => r.Direction == DataSyncRequestDirection.Incoming);
        var approved = await _a.CallAsync(s => s.ApproveRequestAsync(request.RequestId,
            new DataSyncApproveInput(true, null), default));
        Assert.IsNull(approved.Problem, approved.Problem?.Code.ToString());
        _clock.Advance(TimeSpan.FromSeconds(5));
        _network.Claim(_b.NodeId);
        await _b.CycleAsync();
        return (await _b.RequireLinkToAsync(_a)).ReviewId ?? throw new AssertFailedException("No review was staged.");
    }

    /// <summary>B applies its first review with the planner's defaults: A's Genre is created on B.</summary>
    private async Task ApplyReviewAsync(string reviewId)
    {
        var start = await _b.CallAsync(s => s.ApplyReviewAsync(reviewId, new DataSyncReviewApplyInput([], false),
            default));
        Assert.IsNull(start.Problem, $"{start.Problem?.Code} {string.Join(", ", start.DecisionErrors)}");
        await _b.RunWriteTasksAsync();
        CollectionAssert.AreEqual(new[] { "Rock", "Pop" }, Labels(await GenreAsync(_b)));
    }

    /// <summary>A pull by <paramref name="node"/>: its link is due again a minute later (§8.2).</summary>
    private async Task PullAsync(TwoHostNode node)
    {
        _clock.Advance(TimeSpan.FromSeconds(61));
        await node.CycleAsync();
    }

    private static DataSyncDevice Device(string name) =>
        new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), name);

    private static CustomPropertyAddOrPutDto Genre(params (string Id, string Label)[] choices) => new()
    {
        Name = "Genre",
        Type = PropertyType.MultipleChoice,
        Options = JsonConvert.SerializeObject(new MultipleChoicePropertyOptions
        {
            Choices = choices.Select(c => new ChoiceOptions { Value = c.Id, Label = c.Label }).ToList(),
        }),
    };

    private static Task<List<Bakabase.Abstractions.Models.Domain.CustomProperty>> PropertiesAsync(TwoHostNode node) =>
        node.InScopeAsync(sp => sp.GetRequiredService<ICustomPropertyService>().GetAll());

    private static async Task<Bakabase.Abstractions.Models.Domain.CustomProperty> GenreAsync(TwoHostNode node) =>
        (await PropertiesAsync(node)).Single(p => p.Name == "Genre");

    private static string[] Labels(Bakabase.Abstractions.Models.Domain.CustomProperty property) =>
        (JsonConvert.DeserializeObject<MultipleChoicePropertyOptions>(JsonConvert.SerializeObject(property.Options))!
            .Choices ?? []).Select(c => c.Label).ToArray();
}
