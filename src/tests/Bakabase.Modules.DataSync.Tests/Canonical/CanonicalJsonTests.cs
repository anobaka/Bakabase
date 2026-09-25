using System.Text;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Canonical;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Canonical;

[TestClass]
public class CanonicalJsonTests
{
    // ---- v3.1 §3.1 vectors -----------------------------------------------------------------

    [TestMethod]
    public void SortsMembersAndKeepsArrayOrder()
    {
        var node = JsonNode.Parse("{\"b\":1,\"a\":\"é\\n\",\"c\":[true,{\"y\":2,\"x\":\"<\"}]}");
        Assert.AreEqual("{\"a\":\"é\\n\",\"b\":1,\"c\":[true,{\"x\":\"<\",\"y\":2}]}", CanonicalJson.Serialize(node));
    }

    [TestMethod]
    public void EscapesControlCharactersWithLowercaseHex()
    {
        Assert.AreEqual("{\"k\":\"\\u0007\"}", CanonicalJson.Serialize(JsonNode.Parse("{\"k\":\"\\u0007\"}")));
        Assert.AreEqual("\"\\u001f\\u000b\\u0000\"", CanonicalJson.Serialize(JsonValue.Create("\u001f\u000b\0")));
    }

    [TestMethod]
    public void HashOfTheEmptyObject()
    {
        Assert.AreEqual("sha256:44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a",
            ContentHash.Of(new JsonObject()));
    }

    // ---- escaping ----------------------------------------------------------------------------

    [TestMethod]
    public void EscapesMinimally()
    {
        var value = JsonValue.Create("\"\\\b\f\n\r\t/<>&'\u007f\u2028é日本😀");
        Assert.AreEqual("\"\\\"\\\\\\b\\f\\n\\r\\t/<>&'\u007f\u2028é日本😀\"", CanonicalJson.Serialize(value));
    }

    [TestMethod]
    public void EscapesKeysLikeValues()
    {
        var node = new JsonObject { ["a\"b"] = 1, ["\n"] = 2 };
        Assert.AreEqual("{\"\\n\":2,\"a\\\"b\":1}", CanonicalJson.Serialize(node));
    }

    [TestMethod]
    public void WritesNonAsciiAsUtf8()
    {
        var bytes = CanonicalJson.SerializeToUtf8Bytes(JsonValue.Create("é😀"));
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("\"é😀\""), bytes);
        Assert.AreNotEqual(0xEF, bytes[0], "No BOM.");
    }

    // ---- local strings (v3.1 B3) -------------------------------------------------------------

    [TestMethod]
    public void LocalStringsWithNulAndLoneSurrogatesHashStablyAndDistinctly()
    {
        Assert.AreEqual("\"a\\u0000\"", CanonicalJson.Serialize(JsonValue.Create("a\0")));
        Assert.AreEqual("\"\\ud800x\"", CanonicalJson.Serialize(JsonValue.Create("\ud800x")));
        Assert.AreEqual("\"\\udc00\"", CanonicalJson.Serialize(JsonValue.Create("\udc00")));
        Assert.AreEqual("\"x\\udbff\"", CanonicalJson.Serialize(JsonValue.Create("x\udbff")));
        Assert.AreEqual("\"\\udc00\\ud800\"", CanonicalJson.Serialize(JsonValue.Create("\udc00\ud800")));

        var hashes = new[] { "\ud800x", "\udc00", "\ufffdx", "a\0", "a" }
            .Select(s => ContentHash.Of(new JsonObject { ["label"] = s }))
            .ToArray();
        Assert.AreEqual(hashes.Length, hashes.Distinct().Count());
        CollectionAssert.AreEqual(hashes, new[] { "\ud800x", "\udc00", "\ufffdx", "a\0", "a" }
            .Select(s => ContentHash.Of(new JsonObject { ["label"] = s })).ToArray());
    }

    // ---- key order ---------------------------------------------------------------------------

    [TestMethod]
    public void SortsKeysByUtf16CodeUnits()
    {
        // U+1F600 is the surrogate pair D83D DE00, which sorts before U+FB01 by code unit (by code point it
        // would sort after). Upper case sorts before lower case; a prefix sorts first.
        var node = new JsonObject { ["\ufb01"] = 1, ["😀"] = 2, ["b"] = 3, ["B"] = 4, ["ba"] = 5, [""] = 6 };
        Assert.AreEqual("{\"\":6,\"B\":4,\"b\":3,\"ba\":5,\"😀\":2,\"\ufb01\":1}", CanonicalJson.Serialize(node));
    }

    // ---- numbers -----------------------------------------------------------------------------

    [TestMethod]
    [DataRow("0", "0")]
    [DataRow("-0", "0")]
    [DataRow("-1", "-1")]
    [DataRow("9223372036854775807", "9223372036854775807")]
    [DataRow("-9223372036854775808", "-9223372036854775808")]
    public void WritesParsedIntegers(string json, string expected)
    {
        Assert.AreEqual(expected, CanonicalJson.Serialize(JsonNode.Parse(json)));
    }

    [TestMethod]
    [DataRow("1.5")]
    [DataRow("1.0")]
    [DataRow("1e2")]
    [DataRow("9223372036854775808")]
    [DataRow("-9223372036854775809")]
    public void RefusesParsedNonIntegers(string json)
    {
        Assert.ThrowsException<InvalidOperationException>(() => CanonicalJson.Serialize(JsonNode.Parse(json)));
    }

    [TestMethod]
    public void WritesClrIntegersAndRefusesFloats()
    {
        var node = new JsonObject
        {
            ["int"] = 1, ["long"] = long.MaxValue, ["short"] = (short)-3, ["byte"] = (byte)4, ["uint"] = 5u,
            ["ulong"] = 6ul, ["decimal"] = 7m,
        };
        Assert.AreEqual("{\"byte\":4,\"decimal\":7,\"int\":1,\"long\":9223372036854775807,\"short\":-3,\"uint\":5,\"ulong\":6}",
            CanonicalJson.Serialize(node));

        Assert.ThrowsException<InvalidOperationException>(() => CanonicalJson.Serialize(JsonValue.Create(1.5)));
        Assert.ThrowsException<InvalidOperationException>(() => CanonicalJson.Serialize(JsonValue.Create(2.0)));
        Assert.ThrowsException<InvalidOperationException>(() => CanonicalJson.Serialize(JsonValue.Create(1.5f)));
        Assert.ThrowsException<InvalidOperationException>(() => CanonicalJson.Serialize(JsonValue.Create(1.5m)));
        Assert.ThrowsException<InvalidOperationException>(() => CanonicalJson.Serialize(JsonValue.Create(ulong.MaxValue)));
    }

    [TestMethod]
    public void RefusesNonStringClrValues()
    {
        Assert.ThrowsException<InvalidOperationException>(() => CanonicalJson.Serialize(JsonValue.Create(Guid.Empty)));
        Assert.ThrowsException<InvalidOperationException>(() =>
            CanonicalJson.Serialize(JsonValue.Create(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))));
    }

    // ---- other literals and structure -------------------------------------------------------

    [TestMethod]
    public void WritesLiteralsAndNull()
    {
        var node = new JsonObject { ["t"] = true, ["f"] = false, ["n"] = null, ["c"] = JsonValue.Create('x') };
        Assert.AreEqual("{\"c\":\"x\",\"f\":false,\"n\":null,\"t\":true}", CanonicalJson.Serialize(node));
        Assert.AreEqual("null", CanonicalJson.Serialize(null));
        Assert.AreEqual("[null,1,[]]", CanonicalJson.Serialize(new JsonArray(null, 1, new JsonArray())));
    }

    [TestMethod]
    public void NestedObjectsAndArrays()
    {
        var node = JsonNode.Parse("""
            { "nodes": [ { "label": "Asia", "children": [ { "label": "Japan", "uuid": "b" }, { "uuid": "c", "label": "China" } ], "uuid": "a" } ],
              "name": "Region", "type": "Multilevel", "empty": {}, "list": [] }
            """);
        Assert.AreEqual(
            "{\"empty\":{},\"list\":[],\"name\":\"Region\",\"nodes\":[{\"children\":[{\"label\":\"Japan\",\"uuid\":\"b\"}," +
            "{\"label\":\"China\",\"uuid\":\"c\"}],\"label\":\"Asia\",\"uuid\":\"a\"}],\"type\":\"Multilevel\"}",
            CanonicalJson.Serialize(node));
    }

    [TestMethod]
    public void DeepNestingIsWritten()
    {
        JsonNode node = new JsonObject { ["leaf"] = 1 };
        for (var i = 0; i < 100; i++) node = new JsonObject { ["c"] = new JsonArray(node) };
        var text = CanonicalJson.Serialize(node);
        Assert.IsTrue(text.StartsWith("{\"c\":[{\"c\":["));
        Assert.IsTrue(text.EndsWith("]}]}"));
    }

    [TestMethod]
    public void IsIdempotent()
    {
        foreach (var json in new[]
                 {
                     "{\"b\":1,\"a\":\"é\\n\",\"c\":[true,{\"y\":2,\"x\":\"<\"}]}",
                     "{\"z\":{\"y\":{\"x\":[3,2,1]}},\"a\":\"\\u0001\\\"\"}",
                     "[{\"b\":null,\"a\":false},\"日本\",-12]",
                 })
        {
            var once = CanonicalJson.Serialize(JsonNode.Parse(json));
            Assert.AreEqual(once, CanonicalJson.Serialize(JsonNode.Parse(once)));
        }
    }

    // ---- hashing -----------------------------------------------------------------------------

    [TestMethod]
    public void HashIsStableAcrossMemberOrderAndConstruction()
    {
        var parsed = JsonNode.Parse("{\"name\":\"Genre\",\"type\":\"SingleChoice\",\"choices\":[{\"label\":\"A\",\"uuid\":\"1\"}]}");
        var built = new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject { ["uuid"] = "1", ["label"] = "A" }),
            ["type"] = "SingleChoice",
            ["name"] = "Genre",
        };
        Assert.AreEqual(ContentHash.Of(parsed), ContentHash.Of(built));
        Assert.AreEqual(ContentHash.Of(built),
            ContentHash.OfCanonicalBytes(CanonicalJson.SerializeToUtf8Bytes(built)));
        Assert.AreNotEqual(ContentHash.Of(built),
            ContentHash.Of(JsonNode.Parse("{\"name\":\"Genre\",\"type\":\"SingleChoice\",\"choices\":[{\"label\":\"B\",\"uuid\":\"1\"}]}")));
        // Pinned, so an accidental change of the canonical form fails here.
        Assert.AreEqual("sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes("{\"choices\":[{\"label\":\"A\",\"uuid\":\"1\"}],\"name\":\"Genre\",\"type\":\"SingleChoice\"}"))),
            ContentHash.Of(built));
    }

    [TestMethod]
    [DataRow("sha256:44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a", true)]
    [DataRow("sha256:44136FA355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a", false)]
    [DataRow("sha256:44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8", false)]
    [DataRow("md5:44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a0", false)]
    [DataRow(null, false)]
    public void HashFormat(string? hash, bool valid)
    {
        Assert.AreEqual(valid, ContentHash.IsValid(hash));
    }
}
