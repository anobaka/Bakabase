using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Refs;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.DataSync;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests.DataSync.CustomProperties;

/// <summary>
/// The <c>customProperty</c> adapter over the real services and a real SQLite database (v3.1
/// <c>CustomPropertyDataSyncKindTests</c>, §2.3, §3.3, §3.7, §8.10.5, §8.10.6 here): reading every type, local content
/// kept unvalidated (B3), unreadable rows, the operations of a batch, placement, usage, undo's pre-images, the caches
/// after a rollback, the search index, and the data sync row of the write-path matrix (an update keeps
/// <c>CreatedAt</c> and <c>Order</c>).
/// </summary>
[TestClass]
public class CustomPropertyDataSyncKindTests
{
    private static readonly DateTime KnownCreatedAt = new DateTime(2021, 3, 4, 5, 6, 7, 890).AddTicks(1234);

    private IServiceProvider _sp = null!;
    private RecordingSearchIndex _index = null!;
    private IDisposable? _newtonsoft;

    private IDataSyncKind Kind => _sp.GetServices<IDataSyncKind>()
        .Single(k => k.Codec.Descriptor.Kind == DataSyncKindIds.CustomProperty);

    private IDataSyncKindCodec Codec => Kind.Codec;
    private ICustomPropertyService Properties => _sp.GetRequiredService<ICustomPropertyService>();
    private ICustomPropertyValueService Values => _sp.GetRequiredService<ICustomPropertyValueService>();

    /// <remarks>Options are stored as the app stores them: under its Newtonsoft defaults (camelCase, nulls ignored).</remarks>
    [TestInitialize]
    public async Task Setup()
    {
        _newtonsoft = NewtonsoftDefaults.UseApp();
        _index = new RecordingSearchIndex();
        _sp = await BuildProviderAsync(_index);
    }

    [TestCleanup]
    public void Cleanup() => _newtonsoft?.Dispose();

    private static Task<IServiceProvider> BuildProviderAsync(RecordingSearchIndex index) =>
        TestServiceBuilder.BuildServiceProvider(s => s.AddSingleton<IResourceSearchIndexService>(index));

    // ---- registration and reading ----------------------------------------------------------------

    [TestMethod]
    public void TheKindIsRegisteredOnce_WithTheCustomPropertyCodec()
    {
        var kinds = _sp.GetServices<IDataSyncKind>().Where(k => k.Codec.Descriptor.Kind == DataSyncKindIds.CustomProperty)
            .ToList();
        Assert.AreEqual(1, kinds.Count);
        Assert.IsInstanceOfType<CustomPropertyCodec>(kinds[0].Codec);
        Assert.IsTrue(kinds[0].Codec.Descriptor.HasOrder);
        Assert.AreSame(Kind, Kind, "scoped: one adapter per scope");
    }

    [TestMethod]
    public async Task ReadMapsEveryTypeOfTheFixture_InLocalOrder()
    {
        var seeded = await DataSyncFixture.SeedCustomPropertiesAsync(_sp);
        Assert.IsTrue(Enum.GetValues<PropertyType>().All(t => seeded.Any(p => p.Type == t)), "the fixture covers every type");
        var ids = seeded.Select(p => p.Id).ToArray();
        await Properties.Sort(ids.Reverse().ToArray());

        var entities = await Kind.ReadAsync(null, CancellationToken.None);
        CollectionAssert.AreEqual(ids.Reverse().Select(Key).ToArray(), entities.Select(e => e.LocalKey).ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(0, ids.Length).ToArray(), entities.Select(e => e.Position).ToArray());
        CollectionAssert.AreEqual(entities.Select(e => e.LocalKey).ToArray(),
            (await Kind.ReadOrderAsync(CancellationToken.None)).ToArray());
        foreach (var entity in entities)
        {
            Assert.IsFalse(entity.Unreadable, entity.LocalKey);
            // The codec's canonical form, byte for byte (Write(ReadLocal(x)) == x).
            Assert.AreEqual(Canon(entity.Content), Canon(Codec.Write(Codec.ReadLocal(entity.Content))));
            // What a reader would accept: nothing the fixture holds is held at source.
            Assert.IsNull(Codec.Publish(Codec.ReadLocal(entity.Content), DataSyncOverlay.None, false).Held, entity.LocalKey);
            var row = (await Properties.GetAllDbModels(r => r.Id == int.Parse(entity.LocalKey))).Single();
            Assert.AreEqual(row.CreatedAt.Ticks.ToString(), entity.Fingerprint);
        }

        // IgnoreCase switched on after the fact: the stored case variant is kept (F72), and so is the default order.
        var genre = Content(entities, seeded, DataSyncFixture.GenreName);
        Assert.AreEqual("""
            {"choices":[{"color":"#3e63dd","label":"Action","uuid":"00000000-0000-4000-8000-000000000101"},{"label":"Drama","uuid":"00000000-0000-4000-8000-000000000102"},{"label":"action","uuid":"00000000-0000-4000-8000-000000000103"}],"defaultValue":[{"label":"Drama","uuid":"00000000-0000-4000-8000-000000000102"},{"label":"Action","uuid":"00000000-0000-4000-8000-000000000101"}],"ignoreCase":true,"name":"Genre","type":"MultipleChoice"}
            """, Canon(genre));
        // A tag written with the group "" is stored with none.
        var studio = Content(entities, seeded, DataSyncFixture.StudioTagsName);
        Assert.AreEqual("""
            {"ignoreCase":false,"name":"Studio tags","tags":[{"color":"#3e63dd","group":"Studio","name":"Kyoto","uuid":"00000000-0000-4000-8000-000000000301"},{"name":"Isekai","uuid":"00000000-0000-4000-8000-000000000302"},{"name":"Mecha","uuid":"00000000-0000-4000-8000-000000000304"}],"type":"Tags"}
            """, Canon(studio));
        var region = Content(entities, seeded, DataSyncFixture.RegionName);
        StringAssert.StartsWith(Canon(region), """{"defaultValue":[{"path":["Asia","Japan","Kyoto"],"uuid":"00000000-0000-4000-8000-000000000403"}],"ignoreCase":true""");
        StringAssert.Contains(Canon(region), "\"settings\":{\"valueIsSingleton\":true}");
    }

    [TestMethod]
    public async Task APropertyWithoutOptions_ReadsAsItsTypesDefaults()
    {
        var rating = await AddAsync("Rating", PropertyType.Rating);
        var tags = await AddAsync("Tags", PropertyType.Tags);
        var tree = await AddAsync("Tree", PropertyType.Multilevel);
        Assert.IsNull((await StoredRowAsync(rating)).Options);

        Assert.AreEqual("""{"name":"Rating","settings":{"maxValue":5},"type":"Rating"}""", Canon((await ReadOneAsync(rating)).Content));
        Assert.AreEqual("""{"ignoreCase":false,"name":"Tags","type":"Tags"}""", Canon((await ReadOneAsync(tags)).Content));
        Assert.AreEqual("""{"ignoreCase":false,"name":"Tree","settings":{"valueIsSingleton":false},"type":"Multilevel"}""",
            Canon((await ReadOneAsync(tree)).Content));
    }

    [TestMethod]
    public async Task ReadWithKeys_ReturnsThoseInLocalOrder_WithTheirPositionInTheKind()
    {
        var a = await AddAsync("A", PropertyType.SingleLineText);
        var b = await AddAsync("B", PropertyType.Number);
        var c = await AddAsync("C", PropertyType.Boolean);
        await Properties.Sort([c, a, b]);

        var read = await Kind.ReadAsync([Key(b), Key(c), "999", "not-a-key"], CancellationToken.None);
        CollectionAssert.AreEqual(new[] { Key(c), Key(b) }, read.Select(e => e.LocalKey).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 2 }, read.Select(e => e.Position).ToArray());
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task LocalOptionsAreReadUnvalidated_AndAnUpdateWritesThemBack(bool ignoreCase, bool duplicateWithoutId)
    {
        // v3.1 B3, §3.3: a 200-character id, a duplicate id, a control character, a null label and an option without
        // an id are all local data: read as they are, kept by a merge and written back by the update — under IgnoreCase
        // an option without an id that is a case variant of another too, which the service's normalizer would fold
        // away (it reads it with a fresh random id each time), so the update is stored as it is.
        var longUuid = new string('f', 200);
        var id = await AddAsync("Genre", PropertyType.MultipleChoice);
        var choices = new List<object>
        {
            new { Value = longUuid, Label = "Long", Color = (string?) null },
            new { Value = "dup", Label = "A\u0000B", Color = "#fff" },
            new { Value = "dup", Label = (string?) null, Color = (string?) null },
            new { Value = (string?) null, Label = "No id", Color = (string?) null },
            new { Value = "keep", Label = "Keep", Color = (string?) null },
        };
        if (duplicateWithoutId) choices.Add(new { Value = (string?) null, Label = "LONG", Color = (string?) null });
        await SetRawOptionsAsync(id, JsonConvert.SerializeObject(new
        {
            IgnoreCase = ignoreCase, Choices = choices, DefaultValue = new[] { "keep" },
        }));

        var local = (await Kind.ReadAsync([Key(id)], CancellationToken.None)).Single();
        Assert.IsFalse(local.Unreadable);
        var content = (CustomPropertyContentV1) Codec.ReadLocal(local.Content);
        CollectionAssert.AreEqual(new[] { longUuid, "dup", "dup", null, "keep", null }.Take(choices.Count).ToArray(),
            content.Choices.Select(c => c.Uuid).ToArray());
        CollectionAssert.AreEqual(new[] { "Long", "A\u0000B", "", "No id", "Keep", "LONG" }.Take(choices.Count).ToArray(),
            content.Choices.Select(c => c.Label).ToArray());
        // The reader would drop what is invalid: that limits what travels, never what is kept (§3.5).
        Assert.IsTrue(Codec.Publish(content, DataSyncOverlay.None, false).ChildrenWithheld >= 3);

        var merged = content with
        {
            Name = "Genres",
            Choices = content.Choices.Select(c => c.Uuid == "keep" ? c with { Label = "Kept", Color = "#000" } : c)
                .Append(new CustomPropertyChoiceV1("new", "New", null)).ToArray(),
            DefaultValue = [OptionRef.Choice("keep", "Kept")],
        };
        var outcome = await ApplyAsync(Update("u", id, local, merged));
        Assert.AreEqual(0, outcome.ChangedDuringApplyItemIds.Count);

        var after = (await Kind.ReadAsync([Key(id)], CancellationToken.None)).Single();
        // Echo prevention depends on it: what is read back is exactly what was merged.
        Assert.AreEqual(Canon(Codec.Write(merged)), Canon(after.Content));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AMergeKeepsAChoiceWithoutAnIdThatIsACaseVariantOfAnother(bool withoutIdFirst)
    {
        // §3.3, §8.5.4 step 0: the peer only renamed the property. Merge3 keeps the choice without an id (it takes no
        // part in folding), and the update stores exactly that.
        var id = await AddAsync("Genre", PropertyType.MultipleChoice);
        var withId = new { Value = "keep", Label = "Action" };
        var withoutId = new { Label = "action" };
        await SetRawOptionsAsync(id, JsonConvert.SerializeObject(new
        {
            IgnoreCase = true, Choices = withoutIdFirst ? new object[] { withoutId, withId } : [withId, withoutId],
        }));
        var local = await ReadOneAsync(id);
        var content = (CustomPropertyContentV1) Codec.ReadLocal(local.Content);
        Assert.AreEqual(2, content.Choices.Count);

        var peer = content with { Name = "Genres", Choices = content.Choices.Where(c => c.Uuid is not null).ToArray() };
        var result = Codec.Merge3(new DataSyncMerge3Input(content with { Choices = peer.Choices }, content,
            DataSyncOverlay.None, peer, DataSyncMerge3Mode.ThreeWay, new Dictionary<string, string> { ["keep"] = "keep" },
            false, false, DataSyncLinkMode.TwoWay, true, DataSyncMergeSide.Local, new Dictionary<string, int>(),
            DataSyncChildDeletionMode.Normal));
        var merged = (CustomPropertyContentV1) result.Merged;
        Assert.AreEqual("Genres", merged.Name);
        CollectionAssert.AreEqual(content.Choices.ToArray(), merged.Choices.ToArray(), "both choices, as stored");

        await ApplyAsync(Update("u", id, local, merged));
        var after = await ReadOneAsync(id);
        Assert.AreEqual(Canon(Codec.Write(merged)), Canon(after.Content), "read back exactly as merged");
        CollectionAssert.AreEqual(content.Choices.ToArray(),
            ((CustomPropertyContentV1) Codec.ReadLocal(after.Content)).Choices.ToArray());
    }

    [TestMethod]
    public async Task UnreadableOptions_AreRecordedAndNeverWritten()
    {
        var id = await AddAsync("Broken", PropertyType.MultipleChoice);
        await SetRawOptionsAsync(id, "{broken");

        var local = (await Kind.ReadAsync([Key(id)], CancellationToken.None)).Single();
        Assert.IsTrue(local.Unreadable);
        Assert.AreEqual("""{"name":"Broken","type":"MultipleChoice"}""", Canon(local.Content));
        Assert.IsTrue((await Kind.ReadRawHashesAsync(CancellationToken.None)).ContainsKey(Key(id)));

        var hash = ContentHash.Of(local.Content);
        var merged = (CustomPropertyContentV1) Codec.ReadLocal(local.Content) with { Name = "Fixed" };
        var outcome = await ApplyAsync(
            new UpdateEntityOperation("u", Key(id), hash, Codec.Write(merged), EntityKeys.None, [], []),
            new ChangeSubtypeOperation("t", Key(id), hash, nameof(PropertyType.SingleChoice)),
            new DeleteEntityOperation("d", Key(id), hash));
        CollectionAssert.AreEquivalent(new[] { "u", "t", "d" }, outcome.ChangedDuringApplyItemIds.ToArray());

        var row = await StoredRowAsync(id);
        Assert.AreEqual("Broken", row.Name);
        Assert.AreEqual(PropertyType.MultipleChoice, row.Type);
        Assert.AreEqual("{broken", row.Options);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            Kind.RestoreAsync(Key(id), new JsonObject { ["name"] = "X", ["type"] = (int) PropertyType.MultipleChoice },
                CancellationToken.None));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            Kind.ChangeSubtypeAsync(Key(id), nameof(PropertyType.SingleChoice), CancellationToken.None));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            Kind.PreviewSubtypeChangeAsync(Key(id), nameof(PropertyType.SingleChoice), CancellationToken.None));
    }

    [TestMethod]
    public async Task RowsOfATypeThisBuildDoesNotKnow_AreNeverReadButNotTakenForDeleted()
    {
        var a = await AddAsync("A", PropertyType.SingleLineText);
        var future = await AddAsync("Future", PropertyType.SingleLineText);
        var b = await AddAsync("B", PropertyType.SingleLineText);
        await Properties.Sort([a, future, b]);
        await Properties.UpdateRange([(await StoredRowAsync(future)) with { Type = (PropertyType) 99 }]);

        CollectionAssert.AreEqual(new[] { Key(a), Key(b) }, (await Kind.ReadOrderAsync(CancellationToken.None)).ToArray());
        CollectionAssert.AreEqual(new[] { Key(a), Key(b) },
            (await Kind.ReadAsync(null, CancellationToken.None)).Select(e => e.LocalKey).ToArray());
        Assert.AreEqual(0, (await Kind.ReadAsync([Key(future)], CancellationToken.None)).Count);
        // Refresh tombstones what has no raw hash: a downgrade must not publish a deletion of it.
        var raw = await Kind.ReadRawHashesAsync(CancellationToken.None);
        Assert.IsTrue(raw.ContainsKey(Key(future)));
        Assert.AreEqual(3, raw.Count);
        var usage = await Kind.GetUsageAsync(new Dictionary<string, IReadOnlyCollection<string>> { [Key(future)] = [] },
            CancellationToken.None);
        Assert.AreEqual(0, usage[Key(future)].ValueCount);
        await Assert.ThrowsExceptionAsync<KeyNotFoundException>(() => Kind.DeleteAsync(Key(future), CancellationToken.None));
        Assert.AreEqual((PropertyType) 99, (await StoredRowAsync(future)).Type);

        // Placing the synced ones never moves it out of its slot.
        await Kind.ApplyOrderAsync([Key(b), Key(a)], CancellationToken.None);
        Assert.AreEqual(1, (await StoredRowAsync(future)).Order);
        CollectionAssert.AreEqual(new[] { Key(b), Key(a) }, (await Kind.ReadOrderAsync(CancellationToken.None)).ToArray());
    }

    [TestMethod]
    public async Task RawHashes_MoveWithTheRowsContentAndCreationTime_NotWithItsOrder()
    {
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "A")));
        await AddAsync("Other", PropertyType.SingleLineText);
        async Task<string> Raw() => (await Kind.ReadRawHashesAsync(CancellationToken.None))[Key(id)];

        var initial = await Raw();
        Assert.AreEqual(initial, await Raw(), "stable");
        await Properties.SetOrders(new Dictionary<int, int> { [id] = 7 });
        Assert.AreEqual(initial, await Raw(), "order is not content");

        await Properties.UpdateRange([(await StoredRowAsync(id)) with { CreatedAt = KnownCreatedAt }]);
        var reused = await Raw();
        Assert.AreNotEqual(initial, reused, "a new creation time is a reused id (§5.5)");

        await Properties.Put(id, new CustomPropertyAddOrPutDto { Name = "Genres", Type = PropertyType.MultipleChoice,
            Options = (await StoredRowAsync(id)).Options });
        var renamed = await Raw();
        Assert.AreNotEqual(reused, renamed);

        await SetRawOptionsAsync(id, (await StoredRowAsync(id)).Options!.Replace("\"A\"", "\"B\""));
        Assert.AreNotEqual(renamed, await Raw());
    }

    [TestMethod]
    public async Task TheFingerprintIsTheCreationTime_AndUnknownWhenItWasReset()
    {
        var id = await AddAsync("P", PropertyType.SingleLineText);
        await Properties.UpdateRange([(await StoredRowAsync(id)) with { CreatedAt = KnownCreatedAt }]);
        Assert.AreEqual(KnownCreatedAt.Ticks.ToString(), (await ReadOneAsync(id)).Fingerprint);

        await Properties.UpdateRange([(await StoredRowAsync(id)) with { CreatedAt = default }]);
        Assert.IsNull((await ReadOneAsync(id)).Fingerprint, "reset by the old ChangeType bug: unknown (v3.1 §5.4)");
    }

    // ---- the operations of a batch -----------------------------------------------------------------

    [TestMethod]
    public async Task Create_KeepsTheIdsOfEveryKindOfOption()
    {
        CustomPropertyContentV1[] contents =
        [
            new()
            {
                Name = "Genre", Type = PropertyType.SingleChoice, IgnoreCase = true,
                Choices = [new("c1", "Action", "#e5484d"), new("c2", "Drama", null)],
                DefaultValue = [OptionRef.Choice("c2", "Drama")],
            },
            new()
            {
                Name = "Studio", Type = PropertyType.Tags, IgnoreCase = false,
                Tags = [new("t1", "Studio", "Kyoto", "#3e63dd"), new("t2", null, "Isekai", null)],
            },
            new()
            {
                Name = "Region", Type = PropertyType.Multilevel, IgnoreCase = true,
                Settings = new CustomPropertySettingsV1 { ValueIsSingleton = false },
                Nodes = [new("n1", "Asia", null) { Children = [new("n2", "Japan", "#30a46c")] }, new("n3", "Europe", null)],
                DefaultValue = [OptionRef.Node("n2", ["Asia", "Japan"])],
            },
            new() { Name = "Score", Type = PropertyType.Rating, Settings = new CustomPropertySettingsV1 { MaxValue = 10 } },
        ];
        var before = DateTime.Now;
        var outcome = await ApplyAsync(contents.Select((c, i) => (ApplyOperation) Create($"c{i}", c)).ToArray());

        Assert.AreEqual(contents.Length, outcome.CreatedLocalKeysByItemId.Count);
        for (var i = 0; i < contents.Length; i++)
        {
            var localKey = outcome.CreatedLocalKeysByItemId[$"c{i}"];
            var entity = (await Kind.ReadAsync([localKey], CancellationToken.None)).Single();
            Assert.AreEqual(Canon(Codec.Write(contents[i])), Canon(entity.Content), contents[i].Name);
            var row = await StoredRowAsync(int.Parse(localKey));
            Assert.IsTrue(row.CreatedAt >= before.AddSeconds(-1), "a create is a new local property: CreatedAt is now");
        }
    }

    [TestMethod]
    public async Task APeerCreateIsFoldedLikeAnyNewProperty_ARecreateIsStoredAsCaptured()
    {
        var given = new CustomPropertyContentV1
        {
            Name = "Genre", Type = PropertyType.MultipleChoice, IgnoreCase = true,
            Choices = [new("a", "Action", null), new("b", "action", null), new("c", "Drama", null)],
            DefaultValue = [OptionRef.Choice("b", "action")],
        };
        var prepared = (CustomPropertyContentV1) Codec.PrepareCreate(given, null).Content;
        var outcome = await ApplyAsync(Create("peer", given), Create("prepared", prepared),
            Create("undo", given) with { FromPreImage = true }, Create("again", given));
        Assert.AreEqual(4, outcome.CreatedLocalKeysByItemId.Values.Distinct().Count());

        // A peer's create goes through the service's AddRange (§14.2): content PrepareCreate did not fold is folded as
        // any new property is, and PrepareCreate's content (v3.1 H4) is stored as given — the two folds agree.
        foreach (var itemId in new[] { "peer", "prepared", "again" })
        {
            var created = await Kind.ReadAsync([outcome.CreatedLocalKeysByItemId[itemId]], CancellationToken.None);
            Assert.AreEqual(Canon(Codec.Write(prepared)), Canon(created.Single().Content), itemId);
        }

        // A re-created property (undo of a deletion, §8.11) keeps every captured option id: case-variant duplicates
        // under IgnoreCase included (F72).
        var recreated = await Kind.ReadAsync([outcome.CreatedLocalKeysByItemId["undo"]], CancellationToken.None);
        Assert.AreEqual(Canon(Codec.Write(given)), Canon(recreated.Single().Content));

        // Results in input order across the calls a batch is split into.
        var ids = new[] { "peer", "prepared", "undo", "again" }.Select(i => int.Parse(outcome.CreatedLocalKeysByItemId[i]))
            .ToArray();
        CollectionAssert.AreEqual(ids.Order().ToArray(), ids);
    }

    /// <summary>The data sync row of the write-path matrix (v3.1 §11.3, B0).</summary>
    [TestMethod]
    public async Task Update_KeepsCreatedAtOrderAndLocalIds()
    {
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "Action"), ("b", "Drama")));
        var others = new List<int>();
        for (var i = 0; i < 5; i++) others.Add(await AddAsync($"Other {i}", PropertyType.Boolean));
        await Properties.UpdateRange([(await StoredRowAsync(id)) with { CreatedAt = KnownCreatedAt }]);
        await Properties.Sort([..others, id]);
        Assert.AreEqual(5, (await StoredRowAsync(id)).Order);

        var local = await ReadOneAsync(id);
        var content = (CustomPropertyContentV1) Codec.ReadLocal(local.Content);
        var merged = content with
        {
            Name = "Genres",
            Choices = [content.Choices[0] with { Label = "Adventure" }, content.Choices[1], new("c", "Comedy", "#fff")],
        };
        await ApplyAsync(Update("u", id, local, merged));

        var row = await StoredRowAsync(id);
        Assert.AreEqual(KnownCreatedAt.Ticks, row.CreatedAt.Ticks, "CreatedAt is the ID-reuse fingerprint");
        Assert.AreEqual(5, row.Order, "Order is local");
        Assert.AreEqual("Genres", row.Name);
        var viewed = await Properties.GetByKey(id);
        Assert.AreEqual(KnownCreatedAt.Ticks, viewed.CreatedAt.Ticks);
        Assert.AreEqual(5, viewed.Order);
        var options = (MultipleChoicePropertyOptions) viewed.Options!;
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, options.Choices!.Select(c => c.Value).ToArray());
        CollectionAssert.AreEqual(new[] { "Adventure", "Drama", "Comedy" }, options.Choices!.Select(c => c.Label).ToArray());
    }

    [TestMethod]
    public async Task AnOperationWhoseEntityChangedMeanwhile_IsChangedDuringApply_AndWritesNothing()
    {
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "Action")));
        var stale = await ReadOneAsync(id);
        await Properties.Put(id, new CustomPropertyAddOrPutDto
            { Name = "Renamed here", Type = PropertyType.MultipleChoice, Options = (await StoredRowAsync(id)).Options });
        var merged = (CustomPropertyContentV1) Codec.ReadLocal(stale.Content) with { Name = "From the peer" };

        var outcome = await ApplyAsync(
            Update("u", id, stale, merged),
            new DeleteEntityOperation("d", Key(id), ContentHash.Of(stale.Content)),
            new ChangeSubtypeOperation("t", Key(id), ContentHash.Of(stale.Content), nameof(PropertyType.SingleChoice)),
            new UpdateEntityOperation("gone", "424242", ContentHash.Of(stale.Content), Codec.Write(merged), EntityKeys.None, [], []));

        CollectionAssert.AreEquivalent(new[] { "u", "d", "t", "gone" }, outcome.ChangedDuringApplyItemIds.ToArray());
        var row = await StoredRowAsync(id);
        Assert.AreEqual("Renamed here", row.Name);
        Assert.AreEqual(PropertyType.MultipleChoice, row.Type);
    }

    [TestMethod]
    public async Task AnEntityTheBatchWrote_IsJudgedByWhatItWrote()
    {
        // An update, then a delete of the same property: the delete's expected hash is checked against the row the
        // update wrote, never the batch's first read of it.
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "Action")));
        var local = await ReadOneAsync(id);
        var merged = (CustomPropertyContentV1) Codec.ReadLocal(local.Content) with { Name = "Genres" };
        var afterUpdate = ContentHash.Of(Codec.Write(merged));

        var stale = await ApplyAsync(Update("u", id, local, merged),
            new DeleteEntityOperation("d", Key(id), ContentHash.Of(local.Content)));
        CollectionAssert.AreEqual(new[] { "d" }, stale.ChangedDuringApplyItemIds.ToArray());
        Assert.AreEqual("Genres", (await StoredRowAsync(id)).Name, "the update applied, the delete did not");

        var current = await ReadOneAsync(id);
        var renamed = (CustomPropertyContentV1) Codec.ReadLocal(current.Content) with { Name = "Moods" };
        var fresh = await ApplyAsync(Update("u2", id, current, renamed),
            new ChangeSubtypeOperation("t", Key(id), ContentHash.Of(Codec.Write(renamed)), nameof(PropertyType.SingleChoice)),
            new DeleteEntityOperation("d2", Key(id), afterUpdate));
        CollectionAssert.AreEqual(new[] { "d2" }, fresh.ChangedDuringApplyItemIds.ToArray(),
            "the subtype change applied to the renamed row; the delete expected the first update's");
        var row = await StoredRowAsync(id);
        Assert.AreEqual("Moods", row.Name);
        Assert.AreEqual(PropertyType.SingleChoice, row.Type);

        var converted = await ReadOneAsync(id);
        var both = await ApplyAsync(Update("u3", id, converted,
                (CustomPropertyContentV1) Codec.ReadLocal(converted.Content) with { Name = "Last" }),
            new DeleteEntityOperation("d3", Key(id),
                ContentHash.Of(Codec.Write((CustomPropertyContentV1) Codec.ReadLocal(converted.Content) with { Name = "Last" }))));
        Assert.AreEqual(0, both.ChangedDuringApplyItemIds.Count);
        Assert.AreEqual(0, (await Properties.GetAllDbModels(r => r.Id == id)).Count, "deleted after its update");
    }

    [TestMethod]
    public async Task AnUpdateNeverChangesTheType()
    {
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "Action")));
        var local = await ReadOneAsync(id);
        var merged = (CustomPropertyContentV1) Codec.ReadLocal(local.Content) with { Type = PropertyType.SingleChoice };
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => ApplyAsync(Update("u", id, local, merged)));
        Assert.AreEqual(PropertyType.MultipleChoice, (await StoredRowAsync(id)).Type);
    }

    [TestMethod]
    public async Task ABindWritesNothing_AndABatchOfAnotherKindIsRefused()
    {
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "Action")));
        var before = await StoredRowAsync(id);
        var outcome = await ApplyAsync(new BindOnlyOperation("b", Key(id), EntityKeys.None));
        Assert.AreEqual(0, outcome.CreatedLocalKeysByItemId.Count + outcome.ChangedDuringApplyItemIds.Count);
        Assert.AreEqual(before, await StoredRowAsync(id));

        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Kind.ApplyAsync(
            new ApplyBatch(DataSyncKindIds.ExtensionGroup, []), CancellationToken.None));
    }

    [TestMethod]
    public async Task Delete_RemovesThePropertyAndItsValues_AndInvalidatesTheirResources()
    {
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "Action")));
        var other = await AddAsync("Other", PropertyType.MultipleChoice, Choices(("x", "X")));
        await AddValuesAsync(id, PropertyType.MultipleChoice, (101, new List<string> { "a" }), (102, new List<string> { "a" }));
        await AddValuesAsync(other, PropertyType.MultipleChoice, (103, new List<string> { "x" }));
        var local = await ReadOneAsync(id);

        await ApplyAsync(new DeleteEntityOperation("d", Key(id), ContentHash.Of(local.Content)));

        Assert.AreEqual(0, (await Properties.GetAllDbModels(r => r.Id == id)).Count);
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            Assert.IsFalse(await db.CustomProperties.AnyAsync(p => p.Id == id));
            Assert.IsFalse(await db.CustomPropertyValues.AnyAsync(v => v.PropertyId == id));
            Assert.IsTrue(await db.CustomPropertyValues.AnyAsync(v => v.PropertyId == other));
        }

        CollectionAssert.AreEquivalent(new[] { 101, 102 }, _index.Invalidated.ToArray());
    }

    /// <summary>
    /// An automatic deletion (§8.6) was decided on "no values"; its hash covers the row, not the values. A property that
    /// has values by the time it runs is skipped as ChangedDuringApply, values and all; one without is deleted.
    /// </summary>
    [TestMethod]
    public async Task AnAutomaticDelete_OfAPropertyThatHasValues_IsChangedDuringApply()
    {
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "Action")));
        var empty = await AddAsync("Empty", PropertyType.MultipleChoice, Choices(("x", "X")));
        await AddValuesAsync(id, PropertyType.MultipleChoice, (101, new List<string> { "a" }));
        var local = await ReadOneAsync(id);
        var emptyLocal = await ReadOneAsync(empty);

        var outcome = await ApplyAsync(
            new DeleteEntityOperation("d", Key(id), ContentHash.Of(local.Content), RequireNoValues: true),
            new DeleteEntityOperation("e", Key(empty), ContentHash.Of(emptyLocal.Content), RequireNoValues: true));

        CollectionAssert.AreEquivalent(new[] { "d" }, outcome.ChangedDuringApplyItemIds.ToArray());
        Assert.AreEqual(1, (await Properties.GetAllDbModels(r => r.Id == id)).Count, "kept");
        Assert.AreEqual(1, (await Values.GetAllDbModels(v => v.PropertyId == id, false)).Count, "with its value");
        Assert.AreEqual(0, (await Properties.GetAllDbModels(r => r.Id == empty)).Count, "deleted");
    }

    [TestMethod]
    public async Task DeleteAsync_ForUndo_DeletesAndInvalidates()
    {
        var id = await AddAsync("Genre", PropertyType.Tags);
        await AddValuesAsync(id, PropertyType.Tags, (7, new List<string> { "t" }));
        await Kind.DeleteAsync(Key(id), CancellationToken.None);
        Assert.AreEqual(0, (await Properties.GetAllDbModels(r => r.Id == id)).Count);
        CollectionAssert.AreEqual(new[] { 7 }, _index.Invalidated.ToArray());
        await Assert.ThrowsExceptionAsync<KeyNotFoundException>(() => Kind.DeleteAsync(Key(id), CancellationToken.None));
    }

    [TestMethod]
    public async Task ChangeSubtype_ConvertsValues_KeepsCreatedAtAndOrder_AndInvalidatesTheirResources()
    {
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "Action"), ("c", "Comedy")));
        var first = await AddAsync("First", PropertyType.Boolean);
        await Properties.UpdateRange([(await StoredRowAsync(id)) with { CreatedAt = KnownCreatedAt }]);
        await Properties.Sort([first, id]);
        await AddValuesAsync(id, PropertyType.MultipleChoice, (201, new List<string> { "a" }),
            (202, new List<string> { "a", "c" }));
        var local = await ReadOneAsync(id);

        await ApplyAsync(new ChangeSubtypeOperation("t", Key(id), ContentHash.Of(local.Content),
            nameof(PropertyType.SingleChoice)));

        var row = await StoredRowAsync(id);
        Assert.AreEqual(PropertyType.SingleChoice, row.Type);
        Assert.AreEqual(KnownCreatedAt.Ticks, row.CreatedAt.Ticks);
        Assert.AreEqual(1, row.Order);
        var converted = (CustomPropertyContentV1) Codec.ReadLocal((await ReadOneAsync(id)).Content);
        CollectionAssert.AreEquivalent(new[] { "Action", "Action,Comedy" }, converted.Choices.Select(c => c.Label).ToArray());
        Assert.IsTrue(converted.IgnoreCase is false);
        CollectionAssert.AreEquivalent(new[] { 201, 202 }, _index.Invalidated.ToArray());
    }

    [TestMethod]
    public async Task ChangeSubtypeAsync_ToTheSameType_ChangesNothing_AndRefusesAnUnknownOne()
    {
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "Action")));
        await AddValuesAsync(id, PropertyType.MultipleChoice, (1, new List<string> { "a" }));
        var before = await StoredRowAsync(id);

        await Kind.ChangeSubtypeAsync(Key(id), nameof(PropertyType.MultipleChoice), CancellationToken.None);
        Assert.AreEqual(before, await StoredRowAsync(id), "ChangeType would rebuild the options with fresh ids (F73)");
        Assert.AreEqual(0, _index.Invalidated.Count);

        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            Kind.ChangeSubtypeAsync(Key(id), "multipleChoice", CancellationToken.None));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            Kind.ChangeSubtypeAsync(Key(id), "4", CancellationToken.None));
        await Assert.ThrowsExceptionAsync<KeyNotFoundException>(() =>
            Kind.ChangeSubtypeAsync("424242", nameof(PropertyType.SingleChoice), CancellationToken.None));
    }

    [TestMethod]
    public async Task PreviewSubtypeChange_CountsValues_Changes_AndWhatCannotBeConvertedBack()
    {
        var text = await AddAsync("Text", PropertyType.SingleLineText);
        var values = new List<(int, object)> { (1, "12"), (2, "abc") };
        values.AddRange(Enumerable.Range(0, 25).Select(i => (100 + i, (object) $"word {i}")));
        await AddValuesAsync(text, PropertyType.SingleLineText, values.ToArray());

        var toNumber = await Kind.PreviewSubtypeChangeAsync(Key(text), nameof(PropertyType.Number), CancellationToken.None);
        Assert.AreEqual(nameof(PropertyType.SingleLineText), toNumber.FromSubtype);
        Assert.AreEqual(nameof(PropertyType.Number), toNumber.ToSubtype);
        Assert.AreEqual(27, toNumber.ValueCount);
        Assert.AreEqual(26, toNumber.ChangedCount, "\"12\" stays 12");
        Assert.AreEqual(26, toNumber.LossyCount, "text that is not a number is lost");
        Assert.AreEqual(CustomPropertyDataSyncKind<BakabaseDbContext>.MaxTypeChangeSamples, toNumber.Samples.Count);
        Assert.IsTrue(toNumber.Samples.Any(s => s.From == "abc" && s.To is null or ""));

        var toMultiline = await Kind.PreviewSubtypeChangeAsync(Key(text), nameof(PropertyType.MultilineText),
            CancellationToken.None);
        Assert.AreEqual(27, toMultiline.ValueCount);
        Assert.AreEqual(0, toMultiline.ChangedCount);
        Assert.AreEqual(0, toMultiline.LossyCount);
        Assert.AreEqual(0, toMultiline.Samples.Count);

        // A list collapses into one label that displays alike and converts back into the same list; a label holding
        // the separator displays alike too, but converts back into two.
        var genre = await AddAsync("Genre", PropertyType.MultipleChoice,
            Choices(("a", "Action"), ("c", "Comedy"), ("r", "Rock, Pop")));
        await AddValuesAsync(genre, PropertyType.MultipleChoice, (1, new List<string> { "a" }),
            (2, new List<string> { "a", "c" }), (3, new List<string> { "r" }));
        var toSingle = await Kind.PreviewSubtypeChangeAsync(Key(genre), nameof(PropertyType.SingleChoice), CancellationToken.None);
        Assert.AreEqual(3, toSingle.ValueCount);
        Assert.AreEqual(0, toSingle.ChangedCount);
        Assert.AreEqual(1, toSingle.LossyCount, "lost although it displays alike");
        Assert.AreEqual(0, toSingle.Samples.Count);
        // Nothing was written.
        Assert.AreEqual(PropertyType.MultipleChoice, (await StoredRowAsync(genre)).Type);
        Assert.AreEqual(PropertyType.SingleLineText, (await StoredRowAsync(text)).Type);
    }

    // ---- order -------------------------------------------------------------------------------------

    [TestMethod]
    public async Task ApplyOrder_PlacesSyncedPropertiesInTheirSlots_AndWritesOnlyTheRowsThatMove()
    {
        var a = await AddAsync("A (synced)", PropertyType.SingleLineText);
        var l = await AddAsync("L (local)", PropertyType.SingleLineText);
        var b = await AddAsync("B (synced)", PropertyType.SingleLineText);
        var c = await AddAsync("C (synced)", PropertyType.SingleLineText);
        var m = await AddAsync("M (local)", PropertyType.SingleLineText);
        await Properties.Sort([a, l, b, c, m]);
        var rows = new List<CustomPropertyDbModel>();
        foreach (var id in new[] { a, l, b, c, m }) rows.Add(await StoredRowAsync(id));

        var (kind, recorder) = RecordingKind();
        await kind.ApplyOrderAsync([Key(c), Key(a), "424242", Key(b), Key(c)], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { Key(c), Key(l), Key(a), Key(b), Key(m) },
            (await Kind.ReadOrderAsync(CancellationToken.None)).ToArray());
        var written = recorder.SetOrdersCalls.Single();
        CollectionAssert.AreEquivalent(new[] { c, a, b }, written.Keys.ToArray(), "L and M keep their slots and rows");
        Assert.AreEqual(0, written[c]);
        Assert.AreEqual(2, written[a]);
        Assert.AreEqual(3, written[b]);
        foreach (var before in rows)
        {
            // Only Order changes, and only where it had to.
            var after = await StoredRowAsync(before.Id);
            Assert.AreEqual(before with { Order = after.Order }, after);
        }

        // Placed once, placed for good: DetectMoves then finds nothing, so there is no echo (§3.7).
        await kind.ApplyOrderAsync([Key(c), Key(a), Key(b)], CancellationToken.None);
        Assert.AreEqual(1, recorder.SetOrdersCalls.Count);
    }

    [TestMethod]
    public async Task ApplyOrder_TakesCreatesIntoTheSyncedSlots()
    {
        var a = await AddAsync("A", PropertyType.SingleLineText);
        var local = await AddAsync("Local", PropertyType.SingleLineText);
        await Properties.Sort([a, local]);
        // A create from a peer arrives with Order 0, beside the first property.
        var created = (await ApplyAsync(Create("c", new CustomPropertyContentV1 { Name = "New", Type = PropertyType.Boolean })))
            .CreatedLocalKeysByItemId["c"];
        CollectionAssert.AreEqual(new[] { Key(a), created, Key(local) },
            (await Kind.ReadOrderAsync(CancellationToken.None)).ToArray());

        await Kind.ApplyOrderAsync([Key(a), created], CancellationToken.None);
        CollectionAssert.AreEqual(new[] { Key(a), created, Key(local) },
            (await Kind.ReadOrderAsync(CancellationToken.None)).ToArray());
        await Kind.ApplyOrderAsync([created, Key(a)], CancellationToken.None);
        CollectionAssert.AreEqual(new[] { created, Key(a), Key(local) },
            (await Kind.ReadOrderAsync(CancellationToken.None)).ToArray());
        Assert.AreEqual(2, (await StoredRowAsync(local)).Order, "a local property keeps its slot");
    }

    [TestMethod]
    public async Task SetOrders_WritesTheGivenRowsOnly()
    {
        var a = await AddAsync("A", PropertyType.SingleLineText);
        var b = await AddAsync("B", PropertyType.SingleLineText);
        await Properties.UpdateRange([(await StoredRowAsync(a)) with { CreatedAt = KnownCreatedAt, Order = 3 }]);
        var bBefore = await StoredRowAsync(b);

        await Properties.SetOrders(new Dictionary<int, int> { [a] = 9, [424242] = 1 });
        await Properties.SetOrders(new Dictionary<int, int>());

        var aAfter = await StoredRowAsync(a);
        Assert.AreEqual(9, aAfter.Order);
        Assert.AreEqual(KnownCreatedAt.Ticks, aAfter.CreatedAt.Ticks);
        Assert.AreEqual(bBefore, await StoredRowAsync(b));
        Assert.AreEqual(9, (await Properties.GetByKey(a)).Order, "the cache sees it");
    }

    // ---- usage -------------------------------------------------------------------------------------

    [TestMethod]
    public async Task GetUsage_CountsDistinctResourcesPerOptionId()
    {
        var genre = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "A"), ("b", "B"), ("c", "C")));
        await AddValuesAsync(genre, PropertyType.MultipleChoice, (1, new List<string> { "a" }),
            (2, new List<string> { "a", "b" }));
        // The same resource in another scope counts once.
        await Values.AddDbModelRange([Value(genre, PropertyType.MultipleChoice, 1, new List<string> { "a" }, scope: 1000)]);
        var series = await AddAsync("Series", PropertyType.SingleChoice, Choices(("s", "S")));
        await AddValuesAsync(series, PropertyType.SingleChoice, (3, "s"), (4, "s"));
        var region = await AddAsync("Region", PropertyType.Multilevel);
        await AddValuesAsync(region, PropertyType.Multilevel, (5, new List<string> { "japan" }), (6, new List<string> { "asia" }));
        var title = await AddAsync("Title", PropertyType.SingleLineText);
        await AddValuesAsync(title, PropertyType.SingleLineText, (1, "a"));

        var usage = await Kind.GetUsageAsync(new Dictionary<string, IReadOnlyCollection<string>>
        {
            [Key(genre)] = ["a", "b", "c", "b"],
            [Key(series)] = ["s"],
            [Key(region)] = ["asia", "japan", "kyoto"],
            [Key(title)] = ["a"],
            ["424242"] = ["z"],
            [Key(genre + 1000)] = [],
        }, CancellationToken.None);

        AssertUsage(usage[Key(genre)], 3, ("a", 2), ("b", 1), ("c", 0));
        AssertUsage(usage[Key(series)], 2, ("s", 2));
        // A value names the node itself; the merge sums a subtree (§8.5.4).
        AssertUsage(usage[Key(region)], 2, ("asia", 1), ("japan", 1), ("kyoto", 0));
        AssertUsage(usage[Key(title)], 1, ("a", 0));
        AssertUsage(usage["424242"], 0, ("z", 0));
        AssertUsage(usage[Key(genre + 1000)], 0);

        static void AssertUsage(EntityUsage usage, int valueCount, params (string Id, int Resources)[] expected)
        {
            Assert.AreEqual(valueCount, usage.ValueCount);
            Assert.AreEqual(expected.Length, usage.ResourceCountByChildId.Count);
            foreach (var (id, resources) in expected) Assert.AreEqual(resources, usage.ResourceCountByChildId[id], id);
        }
    }

    // ---- undo ------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task APreImageRestoresTheRowAsCaptured()
    {
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "Action"), ("b", "Drama")));
        await Properties.UpdateRange([(await StoredRowAsync(id)) with { CreatedAt = KnownCreatedAt }]);
        var before = await StoredRowAsync(id);
        var beforeHash = ContentHash.Of((await ReadOneAsync(id)).Content);

        var preImages = await Kind.CapturePreImageAsync([Key(id), "424242"], CancellationToken.None);
        Assert.AreEqual(1, preImages.Count);
        var preImage = preImages[Key(id)];
        Assert.AreEqual("Genre", preImage["name"]!.GetValue<string>());
        Assert.AreEqual((int) PropertyType.MultipleChoice, preImage["type"]!.GetValue<int>());
        Assert.AreEqual(before.Options, preImage["options"]!.GetValue<string>());
        Assert.AreEqual(before.Order, preImage["order"]!.GetValue<int>());
        Assert.AreEqual(KnownCreatedAt, DateTime.Parse(preImage["createdAt"]!.GetValue<string>(), null,
            System.Globalization.DateTimeStyles.RoundtripKind));

        var local = await ReadOneAsync(id);
        var content = (CustomPropertyContentV1) Codec.ReadLocal(local.Content);
        await ApplyAsync(Update("u", id, local, content with
        {
            Name = "Genres", Choices = [..content.Choices, new CustomPropertyChoiceV1("c", "Comedy", null)],
        }));
        Assert.AreNotEqual(beforeHash, ContentHash.Of((await ReadOneAsync(id)).Content));

        await Kind.RestoreAsync(Key(id), preImage, CancellationToken.None);
        Assert.AreEqual(beforeHash, ContentHash.Of((await ReadOneAsync(id)).Content), "faithful: the canonical hash");
        Assert.AreEqual(before, await StoredRowAsync(id), "IgnoreCase off: the raw row too");
    }

    [TestMethod]
    public async Task APreImageRecreatesADeletedPropertyWithItsOptionIds()
    {
        // Undo of a deletion (§8.11 Recreate): the pre-image's content is a create's content, whatever the kind.
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "Action"), ("b", "Drama")));
        var content = (await ReadOneAsync(id)).Content;
        var preImage = (await Kind.CapturePreImageAsync([Key(id)], CancellationToken.None))[Key(id)];
        Assert.AreEqual(Canon(content), Canon(preImage["content"]));
        await Kind.DeleteAsync(Key(id), CancellationToken.None);

        var outcome = await ApplyAsync(new CreateEntityOperation("r", EntityKeys.None, "self", 0,
            preImage["content"]!.AsObject(), FromPreImage: true));
        var recreated = outcome.CreatedLocalKeysByItemId["r"];
        Assert.AreNotEqual(Key(id), recreated, "a new local id");
        Assert.AreEqual(Canon(content), Canon((await Kind.ReadAsync([recreated], CancellationToken.None)).Single().Content));

        // An unreadable row has no content to recreate from.
        var broken = await AddAsync("Broken", PropertyType.Tags);
        await SetRawOptionsAsync(broken, "{broken");
        var brokenImage = (await Kind.CapturePreImageAsync([Key(broken)], CancellationToken.None))[Key(broken)];
        Assert.IsNull(brokenImage["content"]);
        Assert.AreEqual("{broken", brokenImage["options"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task APreImageWithIgnoreCaseDuplicatesRestoresAndRecreatesThemAll()
    {
        // F72: duplicates added with IgnoreCase off and kept when it was switched on (the fixture's Genre, Keywords
        // and Region). A peer deletion of the class {Action, action} removes both; undo brings both back, ids and all.
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "Action"), ("b", "action"), ("c", "Drama")));
        var ignoring = Choices(("a", "Action"), ("b", "action"), ("c", "Drama"));
        ignoring.IgnoreCase = true;
        await Properties.Put(id, new CustomPropertyAddOrPutDto
        {
            Name = "Genre", Type = PropertyType.MultipleChoice, Options = JsonConvert.SerializeObject(ignoring),
        });
        var before = await ReadOneAsync(id);
        var captured = (CustomPropertyContentV1) Codec.ReadLocal(before.Content);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, captured.Choices.Select(c => c.Uuid).ToArray());
        var beforeHash = ContentHash.Of(before.Content);
        var preImage = (await Kind.CapturePreImageAsync([Key(id)], CancellationToken.None))[Key(id)];

        await ApplyAsync(Update("u", id, before, captured with { Choices = [captured.Choices[2]] }));
        CollectionAssert.AreEqual(new[] { "c" },
            ((CustomPropertyContentV1) Codec.ReadLocal((await ReadOneAsync(id)).Content)).Choices.Select(c => c.Uuid).ToArray());

        await Kind.RestoreAsync(Key(id), preImage, CancellationToken.None);
        Assert.AreEqual(beforeHash, ContentHash.Of((await ReadOneAsync(id)).Content), "faithful: the canonical hash");
        Assert.AreEqual(preImage["options"]!.GetValue<string>(), (await StoredRowAsync(id)).Options, "the raw options too");

        // Undo of a deletion (§8.11 Recreate): the same ids again, under a new local id.
        await Kind.DeleteAsync(Key(id), CancellationToken.None);
        var outcome = await ApplyAsync(new CreateEntityOperation("r", EntityKeys.None, "self", 0,
            preImage["content"]!.AsObject(), FromPreImage: true));
        var recreated = (await Kind.ReadAsync([outcome.CreatedLocalKeysByItemId["r"]], CancellationToken.None)).Single();
        Assert.AreEqual(Canon(before.Content), Canon(recreated.Content));
    }

    [TestMethod]
    public async Task Restore_RefusesAPropertyThatIsGone_AndCapturedOptionsThatDoNotRead()
    {
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "Action")));
        var preImage = (await Kind.CapturePreImageAsync([Key(id)], CancellationToken.None))[Key(id)];
        await Assert.ThrowsExceptionAsync<KeyNotFoundException>(() =>
            Kind.RestoreAsync("424242", preImage, CancellationToken.None));

        // Captured options that do not read are never written back.
        var other = await AddAsync("Other", PropertyType.MultipleChoice, Choices(("x", "X")));
        var broken = new JsonObject
        {
            ["name"] = "Other", ["type"] = (int) PropertyType.MultipleChoice, ["options"] = "{broken",
        };
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            Kind.RestoreAsync(Key(other), broken, CancellationToken.None));
        Assert.AreEqual("X", ((CustomPropertyContentV1) Codec.ReadLocal((await ReadOneAsync(other)).Content)).Choices.Single().Label);
    }

    /// <summary>§8.11: a type change is undone by restoring its pre-image, which converts the property back.</summary>
    [TestMethod]
    public async Task RestoringAPreImageOfAnotherType_ConvertsBack_AndEveryValueNamesItsCapturedOption()
    {
        var id = await AddAsync("Genre", PropertyType.MultipleChoice,
            Choices(("a", "Action"), ("c", "Comedy"), ("u", "Unused")));
        await AddValuesAsync(id, PropertyType.MultipleChoice, (1, new List<string> { "a" }), (2, new List<string> { "c" }),
            (3, new List<string> { "a" }));
        var before = await StoredRowAsync(id);
        var beforeHash = ContentHash.Of((await ReadOneAsync(id)).Content);
        var preImage = (await Kind.CapturePreImageAsync([Key(id)], CancellationToken.None))[Key(id)];

        var local = await ReadOneAsync(id);
        await ApplyAsync(new ChangeSubtypeOperation("t", Key(id), ContentHash.Of(local.Content),
            nameof(PropertyType.SingleChoice)));
        Assert.AreEqual(PropertyType.SingleChoice, (await StoredRowAsync(id)).Type);
        _index.Invalidated.Clear();

        await Kind.RestoreAsync(Key(id), preImage, CancellationToken.None);
        var row = await StoredRowAsync(id);
        Assert.AreEqual(PropertyType.MultipleChoice, row.Type);
        Assert.AreEqual(before.Options, row.Options, "the captured options, as stored: ids, colours and the unused one");
        Assert.AreEqual(beforeHash, ContentHash.Of((await ReadOneAsync(id)).Content));
        CollectionAssert.AreEquivalent(new[] { "1:a", "2:c", "3:a" }, await ValueIdsAsync(id));
        CollectionAssert.IsSubsetOf(new[] { 1, 2, 3 }, _index.Invalidated.Distinct().ToArray());
    }

    [TestMethod]
    public async Task RestoringAfterALossyConversion_KeepsWhatTheConversionMadeOfAValue()
    {
        // A label holding the list separator becomes text and comes back as whatever the conversions make of it: options
        // the pre-image does not have. They are added to the restored options, so the value still names them (§8.11:
        // what the first conversion lost stays lost, nothing more).
        var id = await AddAsync("Genre", PropertyType.MultipleChoice,
            Choices(("a", "Action"), ("c", "Rock, Pop"), ("u", "Unused")));
        await AddValuesAsync(id, PropertyType.MultipleChoice, (1, new List<string> { "a" }),
            (2, new List<string> { "c" }));
        var preImage = (await Kind.CapturePreImageAsync([Key(id)], CancellationToken.None))[Key(id)];
        var local = await ReadOneAsync(id);
        await ApplyAsync(new ChangeSubtypeOperation("t", Key(id), ContentHash.Of(local.Content),
            nameof(PropertyType.SingleLineText)));

        await Kind.RestoreAsync(Key(id), preImage, CancellationToken.None);
        var restored = (CustomPropertyContentV1) Codec.ReadLocal((await ReadOneAsync(id)).Content);
        Assert.AreEqual(PropertyType.MultipleChoice, restored.Type);
        CollectionAssert.AreEqual(new[] { "a", "c", "u" }, restored.Choices.Take(3).Select(c => c.Uuid).ToArray());
        var options = restored.Choices.Select(c => c.Uuid!).ToHashSet();
        var values = await ValueIdsAsync(id);
        Assert.AreEqual(2, values.Select(v => v.Split(':')[0]).Distinct().Count(), "no value is lost");
        foreach (var value in values) Assert.IsTrue(options.Contains(value.Split(':')[1]), $"{value} names no option");
        Assert.IsTrue(values.Contains("1:a"));
    }

    [TestMethod]
    [DataRow(nameof(PropertyType.SingleLineText))]
    [DataRow(nameof(PropertyType.MultipleChoice))]
    [DataRow(nameof(PropertyType.Tags))]
    public async Task RestoringAMultilevelPropertyOfAnotherType_EveryValueNamesAnOption(string convertedTo)
    {
        var id = await AddAsync("Region", PropertyType.Multilevel, new MultilevelPropertyOptions
        {
            Data =
            [
                new MultilevelDataOptions
                {
                    Value = "n1", Label = "Asia", Children = [new MultilevelDataOptions { Value = "n2", Label = "Japan" }],
                },
                new MultilevelDataOptions { Value = "n3", Label = "Europe" },
            ],
        });
        await AddValuesAsync(id, PropertyType.Multilevel, (1, new List<string> { "n2" }), (2, new List<string> { "n3" }),
            (3, new List<string> { "n1", "n3" }));
        var preImage = (await Kind.CapturePreImageAsync([Key(id)], CancellationToken.None))[Key(id)];
        await Kind.ChangeSubtypeAsync(Key(id), convertedTo, CancellationToken.None);

        await Kind.RestoreAsync(Key(id), preImage, CancellationToken.None);
        var restored = (CustomPropertyContentV1) Codec.ReadLocal((await ReadOneAsync(id)).Content);
        Assert.AreEqual(PropertyType.Multilevel, restored.Type);
        var nodes = Codec.ChildrenOf(restored).Select(c => c.Id).ToHashSet();
        CollectionAssert.IsSubsetOf(new[] { "n1", "n2", "n3" }, nodes.ToArray(), "every captured node, with its id");
        var values = await ValueIdsAsync(id);
        foreach (var value in values) Assert.IsTrue(nodes.Contains(value.Split(':')[1]), $"{value} names no node");
        CollectionAssert.IsSubsetOf(new[] { "2:n3" }, values);
    }

    [TestMethod]
    public async Task RestoringAfterALossyConversion_AddsANodeUnderWhatItsPathStillHas()
    {
        // "Kyoto/Osaka" holds the level separator: as text and back it is the path Asia / Kyoto / Osaka. Asia is the
        // captured n1, so Kyoto / Osaka is added below it for the value that names it.
        var id = await AddAsync("Region", PropertyType.Multilevel, new MultilevelPropertyOptions
        {
            Data =
            [
                new MultilevelDataOptions
                {
                    Value = "n1", Label = "Asia",
                    Children =
                    [
                        new MultilevelDataOptions { Value = "n2", Label = "Japan" },
                        new MultilevelDataOptions { Value = "n4", Label = "Kyoto/Osaka" },
                    ],
                },
                new MultilevelDataOptions { Value = "n3", Label = "Europe" },
            ],
        });
        await AddValuesAsync(id, PropertyType.Multilevel, (1, new List<string> { "n2" }), (2, new List<string> { "n4" }));
        var preImage = (await Kind.CapturePreImageAsync([Key(id)], CancellationToken.None))[Key(id)];
        await Kind.ChangeSubtypeAsync(Key(id), nameof(PropertyType.SingleLineText), CancellationToken.None);

        await Kind.RestoreAsync(Key(id), preImage, CancellationToken.None);
        var restored = (CustomPropertyContentV1) Codec.ReadLocal((await ReadOneAsync(id)).Content);
        var nodes = Codec.ChildrenOf(restored).ToDictionary(c => c.Id);
        CollectionAssert.IsSubsetOf(new[] { "n1", "n2", "n3", "n4" }, nodes.Keys.ToArray());
        var values = (await ValueIdsAsync(id)).ToDictionary(v => v.Split(':')[0], v => v.Split(':')[1]);
        Assert.AreEqual("n2", values["1"]);
        var osaka = nodes[values["2"]];
        CollectionAssert.AreEqual(new[] { "Asia", "Kyoto", "Osaka" }, osaka.Display.Path!.ToArray());
        Assert.AreEqual("n1", nodes[osaka.ParentId!].ParentId, "under the captured Asia");
    }

    [TestMethod]
    public async Task RestoringTheSameType_PointsValuesAtTheirClass_AndRefusesAnOptionInUseThePreImageLacks()
    {
        // Converted back through ChangeSubtypeAsync first: the values name rebuilt options, found by their labels.
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "Action"), ("c", "Comedy")));
        await AddValuesAsync(id, PropertyType.MultipleChoice, (1, new List<string> { "a" }), (2, new List<string> { "c" }));
        var before = await StoredRowAsync(id);
        var preImage = (await Kind.CapturePreImageAsync([Key(id)], CancellationToken.None))[Key(id)];
        await Kind.ChangeSubtypeAsync(Key(id), nameof(PropertyType.SingleChoice), CancellationToken.None);
        await Kind.ChangeSubtypeAsync(Key(id), nameof(PropertyType.MultipleChoice), CancellationToken.None);
        Assert.AreNotEqual(before.Options, (await StoredRowAsync(id)).Options, "ChangeType rebuilt the options (F73)");
        await AddValuesAsync(id, PropertyType.MultipleChoice, (3, new List<string> { "gone" }));

        await Kind.RestoreAsync(Key(id), preImage, CancellationToken.None);
        Assert.AreEqual(before.Options, (await StoredRowAsync(id)).Options);
        // A value that named no option before names none after: left as it is.
        CollectionAssert.AreEquivalent(new[] { "1:a", "2:c", "3:gone" }, await ValueIdsAsync(id));

        // An option added since and used: the pre-image has nothing of its class, so restoring is refused, and
        // nothing is written.
        var mood = await AddAsync("Mood", PropertyType.MultipleChoice, Choices(("h", "Happy")));
        var moodImage = (await Kind.CapturePreImageAsync([Key(mood)], CancellationToken.None))[Key(mood)];
        var local = await ReadOneAsync(mood);
        var content = (CustomPropertyContentV1) Codec.ReadLocal(local.Content);
        await ApplyAsync(Update("u", mood, local, content with { Choices = [..content.Choices, new("s", "Sad", null)] }));
        await AddValuesAsync(mood, PropertyType.MultipleChoice, (4, new List<string> { "s" }));
        var added = await StoredRowAsync(mood);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            Kind.RestoreAsync(Key(mood), moodImage, CancellationToken.None));
        Assert.AreEqual(added, await StoredRowAsync(mood));
        CollectionAssert.AreEqual(new[] { "4:s" }, await ValueIdsAsync(mood));
    }

    // ---- caches and the index after a rollback -------------------------------------------------------

    [TestMethod]
    public async Task AfterARollback_ResetCachesServesTheStoredRowsAgain_AndTheIndexRereadsThem()
    {
        var id = await AddAsync("Genre", PropertyType.MultipleChoice, Choices(("a", "Action")));
        var doomed = await AddAsync("Doomed", PropertyType.Tags);
        await AddValuesAsync(doomed, PropertyType.Tags, (31, new List<string> { "t" }));
        var local = await ReadOneAsync(id);
        var doomedLocal = await ReadOneAsync(doomed);
        var db = _sp.GetRequiredService<BakabaseDbContext>();

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await ApplyAsync(
                Update("u", id, local, (CustomPropertyContentV1) Codec.ReadLocal(local.Content) with { Name = "Uncommitted" }),
                new DeleteEntityOperation("d", Key(doomed), ContentHash.Of(doomedLocal.Content)));
            // The caches are shared and not transactional (F9): they already serve what is not committed.
            Assert.AreEqual("Uncommitted", (await Properties.GetByKey(id)).Name);
            await transaction.RollbackAsync();
        }

        db.ChangeTracker.Clear();
        Assert.IsTrue(Caches.ContainsKey(typeof(CustomPropertyDbModel).FullName!));
        Kind.ResetCaches();
        Assert.IsFalse(Caches.ContainsKey(typeof(CustomPropertyDbModel).FullName!));
        Assert.IsFalse(Caches.ContainsKey(typeof(CustomPropertyValueDbModel).FullName!));

        Assert.AreEqual("Genre", (await Properties.GetByKey(id)).Name);
        Assert.AreEqual(1, (await Properties.GetAllDbModels(r => r.Id == doomed)).Count, "the deletion rolled back");
        Assert.AreEqual(1, (await Values.GetAllDbModels(v => v.PropertyId == doomed)).Count);
        CollectionAssert.AreEqual(new[] { 31, 31 }, _index.Invalidated.ToArray(), "invalidated again after the rollback");

        // Again on every reset: after a rollback to a savepoint, the session resets once more after its commit.
        Kind.ResetCaches();
        CollectionAssert.AreEqual(new[] { 31, 31, 31 }, _index.Invalidated.ToArray(), "once per reset");
    }

    // ---- the round trip of the fixture -----------------------------------------------------------------

    /// <summary>
    /// §13.4 at the adapter: every fixture property published, read as a peer would read it, created on an empty
    /// provider through the adapter and published again compares equal, keeps its option ids except the folded
    /// IgnoreCase duplicates (v3.1 H4), and hands out the codec's canonical form on both sides.
    /// </summary>
    [TestMethod]
    public async Task TheFixtureRoundTripsThroughACreateOnAnEmptyProvider()
    {
        var seeded = await DataSyncFixture.SeedCustomPropertiesAsync(_sp);
        var sources = await Kind.ReadAsync(null, CancellationToken.None);
        Assert.AreEqual(seeded.Count, sources.Count);

        var target = await BuildProviderAsync(new RecordingSearchIndex());
        var targetKind = target.GetServices<IDataSyncKind>().Single(k => k.Codec.Descriptor.Kind == DataSyncKindIds.CustomProperty);
        var creates = new List<ApplyOperation>();
        var sourceForms = new List<string>();
        foreach (var source in sources)
        {
            var published = Codec.Publish(Codec.ReadLocal(source.Content), DataSyncOverlay.None, false);
            Assert.IsNull(published.Held);
            sourceForms.Add(Canon(Codec.ComparisonForm(published.Content!, null, false)));
            // What travels and what the receiver accepts (the wire format itself is package C's).
            var read = Codec.Read(JsonNode.Parse(Canon(Codec.Write(published.Content!)))!.AsObject(), DataSyncLimits.Default);
            Assert.IsNull(read.Held);
            var prepared = Codec.PrepareCreate(read.Content!, null);
            creates.Add(new CreateEntityOperation($"c{creates.Count}", EntityKeys.None, "source-node", creates.Count,
                Codec.Write(prepared.Content)));
        }

        var outcome = await targetKind.ApplyAsync(new ApplyBatch(DataSyncKindIds.CustomProperty, creates), CancellationToken.None);
        Assert.AreEqual(sources.Count, outcome.CreatedLocalKeysByItemId.Count);
        for (var i = 0; i < sources.Count; i++)
        {
            var created = (await targetKind.ReadAsync([outcome.CreatedLocalKeysByItemId[$"c{i}"]], CancellationToken.None)).Single();
            var content = Codec.ReadLocal(created.Content);
            var republished = Codec.Publish(content, DataSyncOverlay.None, false);
            Assert.AreEqual(sourceForms[i], Canon(Codec.ComparisonForm(republished.Content!, null, false)), seeded[i].Name);

            // A folded multilevel node's children move under the node it folds into, so compare as sets.
            var sourceIds = Codec.ChildrenOf(Codec.ReadLocal(sources[i].Content)).Select(c => c.Id)
                .Where(id => !DataSyncFixture.FoldedOnCreate.Contains(id)).ToArray();
            CollectionAssert.AreEquivalent(sourceIds, Codec.ChildrenOf(content).Select(c => c.Id).ToArray(), seeded[i].Name);
        }
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private GlobalCacheVault Caches => _sp.GetRequiredService<GlobalCacheVault>();

    private static string Key(int id) => id.ToString();
    private static readonly CustomPropertyCodec TypedCodec = new();

    private static string Canon(JsonNode? node) => CanonicalJson.Serialize(node);
    private static string Canon(CustomPropertyContentV1 content) => Canon(TypedCodec.Write(content));

    private static CustomPropertyContentV1 Content(IReadOnlyList<LocalEntity> entities,
        IReadOnlyList<DataSyncFixtureProperty> seeded, string name) =>
        TypedCodec.ReadLocal(
            entities.Single(e => e.LocalKey == Key(seeded.Single(p => p.Name == name).Id)).Content);

    private async Task<int> AddAsync(string name, PropertyType type, object? options = null) =>
        (await Properties.Add(new CustomPropertyAddOrPutDto
        {
            Name = name, Type = type, Options = options is null ? null : JsonConvert.SerializeObject(options),
        })).Id;

    private static MultipleChoicePropertyOptions Choices(params (string Uuid, string Label)[] choices) => new()
    {
        Choices = choices.Select(c => new ChoiceOptions { Value = c.Uuid, Label = c.Label }).ToList(),
    };

    private async Task<CustomPropertyDbModel> StoredRowAsync(int id)
    {
        await using var scope = _sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().CustomProperties.AsNoTracking()
            .SingleAsync(p => p.Id == id);
    }

    private async Task SetRawOptionsAsync(int id, string options) =>
        await Properties.UpdateRange([(await StoredRowAsync(id)) with { Options = options }]);

    private async Task<LocalEntity> ReadOneAsync(int id) =>
        (await Kind.ReadAsync([Key(id)], CancellationToken.None)).Single();

    private static CustomPropertyValueDbModel Value(int propertyId, PropertyType type, int resourceId, object dbValue,
        int scope = (int) PropertyValueScope.Manual) => new()
    {
        ResourceId = resourceId, PropertyId = propertyId, Scope = scope,
        Value = dbValue.SerializeAsStandardValue(type.GetDbValueType()),
    };

    private async Task AddValuesAsync(int propertyId, PropertyType type, params (int ResourceId, object DbValue)[] values) =>
        await Values.AddDbModelRange(values.Select(v => Value(propertyId, type, v.ResourceId, v.DbValue)).ToList());

    /// <summary>Every option id each stored value of a property names, as <c>resource:id</c>.</summary>
    private async Task<List<string>> ValueIdsAsync(int propertyId)
    {
        var dbValueType = (await StoredRowAsync(propertyId)).Type.GetDbValueType();
        return (await Values.GetAllDbModels(v => v.PropertyId == propertyId)).SelectMany(v =>
            (v.Value!.DeserializeAsStandardValue(dbValueType) switch
            {
                string single => [single],
                List<string> list => list,
                _ => new List<string>(),
            }).Select(option => $"{v.ResourceId}:{option}")).ToList();
    }

    private Task<ApplyBatchOutcome> ApplyAsync(params ApplyOperation[] operations) =>
        Kind.ApplyAsync(new ApplyBatch(DataSyncKindIds.CustomProperty, operations), CancellationToken.None);

    private CreateEntityOperation Create(string itemId, CustomPropertyContentV1 content) =>
        new(itemId, EntityKeys.None, "peer-node", 0, Codec.Write(content));

    private UpdateEntityOperation Update(string itemId, int id, LocalEntity local, CustomPropertyContentV1 merged) =>
        new(itemId, Key(id), ContentHash.Of(local.Content), Codec.Write(merged), EntityKeys.None, [], []);

    /// <summary>An adapter whose service records <c>SetOrders</c>, so a test sees which rows are written.</summary>
    private (IDataSyncKind Kind, PropertyServiceRecorder Recorder) RecordingKind()
    {
        var proxy = DispatchProxy.Create<ICustomPropertyService, PropertyServiceRecorder>();
        var recorder = (PropertyServiceRecorder) (object) proxy;
        recorder.Target = Properties;
        return (new CustomPropertyDataSyncKind<BakabaseDbContext>(proxy, Values,
            _sp.GetRequiredService<IPropertyTypeConverter>(), Caches, _sp), recorder);
    }

    /// <summary>Forwards every call to the real service and records the arguments of <c>SetOrders</c>.</summary>
    public class PropertyServiceRecorder : DispatchProxy
    {
        public ICustomPropertyService Target { get; set; } = null!;
        public List<IReadOnlyDictionary<int, int>> SetOrdersCalls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(ICustomPropertyService.SetOrders))
                SetOrdersCalls.Add(new Dictionary<int, int>((IReadOnlyDictionary<int, int>) args![0]!));
            try
            {
                return targetMethod.Invoke(Target, args);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }
    }

    /// <summary>Records the resources whose index entries are invalidated; searches fall back to a full scan.</summary>
    private sealed class RecordingSearchIndex : IResourceSearchIndexService
    {
        public List<int> Invalidated { get; } = [];

        public bool IsReady => true;
        public long Version => 0;
        public DateTime LastUpdatedAt => DateTime.MinValue;

        public Task<HashSet<int>?> SearchResourceIdsAsync(ResourceSearchFilterGroup? group) =>
            Task.FromResult<HashSet<int>?>(null);

        public Task<Dictionary<string, int>?> GetPropertyValueResourceCountsAsync(PropertyPool pool, int propertyId,
            IEnumerable<string> valueIds, IReadOnlySet<int>? resourceIds = null) =>
            Task.FromResult<Dictionary<string, int>?>(null);

        public void InvalidateResource(int resourceId)
        {
            lock (Invalidated) Invalidated.Add(resourceId);
        }

        public void InvalidateResources(IEnumerable<int> resourceIds)
        {
            lock (Invalidated) Invalidated.AddRange(resourceIds);
        }

        public void RemoveResource(int resourceId)
        {
        }

        public void RemoveResources(IEnumerable<int> resourceIds)
        {
        }

        public Task WaitForPendingUpdatesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RebuildAllAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task WaitForReadyAsync(TimeSpan? timeout = null) => Task.CompletedTask;
        public ResourceSearchIndexStatus GetStatus() => new() { IsReady = true };
    }
}
