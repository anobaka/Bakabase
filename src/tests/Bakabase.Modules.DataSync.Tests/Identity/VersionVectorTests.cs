using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Identity;

[TestClass]
public class VersionVectorTests
{
    private static readonly DataSyncActorId A = Actor(0xa);
    private static readonly DataSyncActorId B = Actor(0xb);
    private static readonly DataSyncActorId C = Actor(0xc);
    private const long MaxCounter = 1L << 53;

    internal static DataSyncActorId Actor(int i) => new(i.ToString("x16"));

    internal static DataSyncVersionVector Vv(params (DataSyncActorId Actor, long Counter)[] counters) =>
        counters.Aggregate(DataSyncVersionVector.Empty, (v, c) => v.With(c.Actor, c.Counter));

    // ---- relations -------------------------------------------------------------------------

    [TestMethod]
    public void EmptyVectors()
    {
        var empty = DataSyncVersionVector.Empty;
        Assert.AreEqual(0, empty.Counters.Count);
        Assert.AreEqual("{}", empty.ToCanonicalString());
        Assert.AreEqual(0, empty[A]);
        Assert.AreEqual(DataSyncVvRelation.Equal, empty.CompareTo(DataSyncVersionVector.Empty));
        Assert.AreEqual(DataSyncVvRelation.DominatedBy, empty.CompareTo(Vv((A, 1))));
        Assert.AreEqual(DataSyncVvRelation.Dominates, Vv((A, 1)).CompareTo(empty));
    }

    [TestMethod]
    public void CompareToCoversEveryRelation()
    {
        Assert.AreEqual(DataSyncVvRelation.Equal, Vv((A, 2), (B, 3)).CompareTo(Vv((B, 3), (A, 2))));
        Assert.AreEqual(DataSyncVvRelation.Dominates, Vv((A, 3), (B, 3)).CompareTo(Vv((A, 2), (B, 3))));
        Assert.AreEqual(DataSyncVvRelation.Dominates, Vv((A, 2), (B, 3)).CompareTo(Vv((A, 2))));
        Assert.AreEqual(DataSyncVvRelation.DominatedBy, Vv((A, 2)).CompareTo(Vv((A, 2), (C, 1))));
        Assert.AreEqual(DataSyncVvRelation.DominatedBy, Vv((A, 1)).CompareTo(Vv((A, 2))));
        Assert.AreEqual(DataSyncVvRelation.Concurrent, Vv((A, 2), (B, 1)).CompareTo(Vv((A, 1), (B, 2))));
        Assert.AreEqual(DataSyncVvRelation.Concurrent, Vv((A, 1)).CompareTo(Vv((B, 1))));
        Assert.AreEqual(DataSyncVvRelation.Concurrent, Vv((A, 5), (B, 1)).CompareTo(Vv((A, 5), (C, 1))));
    }

    [TestMethod]
    public void CompareToIsAntisymmetric()
    {
        var random = new Random(20260925);
        for (var i = 0; i < 2_000; i++)
        {
            var a = RandomVector(random);
            var b = RandomVector(random);
            var expected = a.CompareTo(b) switch
            {
                DataSyncVvRelation.Dominates => DataSyncVvRelation.DominatedBy,
                DataSyncVvRelation.DominatedBy => DataSyncVvRelation.Dominates,
                var same => same,
            };
            Assert.AreEqual(expected, b.CompareTo(a), $"{a} vs {b}");
            Assert.AreEqual(a.CompareTo(b) == DataSyncVvRelation.Equal, a == b);
        }
    }

    // ---- With ------------------------------------------------------------------------------

    [TestMethod]
    public void WithSetsACounterAndLeavesTheOriginalUnchanged()
    {
        var one = Vv((A, 1));
        var two = one.With(A, 5).With(B, 1);
        Assert.AreEqual(1, one[A]);
        Assert.AreEqual(0, one[B]);
        Assert.AreEqual(5, two[A]);
        Assert.AreEqual(1, two[B]);
        Assert.AreEqual(MaxCounter, DataSyncVersionVector.Empty.With(A, MaxCounter)[A]);
    }

    [TestMethod]
    public void WithRefusesANonIncreasingCounter()
    {
        var vv = Vv((A, 3));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => vv.With(A, 3));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => vv.With(A, 2));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => vv.With(B, 0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => vv.With(B, -1));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => vv.With(B, MaxCounter + 1));
        Assert.ThrowsException<ArgumentException>(() => vv.With(default, 1));
        Assert.ThrowsException<ArgumentException>(() => _ = vv[default]);
    }

    // ---- Max -------------------------------------------------------------------------------

    [TestMethod]
    public void MaxTakesTheHigherCounterOfEveryActor()
    {
        var max = DataSyncVersionVector.Max(Vv((A, 3), (B, 1)), Vv((B, 4), (C, 2)));
        Assert.AreEqual(Vv((A, 3), (B, 4), (C, 2)), max);
        Assert.AreEqual(Vv((A, 1)), DataSyncVersionVector.Max(Vv((A, 1)), DataSyncVersionVector.Empty));
        Assert.AreEqual(Vv((A, 1)), DataSyncVersionVector.Max(DataSyncVersionVector.Empty, Vv((A, 1))));
    }

    [TestMethod]
    public void MaxIsCommutativeAssociativeAndIdempotent()
    {
        var random = new Random(42);
        for (var i = 0; i < 2_000; i++)
        {
            var a = RandomVector(random);
            var b = RandomVector(random);
            var c = RandomVector(random);
            var ab = DataSyncVersionVector.Max(a, b);
            Assert.AreEqual(ab, DataSyncVersionVector.Max(b, a));
            Assert.AreEqual(DataSyncVersionVector.Max(ab, c), DataSyncVersionVector.Max(a, DataSyncVersionVector.Max(b, c)));
            Assert.AreEqual(a, DataSyncVersionVector.Max(a, a));
            Assert.IsTrue(ab.CompareTo(a) is DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates);
            Assert.IsTrue(ab.CompareTo(b) is DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates);
            // Max is the least upper bound: one it dominates is exactly one below a or b.
            if (a.CompareTo(b) is DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates) Assert.AreEqual(a, ab);
        }
    }

    // ---- canonical form --------------------------------------------------------------------

    [TestMethod]
    public void CanonicalStringSortsActorsAndRoundTrips()
    {
        var vv = Vv((C, 7), (A, 1), (B, MaxCounter));
        var canonical = vv.ToCanonicalString();
        Assert.AreEqual("{\"000000000000000a\":1,\"000000000000000b\":9007199254740992,\"000000000000000c\":7}",
            canonical);
        Assert.AreEqual(canonical, CanonicalJson.Serialize(JsonNode.Parse(canonical)));
        Assert.AreEqual(vv, DataSyncVersionVector.ParseStored(canonical));
        Assert.AreEqual(canonical, vv.ToString());
        CollectionAssert.AreEqual(new[] { A.Value, B.Value, C.Value }, vv.Counters.Keys.ToArray());
    }

    [TestMethod]
    public void OrderOfConstructionDoesNotMatter()
    {
        var one = Vv((A, 1), (B, 2), (C, 3));
        var other = Vv((C, 3), (B, 2), (A, 1));
        Assert.AreEqual(one.ToCanonicalString(), other.ToCanonicalString());
        Assert.IsTrue(one == other);
    }

    // ---- TryParse (peer input) -------------------------------------------------------------

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"000000000000000a\":1}")]
    [DataRow("{\"000000000000000b\":9007199254740992,\"000000000000000a\":3}")]
    public void TryParseAcceptsValidVectors(string json)
    {
        Assert.IsTrue(DataSyncVersionVector.TryParse(JsonNode.Parse(json), DataSyncLimits.Default, out var vv));
        Assert.AreEqual(CanonicalJson.Serialize(JsonNode.Parse(json)), vv.ToCanonicalString());
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("\"{}\"")]
    [DataRow("1")]
    [DataRow("true")]
    [DataRow("null")]
    [DataRow("{\"000000000000000A\":1}")]
    [DataRow("{\"00000000000000a\":1}")]
    [DataRow("{\"000000000000000a0\":1}")]
    [DataRow("{\"zzzzzzzzzzzzzzzz\":1}")]
    [DataRow("{\"\":1}")]
    [DataRow("{\"000000000000000a\":0}")]
    [DataRow("{\"000000000000000a\":-1}")]
    [DataRow("{\"000000000000000a\":9007199254740993}")]
    [DataRow("{\"000000000000000a\":9223372036854775808}")]
    [DataRow("{\"000000000000000a\":1.5}")]
    [DataRow("{\"000000000000000a\":1.0}")]
    [DataRow("{\"000000000000000a\":1e2}")]
    [DataRow("{\"000000000000000a\":\"1\"}")]
    [DataRow("{\"000000000000000a\":true}")]
    [DataRow("{\"000000000000000a\":null}")]
    [DataRow("{\"000000000000000a\":{}}")]
    [DataRow("{\"000000000000000a\":[1]}")]
    [DataRow("{\"000000000000000a\":1,\"000000000000000a\":2}")]
    public void TryParseRejectsBadInputWithoutThrowing(string json)
    {
        Assert.IsFalse(DataSyncVersionVector.TryParse(JsonNode.Parse(json), DataSyncLimits.Default, out var vv), json);
        Assert.AreEqual(DataSyncVersionVector.Empty, vv);
    }

    [TestMethod]
    public void TryParseRejectsANullNode()
    {
        Assert.IsFalse(DataSyncVersionVector.TryParse(null, DataSyncLimits.Default, out _));
    }

    [TestMethod]
    public void TryParseEnforcesMaxActorsPerVector()
    {
        var limits = DataSyncLimits.Default with { MaxActorsPerVector = 3 };
        var three = new JsonObject { [A.Value] = 1, [B.Value] = 2, [C.Value] = 3 };
        Assert.IsTrue(DataSyncVersionVector.TryParse(three, limits, out _));
        three[Actor(0xd).Value] = 4;
        Assert.IsFalse(DataSyncVersionVector.TryParse(three, limits, out _));

        var full = new JsonObject();
        for (var i = 0; i < DataSyncLimits.Default.MaxActorsPerVector; i++) full[Actor(i).Value] = 1;
        Assert.IsTrue(DataSyncVersionVector.TryParse(full, DataSyncLimits.Default, out var vv));
        Assert.AreEqual(DataSyncLimits.Default.MaxActorsPerVector, vv.Counters.Count);
        full[Actor(10_000).Value] = 1;
        Assert.IsFalse(DataSyncVersionVector.TryParse(full, DataSyncLimits.Default, out _));
    }

    [TestMethod]
    public void TryParseAcceptsNodesBuiltFromClrNumbers()
    {
        var node = new JsonObject { [A.Value] = 1, [B.Value] = 2L, [C.Value] = (byte)3 };
        Assert.IsTrue(DataSyncVersionVector.TryParse(node, DataSyncLimits.Default, out var vv));
        Assert.AreEqual(Vv((A, 1), (B, 2), (C, 3)), vv);
        Assert.IsFalse(DataSyncVersionVector.TryParse(new JsonObject { [A.Value] = 1.0 }, DataSyncLimits.Default, out _));
    }

    [TestMethod]
    public void TryParseNeverThrowsOnMutatedInput()
    {
        const string valid = "{\"000000000000000a\":12,\"000000000000000b\":9007199254740992}";
        var random = new Random(7);
        const string alphabet = "{}[]\":,0123456789abcdefAZ.-e \\";
        for (var i = 0; i < 2_000; i++)
        {
            var chars = valid.ToCharArray();
            for (var m = random.Next(1, 4); m > 0; m--) chars[random.Next(chars.Length)] = alphabet[random.Next(alphabet.Length)];
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(new string(chars));
            }
            catch (JsonException)
            {
                continue;
            }

            DataSyncVersionVector.TryParse(node, DataSyncLimits.Default, out _);
        }
    }

    // ---- ParseStored (local storage) -------------------------------------------------------

    [TestMethod]
    [DataRow("")]
    [DataRow("not json")]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("{\"x\":1}")]
    [DataRow("{\"000000000000000a\":0}")]
    [DataRow("{\"000000000000000a\":1.5}")]
    [DataRow("{\"000000000000000a\":1,\"000000000000000a\":2}")]
    public void ParseStoredThrowsInvalidDataOnCorruption(string json)
    {
        Assert.ThrowsException<InvalidDataException>(() => DataSyncVersionVector.ParseStored(json));
    }

    [TestMethod]
    public void ParseStoredHasNoActorLimit()
    {
        var vv = DataSyncVersionVector.Empty;
        for (var i = 1; i <= DataSyncLimits.Default.MaxActorsPerVector + 5; i++) vv = vv.With(Actor(i), i);
        Assert.AreEqual(vv, DataSyncVersionVector.ParseStored(vv.ToCanonicalString()));
    }

    // ---- value equality --------------------------------------------------------------------

    [TestMethod]
    public void VectorsParsedFromOneStringAreEqual()
    {
        const string json = "{\"000000000000000a\":3,\"000000000000000b\":1}";
        var one = DataSyncVersionVector.ParseStored(json);
        var two = DataSyncVersionVector.ParseStored(json);
        Assert.AreNotSame(one, two);
        Assert.IsTrue(one == two);
        Assert.IsFalse(one != two);
        Assert.IsTrue(one.Equals(two));
        Assert.IsTrue(one.Equals((object)two));
        Assert.AreEqual(one.GetHashCode(), two.GetHashCode());

        var other = DataSyncVersionVector.ParseStored("{\"000000000000000a\":3}");
        Assert.IsTrue(one != other);
        Assert.IsFalse(one.Equals((object?)null));
        Assert.IsFalse(one == null);
        Assert.IsTrue((DataSyncVersionVector?)null == null);
    }

    [TestMethod]
    public void RecordsHoldingEqualVectorsCompareEqual()
    {
        const string json = "{\"000000000000000a\":3}";
        IReadOnlyList<string> keys = ["0123456789abcdef0123456789abcdef"];
        var content = new JsonObject { ["name"] = "Genre" };
        var editor = new DataSyncEditorRef("node", "PC", A.Value);

        DataSyncWireRecord Record(DataSyncVersionVector vv) =>
            new(keys, "node", 5, vv, editor, false, 1, null, content, null, null, 0);

        Assert.AreEqual(Record(DataSyncVersionVector.ParseStored(json)), Record(DataSyncVersionVector.ParseStored(json)));
        Assert.AreNotEqual(Record(DataSyncVersionVector.ParseStored(json)), Record(Vv((A, 4))));

        IReadOnlyDictionary<string, string> childMap = new Dictionary<string, string>();
        IReadOnlyList<string> exclusionKeys = [];
        var key = new SyncKey(keys[0]);
        var record = Record(Vv((A, 4)));

        DataSyncPeerBase Base(DataSyncVersionVector vv) =>
            new("customProperty", key, DataSyncBaseState.Normal, null, vv, childMap, null, record, exclusionKeys);

        Assert.AreEqual(Base(DataSyncVersionVector.ParseStored(json)), Base(DataSyncVersionVector.ParseStored(json)));
        Assert.AreEqual(Base(DataSyncVersionVector.ParseStored(json)).GetHashCode(),
            Base(DataSyncVersionVector.ParseStored(json)).GetHashCode());

        // One copy of the base content: the record's.
        Assert.AreSame(content, Base(Vv((A, 4))).Content);
        Assert.IsNull((Base(Vv((A, 4))) with {Record = null}).Content);
    }

    // ---- System.Text.Json ------------------------------------------------------------------

    private sealed record Holder(string Name, DataSyncVersionVector Vv, DataSyncVersionVector? Other);

    [TestMethod]
    public void SerializesAsItsCanonicalObject()
    {
        var holder = new Holder("x", Vv((B, 2), (A, 1)), null);
        var json = JsonSerializer.Serialize(holder, DataSyncJson.Options);
        Assert.AreEqual("{\"name\":\"x\",\"vv\":{\"000000000000000a\":1,\"000000000000000b\":2}}", json);
        Assert.AreEqual(holder, JsonSerializer.Deserialize<Holder>(json, DataSyncJson.Options));
        Assert.ThrowsException<JsonException>(() =>
            JsonSerializer.Deserialize<Holder>("{\"name\":\"x\",\"vv\":{\"bad\":1}}", DataSyncJson.Options));
    }

    private static DataSyncVersionVector RandomVector(Random random)
    {
        var vv = DataSyncVersionVector.Empty;
        for (var actor = 0; actor < 4; actor++)
        {
            if (random.Next(3) == 0) continue;
            vv = vv.With(Actor(actor), random.Next(1, 4));
        }

        return vv;
    }
}
