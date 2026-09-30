using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Merging.MergeFixture;

namespace Bakabase.Modules.DataSync.Tests.Merging;

/// <summary>§8.4 rows A1/A2, with gate fix B1(a) for retired actors.</summary>
[TestClass]
public class AnomalyTests
{
    private static readonly SyncKey A = K(0xa), B = K(0xb);

    private static readonly Dictionary<string, long> Own = new()
    {
        [Self.Value] = 10,
        [Retired.Value] = 40,
    };

    // ---- A1 -----------------------------------------------------------------------------------

    [TestMethod]
    public void ACounterAtOrBelowTheRecordedOneIsNoRegression()
    {
        Assert.IsNull(DataSyncAnomalies.FindRegression(
            [("k", A, Vv((Self, 10), (Retired, 40), (Peer, 99)))], Self, Own));
    }

    [TestMethod]
    public void TheCurrentActorAboveItsCounterIsARegression()
    {
        var anomaly = DataSyncAnomalies.FindRegression(
            [("k", A, Vv((Self, 11))), ("k", B, Vv((Self, 14)))], Self, Own)!;
        Assert.AreEqual(DataSyncAnomalies.Regression, anomaly.Code);
        Assert.AreEqual(Self.Value, anomaly.ActorId);
        Assert.AreEqual(14, anomaly.SeenCounter, "the highest counter of the merge");
        Assert.AreEqual(B, anomaly.Key);
    }

    [TestMethod]
    public void ARetiredActorIsComparedWithItsRecordedCounter()
    {
        Assert.IsNull(DataSyncAnomalies.FindRegression([("k", A, Vv((Retired, 40)))], Self, Own),
            "counters lost in one restore are history after the first detection");
        var anomaly = DataSyncAnomalies.FindRegression([("k", A, Vv((Retired, 41)))], Self, Own)!;
        Assert.AreEqual(Retired.Value, anomaly.ActorId);
        Assert.AreEqual(41, anomaly.SeenCounter);
    }

    [TestMethod]
    public void TheCurrentActorIsNamedFirstWhenBothRegressed()
    {
        var anomaly = DataSyncAnomalies.FindRegression([("k", A, Vv((Retired, 90))), ("k", B, Vv((Self, 11)))], Self, Own)!;
        Assert.AreEqual(Self.Value, anomaly.ActorId);
    }

    [TestMethod]
    public void TheMergerReportsARetiredActorsRegression()
    {
        var f = new MergeFixture { ActorCounter = 3 };
        f.RetiredCounters[Retired.Value] = 7;
        f.Local("1", A, T("Genre"), Vv((Retired, 7), (Self, 3)));
        f.Pull(f.Record(A, T("Genre 2"), Vv((Retired, 9), (Self, 3), (Peer, 1))));

        var r = f.Merge();
        Assert.AreEqual(Retired.Value, r.Anomaly!.ActorId);
        Assert.AreEqual(9, r.Anomaly.SeenCounter);
        Assert.IsNull(r.Pause, "evidence about a retired actor never pauses by itself");
    }

    // ---- A2 -----------------------------------------------------------------------------------

    [TestMethod]
    public void OnlyTheActorsOwnerMakesADuplicateActor()
    {
        Assert.IsNull(DataSyncAnomalies.JudgeEqualVectors(true, Self.Value, Self, Peer.Value), "equal forms");
        Assert.AreEqual(DataSyncAnomalies.DuplicateActor,
            DataSyncAnomalies.JudgeEqualVectors(false, Self.Value, Self, Peer.Value), "this device's own actor");
        Assert.AreEqual(DataSyncAnomalies.DuplicateActor,
            DataSyncAnomalies.JudgeEqualVectors(false, Peer.Value, Self, Peer.Value), "the source's own actor");
        Assert.AreEqual(DataSyncAnomalies.Drift,
            DataSyncAnomalies.JudgeEqualVectors(false, Third.Value, Self, Peer.Value), "a third device's, relayed");
        Assert.AreEqual(DataSyncAnomalies.Drift,
            DataSyncAnomalies.JudgeEqualVectors(false, null, Self, Peer.Value), "no editor");
    }

    [TestMethod]
    public void TheSameVectorsRelayedFromAThirdDeviceWithADifferentFormAreDrift()
    {
        var f = new MergeFixture();
        var vv = Vv((Third, 4));
        f.Local("1", A, T("Genre"), vv, lastEditor: ThirdEditor);
        f.Pull(f.Record(A, T("Genre."), vv, editedBy: ThirdEditor));

        var r = f.Merge();
        Assert.IsNull(r.Anomaly);
        Assert.IsNull(r.Pause);
        Assert.AreEqual(DataSyncMergeNoteCodes.NormalizationChanged, r.Notes.Single().Code);
    }

    [TestMethod]
    public void AnEqualBaseVectorWithAnotherFormIsJudgedToo()
    {
        var f = new MergeFixture();
        var baseVv = Vv((Peer, 2));
        f.Base(A, f.Record(A, T("Genre"), baseVv));
        f.Local("1", A, T("Genre"), Vv((Peer, 2), (Self, 3)));
        f.Pull(f.Record(A, T("Different"), baseVv, editedBy: PeerEditor));

        var r = f.Merge();
        Assert.AreEqual(DataSyncAnomalies.DuplicateActor, r.Anomaly!.Code);
        Assert.AreEqual(Peer.Value, r.Anomaly.ActorId);
        Assert.AreEqual(2, r.Anomaly.SeenCounter);
    }

    [TestMethod]
    public void FormsAreRecomputedFromStoredContentNeverTakenFromTheStoredHash()
    {
        var f = new MergeFixture();
        var vv = Vv((Self, 3), (Peer, 2));
        var local = f.Local("1", A, T("Genre"), vv);
        // A stale stored hash must not make equal content look different.
        f.EntitiesOf(ItemKind)[0] = local with { SharedHash = "sha256:" + new string('0', 64) };
        f.Pull(f.Record(A, T("Genre"), vv, editedBy: PeerEditor));

        var r = f.Merge();
        Assert.IsNull(r.Anomaly);
        Assert.AreEqual(0, r.Notes.Count);
    }

    [TestMethod]
    public void AnEntityHeldByTheGuardIsNotJudged()
    {
        // Its content changed without a revision (the lost-update guard held it): equal vectors prove nothing.
        var f = new MergeFixture();
        var vv = Vv((Self, 3), (Peer, 2));
        f.Local("1", A, T("Reverted"), vv, publishHeld: true);
        f.Pull(f.Record(A, T("Genre"), vv, editedBy: PeerEditor));

        var r = f.Merge();
        Assert.IsNull(r.Anomaly);
        Assert.AreEqual(DataSyncPendingReason.PublishHeld, r.BaseUpdates.Single().Pending!.Reason);
    }

    // ---- §5.6 evidence ------------------------------------------------------------------------

    [TestMethod]
    public void ADuplicatedActorIsOwnOnlyWhenItIsOneOfThisDevicesActors()
    {
        Assert.IsTrue(DataSyncAnomalies.IsOwnActor(Retired.Value, Own));
        Assert.IsFalse(DataSyncAnomalies.IsOwnActor(Peer.Value, Own));
    }
}
