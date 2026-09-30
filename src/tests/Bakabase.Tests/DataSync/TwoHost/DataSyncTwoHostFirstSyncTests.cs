using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
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
/// The first sync over two real hosts (§8.3, §10.1): the read-only preview of the ordinary merge, its Start and what
/// Start refuses, and two-way approvals whose read-back is declined or fails (§7.2.4, N14).
/// </summary>
[TestClass]
public class DataSyncTwoHostFirstSyncTests
{
    private const string Rock = "00000000-0000-4000-8000-00000000a001";
    private const string Jazz = "00000000-0000-4000-8000-00000000a002";

    private DataSyncTestClock _clock = null!;
    private TwoHostNetwork _network = null!;
    private TwoHostNode _a = null!;
    private TwoHostNode _b = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _clock = new DataSyncTestClock(DateTime.UtcNow.AddTicks(-(DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond)));
        _network = new TwoHostNetwork(_clock);
        _a = await TwoHostNode.StartAsync(_network, Device("PC-A"));
        _b = await TwoHostNode.StartAsync(_network, Device("PC-B"));
        await _a.InScopeAsync(sp => sp.GetRequiredService<ICustomPropertyService>().Add(Genre("Rock")));
    }

    [TestMethod]
    public async Task A_first_sync_previews_the_merge_and_its_start_applies_it()
    {
        // B's first sync with A creates Genre with A's option ids; B's resources then use Rock.
        var link = await _b.PairTwoWayAsync(_a, _network);
        var preview = await _b.CallAsync(s => s.GetFirstSyncAsync(link.Id, default));
        Assert.AreEqual(("Genre", DataSyncPreviewOutcome.Create),
            (preview.Entries.Single().Name, preview.Entries.Single().Outcome));
        await _b.StartFirstSyncAsync(link);
        var genre = (await PropertiesAsync(_b)).Single();
        await _b.InScopeAsync(sp => sp.GetRequiredService<ICustomPropertyValueService>().AddDbModelRange(
            [Value(genre.Id, 1, Rock), Value(genre.Id, 2, Rock), Value(genre.Id, 3, Jazz)]));

        // B starts over with A (Dismiss, §8.1), while A renames Rock: B's next first sync finds Genre by key.
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
        link = await _b.RequireLinkToAsync(_a);

        // The read-only preview (§8.3): the key-bound Genre is updated. A choice naming no record of the snapshot is
        // refused, and nothing is started.
        preview = await _b.CallAsync(s => s.GetFirstSyncAsync(link.Id, default));
        Assert.AreEqual(DataSyncPreviewOutcome.Update, preview.Entries.Single().Outcome);
        var refused = await _b.CallAsync(s => s.StartFirstSyncAsync(link.Id, new DataSyncFirstSyncStartInput(
            [new DataSyncFirstSyncChoice(DataSyncKindIds.CustomProperty, "nothing", DataSyncFirstSyncAction.Skip)]),
            default));
        Assert.AreEqual((DataSyncProblemCode.DecisionsInvalid, (string?) null), (refused.Problem?.Code, refused.TaskId));

        await _b.StartFirstSyncAsync(link);
        var labels = Choices((await PropertiesAsync(_b)).Single()).ToDictionary(c => c.Value, c => c.Label);
        Assert.AreEqual(("Rock & Roll", "Jazz"), (labels[Rock], labels[Jazz]), "renamed in place, values kept");
        Assert.AreEqual(DataSyncLinkState.Active, (await _b.RequireLinkToAsync(_a)).State);
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
        await _b.StartFirstSyncAsync(await _b.PairTwoWayAsync(_a, _network));
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
        // Every definition stays.
        Assert.AreEqual("Genre", (await PropertiesAsync(_b)).Single().Name);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

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
