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

/// <summary>Records this build does not read are held, never partially applied (§8.12, one contract).</summary>
[TestClass]
public class HeldRecordTests
{
    private static readonly SyncKey A = K(0xa);

    [TestMethod]
    public void ARecordOfAnotherSchemaIsHeldAndTheCursorAdvances()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Genre"), Vv((Self, 1)));
        f.Pull(f.Record(A, T("Genre 2"), Vv((Self, 1), (Peer, 1)), schemaVersion: 2));
        var r = f.Merge();
        Assert.AreEqual(DataSyncPendingReason.Held, r.BaseUpdates.Single().Pending!.Reason);
        Assert.IsTrue(r.CursorAdvance.ContainsKey(ItemKind));
        Assert.AreEqual(0, r.Batches.Count, "never partially applied");
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
