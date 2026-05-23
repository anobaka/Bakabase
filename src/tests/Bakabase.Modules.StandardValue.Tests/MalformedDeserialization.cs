using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Extensions;

namespace Bakabase.Modules.StandardValue.Tests;

/// <summary>
/// DeserializeAsStandardValue must degrade gracefully on malformed input — returning
/// null rather than throwing — for every standard value type, while still throwing when
/// the caller opts in via throwOnError.
/// </summary>
[TestClass]
public sealed class MalformedDeserialization
{
    [TestMethod]
    public void Decimal_NonNumeric_ReturnsNull()
    {
        Assert.IsNull("abc".DeserializeAsStandardValue(StandardValueType.Decimal));
        Assert.IsNull("1.2.3".DeserializeAsStandardValue(StandardValueType.Decimal));
    }

    [TestMethod]
    public void Boolean_Garbage_ReturnsNull()
    {
        Assert.IsNull("yes".DeserializeAsStandardValue(StandardValueType.Boolean));
        Assert.IsNull("2".DeserializeAsStandardValue(StandardValueType.Boolean));
    }

    [TestMethod]
    public void DateTime_NonNumeric_ReturnsNull()
        => Assert.IsNull("not-a-timestamp".DeserializeAsStandardValue(StandardValueType.DateTime));

    [TestMethod]
    public void Time_NonNumeric_ReturnsNull()
        => Assert.IsNull("not-a-number".DeserializeAsStandardValue(StandardValueType.Time));

    [TestMethod]
    public void Link_MissingUrlField_ReturnsNullGracefully()
    {
        // A Link needs two comma-separated fields; a single field must not crash.
        Assert.IsNull("onlytext".DeserializeAsStandardValue(StandardValueType.Link));
    }

    [TestMethod]
    public void EmptyString_ReturnsNull_ForEveryType()
    {
        Assert.IsNull("".DeserializeAsStandardValue(StandardValueType.ListString));
        Assert.IsNull("".DeserializeAsStandardValue(StandardValueType.Decimal));
        Assert.IsNull("".DeserializeAsStandardValue(StandardValueType.Boolean));
        Assert.IsNull("".DeserializeAsStandardValue(StandardValueType.DateTime));
        Assert.IsNull("".DeserializeAsStandardValue(StandardValueType.Time));
    }

    [TestMethod]
    public void Malformed_WithThrowOnError_Throws()
        => Assert.ThrowsException<System.FormatException>(() =>
            "abc".DeserializeAsStandardValue(StandardValueType.Decimal, throwOnError: true));

    [TestMethod]
    public void ValidValues_StillDeserializeCorrectly()
    {
        Assert.AreEqual(1.5m, "1.5".DeserializeAsStandardValue(StandardValueType.Decimal));
        Assert.AreEqual(true, "True".DeserializeAsStandardValue(StandardValueType.Boolean));
    }
}
