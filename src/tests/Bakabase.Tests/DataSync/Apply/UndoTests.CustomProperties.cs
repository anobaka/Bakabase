using System.Globalization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Tests.DataSync.CustomProperties;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using static Bakabase.Tests.DataSync.Apply.DataSyncApplyFixture;

namespace Bakabase.Tests.DataSync.Apply;

/// <summary>
/// §8.11 over the real custom property kind (package B's adapter and the Property services): a type change converts
/// back to the captured row, options, ids, colours and values' references included; a re-created deletion keeps the
/// case-variant duplicates IgnoreCase switched on afterwards kept (F72); and an undone multilevel node takes nothing
/// with it that the apply did not put under it.
/// </summary>
public partial class UndoTests
{
    private static readonly CustomPropertyCodec PropertyCodec = CustomPropertyCodec.Instance;

    /// <summary>A provider with the real custom property kind, and a two-way link to a simulated peer.</summary>
    private static async Task<(DataSyncApplyFixture F, DataSyncPeer Peer, DataSyncLinkDbModel Link)> PropertiesAsync()
    {
        var f = await CreateAsync(customProperties: true);
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer, DataSyncLinkMode.TwoWay, true, Properties);
        return (f, peer, link);
    }

    private static async Task<int> AddPropertyAsync(DataSyncApplyFixture f, string name, PropertyType type, object options) =>
        (await f.CustomProperties.Add(new CustomPropertyAddOrPutDto
        {
            Name = name, Type = type, Options = JsonConvert.SerializeObject(options),
        })).Id;

    private static Task PutPropertyAsync(DataSyncApplyFixture f, int id, string name, PropertyType type, object options) =>
        f.CustomProperties.Put(id, new CustomPropertyAddOrPutDto
        {
            Name = name, Type = type, Options = JsonConvert.SerializeObject(options),
        });

    private static string KeyOf(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static async Task<CustomPropertyContentV1> PropertyContentAsync(DataSyncApplyFixture f, string localKey)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var kind = scope.ServiceProvider.GetServices<IDataSyncKind>()
            .Single(k => k.Codec.Descriptor.Kind == DataSyncKindIds.CustomProperty);
        return PropertyCodec.ReadLocal((await kind.ReadAsync([localKey], default)).Single().Content);
    }

    private static string CanonicalHash(CustomPropertyContentV1 content) =>
        ContentHash.Of(PropertyCodec.Write(content));

    private static ChoiceOptions Choice(string uuid, string label, string? color = null) =>
        new() { Value = uuid, Label = label, Color = color };

    private static MultilevelDataOptions Node(string uuid, string label, params MultilevelDataOptions[] children) =>
        new() { Value = uuid, Label = label, Children = children.Length == 0 ? null : children.ToList() };

    private static MultilevelPropertyOptions Places(params MultilevelDataOptions[] nodes) => new() { Data = nodes.ToList() };

    private static IEnumerable<string> Tree(IEnumerable<CustomPropertyNodeV1> nodes, string? parent = null) =>
        nodes.SelectMany(n => new[] { (parent is null ? "" : parent + "/") + n.Label }
            .Concat(Tree(n.Children, (parent is null ? "" : parent + "/") + n.Label)));

    [TestMethod]
    public async Task Undoing_a_convert_restores_every_option_with_its_id_and_colour_and_the_values_point_at_them()
    {
        using var newtonsoft = NewtonsoftDefaults.UseApp();
        var (f, peer, link) = await PropertiesAsync();
        var id = await AddPropertyAsync(f, "Genre", PropertyType.SingleChoice, new SingleChoicePropertyOptions
        {
            Choices = [Choice("r", "Rock", "#ff0000"), Choice("p", "Pop", "#00ff00"), Choice("j", "Jazz")],
        });
        var localKey = KeyOf(id);
        var values = f.Services.GetRequiredService<ICustomPropertyValueService>();
        await values.AddDbModelRange([new CustomPropertyValueDbModel
        {
            ResourceId = 1, PropertyId = id, Scope = (int) PropertyValueScope.Manual,
            Value = "r".SerializeAsStandardValue(PropertyType.SingleChoice.GetDbValueType()),
        }]);
        await f.RefreshAsync();
        var before = await PropertyContentAsync(f, localKey);
        var row = await f.RowAsync(localKey, Properties);

        // The peer changes the type; this device converts.
        await f.ApplyAsync(link, peer, (Properties, peer.Record([row.SyncKey], peer.Next(Vv(row.VvJson)),
            PropertyCodec.Write(before with { Type = PropertyType.MultipleChoice }), row.OrderKey)));
        var item = (await f.OpenItemsAsync()).Single(i => i.Type == DataSyncInboxItemType.TypeChange);
        var logId = await f.ResolveAsync(item, DataSyncInboxAction.Convert);
        Assert.AreEqual(PropertyType.MultipleChoice, (await PropertyContentAsync(f, localKey)).Type);

        var preview = await f.Services.GetRequiredService<DataSyncUndoPlanner>().PreviewAsync(logId!.Value, default);
        Assert.IsTrue(preview.CanUndo);
        var undoId = await f.UndoAsync(logId.Value);

        Assert.IsNotNull(undoId);
        var after = await PropertyContentAsync(f, localKey);
        Assert.AreEqual(PropertyType.SingleChoice, after.Type);
        CollectionAssert.AreEqual(new[] { "r:Rock:#ff0000", "p:Pop:#00ff00", "j:Jazz:" },
            after.Choices.Select(c => $"{c.Uuid}:{c.Label}:{c.Color}").ToArray(), "every option, id and colour");
        Assert.AreEqual(CanonicalHash(before), CanonicalHash(after), "faithful: the canonical hash (§8.11)");
        var value = (await values.GetAllDbModels(v => v.PropertyId == id)).Single();
        Assert.AreEqual("r", value.Value!.DeserializeAsStandardValue<string>(StandardValueType.String),
            "the value points at the restored option");

        // What the undo publishes is the whole definition again.
        var published = await f.RowAsync(localKey, Properties);
        Assert.IsFalse(published.PublishHeld);
        Assert.AreEqual(DataSyncHistoryKind.Undo, (await f.HistoryAsync()).Single(h => h.Id == undoId).Kind);
    }

    [TestMethod]
    public async Task Undoing_a_deletion_recreates_case_variant_duplicates_under_IgnoreCase_verbatim()
    {
        using var newtonsoft = NewtonsoftDefaults.UseApp();
        var (f, peer, link) = await PropertiesAsync();
        // Added with IgnoreCase off, switched on afterwards: the service keeps "action" beside "Action" (F72).
        var options = new MultipleChoicePropertyOptions
        {
            IgnoreCase = false, Choices = [Choice("a1", "Action"), Choice("a2", "action"), Choice("d", "Drama")],
        };
        var id = await AddPropertyAsync(f, "Genre", PropertyType.MultipleChoice, options);
        options.IgnoreCase = true;
        await PutPropertyAsync(f, id, "Genre", PropertyType.MultipleChoice, options);
        await f.RefreshAsync();
        var before = await PropertyContentAsync(f, KeyOf(id));
        CollectionAssert.AreEqual(new[] { "a1", "a2", "d" }, before.Choices.Select(c => c.Uuid).ToArray());
        var row = await f.RowAsync(KeyOf(id), Properties);

        // The peer deletes it; this device deletes it here too.
        await f.ApplyAsync(link, peer, (Properties, peer.Tombstone([row.SyncKey], peer.Next(Vv(row.VvJson)))));
        var item = (await f.OpenItemsAsync()).Single(i => i.Type == DataSyncInboxItemType.DeletedThere);
        var logId = await f.ResolveAsync(item, DataSyncInboxAction.DeleteHere);
        Assert.IsNotNull((await f.ByKeyAsync(row.SyncKey, Properties))!.DeletedAtUtc);

        Assert.IsNotNull(await f.UndoAsync(logId!.Value));

        var revived = await f.ByKeyAsync(row.SyncKey, Properties);
        Assert.IsNull(revived!.DeletedAtUtc);
        Assert.AreNotEqual(KeyOf(id), revived.LocalKey, "a new local id (§8.11)");
        var after = await PropertyContentAsync(f, revived.LocalKey);
        Assert.IsTrue(after.IgnoreCase);
        CollectionAssert.AreEqual(new[] { "a1:Action", "a2:action", "d:Drama" },
            after.Choices.Select(c => $"{c.Uuid}:{c.Label}").ToArray(), "the case variant is not folded away");
        Assert.AreEqual(CanonicalHash(before), CanonicalHash(after), "faithful: the canonical hash (§8.11)");
    }

    /// <summary>The peer added "Europe" (and, with <paramref name="withChild"/>, "France" under it) to Places here.</summary>
    private static async Task<(DataSyncApplyFixture F, int Id, int LogId)> EuropeAddedByPeerAsync(bool withChild = false)
    {
        var (f, peer, link) = await PropertiesAsync();
        var id = await AddPropertyAsync(f, "Places", PropertyType.Multilevel,
            Places(Node("asia", "Asia"), Node("paris", "Paris")));
        await f.RefreshAsync();
        var row = await f.RowAsync(KeyOf(id), Properties);
        var content = await PropertyContentAsync(f, KeyOf(id));
        var europe = new CustomPropertyNodeV1("europe", "Europe", null)
        {
            Children = withChild ? [new CustomPropertyNodeV1("france", "France", null)] : [],
        };
        var outcome = await f.ApplyAsync(link, peer, (Properties, peer.Record([row.SyncKey], peer.Next(Vv(row.VvJson)),
            PropertyCodec.Write(content with { Nodes = [..content.Nodes, europe] }), row.OrderKey)));
        Assert.IsNotNull(outcome.ApplyLogId);
        CollectionAssert.Contains(Tree((await PropertyContentAsync(f, KeyOf(id))).Nodes).ToList(), "Europe");
        return (f, id, outcome.ApplyLogId!.Value);
    }

    [TestMethod]
    public async Task Undoing_an_added_node_with_its_own_child_takes_both_back()
    {
        using var newtonsoft = NewtonsoftDefaults.UseApp();
        var (f, id, logId) = await EuropeAddedByPeerAsync(withChild: true);

        Assert.IsNotNull(await f.UndoAsync(logId));

        CollectionAssert.AreEqual(new[] { "Asia", "Paris" }, Tree((await PropertyContentAsync(f, KeyOf(id))).Nodes).ToArray());
    }

    /// <summary>
    /// §8.11 restores exactly the changed paths: a node the apply added goes with everything below it, so it is not
    /// undone once something the apply did not put there is under it — a node the person added, or an older node
    /// moved there. Removing it would delete that too, and the undo's revision would delete it on every device.
    /// </summary>
    [TestMethod]
    [DataRow(false, DisplayName = "a node added under it")]
    [DataRow(true, DisplayName = "an older node moved under it")]
    public async Task An_added_node_with_something_else_under_it_now_is_not_undone(bool moved)
    {
        using var newtonsoft = NewtonsoftDefaults.UseApp();
        var (f, id, logId) = await EuropeAddedByPeerAsync();
        var edited = moved
            ? Places(Node("asia", "Asia"), Node("europe", "Europe", Node("paris", "Paris")))
            : Places(Node("asia", "Asia"), Node("paris", "Paris"), Node("europe", "Europe", Node("france", "France")));
        await PutPropertyAsync(f, id, "Places", PropertyType.Multilevel, edited);
        await f.RefreshAsync();
        var tree = Tree((await PropertyContentAsync(f, KeyOf(id))).Nodes).ToArray();

        var preview = await f.Services.GetRequiredService<DataSyncUndoPlanner>().PreviewAsync(logId, default);
        Assert.IsFalse(preview.CanUndo);
        Assert.AreEqual((DataSyncUndoBlock?) DataSyncUndoBlock.ChangedSinceImport, preview.Items.Single().Blocked);
        await NothingUndoneAsync(() => f.UndoAsync(logId));

        CollectionAssert.AreEqual(tree, Tree((await PropertyContentAsync(f, KeyOf(id))).Nodes).ToArray(),
            "nothing removed");
    }

    /// <summary>
    /// "Put the synced change back" (§6.5) removes a node again only with what the apply removed under it: a node the
    /// person restored and then put something else under stays, with it; the rest is put back.
    /// </summary>
    [TestMethod]
    [DataRow(false, DisplayName = "a node added under it")]
    [DataRow(true, DisplayName = "an older node moved under it")]
    public async Task Reapply_leaves_a_restored_node_with_something_else_under_it(bool moved)
    {
        using var newtonsoft = NewtonsoftDefaults.UseApp();
        var (f, peer, link) = await PropertiesAsync();
        var synced = Places(Node("asia", "Asia"), Node("europe", "Europe", Node("paris", "Paris")));
        var id = await AddPropertyAsync(f, "Places", PropertyType.Multilevel, synced);
        await f.RefreshAsync();
        var row = await f.RowAsync(KeyOf(id), Properties);
        var content = await PropertyContentAsync(f, KeyOf(id));
        // The peer deletes Europe and Paris under it, which nothing here uses: removed by themselves.
        await f.ApplyAsync(link, peer, (Properties, peer.Record([row.SyncKey], peer.Next(Vv(row.VvJson)),
            PropertyCodec.Write(content with { Nodes = [content.Nodes[0]] }), row.OrderKey)));
        CollectionAssert.AreEqual(new[] { "Asia" }, Tree((await PropertyContentAsync(f, KeyOf(id))).Nodes).ToArray());
        // A whole-row writer puts them back; then the person puts something else under Europe.
        await PutPropertyAsync(f, id, "Places", PropertyType.Multilevel, synced);
        await f.RefreshAsync();
        Assert.IsTrue((await f.RowAsync(KeyOf(id), Properties)).PublishHeld, "the lost-update guard held it (§6.5)");
        await PutPropertyAsync(f, id, "Places", PropertyType.Multilevel, moved
            ? Places(Node("europe", "Europe", Node("paris", "Paris"), Node("asia", "Asia")))
            : Places(Node("asia", "Asia"), Node("europe", "Europe", Node("paris", "Paris"), Node("france", "France"))));
        await f.RefreshAsync();
        var item = (await f.OpenItemsAsync()).Single(i => i.Type == DataSyncInboxItemType.SuspectedLostUpdate);

        await f.ResolveAsync(item, DataSyncInboxAction.Reapply);

        CollectionAssert.AreEqual(moved ? new[] { "Europe", "Europe/Asia" } : new[] { "Asia", "Europe", "Europe/France" },
            Tree((await PropertyContentAsync(f, KeyOf(id))).Nodes).ToArray(),
            "Paris is removed again; Europe stays with what the person put under it");
        Assert.IsFalse((await f.RowAsync(KeyOf(id), Properties)).PublishHeld);
        Assert.AreEqual(0, (await f.OpenItemsAsync()).Count);
    }
}
