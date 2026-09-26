using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Service.Controllers;
using Bakabase.Tests.Federation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    public async Task Fetch_again_keeps_the_review_while_the_peer_cannot_be_read_and_replaces_it_once_it_can()
    {
        var reviewId = await PairTwoWayAsync();

        // A is offline: the person keeps the review they had open, told why it was not refreshed (§8.3).
        _b.Client.SetReachable(_a.NodeId, false);
        var failed = await _b.CallAsync(s => s.RefetchReviewAsync(reviewId, default));
        Assert.AreEqual(DataSyncProblemCode.PeerUnreachable, failed.Problem?.Code);
        Assert.AreEqual(reviewId, failed.ReviewId);
        Assert.IsNotNull(failed.Plan);
        Assert.IsNull((await _b.CallAsync(s => s.GetReviewAsync(reviewId, default))).Problem, "still staged");
        Assert.AreEqual(reviewId, (await _b.RequireLinkToAsync(_a)).ReviewId);

        // Back online: the fresh review replaces it.
        _b.Client.SetReachable(_a.NodeId, true);
        _clock.Advance(Bakabase.InsideWorld.Business.Components.DataSync.Feed.DataSyncFeedSnapshots.ManifestInterval);
        var fresh = await _b.CallAsync(s => s.RefetchReviewAsync(reviewId, default));
        Assert.IsNull(fresh.Problem, fresh.Problem?.Code.ToString());
        Assert.AreNotEqual(reviewId, fresh.ReviewId);
        Assert.AreEqual(fresh.ReviewId, (await _b.RequireLinkToAsync(_a)).ReviewId);
        Assert.AreEqual(DataSyncProblemCode.ReviewExpired,
            (await _b.CallAsync(s => s.GetReviewAsync(reviewId, default))).Problem?.Code);
    }

    [TestMethod]
    public async Task A_two_way_request_approved_without_reading_back_says_so_until_the_approver_reads_back()
    {
        var created = await _b.CallAsync(s => s.CreateLinkAsync(
            new DataSyncLinkCreateInput(_a.NodeId, null, null, DataSyncLinkMode.TwoWay, []), true, default));
        Assert.IsNull(created.Problem);
        var request = (await _a.CallAsync(s => s.GetRequestsAsync(default)))
            .Single(r => r.Direction == DataSyncRequestDirection.Incoming);
        Assert.IsNull((await _a.CallAsync(s => s.ApproveRequestAsync(request.RequestId,
            new DataSyncApproveInput(false, null), default))).Problem);

        // B's claim hears that A does not read it back (§7.2.4 step 7): its link says so.
        _network.Claim(_b.NodeId);
        await _b.TickAsync();
        var link = await _b.RequireLinkToAsync(_a);
        Assert.AreEqual((DataSyncLinkState.AwaitingReview, true), (link.State, link.ReadBackDeclined));
        Assert.IsNull(await _a.LinkToAsync(_b), "A makes no link of its own without reading B back");

        // [Ask A to keep in step]: the note stays while that request waits, and goes once A reads B back.
        Assert.IsNull((await _b.CallAsync(s => s.ResumeLinkAsync(link.Id, DataSyncResumeAction.AskAccessAgain,
            default))).Problem);
        Assert.IsTrue((await _b.RequireLinkToAsync(_a)).ReadBackDeclined);
        var again = (await _a.CallAsync(s => s.GetRequestsAsync(default)))
            .Single(r => r.Direction == DataSyncRequestDirection.Incoming && r.Status == "pending");
        Assert.AreEqual(DataSyncRequestIntent.TwoWay, again.Intent);
        Assert.IsNull((await _a.CallAsync(s => s.ApproveRequestAsync(again.RequestId,
            new DataSyncApproveInput(true, null), default))).Problem);
        _network.Claim(_b.NodeId);
        await _b.TickAsync();
        Assert.IsFalse((await _b.RequireLinkToAsync(_a)).ReadBackDeclined);
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

    [TestMethod]
    public async Task Removing_the_other_device_resets_the_link_to_it_and_the_map_forgets_it()
    {
        // B keeps in step with A both ways: its link is working, and A reads B.
        await ApplyAsync(await PairTwoWayAsync(), []);
        Assert.AreEqual(DataSyncLinkState.Active, (await _b.RequireLinkToAsync(_a)).State);
        Assert.IsTrue((await _b.CallAsync(s => s.GetMapAsync(default))).Peers.Any(p => p.NodeId == _a.NodeId));

        // "Remove device" on B (DELETE /federation/local/peers/{A}). RemovePeerAsync ends access both ways — in the
        // Service, the same federation state data sync reads its grants from; here, the network's grants.
        var grants = _b.Services.GetRequiredService<IDataSyncGrantService>();
        await grants.RevokeAsync(_a.NodeId, default);
        await grants.ForgetOutboundAsync(_a.NodeId, default);
        using var directory = new FederationBrowsingControlTests.StateDirectory();
        var store = new FederationStateStore(directory, directory);
        using var leases = new GrantLeaseRegistry();
        var peers = new FederationPeerService(store, new NodeIdentityProvider(store), leases, TimeProvider.System);
        await using (var scope = _b.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope())
        {
            var controller = new FederationPeerController(peers, null!, null!, null!, null!, null!, null!, null!, null!,
                null!, TimeProvider.System, null!, store)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider },
                },
            };
            await controller.Remove(_a.NodeId, default);
        }

        // B's link is gone, as Reset leaves it: nothing blames A for no longer sharing, and the map draws nothing
        // for A — no line, no request of B's own that ended.
        Assert.AreEqual(0, (await _b.CallAsync(s => s.GetLinksAsync(default))).Count);
        var map = await _b.CallAsync(s => s.GetMapAsync(default));
        Assert.IsFalse(map.Peers.Any(p => p.NodeId == _a.NodeId), string.Join(", ", map.Peers.Select(p => p.NodeId)));
        Assert.IsFalse(map.Outgoing.Any(o => o.NodeId == _a.NodeId));
        // Every definition stays.
        Assert.AreEqual("Genre", (await PropertiesAsync(_b)).Single().Name);
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
