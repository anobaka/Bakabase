using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Refs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Refs;

[TestClass]
public class OptionRefTests
{
    [TestMethod]
    public void TheThreeFormsRoundTrip()
    {
        var refs = new[]
        {
            OptionRef.Choice("u1", "Action"),
            OptionRef.Tag("u2", "Studio", "Kyoto"),
            OptionRef.Tag("u3", null, "Isekai"),
            OptionRef.Tag("u4", "", "Empty group"),
            OptionRef.Node("u5", ["Asia", "Japan"]),
        };
        var expected = new[]
        {
            "{\"label\":\"Action\",\"uuid\":\"u1\"}",
            "{\"group\":\"Studio\",\"name\":\"Kyoto\",\"uuid\":\"u2\"}",
            "{\"name\":\"Isekai\",\"uuid\":\"u3\"}",
            "{\"group\":\"\",\"name\":\"Empty group\",\"uuid\":\"u4\"}",
            "{\"path\":[\"Asia\",\"Japan\"],\"uuid\":\"u5\"}",
        };
        for (var i = 0; i < refs.Length; i++)
        {
            var json = refs[i].ToJson();
            Assert.AreEqual(expected[i], CanonicalJson.Serialize(json));
            Assert.IsTrue(OptionRef.TryRead(JsonNode.Parse(expected[i]), out var read), expected[i]);
            Assert.AreEqual(refs[i], read);
            Assert.AreEqual(refs[i].GetHashCode(), read!.GetHashCode());
        }
    }

    [TestMethod]
    public void EqualityComparesPathsByValue()
    {
        Assert.AreEqual(OptionRef.Node("u", ["a", "b"]), OptionRef.Node("u", new List<string> { "a", "b" }));
        Assert.AreNotEqual(OptionRef.Node("u", ["a", "b"]), OptionRef.Node("u", ["a"]));
        Assert.AreNotEqual(OptionRef.Tag("u", null, "x"), OptionRef.Tag("u", "", "x"));
        Assert.AreNotEqual(OptionRef.Choice("u", "x"), OptionRef.Tag("u", null, "x"));
    }

    [TestMethod]
    [DataRow("{\"label\":\"x\"}")]
    [DataRow("{\"uuid\":1,\"label\":\"x\"}")]
    [DataRow("{\"uuid\":\"u\"}")]
    [DataRow("{\"uuid\":\"u\",\"label\":\"x\",\"name\":\"y\"}")]
    [DataRow("{\"uuid\":\"u\",\"label\":\"x\",\"extra\":1}")]
    [DataRow("{\"uuid\":\"u\",\"group\":\"g\"}")]
    [DataRow("{\"uuid\":\"u\",\"group\":null,\"name\":\"y\"}")]
    [DataRow("{\"uuid\":\"u\",\"path\":[]}")]
    [DataRow("{\"uuid\":\"u\",\"path\":[\"a\",1]}")]
    [DataRow("{\"uuid\":\"u\",\"path\":\"a\"}")]
    [DataRow("{\"uuid\":\"u\",\"label\":\"x\",\"uuid\":\"v\"}")]
    [DataRow("[]")]
    public void TryReadRefusesAnythingElseWithoutThrowing(string json)
    {
        Assert.IsFalse(OptionRef.TryRead(JsonNode.Parse(json), out var read), json);
        Assert.IsNull(read);
    }

    [TestMethod]
    public void FactoriesRefuseMissingParts()
    {
        Assert.ThrowsException<ArgumentNullException>(() => OptionRef.Choice(null!, "x"));
        Assert.ThrowsException<ArgumentNullException>(() => OptionRef.Tag("u", null, null!));
        Assert.ThrowsException<ArgumentException>(() => OptionRef.Node("u", []));
        Assert.IsFalse(OptionRef.TryRead(null, out _));
    }
}
