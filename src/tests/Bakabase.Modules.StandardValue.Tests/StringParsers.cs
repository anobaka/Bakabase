using Bakabase.Modules.StandardValue.Abstractions.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Tests;

/// <summary>
/// Boundary coverage for StringStandardValueExtensions — the string-to-typed
/// parsers behind StandardValue conversion: truthiness, numeric/date/time
/// parsing, and the comma / slash separated list parsers.
/// </summary>
[TestClass]
public sealed class StringParsers
{
    #region ConvertToBoolean

    [TestMethod]
    public void ConvertToBoolean_FalsyStrings_ReturnFalse()
    {
        Assert.AreEqual(false, "0".ConvertToBoolean());
        Assert.AreEqual(false, "false".ConvertToBoolean());
        Assert.AreEqual(false, "False".ConvertToBoolean());
    }

    [TestMethod]
    public void ConvertToBoolean_AnyOtherNonEmptyString_ReturnsTrue()
    {
        Assert.AreEqual(true, "1".ConvertToBoolean());
        Assert.AreEqual(true, "true".ConvertToBoolean());
        Assert.AreEqual(true, "anything".ConvertToBoolean());
    }

    [TestMethod]
    public void ConvertToBoolean_NullOrWhitespace_ReturnsNull()
    {
        string? nullStr = null;
        Assert.IsNull(nullStr.ConvertToBoolean());
        Assert.IsNull("".ConvertToBoolean());
        Assert.IsNull("   ".ConvertToBoolean());
    }

    #endregion

    #region ConvertToDecimal

    [TestMethod]
    public void ConvertToDecimal_ValidNumbers()
    {
        Assert.AreEqual(123.45m, "123.45".ConvertToDecimal());
        Assert.AreEqual(-7m, "-7".ConvertToDecimal());
        Assert.AreEqual(10m, "  10  ".ConvertToDecimal());
    }

    [TestMethod]
    public void ConvertToDecimal_InvalidOrEmpty_ReturnsNull()
    {
        Assert.IsNull("abc".ConvertToDecimal());
        Assert.IsNull("".ConvertToDecimal());
        Assert.IsNull(((string?)null).ConvertToDecimal());
    }

    #endregion

    #region ConvertToTime / ConvertToDateTime

    [TestMethod]
    public void ConvertToTime_ValidAndInvalid()
    {
        Assert.AreEqual(new TimeSpan(1, 30, 0), "01:30:00".ConvertToTime());
        Assert.IsNull("not a time".ConvertToTime());
        Assert.IsNull(((string?)null).ConvertToTime());
    }

    [TestMethod]
    public void ConvertToDateTime_ValidAndInvalid()
    {
        Assert.AreEqual(new DateTime(2024, 6, 15), "2024-06-15".ConvertToDateTime());
        Assert.IsNull("not a date".ConvertToDateTime());
        Assert.IsNull(((string?)null).ConvertToDateTime());
    }

    #endregion

    #region ConvertToListString

    [TestMethod]
    public void ConvertToListString_SplitsAndTrims()
    {
        CollectionAssert.AreEqual(new List<string> { "a", "b", "c" }, "a, b ,c".ConvertToListString());
    }

    [TestMethod]
    public void ConvertToListString_RemovesEmptyEntries()
    {
        CollectionAssert.AreEqual(new List<string> { "a", "b" }, "a,,b".ConvertToListString());
        CollectionAssert.AreEqual(new List<string> { "a", "b" }, "a, ,b".ConvertToListString());
    }

    [TestMethod]
    public void ConvertToListString_Null_ReturnsNull()
        => Assert.IsNull(((string?)null).ConvertToListString());

    #endregion

    #region ConvertToListListString

    [TestMethod]
    public void ConvertToListListString_NestedSplitByCommaThenSlash()
    {
        var result = "a/b,c".ConvertToListListString();
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result!.Count);
        CollectionAssert.AreEqual(new List<string> { "a", "b" }, result[0]);
        CollectionAssert.AreEqual(new List<string> { "c" }, result[1]);
    }

    [TestMethod]
    public void ConvertToInnerListOfListListString_SplitsBySlash()
    {
        CollectionAssert.AreEqual(new List<string> { "a", "b", "c" }, "a/b/c".ConvertToInnerListOfListListString());
        Assert.IsNull("".ConvertToInnerListOfListListString());
    }

    #endregion

    #region ConvertToListTag

    [TestMethod]
    public void ConvertToListTag_ParsesEachCommaSeparatedTag()
    {
        var result = "Group:Name,Solo".ConvertToListTag();
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result!.Count);
        Assert.AreEqual("Group", result[0].Group);
        Assert.AreEqual("Name", result[0].Name);
        Assert.IsNull(result[1].Group);
        Assert.AreEqual("Solo", result[1].Name);
    }

    #endregion
}
