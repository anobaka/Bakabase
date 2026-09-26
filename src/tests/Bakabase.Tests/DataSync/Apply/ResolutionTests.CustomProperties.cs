using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Tests.DataSync.CustomProperties;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.Apply.DataSyncApplyFixture;

namespace Bakabase.Tests.DataSync.Apply;

/// <summary>
/// §8.5.6 Convert over the real custom property kind: <c>ChangeType</c> rebuilds the options from the values with fresh
/// ids (F73), and whatever data sync keeps by local option id follows them — a child held because the peer deleted it
/// while it is in use here stays withheld and its question still applies; a child kept on this device only stays so.
/// </summary>
public partial class ResolutionTests
{
    private const string RockId = "00000000-0000-4000-8000-00000000c0a1";
    private const string PopId = "00000000-0000-4000-8000-00000000c0a2";

    private static CustomPropertyContentV1 Genre(PropertyType type, params (string Uuid, string Label)[] choices) => new()
    {
        Name = "Genre", Type = type, Choices = choices.Select(c => new CustomPropertyChoiceV1(c.Uuid, c.Label, null)).ToList(),
    };

    /// <summary>
    /// The peer created single choice Genre (Rock, Pop) here, resource 1 uses Pop, and the peer deleted Pop: Pop is
    /// held with its <c>ChildDeletedInUse</c> question. Then the peer changed the type to multiple choice, which waits
    /// as a <c>TypeChange</c> question.
    /// </summary>
    private static async Task<(DataSyncApplyFixture F, DataSyncLinkDbModel Link, string LocalKey, string Key)>
        HeldChildThenTypeChangeAsync(bool keepHereOnly)
    {
        var f = await CreateAsync(customProperties: true);
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer, DataSyncLinkMode.TwoWay, true, Properties);
        var codec = CustomPropertyCodec.Instance;
        var key = SyncKey.New().Value;
        var v1 = peer.Next();
        await f.ApplyAsync(link, peer,
            (Properties, peer.Record([key], v1, codec.Write(Genre(PropertyType.SingleChoice, (RockId, "Rock"), (PopId, "Pop"))), "a0")));
        var property = (await f.CustomProperties.GetAll()).Single(p => p.Name == "Genre");
        var localKey = property.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await f.Services.GetRequiredService<ICustomPropertyValueService>().AddDbModelRange([new CustomPropertyValueDbModel
        {
            ResourceId = 1, PropertyId = property.Id, Scope = (int) PropertyValueScope.Manual,
            Value = PopId.SerializeAsStandardValue(PropertyType.SingleChoice.GetDbValueType()),
        }]);

        var v2 = peer.Next(v1);
        await f.ApplyAsync(link, peer,
            (Properties, peer.Record([key], v2, codec.Write(Genre(PropertyType.SingleChoice, (RockId, "Rock"))), "a0")));
        var held = (await f.OpenItemsAsync()).Single(i => i.Type == DataSyncInboxItemType.ChildDeletedInUse);
        Assert.AreEqual("choice:" + PopId, held.SubjectPath);
        if (keepHereOnly)
        {
            await f.ResolveAsync(held, DataSyncInboxAction.KeepHereOnly);
            CollectionAssert.AreEqual(new[] { PopId },
                DataSyncStoredJson.ReadOverlay((await f.RowAsync(localKey, Properties)).OverlayJson).LocalOnlyChildren.ToArray());
        }

        var v3 = peer.Next(v2);
        await f.ApplyAsync(link, peer,
            (Properties, peer.Record([key], v3, codec.Write(Genre(PropertyType.MultipleChoice, (RockId, "Rock"))), "a0")));
        Assert.AreEqual(1, (await f.OpenItemsAsync()).Count(i => i.Type == DataSyncInboxItemType.TypeChange));
        return (f, link, localKey, key);
    }

    private static async Task<(CustomPropertyContentV1 Content, DataSyncOverlay Overlay, string Published)> ConvertedAsync(
        DataSyncApplyFixture f, string localKey)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var kind = scope.ServiceProvider.GetServices<IDataSyncKind>()
            .Single(k => k.Codec.Descriptor.Kind == DataSyncKindIds.CustomProperty);
        var local = (await kind.ReadAsync([localKey], default)).Single();
        var row = await f.RowAsync(localKey, Properties);
        var overlay = DataSyncStoredJson.ReadOverlay(row.OverlayJson);
        // What the entity publishes, as Refresh and the feed compute it.
        var published = DataSyncEntityForms.Evaluate(kind.Codec, local, overlay, row.ChildrenLocal, row.OrderKey,
            DataSyncEntityForms.ReadUnknown(row.UnknownJson)).Published;
        return (CustomPropertyCodec.Instance.ReadLocal(local.Content), overlay, published.Content!.ToJsonString());
    }

    [TestMethod]
    public async Task Convert_keeps_a_held_child_withheld_under_its_new_id_and_its_question_still_applies()
    {
        using var newtonsoft = NewtonsoftDefaults.UseApp();
        var (f, link, localKey, key) = await HeldChildThenTypeChangeAsync(keepHereOnly: false);
        var held = (await f.OpenItemsAsync()).Single(i => i.Type == DataSyncInboxItemType.ChildDeletedInUse);

        await f.ResolveAsync((await f.OpenItemsAsync()).Single(i => i.Type == DataSyncInboxItemType.TypeChange),
            DataSyncInboxAction.Convert);

        var (content, overlay, published) = await ConvertedAsync(f, localKey);
        Assert.AreEqual(PropertyType.MultipleChoice, content.Type);
        var pop = content.Choices.Single(c => c.Label == "Pop");
        Assert.AreNotEqual(PopId, pop.Uuid, "ChangeType rebuilt the options with fresh ids (F73)");
        CollectionAssert.AreEqual(new[] { (pop.Uuid!, link.Id) },
            overlay.HeldChildren.Select(h => (h.ChildId, h.LinkId)).ToArray(), "still held, under its new id");
        StringAssert.Contains(published, "Rock");
        Assert.IsFalse(published.Contains("Pop", StringComparison.Ordinal),
            "never published again: the peer's deletion stands until someone decides");
        var peerBase = (await f.BasesAsync(link.Id)).Single(b => b.SyncKey == key);
        Assert.AreEqual(pop.Uuid, DataSyncStoredJson.ReadChildMap(peerBase.ChildMapJson)[PopId],
            "the peer's Pop still leads to the hold");

        // The question still stands, and applies: Delete here removes the child under its new id.
        var open = (await f.OpenItemsAsync()).Single(i => i.Type == DataSyncInboxItemType.ChildDeletedInUse);
        Assert.AreEqual(held.Id, open.Id);
        Assert.IsNotNull(await f.ResolveAsync(open, DataSyncInboxAction.DeleteHere));
        (content, overlay, _) = await ConvertedAsync(f, localKey);
        CollectionAssert.AreEqual(new[] { "Rock" }, content.Choices.Select(c => c.Label).ToArray());
        Assert.AreEqual(0, overlay.HeldChildren.Count);
        var closed = (await f.ItemsAsync()).Single(i => i.Id == held.Id);
        Assert.AreEqual((DataSyncInboxClosure.ResolvedHere, (DataSyncInboxAction?) DataSyncInboxAction.DeleteHere),
            (closed.Closure, closed.Action));
    }

    [TestMethod]
    public async Task Convert_keeps_a_child_kept_on_this_device_only_so_under_its_new_id()
    {
        using var newtonsoft = NewtonsoftDefaults.UseApp();
        var (f, _, localKey, _) = await HeldChildThenTypeChangeAsync(keepHereOnly: true);

        await f.ResolveAsync((await f.OpenItemsAsync()).Single(i => i.Type == DataSyncInboxItemType.TypeChange),
            DataSyncInboxAction.Convert);

        var (content, overlay, published) = await ConvertedAsync(f, localKey);
        var pop = content.Choices.Single(c => c.Label == "Pop");
        Assert.AreNotEqual(PopId, pop.Uuid);
        CollectionAssert.AreEqual(new[] { pop.Uuid }, overlay.LocalOnlyChildren.ToArray());
        Assert.IsFalse(published.Contains("Pop", StringComparison.Ordinal), "still kept on this device only");
    }

    /// <summary>
    /// A held child a writer outside data sync removed: its question closes <c>Superseded</c> and the hold is released,
    /// instead of a decision recorded as applied that wrote nothing.
    /// </summary>
    [TestMethod]
    public async Task A_question_about_a_held_child_that_is_gone_closes_superseded_and_releases_the_hold()
    {
        var (key, localKey, v1) = await SyncedFromPeerAsync();
        _f.Kind.Use(localKey, "a", 30);
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], _peer.Next(v1), Content("Genre", ("b", "Jazz")), "a0")));
        var item = await SingleOpenAsync(DataSyncInboxItemType.ChildDeletedInUse);
        // The property's own editor rebuilt the options: Rock is there under another id, and nothing holds it.
        _f.Kind.Definitions[localKey] = Content("Genre", ("a2", "Rock"), ("b", "Jazz"));
        _f.Kind.Usage.Remove(localKey);

        var logId = await _f.ResolveAsync(item, DataSyncInboxAction.DeleteHere);

        Assert.IsNull(logId, "nothing was decided");
        CollectionAssert.AreEqual(new[] { "a2", "b" }, _f.Kind[localKey].Children.Select(c => c.Id).ToArray(),
            "nothing deleted");
        var closed = (await _f.ItemsAsync()).Single(i => i.Id == item.Id);
        Assert.AreEqual((DataSyncInboxClosure.Superseded, (DataSyncInboxAction?) null), (closed.Closure, closed.Action));
        Assert.AreEqual(0, DataSyncStoredJson.ReadOverlay((await _f.RowAsync(localKey)).OverlayJson).HeldChildren.Count);
    }
}
