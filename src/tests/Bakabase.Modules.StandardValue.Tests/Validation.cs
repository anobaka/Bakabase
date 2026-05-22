using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Tests;

/// <summary>
/// Boundary coverage for StandardValue type checking and inference:
/// <see cref="StandardValueExtensions.IsStandardValueType"/> (does a CLR value
/// satisfy a StandardValueType) and InferStandardValueType (which
/// StandardValueType a CLR type / value maps to).
/// </summary>
[TestClass]
public sealed class Validation
{
    #region IsStandardValueType

    [TestMethod]
    public void IsStandardValueType_Null_IsValidForEveryType()
    {
        foreach (var type in Enum.GetValues<StandardValueType>())
        {
            Assert.IsTrue(((object?)null).IsStandardValueType(type), $"null should be valid for {type}.");
        }
    }

    [TestMethod]
    public void IsStandardValueType_MatchingValue_ReturnsTrue()
    {
        Assert.IsTrue("text".IsStandardValueType(StandardValueType.String));
        Assert.IsTrue(new List<string>().IsStandardValueType(StandardValueType.ListString));
        Assert.IsTrue(1.5m.IsStandardValueType(StandardValueType.Decimal));
        Assert.IsTrue(new LinkValue("t", "u").IsStandardValueType(StandardValueType.Link));
        Assert.IsTrue(true.IsStandardValueType(StandardValueType.Boolean));
        Assert.IsTrue(DateTime.Now.IsStandardValueType(StandardValueType.DateTime));
        Assert.IsTrue(TimeSpan.Zero.IsStandardValueType(StandardValueType.Time));
        Assert.IsTrue(new List<List<string>>().IsStandardValueType(StandardValueType.ListListString));
        Assert.IsTrue(new List<TagValue>().IsStandardValueType(StandardValueType.ListTag));
    }

    [TestMethod]
    public void IsStandardValueType_MismatchedValue_ReturnsFalse()
    {
        Assert.IsFalse("text".IsStandardValueType(StandardValueType.Decimal));
        Assert.IsFalse(1.5m.IsStandardValueType(StandardValueType.String));
        Assert.IsFalse(true.IsStandardValueType(StandardValueType.DateTime));
        Assert.IsFalse(DateTime.Now.IsStandardValueType(StandardValueType.Time));
        Assert.IsFalse(TimeSpan.Zero.IsStandardValueType(StandardValueType.DateTime));
    }

    [TestMethod]
    public void IsStandardValueType_ListStringIsNotListListString()
    {
        // List<string> and List<List<string>> must not be confused for each other.
        object listString = new List<string> { "a" };
        Assert.IsTrue(listString.IsStandardValueType(StandardValueType.ListString));
        Assert.IsFalse(listString.IsStandardValueType(StandardValueType.ListListString));
    }

    [TestMethod]
    public void IsStandardValueType_BoxedInt32IsNotDecimal()
    {
        // The check is a strict type match — a boxed Int32 is not a Decimal.
        object boxedInt = 42;
        Assert.IsFalse(boxedInt.IsStandardValueType(StandardValueType.Decimal));
    }

    [TestMethod]
    public void IsStandardValueType_UndefinedEnumValue_Throws()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => { _ = "text".IsStandardValueType((StandardValueType)999); });
    }

    #endregion

    #region InferStandardValueType (from Type)

    [TestMethod]
    public void InferStandardValueType_FromKnownClrType_MapsCorrectly()
    {
        Assert.AreEqual(StandardValueType.String, typeof(string).InferStandardValueType());
        Assert.AreEqual(StandardValueType.ListString, typeof(List<string>).InferStandardValueType());
        Assert.AreEqual(StandardValueType.Decimal, typeof(decimal).InferStandardValueType());
        Assert.AreEqual(StandardValueType.Link, typeof(LinkValue).InferStandardValueType());
        Assert.AreEqual(StandardValueType.Boolean, typeof(bool).InferStandardValueType());
        Assert.AreEqual(StandardValueType.DateTime, typeof(DateTime).InferStandardValueType());
        Assert.AreEqual(StandardValueType.Time, typeof(TimeSpan).InferStandardValueType());
        Assert.AreEqual(StandardValueType.ListListString, typeof(List<List<string>>).InferStandardValueType());
        Assert.AreEqual(StandardValueType.ListTag, typeof(List<TagValue>).InferStandardValueType());
    }

    [TestMethod]
    public void InferStandardValueType_FromUnknownType_ReturnsNull()
    {
        Assert.IsNull(typeof(int).InferStandardValueType());
        Assert.IsNull(typeof(object).InferStandardValueType());
        Assert.IsNull(typeof(Guid).InferStandardValueType());
    }

    #endregion

    #region InferStandardValueType (from value)

    [TestMethod]
    public void InferStandardValueType_FromValue_MapsCorrectly()
    {
        Assert.AreEqual(StandardValueType.String, "text".InferStandardValueType());
        Assert.AreEqual(StandardValueType.Decimal, 1.5m.InferStandardValueType());
        Assert.AreEqual(StandardValueType.Boolean, true.InferStandardValueType());
        Assert.AreEqual(StandardValueType.Time, TimeSpan.Zero.InferStandardValueType());
        Assert.AreEqual(StandardValueType.ListString, new List<string> { "a" }.InferStandardValueType());
    }

    [TestMethod]
    public void InferStandardValueType_FromNullValue_ReturnsNull()
    {
        object? value = null;
        Assert.IsNull(value.InferStandardValueType());
    }

    [TestMethod]
    public void InferStandardValueType_FromUnknownValue_ReturnsNull()
    {
        object value = 42;
        Assert.IsNull(value.InferStandardValueType());
    }

    #endregion
}
