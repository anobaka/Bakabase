using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Service.Components.DataSync;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Tests.DataSync.Runtime;

/// <summary>The runtime's parts on their own: the attempt registry, link columns, composition.</summary>
[TestClass]
public class DataSyncRuntimePartsTests
{
    [TestMethod]
    public async Task Attempts_are_replaced_and_cancelled_and_flow_to_the_runner()
    {
        var registry = new DataSyncTaskRegistry();
        Assert.IsFalse(registry.RequestCancel("DataSyncApply"), "unknown");

        var first = registry.Register("DataSyncApply");
        Assert.IsTrue(registry.ShouldRun("DataSyncApply", first.AttemptId));
        var second = registry.Register("DataSyncApply");
        Assert.IsFalse(registry.ShouldRun("DataSyncApply", first.AttemptId), "replaced");
        Assert.IsTrue(registry.ShouldRun("DataSyncApply", second.AttemptId));

        Assert.IsTrue(registry.ShouldRunCurrent(), "outside a task body");
        bool inBody;
        using (DataSyncTaskAttempts.Enter(second))
        {
            inBody = await Task.Run(() => registry.ShouldRunCurrent());
            Assert.IsTrue(registry.RequestCancel("DataSyncApply"));
            Assert.IsFalse(registry.ShouldRunCurrent(), "cancelled");
        }

        Assert.IsTrue(inBody, "the attempt flows with the body's async calls");
        Assert.IsNull(DataSyncTaskAttempts.Current);
        Assert.IsTrue(registry.ShouldRun("DataSyncApply", registry.Register("DataSyncApply").AttemptId),
            "a new attempt starts without the old cancel");
    }

    [TestMethod]
    public void The_kind_content_hash_is_over_key_seq_and_record_hash_in_page_order()
    {
        DataSyncWireRecord Record(string key, long seq, bool deleted = false, string? hash = null, bool held = false) =>
            new([key], "origin", seq, Bakabase.Modules.DataSync.Identity.DataSyncVersionVector.Empty, null, deleted, 1,
                null, null, hash, held ? Bakabase.Modules.DataSync.Planning.DataSyncHeldReason.AtSource : null);

        var hash = DataSyncKindPageReader.KindContentHash([
            Record("k1", 1, hash: "sha256:abc"), Record("k2", 2, deleted: true), Record("k3", 3, held: true),
        ]);

        // §7.5.2 step 6, computed independently of the canonical JSON writer.
        var canonical = "[[\"k1\",1,\"sha256:abc\"],[\"k2\",2,\"tombstone\"],[\"k3\",3,\"held\"]]";
        var expected = "sha256:" + Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
        Assert.AreEqual(expected, hash);

        var reader = new DataSyncKindPageReader([], DataSyncLimits.Default);
        Assert.IsFalse(reader.Supports(DataSyncKindIds.CustomProperty), "a kind without a codec is never requested");
    }

    [TestMethod]
    public void Task_ids_and_their_localization_keys()
    {
        Assert.AreEqual("DataSyncUndo:42", DataSyncTaskIds.Undo(42));
        Assert.AreEqual("DataSyncUndo", DataSyncTaskIds.NameKey(DataSyncTaskIds.Undo(42)));
        Assert.AreEqual("DataSyncReview", DataSyncTaskIds.NameKey(DataSyncTaskIds.Review(1)));
        Assert.IsTrue(DataSyncTaskIds.IsWriteTask(DataSyncTaskIds.Apply));
        Assert.IsTrue(DataSyncTaskIds.IsWriteTask(DataSyncTaskIds.Resolve("b")));
        Assert.IsTrue(DataSyncTaskIds.IsWriteTask(DataSyncTaskIds.Restore));
        Assert.IsFalse(DataSyncTaskIds.IsWriteTask(DataSyncTaskIds.Fetch), "the fetch writes no definitions");
        CollectionAssert.AreEquivalent(new[] {"DataSyncApply", "Enhancement", "SyncResources", "SyncPathMarks"},
            DataSyncTaskIds.WriteConflictKeys.ToArray());
    }

    /// <summary>
    /// What a reader declares to its source (§7.5.6), which the source's readers list words: waiting for its own
    /// first review, or for the source's, are two different things to say there.
    /// </summary>
    [TestMethod]
    public void The_declared_state_tells_whose_first_review_the_link_waits_for()
    {
        DataSyncLinkDbModel Link(DataSyncLinkState state) =>
            new() { PeerNodeId = "p", PeerName = "P", State = state, PausedReason = DataSyncPauseReason.ByUser };

        Assert.AreEqual("ok", Link(DataSyncLinkState.Active).GetDeclaredState(0));
        Assert.AreEqual("needsYou:2", Link(DataSyncLinkState.Active).GetDeclaredState(2));
        Assert.AreEqual("awaitingReview", Link(DataSyncLinkState.AwaitingReview).GetDeclaredState(0));
        Assert.AreEqual("waitingForPeerReview", Link(DataSyncLinkState.WaitingForPeerReview).GetDeclaredState(0));
        Assert.AreEqual("paused:byUser", Link(DataSyncLinkState.Paused).GetDeclaredState(3));
        Assert.AreEqual("ok", Link(DataSyncLinkState.AwaitingAccess).GetDeclaredState(0));
    }

    [TestMethod]
    public void Link_columns_read_tolerantly_and_derive_the_link_facts()
    {
        var link = new DataSyncLinkDbModel
        {
            PeerNodeId = "p", PeerName = "P", KindsJson = "not json", CursorsJson = "{\"customProperty\":4}",
            Mode = DataSyncLinkMode.Follow, State = DataSyncLinkState.Paused,
            PausedReason = DataSyncPauseReason.LocalRestoreSuspected, Initiator = DataSyncLinkInitiator.Peer,
        };
        CollectionAssert.AreEqual(DataSyncKindIds.All.ToArray(), link.GetKinds().ToArray(), "a bad column reads as default");
        Assert.AreEqual(4, link.GetCursors()["customProperty"]);
        Assert.AreEqual("paused:localRestoreSuspected", link.GetDeclaredState(5));
        Assert.AreEqual(DataSyncLinkState.WaitingForPeerReview, link.GetResumeState());
        Assert.IsFalse(link.IsFetchable());


        link.SetCounterpart(new DataSyncFeedCounterpart("follow", true, ["customProperty"]));
        Assert.AreEqual(DataSyncLinkMode.TwoWay, link.GetEffectiveMode());
        Assert.AreEqual("twoWay", link.GetDeclaredMode());
    }
}
