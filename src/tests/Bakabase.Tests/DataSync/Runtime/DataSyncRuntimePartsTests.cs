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

/// <summary>The runtime's parts on their own: the staged-pull store, the attempt registry, link columns, composition.</summary>
[TestClass]
public class DataSyncRuntimePartsTests
{
    private static DataSyncStagedPull Pull(string peer, long maxSeq = 5) =>
        new(peer, peer,
            new DataSyncFeedManifest("snap", 1, peer, "e", "0123456789abcdef", 1, 1, "v", [], null,
                new DataSyncSourceAttention(false, 0, 0, false, 0)),
            [new DataSyncStagedKind(DataSyncKindIds.CustomProperty, 1, true, null, [], maxSeq, false)],
            DateTime.UtcNow);

    [TestMethod]
    public void Staged_pulls_one_per_link_bounded_each_and_in_total()
    {
        var limits = new DataSyncLimits {MaxStagedPullBytes = 100};
        var store = new DataSyncStagedPullStore(limits, maxTotalBytes: 250);

        Assert.IsTrue(store.Put(1, Pull("a"), 100));
        var newer = Pull("a", 7);
        Assert.IsTrue(store.Put(1, newer, 100));
        Assert.AreSame(newer, store.Peek(1), "a newer pull replaces the link's pull");
        Assert.IsFalse(store.Put(2, Pull("b"), 101), "larger than one link may stage");
        Assert.IsNull(store.Peek(2));

        Assert.IsTrue(store.Put(2, Pull("b"), 100));
        Assert.IsTrue(store.Put(3, Pull("c"), 100));
        CollectionAssert.AreEqual(new[] {2, 3}, store.LinksWaiting().ToArray(), "over the total, the oldest is dropped");
        Assert.AreEqual(200, store.TotalBytes);

        Assert.IsNotNull(store.Take(2));
        Assert.IsNull(store.Take(2));
        CollectionAssert.AreEqual(new[] {3}, store.LinksWaiting().ToArray());
        IDataSyncStagedPullStore asInterface = store;
        asInterface.Put(4, Pull("d"));
        Assert.IsNotNull(store.Peek(4), "the interface's own Put estimates the size");
    }

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
                null, null, hash, held ? Bakabase.Modules.DataSync.Planning.DataSyncHeldReason.AtSource : null, 0);

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
        Assert.AreEqual("DataSyncReview", DataSyncTaskIds.NameKey(DataSyncTaskIds.Review("r1")));
        Assert.IsTrue(DataSyncTaskIds.IsWriteTask(DataSyncTaskIds.Apply));
        Assert.IsTrue(DataSyncTaskIds.IsWriteTask(DataSyncTaskIds.Resolve("b")));
        Assert.IsTrue(DataSyncTaskIds.IsWriteTask(DataSyncTaskIds.Restore));
        Assert.IsFalse(DataSyncTaskIds.IsWriteTask(DataSyncTaskIds.Fetch), "the fetch writes no definitions");
        CollectionAssert.AreEquivalent(new[] {"DataSyncApply", "Enhancement", "SyncResources", "SyncPathMarks"},
            DataSyncTaskIds.WriteConflictKeys.ToArray());
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

        link.SetOnceFlags(new DataSyncMergeFlags(DeletionsAsItems: true, SkipLargeChange: true));
        Assert.AreEqual(DataSyncMergeFlags.None with {SkipLargeChange = true}, link.GetOnceFlags().PullIndependent());
        Assert.AreEqual(DataSyncMergeFlags.None with {DeletionsAsItems = true},
            link.GetOnceFlags().Without(link.GetOnceFlags().PullIndependent()));
        link.SetOnceFlags(DataSyncMergeFlags.None);
        Assert.IsNull(link.OnceFlagsJson);

        link.SetCounterpart(new DataSyncFeedCounterpart("follow", true, ["customProperty"]));
        Assert.AreEqual(DataSyncLinkMode.TwoWay, link.GetEffectiveMode());
        Assert.AreEqual("twoWay", link.GetDeclaredMode());
        link.FirstContactCompletedAtUtc = DateTime.UtcNow;
        Assert.AreEqual(0, link.GetKindsAwaitingFirstContact().Count,
            "a completed first contact without a kind list never sends the link back to a review");
    }

    [TestMethod]
    public async Task The_runtime_composes_in_the_test_kit_with_the_only_grant_events()
    {
        IServiceCollection? registered = null;
        var sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.AddDataSyncRuntime();
            services.AddDataSyncRuntime();
            services.AddDataSyncServiceComponents();
            registered = services;
        });

        Assert.IsInstanceOfType<DataSyncGrantEventsHandler>(sp.GetRequiredService<IDataSyncGrantEvents>());
        Assert.AreEqual(1, registered!.Count(d => d.ServiceType == typeof(IDataSyncGrantEvents)));
        Assert.AreEqual(1, registered!.Count(d => d.ServiceType == typeof(IHostedService) &&
                                                  d.ImplementationFactory?.GetType().GenericTypeArguments.Last() ==
                                                  typeof(DataSyncScheduler)), "the scheduler is hosted once");
        var fetchTask = sp.GetServices<IPredefinedBTaskBuilder>().OfType<DataSyncFetchTask>().Single();
        Assert.IsTrue(fetchTask.IsEnabled(), "AddBTask discovers the DataSync task; the runtime enables it");
        Assert.AreEqual(DataSyncRuntimeState.FetchInterval, fetchTask.GetInterval());
        CollectionAssert.AreEquivalent(new[] {"DataSync"}, fetchTask.ConflictKeys!.ToArray());
        Assert.IsNotNull(sp.GetRequiredService<DataSyncScheduler>());
    }

    [TestMethod]
    public void Without_the_runtime_the_DataSync_task_stays_disabled()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var fetchTask = new DataSyncFetchTask(provider, new Bakabase.TestKit.Implementations.TestBakabaseLocalizer());
        Assert.IsFalse(fetchTask.IsEnabled(), "a host that does not register the runtime never runs the task");
    }
}
