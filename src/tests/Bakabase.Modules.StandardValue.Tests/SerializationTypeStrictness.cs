using System.Globalization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Extensions;
using Bakabase.Modules.StandardValue.Extensions;

namespace Bakabase.Modules.StandardValue.Tests;

/// <summary>
/// SerializeAsStandardValue must be type-strict: a value of the wrong CLR type returns
/// null instead of persisting garbage via ToString(), and non-decimal numerics are
/// normalized through decimal with InvariantCulture.
/// </summary>
[TestClass]
public sealed class SerializationTypeStrictness
{
    private static void WithCulture(string culture, Action action)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void Decimal_NonDecimalNumerics_SerializeInvariant()
    {
        WithCulture("de-DE", () =>
        {
            Assert.AreEqual("1.5", ((object)1.5d).SerializeAsStandardValue(StandardValueType.Decimal));
            Assert.AreEqual("42", ((object)42).SerializeAsStandardValue(StandardValueType.Decimal));
            Assert.AreEqual("1.5", ((object)1.5f).SerializeAsStandardValue(StandardValueType.Decimal));
        });
    }

    [TestMethod]
    public void Decimal_NonNumericValue_ReturnsNull()
    {
        Assert.IsNull(((object)"not a number").SerializeAsStandardValue(StandardValueType.Decimal));
        Assert.IsNull(((object)true).SerializeAsStandardValue(StandardValueType.Decimal));
    }

    [TestMethod]
    public void Boolean_NonBoolValue_ReturnsNull()
    {
        Assert.IsNull(((object)"true").SerializeAsStandardValue(StandardValueType.Boolean));
        Assert.IsNull(((object)1).SerializeAsStandardValue(StandardValueType.Boolean));
    }

    [TestMethod]
    public void Boolean_SerializesAsBoolToString()
    {
        Assert.AreEqual("True", ((object)true).SerializeAsStandardValue(StandardValueType.Boolean));
        Assert.AreEqual("False", ((object)false).SerializeAsStandardValue(StandardValueType.Boolean));
    }

    [TestMethod]
    public void ConvertToDecimal_PrefersInvariantOverMachineCulture()
    {
        WithCulture("de-DE", () =>
        {
            // Previously de-DE treated '.' as a group separator and parsed "1.5" as 15.
            Assert.AreEqual(1.5m, "1.5".ConvertToDecimal());
            // Regional decimal-comma input still parses via the CurrentCulture fallback.
            Assert.AreEqual(1.5m, "1,5".ConvertToDecimal());
        });
    }
}
