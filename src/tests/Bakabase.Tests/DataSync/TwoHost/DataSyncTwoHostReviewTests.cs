using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.StandardValue.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests.DataSync.TwoHost;

/// <summary>
/// The first-link review over two real hosts with the real planner (§8.3, §10.1): the read-only re-plan, its change
/// pages, the rename rows' reach (v3.1 M9), the apply's strict validation and a two-way approval whose read-back
/// fails (§7.2.4, N14).
/// </summary>
[TestClass]
public class DataSyncTwoHostReviewTests
{
    private const string Rock = "00000000-0000-4000-8000-00000000a001";
    private const string Jazz = "00000000-0000-4000-8000-00000000a002";

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
        await _a.InScopeAsync(sp => sp.GetRequiredService<ICustomPropertyService>().Add(Genre("Rock")));
    }

    [TestMethod]
    public async Task A_review_shows_how_far_a_rename_reaches_and_applies_only_decisions_checked_against_it()
    {
        // B's first sync with A creates Genre with A's option ids; B's resources then use Rock.
        var reviewId = await PairTwoWayAsync();
        await ApplyAsync(reviewId, []);
        var genre = (await PropertiesAsync(_b)).Single();
        await _b.InScopeAsync(sp => sp.GetRequiredService<ICustomPropertyValueService>().AddDbModelRange(
            [Value(genre.Id, 1, Rock), Value(genre.Id, 2, Rock), Value(genre.Id, 3, Jazz)]));

        // B starts over with A (Dismiss, §8.1), while A renames Rock: B's next review is a first contact again, and
        // finds Genre by key.
        var link = await _b.RequireLinkToAsync(_a);
        Assert.IsNull(await _b.CallAsync(s => s.ResetLinkAsync(link.Id, default)));
        await _a.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<ICustomPropertyService>();
            var property = (await service.GetAll()).Single();
            await service.Put(property.Id, Genre("Rock & Roll"));
        });
        var created = await _b.CallAsync(s => s.CreateLinkAsync(
            new DataSyncLinkCreateInput(_a.NodeId, null, null, DataSyncLinkMode.TwoWay, []), true, default));
        Assert.IsNull(created.Problem, created.Problem?.Code.ToString());
        Assert.AreEqual(DataSyncLinkState.AwaitingReview, created.Link!.State, "B already reads A");
        // A reader gets one snapshot per 10 s (§7.5.2).
        _clock.Advance(Bakabase.InsideWorld.Business.Components.DataSync.Feed.DataSyncFeedSnapshots.ManifestInterval);
        await _b.CycleAsync();
        var relinked = await _b.RequireLinkToAsync(_a);
        Assert.IsNotNull(relinked.ReviewId, $"{relinked.State} {relinked.LastErrorCode} {relinked.LastErrorDetail}");
        reviewId = relinked.ReviewId!;

        // The read-only re-plan (§8.3 step 4): an update of the key-bound Genre, whose rename row says how many
        // resources show the option here.
        var review = await _b.CallAsync(s => s.GetReviewAsync(reviewId, default));
        Assert.IsNull(review.Problem, review.Problem?.Code.ToString());
        var item = review.Plan!.Kinds.Single(k => k.Kind == DataSyncKindIds.CustomProperty).Items.Single();
        Assert.AreEqual((DataSyncPlanItemType.Update, genre.Id.ToString()), (item.Type, item.Local?.LocalKey));
        var rename = item.Changes.Single(c => c.Kind == DataSyncFieldChangeKind.RenameChild);
        Assert.AreEqual(("Rock", "Rock & Roll", (int?) 2), (rename.From?.Text, rename.To?.Text, rename.InUseCount));

        // The change page of the same plan carries it too; a page of another plan is refused.
        var page = await _b.CallAsync(s => s.GetReviewChangesAsync(reviewId, review.Plan.PlanId, item.ItemId, null, 0,
            50, default));
        Assert.IsNull(page.Problem, page.Problem?.Code.ToString());
        Assert.AreEqual(2, page.Changes.Single(c => c.ChangeId == rename.ChangeId).InUseCount);
        var stale = await _b.CallAsync(s => s.GetReviewChangesAsync(reviewId, "0000000000000000", item.ItemId, null, 0,
            50, default));
        Assert.AreEqual(DataSyncProblemCode.PlanChanged, stale.Problem?.Code);

        // The apply validates strictly against a fresh plan: a decision with a token the person never saw is refused
        // with the plan, and nothing is enqueued.
        var forged = new DataSyncPlanDecision(item.ItemId, DataSyncPlanResolution.Update, item.DefaultTargetLocalKey,
            null, [], "forged");
        var refused = await _b.CallAsync(s => s.ApplyReviewAsync(reviewId, new DataSyncReviewApplyInput([forged], false),
            default));
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid, refused.Problem?.Code);
        Assert.IsTrue(refused.DecisionErrors.Count > 0);
        Assert.IsNotNull(refused.Plan);
        Assert.IsNull(refused.TaskId);

        await ApplyAsync(reviewId, [new DataSyncPlanDecision(item.ItemId, DataSyncPlanResolution.Update,
            item.DefaultTargetLocalKey, null, [], item.ReviewToken)]);
        var labels = Choices((await PropertiesAsync(_b)).Single()).ToDictionary(c => c.Value, c => c.Label);
        Assert.AreEqual(("Rock & Roll", "Jazz"), (labels[Rock], labels[Jazz]), "renamed in place, values kept");
        Assert.AreEqual(DataSyncLinkState.Active, (await _b.RequireLinkToAsync(_a)).State);
    }

    [TestMethod]
    public async Task A_two_way_approval_whose_read_back_fails_leaves_the_approvers_link_saying_why()
    {
        _network.FailNextReadBack = nameof(DataSyncPeerErrorCode.Unreachable);
        var created = await _b.CallAsync(s => s.CreateLinkAsync(
            new DataSyncLinkCreateInput(_a.NodeId, null, null, DataSyncLinkMode.TwoWay, []), true, default));
        Assert.IsNull(created.Problem);
        var request = (await _a.CallAsync(s => s.GetRequestsAsync(default)))
            .Single(r => r.Direction == DataSyncRequestDirection.Incoming);

        var approved = await _a.CallAsync(s => s.ApproveRequestAsync(request.RequestId,
            new DataSyncApproveInput(true, null), default));
        Assert.IsFalse(approved.ReadBackGranted);
        // The approval's outcome made the link; the pairing flow's events (InboundGranted, then ReadBackFailed), drained
        // on the next tick, leave it waiting with the reason.
        await _a.TickAsync();

        var link = await _a.RequireLinkToAsync(_b);
        Assert.AreEqual((DataSyncLinkState.AwaitingAccess, DataSyncLinkInitiator.Peer), (link.State, link.Initiator));
        Assert.AreEqual(("ReadBackFailed", nameof(DataSyncPeerErrorCode.Unreachable)),
            (link.LastErrorCode, link.LastErrorDetail));
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

    private async Task ApplyAsync(string reviewId, IReadOnlyList<DataSyncPlanDecision> decisions)
    {
        var start = await _b.CallAsync(s => s.ApplyReviewAsync(reviewId, new DataSyncReviewApplyInput(decisions, false),
            default));
        Assert.IsNull(start.Problem, $"{start.Problem?.Code} {string.Join(", ", start.DecisionErrors)}");
        await _b.RunWriteTasksAsync();
        Assert.IsNotNull((await _b.CallAsync(s => s.GetReviewAsync(reviewId, default))).ApplyLogId, "the review applied");
    }

    private static DataSyncDevice Device(string name) =>
        new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), name);

    private static CustomPropertyAddOrPutDto Genre(string rock) => new()
    {
        Name = "Genre",
        Type = PropertyType.MultipleChoice,
        Options = JsonConvert.SerializeObject(new MultipleChoicePropertyOptions
        {
            Choices = [new ChoiceOptions {Value = Rock, Label = rock}, new ChoiceOptions {Value = Jazz, Label = "Jazz"}],
        }),
    };

    private static CustomPropertyValueDbModel Value(int propertyId, int resourceId, string option) => new()
    {
        ResourceId = resourceId, PropertyId = propertyId, Scope = (int) PropertyValueScope.Manual,
        Value = new List<string> {option}.SerializeAsStandardValue(StandardValueType.ListString),
    };

    private static Task<List<Bakabase.Abstractions.Models.Domain.CustomProperty>> PropertiesAsync(TwoHostNode node) =>
        node.InScopeAsync(sp => sp.GetRequiredService<ICustomPropertyService>().GetAll());

    private static List<ChoiceOptions> Choices(Bakabase.Abstractions.Models.Domain.CustomProperty property) =>
        JsonConvert.DeserializeObject<MultipleChoicePropertyOptions>(JsonConvert.SerializeObject(property.Options))!
            .Choices ?? [];
}
