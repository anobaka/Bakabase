using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Components;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Tests;

/// <summary>
/// Boundary coverage for StandardValueFactory: the normalization it applies
/// (trimming, dropping empty/null entries) and the nullable factory overloads
/// that return null rather than an empty wrapper.
/// </summary>
[TestClass]
public sealed class Factory
{
    #region String

    [TestMethod]
    public void String_TrimsSurroundingWhitespace()
    {
        Assert.AreEqual("hello", StandardValueFactory.String("  hello  ").Value);
    }

    [TestMethod]
    public void String_Null_ValueIsNull()
    {
        Assert.IsNull(StandardValueFactory.String(null).Value);
    }

    [TestMethod]
    public void String_Type_IsString()
    {
        Assert.AreEqual(StandardValueType.String, StandardValueFactory.String("x").Type);
    }

    #endregion

    #region ListString

    [TestMethod]
    public void ListString_DropsNullAndEmptyEntries()
    {
        var value = StandardValueFactory.ListString(new List<string> { "a", "", "b", null! }).Value;
        CollectionAssert.AreEqual(new List<string> { "a", "b" }, value);
    }

    [TestMethod]
    public void ListString_FromParams()
    {
        CollectionAssert.AreEqual(new List<string> { "x", "y" },
            StandardValueFactory.ListString("x", "y").Value);
    }

    [TestMethod]
    public void ListString_NullInput_ValueIsNull()
    {
        Assert.IsNull(StandardValueFactory.ListString((List<string>?)null).Value);
    }

    [TestMethod]
    public void ListString_AllEntriesEmpty_ValueIsEmptyList()
    {
        var value = StandardValueFactory.ListString(new List<string> { "", null! }).Value;
        Assert.IsNotNull(value);
        Assert.AreEqual(0, value!.Count);
    }

    #endregion

    #region ListListString

    [TestMethod]
    public void ListListString_DropsEmptyInnerLists()
    {
        var value = StandardValueFactory.ListListString(
            new List<string> { "a" }, new List<string>()).Value;
        Assert.IsNotNull(value);
        Assert.AreEqual(1, value!.Count);
        CollectionAssert.AreEqual(new List<string> { "a" }, value[0]);
    }

    [TestMethod]
    public void ListListString_NullInput_ValueIsNull()
    {
        Assert.IsNull(StandardValueFactory.ListListString((List<List<string>>?)null).Value);
    }

    #endregion

    #region ListTag

    [TestMethod]
    public void ListTag_DropsTagsWithEmptyName()
    {
        var value = StandardValueFactory.ListTag(
            new List<TagValue> { new("G", "Named"), new("G", "") }).Value;
        Assert.IsNotNull(value);
        Assert.AreEqual(1, value!.Count);
        Assert.AreEqual("Named", value[0].Name);
    }

    [TestMethod]
    public void ListTag_FromGroupNameTuples()
    {
        var value = StandardValueFactory.ListTag(("Studio", "Ghibli"), (null, "Classic")).Value;
        Assert.IsNotNull(value);
        Assert.AreEqual(2, value!.Count);
        Assert.AreEqual("Studio", value[0].Group);
        Assert.IsNull(value[1].Group);
    }

    #endregion

    #region Numeric / Boolean / DateTime / Time nullable overloads

    [TestMethod]
    public void Decimal_Value_AndType()
    {
        var v = StandardValueFactory.Decimal(12.5m);
        Assert.AreEqual(12.5m, v.Value);
        Assert.AreEqual(StandardValueType.Decimal, v.Type);
    }

    [TestMethod]
    public void Decimal_Null_ReturnsNullWrapper()
    {
        Assert.IsNull(StandardValueFactory.Decimal((decimal?)null));
    }

    [TestMethod]
    public void Boolean_Value_AndNull()
    {
        Assert.AreEqual(true, StandardValueFactory.Boolean(true).Value);
        Assert.IsNull(StandardValueFactory.Boolean((bool?)null));
    }

    [TestMethod]
    public void DateTime_Null_ReturnsNullWrapper()
    {
        Assert.IsNull(StandardValueFactory.DateTime((DateTime?)null));
    }

    [TestMethod]
    public void Time_Null_ReturnsNullWrapper()
    {
        Assert.IsNull(StandardValueFactory.Time((TimeSpan?)null));
    }

    #endregion

    #region Link

    [TestMethod]
    public void Link_FromTextAndUrl()
    {
        var v = StandardValueFactory.Link("Google", "https://google.com");
        Assert.AreEqual(StandardValueType.Link, v.Type);
        Assert.AreEqual("Google", v.Value!.Text);
        Assert.AreEqual("https://google.com", v.Value.Url);
    }

    #endregion

    #region StandardValue<T> wrapper

    [TestMethod]
    public void IsEmpty_TrueForNullEmptyStringAndEmptyList()
    {
        Assert.IsTrue(StandardValueFactory.String(null).IsEmpty);
        Assert.IsTrue(StandardValueFactory.String("").IsEmpty);
        Assert.IsTrue(StandardValueFactory.ListString(new List<string>()).IsEmpty);
    }

    [TestMethod]
    public void IsEmpty_FalseForNonEmptyValue()
    {
        Assert.IsFalse(StandardValueFactory.String("x").IsEmpty);
        Assert.IsFalse(StandardValueFactory.ListString("a").IsEmpty);
    }

    [TestMethod]
    public void AsObject_ReturnsUnderlyingValue()
    {
        Assert.AreEqual("hello", StandardValueFactory.String("hello").AsObject());
    }

    #endregion
}
