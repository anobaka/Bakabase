using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.Cp;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>
/// §3.4: the comparison form is id-free and class-folded — one entry per label class, sorted by key, the
/// representative's colour, multilevel classes uniting their members' subtrees, <c>defaultValue</c> as class keys,
/// <c>childrenLocal</c>, <c>orderKey</c> and unknown members verbatim.
/// </summary>
[TestClass]
public class ComparisonFormTests
{
    [TestMethod]
    public void TheSpecExampleForm()
    {
        var genre = Choice("Genre", true, C("d", "Drama"), C("a", "Action", "#e5484d"), C("a2", "action", "#000000"))
            with { DefaultValue = [Ref("d", "Drama")] };
        Assert.AreEqual(
            """{"choices":[{"color":"#e5484d","key":"ACTION"},{"key":"DRAMA"}],"defaultValue":["DRAMA"],"ignoreCase":true,"name":"Genre","orderKey":"a0V","type":"MultipleChoice"}""",
            Form(genre, "a0V"));
    }

    [TestMethod]
    public void DuplicatesCollapseAndTheFirstMemberIsTheRepresentative()
    {
        var first = Choice("G", true, C("1", "Action", "#111"), C("2", "ACTION", "#222"), C("3", "action"));
        var reordered = Choice("G", true, C("3", "action"), C("1", "Action", "#111"), C("2", "ACTION", "#222"));
        Assert.AreEqual("""{"choices":[{"color":"#111","key":"ACTION"}],"ignoreCase":true,"name":"G","type":"MultipleChoice"}""",
            Form(first));
        // Another first member is another representative: its (empty) colour is the class's.
        Assert.AreEqual("""{"choices":[{"key":"ACTION"}],"ignoreCase":true,"name":"G","type":"MultipleChoice"}""",
            Form(reordered));
    }

    [TestMethod]
    public void IdsAndChildOrderAreNotInTheForm()
    {
        var a = Choice("G", false, C("1", "B"), C("2", "A", "#fff"));
        var b = Choice("G", false, C("x", "A", "#fff"), C("y", "B"));
        Assert.AreEqual(Form(a), Form(b));
        Assert.IsFalse(Form(a).Contains("uuid"));
    }

    [TestMethod]
    public void WithIgnoreCaseOffExactDuplicatesAreOneClassButCasingIsNot()
    {
        var content = Choice("G", false, C("1", "Action"), C("2", "Action"), C("3", "action"));
        Assert.AreEqual("""{"choices":[{"key":"Action"},{"key":"action"}],"ignoreCase":false,"name":"G","type":"MultipleChoice"}""",
            Form(content));
    }

    [TestMethod]
    public void ATagGroupNullEqualsEmptyAndIsOmitted()
    {
        var withNull = Tags("T", false, T("1", null, "Isekai"), T("2", "Studio", "Kyoto", "#abc"));
        var withEmpty = Tags("T", false, T("9", "", "Isekai"), T("8", "Studio", "Kyoto", "#abc"));
        Assert.AreEqual(
            """{"ignoreCase":false,"name":"T","tags":[{"name":"Isekai"},{"color":"#abc","group":"Studio","name":"Kyoto"}],"type":"Tags"}""",
            Form(withNull));
        Assert.AreEqual(Form(withNull), Form(withEmpty));
        Assert.AreNotEqual(Canon(withNull), Canon(withEmpty), "content keeps them apart; only the form folds them");
    }

    [TestMethod]
    public void TagClassesFoldGroupAndNameAndSortByGroupThenName()
    {
        var tags = Tags("T", true, T("1", "b", "x"), T("2", null, "z"), T("3", "B", "X", "#1"), T("4", "a", "y"));
        Assert.AreEqual(
            """{"ignoreCase":true,"name":"T","tags":[{"name":"Z"},{"group":"A","name":"Y"},{"group":"B","name":"X"}],"type":"Tags"}""",
            Form(tags));
    }

    [TestMethod]
    public void MultilevelMembersSubtreesUniteAndNest()
    {
        var tree = Tree("R", true,
            N("1", "Asia", "#0a0", N("2", "Japan"), N("3", "China")),
            N("4", "ASIA", N("5", "japan", "#f00", N("6", "Kyoto")), N("7", "Korea")),
            N("8", "Europe"));
        Assert.AreEqual(
            """{"ignoreCase":true,"name":"R","nodes":[{"children":[{"key":"CHINA"},{"children":[{"key":"KYOTO"}],"key":"JAPAN"},{"key":"KOREA"}],"color":"#0a0","key":"ASIA"},{"key":"EUROPE"}],"settings":{"valueIsSingleton":false},"type":"Multilevel"}""",
            Form(tree));
    }

    [TestMethod]
    public void DefaultValuesAreSortedClassKeysAndNodePaths()
    {
        var choices = Choice("G", true, C("1", "Drama"), C("2", "action"), C("3", "ACTION"))
            with { DefaultValue = [Ref("1", "Drama"), Ref("3", "ACTION"), Ref("zz", "action"), Ref("gone", "Nothing")] };
        StringAssert.Contains(Form(choices), "\"defaultValue\":[\"ACTION\",\"DRAMA\"]");

        var tree = Tree("R", false, N("a", "Asia", N("j", "Japan")), N("e", "Europe"))
            with { DefaultValue = [NodeRef("j", "Asia", "Japan"), NodeRef("x", "Europe")] };
        StringAssert.Contains(Form(tree), "\"defaultValue\":[[\"Asia\",\"Japan\"],[\"Europe\"]]");
    }

    [TestMethod]
    public void ChildrenLocalDropsChildrenAndDefaultOnReferenceTypesOnly()
    {
        var genre = Choice("G", false, C("1", "A")) with { DefaultValue = [Ref("1", "A")] };
        Assert.AreEqual("""{"childrenLocal":true,"ignoreCase":false,"name":"G","orderKey":"k","type":"MultipleChoice"}""",
            Form(genre, "k", childrenLocal: true));
        Assert.AreEqual(Form(genre, "k", childrenLocal: true), Form(genre with { ChildrenLocal = true }, "k"));

        var number = new CustomPropertyContentV1
        {
            Name = "N", Type = PropertyType.Number, Settings = new CustomPropertySettingsV1 { Precision = 2 },
        };
        Assert.AreEqual("""{"name":"N","settings":{"precision":2},"type":"Number"}""", Form(number, childrenLocal: true));
    }

    [TestMethod]
    public void EmptyColoursAreOmittedAndOrderKeyIsEmittedWhenGiven()
    {
        var content = Choice("G", false, C("1", "A", ""));
        Assert.AreEqual("""{"choices":[{"key":"A"}],"ignoreCase":false,"name":"G","type":"MultipleChoice"}""", Form(content));
        StringAssert.Contains(Form(content, "a0"), "\"orderKey\":\"a0\"");
    }

    [TestMethod]
    public void UnknownMembersAreAddedVerbatimAndNeverOverwriteAFormMember()
    {
        var content = Choice("G", false, C("1", "A"));
        var unknown = new JsonObject
        {
            ["x-future"] = new JsonObject { ["b"] = 1, ["a"] = new JsonArray("q") },
            ["name"] = "hijack",
            ["choices"] = "hijack",
            ["defaultValue"] = "hijack",
            ["orderKey"] = "hijack",
        };
        var form = Untyped.ComparisonForm(content, "o1", false, unknown);
        Assert.AreEqual(
            """{"choices":[{"key":"A"}],"ignoreCase":false,"name":"G","orderKey":"o1","type":"MultipleChoice","x-future":{"a":["q"],"b":1}}""",
            Canon(form));
        Assert.AreEqual(ContentHash.Of(form), Untyped.SharedHash(content, "o1", false, unknown));
        Assert.AreEqual(ContentHash.Of(Codec.ComparisonForm(content, "o1", false)),
            Untyped.SharedHash(content, "o1", false, null));
        Assert.AreSame(unknown, unknown["x-future"]!.Parent, "members are copied, never moved out of the caller's object");
    }

    [TestMethod]
    public void TheFormNeverContainsNulls()
    {
        foreach (var content in GoldenContents()) AssertNoNulls(Codec.ComparisonForm(content, null, false));
    }

    // ---- the golden: any change to any vector bumps ComparisonFormVersion --------------------

    /// <summary>
    /// If this fails, the comparison form changed. Bump <see cref="CustomPropertyCodec.CurrentComparisonFormVersion"/>
    /// (Refresh then recomputes every stored SharedHash without issuing revisions, §3.4, §6.1) and update the version
    /// and the vectors here together.
    /// </summary>
    [TestMethod]
    public void ComparisonFormGolden()
    {
        var expected = new[]
        {
            """{"choices":[{"color":"#e5484d","key":"ACTION"},{"key":"DRAMA"}],"defaultValue":["DRAMA"],"ignoreCase":true,"name":"Genre","orderKey":"a0V","type":"MultipleChoice"}""",
            """{"choices":[{"key":"A"},{"key":"a"}],"defaultValue":["a"],"ignoreCase":false,"name":"One","type":"SingleChoice"}""",
            """{"ignoreCase":true,"name":"Studio tags","tags":[{"name":"ISEKAI"},{"color":"#3e63dd","group":"STUDIO","name":"KYOTO"}],"type":"Tags"}""",
            """{"defaultValue":[["ASIA","JAPAN"]],"ignoreCase":true,"name":"Region","nodes":[{"children":[{"key":"JAPAN"},{"key":"KOREA"}],"color":"#30a46c","key":"ASIA"}],"orderKey":"b","settings":{"valueIsSingleton":true},"type":"Multilevel"}""",
            """{"childrenLocal":true,"ignoreCase":false,"name":"Local options","type":"Tags"}""",
            """{"name":"Score","settings":{"precision":1},"type":"Number"}""",
            """{"name":"Progress","settings":{"precision":0,"showProgressBar":true},"type":"Percentage"}""",
            """{"name":"My rating","settings":{"maxValue":10},"type":"Rating"}""",
            """{"name":"Gallery","settings":{"layout":"Carousel"},"type":"Attachment"}""",
            """{"name":"Notes","type":"MultilineText"}""",
        };
        var actual = GoldenContents().Select((c, i) => Form(c, i switch { 0 => "a0V", 3 => "b", _ => null })).ToArray();
        CollectionAssert.AreEqual(expected, actual, string.Join("\n", actual));
        Assert.AreEqual(1, CustomPropertyCodec.CurrentComparisonFormVersion,
            "the vectors above belong to comparison form version 1");
    }

    private static IEnumerable<CustomPropertyContentV1> GoldenContents()
    {
        yield return Choice("Genre", true, C("a", "Action", "#e5484d"), C("d", "Drama"), C("x", "ACTION"))
            with { DefaultValue = [Ref("d", "Drama")] };
        yield return Single("One", false, C("1", "A"), C("2", "a"), C("3", "A")) with { DefaultValue = [Ref("2", "a")] };
        yield return Tags("Studio tags", true, T("b3", "Studio", "Kyoto", "#3e63dd"), T("c4", null, "Isekai"),
            T("c5", "", "isekai"), T("c6", "STUDIO", "KYOTO"));
        yield return Tree("Region", true, N("d5", "Asia", "#30a46c", N("e6", "Japan")), N("d6", "asia", N("e7", "Korea")))
            with
            {
                DefaultValue = [NodeRef("e6", "Asia", "Japan")],
                Settings = new CustomPropertySettingsV1 { ValueIsSingleton = true },
            };
        yield return Tags("Local options", false, T("1", null, "A")) with { ChildrenLocal = true };
        yield return new CustomPropertyContentV1
            { Name = "Score", Type = PropertyType.Number, Settings = new CustomPropertySettingsV1 { Precision = 1 } };
        yield return new CustomPropertyContentV1
        {
            Name = "Progress", Type = PropertyType.Percentage,
            Settings = new CustomPropertySettingsV1 { Precision = 0, ShowProgressBar = true },
        };
        yield return new CustomPropertyContentV1
            { Name = "My rating", Type = PropertyType.Rating, Settings = new CustomPropertySettingsV1 { MaxValue = 10 } };
        yield return new CustomPropertyContentV1
        {
            Name = "Gallery", Type = PropertyType.Attachment,
            Settings = new CustomPropertySettingsV1 { Layout = CustomPropertyAttachmentLayouts.Carousel },
        };
        yield return new CustomPropertyContentV1 { Name = "Notes", Type = PropertyType.MultilineText };
    }
}
