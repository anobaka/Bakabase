using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.Cp;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>
/// §3.5: what this device publishes — overlays out, "Sync the definition only", withheld children, the reader's own
/// validation before hashing, a held entity, unknown members merged back — and §3.3's null values.
/// </summary>
[TestClass]
public class PublishTests
{
    private static DataSyncPublishable Publish(CustomPropertyContentV1 local, DataSyncOverlay? overlay = null,
        bool childrenLocal = false) => Untyped.Publish(local, overlay ?? DataSyncOverlay.None, childrenLocal);

    private static CustomPropertyContentV1 Published(DataSyncPublishable publishable)
    {
        Assert.IsNull(publishable.Held, publishable.HeldDetail);
        return (CustomPropertyContentV1)publishable.Content!;
    }

    [TestMethod]
    public void CleanContentIsPublishedAsItIs()
    {
        var local = Choice("G", true, C("a", "Action", "#f00"), C("d", "Drama")) with { DefaultValue = [Ref("d", "Drama")] };
        var result = Publish(local);
        Assert.AreEqual(Canon(local), Canon(Published(result)));
        Assert.AreEqual(0, result.ChildrenWithheld);
        Assert.AreEqual(0, result.Warnings.Length);
    }

    [TestMethod]
    public void OverlayChildrenAreRemovedWithTheirSubtreesAndDefaultRefsToThem()
    {
        var tree = Tree("R", false,
            N("asia", "Asia", N("jp", "Japan", N("kyoto", "Kyoto")), N("kr", "Korea")),
            N("eu", "Europe", N("fr", "France")))
            with { DefaultValue = [NodeRef("kyoto", "Asia", "Japan", "Kyoto"), NodeRef("fr", "Europe", "France")] };
        var overlay = new DataSyncOverlay(["jp"], [new DataSyncHeldChild("eu", 3), new DataSyncHeldChild("eu", 4)]);
        var result = Publish(tree, overlay);
        var published = Published(result);
        Assert.AreEqual(Canon(Tree("R", false, N("asia", "Asia", N("kr", "Korea")))), Canon(published));
        Assert.AreEqual(4, result.ChildrenWithheld, "Japan with Kyoto, Europe with France");
        Assert.AreEqual(0, result.Warnings.Length, "overlays are local rules, never warnings");

        var choices = Choice("G", false, C("1", "A"), C("2", "B"), C("3", "C")) with { DefaultValue = [Ref("2", "B"), Ref("3", "C")] };
        var fromChoices = Published(Publish(choices, new DataSyncOverlay(["2"], [])));
        CollectionAssert.AreEqual(new[] { "1", "3" }, fromChoices.Choices.Select(c => c.Uuid).ToArray());
        CollectionAssert.AreEqual(new[] { "3" }, fromChoices.DefaultValue.Select(r => r.Uuid).ToArray());
    }

    [TestMethod]
    public void ARefToAWithheldChildIsNotResolvedByLabelToAnother()
    {
        // "Action" is kept here only; the default pointed at it, so the default goes too, even though "action" stays.
        var content = Choice("G", true, C("1", "Action"), C("2", "action")) with { DefaultValue = [Ref("1", "Action")] };
        var published = Published(Publish(content, new DataSyncOverlay(["1"], [])));
        Assert.AreEqual(0, published.DefaultValue.Count);
        CollectionAssert.AreEqual(new[] { "2" }, published.Choices.Select(c => c.Uuid).ToArray());
    }

    [TestMethod]
    public void ChildrenLocalPublishesNoChildrenAndNoDefault()
    {
        var tags = Tags("T", true, T("1", null, "A"), T("2", "g", "B"));
        var result = Publish(tags, new DataSyncOverlay(["1"], []), childrenLocal: true);
        Assert.AreEqual("""{"childrenLocal":true,"ignoreCase":true,"name":"T","type":"Tags"}""", Canon(Published(result)));
        Assert.AreEqual(0, result.ChildrenWithheld, "nothing is withheld: no option is published by design");

        var choice = Choice("G", false, C("1", "A")) with { DefaultValue = [Ref("1", "A")] };
        Assert.AreEqual("""{"childrenLocal":true,"ignoreCase":false,"name":"G","type":"MultipleChoice"}""",
            Canon(Published(Publish(choice, childrenLocal: true))));
    }

    [TestMethod]
    public void ChildrenLocalIsIgnoredOnTypesWithoutOptions()
    {
        var number = new CustomPropertyContentV1
        {
            Name = "N", Type = PropertyType.Number, Settings = new CustomPropertySettingsV1 { Precision = 1 },
        };
        var result = Publish(number, childrenLocal: true);
        Assert.AreEqual("""{"name":"N","settings":{"precision":1},"type":"Number"}""", Canon(Published(result)));
        Assert.IsFalse(Published(result).ChildrenLocal);
    }

    [TestMethod]
    public void OptionsWithoutAUuidOrLabelAreWithheldAndCounted()
    {
        // As ReadLocal returns them: "uuid":"" is a null uuid, a null label was written as "".
        var local = Codec.ReadLocal(Json("""
            {"choices":[{"label":"No id","uuid":""},{"label":"","uuid":"blank"},{"label":"Fine","uuid":"ok"}],
             "defaultValue":[{"label":"No id","uuid":""},{"label":"","uuid":"blank"},{"label":"Fine","uuid":"ok"}],
             "ignoreCase":false,"name":"G","type":"MultipleChoice"}
            """));
        var result = Publish(local);
        var published = Published(result);
        CollectionAssert.AreEqual(new[] { "ok" }, published.Choices.Select(c => c.Uuid).ToArray());
        CollectionAssert.AreEqual(new[] { "ok" }, published.DefaultValue.Select(r => r.Uuid).ToArray());
        Assert.AreEqual(2, result.ChildrenWithheld);
        var drops = Warnings(result.Warnings, DataSyncWarningCode.OptionDropped);
        CollectionAssert.AreEqual(new[] { "uuid", "label" }, drops.Select(w => Arg(w, "reason")).ToArray());
        Assert.AreEqual("blank", Arg(drops[1], "uuid"));

        var tree = Codec.ReadLocal(Json("""
            {"ignoreCase":false,"name":"R","nodes":[{"children":[{"label":"Child","uuid":"c"}],"label":"Root","uuid":""},{"label":"Kept","uuid":"k"}],"type":"Multilevel"}
            """));
        var fromTree = Publish(tree);
        Assert.AreEqual(2, fromTree.ChildrenWithheld, "a node without an id takes its subtree");
        Assert.AreEqual("1", Arg(Warnings(fromTree.Warnings, DataSyncWarningCode.OptionDropped).Single(), "descendants"));
        CollectionAssert.AreEqual(new[] { "k" }, Published(fromTree).Nodes.Select(n => n.Uuid).ToArray());
    }

    [TestMethod]
    public void ChildrenTheReaderWouldDropAreLeftOutBeforeHashing()
    {
        var local = Choice("G", false,
            C("ok", "Fine"), C(new string('u', 200), "Long uuid"), C("dup", "One"), C("dup", "Two"),
            C("nul", "a\u0000b"), C("lone", "a\ud800"), C("*", "Star"), C("c", "Colour", new string('c', 65)));
        var result = Publish(local);
        var published = Published(result);
        CollectionAssert.AreEqual(new[] { "ok", "dup" }, published.Choices.Select(c => c.Uuid).ToArray());
        Assert.AreEqual("One", published.Choices[1].Label, "the later duplicate is the one left out");
        Assert.AreEqual(6, result.ChildrenWithheld);

        // SharedHash is the hash of the validated content's form: what a receiver computes from the record.
        var record = Untyped.WritePublished(published, null);
        var received = ReadValid(record).Content;
        Assert.AreEqual(Form(received), Form(published));
        Assert.AreEqual(Untyped.SharedHash(received, null, false, null), Untyped.SharedHash(published, null, false, null));
        Assert.AreEqual(ContentHash.Of(Codec.ComparisonForm(published, null, false)),
            Untyped.SharedHash(published, null, false, null));
    }

    [TestMethod]
    public void NodesBelowTheDepthLimitAreLeftOutWithTheirSubtree()
    {
        var chain = N("n17", "L17", N("n18", "L18"));
        for (var level = 16; level >= 1; level--) chain = N($"n{level}", $"L{level}", chain);
        var result = Publish(Tree("Deep", false, chain));
        Assert.AreEqual(16, Codec.ChildCountOf(Published(result)));
        Assert.AreEqual(2, result.ChildrenWithheld);
    }

    [TestMethod]
    public void APropertyOverTheOptionLimitIsHeldAtTheSource()
    {
        var tags = Enumerable.Range(0, 20_001).Select(i => T($"t{i}", null, $"Tag {i}")).ToArray();
        var local = Tags("Many", false, tags);
        var result = Publish(local);
        Assert.IsNull(result.Content);
        Assert.AreEqual(DataSyncHeldReason.Invalid, result.Held);
        Assert.AreEqual(CustomPropertyCodec.HeldDetails.TooManyChildren, result.HeldDetail);

        // Keeping one option here only brings it under the limit: the limit is on what travels.
        var underLimit = Publish(local, new DataSyncOverlay(["t0"], []));
        Assert.IsNull(underLimit.Held);
        Assert.AreEqual(20_000, ((CustomPropertyContentV1)underLimit.Content!).Tags.Count);
        Assert.AreEqual(1, underLimit.ChildrenWithheld);

        // "Sync the definition only" publishes the definition of a property of any size.
        Assert.IsNull(Publish(local, childrenLocal: true).Held);
    }

    [TestMethod]
    public void ALowerLimitIsTheCodecsOwn()
    {
        var codec = new CustomPropertyCodec(DataSyncLimits.Default with { MaxOptionsPerProperty = 2 });
        var result = ((IDataSyncKindCodec)codec).Publish(Choice("G", false, C("1", "A"), C("2", "B"), C("3", "C")),
            DataSyncOverlay.None, false);
        Assert.AreEqual(DataSyncHeldReason.Invalid, result.Held);
    }

    [TestMethod]
    public void UnknownMembersAreMergedBackWithoutOverridingKnownOnes()
    {
        var local = Choice("G", false, C("1", "A"));
        var published = Published(Publish(local));
        var unknown = new JsonObject
        {
            ["x-new"] = new JsonObject { ["deep"] = new JsonArray(1, "two") },
            ["name"] = "not this",
            ["defaultValue"] = "nor this",
            ["childrenLocal"] = true,
        };
        var record = Untyped.WritePublished(published, unknown);
        Assert.AreEqual(
            """{"choices":[{"label":"A","uuid":"1"}],"ignoreCase":false,"name":"G","type":"MultipleChoice","x-new":{"deep":[1,"two"]}}""",
            Canon(record));

        // A receiver reads the member back as unknown and republishes it the same way.
        var read = ReadValid(record);
        Assert.AreEqual("""{"x-new":{"deep":[1,"two"]}}""", Canon(read.Result.Unknown));
        Assert.AreEqual(Canon(record), Canon(Untyped.WritePublished(read.Content, read.Result.Unknown)));
        Assert.AreEqual(Untyped.SharedHash(published, "a", false, unknown), Untyped.SharedHash(read.Content, "a", false, read.Result.Unknown));
    }

    [TestMethod]
    public void PublishIsPureAndDeterministic()
    {
        var local = Tree("R", true, N("1", "Asia", N("2", "Japan")), N(null, "No id")) with
        {
            DefaultValue = [NodeRef("2", "Asia", "Japan")],
        };
        var before = Canon(local);
        var first = Publish(local, new DataSyncOverlay([], [new DataSyncHeldChild("x", 1)]));
        var second = Publish(local, new DataSyncOverlay([], [new DataSyncHeldChild("x", 1)]));
        Assert.AreEqual(before, Canon(local));
        Assert.AreEqual(Canon(Codec.Write((CustomPropertyContentV1)first.Content!)), Canon(Codec.Write((CustomPropertyContentV1)second.Content!)));
        Assert.AreEqual(first.ChildrenWithheld, second.ChildrenWithheld);
    }

    [TestMethod]
    public void UnreadableLocalContentPublishesItsDefinitionWithDefaults()
    {
        // The adapter never Puts it, and the feed serves it as HeldAtSource = LocalUnreadable (§3.3); Publish itself
        // still yields what a reader would accept, so nothing here depends on the unreadable options.
        var local = Codec.ReadLocal(Json("""{"name":"Broken","type":"Tags"}"""));
        Assert.AreEqual("""{"ignoreCase":false,"name":"Broken","type":"Tags"}""", Canon(Published(Publish(local))));
    }
}
