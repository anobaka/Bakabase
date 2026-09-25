using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Merging.MergeFixture;

namespace Bakabase.Modules.DataSync.Tests.Merging;

/// <summary>
/// Pending records (§8.4, §7.5.5), the pure half: every reason stores the record once, on its base row, and items
/// refer to it by hash without holding a copy; re-staging judges a stored record like a pull; the re-merge
/// conditions and the flags a record is merged with. The store half is <c>Bakabase.Tests/DataSync/PendingRecordTests</c> [C].
/// </summary>
[TestClass]
public class PendingRecordTests
{
    private static readonly SyncKey A = K(0xa), B = K(0xb);

    [TestMethod]
    public void EveryReasonStoresTheRecordOnceAndItemsCarryOnlyItsHash()
    {
        var cases = new List<(DataSyncPendingReason Reason, DataSyncMergeResult Result)>();

        var conflict = new MergeFixture();
        conflict.Base(A, conflict.Record(A, T("Artist"), Vv((Self, 3))));
        conflict.Local("1", A, T("作者"), Vv((Self, 4)));
        conflict.Pull(conflict.Record(A, T("Artists"), Vv((Self, 3), (Peer, 1))));
        cases.Add((DataSyncPendingReason.Conflict, conflict.Merge()));

        var type = new MergeFixture();
        type.Local("1", A, T("Genre", null, "Tags"), Vv((Self, 1)));
        type.Pull(type.Record(A, T("Genre", null, "Number"), Vv((Self, 1), (Peer, 1))));
        cases.Add((DataSyncPendingReason.TypeChange, type.Merge()));

        var awaiting = new MergeFixture();
        awaiting.Local("1", A, T("Mood"), Vv((Peer, 1)));
        awaiting.Pull(awaiting.Record(A, null, Vv((Peer, 2)), deleted: true));
        cases.Add((DataSyncPendingReason.AwaitingDecision, awaiting.Merge()));

        var identity = new MergeFixture();
        identity.Local("1", A, T("Artist"), Vv((Self, 1)));
        identity.Local("2", B, T("Author"), Vv((Self, 2)));
        identity.Pull(identity.Record(K(0xc), T("Artist"), Vv((Peer, 1)), aliases: [A, B]));
        cases.Add((DataSyncPendingReason.IdentityConflict, identity.Merge()));

        var held = new MergeFixture();
        held.Pull(held.Record(A, T("Future"), Vv((Peer, 1)), schemaVersion: 9));
        cases.Add((DataSyncPendingReason.Held, held.Merge()));

        var frozen = new MergeFixture();
        frozen.Local("1", A, T("Genre"), Vv((Self, 1)), publishHeld: true);
        frozen.Pull(frozen.Record(A, T("Genre!"), Vv((Self, 1), (Peer, 1))));
        cases.Add((DataSyncPendingReason.PublishHeld, frozen.Merge()));

        foreach (var (reason, result) in cases)
        {
            var pending = result.BaseUpdates.Where(u => u.Pending is not null).ToList();
            Assert.AreEqual(1, pending.Count, $"{reason}: stored once");
            Assert.AreEqual(reason, pending[0].Pending!.Reason);
            Assert.AreEqual(DataSyncPendingRecords.RecordHashOf(pending[0].Pending!.Record), pending[0].Pending!.RecordHash);
            foreach (var item in result.Inbox)
            {
                Assert.AreEqual(pending[0].Pending!.RecordHash, item.RecordHash, $"{reason}: items refer by hash");
                Assert.AreEqual(pending[0].Pending!.Record.Vv, item.RecordVv, $"{reason}: and carry its vector");
            }
        }
    }

    [TestMethod]
    public void TheRecordHashCoversTheWholeRecordTombstonesIncluded()
    {
        var f = new MergeFixture();
        var live = f.Record(A, T("Genre"), Vv((Peer, 1)), seq: 5);
        Assert.AreEqual(DataSyncPendingRecords.RecordHashOf(live), DataSyncPendingRecords.RecordHashOf(live with { }));
        Assert.AreNotEqual(DataSyncPendingRecords.RecordHashOf(live),
            DataSyncPendingRecords.RecordHashOf(live with { Vv = Vv((Peer, 2)) }));
        Assert.AreNotEqual(DataSyncPendingRecords.RecordHashOf(live), DataSyncPendingRecords.RecordHashOf(live with { Seq = 6 }));
        var tombstone = f.Record(A, null, Vv((Peer, 2)), deleted: true, seq: 7);
        Assert.IsTrue(DataSyncPendingRecords.RecordHashOf(tombstone).StartsWith("sha256:", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AStoredRecordIsStagedAgainLikeAPull()
    {
        var f = new MergeFixture();
        var future = PendingOf(f.Record(A, T("Genre"), Vv((Peer, 1)), schemaVersion: 2), DataSyncPendingReason.Held);
        Assert.AreEqual(DataSyncHeldReason.NewerSchema, DataSyncPendingRecords.Stage(Items, future, DataSyncLimits.Default).Held);

        // After this build upgrades (schema 2), the same stored record is valid.
        var upgraded = DataSyncPendingRecords.Stage(new TestItemCodec(schemaVersion: 2), future, DataSyncLimits.Default);
        Assert.IsNull(upgraded.Held);
        Assert.AreEqual(T("Genre"), upgraded.Content);

        var tampered = future with { Record = future.Record with { Hash = "sha256:" + new string('1', 64), SchemaVersion = 1 } };
        Assert.AreEqual(DataSyncHeldReason.Invalid, DataSyncPendingRecords.Stage(Items, tampered, DataSyncLimits.Default).Held);
        Assert.AreEqual(DataSyncHeldReason.UnknownKind, DataSyncPendingRecords.Stage(null, future, DataSyncLimits.Default).Held);
    }

    [TestMethod]
    public void FlagsCombineStoredAndOnce()
    {
        var stored = new DataSyncMergeFlags(DeletionsAsItems: true, ChildDeletions: DataSyncChildDeletionMode.Restore);
        var once = new DataSyncMergeFlags(SkipLargeChange: true, ChildDeletions: DataSyncChildDeletionMode.Apply);
        Assert.AreEqual(new DataSyncMergeFlags(true, false, true, DataSyncChildDeletionMode.Restore),
            DataSyncPendingRecords.Combine(stored, once));
        Assert.AreEqual(DataSyncChildDeletionMode.Apply,
            DataSyncPendingRecords.Combine(DataSyncMergeFlags.None, once).ChildDeletions);
    }

    [TestMethod]
    public void TheEvaluatedSeqFollowsTheApplysOwnRevision()
    {
        var f = new MergeFixture();
        var pending = PendingOf(f.Record(A, T("x"), Vv((Peer, 1))), DataSyncPendingReason.Conflict, 10);
        var updates = new[]
        {
            new DataSyncBaseUpdate(ItemKind, A, DataSyncBaseState.Normal, null, null, null, pending, false),
            new DataSyncBaseUpdate(ItemKind, B, DataSyncBaseState.Normal, null, null, null, pending, false),
        };
        var raised = DataSyncPendingRecords.WithEvaluatedSeqs(updates,
            new Dictionary<(string, SyncKey), long> { [(ItemKind, A)] = 14 });
        Assert.AreEqual(14, raised[0].Pending!.EvaluatedAtLocalSeq);
        Assert.AreEqual(10, raised[1].Pending!.EvaluatedAtLocalSeq);
        Assert.IsFalse(DataSyncPendingRecords.ShouldRemerge(raised[0].Pending!, 14, false, DataSyncMergeFlags.None, false),
            "its own revision does not re-merge it next pull");
    }

    [TestMethod]
    public void LargeChangeRecordsApplyOnApplyAllWithoutARefetch()
    {
        var f = new MergeFixture { NoPull = true, LinkFlags = new DataSyncMergeFlags(SkipLargeChange: true) };
        for (var i = 1; i <= 60; i++)
        {
            var key = K(0x400 + i);
            f.Local(i.ToString(), key, T("E" + i), Vv((Self, 1)));
            f.Base(key, f.Record(key, T("E" + i), Vv((Self, 1))),
                pending: PendingOf(f.Record(key, T("E" + i + "!"), Vv((Self, 1), (Peer, i))), DataSyncPendingReason.LargeChange));
            if (DataSyncPendingRecords.ShouldRemerge(f.Bases[(ItemKind, key)].Pending!, 10, false, f.LinkFlags, false))
                f.PendingToMerge.Add((ItemKind, key));
        }

        var r = f.Merge();
        Assert.AreEqual(60, f.PendingToMerge.Count);
        Assert.AreEqual(60, r.Batches.Single().Operations.Count);
        Assert.IsTrue(r.BaseUpdates.All(u => u.Pending is null && u.ClearPending));
        Assert.AreEqual(0, r.Inbox.Count);
    }
}
