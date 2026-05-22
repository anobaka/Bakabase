using Bakabase.Abstractions.Extensions;

namespace Bakabase.Tests;

/// <summary>
/// Boundary coverage for the abstractions-level string utilities: the
/// escape-aware split/join that backs StandardValue serialization, natural
/// sort, and blank-trimming.
/// </summary>
[TestClass]
public sealed class StringExtensionsTests
{
    #region SplitWithEscapeChar / Join

    [TestMethod]
    public void SplitWithEscapeChar_PlainSeparators()
    {
        CollectionAssert.AreEqual(
            new List<string> { "a", "b", "c" }, "a,b,c".SplitWithEscapeChar(',', '\\'));
    }

    [TestMethod]
    public void SplitWithEscapeChar_EscapedSeparatorStaysInValue()
    {
        // the literal text  a\,b,c  -> the escaped comma is not a split point
        CollectionAssert.AreEqual(
            new List<string> { "a,b", "c" }, @"a\,b,c".SplitWithEscapeChar(',', '\\'));
    }

    [TestMethod]
    public void SplitWithEscapeChar_SingleItem()
    {
        CollectionAssert.AreEqual(new List<string> { "abc" }, "abc".SplitWithEscapeChar(',', '\\'));
    }

    [TestMethod]
    public void SplitWithEscapeChar_EmptyString_ReturnsNull()
    {
        Assert.IsNull("".SplitWithEscapeChar(',', '\\'));
    }

    [TestMethod]
    public void SplitWithEscapeChar_Nested_HighThenLowSeparator()
    {
        var result = "a,b;c,d".SplitWithEscapeChar(';', ',', '\\');
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result!.Count);
        CollectionAssert.AreEqual(new List<string> { "a", "b" }, result[0]);
        CollectionAssert.AreEqual(new List<string> { "c", "d" }, result[1]);
    }

    [TestMethod]
    public void Join_EscapesSeparatorWithinValues()
    {
        Assert.AreEqual(@"a\,b,c", new[] { "a,b", "c" }.Join(',', '\\'));
    }

    [TestMethod]
    public void Join_ThenSplit_RoundTrips()
    {
        var original = new List<string> { "x,y", "z", "plain" };
        var joined = original.Join(',', '\\');
        CollectionAssert.AreEqual(original, joined.SplitWithEscapeChar(',', '\\'));
    }

    #endregion

    #region OrderByNatural

    [TestMethod]
    public void OrderByNatural_SortsEmbeddedNumbersNumerically()
    {
        var sorted = new[] { "a10", "a2", "a1" }.OrderByNatural().ToList();
        CollectionAssert.AreEqual(new List<string> { "a1", "a2", "a10" }, sorted);
    }

    [TestMethod]
    public void OrderByNatural_IsCaseInsensitive()
    {
        var sorted = new[] { "B", "a", "C" }.OrderByNatural().ToList();
        CollectionAssert.AreEqual(new List<string> { "a", "B", "C" }, sorted);
    }

    [TestMethod]
    public void OrderByNatural_NumbersBeforePureText()
    {
        var sorted = new[] { "file", "10", "2" }.OrderByNatural().ToList();
        // numeric parts sort ahead of text
        Assert.AreEqual("2", sorted[0]);
        Assert.AreEqual("10", sorted[1]);
        Assert.AreEqual("file", sorted[2]);
    }

    #endregion

    #region TrimAndRemoveEmpty

    [TestMethod]
    public void TrimAndRemoveEmpty_TrimsAndDropsBlankEntries()
    {
        var result = new[] { "  a  ", "", null, "  ", "b" }.TrimAndRemoveEmpty();
        CollectionAssert.AreEqual(new List<string> { "a", "b" }, result);
    }

    [TestMethod]
    public void TrimAndRemoveEmpty_AllBlank_ReturnsNull()
    {
        Assert.IsNull(new[] { "", "   ", null }.TrimAndRemoveEmpty());
    }

    #endregion
}
