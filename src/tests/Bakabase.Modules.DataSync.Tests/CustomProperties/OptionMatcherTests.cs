using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.Cp;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>v3.1 <c>OptionMatcherTests</c>: the Property module's own option identity (§3.3.1).</summary>
[TestClass]
public class OptionMatcherTests
{
    [TestMethod]
    public void TheComparerFollowsIgnoreCase()
    {
        Assert.IsTrue(OptionMatcher.SameChoice(C("1", "Action"), C("2", "ACTION"), ignoreCase: true));
        Assert.IsFalse(OptionMatcher.SameChoice(C("1", "Action"), C("2", "ACTION"), ignoreCase: false));
        Assert.IsTrue(OptionMatcher.SameChoice(C("1", "Action"), C("2", "Action"), ignoreCase: false));
        Assert.IsTrue(OptionMatcher.SameNode(N("1", "Asia"), N("2", "asia"), ignoreCase: true));
        Assert.IsFalse(OptionMatcher.SameNode(N("1", "Asia"), N("2", "asia"), ignoreCase: false));
    }

    [TestMethod]
    public void TheFirstEquivalentOptionWins()
    {
        var choices = new[] { C("1", "Other"), C("2", "action"), C("3", "Action"), C("4", "ACTION") };
        Assert.AreEqual("2", OptionMatcher.FindChoice(choices, "Action", ignoreCase: true)!.Uuid);
        Assert.AreEqual("3", OptionMatcher.FindChoice(choices, "Action", ignoreCase: false)!.Uuid);
        Assert.IsNull(OptionMatcher.FindChoice(choices, "Drama", ignoreCase: true));

        var siblings = new[] { N("1", "Japan"), N("2", "JAPAN") };
        Assert.AreEqual("1", OptionMatcher.FindNode(siblings, "japan", ignoreCase: true)!.Uuid);
        Assert.IsNull(OptionMatcher.FindNode(siblings, "japan", ignoreCase: false));
    }

    [TestMethod]
    public void UnicodeCases()
    {
        Assert.IsTrue(OptionMatcher.SameChoice(C("1", "Ä"), C("2", "ä"), ignoreCase: true));
        Assert.IsFalse(OptionMatcher.SameChoice(C("1", "ß"), C("2", "SS"), ignoreCase: true));
        Assert.IsFalse(OptionMatcher.SameChoice(C("1", "ı"), C("2", "I"), ignoreCase: true));
        Assert.IsTrue(OptionMatcher.SameChoice(C("1", "Ελλάδα"), C("2", "ΕΛΛΆΔΑ"), ignoreCase: true));
    }

    [TestMethod]
    public void ATagIsItsGroupAndNameAndAnEmptyGroupIsNone()
    {
        Assert.IsTrue(OptionMatcher.SameTag(T("1", "Studio", "Kyoto"), T("2", "STUDIO", "kyoto"), ignoreCase: true));
        Assert.IsFalse(OptionMatcher.SameTag(T("1", "Studio", "Kyoto"), T("2", "STUDIO", "kyoto"), ignoreCase: false));
        // The module's TagValue stores "" as null, so the service never tells the two apart.
        Assert.IsTrue(OptionMatcher.SameTag(T("1", null, "Kyoto"), T("2", "", "Kyoto"), ignoreCase: false));
        Assert.IsTrue(OptionMatcher.SameTag(T("1", null, "Kyoto"), T("2", null, "KYOTO"), ignoreCase: true));
        Assert.IsNull(OptionMatcher.GroupOf(""));
        Assert.AreEqual("g", OptionMatcher.GroupOf("g"));

        var tags = new[] { T("1", "", "A"), T("2", null, "A"), T("3", null, "a") };
        Assert.AreEqual("1", OptionMatcher.FindTag(tags, null, "a", ignoreCase: true)!.Uuid);
        Assert.AreEqual("1", OptionMatcher.FindTag(tags, "", "A", ignoreCase: false)!.Uuid);
        Assert.AreEqual("3", OptionMatcher.FindTag(tags, "", "a", ignoreCase: false)!.Uuid);

        var comparer = OptionMatcher.TagKeyComparer(true);
        Assert.IsTrue(comparer.Equals(("Studio", "Kyoto"), ("studio", "KYOTO")));
        Assert.AreEqual(comparer.GetHashCode(("Studio", "Kyoto")), comparer.GetHashCode(("studio", "KYOTO")));
        Assert.IsTrue(comparer.Equals((null, "Kyoto"), ("", "kyoto")));
        Assert.AreEqual(comparer.GetHashCode((null, "Kyoto")), comparer.GetHashCode(("", "kyoto")));
    }

    [TestMethod]
    public void LabelClassesAgreeWithTheServiceIdentityExceptForExactDuplicates()
    {
        Assert.AreEqual(ChildClasses.KeyOf(T("1", null, "Kyoto"), true), ChildClasses.KeyOf(T("2", "", "kyoto"), true));
        Assert.IsTrue(OptionMatcher.SameTag(T("1", null, "Kyoto"), T("2", "", "kyoto"), ignoreCase: true));
        // With IgnoreCase off, exact duplicates are one class; the service keeps both (its normalizer does not run).
        Assert.AreEqual(1, ChildClasses.OfChoices([C("1", "A"), C("2", "A")], false).Count);
        Assert.AreEqual(2, OptionFolding.Fold(Choice("G", false, C("1", "A"), C("2", "A"))).Content.Choices.Count);
    }
}
