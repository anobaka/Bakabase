using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Merging.MergeFixture;

namespace Bakabase.Modules.DataSync.Tests.Merging;

// §8.4 row K6 with conflicts: the base stays at the last agreement, and what the merge applied is remembered with
// the Conflict pending record (its applied base), so later merges of the entity do not take it again.
public partial class MergerTests
{
    /// <summary>The last agreement, B = {A, c1=x} (the same record at every call).</summary>
    private static DataSyncWireRecord AgreedBase => new MergeFixture().Record(A, T("A", ("c1", "x")), Vv((Self, 1)));

    /// <summary>The first merge: B = {A, c1=x}, L = {Y, c1=x}, R = {X, c1=y}. The name conflicts; c1 applies.</summary>
    private static (MergeFixture Fixture, DataSyncWireRecord Record, DataSyncMergeResult Result) ConflictedMerge()
    {
        var f = new MergeFixture();
        f.Base(A, AgreedBase, childMap: new Dictionary<string, string> { ["c1"] = "c1" });
        f.Local("1", A, T("Y", ("c1", "x")), Vv((Self, 2)));
        f.UseAllChildren(0);
        var record = f.Pull(f.Record(A, T("X", ("c1", "y")), Vv((Self, 1), (Peer, 1))));
        return (f, record, f.Merge());
    }

    /// <summary>
    /// The state the first merge left: the base at B with <paramref name="pending"/> waiting on it, and the entity
    /// holding <paramref name="local"/> (the person's edits since, a newer local vector).
    /// </summary>
    private static MergeFixture AfterConflict(DataSyncPendingRecord pending, object local)
    {
        var f = new MergeFixture();
        f.Base(A, AgreedBase, childMap: new Dictionary<string, string> { ["c1"] = "c1" }, pending: pending);
        f.Local("1", A, local, Vv((Self, 5)), seq: 30);
        f.UseAllChildren(0);
        return f;
    }

    [TestMethod]
    public void K6_AConflictRemembersWhatItAppliedWithThePendingRecord()
    {
        var (_, record, r) = ConflictedMerge();
        Assert.AreEqual(T("Y", ("c1", "y")), Items.ReadLocal(((UpdateEntityOperation)Ops(r).Single()).MergedContent));
        var b = BaseOf(r, A);
        Assert.IsNull(b.Record, "the base stays at the last agreement");
        var applied = b.Pending!.AppliedBase!;
        Assert.AreEqual(record, applied.Record, "what applied is the record's safe part");
        Assert.AreEqual("name", applied.KeptPaths.Single().Path, "the conflicting path did not apply");
        Assert.AreEqual("A", applied.KeptPaths.Single().Base!.Text, "shown with the base's value");
    }

    [TestMethod]
    public void K6_AnAppliedPathChangedBackHereIsNotTakenAgainWhenTheConflictIsReMerged()
    {
        // The person renames c1 back to x and settles the name on X. Merged again against B, c1 was y again and the
        // revision, which dominates R, spread the revert of their edit everywhere.
        var (_, record, first) = ConflictedMerge();
        var f = AfterConflict(BaseOf(first, A).Pending!, T("X", ("c1", "x")));
        f.NoPull = true;
        f.PendingToMerge.Add((ItemKind, A));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count, "nothing of the record applies again");
        var revision = r.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.MergedNoConflict, revision.Revision);
        Assert.IsTrue(revision.ResultEqualsLocal);
        Assert.IsFalse(revision.ResultEqualsRemote, "c1 stays this device's edit");
        Assert.AreEqual(record, BaseOf(r, A).Record, "no conflict is left: the base takes the record");
        Assert.IsNull(BaseOf(r, A).Pending);
        Assert.AreEqual(0, r.Inbox.Count);
    }

    [TestMethod]
    public void K6_ANewerRecordWhileAConflictIsOpenMergesAgainstWhatApplied()
    {
        // The name conflict is still open and c1 was renamed back here; the peer then adds c3.
        var (_, _, first) = ConflictedMerge();
        var f = AfterConflict(BaseOf(first, A).Pending!, T("Y", ("c1", "x")));
        var next = f.Pull(f.Record(A, T("X", ("c1", "y"), ("c3", "z")), Vv((Self, 1), (Peer, 2))));

        var r = f.Merge();
        var update = (UpdateEntityOperation)Ops(r).Single();
        Assert.AreEqual(T("Y", ("c1", "x"), ("c3", "z")), Items.ReadLocal(update.MergedContent),
            "the peer's new child applies; c1 keeps this device's edit");
        Assert.AreEqual(DataSyncRevisionKind.MergedWithConflicts, r.Revisions.Single().Revision);
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.FieldConflict, item.Type);
        Assert.AreEqual("name", item.SubjectPath);
        var field = item.Payload.Fields.Single();
        Assert.AreEqual(DataSyncFieldResolution.Conflict, field.Resolution);
        Assert.AreEqual("A", field.Base!.Text, "the card shows the base's value, as when it was first asked");
        var b = BaseOf(r, A);
        Assert.IsNull(b.Record);
        Assert.AreEqual(next, b.Pending!.Record);
        Assert.AreEqual(next, b.Pending.AppliedBase!.Record);
        Assert.AreEqual("A", b.Pending.AppliedBase.KeptPaths.Single().Base!.Text);
    }

    [TestMethod]
    public void K6_TheSameConflictDerivedAgainKeepsItsToken()
    {
        // A full reconciliation re-sends the record: the item is derived again with the card it had.
        var (_, record, first) = ConflictedMerge();
        var f = AfterConflict(BaseOf(first, A).Pending!, T("Y", ("c1", "y")));
        f.Pull(record with { Seq = record.Seq + 7 });
        f.FullReconciliation.Add(ItemKind);

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count + r.Revisions.Count);
        Assert.AreEqual(first.Inbox.Single().Token, r.Inbox.Single().Token);
        Assert.AreEqual(DataSyncPendingReason.Conflict, BaseOf(r, A).Pending!.Reason);
    }

    [TestMethod]
    public void K6_ANameBothSidesNowAgreeOnIsNoLongerAConflict()
    {
        // The peer, too, moves on to the name this device kept.
        var (_, _, first) = ConflictedMerge();
        var f = AfterConflict(BaseOf(first, A).Pending!, T("Y", ("c1", "y")));
        var next = f.Pull(f.Record(A, T("Y", ("c1", "y")), Vv((Self, 1), (Peer, 2))));

        var r = f.Merge();
        Assert.AreEqual(0, r.Inbox.Count);
        Assert.AreEqual(next, BaseOf(r, A).Record);
        Assert.IsNull(BaseOf(r, A).Pending);
    }

    [TestMethod]
    public void AWaitThatReplacesAConflictKeepsItsAppliedBase()
    {
        // Row F: the lost-update guard holds the entity, and the peer's next record waits as PublishHeld. When it is
        // merged later, what the conflicted merge applied must still count as applied.
        var (_, _, first) = ConflictedMerge();
        var conflict = BaseOf(first, A).Pending!;
        var f = new MergeFixture();
        f.Base(A, AgreedBase, pending: conflict);
        f.Local("1", A, T("Y", ("c1", "y")), Vv((Self, 5)), publishHeld: true);
        f.Pull(f.Record(A, T("X", ("c1", "y"), ("c3", "z")), Vv((Self, 1), (Peer, 2))));

        var b = BaseOf(f.Merge(), A);
        Assert.AreEqual(DataSyncPendingReason.PublishHeld, b.Pending!.Reason);
        Assert.AreEqual(conflict.AppliedBase, b.Pending.AppliedBase);
    }

    [TestMethod]
    public void ARecordChangedDuringApplyKeepsTheAppliedBaseThatStood()
    {
        // Nothing of the merge applied, so the Retry record keeps what applied before, never what this merge
        // would have made.
        var (_, _, first) = ConflictedMerge();
        var conflict = BaseOf(first, A).Pending!;
        var f = AfterConflict(conflict, T("Y", ("c1", "x")));
        f.Pull(f.Record(A, T("X", ("c1", "y"), ("c3", "z")), Vv((Self, 1), (Peer, 2))));
        var input = f.Input();
        var r = DataSyncMerger.Merge(input);
        var changed = r.Batches.SelectMany(x => x.Operations).Select(o => o.ItemId).ToList();

        var retry = DataSyncRecordApply.WithoutChangedDuringApply(r, changed, input.Bases).BaseUpdates.Single().Pending!;
        Assert.AreEqual(DataSyncPendingReason.Retry, retry.Reason);
        Assert.AreEqual(conflict.AppliedBase, retry.AppliedBase);

        var fresh = ConflictedMerge();
        var noneBefore = DataSyncRecordApply.WithoutChangedDuringApply(fresh.Result,
            fresh.Result.Batches.SelectMany(x => x.Operations).Select(o => o.ItemId).ToList(), fresh.Fixture.Input().Bases);
        Assert.IsNull(noneBefore.BaseUpdates.Single().Pending!.AppliedBase, "the first conflict applied nothing");
    }
}
