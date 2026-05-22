using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Alias.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Tests;

/// <summary>
/// Boundary coverage for AliasExtensions.BuildContextForReplacingValueWithAlias:
/// which string values are extracted from each StandardValue type, how the
/// replacement function rebuilds the value, and which types opt out (null).
/// </summary>
[TestClass]
public sealed class AliasExtensionsTests
{
    [TestMethod]
    public void String_ExtractsValueAndReplacesViaAliasMap()
    {
        var ctx = "hello".BuildContextForReplacingValueWithAlias(StandardValueType.String);
        Assert.IsTrue(ctx.HasValue);
        CollectionAssert.AreEquivalent(new List<string> { "hello" }, ctx!.Value.StringValues.ToList());
        var replaced = ctx.Value.ReplaceWithAlias(new Dictionary<string, string> { ["hello"] = "HELLO" });
        Assert.AreEqual("HELLO", replaced);
    }

    [TestMethod]
    public void ListString_ExtractsAllValuesAndReplaces()
    {
        var ctx = new List<string> { "a", "b" }.BuildContextForReplacingValueWithAlias(StandardValueType.ListString);
        Assert.IsTrue(ctx.HasValue);
        CollectionAssert.AreEquivalent(new List<string> { "a", "b" }, ctx!.Value.StringValues.ToList());
        var replaced = (List<string>)ctx.Value.ReplaceWithAlias(
            new Dictionary<string, string> { ["a"] = "A", ["b"] = "B" });
        CollectionAssert.AreEqual(new List<string> { "A", "B" }, replaced);
    }

    [TestMethod]
    public void ListListString_ExtractsFlattenedValuesAndReplaces()
    {
        var value = new List<List<string>> { new() { "a" }, new() { "b", "c" } };
        var ctx = value.BuildContextForReplacingValueWithAlias(StandardValueType.ListListString);
        Assert.IsTrue(ctx.HasValue);
        CollectionAssert.AreEquivalent(new List<string> { "a", "b", "c" }, ctx!.Value.StringValues.ToList());
        var replaced = (List<List<string>>)ctx.Value.ReplaceWithAlias(
            new Dictionary<string, string> { ["a"] = "A", ["b"] = "B", ["c"] = "C" });
        Assert.AreEqual(2, replaced.Count);
        CollectionAssert.AreEqual(new List<string> { "A" }, replaced[0]);
        CollectionAssert.AreEqual(new List<string> { "B", "C" }, replaced[1]);
    }

    [TestMethod]
    public void ListTag_ExtractsGroupAndNameAndReplaces()
    {
        var value = new List<TagValue> { new("Group", "Name") };
        var ctx = value.BuildContextForReplacingValueWithAlias(StandardValueType.ListTag);
        Assert.IsTrue(ctx.HasValue);
        CollectionAssert.AreEquivalent(new List<string> { "Group", "Name" }, ctx!.Value.StringValues.ToList());
        var replaced = (List<TagValue>)ctx.Value.ReplaceWithAlias(
            new Dictionary<string, string> { ["Group"] = "g", ["Name"] = "n" });
        Assert.AreEqual(1, replaced.Count);
        Assert.AreEqual("g", replaced[0].Group);
        Assert.AreEqual("n", replaced[0].Name);
    }

    [TestMethod]
    public void ListTag_NullGroup_OmittedFromExtractedValues()
    {
        var value = new List<TagValue> { new(null, "Name") };
        var ctx = value.BuildContextForReplacingValueWithAlias(StandardValueType.ListTag);
        Assert.IsTrue(ctx.HasValue);
        CollectionAssert.AreEquivalent(new List<string> { "Name" }, ctx!.Value.StringValues.ToList());
    }

    [TestMethod]
    public void NonTextTypes_ReturnNull()
    {
        Assert.IsFalse(true.BuildContextForReplacingValueWithAlias(StandardValueType.Boolean).HasValue);
        Assert.IsFalse(1m.BuildContextForReplacingValueWithAlias(StandardValueType.Decimal).HasValue);
        Assert.IsFalse(new LinkValue("t", "u").BuildContextForReplacingValueWithAlias(StandardValueType.Link).HasValue);
        Assert.IsFalse(DateTime.Now.BuildContextForReplacingValueWithAlias(StandardValueType.DateTime).HasValue);
        Assert.IsFalse(TimeSpan.Zero.BuildContextForReplacingValueWithAlias(StandardValueType.Time).HasValue);
    }

    [TestMethod]
    public void MismatchedClrType_ReturnsNull()
    {
        object notAString = 123;
        Assert.IsFalse(notAString.BuildContextForReplacingValueWithAlias(StandardValueType.String).HasValue);
    }
}
