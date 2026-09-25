using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Ordering;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Ordering;

[TestClass]
public class OrderPlannerTests
{
    private const int MaxLength = 128;

    private static DataSyncOrderEntry E(string localKey, string? orderKey) =>
        new(localKey, orderKey, "t" + localKey);

    /// <summary>The entries with their keys after applying <paramref name="moves"/>.</summary>
    private static List<DataSyncOrderEntry> Apply(IEnumerable<DataSyncOrderEntry> entries,
        IReadOnlyDictionary<string, string> moves) =>
        entries.Select(e => moves.TryGetValue(e.LocalKey, out var key) ? e with { OrderKey = key } : e).ToList();

    private static void AssertSharedOrderIs(IReadOnlyList<DataSyncOrderEntry> entries, params string[] localKeys) =>
        CollectionAssert.AreEqual(localKeys, DataSyncOrderPlanner.Sort(entries).Select(e => e.LocalKey).ToArray());

    // ---- DetectMoves --------------------------------------------------------------------------

    [TestMethod]
    public void NothingMovesWhenLocalOrderIsTheSharedOrder()
    {
        var entries = new[] { E("1", "a0"), E("2", "a1"), E("3", "a2") };
        Assert.AreEqual(0, DataSyncOrderPlanner.DetectMoves(entries, MaxLength).Count);
    }

    [TestMethod]
    public void OneMovedItemGetsOneNewKeyAndNothingElseChanges()
    {
        // 4 was moved to the front.
        var local = new[] { E("4", "a3"), E("1", "a0"), E("2", "a1"), E("3", "a2") };
        var moves = DataSyncOrderPlanner.DetectMoves(local, MaxLength);
        Assert.AreEqual(1, moves.Count);
        Assert.AreEqual("Zz", moves["4"]);
        AssertSharedOrderIs(Apply(local, moves), "4", "1", "2", "3");

        // 1 was moved between 3 and 4.
        local = [E("2", "a1"), E("3", "a2"), E("1", "a0"), E("4", "a3")];
        moves = DataSyncOrderPlanner.DetectMoves(local, MaxLength);
        Assert.AreEqual(1, moves.Count);
        Assert.AreEqual("a2V", moves["1"]);
        AssertSharedOrderIs(Apply(local, moves), "2", "3", "1", "4");
    }

    [TestMethod]
    public void LongestRunTiesAreDeterministic()
    {
        // Swapping two neighbours: either could be the one that moved. The same one is re-keyed every time.
        var local = new[] { E("2", "a1"), E("1", "a0"), E("3", "a2"), E("4", "a3") };
        var first = DataSyncOrderPlanner.DetectMoves(local, MaxLength);
        Assert.AreEqual(1, first.Count);
        for (var i = 0; i < 5; i++)
            CollectionAssert.AreEquivalent(first.ToList(), DataSyncOrderPlanner.DetectMoves(local, MaxLength).ToList());
        AssertSharedOrderIs(Apply(local, first), "2", "1", "3", "4");
    }

    [TestMethod]
    public void NewEntitiesAreKeyedAfterTheirPredecessor()
    {
        var local = new[] { E("1", "a0"), E("new1", null), E("2", "a1"), E("new2", null), E("new3", null) };
        var moves = DataSyncOrderPlanner.DetectMoves(local, MaxLength);
        CollectionAssert.AreEquivalent(new[] { "new1", "new2", "new3" }, moves.Keys.ToArray());
        Assert.AreEqual("a0V", moves["new1"]);
        Assert.AreEqual("a2", moves["new2"]);
        Assert.AreEqual("a3", moves["new3"]);
        AssertSharedOrderIs(Apply(local, moves), "1", "new1", "2", "new2", "new3");

        // An empty kind keys its first entity a0.
        Assert.AreEqual("a0", DataSyncOrderPlanner.DetectMoves([E("x", null)], MaxLength)["x"]);
    }

    [TestMethod]
    public void AppendsOnTwoDevicesThenAnInsertionBetweenThemReKeysOnlyTheTiedRun()
    {
        // Two devices appended after a0 at once and got the same key; tie keys order them.
        var shared = new[] { E("x", "a0"), new DataSyncOrderEntry("A", "a1", "t1"), new DataSyncOrderEntry("B", "a1", "t2"),
            E("y", "a2") };
        Assert.AreEqual(0, DataSyncOrderPlanner.DetectMoves(shared, MaxLength).Count, "equal keys alone are no move");

        // A third entity is inserted between them locally: Between(a1, a1) must never be asked for.
        var local = new[] { shared[0], shared[1], E("C", null), shared[2], shared[3] };
        var moves = DataSyncOrderPlanner.DetectMoves(local, MaxLength);
        Assert.IsFalse(moves.ContainsKey("x"));
        Assert.IsFalse(moves.ContainsKey("y"));
        CollectionAssert.IsSubsetOf(moves.Keys.ToArray(), new[] { "A", "B", "C" });
        Assert.IsTrue(moves.ContainsKey("C") && moves.ContainsKey("B"));
        AssertSharedOrderIs(Apply(local, moves), "x", "A", "C", "B", "y");
        Assert.IsTrue(Apply(local, moves).All(e => e.LocalKey is "x" or "y" ||
                                                   string.CompareOrdinal(e.OrderKey, "a0") > 0 &&
                                                   string.CompareOrdinal(e.OrderKey, "a2") < 0));
    }

    [TestMethod]
    public void AnInsertionIntoATieAtTheEndReKeysTheRunToTheOpenEnd()
    {
        var local = new[] { E("x", "a0"), new DataSyncOrderEntry("A", "a1", "t1"), E("C", null),
            new DataSyncOrderEntry("B", "a1", "t2") };
        var moves = DataSyncOrderPlanner.DetectMoves(local, MaxLength);
        AssertSharedOrderIs(Apply(local, moves), "x", "A", "C", "B");
        Assert.IsFalse(moves.ContainsKey("x"));
    }

    [TestMethod]
    public void RankingUsesOrderKeyThenTieKeyLikePlace()
    {
        // Equal keys are ranked by tie key; a local order that follows that ranking is no move.
        var a = new DataSyncOrderEntry("A", "a1", "t2");
        var b = new DataSyncOrderEntry("B", "a1", "t1");
        Assert.AreEqual(0, DataSyncOrderPlanner.DetectMoves([b, a], MaxLength).Count);
        Assert.AreEqual(1, DataSyncOrderPlanner.DetectMoves([a, b], MaxLength).Count);
        CollectionAssert.AreEqual(new[] { "B", "A" }, DataSyncOrderPlanner.Place(["A", "B"], [a, b]).ToArray());
    }

    [TestMethod]
    public void TieKeyIsTheSmallestOfAllKeys()
    {
        var keys = new EntityKeys([new SyncKey("f".PadLeft(32, 'f')), new SyncKey("0".PadLeft(32, '1')),
            new SyncKey("a".PadLeft(32, 'a'))]);
        Assert.AreEqual("0".PadLeft(32, '1'), DataSyncOrderPlanner.TieKeyOf(keys));
        Assert.ThrowsException<ArgumentException>(() => DataSyncOrderPlanner.TieKeyOf(EntityKeys.None));
    }

    [TestMethod]
    public void RandomLocalOrdersAreReproducedAndOnlyMovedEntitiesChange()
    {
        var random = new Random(42);
        for (var run = 0; run < 300; run++)
        {
            var count = random.Next(1, 30);
            var keys = FractionalIndex.NBetween(null, null, count);
            var shared = Enumerable.Range(0, count).Select(i => E(i.ToString(), keys[i])).ToList();

            // Shuffle a few, add some new ones, and give some equal keys (concurrent appends).
            var local = shared.ToList();
            for (var m = random.Next(0, 4); m > 0; m--)
            {
                var from = random.Next(local.Count);
                var item = local[from];
                local.RemoveAt(from);
                local.Insert(random.Next(local.Count + 1), item);
            }

            for (var add = random.Next(0, 3); add > 0; add--)
                local.Insert(random.Next(local.Count + 1), E("n" + add, null));
            if (local.Count > 2 && random.Next(3) == 0)
            {
                var i = random.Next(local.Count - 1);
                if (local[i].OrderKey is not null) local[i + 1] = local[i + 1] with { OrderKey = local[i].OrderKey };
            }

            var moves = DataSyncOrderPlanner.DetectMoves(local, MaxLength);
            var after = Apply(local, moves);
            AssertSharedOrderIs(after, local.Select(e => e.LocalKey).ToArray());
            Assert.IsTrue(after.All(e => FractionalIndex.IsValid(e.OrderKey)));
            Assert.AreEqual(0, DataSyncOrderPlanner.DetectMoves(after, MaxLength).Count, "no echo");
        }
    }

    // ---- Rebalance ----------------------------------------------------------------------------

    [TestMethod]
    public void RebalanceReKeysOnlyTheOffendingRun()
    {
        // Twenty well spaced entities, and a gap between the 10th and 11th that was split again and again.
        var spaced = FractionalIndex.NBetween(null, null, 20);
        var cluster = new List<string>();
        var right = spaced[10];
        for (var i = 0; i < 40; i++)
        {
            right = FractionalIndex.Between(spaced[9], right);
            cluster.Insert(0, right);
        }

        var order = spaced.Take(10).Select((k, i) => E("s" + i, k))
            .Concat(cluster.Select((k, i) => E("c" + i, k)))
            .Concat(spaced.Skip(10).Select((k, i) => E("s" + (i + 10), k))).ToList();
        var longest = cluster.Max(k => k.Length);
        var max = longest - 1;

        var moves = DataSyncOrderPlanner.Rebalance(order, max);
        Assert.IsTrue(moves.Count > 0);
        Assert.IsTrue(moves.Keys.All(k => k.StartsWith('c')), string.Join(",", moves.Keys));
        var after = Apply(order, moves);
        Assert.IsTrue(after.All(e => e.OrderKey!.Length <= max));
        AssertSharedOrderIs(after, order.Select(e => e.LocalKey).ToArray());
    }

    [TestMethod]
    public void DetectMovesRebalancesAKeyThatWouldBeTooLong()
    {
        // Keep inserting at the front of the same gap until keys exceed a small maximum.
        var entries = new List<DataSyncOrderEntry> { E("a", "a0"), E("b", "a1") };
        const int max = 6;
        for (var i = 0; i < 60; i++)
        {
            entries.Insert(1, E("n" + i, null));
            var moves = DataSyncOrderPlanner.DetectMoves(entries, max);
            entries = Apply(entries, moves);
            Assert.IsTrue(entries.All(e => e.OrderKey!.Length <= max), $"step {i}");
            AssertSharedOrderIs(entries, entries.Select(e => e.LocalKey).ToArray());
        }
    }

    // ---- Place --------------------------------------------------------------------------------

    [TestMethod]
    public void PlaceFillsSyncedSlotsAndLeavesOtherSlotsAlone()
    {
        // L1 and L2 are local-only; the synced entities arrive in another shared order.
        var local = new[] { "L1", "s1", "s2", "L2", "s3" };
        var synced = new[] { E("s1", "a2"), E("s2", "a0"), E("s3", "a1") };
        var placed = DataSyncOrderPlanner.Place(local, synced);
        CollectionAssert.AreEqual(new[] { "L1", "s2", "s3", "L2", "s1" }, placed.ToArray());
    }

    [TestMethod]
    public void PlaceBreaksTiesByTheSmallestKeyAndKeepsKeylessSlots()
    {
        var local = new[] { "a", "b", "new" };
        var synced = new[] { new DataSyncOrderEntry("a", "a1", "t9"), new DataSyncOrderEntry("b", "a1", "t1"),
            E("new", null) };
        CollectionAssert.AreEqual(new[] { "b", "a", "new" }, DataSyncOrderPlanner.Place(local, synced).ToArray());
    }

    [TestMethod]
    public void PlacingThenDetectingFindsNoMove()
    {
        var random = new Random(9);
        for (var run = 0; run < 200; run++)
        {
            var count = random.Next(1, 25);
            var keys = FractionalIndex.NBetween(null, null, count);
            var synced = Enumerable.Range(0, count)
                .Select(i => new DataSyncOrderEntry("s" + i, keys[random.Next(count)], "t" + i)).ToList();
            var local = synced.Select(e => e.LocalKey).Concat(Enumerable.Range(0, random.Next(4)).Select(i => "L" + i))
                .OrderBy(_ => random.Next()).ToList();

            var placed = DataSyncOrderPlanner.Place(local, synced);
            CollectionAssert.AreEquivalent(local, placed.ToList());
            for (var i = 0; i < local.Count; i++)
            {
                if (local[i].StartsWith('L')) Assert.AreEqual(local[i], placed[i], "local-only slots never move");
            }

            var byKey = synced.ToDictionary(e => e.LocalKey);
            var syncedInPlacedOrder = placed.Where(byKey.ContainsKey).Select(k => byKey[k]).ToList();
            Assert.AreEqual(0, DataSyncOrderPlanner.DetectMoves(syncedInPlacedOrder, MaxLength).Count);
        }
    }

    [TestMethod]
    public void PlaceIgnoresSyncedKeysThatAreNotLocal()
    {
        var placed = DataSyncOrderPlanner.Place(["a", "b"], (IReadOnlyList<string>)["gone", "b", "a"]);
        CollectionAssert.AreEqual(new[] { "b", "a" }, placed.ToArray());
    }
}
