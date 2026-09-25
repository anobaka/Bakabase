using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Merging;

/// <summary>
/// The breakers of §8.7 as pure checks, exactly at and above their thresholds (the merger applies them in
/// <see cref="MergerTests"/>; B6 and B7 are <see cref="AnomalyTests"/>).
/// </summary>
[TestClass]
public class BreakerTests
{
    private static readonly DataSyncAutoApplyPolicy Policy = DataSyncAutoApplyPolicy.Default;

    [TestMethod]
    public void B1_AnotherNodeOrEpochLooksReset()
    {
        Assert.IsNull(DataSyncBreakers.PeerReset("pc-1", "e1", "pc-1", "e1"));
        Assert.IsNull(DataSyncBreakers.PeerReset("pc-1", null, "pc-1", "e1"), "no epoch recorded yet: a first contact");
        Assert.AreEqual(new DataSyncBreakerTrip(DataSyncPauseReason.PeerReset, "epoch"),
            DataSyncBreakers.PeerReset("pc-1", "e1", "pc-1", "e2"));
        Assert.AreEqual(new DataSyncBreakerTrip(DataSyncPauseReason.PeerReset, "node"),
            DataSyncBreakers.PeerReset("pc-1", "e1", "pc-9", "e1"));
    }

    [TestMethod]
    public void B1b_ASequenceBelowTheCursorLooksRestored()
    {
        static DataSyncFeedKindHead Head(string kind, long maxSeq) => new(kind, 1, maxSeq, false, 1);
        var cursors = new Dictionary<string, long> { ["customProperty"] = 120, ["extensionGroup"] = 40 };
        string[] linkKinds = ["customProperty", "extensionGroup"];

        Assert.IsNull(DataSyncBreakers.PeerRestored(cursors, [Head("customProperty", 120), Head("extensionGroup", 41)], linkKinds));
        var trip = DataSyncBreakers.PeerRestored(cursors, [Head("customProperty", 119), Head("extensionGroup", 41)], linkKinds);
        Assert.AreEqual(DataSyncPauseReason.PeerReset, trip!.Reason);
        Assert.AreEqual("restored;kind=customProperty", trip.Detail);
        Assert.IsNull(DataSyncBreakers.PeerRestored(cursors, [Head("customProperty", 1)], ["extensionGroup"]),
            "only kinds of the link count");
        Assert.IsNull(DataSyncBreakers.PeerRestored(new Dictionary<string, long>(), [Head("customProperty", 1)], linkKinds),
            "no cursor, nothing to be behind");
    }

    [TestMethod]
    [DataRow(10, 0, false)]
    [DataRow(11, 0, true)]
    [DataRow(4, 20, false)]
    [DataRow(5, 20, true)]
    [DataRow(5, 19, false)]
    public void B2_NewDeletionsAboveTenOrTwentyPercent(int deletions, int bases, bool trips)
    {
        var trip = DataSyncBreakers.MassDeletion("customProperty", deletions, bases, Policy);
        Assert.AreEqual(trips, trip is not null);
        if (trips) Assert.AreEqual($"deletions={deletions};kind=customProperty", trip!.Detail);
    }

    [TestMethod]
    [DataRow(0, 3, true)]
    [DataRow(0, 2, false)]
    [DataRow(1, 30, false)]
    public void B3_ZeroLiveEntitiesWithThreeBases(int live, int bases, bool trips) =>
        Assert.AreEqual(trips, DataSyncBreakers.KindEmptied("extensionGroup", live, bases, Policy) is not null);

    [TestMethod]
    [DataRow(50, 1000, false)]
    [DataRow(51, 1000, true)]
    [DataRow(2, 10, false)]
    [DataRow(3, 10, true)]
    [DataRow(3, 9, false)]
    public void B4_MassChildDeletionExactlyAtAndAboveFiftyAndTwentyPercent(int candidates, int children, bool trips) =>
        Assert.AreEqual(trips, DataSyncBreakers.IsMassChildDeletion(candidates, children, Policy));

    [TestMethod]
    public void B5_EachSideIsCheckedOnItsOwn()
    {
        Assert.AreEqual((false, false), DataSyncBreakers.LargeChange(50, 50, Policy));
        Assert.AreEqual((true, false), DataSyncBreakers.LargeChange(51, 50, Policy));
        Assert.AreEqual((false, true), DataSyncBreakers.LargeChange(0, 51, Policy));
    }

    [TestMethod]
    public void B8_TheOpenItemLimitAndItsResumeRule()
    {
        var limits = DataSyncLimits.Default with { MaxOpenInboxItemsPerLink = 100 };
        Assert.IsNull(DataSyncBreakers.TooManyDecisions(100, limits));
        Assert.AreEqual("openItems=101", DataSyncBreakers.TooManyDecisions(101, limits)!.Detail);
        Assert.IsFalse(DataSyncBreakers.MayResumeTooManyDecisions(100, limits));
        Assert.IsTrue(DataSyncBreakers.MayResumeTooManyDecisions(99, limits));
    }
}

/// <summary>§8.6: what applies without asking.</summary>
[TestClass]
public class AutoApplyPolicyTests
{
    private static DataSyncEntityDeletionFacts Safe => new(DataSyncVvRelation.DominatedBy, true, 0, false, false, false, false);

    [TestMethod]
    public void ADeletionAppliesByItselfOnlyWhenEveryConditionHolds()
    {
        var policy = DataSyncAutoApplyPolicy.Default;
        Assert.IsTrue(policy.DecideEntityDeletion(Safe).IsAutomatic);

        var cases = new Dictionary<string, DataSyncEntityDeletionFacts>
        {
            [DataSyncAutoDeleteVerdict.NotDominated] = Safe with { LocalToTombstone = DataSyncVvRelation.Concurrent },
            [DataSyncAutoDeleteVerdict.NotCreatedBySync] = Safe with { CreatedBySync = false },
            [DataSyncAutoDeleteVerdict.HasValues] = Safe with { ValueCount = 1 },
            [DataSyncAutoDeleteVerdict.ValueCountUnknown] = Safe with { ValueCount = null },
            [DataSyncAutoDeleteVerdict.OpenItem] = Safe with { HasOpenItem = true },
            [DataSyncAutoDeleteVerdict.PendingRecord] = Safe with { HasPendingRecord = true },
            [DataSyncAutoDeleteVerdict.HeldChildren] = Safe with { HasHeldChildren = true },
            [DataSyncAutoDeleteVerdict.DeletionsAsItems] = Safe with { DeletionsAsItems = true },
        };
        foreach (var (reason, facts) in cases)
        {
            var verdict = policy.DecideEntityDeletion(facts);
            Assert.IsFalse(verdict.IsAutomatic, reason);
            Assert.AreEqual(reason, verdict.Reason);
        }

        // An equal version is not "nothing changed here that the deleting device had not seen".
        Assert.IsFalse(policy.DecideEntityDeletion(Safe with { LocalToTombstone = DataSyncVvRelation.Equal }).IsAutomatic);
    }

    [TestMethod]
    public void TheDefaultsAreTheSpecsThresholds()
    {
        var p = DataSyncAutoApplyPolicy.Default;
        Assert.AreEqual((10, 0.2, 20, 3), (p.MaxEntityDeletionsPerPull, p.MaxEntityDeletionRatio, p.MinEntitiesForRatio,
            p.MinBasesForKindEmptied));
        Assert.AreEqual((50, 0.2, 10, 50, 50), (p.MaxChildDeletionsPerEntity, p.MaxChildDeletionRatio, p.MinChildrenForRatio,
            p.MaxUpdatedEntitiesPerPull, p.MaxCreatedEntitiesPerPull));
    }
}
