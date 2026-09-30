using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Merging.MergeFixture;

namespace Bakabase.Modules.DataSync.Tests.Merging;

// "Sync the definition only" (§3.6): a shared field the merger carries beside the content. The test kind offers it
// as the custom property kind does, so these cases pin the merger's own handling — the merged flag, the note when
// it turns off, creates — whatever codec is plugged in.
public partial class MergerTests
{
    [TestMethod]
    public void ChildrenLocal_FastForwardTakesTheRecordsFlagAndLeavesTheChildren()
    {
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("Genre", ("1", "Action")), Vv((Self, 3))));
        f.Local("1", A, T("Genre", ("1", "Action")), Vv((Self, 3)));
        f.UseAllChildren(0);
        var record = f.Pull(f.Record(A, T("Genre", ("1", "Action")), Vv((Self, 3), (Peer, 1)), childrenLocal: true));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count, "no child is removed: the peer's missing children say nothing");
        var revision = r.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.FastForward, revision.Revision);
        Assert.IsTrue(revision.ChildrenLocal);
        Assert.IsTrue(revision.ResultEqualsRemote);
        Assert.AreEqual(record, BaseOf(r, A).Record);
        Assert.AreEqual(0, r.Notes.Count);
    }

    [TestMethod]
    public void ChildrenLocal_ThreeWayTakesAOneSidedChange()
    {
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("Genre", ("1", "Action")), Vv((Self, 3))));
        f.Local("1", A, T("类型", ("1", "Action"), ("2", "Drama")), Vv((Self, 4)));
        f.UseAllChildren(0);
        f.Pull(f.Record(A, T("Genre", ("1", "Action")), Vv((Self, 3), (Peer, 1)), childrenLocal: true));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count, "the local name and children stay");
        var revision = r.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.MergedNoConflict, revision.Revision);
        Assert.IsTrue(revision.ChildrenLocal, "the peer turned it on");
        Assert.AreEqual(0, r.Inbox.Count);

        // The other way round: this device turned it on, the peer changed something else — kept on.
        var g = new MergeFixture();
        g.Base(A, g.Record(A, T("Genre", ("1", "Action")), Vv((Self, 3))));
        g.Local("1", A, T("Genre", ("1", "Action")), Vv((Self, 4)), childrenLocal: true);
        g.UseAllChildren(0);
        g.Pull(g.Record(A, T("Genres", ("1", "Action"), ("2", "Drama")), Vv((Self, 3), (Peer, 1))));

        var kept = g.Merge();
        Assert.AreEqual(T("Genres", ("1", "Action")),
            Items.ReadLocal(((UpdateEntityOperation)Ops(kept).Single()).MergedContent), "no child added while it holds");
        Assert.IsTrue(kept.Revisions.Single().ChildrenLocal);
        Assert.AreEqual(0, kept.Notes.Count);
    }

    [TestMethod]
    public void ChildrenLocal_AConflictKeepsTheLocalFlag()
    {
        // Without a base, a flag set on one side only is concurrent: a question, the local value kept meanwhile.
        var f = new MergeFixture();
        f.Local("1", A, T("Genre", ("1", "Action")), Vv((Self, 3)), childrenLocal: true);
        f.UseAllChildren(0);
        f.Pull(f.Record(A, T("Genre", ("1", "Action"), ("2", "Drama")), Vv((Peer, 2))));

        var r = f.Merge();
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.FieldConflict, item.Type);
        Assert.AreEqual(TestItemCodec.ChildrenLocalPath, item.SubjectPath);
        Assert.AreEqual(true, item.Payload.Fields.Single().Local!.Flag);
        Assert.AreEqual(false, item.Payload.Fields.Single().Remote!.Flag);
        Assert.AreEqual(0, Ops(r).Count, "the children stay as they are while it holds here");
        Assert.AreEqual(DataSyncPendingReason.Conflict, BaseOf(r, A).Pending!.Reason);
        Assert.IsTrue(r.Revisions.All(x => x.ChildrenLocal == true));
    }

    [TestMethod]
    public void ChildrenLocal_TurnedOffUnionsTheChildrenAndSaysSo()
    {
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("Genre"), Vv((Self, 3)), childrenLocal: true));
        f.Local("1", A, T("Genre", ("1", "Action"), ("3", "Horror")), Vv((Self, 3)), childrenLocal: true);
        f.UseAllChildren(0);
        f.Pull(f.Record(A, T("Genre", ("1", "Action"), ("2", "Drama")), Vv((Self, 3), (Peer, 1))));

        var r = f.Merge();
        var update = (UpdateEntityOperation)Ops(r).Single();
        Assert.AreEqual(T("Genre", ("1", "Action"), ("3", "Horror"), ("2", "Drama")), Items.ReadLocal(update.MergedContent),
            "a union: nothing of this device's is removed");
        Assert.AreEqual(0, update.RemovedChildIds.Count);
        var revision = r.Revisions.Single();
        Assert.AreEqual(false, revision.ChildrenLocal);
        Assert.AreEqual(DataSyncMergeNoteCodes.ChildrenLocalTurnedOff, r.Notes.Single().Code);
    }

    [TestMethod]
    public void ChildrenLocal_ACreateTakesTheRecordsFlag()
    {
        var f = new MergeFixture();
        f.Pull(f.Record(A, T("Mood", ("1", "Calm")), Vv((Peer, 1)), childrenLocal: true));

        var r = f.Merge();
        var create = (CreateEntityOperation)Ops(r).Single();
        Assert.AreEqual(T("Mood"), Items.ReadLocal(create.Content), "the flag is the side row's, not the content's");
        var revision = r.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.Create, revision.Revision);
        Assert.AreEqual(true, revision.ChildrenLocal);
        Assert.IsTrue(revision.ResultEqualsRemote);
    }
}
