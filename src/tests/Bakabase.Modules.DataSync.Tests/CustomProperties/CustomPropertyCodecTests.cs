using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.Cp;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>
/// v3.1 <c>CustomPropertyCodecTests</c> (content, validation, local content) and §3.3's additions: every one of the
/// 16 types, settings defaults, enum-by-name, what the reader drops and holds, and ReadLocal's round trip.
/// </summary>
[TestClass]
public class CustomPropertyCodecTests
{
    // ---- descriptor ---------------------------------------------------------------------------

    [TestMethod]
    public void TheDescriptorIsCustomPropertySchemaOne()
    {
        var d = Codec.Descriptor;
        Assert.AreEqual(DataSyncKindIds.CustomProperty, d.Kind);
        Assert.AreEqual(1, d.SchemaVersion);
        Assert.AreEqual(0, d.DependsOn.Count);
        Assert.AreEqual(typeof(CustomPropertyContentV1), d.ContentType);
        Assert.IsFalse(d.AutoLinkIdentical);
        Assert.IsTrue(d.HasOrder);
        Assert.IsTrue(d.HasChildren);
        Assert.IsTrue(d.SupportsChildrenLocal);
        Assert.AreEqual("option", d.ChildNoun);
        Assert.AreEqual(1, Codec.ComparisonFormVersion);
    }

    // ---- the 16 types -------------------------------------------------------------------------

    /// <summary>Minimal peer content per type: settings are filled with their defaults, IgnoreCase only where it exists.</summary>
    [TestMethod]
    [DataRow("SingleLineText", """{"name":"P","type":"SingleLineText"}""")]
    [DataRow("MultilineText", """{"name":"P","type":"MultilineText"}""")]
    [DataRow("SingleChoice", """{"ignoreCase":false,"name":"P","type":"SingleChoice"}""")]
    [DataRow("MultipleChoice", """{"ignoreCase":false,"name":"P","type":"MultipleChoice"}""")]
    [DataRow("Number", """{"name":"P","settings":{"precision":0},"type":"Number"}""")]
    [DataRow("Percentage", """{"name":"P","settings":{"precision":0,"showProgressBar":false},"type":"Percentage"}""")]
    [DataRow("Rating", """{"name":"P","settings":{"maxValue":5},"type":"Rating"}""")]
    [DataRow("Boolean", """{"name":"P","type":"Boolean"}""")]
    [DataRow("Link", """{"name":"P","type":"Link"}""")]
    [DataRow("Attachment", """{"name":"P","settings":{"layout":"Tile"},"type":"Attachment"}""")]
    [DataRow("Date", """{"name":"P","type":"Date"}""")]
    [DataRow("DateTime", """{"name":"P","type":"DateTime"}""")]
    [DataRow("Time", """{"name":"P","type":"Time"}""")]
    [DataRow("Formula", """{"name":"P","type":"Formula"}""")]
    [DataRow("Multilevel", """{"ignoreCase":false,"name":"P","settings":{"valueIsSingleton":false},"type":"Multilevel"}""")]
    [DataRow("Tags", """{"ignoreCase":false,"name":"P","type":"Tags"}""")]
    public void EveryTypeReadsWithItsDefaults(string type, string expected)
    {
        var (content, result) = ReadValid(new JsonObject { ["name"] = "P", ["type"] = type });
        Assert.AreEqual(Enum.Parse<PropertyType>(type), content.Type);
        Assert.AreEqual(expected, Canon(content));
        Assert.AreEqual(0, result.Warnings.Count);
        Assert.AreEqual(type, Codec.SubtypeOf(content));
        Assert.AreEqual(expected, Canon(Codec.Write(Codec.ReadLocal(Json(expected)))), "Write(ReadLocal(x)) == x");
    }

    [TestMethod]
    public void AllSixteenTypesAreCovered()
    {
        Assert.AreEqual(16, Enum.GetValues<PropertyType>().Length);
        Assert.AreEqual(4, Enum.GetValues<PropertyType>().Count(CustomPropertyTypes.IsReference));
        foreach (var type in Enum.GetValues<PropertyType>())
        {
            Assert.IsTrue(CustomPropertyTypes.TryParse(type.ToString(), out var parsed));
            Assert.AreEqual(type, parsed);
        }
    }

    /// <summary>The v3.1 §3.3 shapes read and write back byte for byte.</summary>
    [TestMethod]
    [DataRow("""{"choices":[{"color":"#e5484d","label":"Action","uuid":"0c1d2e3f-4a5b-6c7d-8e9f-0a1b2c3d4e5f"},{"label":"Drama","uuid":"6a7b8c9d-0e1f-2a3b-4c5d-6e7f8a9b0c1d"}],"defaultValue":[{"label":"Drama","uuid":"6a7b8c9d-0e1f-2a3b-4c5d-6e7f8a9b0c1d"}],"ignoreCase":true,"name":"Genre","type":"MultipleChoice"}""")]
    [DataRow("""{"ignoreCase":false,"name":"Studio tags","tags":[{"color":"#3e63dd","group":"Studio","name":"Kyoto","uuid":"b3"},{"name":"Isekai","uuid":"c4"},{"group":"","name":"Plain","uuid":"c5"}],"type":"Tags"}""")]
    [DataRow("""{"defaultValue":[{"path":["Asia","Japan"],"uuid":"e6"}],"ignoreCase":true,"name":"Region","nodes":[{"children":[{"label":"Japan","uuid":"e6"}],"color":"#30a46c","label":"Asia","uuid":"d5"}],"settings":{"valueIsSingleton":false},"type":"Multilevel"}""")]
    [DataRow("""{"name":"Score","settings":{"precision":1},"type":"Number"}""")]
    [DataRow("""{"name":"Progress","settings":{"precision":0,"showProgressBar":true},"type":"Percentage"}""")]
    [DataRow("""{"name":"My rating","settings":{"maxValue":10},"type":"Rating"}""")]
    [DataRow("""{"name":"Gallery","settings":{"layout":"Carousel"},"type":"Attachment"}""")]
    [DataRow("""{"name":"Notes","type":"MultilineText"}""")]
    [DataRow("""{"defaultValue":[{"label":"A","uuid":"a"}],"ignoreCase":false,"name":"One","type":"SingleChoice","choices":[{"label":"A","uuid":"a"}]}""")]
    [DataRow("""{"childrenLocal":true,"ignoreCase":true,"name":"Kept here","type":"Tags"}""")]
    public void TheSpecShapesRoundTrip(string json)
    {
        var canonical = Canon(Json(json));
        var (content, result) = ReadValid(json);
        Assert.AreEqual(canonical, Canon(content));
        Assert.AreEqual(0, result.Warnings.Count, string.Join(",", result.Warnings.Select(w => w.Code)));
        Assert.AreEqual(canonical, Canon(Codec.ReadLocal(Json(json))));
        AssertNoNulls(Codec.Write(content));
    }

    [TestMethod]
    public void TagGroupsKeepNullAndEmptyApart()
    {
        var (content, _) = ReadValid("""{"ignoreCase":false,"name":"T","tags":[{"name":"A","uuid":"1"},{"group":"","name":"A","uuid":"2"}],"type":"Tags"}""");
        Assert.IsNull(content.Tags[0].Group);
        Assert.AreEqual("", content.Tags[1].Group);
        StringAssert.Contains(Canon(content), "\"group\":\"\"");
    }

    [TestMethod]
    public void EmptyColoursAndEmptyListsAreOmitted()
    {
        var content = Choice("P", false, C("1", "A", "")) with { DefaultValue = [] };
        Assert.AreEqual("""{"choices":[{"label":"A","uuid":"1"}],"ignoreCase":false,"name":"P","type":"MultipleChoice"}""",
            Canon(content));
        Assert.AreEqual("""{"ignoreCase":true,"name":"P","nodes":[{"label":"A","uuid":"1"}],"settings":{"valueIsSingleton":false},"type":"Multilevel"}""",
            Canon(Tree("P", true, N("1", "A"))));
    }

    // ---- enums by name ------------------------------------------------------------------------

    [TestMethod]
    [DataRow("""{"name":"P","type":"Hologram"}""")]
    [DataRow("""{"name":"P","type":"3"}""")]
    [DataRow("""{"name":"P","type":"singleChoice"}""")]
    [DataRow("""{"name":"P","settings":{"layout":"Grid"},"type":"Attachment"}""")]
    public void AnUnknownEnumNameHoldsTheEntity(string json)
    {
        var result = ReadHeld(Json(json), DataSyncHeldReason.UnknownEnumValue);
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    [DataRow("""{"name":"P","type":3}""", "type")]
    [DataRow("""{"name":"P"}""", "type")]
    [DataRow("""{"type":"Boolean"}""", "name")]
    [DataRow("""{"name":"","type":"Boolean"}""", "name")]
    [DataRow("""{"name":"a\u0000b","type":"Boolean"}""", "name")]
    [DataRow("""{"name":"P","settings":{"precision":29},"type":"Number"}""", "settings.precision")]
    [DataRow("""{"name":"P","settings":{"precision":-1},"type":"Number"}""", "settings.precision")]
    [DataRow("""{"name":"P","settings":{"precision":1.5},"type":"Number"}""", "settings.precision")]
    [DataRow("""{"name":"P","settings":{"precision":"1"},"type":"Number"}""", "settings.precision")]
    [DataRow("""{"name":"P","settings":{"maxValue":0},"type":"Rating"}""", "settings.maxValue")]
    [DataRow("""{"name":"P","settings":{"maxValue":101},"type":"Rating"}""", "settings.maxValue")]
    [DataRow("""{"name":"P","settings":{"showProgressBar":1},"type":"Percentage"}""", "settings.showProgressBar")]
    [DataRow("""{"name":"P","settings":{"layout":1},"type":"Attachment"}""", "settings.layout")]
    [DataRow("""{"name":"P","settings":[],"type":"Number"}""", "shape:settings")]
    [DataRow("""{"ignoreCase":"yes","name":"P","type":"Tags"}""", "shape:ignoreCase")]
    [DataRow("""{"childrenLocal":1,"name":"P","type":"Tags"}""", "shape:childrenLocal")]
    [DataRow("""{"name":"P","tags":{},"type":"Tags"}""", "shape:tags")]
    [DataRow("""{"defaultValue":{},"name":"P","type":"MultipleChoice"}""", "shape:defaultValue")]
    [DataRow("""{"name":"P","nodes":[{"children":{},"label":"A","uuid":"1"}],"type":"Multilevel"}""", "shape:children")]
    public void AnInvalidEntityIsHeld(string json, string error)
    {
        var result = ReadHeld(Json(json), DataSyncHeldReason.Invalid);
        CollectionAssert.Contains(result.Errors.ToArray(), error);
    }

    [TestMethod]
    public void ANameOfTheMaximumLengthIsAcceptedAndOneMoreIsHeld()
    {
        ReadValid(new JsonObject { ["name"] = new string('n', 256), ["type"] = "Boolean" });
        ReadHeld(new JsonObject { ["name"] = new string('n', 257), ["type"] = "Boolean" }, DataSyncHeldReason.Invalid);
        ReadHeld(new JsonObject { ["name"] = "a\ud800", ["type"] = "Boolean" }, DataSyncHeldReason.Invalid);
    }

    [TestMethod]
    public void DuplicateMembersInParsedContentHoldTheEntity()
    {
        var result = Codec.Read(Json("""{"name":"A","name":"B","type":"Boolean"}"""), DataSyncLimits.Default);
        Assert.AreEqual(DataSyncHeldReason.Invalid, result.Held);
    }

    // ---- per-option drops (v3.1 §6.3) ---------------------------------------------------------

    [TestMethod]
    public void OptionsTheReaderRejectsAreDroppedWithTheirReason()
    {
        var json = new JsonObject
        {
            ["name"] = "P", ["type"] = "MultipleChoice", ["ignoreCase"] = false,
            ["choices"] = new JsonArray(
                Option("ok", "Fine"),
                Option("nul", "a\u0000b"),
                Option("lone", "a\ud800"),
                Option(new string('u', 129), "Long uuid"),
                Option("*", "Star"),
                Option("ok", "Duplicate uuid"),
                Option("empty", ""),
                Option("colour", "Colour", new string('c', 65)),
                Option("ctl", "Tabs\tand\nnewlines"),
                Option("ctrl\u0001", "Control in uuid"),
                new JsonObject { ["label"] = "No uuid" },
                JsonValue.Create("not an object")),
        };
        var (content, result) = ReadValid(json);
        CollectionAssert.AreEqual(new[] { "ok", "ctl" }, content.Choices.Select(c => c.Uuid).ToArray());
        Assert.AreEqual("Tabs\tand\nnewlines", content.Choices[1].Label, "\\t and \\n are accepted");
        var reasons = Warnings(result.Warnings, DataSyncWarningCode.OptionDropped)
            .Select(w => (w.Args!.GetValueOrDefault("uuid"), Arg(w, "reason"))).ToArray();
        CollectionAssert.AreEqual(new (string?, string)[]
        {
            ("nul", "label"), ("lone", "label"), (null, "uuid"), ("*", "uuid"), ("ok", "duplicateUuid"),
            ("empty", "label"), ("colour", "color"), (null, "uuid"), (null, "uuid"), (null, "uuid"),
        }, reasons);

        static JsonObject Option(string uuid, string label, string? color = null)
        {
            var option = new JsonObject { ["uuid"] = uuid, ["label"] = label };
            if (color is not null) option["color"] = color;
            return option;
        }
    }

    [TestMethod]
    public void MultilevelNodesBelowLevelSixteenAreDroppedWithTheirSubtree()
    {
        // A chain of 18 levels; level 17 and its child go, with one warning for level 17.
        JsonObject? chain = null;
        for (var level = 18; level >= 1; level--)
        {
            var node = new JsonObject { ["uuid"] = $"n{level}", ["label"] = $"L{level}" };
            if (chain is not null) node["children"] = new JsonArray(chain);
            chain = node;
        }

        var (content, result) = ReadValid(new JsonObject
        {
            ["name"] = "Deep", ["type"] = "Multilevel", ["nodes"] = new JsonArray(chain),
        });
        Assert.AreEqual(16, Codec.ChildCountOf(content));
        var drop = Warnings(result.Warnings, DataSyncWarningCode.OptionDropped).Single();
        Assert.AreEqual("n17", Arg(drop, "uuid"));
        Assert.AreEqual("depth", Arg(drop, "reason"));
        Assert.AreEqual("1", Arg(drop, "descendants"));
    }

    [TestMethod]
    public void ADroppedNodeTakesItsSubtreeAndDuplicatesAcrossTheTreeAreDropped()
    {
        var (content, result) = ReadValid("""
            {"name":"R","type":"Multilevel","nodes":[
              {"uuid":"a","label":"Asia","children":[{"uuid":"j","label":"Japan"},{"uuid":"a","label":"Dup","children":[{"uuid":"x","label":"X"}]}]},
              {"uuid":"bad\u0000","label":"Bad","children":[{"uuid":"y","label":"Y"},{"uuid":"z","label":"Z"}]}]}
            """);
        Assert.AreEqual(2, Codec.ChildCountOf(content));
        var drops = Warnings(result.Warnings, DataSyncWarningCode.OptionDropped);
        Assert.AreEqual(2, drops.Count);
        Assert.AreEqual("duplicateUuid", Arg(drops[0], "reason"));
        Assert.AreEqual("1", Arg(drops[0], "descendants"));
        Assert.AreEqual("uuid", Arg(drops[1], "reason"));
        Assert.AreEqual("2", Arg(drops[1], "descendants"));
    }

    [TestMethod]
    public void TheOptionLimitHoldsAPropertyAboveItAndCountsEveryLevel()
    {
        Assert.IsNull(Codec.Read(ManyTags(20_000), DataSyncLimits.Default).Held);
        var held = ReadHeld(ManyTags(20_001), DataSyncHeldReason.Invalid);
        CollectionAssert.Contains(held.Errors.ToArray(), CustomPropertyCodec.HeldDetails.TooManyChildren);

        var limits = DataSyncLimits.Default with { MaxOptionsPerProperty = 3 };
        ReadValid(Json("""{"name":"R","type":"Multilevel","nodes":[{"uuid":"a","label":"A","children":[{"uuid":"b","label":"B"}]},{"uuid":"c","label":"C"}]}"""), limits);
        ReadHeld(Json("""{"name":"R","type":"Multilevel","nodes":[{"uuid":"a","label":"A","children":[{"uuid":"b","label":"B"},{"uuid":"d","label":"D"}]},{"uuid":"c","label":"C"}]}"""),
            DataSyncHeldReason.Invalid, limits);
    }

    internal static JsonObject ManyTags(int count)
    {
        var tags = new JsonArray();
        for (var i = 0; i < count; i++) tags.Add(new JsonObject { ["uuid"] = $"t{i}", ["name"] = $"Tag {i}" });
        return new JsonObject { ["name"] = "Many", ["type"] = "Tags", ["tags"] = tags };
    }

    // ---- members on the wrong type, unknown members -------------------------------------------

    [TestMethod]
    public void MembersATypeDoesNotUseAreIgnoredWithAWarning()
    {
        var (content, result) = ReadValid("""
            {"name":"P","type":"Number","ignoreCase":true,"childrenLocal":true,"settings":{"precision":2,"maxValue":7},
             "choices":[{"uuid":"1","label":"A"}],"defaultValue":[{"uuid":"1","label":"A"}]}
            """);
        Assert.AreEqual("""{"name":"P","settings":{"precision":2},"type":"Number"}""", Canon(content));
        var ignored = Warnings(result.Warnings, DataSyncWarningCode.SettingsIgnoredForType).Select(w => Arg(w, "setting"))
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(new[] { "childrenLocal", "choices", "defaultValue", "ignoreCase", "settings.maxValue" }, ignored);
    }

    [TestMethod]
    public void TagsHaveNoDefaultValue()
    {
        var (content, result) = ReadValid("""{"name":"T","type":"Tags","tags":[{"uuid":"1","name":"A"}],"defaultValue":[{"uuid":"1","name":"A"}]}""");
        Assert.AreEqual(0, content.DefaultValue.Count);
        Assert.AreEqual("defaultValue", Arg(Warnings(result.Warnings, DataSyncWarningCode.SettingsIgnoredForType).Single(), "setting"));
    }

    [TestMethod]
    public void ChildrenLocalContentCarriesNoChildrenAndNoDefault()
    {
        var (content, result) = ReadValid("""{"childrenLocal":true,"name":"T","type":"MultipleChoice","choices":[{"uuid":"1","label":"A"}],"defaultValue":[{"uuid":"1","label":"A"}]}""");
        Assert.IsTrue(content.ChildrenLocal);
        Assert.AreEqual(0, content.Choices.Count);
        Assert.AreEqual(0, content.DefaultValue.Count);
        Assert.AreEqual(2, Warnings(result.Warnings, DataSyncWarningCode.SettingsIgnoredForType).Count);
    }

    [TestMethod]
    public void UnknownTopLevelMembersAreReturnedVerbatimAndCounted()
    {
        var (content, result) = ReadValid("""
            {"name":"P","type":"Tags","x-new":{"b":[1,2],"a":"z"},"another":true,
             "tags":[{"uuid":"1","name":"A","nested":1}],"settings":{"futureSetting":3}}
            """);
        Assert.AreEqual(1, content.Tags.Count);
        Assert.IsNotNull(result.Unknown);
        Assert.AreEqual("""{"another":true,"x-new":{"a":"z","b":[1,2]}}""", Canon(result.Unknown));
        Assert.AreEqual("4", Arg(Warnings(result.Warnings, DataSyncWarningCode.UnknownFieldsIgnored).Single(), "count"));
    }

    [TestMethod]
    public void AContentWithoutUnknownMembersHasNoUnknown()
    {
        var (_, result) = ReadValid("""{"name":"P","type":"Boolean"}""");
        Assert.IsNull(result.Unknown);
    }

    // ---- defaultValue -------------------------------------------------------------------------

    [TestMethod]
    public void DefaultRefsResolveByUuidThenByLabelAndOthersAreDropped()
    {
        var (content, result) = ReadValid("""
            {"name":"G","type":"MultipleChoice","ignoreCase":true,
             "choices":[{"uuid":"a","label":"Action"},{"uuid":"d","label":"Drama"}],
             "defaultValue":[{"uuid":"a","label":"Stale label"},{"uuid":"other","label":"DRAMA"},{"uuid":"gone","label":"Nothing"},
                             {"uuid":"a2","label":"action"},{"uuid":"p","path":["X"]},{"bad":1}]}
            """);
        CollectionAssert.AreEqual(new[] { "a", "other" }, content.DefaultValue.Select(r => r.Uuid).ToArray());
        var dropped = Warnings(result.Warnings, DataSyncWarningCode.DefaultValueRefDropped);
        CollectionAssert.AreEqual(new[] { "gone", "a2", "p", null },
            dropped.Select(w => w.Args?.GetValueOrDefault("uuid")).ToArray());
    }

    [TestMethod]
    public void ASingleChoiceKeepsOneDefaultAndMultilevelRefsUsePaths()
    {
        var (single, singleResult) = ReadValid("""
            {"name":"S","type":"SingleChoice","choices":[{"uuid":"a","label":"A"},{"uuid":"b","label":"B"}],
             "defaultValue":[{"uuid":"a","label":"A"},{"uuid":"b","label":"B"}]}
            """);
        Assert.AreEqual("a", single.DefaultValue.Single().Uuid);
        Assert.AreEqual(1, Warnings(singleResult.Warnings, DataSyncWarningCode.DefaultValueRefDropped).Count);

        var (tree, _) = ReadValid("""
            {"name":"R","type":"Multilevel","ignoreCase":true,
             "nodes":[{"uuid":"asia","label":"Asia","children":[{"uuid":"jp","label":"Japan"}]}],
             "defaultValue":[{"uuid":"elsewhere","path":["ASIA","japan"]},{"uuid":"jp","label":"Japan"}]}
            """);
        Assert.AreEqual("elsewhere", tree.DefaultValue.Single().Uuid, "resolved by key path; a label form is not a node ref");
    }

    // ---- ReadLocal: local content is never validated (v3.1 B3, §3.3) ----------------------------

    [TestMethod]
    public void ReadLocalKeepsWhatTheReaderWouldRejectAndRoundTrips()
    {
        var nodes = new JsonArray();
        JsonObject? deep = null;
        for (var level = 20; level >= 1; level--)
        {
            var node = new JsonObject { ["uuid"] = $"d{level}", ["label"] = $"L{level}" };
            if (deep is not null) node["children"] = new JsonArray(deep);
            deep = node;
        }

        nodes.Add(deep);
        var local = new JsonObject
        {
            ["ignoreCase"] = true, ["name"] = "Local", ["type"] = "Multilevel", ["nodes"] = nodes,
            ["settings"] = new JsonObject { ["valueIsSingleton"] = false },
        };
        var json = Canon(local);
        var content = Codec.ReadLocal(Json(json));
        Assert.AreEqual(20, Codec.ChildCountOf(content));
        Assert.AreEqual(json, Canon(content));

        var choices = new JsonArray(
            new JsonObject { ["uuid"] = new string('u', 200), ["label"] = "Long" },
            new JsonObject { ["uuid"] = "same", ["label"] = "One" },
            new JsonObject { ["uuid"] = "same", ["label"] = "Two" },
            new JsonObject { ["uuid"] = "nul", ["label"] = "a\u0000b" },
            new JsonObject { ["uuid"] = "lone", ["label"] = "a\ud800" },
            new JsonObject { ["uuid"] = "*", ["label"] = "Star" },
            new JsonObject { ["uuid"] = "c", ["label"] = "Colour", ["color"] = new string('c', 65) });
        var choiceJson = new JsonObject { ["ignoreCase"] = false, ["name"] = "C", ["type"] = "MultipleChoice", ["choices"] = choices };
        var parsed = Codec.ReadLocal(choiceJson);
        Assert.AreEqual(7, parsed.Choices.Count);
        Assert.AreEqual(Canon(choiceJson), Canon(parsed));
    }

    [TestMethod]
    public void ReadLocalKeepsAHugeProperty()
    {
        var json = ManyTags(25_000);
        json["ignoreCase"] = false;
        var content = Codec.ReadLocal(json);
        Assert.AreEqual(25_000, content.Tags.Count);
        Assert.AreEqual(Canon(json), Canon(content));
    }

    [TestMethod]
    public void NullLocalValuesRoundTrip()
    {
        // The mapper writes a null uuid as "" and a null label or name as "" (§3.3).
        var json = Json("""
            {"choices":[{"label":"","uuid":""},{"label":"A","uuid":"a"}],"ignoreCase":false,"name":"","type":"MultipleChoice"}
            """);
        var content = Codec.ReadLocal(json);
        Assert.IsNull(content.Choices[0].Uuid);
        Assert.AreEqual("", content.Choices[0].Label);
        Assert.AreEqual(Canon(json), Canon(content));

        var tags = Json("""{"ignoreCase":true,"name":"T","tags":[{"group":"","name":"","uuid":""}],"type":"Tags"}""");
        Assert.AreEqual(Canon(tags), Canon(Codec.ReadLocal(tags)));
        var tree = Json("""{"ignoreCase":true,"name":"R","nodes":[{"children":[{"label":"x","uuid":""}],"label":"","uuid":"r"}],"type":"Multilevel"}""");
        Assert.AreEqual(Canon(tree), Canon(Codec.ReadLocal(tree)));
    }

    /// <summary>An unreadable local property is content {name, type} only (§3.3); it still round-trips.</summary>
    [TestMethod]
    public void UnreadableLocalContentRoundTrips()
    {
        var json = Json("""{"name":"Broken","type":"Tags"}""");
        var content = Codec.ReadLocal(json);
        Assert.IsNull(content.IgnoreCase);
        Assert.AreEqual(Canon(json), Canon(content));
    }

    [TestMethod]
    [DataRow("""{"name":"P","type":"Boolean","extra":1}""")]
    [DataRow("""{"name":"P","type":"Hologram"}""")]
    [DataRow("""{"name":"P","type":"Tags","tags":[{"uuid":"1","name":"A","nested":1}]}""")]
    [DataRow("""{"name":"P","type":"Tags","tags":[{"uuid":1,"name":"A"}]}""")]
    [DataRow("""{"name":1,"type":"Boolean"}""")]
    public void ReadLocalRefusesAShapeWriteNeverProduces(string json) =>
        Assert.ThrowsException<FormatException>(() => Codec.ReadLocal(Json(json)));

    [TestMethod]
    public void ReadLocalAndReadGoThroughTheUntypedInterface()
    {
        var json = Json("""{"ignoreCase":false,"name":"P","type":"SingleChoice"}""");
        var local = Untyped.ReadLocal(json);
        Assert.IsInstanceOfType<CustomPropertyContentV1>(local);
        Assert.AreEqual("P", Untyped.NameOf(local));
        Assert.AreEqual("SingleChoice", Untyped.SubtypeOf(local));
        Assert.AreEqual(Canon(json), Canon(Untyped.Write(local)));
    }

    // ---- children -----------------------------------------------------------------------------

    [TestMethod]
    public void ChildrenOfListsAddressableOptionsWithDisplayValues()
    {
        var tree = Tree("R", true, N("asia", "Asia", "#111", N("jp", "Japan"), N(null, "No id", N("deep", "Deep"))));
        var children = Codec.ChildrenOf(tree);
        CollectionAssert.AreEqual(new[] { "asia", "jp", "deep" }, children.Select(c => c.Id).ToArray());
        Assert.IsNull(children[0].ParentId);
        Assert.AreEqual("asia", children[1].ParentId);
        Assert.AreEqual("asia", children[2].ParentId, "a node without an id hands its parent's id down");
        CollectionAssert.AreEqual(new[] { "Asia", "No id", "Deep" }, children[2].Display.Path!.ToArray());
        Assert.AreEqual("#111", children[0].Display.Color);
        Assert.AreEqual(4, Codec.ChildCountOf(tree));

        var tags = Codec.ChildrenOf(Tags("T", false, T("1", "Studio", "Kyoto", "#abc"), T(null, null, "No id")));
        Assert.AreEqual(1, tags.Count);
        Assert.AreEqual(new DataSyncDisplayValue("Kyoto", "#abc", "Studio"), tags[0].Display);
    }

    // ---- natural matches ----------------------------------------------------------------------

    [TestMethod]
    public void NaturalMatchLevels()
    {
        var genre = Choice("Genre", true, C("1", "Action", "#f00"), C("2", "Drama"));
        Assert.AreEqual(DataSyncNaturalMatch.None, Codec.MatchNatural(genre with { Name = "Mood" }, genre));
        Assert.AreEqual(DataSyncNaturalMatch.Clash, Codec.MatchNatural(genre with { Type = PropertyType.SingleChoice }, genre));
        Assert.AreEqual(DataSyncNaturalMatch.Similar, Codec.MatchNatural(genre with { Name = " genre " }, genre));
        Assert.AreEqual(DataSyncNaturalMatch.Exact,
            Codec.MatchNatural(genre with { Choices = [C("x", "Action")] }, genre));
        // Other ids, other order, a duplicate and another casing under IgnoreCase: the same comparison form.
        Assert.AreEqual(DataSyncNaturalMatch.Identical,
            Codec.MatchNatural(Choice("Genre", true, C("9", "drama"), C("8", "Action", "#f00"), C("7", "ACTION")), genre));
    }
}
