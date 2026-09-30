using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Merging.MergeFixture;

namespace Bakabase.Modules.DataSync.Tests.Merging;

// §8.5.5: appearance fields (the order key, class colours) are never items. One side changed → taken; both → the
// appearance winner, the side whose last editor has the ordinally larger actor id — the local side's LastActorId,
// the peer's R.editedBy.actorId — so two devices merging the same pair crosswise produce the same content.
public partial class MergerTests
{
    [TestMethod]
    [DataRow(DataSyncMerge3Mode.FastForward, "a0", "a1", "a2", DataSyncMergeSide.Local, "a2", DisplayName = "FastForward: the peer's")]
    [DataRow(DataSyncMerge3Mode.Convert, "a0", "a1", "a2", DataSyncMergeSide.Local, "a2", DisplayName = "Convert: the peer's")]
    [DataRow(DataSyncMerge3Mode.ThreeWay, "a0", "a0", "a2", DataSyncMergeSide.Local, "a2", DisplayName = "ThreeWay: only the peer moved")]
    [DataRow(DataSyncMerge3Mode.ThreeWay, "a0", "a1", "a0", DataSyncMergeSide.Remote, "a1", DisplayName = "ThreeWay: only this device moved")]
    [DataRow(DataSyncMerge3Mode.ThreeWay, "a0", "a1", "a2", DataSyncMergeSide.Remote, "a2", DisplayName = "ThreeWay: both, the peer wins")]
    [DataRow(DataSyncMerge3Mode.ThreeWay, "a0", "a1", "a2", DataSyncMergeSide.Local, "a1", DisplayName = "ThreeWay: both, this device wins")]
    [DataRow(DataSyncMerge3Mode.ThreeWay, null, null, "a2", DataSyncMergeSide.Local, "a2", DisplayName = "ThreeWay: a key given where none was")]
    [DataRow(DataSyncMerge3Mode.NoBase, null, null, "a2", DataSyncMergeSide.Local, "a2", DisplayName = "NoBase: only the peer has one")]
    [DataRow(DataSyncMerge3Mode.NoBase, null, "a1", null, DataSyncMergeSide.Remote, "a1", DisplayName = "NoBase: only this device has one")]
    [DataRow(DataSyncMerge3Mode.NoBase, null, "a1", "a2", DataSyncMergeSide.Remote, "a2", DisplayName = "NoBase: both, the peer wins")]
    [DataRow(DataSyncMerge3Mode.NoBase, null, "a1", "a2", DataSyncMergeSide.Local, "a1", DisplayName = "NoBase: both, this device wins")]
    [DataRow(DataSyncMerge3Mode.ThreeWay, "a0", "a1", "a1", DataSyncMergeSide.Remote, "a1", DisplayName = "equal keys")]
    public void Appearance_TheOrderKeyMergesByTheAppearanceRule(DataSyncMerge3Mode mode3, string? baseKey, string? local,
        string? remote, DataSyncMergeSide winner, string? expected) =>
        Assert.AreEqual(expected, DataSyncMergeEngine.MergeOrderKey(mode3, baseKey, local, remote, winner));

    /// <summary>
    /// One device's merge of the other's concurrent recolour and move, against the base both agreed on: the
    /// device's own edit is its local side (last edited by <paramref name="self"/>), the other's its record.
    /// Returns the order key and colour the device ends with.
    /// </summary>
    private static (string? OrderKey, string? Color) MergeAppearance(DataSyncEditorRef self, (string Key, string Color) mine,
        DataSyncEditorRef other, (string Key, string Color) theirs, DataSyncVersionVector mineVv, DataSyncVersionVector theirsVv)
    {
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("Genre", "#e5484d", null), Vv((Peer, 1)), orderKey: "a0", editedBy: PeerEditor));
        var local = T("Genre", mine.Color, null);
        f.Local("1", A, local, mineVv, lastEditor: self, orderKey: mine.Key);
        f.Pull(f.Record(A, T("Genre", theirs.Color, null), theirsVv, orderKey: theirs.Key, editedBy: other));

        var r = f.Merge();
        Assert.AreEqual(0, r.Inbox.Count, "appearance is never an item");
        var revision = r.Revisions.Single();
        var content = Ops(r).OfType<UpdateEntityOperation>().SingleOrDefault() is { } update
            ? (TestItemContent)Items.ReadLocal(update.MergedContent)
            : local;
        return (revision.OrderKey, content.Color);
    }

    [TestMethod]
    public void Appearance_BothDevicesPickTheSameWinnerFromTheSamePair()
    {
        // PC-1 (actor Peer) and PC-2 (actor Third) recolour and move the same entity from one base.
        var onPc1 = Vv((Peer, 2));
        var onPc2 = Vv((Peer, 1), (Third, 1));
        var pc1 = (Key: "a5", Color: "#0090ff");
        var pc2 = (Key: "a3", Color: "#30a46c");

        var mergedOnPc1 = MergeAppearance(PeerEditor, pc1, ThirdEditor, pc2, onPc1, onPc2);
        var mergedOnPc2 = MergeAppearance(ThirdEditor, pc2, PeerEditor, pc1, onPc2, onPc1);

        Assert.AreEqual(mergedOnPc1, mergedOnPc2, "both devices produce the same content from the same pair");
        // The winner is the side whose last editor has the ordinally larger actor id.
        var winner = string.CompareOrdinal(Peer.Value, Third.Value) > 0 ? pc1 : pc2;
        Assert.AreEqual((winner.Key, winner.Color), mergedOnPc1);
    }
}
