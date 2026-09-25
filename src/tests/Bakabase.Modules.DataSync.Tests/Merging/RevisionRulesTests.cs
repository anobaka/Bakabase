using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Identity.VersionVectorTests;

namespace Bakabase.Modules.DataSync.Tests.Merging;

[TestClass]
public class RevisionRulesTests
{
    private static readonly DataSyncActorId Self = Actor(0x5e1f);
    private static readonly DataSyncActorId Peer = Actor(0xbee);
    private static readonly DataSyncActorId Third = Actor(0x3);

    /// <summary>Issues counters like the local state row: one above the last, and counts how often it was asked.</summary>
    private sealed class Counter(long last)
    {
        public long Last { get; private set; } = last;
        public int Issued { get; private set; }

        public long Next()
        {
            Issued++;
            return ++Last;
        }
    }

    private static DataSyncVersionVector Next(DataSyncRevisionKind kind, DataSyncVersionVector local,
        DataSyncVersionVector? remote, Counter counter, bool resultEqualsRemote = false, bool resultEqualsLocal = false,
        DataSyncVersionVector? tombstone = null) =>
        DataSyncRevisionRules.Next(kind, local, remote, resultEqualsRemote, resultEqualsLocal, Self, counter.Next,
            tombstone);

    // ---- each row of §2.8 --------------------------------------------------------------------

    [TestMethod]
    [DataRow(DataSyncRevisionKind.LocalEdit)]
    [DataRow(DataSyncRevisionKind.LocalDelete)]
    [DataRow(DataSyncRevisionKind.Undo)]
    public void LocalRevisionsAddOwnCounter(DataSyncRevisionKind kind)
    {
        var counter = new Counter(7);
        var local = Vv((Self, 3), (Peer, 2));
        var remote = Vv((Peer, 9));
        Assert.AreEqual(Vv((Self, 8), (Peer, 2)), Next(kind, local, remote, counter, resultEqualsRemote: true));
        Assert.AreEqual(1, counter.Issued);
    }

    [TestMethod]
    [DataRow(DataSyncRevisionKind.Create)]
    [DataRow(DataSyncRevisionKind.FastForward)]
    public void CreateAndFastForwardAdoptTheRemoteVectorWhenTheResultEqualsIt(DataSyncRevisionKind kind)
    {
        var local = kind == DataSyncRevisionKind.Create ? DataSyncVersionVector.Empty : Vv((Peer, 1));
        var remote = Vv((Peer, 4), (Third, 1));

        var same = new Counter(10);
        Assert.AreEqual(remote, Next(kind, local, remote, same, resultEqualsRemote: true));
        Assert.AreEqual(0, same.Issued);

        var drift = new Counter(10);
        Assert.AreEqual(Vv((Peer, 4), (Third, 1), (Self, 11)), Next(kind, local, remote, drift));
        Assert.AreEqual(1, drift.Issued);
    }

    [TestMethod]
    public void FastForwardRefusesARemoteThatDoesNotIncludeLocal()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            Next(DataSyncRevisionKind.FastForward, Vv((Self, 2)), Vv((Peer, 4)), new Counter(2),
                resultEqualsRemote: true));
        Assert.ThrowsException<ArgumentException>(() =>
            Next(DataSyncRevisionKind.Create, Vv((Self, 2)), Vv((Peer, 4)), new Counter(2), resultEqualsRemote: true));
    }

    [TestMethod]
    public void ReviveAdoptsTheRemoteOnlyWhenItIncludesTheTombstone()
    {
        var tombstone = Vv((Self, 5), (Peer, 1));
        var dominating = Vv((Self, 5), (Peer, 3));
        var counter = new Counter(5);
        Assert.AreEqual(dominating, Next(DataSyncRevisionKind.Revive, DataSyncVersionVector.Empty, dominating, counter,
            resultEqualsRemote: true, tombstone: tombstone));
        Assert.AreEqual(0, counter.Issued);

        // The remote never saw the deletion: the revived entity must still dominate the tombstone.
        var older = Vv((Peer, 3));
        Assert.AreEqual(Vv((Self, 6), (Peer, 3)), Next(DataSyncRevisionKind.Revive, DataSyncVersionVector.Empty, older,
            new Counter(5), resultEqualsRemote: true, tombstone: tombstone));

        // Not equal to the remote: always a counter of its own.
        Assert.AreEqual(Vv((Self, 6), (Peer, 3)), Next(DataSyncRevisionKind.Revive, DataSyncVersionVector.Empty,
            dominating, new Counter(5), tombstone: tombstone));
    }

    [TestMethod]
    public void MergedNoConflictTakesTheMaxAndAddsACounterOnlyWhenTheResultIsNew()
    {
        var local = Vv((Self, 2), (Peer, 1));
        var remote = Vv((Peer, 3), (Third, 1));
        var same = new Counter(2);
        Assert.AreEqual(Vv((Self, 2), (Peer, 3), (Third, 1)),
            Next(DataSyncRevisionKind.MergedNoConflict, local, remote, same, resultEqualsRemote: true));
        Assert.AreEqual(0, same.Issued);
        Assert.AreEqual(Vv((Self, 3), (Peer, 3), (Third, 1)),
            Next(DataSyncRevisionKind.MergedNoConflict, local, remote, new Counter(2)));
    }

    [TestMethod]
    public void FollowMergedAlwaysAddsACounter()
    {
        var local = Vv((Self, 2), (Peer, 1));
        var remote = Vv((Peer, 3));
        var counter = new Counter(2);
        Assert.AreEqual(Vv((Self, 3), (Peer, 3)),
            Next(DataSyncRevisionKind.FollowMerged, local, remote, counter, resultEqualsRemote: true,
                resultEqualsLocal: true));
        Assert.AreEqual(1, counter.Issued);
    }

    [TestMethod]
    public void TwoDevicesYieldingToEachOtherNeverEndWithEqualVectors()
    {
        // Engineering B8: each device follows the other and yields a field. Their results have each seen both sides,
        // and each carries its own new counter, so the vectors differ instead of colliding with swapped contents.
        var start = Vv((Self, 1), (Peer, 1));
        var selfSide = start.With(Self, 2);
        var peerSide = start.With(Peer, 2);
        var selfResult = DataSyncRevisionRules.Next(DataSyncRevisionKind.FollowMerged, selfSide, peerSide, false, false,
            Self, new Counter(2).Next);
        var peerResult = DataSyncRevisionRules.Next(DataSyncRevisionKind.FollowMerged, peerSide, selfSide, false, false,
            Peer, new Counter(2).Next);
        Assert.AreNotEqual(selfResult, peerResult);
        Assert.AreEqual(DataSyncVvRelation.Concurrent, selfResult.CompareTo(peerResult));
    }

    [TestMethod]
    public void MergedWithConflictsNeverAbsorbsThePeersCounters()
    {
        var local = Vv((Self, 2), (Peer, 1));
        var remote = Vv((Peer, 5), (Third, 2));
        var counter = new Counter(2);
        Assert.AreSame(local, Next(DataSyncRevisionKind.MergedWithConflicts, local, remote, counter,
            resultEqualsLocal: true));
        Assert.AreEqual(0, counter.Issued);
        Assert.AreEqual(Vv((Self, 3), (Peer, 1)),
            Next(DataSyncRevisionKind.MergedWithConflicts, local, remote, new Counter(2)));
    }

    [TestMethod]
    [DataRow(DataSyncRevisionKind.Resolution)]
    [DataRow(DataSyncRevisionKind.KeepDeleted)]
    public void ResolutionAndKeepDeletedTakeTheMaxAndAddACounter(DataSyncRevisionKind kind)
    {
        var local = Vv((Self, 2), (Peer, 1));
        var resolved = Vv((Peer, 4), (Third, 1));
        Assert.AreEqual(Vv((Self, 3), (Peer, 4), (Third, 1)),
            Next(kind, local, resolved, new Counter(2), resultEqualsRemote: true));
        // No resolved record (a state-derived item): nothing to absorb.
        Assert.AreEqual(Vv((Self, 3), (Peer, 1)), Next(kind, local, null, new Counter(2)));
    }

    [TestMethod]
    public void AcceptRemoteDeleteTakesTheMaxWithoutACounter()
    {
        var counter = new Counter(2);
        Assert.AreEqual(Vv((Self, 2), (Peer, 6)),
            Next(DataSyncRevisionKind.AcceptRemoteDelete, Vv((Self, 2), (Peer, 1)), Vv((Self, 2), (Peer, 6)), counter));
        Assert.AreEqual(0, counter.Issued);
    }

    [TestMethod]
    public void RetireTakesTheMaxOfLiveAndTombstoneWithoutACounter()
    {
        var counter = new Counter(9);
        Assert.AreEqual(Vv((Self, 4), (Peer, 7)),
            Next(DataSyncRevisionKind.Retire, Vv((Self, 4), (Peer, 1)), null, counter,
                tombstone: Vv((Self, 3), (Peer, 7))));
        Assert.AreEqual(0, counter.Issued);
    }

    [TestMethod]
    public void RestoreWinsDominatesEveryKnownVectorAndEveryRetiredCounter()
    {
        var retiredActor = Actor(0x01d);
        var local = Vv((retiredActor, 3), (Peer, 1));
        var baseVv = Vv((retiredActor, 5), (Peer, 2));
        var pending = Vv((retiredActor, 4), (Peer, 6));
        var item = Vv((Third, 1));
        // The device issued counters up to 9 under its old actor before the restore; only 5 reached anyone.
        var retired = new Dictionary<string, long> { [retiredActor.Value] = 9, [Actor(0x02d).Value] = 0 };

        var remote = DataSyncRevisionRules.RestoreWinsRemote([baseVv, pending, item], retired);
        Assert.AreEqual(Vv((retiredActor, 9), (Peer, 6), (Third, 1)), remote);

        var result = Next(DataSyncRevisionKind.RestoreWins, local, remote, new Counter(0));
        Assert.AreEqual(Vv((retiredActor, 9), (Peer, 6), (Third, 1), (Self, 1)), result);
        foreach (var known in new[] { local, baseVv, pending, item, Vv((retiredActor, 9)) })
            Assert.AreEqual(DataSyncVvRelation.Dominates, result.CompareTo(known));
    }

    [TestMethod]
    public void RestoreWinsRemoteKeepsAHigherObservedCounter()
    {
        var retiredActor = Actor(0x01d);
        var remote = DataSyncRevisionRules.RestoreWinsRemote([Vv((retiredActor, 12))],
            new Dictionary<string, long> { [retiredActor.Value] = 9 });
        Assert.AreEqual(Vv((retiredActor, 12)), remote);
        Assert.AreEqual(DataSyncVersionVector.Empty,
            DataSyncRevisionRules.RestoreWinsRemote([], new Dictionary<string, long>()));
    }

    // ---- guards ------------------------------------------------------------------------------

    [TestMethod]
    public void RequiredVectorsAreRequired()
    {
        var local = Vv((Self, 1));
        foreach (var kind in new[]
                 {
                     DataSyncRevisionKind.Create, DataSyncRevisionKind.FastForward, DataSyncRevisionKind.MergedNoConflict,
                     DataSyncRevisionKind.FollowMerged, DataSyncRevisionKind.AcceptRemoteDelete, DataSyncRevisionKind.Revive,
                 })
            Assert.ThrowsException<ArgumentException>(() => Next(kind, local, null, new Counter(1),
                tombstone: DataSyncVersionVector.Empty), kind.ToString());

        Assert.ThrowsException<ArgumentException>(() =>
            Next(DataSyncRevisionKind.Revive, local, Vv((Self, 1)), new Counter(1)));
        Assert.ThrowsException<ArgumentException>(() => Next(DataSyncRevisionKind.Retire, local, null, new Counter(1)));
        Assert.ThrowsException<ArgumentException>(() => DataSyncRevisionRules.Next(DataSyncRevisionKind.LocalEdit, local,
            null, false, false, default, new Counter(1).Next));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => DataSyncRevisionRules.Next((DataSyncRevisionKind)99,
            local, null, false, false, Self, new Counter(1).Next));
    }

    [TestMethod]
    public void AStaleOwnCounterIsRefused()
    {
        // A peer has seen counter 9 of this actor, but the local row only issues 4: a restored database. The rules
        // never overwrite a counter with a lower one.
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            Next(DataSyncRevisionKind.MergedNoConflict, Vv((Self, 3)), Vv((Self, 9)), new Counter(3)));
    }

    // ---- property: monotonicity (I7) ---------------------------------------------------------

    [TestMethod]
    public void EveryRuleIsMonotonic()
    {
        var random = new Random(20260925);
        var kinds = Enum.GetValues<DataSyncRevisionKind>();
        for (var i = 0; i < 5_000; i++)
        {
            var kind = kinds[random.Next(kinds.Length)];
            var local = RandomVector(random);
            var remote = RandomVector(random);
            var tombstone = RandomVector(random);
            if (kind is DataSyncRevisionKind.Create or DataSyncRevisionKind.FastForward)
                remote = DataSyncVersionVector.Max(local, remote); // their precondition: remote includes local
            if (kind == DataSyncRevisionKind.Revive)
                local = random.Next(2) == 0 ? DataSyncVersionVector.Empty : tombstone; // no live row: the tombstone's
            var resultEqualsRemote = random.Next(2) == 0;
            var resultEqualsLocal = random.Next(2) == 0;
            // This device's counter is never below what any vector already holds for it.
            var counter = new Counter(new[] { local, remote, tombstone }.Max(v => v[Self]) + random.Next(0, 3));

            var result = DataSyncRevisionRules.Next(kind, local, remote, resultEqualsRemote, resultEqualsLocal, Self,
                counter.Next, tombstone);
            var context = $"{kind} local={local} remote={remote} tombstone={tombstone}";

            Assert.IsTrue(AtLeast(result, local), context);
            if (kind is DataSyncRevisionKind.Revive or DataSyncRevisionKind.Retire)
                Assert.IsTrue(AtLeast(result, tombstone), context);
            if (kind is not (DataSyncRevisionKind.LocalEdit or DataSyncRevisionKind.LocalDelete
                    or DataSyncRevisionKind.Undo or DataSyncRevisionKind.MergedWithConflicts or DataSyncRevisionKind.Retire))
                Assert.IsTrue(AtLeast(result, remote), context);
            if (kind == DataSyncRevisionKind.MergedWithConflicts)
            {
                foreach (var (actor, value) in result.Counters)
                    if (actor != Self.Value) Assert.AreEqual(local.Counters.GetValueOrDefault(actor), value, context);
            }

            if (counter.Issued == 1) Assert.AreEqual(counter.Last, result[Self], context);
            Assert.IsTrue(counter.Issued <= 1, context);
        }
    }

    private static bool AtLeast(DataSyncVersionVector a, DataSyncVersionVector b) =>
        a.CompareTo(b) is DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates;

    private static DataSyncVersionVector RandomVector(Random random)
    {
        var vv = DataSyncVersionVector.Empty;
        foreach (var actor in new[] { Self, Peer, Third, Actor(0x4) })
        {
            if (random.Next(3) == 0) continue;
            vv = vv.With(actor, random.Next(1, 6));
        }

        return vv;
    }
}
