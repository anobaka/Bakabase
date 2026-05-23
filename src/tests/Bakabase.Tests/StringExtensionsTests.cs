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

    [TestMethod]
    public void Join_EscapesTheEscapeCharItself()
    {
        // "a\b" -> the lone backslash must be doubled so it is not read as an escape.
        Assert.AreEqual("a\\\\b", new[] { "a\\b" }.Join(',', '\\'));
    }

    [TestMethod]
    public void Join_ThenSplit_RoundTrips_ValueContainingEscapeChar()
    {
        var original = new List<string> { "a\\b", "c" };
        CollectionAssert.AreEqual(original, original.Join(',', '\\').SplitWithEscapeChar(',', '\\'));
    }

    [TestMethod]
    public void Join_ThenSplit_RoundTrips_ValueEndingWithEscapeChar()
    {
        // Regression: a value ending in the escape char, followed by a separator, used to
        // be misread as an escaped separator and merged with the next value.
        var original = new List<string> { "x\\", "y" };
        CollectionAssert.AreEqual(original, original.Join(',', '\\').SplitWithEscapeChar(',', '\\'));
    }

    [TestMethod]
    public void Join_ThenSplit_RoundTrips_ValueWithEscapeCharAndSeparator()
    {
        var original = new List<string> { "a\\,b" };
        CollectionAssert.AreEqual(original, original.Join(',', '\\').SplitWithEscapeChar(',', '\\'));
    }

    [TestMethod]
    public void SplitWithEscapeChar_LegacyBareBackslash_IsPreserved()
    {
        // Legacy data never escaped the escape char; a bare backslash not before a
        // separator must still be read literally (e.g. a Windows path).
        CollectionAssert.AreEqual(
            new List<string> { "C:\\foo\\bar" }, "C:\\foo\\bar".SplitWithEscapeChar(',', '\\'));
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

    #region StandardizePath / IsUncPath

    [TestMethod]
    public void StandardizePath_NullOrEmpty_ReturnsNull()
    {
        Assert.IsNull(((string?)null).StandardizePath());
        Assert.IsNull("".StandardizePath());
    }

    [TestMethod]
    public void StandardizePath_NormalizesSeparatorsToForwardSlash()
    {
        Assert.AreEqual("a/b/c", @"a\b\c".StandardizePath());
        Assert.AreEqual("a/b/c", @"a/b\c".StandardizePath());
    }

    [TestMethod]
    public void StandardizePath_CollapsesRepeatedSeparators()
    {
        Assert.AreEqual("a/b/c", "a//b///c".StandardizePath());
    }

    [TestMethod]
    public void StandardizePath_DropsTrailingSeparator()
    {
        Assert.AreEqual("a/b", "a/b/".StandardizePath());
    }

    [TestMethod]
    public void StandardizePath_PreservesLeadingSlash()
    {
        Assert.AreEqual("/a/b", "/a/b".StandardizePath());
    }

    [TestMethod]
    public void StandardizePath_WindowsDriveGetsRootSlash()
    {
        Assert.AreEqual("C:/", "C:".StandardizePath());
        Assert.AreEqual("C:/foo", @"C:\foo".StandardizePath());
    }

    [TestMethod]
    public void IsUncPath_FalseForNullEmptyAndRelativePaths()
    {
        Assert.IsFalse(((string?)null).IsUncPath());
        Assert.IsFalse("".IsUncPath());
        Assert.IsFalse("a/b".IsUncPath());
    }

    #endregion
}
