using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Tests;

/// <summary>
/// Boundary coverage for the StandardValue domain value objects LinkValue and
/// TagValue: their ToString rendering, emptiness, parsing, and the
/// constructor-level normalization.
/// </summary>
[TestClass]
public sealed class DomainValues
{
    #region LinkValue

    [TestMethod]
    public void LinkValue_ToString_BothParts_RendersMarkdown()
        => Assert.AreEqual("[Google](https://g.com)", new LinkValue("Google", "https://g.com").ToString());

    [TestMethod]
    public void LinkValue_ToString_OneSideOnly_RendersThatSide()
    {
        Assert.AreEqual("Google", new LinkValue("Google", null).ToString());
        Assert.AreEqual("https://g.com", new LinkValue(null, "https://g.com").ToString());
    }

    [TestMethod]
    public void LinkValue_ToString_BothEmpty_ReturnsNull()
    {
        Assert.IsNull(new LinkValue(null, null).ToString());
        Assert.IsNull(new LinkValue("", "").ToString());
    }

    [TestMethod]
    public void LinkValue_ToString_TrimsWhitespace()
        => Assert.AreEqual("[G](u)", new LinkValue("  G  ", "  u  ").ToString());

    [TestMethod]
    public void LinkValue_IsEmpty_TrueWhenBothBlank()
    {
        Assert.IsTrue(new LinkValue(null, null).IsEmpty);
        Assert.IsTrue(new LinkValue("   ", "   ").IsEmpty);
        Assert.IsFalse(new LinkValue("G", null).IsEmpty);
    }

    [TestMethod]
    public void LinkValue_TryParse_MarkdownFormat()
    {
        var lv = LinkValue.TryParse("[Google](https://g.com)");
        Assert.IsNotNull(lv);
        Assert.AreEqual("Google", lv!.Text);
        Assert.AreEqual("https://g.com", lv.Url);
    }

    [TestMethod]
    public void LinkValue_TryParse_PlainText_BecomesTextOnlyLink()
    {
        var lv = LinkValue.TryParse("just text");
        Assert.IsNotNull(lv);
        Assert.AreEqual("just text", lv!.Text);
        Assert.IsNull(lv.Url);
    }

    [TestMethod]
    public void LinkValue_TryParse_NullOrWhitespace_ReturnsNull()
    {
        Assert.IsNull(LinkValue.TryParse(null));
        Assert.IsNull(LinkValue.TryParse("   "));
    }

    [TestMethod]
    public void LinkValue_TextUrlComparer_ComparesBothFields()
    {
        Assert.IsTrue(LinkValue.TextUrlComparer.Equals(new LinkValue("a", "b"), new LinkValue("a", "b")));
        Assert.IsFalse(LinkValue.TextUrlComparer.Equals(new LinkValue("a", "b"), new LinkValue("a", "c")));
    }

    #endregion

    #region TagValue

    [TestMethod]
    public void TagValue_Constructor_BlankGroup_NormalizedToNull()
    {
        Assert.IsNull(new TagValue("", "Name").Group);
        Assert.IsNull(new TagValue(null, "Name").Group);
    }

    [TestMethod]
    public void TagValue_ToString_WithAndWithoutGroup()
    {
        Assert.AreEqual("Group:Name", new TagValue("Group", "Name").ToString());
        Assert.AreEqual("Name", new TagValue(null, "Name").ToString());
    }

    [TestMethod]
    public void TagValue_TryParse_GroupAndName()
    {
        var tv = TagValue.TryParse("Group:Name");
        Assert.IsNotNull(tv);
        Assert.AreEqual("Group", tv!.Group);
        Assert.AreEqual("Name", tv.Name);
    }

    [TestMethod]
    public void TagValue_TryParse_NameOnly()
    {
        var tv = TagValue.TryParse("Name");
        Assert.IsNotNull(tv);
        Assert.IsNull(tv!.Group);
        Assert.AreEqual("Name", tv.Name);
    }

    [TestMethod]
    public void TagValue_TryParse_MultipleSeparators_FirstIsGroupRestIsName()
    {
        var tv = TagValue.TryParse("a:b:c");
        Assert.IsNotNull(tv);
        Assert.AreEqual("a", tv!.Group);
        Assert.AreEqual("b:c", tv.Name);
    }

    [TestMethod]
    public void TagValue_TryParse_NullOrWhitespace_ReturnsNull()
    {
        Assert.IsNull(TagValue.TryParse(null));
        Assert.IsNull(TagValue.TryParse("   "));
    }

    [TestMethod]
    public void TagValue_GroupNameComparer_ComparesBothFields()
    {
        Assert.IsTrue(TagValue.GroupNameComparer.Equals(new TagValue("g", "n"), new TagValue("g", "n")));
        Assert.IsFalse(TagValue.GroupNameComparer.Equals(new TagValue("g", "n"), new TagValue("x", "n")));
    }

    #endregion
}
