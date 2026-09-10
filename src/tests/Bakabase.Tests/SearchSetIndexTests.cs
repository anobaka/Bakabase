using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Search;

namespace Bakabase.Tests;

/// <summary>
/// The two-way index that resource profiles and rule collections share.
/// <para>
/// One direction can always be recomputed by running the search again. The other cannot be computed
/// at all without keeping it — "which saved searches is this resource in" is a scan over every
/// search there is. So the thing that must hold is that the two never disagree.
/// </para>
/// </summary>
[TestClass]
public sealed class SearchSetIndexTests
{
    [TestMethod]
    public void BothDirectionsAgree()
    {
        var index = new SearchSetIndex();

        index.Replace(1, [10, 11]);
        index.Replace(2, [11, 12]);

        CollectionAssert.AreEquivalent(new[] {10, 11}, index.GetMembers(1).ToArray());
        CollectionAssert.AreEqual(new[] {1}, index.GetSetIds(10).ToArray());
        CollectionAssert.AreEqual(new[] {1, 2}, index.GetSetIds(11).ToArray());
        CollectionAssert.AreEqual(new[] {2}, index.GetSetIds(12).ToArray());
    }

    /// <summary>
    /// What callers act on. A resource whose membership did not move needs nothing recomputed about
    /// it, and saying otherwise turns every rebuild into a cache flush.
    /// </summary>
    [TestMethod]
    public void ReplacingSaysOnlyWhoActuallyMoved()
    {
        var index = new SearchSetIndex();

        index.Replace(1, [10, 11]);

        var changed = index.Replace(1, [11, 12]);

        CollectionAssert.AreEquivalent(new[] {10, 12}, changed.ToArray(),
            "11 was in the set before and after, so nothing about it changed");
    }

    [TestMethod]
    public void ReplacingWithTheSameMembersChangesNobody()
    {
        var index = new SearchSetIndex();

        index.Replace(1, [10, 11]);

        Assert.AreEqual(0, index.Replace(1, [11, 10]).Count, "a set is a set, not a list");
    }

    [TestMethod]
    public void RemovingASetTakesItOutOfBothDirections()
    {
        var index = new SearchSetIndex();

        index.Replace(1, [10, 11]);
        index.Replace(2, [11]);

        var wereIn = index.Remove(1);

        CollectionAssert.AreEquivalent(new[] {10, 11}, wereIn.ToArray());
        Assert.AreEqual(0, index.GetMembers(1).Count);
        Assert.AreEqual(0, index.GetSetIds(10).Count, "a resource in no set is absent, not empty-listed");
        CollectionAssert.AreEqual(new[] {2}, index.GetSetIds(11).ToArray());
    }

    /// <summary>
    /// For profiles this is the priority that decides whose configuration a resource gets, so the
    /// order is not cosmetic.
    /// </summary>
    [TestMethod]
    public void SetsComeBackHighestRankFirst()
    {
        var index = new SearchSetIndex();

        index.SetRank(1, 10);
        index.SetRank(2, 20);
        index.Replace(1, [10]);
        index.Replace(2, [10]);

        CollectionAssert.AreEqual(new[] {2, 1}, index.GetSetIds(10).ToArray());
    }

    [TestMethod]
    public void ChangingARankReordersWhatIsAlreadyIndexed()
    {
        var index = new SearchSetIndex();

        index.SetRank(1, 10);
        index.SetRank(2, 20);
        index.Replace(1, [10]);
        index.Replace(2, [10]);

        index.SetRank(1, 30);

        CollectionAssert.AreEqual(new[] {1, 2}, index.GetSetIds(10).ToArray(),
            "a priority edit has to move the sets that were indexed before it");
    }

    /// <summary>
    /// Two sets of equal rank must come back in the same order every time, or a resource's effective
    /// configuration would depend on which of them happened to be indexed first.
    /// </summary>
    [TestMethod]
    public void EqualRanksAreBrokenByIdRatherThanByLuck()
    {
        var first = new SearchSetIndex();

        first.Replace(7, [10]);
        first.Replace(3, [10]);

        var second = new SearchSetIndex();

        second.Replace(3, [10]);
        second.Replace(7, [10]);

        CollectionAssert.AreEqual(first.GetSetIds(10).ToArray(), second.GetSetIds(10).ToArray());
        CollectionAssert.AreEqual(new[] {3, 7}, first.GetSetIds(10).ToArray());
    }

    [TestMethod]
    public void ForgettingAResourceTakesItOutOfEverySet()
    {
        var index = new SearchSetIndex();

        index.Replace(1, [10, 11]);
        index.Replace(2, [10]);

        index.Forget([10]);

        Assert.AreEqual(0, index.GetSetIds(10).Count);
        CollectionAssert.AreEqual(new[] {11}, index.GetMembers(1).ToArray());
        Assert.AreEqual(0, index.GetMembers(2).Count);
    }

    /// <summary>
    /// Until the first build finishes, an answer of "no sets" is indistinguishable from the truth —
    /// which is why readers wait rather than read.
    /// </summary>
    [TestMethod]
    public async Task ReadinessIsSomethingToWaitFor()
    {
        var index = new SearchSetIndex();

        Assert.IsFalse(index.IsReady);

        var waiting = index.WaitUntilReady();

        Assert.IsFalse(waiting.IsCompleted);

        index.MarkReady();

        await waiting.WaitAsync(System.TimeSpan.FromSeconds(5));
        Assert.IsTrue(index.IsReady);
    }

    [TestMethod]
    public async Task AResetWaitsAgain()
    {
        var index = new SearchSetIndex();

        index.Replace(1, [10]);
        index.MarkReady();
        await index.WaitUntilReady();

        index.Reset();

        Assert.IsFalse(index.IsReady);
        Assert.AreEqual(0, index.GetMembers(1).Count, "a rebuild starts from nothing");
        Assert.IsFalse(index.WaitUntilReady().IsCompleted);

        index.MarkReady();
        await index.WaitUntilReady().WaitAsync(System.TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void ASetThatMatchesNothingIsStillASet()
    {
        var index = new SearchSetIndex();

        index.Replace(1, []);

        CollectionAssert.Contains(index.SetIds.ToArray(), 1,
            "otherwise a rebuild could not tell an empty set from one it has never heard of");
    }

    /// <summary>
    /// Both directions move under one lock. A reader that saw a resource listing a set the set does
    /// not have would be reading a contradiction, not a stale answer.
    /// </summary>
    [TestMethod]
    public async Task ConcurrentWritesLeaveTheTwoDirectionsConsistent()
    {
        var index = new SearchSetIndex();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var round = 0; round < 50; round++)
            {
                index.Replace(worker, Enumerable.Range(0, 20).Where(r => (r + round) % 3 == 0).ToList());
            }
        })));

        foreach (var setId in index.SetIds)
        {
            foreach (var resourceId in index.GetMembers(setId))
            {
                CollectionAssert.Contains(index.GetSetIds(resourceId).ToArray(), setId,
                    $"resource {resourceId} is in set {setId} but does not say so");
            }
        }

        for (var resourceId = 0; resourceId < 20; resourceId++)
        {
            foreach (var setId in index.GetSetIds(resourceId))
            {
                Assert.IsTrue(index.GetMembers(setId).Contains(resourceId),
                    $"resource {resourceId} says it is in set {setId}, which disagrees");
            }
        }
    }
}
