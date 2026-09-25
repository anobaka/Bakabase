using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Merging.MergeFixture;

namespace Bakabase.Modules.DataSync.Tests.Merging;

/// <summary>§8.4 rows A1/A2 and the evidence rules of §5.6, with gate fix B1(a) for retired actors.</summary>
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
        Assert.IsNull(DataSyncAnomalies.JudgeEqualVectors(true, Self.Value, Self, Peer.Value, 1, 1), "equal forms");
        Assert.AreEqual(DataSyncAnomalies.DuplicateActor,
            DataSyncAnomalies.JudgeEqualVectors(false, Self.Value, Self, Peer.Value, 1, 1), "this device's own actor");
        Assert.AreEqual(DataSyncAnomalies.DuplicateActor,
            DataSyncAnomalies.JudgeEqualVectors(false, Peer.Value, Self, Peer.Value, 1, 1), "the source's own actor");
        Assert.AreEqual(DataSyncAnomalies.Drift,
            DataSyncAnomalies.JudgeEqualVectors(false, Third.Value, Self, Peer.Value, 1, 1), "a third device's, relayed");
        Assert.AreEqual(DataSyncAnomalies.Drift,
            DataSyncAnomalies.JudgeEqualVectors(false, Peer.Value, Self, Peer.Value, 2, 1), "another form version");
        Assert.AreEqual(DataSyncAnomalies.Drift,
            DataSyncAnomalies.JudgeEqualVectors(false, Peer.Value, Self, Peer.Value, null, 1), "a head that did not say");
        Assert.AreEqual(DataSyncAnomalies.Drift,
            DataSyncAnomalies.JudgeEqualVectors(false, null, Self, Peer.Value, 1, 1), "no editor");
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
    public void EvidenceAtOrBelowTheRecordedCounterDoesNothing()
    {
        Assert.IsFalse(DataSyncAnomalies.IsNewEvidence(null, 10));
        Assert.IsFalse(DataSyncAnomalies.IsNewEvidence(10, 10));
        Assert.IsTrue(DataSyncAnomalies.IsNewEvidence(11, 10));
        Assert.AreEqual(12, DataSyncAnomalies.RetiredCounter(9, 12, 11));
        Assert.AreEqual(9, DataSyncAnomalies.RetiredCounter(9, null, null));
    }

    [TestMethod]
    public void RestoreEvidenceDecidesTheScopeOfThePause()
    {
        static DataSyncRestoreEvidence Peer(string node, bool retired = false) =>
            new(DataSyncRestoreEvidence.Peer, node, retired);

        Assert.IsNull(DataSyncAnomalies.RestorePause([]));
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreSuspected, DataSyncAnomalies.RestorePause([Peer("pc-2")]),
            "one peer: only that link");
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreSuspected,
            DataSyncAnomalies.RestorePause([Peer("pc-2"), Peer("pc-2")]), "the same peer twice is one peer");
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected,
            DataSyncAnomalies.RestorePause([Peer("pc-2"), Peer("nas")]), "two peers");
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected,
            DataSyncAnomalies.RestorePause([Peer("pc-2"), new DataSyncRestoreEvidence(DataSyncRestoreEvidence.Reader, "nas")]),
            "a peer and a reader");
        Assert.AreEqual(DataSyncPauseReason.LocalRestoreDetected,
            DataSyncAnomalies.RestorePause([new DataSyncRestoreEvidence(DataSyncRestoreEvidence.Watermark, null)]),
            "this device's own records");
        Assert.IsNull(DataSyncAnomalies.RestorePause([Peer("pc-2", retired: true), Peer("nas", retired: true)]),
            "evidence about a retired actor never pauses");
    }

    [TestMethod]
    public void ADuplicatedActorIsOwnOnlyWhenItIsOneOfThisDevicesActors()
    {
        Assert.IsTrue(DataSyncAnomalies.IsOwnActor(Retired.Value, Own));
        Assert.IsFalse(DataSyncAnomalies.IsOwnActor(Peer.Value, Own));
    }
}
