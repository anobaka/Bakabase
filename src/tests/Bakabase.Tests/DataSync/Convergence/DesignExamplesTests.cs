using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Services;

namespace Bakabase.Tests.DataSync.Convergence;

/// <summary>
/// The worked examples of §9.1 (design §6.8 B, D–I, L–N) and the engineering critique's (a) and (d), on the real
/// stack: custom properties through the Property service, every decision through the facade. A and C, J and K run in
/// the two-host tests; the rest of the critique in the Merge3 and codec tests.
/// </summary>
[TestClass]
public class DesignExamplesTests
{
    private const string Cp = DataSyncKindIds.CustomProperty;

    private readonly SyncWorld _world = new();

    private async Task<(SyncHost, SyncHost)> TwoWayAsync()
    {
        var pc1 = await _world.AddHostAsync("PC-1");
        var pc2 = await _world.AddHostAsync("PC-2");
        await _world.LinkAsync(pc2, pc1, DataSyncLinkMode.TwoWay);
        return (pc1, pc2);
    }

    /// <summary>Pulls in turn until nothing changes, then one more round: no revision, no Seq (no ping-pong).</summary>
    private async Task SettleAsync(params SyncHost[] hosts)
    {
        for (var round = 0; round < 6; round++)
        {
            var before = await _world.DigestAsync();
            foreach (var host in hosts) await _world.SyncAsync(host);
            if (await _world.DigestAsync() == before) return;
        }

        Assert.Fail("The hosts did not settle: a ping-pong.\n" + await _world.DigestAsync());
    }

    private static async Task<DataSyncInboxItemView> ItemAsync(SyncHost host, DataSyncInboxItemType type) =>
        (await host.OpenItemsAsync()).Single(i => i.Type == type);

    private static async Task<int> OpenAsync(params SyncHost[] hosts)
    {
        var open = 0;
        foreach (var host in hosts) open += (await host.OpenItemsAsync()).Count;
        return open;
    }

    private static async Task<List<string>> LabelsAsync(SyncHost host, string name) =>
        SyncOptions.Read(await host.PropertyAsync(name)).Select(o => o.Label).ToList();

    private static async Task<bool> HasAsync(SyncHost host, string name) =>
        (await host.PropertiesAsync()).Any(p => p.Name == name);

    /// <summary>Both hosts hold the definition under one key with one comparison form (§3.4).</summary>
    private static async Task AssertSameFormAsync(SyncHost a, SyncHost b, string name)
    {
        async Task<DataSyncEntityDbModel> RowAsync(SyncHost host)
        {
            var id = (await host.PropertyAsync(name)).Id.ToString();
            return (await host.Node.EntitiesAsync()).Single(e => e.Kind == Cp && e.LocalKey == id);
        }

        var (ra, rb) = (await RowAsync(a), await RowAsync(b));
        var bKeys = await b.Node.KeysAsync();
        Assert.IsTrue(bKeys.TryGetValue((Cp, ra.SyncKey), out var bId) && bId == rb.Id, $"{name}: one key on both");
        Assert.AreEqual(ra.SharedHash, rb.SharedHash, $"{a} and {b} differ on {name}");
    }

    // ---- B -------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task B_one_side_renames_the_other_adds_an_option()
    {
        var (pc1, pc2) = await TwoWayAsync();
        await pc1.AddPropertyAsync("Genre", PropertyType.MultipleChoice, "Action");
        await SettleAsync(pc1, pc2);

        await pc1.RenameAsync("Genre", "类型");
        await pc2.EditAsync("Genre", o => o.Append(new SyncOption(SyncHost.NewId(), "Isekai")));
        await SettleAsync(pc1, pc2);

        Assert.AreEqual(0, await OpenAsync(pc1, pc2));
        await AssertSameFormAsync(pc1, pc2, "类型");
        CollectionAssert.AreEqual(new[] { "Action", "Isekai" }, await LabelsAsync(pc1, "类型"));
    }

    // ---- D and critique (d) --------------------------------------------------------------------------------

    [TestMethod]
    [DataRow(true, false, DisplayName = "concurrent")]
    [DataRow(true, true, DisplayName = "concurrent, under IgnoreCase")]
    [DataRow(false, true, DisplayName = "sequential, under IgnoreCase: the service folds the second into the first")]
    public async Task D_both_devices_add_the_same_option(bool concurrent, bool ignoreCase)
    {
        var (pc1, pc2) = await TwoWayAsync();
        var genre = await pc1.AddPropertyAsync("Genre", PropertyType.MultipleChoice, "Drama");
        if (ignoreCase) await pc1.PutAsync(genre, "Genre", true, SyncOptions.Read(genre));
        await SettleAsync(pc1, pc2);

        await pc1.EditAsync("Genre", o => o.Append(new SyncOption(SyncHost.NewId(), "Action")));
        if (!concurrent) await SettleAsync(pc1, pc2);
        await pc2.EditAsync("Genre", o => o.Append(new SyncOption(SyncHost.NewId(), ignoreCase ? "action" : "Action")));
        await SettleAsync(pc2, pc1);

        // Mapped, not duplicated: no rename, no item, and another round issues nothing.
        Assert.AreEqual(0, await OpenAsync(pc1, pc2));
        await AssertSameFormAsync(pc1, pc2, "Genre");
        foreach (var host in new[] { pc1, pc2 })
        {
            Assert.AreEqual(1, (await LabelsAsync(host, "Genre"))
                .Count(l => string.Equals(l, "action", StringComparison.OrdinalIgnoreCase)), $"{host}");
        }
    }

    [TestMethod]
    public async Task CritiqueD_a_source_holding_Action_and_action_keeps_both_and_the_receiver_holds_one()
    {
        var (pc1, pc2) = await TwoWayAsync();
        // Both stored before IgnoreCase was turned on: the service's Put keeps every stored id (F72).
        var genre = await pc1.AddPropertyAsync("Genre", PropertyType.MultipleChoice, "Action", "action", "Drama");
        await pc1.PutAsync(genre, "Genre", true, SyncOptions.Read(genre));
        Assert.AreEqual(3, (await LabelsAsync(pc1, "Genre")).Count);
        await SettleAsync(pc1, pc2);

        CollectionAssert.AreEqual(new[] { "Action", "Drama" }, await LabelsAsync(pc2, "Genre"),
            "the receiver's create folds as a fresh property does");
        Assert.AreEqual(3, (await LabelsAsync(pc1, "Genre")).Count, "the source keeps both");

        // Edits of either side travel, and no deletion of the duplicate is ever offered or made.
        await pc2.RenameAsync("Genre", "Genres");
        await SettleAsync(pc1, pc2);
        await pc1.EditAsync("Genres", o => o.Append(new SyncOption(SyncHost.NewId(), "Horror")));
        await SettleAsync(pc1, pc2);
        Assert.AreEqual(4, (await LabelsAsync(pc1, "Genres")).Count);
        CollectionAssert.AreEqual(new[] { "Action", "Drama", "Horror" }, await LabelsAsync(pc2, "Genres"));
        Assert.AreEqual(0, await OpenAsync(pc1, pc2), "nothing asked");
        await AssertSameFormAsync(pc1, pc2, "Genres");
    }

    // ---- E -------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task E_a_type_change_is_always_a_decision_and_only_that_definition_waits()
    {
        var (pc1, pc2) = await TwoWayAsync();
        await pc1.AddPropertyAsync("Genre", PropertyType.MultipleChoice, "A");
        await pc1.AddPropertyAsync("Mood", PropertyType.SingleLineText);
        await SettleAsync(pc1, pc2);
        var genre2 = await pc2.PropertyAsync("Genre");
        await pc2.UseAsync(genre2, SyncOptions.Read(genre2)[0].Id, 3);

        await pc1.ChangeTypeAsync(await pc1.PropertyAsync("Genre"), PropertyType.SingleChoice);
        await pc1.RenameAsync("Mood", "Moods");
        await _world.SyncAsync(pc2);

        var item = await ItemAsync(pc2, DataSyncInboxItemType.TypeChange);
        Assert.AreEqual("SingleChoice", item.Payload.RemoteSubtype);
        Assert.AreEqual(3, item.Payload.ValueCount);
        Assert.AreEqual(PropertyType.MultipleChoice, (await pc2.PropertyAsync("Genre")).Type, "frozen for this link");
        Assert.IsTrue(await HasAsync(pc2, "Moods"), "everything else syncs");
        await _world.SyncAsync(pc2);
        Assert.AreEqual(1, (await pc2.OpenItemsAsync()).Count(i => i.Type == DataSyncInboxItemType.TypeChange));
    }

    // ---- F -------------------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow(DataSyncInboxAction.KeepDeleted)]
    [DataRow(DataSyncInboxAction.RestoreHere)]
    public async Task F_a_deletion_and_an_edit_at_the_same_time(DataSyncInboxAction action)
    {
        var (pc1, pc2) = await TwoWayAsync();
        await pc1.AddPropertyAsync("Mood", PropertyType.MultipleChoice, "Calm");
        await SettleAsync(pc1, pc2);
        var mood2 = await pc2.PropertyAsync("Mood");
        await pc2.UseAsync(mood2, SyncOptions.Read(mood2)[0].Id, 5);

        await pc1.DeletePropertyAsync(await pc1.PropertyAsync("Mood"));
        await pc2.EditAsync("Mood", o => o.Append(new SyncOption(SyncHost.NewId(), "Tense")));
        await _world.SyncAsync(pc2);
        Assert.IsTrue(await HasAsync(pc2, "Mood"), "the edit wins on PC-2, silently");
        Assert.AreEqual(0, await OpenAsync(pc2));

        await _world.SyncAsync(pc1);
        var item = await ItemAsync(pc1, DataSyncInboxItemType.DeletedHereEditedThere);
        Assert.AreEqual(DataSyncInboxDrafts.DetailChangedAfterDelete, item.Payload.Detail);
        Assert.IsFalse(await HasAsync(pc1, "Mood"), "never an automatic revive");

        Assert.IsNull(await pc1.ResolveAsync(item, action));
        if (action == DataSyncInboxAction.KeepDeleted)
        {
            // The tombstone dominates, and PC-2 is asked: it has values there.
            await _world.SyncAsync(pc2);
            await ItemAsync(pc2, DataSyncInboxItemType.DeletedThere);
            return;
        }

        await SettleAsync(pc1, pc2);
        await AssertSameFormAsync(pc1, pc2, "Mood");
        Assert.AreEqual(0, await OpenAsync(pc1, pc2));
    }

    // ---- G and N: a headless hub ---------------------------------------------------------------------------

    private async Task<(SyncHost Pc1, SyncHost Pc2, SyncHost Nas)> StarAsync()
    {
        var nas = await _world.AddHostAsync("NAS", headless: true);
        var pc1 = await _world.AddHostAsync("PC-1");
        var pc2 = await _world.AddHostAsync("PC-2");
        await _world.LinkAsync(pc1, nas, DataSyncLinkMode.TwoWay);
        await _world.LinkAsync(pc2, nas, DataSyncLinkMode.TwoWay);
        return (pc1, pc2, nas);
    }

    [TestMethod]
    public async Task G_a_star_around_a_headless_hub()
    {
        var (pc1, pc2, nas) = await StarAsync();
        await pc1.AddPropertyAsync("Artist", PropertyType.SingleLineText);
        await SettleAsync(pc1, nas, pc2);

        await pc1.RenameAsync("Artist", "Artists");
        await pc2.RenameAsync("Artist", "作者");
        await _world.SyncAsync(nas);
        var nasItem = await ItemAsync(nas, DataSyncInboxItemType.FieldConflict);
        await _world.SyncAsync(pc1);
        Assert.AreEqual(1, (await pc1.CallAsync(s => s.GetLinksAsync(default))).Single().PeerAttention?.OpenDecisions,
            "the NAS's heads report the decision");
        Assert.AreEqual(0, await OpenAsync(pc1), "PC-1 never sees a conflict");

        await _world.SyncAsync(pc2);
        Assert.IsNull(await pc2.ResolveAsync(await ItemAsync(pc2, DataSyncInboxItemType.FieldConflict),
            DataSyncInboxAction.KeepLocal));
        await _world.SyncAsync(nas);
        var closed = (await nas.Node.ItemsAsync(false)).Single(i => i.Id == nasItem.Id);
        Assert.AreEqual(DataSyncInboxClosure.ResolvedElsewhere, closed.Closure, "decided on PC-2");
        Assert.AreEqual("PC-2", closed.ClosedByName);

        await SettleAsync(pc1, nas, pc2);
        Assert.IsTrue(await HasAsync(pc1, "作者"));
        Assert.AreEqual(0, await OpenAsync(pc1, pc2, nas));
        await AssertSameFormAsync(pc1, pc2, "作者");
    }

    [TestMethod]
    public async Task N_a_decision_waits_on_a_headless_hub()
    {
        var (pc1, pc2, nas) = await StarAsync();
        await pc1.AddPropertyAsync("Mood", PropertyType.MultipleChoice, "Calm");
        await SettleAsync(pc1, nas, pc2);
        var mood = await nas.PropertyAsync("Mood");
        await nas.UseAsync(mood, SyncOptions.Read(mood)[0].Id, 12);

        await pc1.DeletePropertyAsync(await pc1.PropertyAsync("Mood"));
        await SettleAsync(pc1, nas, pc2);
        Assert.IsTrue(await HasAsync(nas, "Mood"), "the NAS keeps it until someone decides");
        Assert.IsTrue(await HasAsync(pc2, "Mood"), "so PC-2 never receives the deletion");
        var attention = (await pc2.CallAsync(s => s.GetLinksAsync(default))).Single().PeerAttention;
        Assert.AreEqual((true, 1), (attention!.Headless, attention.OpenDecisions), "readers see what waits there");

        // Decided on the NAS (through server switching): the deletion flows on.
        Assert.IsNull(await nas.ResolveAsync(await ItemAsync(nas, DataSyncInboxItemType.DeletedThere),
            DataSyncInboxAction.DeleteHere));
        await SettleAsync(pc1, nas, pc2);
        Assert.IsFalse(await HasAsync(pc2, "Mood"));
    }

    // ---- H -------------------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow(DataSyncInboxAction.Link)]
    [DataRow(DataSyncInboxAction.KeepBoth)]
    [DataRow(DataSyncInboxAction.Skip)]
    public async Task H_two_devices_created_the_same_property_separately(DataSyncInboxAction action)
    {
        var (pc1, pc2) = await TwoWayAsync();
        await pc1.AddPropertyAsync("Rating", PropertyType.SingleChoice, "Good");
        await pc2.AddPropertyAsync("Rating", PropertyType.SingleChoice, "Good");
        await _world.SyncAsync(pc2);

        var suggestion = await ItemAsync(pc2, DataSyncInboxItemType.LinkSuggestion);
        Assert.AreEqual(DataSyncNaturalMatch.Identical, suggestion.Payload.Candidates!.Single().Match);
        Assert.AreEqual(1, (await pc2.PropertiesAsync()).Count, "never linked by name, and not created: it waits");
        Assert.IsNull(await pc2.ResolveAsync(suggestion, action, suggestion.Payload.Candidates!.Single().LocalKey));
        await SettleAsync(pc1, pc2);
        // A skip is PC-2's alone: PC-1 is asked about PC-2's copy in its turn.
        Assert.AreEqual(0, action == DataSyncInboxAction.Skip ? await OpenAsync(pc2) : await OpenAsync(pc1, pc2),
            "nobody asks again");

        switch (action)
        {
            case DataSyncInboxAction.Link:
                Assert.AreEqual(1, (await pc2.PropertiesAsync()).Count);
                Assert.AreEqual(2, (await pc1.Node.KeysAsync()).Count(k => k.Key.Kind == Cp), "the keys became aliases");
                await AssertSameFormAsync(pc1, pc2, "Rating");
                break;
            case DataSyncInboxAction.KeepBoth:
                // Two-way: the peer's copy takes the other name too.
                Assert.AreEqual(2, (await pc1.PropertiesAsync()).Count);
                Assert.AreEqual(2, (await pc2.PropertiesAsync()).Count);
                break;
            default:
                await pc1.EditAsync("Rating", o => o.Append(new SyncOption(SyncHost.NewId(), "Bad")));
                await SettleAsync(pc1, pc2);
                Assert.AreEqual(0, await OpenAsync(pc2), "a skipped definition is not proposed again");
                Assert.AreEqual(1, (await pc2.PropertiesAsync()).Count, "and no duplicate is created");
                break;
        }
    }

    // ---- I -------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task I_local_changes_in_a_followed_definition()
    {
        var pc1 = await _world.AddHostAsync("PC-1");
        var nas = await _world.AddHostAsync("NAS", headless: true);
        var pc2 = await _world.AddHostAsync("PC-2");
        await _world.LinkAsync(nas, pc1, DataSyncLinkMode.Follow);
        await _world.LinkAsync(pc2, nas, DataSyncLinkMode.TwoWay);
        await pc1.AddPropertyAsync("Genre", PropertyType.SingleLineText);
        await pc1.AddPropertyAsync("Mood", PropertyType.SingleLineText);
        await SettleAsync(nas, pc2);

        // The NAS's own change to a field PC-1 changes too: PC-1 wins, and nothing asks.
        await nas.RenameAsync("Genre", "Genre (NAS)");
        await pc1.RenameAsync("Genre", "Genre (PC-1)");
        await _world.SyncAsync(nas);
        Assert.IsTrue(await HasAsync(nas, "Genre (PC-1)"));
        Assert.AreEqual(0, await OpenAsync(nas));

        // Except when the value came from PC-2 through the two-way link: overriding it would undo PC-2's edit.
        await pc2.RenameAsync("Mood", "Mood (PC-2)");
        await _world.SyncAsync(nas);
        await pc1.RenameAsync("Mood", "Mood (PC-1)");
        await _world.SyncAsync(nas);
        await ItemAsync(nas, DataSyncInboxItemType.FieldConflict);
        Assert.IsTrue(await HasAsync(nas, "Mood (PC-2)"));
    }

    [TestMethod]
    public async Task Mutual_Follow_ends_in_items_not_a_flip_flop()
    {
        var pc1 = await _world.AddHostAsync("PC-1");
        var pc2 = await _world.AddHostAsync("PC-2");
        await _world.LinkAsync(pc1, pc2, DataSyncLinkMode.Follow);
        await _world.LinkAsync(pc2, pc1, DataSyncLinkMode.Follow);
        await pc1.AddPropertyAsync("Genre", PropertyType.SingleLineText);
        await SettleAsync(pc1, pc2);

        await pc1.RenameAsync("Genre", "One");
        await pc2.RenameAsync("Genre", "Two");
        await SettleAsync(pc1, pc2);
        Assert.IsTrue(await HasAsync(pc1, "One") && await HasAsync(pc2, "Two"));
        Assert.AreEqual((1, 1), ((await pc1.OpenItemsAsync()).Count, (await pc2.OpenItemsAsync()).Count));
    }

    // ---- L -------------------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow(DataSyncInboxAction.DeleteHere)]
    [DataRow(DataSyncInboxAction.KeepHereOnly)]
    public async Task L_a_definition_deleted_on_the_peer(DataSyncInboxAction action)
    {
        var (pc1, pc2) = await TwoWayAsync();
        await pc1.AddPropertyAsync("Mood", PropertyType.MultipleChoice, "Calm");
        await pc1.AddPropertyAsync("Tone", PropertyType.SingleLineText);
        await SettleAsync(pc1, pc2);
        var mood = await pc2.PropertyAsync("Mood");
        await pc2.UseAsync(mood, SyncOptions.Read(mood)[0].Id, 4);

        await pc1.DeletePropertyAsync(await pc1.PropertyAsync("Mood"));
        await pc1.DeletePropertyAsync(await pc1.PropertyAsync("Tone"));
        await _world.SyncAsync(pc2);
        Assert.IsFalse(await HasAsync(pc2, "Tone"), "created by sync, no values: deleted by itself");
        var item = await ItemAsync(pc2, DataSyncInboxItemType.DeletedThere);
        Assert.AreEqual(4, item.Payload.ValueCount);

        Assert.IsNull(await pc2.ResolveAsync(item, action));
        await SettleAsync(pc1, pc2);
        Assert.AreEqual(0, await OpenAsync(pc1, pc2));
        Assert.AreEqual(action == DataSyncInboxAction.KeepHereOnly, await HasAsync(pc2, "Mood"));
        if (action == DataSyncInboxAction.KeepHereOnly)
        {
            Assert.AreEqual(DataSyncEntitySyncState.Detached, (await pc2.Node.EntitiesAsync())
                .Single(e => e.LocalKey == mood.Id.ToString()).State);
        }
    }

    // ---- M -------------------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow(DataSyncInboxAction.ReviewEach)]
    [DataRow(DataSyncInboxAction.ApplyAll)]
    public async Task M_a_mass_option_deletion(DataSyncInboxAction action)
    {
        var (pc1, pc2) = await TwoWayAsync();
        await pc1.AddPropertyAsync("Genre", PropertyType.MultipleChoice,
            Enumerable.Range(0, 70).Select(i => "Tag" + i).ToArray());
        await SettleAsync(pc1, pc2);
        var genre = await pc1.PropertyAsync("Genre");
        await pc1.UseAsync(genre, SyncOptions.Read(genre)[30].Id, 2);

        await pc2.EditAsync("Genre", o => o.Take(10), "Genres");
        await _world.SyncAsync(pc1);
        var item = await ItemAsync(pc1, DataSyncInboxItemType.MassChildDeletion);
        Assert.AreEqual(60, item.Payload.ChildrenTotal);
        Assert.IsTrue(await HasAsync(pc1, "Genre"), "nothing of PC-2's change applies");

        Assert.IsNull(await pc1.ResolveAsync(item, action));
        Assert.IsTrue(await HasAsync(pc1, "Genres"), "the rest of the change applies");
        var held = (await pc1.OpenItemsAsync()).Count(i => i.Type == DataSyncInboxItemType.ChildDeletedInUse);
        var kept = (await LabelsAsync(pc1, "Genres")).Count;
        Assert.AreEqual(action == DataSyncInboxAction.ReviewEach ? (60, 70) : (1, 11), (held, kept),
            "review each: every one its own item; apply all: only the used one held");
    }

    // ---- critique (a) --------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow(false, DisplayName = "alone")]
    [DataRow(true, DisplayName = "while the other device renames the property")]
    public async Task CritiqueA_a_node_moved_on_one_device_converges_and_stops(bool renamedThere)
    {
        var (pc1, pc2) = await TwoWayAsync();
        await pc1.AddPropertyAsync("Region", PropertyType.Multilevel, "Asia", "Japan", "China", "Europe");
        await pc1.EditAsync("Region", o => o.Select(n => n.Label is "Japan" or "China"
            ? n with { Parent = o.Single(x => x.Label == "Asia").Id }
            : n));
        await SettleAsync(pc1, pc2);

        await pc1.EditAsync("Region", o => o.Select(n => n.Label == "Japan"
            ? n with { Parent = o.Single(x => x.Label == "Europe").Id }
            : n));
        if (renamedThere) await pc2.RenameAsync("Region", "Regions");
        await SettleAsync(pc2, pc1);

        var name = renamedThere ? "Regions" : "Region";
        Assert.AreEqual(0, await OpenAsync(pc1, pc2), "a move one device made is no question");
        await AssertSameFormAsync(pc1, pc2, name);
        var nodes = SyncOptions.Read(await pc2.PropertyAsync(name));
        Assert.AreEqual(nodes.Single(n => n.Label == "Europe").Id, nodes.Single(n => n.Label == "Japan").Parent,
            "the node moved, keeping its id");
    }

    // ---- defects the convergence test found ----------------------------------------------------------------

    /// <summary>
    /// A question its decision no longer stands for — the name it matched was renamed away — closes, and its record
    /// is merged again at once: it never waits for a decision without an item.
    /// </summary>
    [TestMethod]
    public async Task A_suggestion_that_no_longer_stands_merges_its_record_at_once()
    {
        var (pc1, pc2) = await TwoWayAsync();
        await pc1.AddPropertyAsync("Mood", PropertyType.SingleLineText);
        await pc2.AddPropertyAsync("Mood", PropertyType.SingleLineText);
        await _world.SyncAsync(pc2);
        var suggestion = await ItemAsync(pc2, DataSyncInboxItemType.LinkSuggestion);

        await pc2.RenameAsync("Mood", "Feeling");
        await pc2.ResolveAsync(suggestion, DataSyncInboxAction.Link, suggestion.Payload.Candidates!.Single().LocalKey);
        Assert.IsTrue(await HasAsync(pc2, "Mood"), "no name matches now: PC-1's Mood is created");
        Assert.AreEqual(0, await OpenAsync(pc2));
        await SettleAsync(pc1, pc2);
        await AssertSameFormAsync(pc1, pc2, "Mood");
    }

    /// <summary>
    /// A definition already agreed with another of the peer's records is never offered or taken for a link: two of the
    /// peer's records would bind to it, and every later change of either would merge without a base. The question
    /// offers Keep both and Skip, and a link asked for anyway is refused, changing nothing.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_definition_agreed_with_another_record_is_not_offered_for_a_link(bool peerCreatedIt)
    {
        var (pc1, pc2) = await TwoWayAsync();
        await (peerCreatedIt ? pc1 : pc2).AddPropertyAsync("Genre", PropertyType.MultipleChoice, "Rock", "Jazz", "Folk");
        await SettleAsync(pc1, pc2);
        await pc1.AddPropertyAsync("Genre", PropertyType.MultipleChoice, "Pop");
        await _world.SyncAsync(pc2);

        var suggestion = await ItemAsync(pc2, DataSyncInboxItemType.LinkSuggestion);
        var agreed = (await pc2.PropertyAsync("Genre")).Id.ToString();
        Assert.IsFalse(suggestion.Payload.Candidates?.Any(c => c.LocalKey == agreed) == true);
        CollectionAssert.AreEqual(new[] {DataSyncInboxAction.KeepBoth, DataSyncInboxAction.Skip},
            suggestion.AllowedActions.ToArray());
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid,
            (await pc2.ResolveAsync(suggestion, DataSyncInboxAction.Link, agreed))?.Code);
        CollectionAssert.AreEquivalent(new[] {"Rock", "Jazz", "Folk"}, await LabelsAsync(pc2, "Genre"));

        Assert.IsNull(await pc2.ResolveAsync(await ItemAsync(pc2, DataSyncInboxItemType.LinkSuggestion),
            DataSyncInboxAction.KeepBoth));
        await SettleAsync(pc1, pc2);
        CollectionAssert.AreEquivalent(new[] {"Pop"}, await LabelsAsync(pc2, "Genre (PC-1)"));
        Assert.AreEqual(0, await OpenAsync(pc1, pc2));
    }

    /// <summary>
    /// A card whose candidate the merger no longer offers — its type changed since — is shown again as the merger sees
    /// it, instead of being refused silently every time the person picks that candidate.
    /// </summary>
    [TestMethod]
    public async Task A_link_to_a_candidate_no_longer_offered_updates_the_card()
    {
        var (pc1, pc2) = await TwoWayAsync();
        await pc1.AddPropertyAsync("Rating", PropertyType.SingleChoice, "Good");
        await pc2.AddPropertyAsync("Rating", PropertyType.SingleChoice, "Good");
        await _world.SyncAsync(pc2);
        var suggestion = await ItemAsync(pc2, DataSyncInboxItemType.LinkSuggestion);
        var candidate = suggestion.Payload.Candidates!.Single();
        Assert.IsTrue(candidate.Updatable);

        await pc2.ChangeTypeAsync(await pc2.PropertyAsync("Rating"), PropertyType.MultipleChoice);
        await pc2.ResolveAsync(suggestion, DataSyncInboxAction.Link, candidate.LocalKey);
        var card = await ItemAsync(pc2, DataSyncInboxItemType.LinkSuggestion);
        Assert.IsFalse(card.Payload.Candidates!.Single().Updatable, "the card offers what the merger offers now");
        Assert.AreEqual(DataSyncProblemCode.DecisionsInvalid,
            (await pc2.ResolveAsync(card, DataSyncInboxAction.Link, candidate.LocalKey))?.Code);
    }

    /// <summary>
    /// Linking anew after a reset, a definition this device deleted and the peer still has is not taken back: the first
    /// sync is the ordinary merge, which never revives by itself (§8.4 rows T2, T3), and the peer is asked about the
    /// deletion instead.
    /// </summary>
    [TestMethod]
    public async Task A_definition_deleted_here_is_not_revived_by_a_new_first_contact()
    {
        var (pc1, pc2) = await TwoWayAsync();
        await pc1.AddPropertyAsync("Tags", PropertyType.Tags, "Blue");
        await SettleAsync(pc1, pc2);
        var link = await pc2.Node.RequireLinkToAsync(pc1.Node);
        await pc2.CallAsync(s => s.UpdateLinkAsync(link.Id, new DataSyncLinkUpdateInput(DataSyncLinkMode.Off, null), true,
            default));
        await pc2.DeletePropertyAsync(await pc2.PropertyAsync("Tags"));

        Assert.IsNull(await pc2.CallAsync(s => s.ResetLinkAsync(link.Id, default)));
        await _world.LinkAsync(pc2, pc1, DataSyncLinkMode.TwoWay);
        await SettleAsync(pc1, pc2);
        Assert.IsFalse(await HasAsync(pc2, "Tags"), "never revived by a merge");
        Assert.AreEqual("Tags", (await ItemAsync(pc1, DataSyncInboxItemType.DeletedThere)).Payload.EntityName);
    }
}
