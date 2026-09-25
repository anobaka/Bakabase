using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Merging.MergeFixture;

namespace Bakabase.Modules.DataSync.Tests.Merging;

/// <summary>Unknown-field preservation (§8.9): per member, against the base, never an item.</summary>
[TestClass]
public class UnknownMembersTests
{
    private static JsonObject J(string json) => JsonNode.Parse(json)!.AsObject();

    [TestMethod]
    public void ThreeWayMergesMemberByMember()
    {
        var r = DataSyncUnknownMembers.Merge(J("""{"a":1,"b":1,"c":1,"d":1}"""), J("""{"a":1,"b":2,"c":3,"d":1}"""),
            J("""{"a":5,"b":1,"c":4,"e":9}"""), DataSyncMerge3Mode.ThreeWay);
        Assert.AreEqual("""{"a":5,"b":2,"c":3,"e":9}""", r.Merged!.ToJsonString(), "d: removed there, unchanged here");
        CollectionAssert.AreEqual(new[] { "c" }, r.ConflictsKeptLocal.ToArray());
        Assert.IsTrue(r.Fields.All(f => f.Path.StartsWith("x:", StringComparison.Ordinal)));
        Assert.IsFalse(r.Fields.Any(f => f.Resolution == DataSyncFieldResolution.Conflict), "never an item");
    }

    [TestMethod]
    public void FastForwardTakesThePeersNoBaseTakesTheUnion()
    {
        var local = J("""{"a":1,"b":2}""");
        var remote = J("""{"b":3,"c":4}""");
        Assert.AreEqual("""{"b":3,"c":4}""",
            DataSyncUnknownMembers.Merge(null, local, remote, DataSyncMerge3Mode.FastForward).Merged!.ToJsonString());
        Assert.AreEqual("""{"a":1,"b":3,"c":4}""",
            DataSyncUnknownMembers.Merge(null, local, remote, DataSyncMerge3Mode.NoBase).Merged!.ToJsonString());
        Assert.IsNull(DataSyncUnknownMembers.Merge(null, null, null, DataSyncMerge3Mode.ThreeWay).Merged);
        Assert.IsTrue(DataSyncUnknownMembers.AreEqual(null, new JsonObject()));
    }

    [TestMethod]
    public void AnOlderBuildNeverRegressesANewerBuildsValue()
    {
        // A newer build changed x from 1 to 2; the older build in the middle still has the agreed 1.
        var r = DataSyncUnknownMembers.Merge(J("""{"x":1}"""), J("""{"x":1}"""), J("""{"x":2}"""), DataSyncMerge3Mode.ThreeWay);
        Assert.AreEqual("""{"x":2}""", r.Merged!.ToJsonString());
        // …and publishing merges it back without overriding a known member.
        var published = DataSyncContentForms.PublishedContent(Items, T("Genre"), J("""{"x":2,"name":"hack"}"""));
        Assert.AreEqual("Genre", (string)published["name"]!);
        Assert.AreEqual(2, (int)published["x"]!);
    }
}

/// <summary>Version skew (§8.12) and its fixtures (§13.8, the pure part).</summary>
[TestClass]
public class VersionSkewTests
{
    private static readonly SyncKey A = K(0xa);

    [TestMethod]
    public void TheContractDecidesWhoIsTooOld()
    {
        Assert.AreEqual(DataSyncLinkState.PeerTooOld, DataSyncVersionSkew.CheckContract(null, null));
        Assert.AreEqual(DataSyncLinkState.PeerTooOld, DataSyncVersionSkew.CheckContract(DataSyncContract.MinimumPeerVersion - 1, 1));
        Assert.AreEqual(DataSyncLinkState.ThisTooOld,
            DataSyncVersionSkew.CheckContract(DataSyncContract.Version + 1, DataSyncContract.Version + 1));
        Assert.IsNull(DataSyncVersionSkew.CheckContract(DataSyncContract.Version, DataSyncContract.MinimumPeerVersion));
    }

    [TestMethod]
    public void ChangedVersionsAreFoundPerKind()
    {
        var current = DataSyncVersionSkew.ComparisonFormVersions([Items, Groups]);
        Assert.AreEqual(1, current[ItemKind]);
        CollectionAssert.AreEqual(new[] { GroupKind, ItemKind },
            DataSyncVersionSkew.ChangedKinds(new Dictionary<string, int>(), current).ToArray(), "nothing recorded yet");
        CollectionAssert.AreEqual(new[] { ItemKind },
            DataSyncVersionSkew.ChangedKinds(new Dictionary<string, int> { [ItemKind] = 0, [GroupKind] = 1 }, current).ToArray());
        Assert.IsTrue(DataSyncVersionSkew.IsComparisonFormMismatch(2, Items));
        Assert.IsFalse(DataSyncVersionSkew.IsComparisonFormMismatch(1, Items));
        Assert.AreEqual(2, DataSyncVersionSkew.SchemaVersions([new TestItemCodec(2)])[ItemKind]);
    }

    [TestMethod]
    public void ANewerSchemaIsHeldTheCursorAdvancesAndAnUpgradeMergesIt()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Genre"), Vv((Self, 1)));
        f.Pull(f.Record(A, T("Genre 2"), Vv((Self, 1), (Peer, 1)), schemaVersion: 2));
        var r = f.Merge();
        var held = r.BaseUpdates.Single().Pending!;
        Assert.AreEqual(DataSyncPendingReason.Held, held.Reason);
        Assert.IsTrue(r.CursorAdvance.ContainsKey(ItemKind));
        Assert.AreEqual(0, r.Batches.Count, "never partially applied");

        // The build upgrades: KindSchemaVersionsJson changes, so the Held record is re-merged (§8.4 condition 6).
        Assert.IsTrue(DataSyncPendingRecords.ShouldRemerge(held, 10, false, DataSyncMergeFlags.None,
            DataSyncVersionSkew.ChangedKinds(new Dictionary<string, int> { [ItemKind] = 1 },
                DataSyncVersionSkew.SchemaVersions([new TestItemCodec(2)])).Contains(ItemKind)));
        var g = new MergeFixture { NoPull = true };
        g.Local("1", A, T("Genre"), Vv((Self, 1)));
        g.Base(A, null, pending: held);
        g.PendingToMerge.Add((ItemKind, A));
        var input = g.Input();
        var upgraded = DataSyncMerger.Merge(input with
        {
            Codecs = new Dictionary<string, IDataSyncKindCodec> { [ItemKind] = new TestItemCodec(2), [GroupKind] = Groups },
        });
        Assert.AreEqual(T("Genre 2"), Items.ReadLocal(((UpdateEntityOperation)upgraded.Batches.Single().Operations.Single()).MergedContent));
    }

    [TestMethod]
    public void AnUnknownTopLevelMemberIsPreservedMergedAndRepublished()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Genre"), Vv((Self, 1)));
        f.Pull(f.Record(A, T("Genre"), Vv((Self, 1), (Peer, 1)), unknown: new JsonObject { ["futureFlag"] = true }));
        var revision = f.Merge().Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.FastForward, revision.Revision);
        Assert.IsTrue(revision.ResultEqualsRemote, "the member is part of the form: taking it reaches the peer's");
        Assert.AreEqual(true, (bool)revision.Unknown!["futureFlag"]!);

        // After a local edit, this (older) build publishes the member back.
        var publication = DataSyncPublication.Of(Items, T("Genre!"), DataSyncOverlay.None, false, null, revision.Unknown);
        Assert.AreEqual(true, (bool)publication.Content!["futureFlag"]!);
    }

    [TestMethod]
    public void AnUnknownEnumOrInvalidContentIsHeldNotApplied()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Genre"), Vv((Self, 1)));
        var bad = f.Record(A, T("Genre"), Vv((Self, 1), (Peer, 1)));
        var content = (JsonObject)bad.Content!.DeepClone();
        content["type"] = 42;                         // not a string: the codec holds the entity
        f.Pull(bad with { Content = content, Hash = ContentHash.Of(content) });

        var r = f.Merge();
        Assert.AreEqual(DataSyncPendingReason.Held, r.BaseUpdates.Single().Pending!.Reason);
        Assert.AreEqual(0, r.Batches.Count);
    }
}

/// <summary>§8.5.6: Convert, phase two, happens when the waiting type change meets a local entity of its type.</summary>
[TestClass]
public class ConvertTests
{
    private static readonly SyncKey A = K(0xa);

    [TestMethod]
    public void AfterPhaseOneTheTypeChangeRecordMergesAsConvert()
    {
        var f = new MergeFixture { NoPull = true };
        var baseRecord = f.Record(A, T("Genre", "#111111", "MultipleChoice", ("1", "Action"), ("2", "Drama")), Vv((Self, 3)));
        var pending = f.Record(A, T("Genre", "#222222", "SingleChoice", ("1", "Action"), ("3", "Comedy")), Vv((Self, 3), (Peer, 1)));
        f.Base(A, baseRecord, childMap: new Dictionary<string, string> { ["1"] = "1", ["2"] = "2" },
            pending: PendingOf(pending, DataSyncPendingReason.TypeChange));
        // Phase one: ChangeType rebuilt the options from this device's values, with fresh ids; name changed here.
        f.Local("1", A, T("Genres", "#111111", "SingleChoice", ("n1", "Action"), ("n2", "Drama")), Vv((Self, 4)));
        f.PendingToMerge.Add((ItemKind, A));

        var r = f.Merge();
        var merged = (TestItemContent)Items.ReadLocal(((UpdateEntityOperation)r.Batches.Single().Operations.Single()).MergedContent);
        Assert.AreEqual("Genres", merged.Name, "name merges three-way against the base before the type change");
        Assert.AreEqual("#222222", merged.Color, "every other scalar takes the record's");
        Assert.AreEqual("SingleChoice", merged.Type);
        CollectionAssert.AreEqual(new[] { "Action", "Drama", "Comedy" }, merged.Children.Select(c => c.Label).ToArray(),
            "children are unioned: nothing deleted");
        Assert.AreEqual(0, r.Inbox.Count, "no FieldConflict for the new type's fields");
        Assert.AreEqual(DataSyncRevisionKind.MergedNoConflict, r.Revisions.Single().Revision);
        Assert.AreEqual(pending, r.BaseUpdates.Single().Record, "base := R; later pulls merge three-way");
    }
}
