using System.Globalization;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Tests.DataSync.Apply;
using Bakabase.Tests.DataSync.CustomProperties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// Echo prevention and normalization drift (spec §6.4) at persistence: an apply's hashes and its revision come from
/// the entity as re-read after the write, never from the payload, so the next Refresh finds nothing to do; a
/// normalization the codec does not model adds this device's counter once and then settles. The fold cases run the
/// real apply runner over the real custom property kind (package B's adapter and the Property service): duplicates a
/// create folds, a renamed casing, a tag group the service stores as none, and IgnoreCase turned on over duplicates
/// make zero extra revisions.
/// </summary>
[TestClass]
public class EchoAndConvergenceTests
{
    private const string PeerActor = "9999999999999999";

    [TestMethod]
    public async Task After_an_apply_recorded_from_the_re_read_Refresh_makes_no_revision()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"));
        await f.RefreshAsync();

        var (record, reread, vv) = await FastForwardAsync(f, "Genres", ("a", "Action"), ("b", "Drama"));

        Assert.IsTrue(DataSyncEntityForms.ResultEqualsRemote(f.Kind.Codec,
            DataSyncEntityForms.Evaluate(f.Kind.Codec, reread, DataSyncOverlay.None, false, null, null), record));
        Assert.AreEqual(record.Vv, vv, "FastForward with resultEqualsRemote takes the remote vector as it is (§2.8)");
        var recorded = await f.RowAsync("1");

        Assert.AreEqual(0, (await f.RefreshAsync()).Changed);
        Assert.AreEqual((recorded.Seq, recorded.VvJson), ((await f.RowAsync("1")).Seq, (await f.RowAsync("1")).VvJson));
    }

    [TestMethod]
    public async Task An_unmodelled_normalization_adds_one_counter_and_then_settles()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"));
        await f.RefreshAsync();
        // A service rule the codec does not mirror: labels are trimmed when written.
        f.Kind.NormalizeOnWrite = content =>
        {
            foreach (var child in content["children"]!.AsArray())
                child!["label"] = child["label"]!.GetValue<string>().Trim();
            return content;
        };

        var (record, reread, vv) = await FastForwardAsync(f, "Genre", ("a", "Action"), ("b", "Drama "));

        var self = new DataSyncActorId((await f.StateAsync()).ActorId);
        Assert.AreEqual(DataSyncVvRelation.Dominates, vv.CompareTo(record.Vv),
            "the re-read differs from the remote: this device adds a counter of its own (§6.4)");
        Assert.IsTrue(vv[self] > 0);
        Assert.AreEqual(0, (await f.RefreshAsync()).Changed, "and the next Refresh still finds nothing");

        // The peer fast-forwards to the normalized content; writing it here again changes nothing: one hop.
        var normalized = new DataSyncWireRecord(record.Keys, record.Origin, record.Seq + 1, vv, null, false, 1, null,
            reread.Content, ContentHash.Of(reread.Content), null, 0);
        var again = (await f.Kind.ReadAsync(["1"], default)).Single();
        Assert.IsTrue(DataSyncEntityForms.ResultEqualsRemote(f.Kind.Codec,
            DataSyncEntityForms.Evaluate(f.Kind.Codec, again, DataSyncOverlay.None, false, null, null), normalized));
    }

    // ---- the fold cases, over the real custom property kind -------------------------------------------------

    private static readonly CustomPropertyCodec Properties = CustomPropertyCodec.Instance;

    [TestMethod]
    public async Task A_create_whose_duplicates_the_service_folds_makes_no_revision_of_its_own()
    {
        using var newtonsoft = NewtonsoftDefaults.UseApp();
        var (f, peer, link) = await PropertiesAsync();
        var key = Guid.NewGuid().ToString("N");
        // Case variants under IgnoreCase: PrepareCreate folds them as a fresh AddRange does (v3.1 H4).
        var record = peer.Record([key], peer.Next(), Properties.Write(new CustomPropertyContentV1
        {
            Name = "Genre", Type = PropertyType.MultipleChoice, IgnoreCase = true,
            Choices = [new("a1", "Action", "#e5484d"), new("a2", "ACTION", null), new("d", "Drama", null)],
        }), "a0");

        Assert.AreEqual(1, (await f.ApplyAsync(link, peer, (DataSyncApplyFixture.Properties, record))).Applied);

        var row = (await f.ByKeyAsync(key, DataSyncApplyFixture.Properties))!;
        var stored = await ContentAsync(f, row.LocalKey);
        CollectionAssert.AreEqual(new[] { "a1", "d" }, stored.Choices.Select(c => c.Uuid).ToArray(), "one member per class");
        await AssertSettledAsync(f, row.LocalKey, record);
    }

    [TestMethod]
    public async Task A_label_the_peer_renamed_to_another_casing_under_IgnoreCase_makes_no_revision_of_its_own()
    {
        using var newtonsoft = NewtonsoftDefaults.UseApp();
        var (f, peer, link) = await PropertiesAsync();
        var localKey = await AddAsync(f, "Genre", PropertyType.MultipleChoice, new MultipleChoicePropertyOptions
        {
            IgnoreCase = true, Choices = [Choice("a1", "Action"), Choice("d", "Drama")],
        });
        var (key, vv) = await SyncedAsync(f, localKey);

        var record = peer.Record([key], peer.Next(vv), Properties.Write(new CustomPropertyContentV1
        {
            Name = "Genre", Type = PropertyType.MultipleChoice, IgnoreCase = true,
            Choices = [new("a1", "ACTION", null), new("d", "Drama", null)],
        }), (await f.RowAsync(localKey, DataSyncApplyFixture.Properties)).OrderKey);
        await f.ApplyAsync(link, peer, (DataSyncApplyFixture.Properties, record));

        // One class key either way: nothing the form shows changed, so nothing is written (§3.4, §8.5).
        Assert.AreEqual("Action", (await ContentAsync(f, localKey)).Choices[0].Label, "a casing is not carried");
        await AssertSettledAsync(f, localKey, record);
    }

    [TestMethod]
    public async Task A_tag_group_the_service_stores_as_none_makes_no_revision_of_its_own()
    {
        using var newtonsoft = NewtonsoftDefaults.UseApp();
        var (f, peer, link) = await PropertiesAsync();
        var localKey = await AddAsync(f, "Studio", PropertyType.Tags, new TagsPropertyOptions
        {
            IgnoreCase = false, Tags = [Tag("k", null, "Kyoto")],
        });
        var (key, vv) = await SyncedAsync(f, localKey);

        // The peer publishes the tag with "" (one group with none, §3.4) and adds one.
        var record = peer.Record([key], peer.Next(vv), Properties.Write(new CustomPropertyContentV1
        {
            Name = "Studio", Type = PropertyType.Tags, IgnoreCase = false,
            Tags = [new("k", "", "Kyoto", null), new("t", "", "Tokyo", null)],
        }), (await f.RowAsync(localKey, DataSyncApplyFixture.Properties)).OrderKey);
        Assert.AreEqual(1, (await f.ApplyAsync(link, peer, (DataSyncApplyFixture.Properties, record))).Applied);

        var stored = await ContentAsync(f, localKey);
        Assert.IsTrue(stored.Tags.All(t => t.Group is null), "TagValue stores \"\" as no group");
        Assert.AreEqual(2, stored.Tags.Count);
        await AssertSettledAsync(f, localKey, record);
    }

    [TestMethod]
    public async Task IgnoreCase_turned_on_over_duplicates_keeps_them_and_makes_no_revision_of_its_own()
    {
        using var newtonsoft = NewtonsoftDefaults.UseApp();
        var (f, peer, link) = await PropertiesAsync();
        var localKey = await AddAsync(f, "Genre", PropertyType.MultipleChoice, new MultipleChoicePropertyOptions
        {
            IgnoreCase = false, Choices = [Choice("a1", "Action"), Choice("a2", "action"), Choice("d", "Drama")],
        });
        var (key, vv) = await SyncedAsync(f, localKey);

        var record = peer.Record([key], peer.Next(vv), Properties.Write(new CustomPropertyContentV1
        {
            Name = "Genre", Type = PropertyType.MultipleChoice, IgnoreCase = true,
            Choices = [new("a1", "Action", null), new("a2", "action", null), new("d", "Drama", null)],
        }), (await f.RowAsync(localKey, DataSyncApplyFixture.Properties)).OrderKey);
        Assert.AreEqual(1, (await f.ApplyAsync(link, peer, (DataSyncApplyFixture.Properties, record))).Applied);

        var stored = await ContentAsync(f, localKey);
        Assert.IsTrue(stored.IgnoreCase);
        CollectionAssert.AreEqual(new[] { "a1", "a2", "d" }, stored.Choices.Select(c => c.Uuid).ToArray(),
            "a stored option is never folded (F72)");
        await AssertSettledAsync(f, localKey, record);
    }

    /// <summary>A provider with the real custom property kind, and a two-way link to a simulated peer.</summary>
    private static async Task<(DataSyncApplyFixture F, DataSyncPeer Peer, DataSyncLinkDbModel Link)> PropertiesAsync()
    {
        var f = await DataSyncApplyFixture.CreateAsync(customProperties: true);
        var peer = new DataSyncPeer("PC-1");
        var link = await f.LinkAsync(peer, DataSyncLinkMode.TwoWay, true, DataSyncApplyFixture.Properties);
        return (f, peer, link);
    }

    private static async Task<string> AddAsync(DataSyncApplyFixture f, string name, PropertyType type, object options)
    {
        var added = await f.CustomProperties.Add(new CustomPropertyAddOrPutDto
        {
            Name = name, Type = type, Options = JsonConvert.SerializeObject(options),
        });
        return added.Id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The property as this device publishes it: Refresh gave it a key and a first revision.</summary>
    private static async Task<(string Key, DataSyncVersionVector Vv)> SyncedAsync(DataSyncApplyFixture f, string localKey)
    {
        await f.RefreshAsync();
        var row = await f.RowAsync(localKey, DataSyncApplyFixture.Properties);
        return (row.SyncKey, DataSyncApplyFixture.Vv(row.VvJson));
    }

    private static async Task<CustomPropertyContentV1> ContentAsync(DataSyncApplyFixture f, string localKey)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var kind = scope.ServiceProvider.GetServices<IDataSyncKind>()
            .Single(k => k.Codec.Descriptor.Kind == DataSyncKindIds.CustomProperty);
        return Properties.ReadLocal((await kind.ReadAsync([localKey], default)).Single().Content);
    }

    /// <summary>
    /// The apply took the record's vector as it is (the re-read entity's form equals the record's: no counter of this
    /// device's, §2.8, §6.4), and the next Refresh finds nothing: no revision, no Seq bump.
    /// </summary>
    private static async Task AssertSettledAsync(DataSyncApplyFixture f, string localKey, DataSyncWireRecord record)
    {
        var applied = await f.RowAsync(localKey, DataSyncApplyFixture.Properties);
        Assert.AreEqual(record.Vv, DataSyncApplyFixture.Vv(applied.VvJson), "no revision of this device's own");
        Assert.AreEqual(DataSyncPublication.SharedHashOfRecord(Properties, record, DataSyncLimits.Default), applied.SharedHash,
            "one comparison form on both sides");
        await f.RefreshAsync();
        var refreshed = await f.RowAsync(localKey, DataSyncApplyFixture.Properties);
        Assert.AreEqual((applied.Seq, applied.VvJson), (refreshed.Seq, refreshed.VvJson), "Refresh finds nothing to do");
        Assert.AreEqual(0, (await f.OpenItemsAsync()).Count);
    }

    private static ChoiceOptions Choice(string uuid, string label) => new() { Value = uuid, Label = label };

    private static TagsPropertyOptions.TagOptions Tag(string uuid, string? group, string name) =>
        new(group, name) { Value = uuid };

    /// <summary>
    /// A peer record that dominates the local entity is written through the adapter and recorded the way the apply
    /// runner records it: hashes from the re-read entity, the FastForward revision with <c>resultEqualsRemote</c>
    /// computed from the re-read comparison form.
    /// </summary>
    private static async Task<(DataSyncWireRecord Record, LocalEntity Reread, DataSyncVersionVector Vv)> FastForwardAsync(
        DataSyncRefreshFixture f, string name, params (string Id, string Label)[] children)
    {
        var row = await f.Db.DataSyncEntities.SingleAsync(e => e.LocalKey == "1");
        var local = DataSyncVersionVector.ParseStored(row.VvJson);
        var remoteContent = new MemoryDefinition(name, children.Select(c => new MemoryChild(c.Id, c.Label)).ToList()).ToContent();
        var record = new DataSyncWireRecord([row.SyncKey], "peer-node", 5, local.With(new DataSyncActorId(PeerActor), 1),
            new DataSyncEditorRef("peer-node", "PC-1", PeerActor), false, 1, null, remoteContent,
            ContentHash.Of(remoteContent), null, 0);

        await f.Kind.ApplyAsync(new ApplyBatch(f.KindId,
        [
            new UpdateEntityOperation("i1", "1", row.LocalHash, remoteContent, EntityKeys.None, ["b"], []),
        ]), default);

        var reread = (await f.Kind.ReadAsync(["1"], default)).Single();
        var form = DataSyncEntityForms.Evaluate(f.Kind.Codec, reread, DataSyncOverlay.None, false, null, null);
        var state = (await f.Store.GetLocalStateAsync(default))!;
        var vv = DataSyncRevisionRules.Next(DataSyncRevisionKind.FastForward, local, record.Vv,
            DataSyncEntityForms.ResultEqualsRemote(f.Kind.Codec, form, record), false,
            new DataSyncActorId(state.ActorId), () => ++state.ActorCounter);
        row.LocalHash = form.LocalHash;
        row.RawHash = (await f.Kind.ReadRawHashesAsync(default))["1"];
        row.SharedHash = form.SharedHash!;
        row.VvJson = vv.ToCanonicalString();
        row.Seq = await f.Store.NextSeqAsync(default);
        await f.Db.SaveChangesAsync();
        return (record, reread, vv);
    }
}
