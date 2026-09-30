using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Merging.MergeFixture;

namespace Bakabase.Modules.DataSync.Tests.Merging;

/// <summary>
/// A copy once (§8.1): a Follow merge without a base that takes no deletion and removes no child, whose name matches
/// were answered in the preview (§8.3).
/// </summary>
[TestClass]
public class CopyOnceTests
{
    private static readonly SyncKey A = K(0xa), B = K(0xb), C = K(0xc);

    private static MergeFixture CopyOnce(IReadOnlyDictionary<(string, string), string>? links = null,
        params (string Kind, string Key)[] keepBoth) => new()
    {
        Mode = DataSyncLinkMode.Off,
        CopyOnce = new DataSyncCopyOnce(links ?? new Dictionary<(string, string), string>(), keepBoth.ToHashSet()),
    };

    private static IReadOnlyList<ApplyOperation> Ops(DataSyncMergeResult r) => r.Batches.SelectMany(b => b.Operations).ToList();

    [TestMethod]
    public void It_takes_the_peers_values_and_removes_no_child_even_from_a_newer_record()
    {
        var f = CopyOnce();
        // Held from the peer: in Follow, a value another device edited last would ask; a copy once takes it.
        f.Local("1", A, T("Genre", ("c1", "Action"), ("c2", "Drama")), Vv((Peer, 1)), lastEditor: PeerEditor);
        f.Base(A, f.Record(A, T("Genre", ("c1", "Action"), ("c2", "Drama")), Vv((Peer, 1))));
        f.Pull(f.Record(A, T("Genres", ("c1", "Action")), Vv((Peer, 2))));

        var r = f.Merge();
        var update = (UpdateEntityOperation)Ops(r).Single();
        var merged = (TestItemContent)Items.ReadLocal(update.MergedContent);
        Assert.AreEqual("Genres", merged.Name, "Follow takes the peer's value");
        CollectionAssert.AreEquivalent(new[] { "Action", "Drama" }, merged.Children.Select(c => c.Label).ToArray(),
            "the child the peer no longer has stays");
        Assert.AreEqual(0, update.RemovedChildIds.Count);
        Assert.AreEqual(0, r.Inbox.Count);
    }

    [TestMethod]
    public void It_takes_no_deletion_from_either_side()
    {
        var f = CopyOnce();
        f.Local("1", A, T("Mood"), Vv((Peer, 1)), createdBySync: true);
        f.ValueCounts[(ItemKind, "1")] = 0;
        f.Pull(f.Record(A, null, Vv((Peer, 2)), deleted: true));
        f.Tombstone(B, Vv((Self, 3)));
        f.Pull(f.Record(B, T("Deleted here"), Vv((Peer, 4))));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count + r.Revisions.Count + r.Inbox.Count + r.BaseUpdates.Count);
    }

    [TestMethod]
    public void Its_name_matches_go_as_the_preview_answered_them()
    {
        var f = CopyOnce(new Dictionary<(string, string), string> { [(ItemKind, B.Value)] = "1" }, (ItemKind, C.Value));
        f.Local("1", A, T("Rating"), Vv((Self, 1)));
        f.Local("2", K(0xd), T("Genre"), Vv((Self, 2)));
        f.Pull(f.Record(B, T("Rating", ("c1", "Good")), Vv((Peer, 1))));
        f.Pull(f.Record(C, T("Genre"), Vv((Peer, 2))));

        var r = f.Merge();
        var linked = (UpdateEntityOperation)Ops(r).Single(o => o is UpdateEntityOperation);
        Assert.AreEqual("1", linked.LocalKey);
        CollectionAssert.AreEqual(new[] { B }, linked.AliasKeysToAdd.All.ToArray());
        var kept = (CreateEntityOperation)Ops(r).Single(o => o is CreateEntityOperation);
        Assert.AreEqual("Genre (PC-1)", ((TestItemContent)Items.ReadLocal(kept.Content)).Name);
        Assert.AreEqual(0, r.Inbox.Count);
    }

    [TestMethod]
    public void A_name_match_the_preview_left_open_waits_as_a_suggestion()
    {
        var f = CopyOnce();
        f.Local("1", A, T("Rating"), Vv((Self, 1)));
        f.Pull(f.Record(B, T("Rating"), Vv((Peer, 1))));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count);
        Assert.AreEqual(DataSyncInboxItemType.LinkSuggestion, r.Inbox.Single().Type);
    }
}
