using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Refs;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Components.DataSync;
using Bakabase.Modules.Property.Components.Properties.Attachment;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests.DataSync.CustomProperties;

/// <summary>
/// <see cref="CustomPropertyContentMapper"/> (v3.1 §3.3, §3.3 here): stored options ↔ content for all 16 types, null
/// options as defaults, unreadable options, null ids and labels, and the service's own serializer.
/// </summary>
[TestClass]
public class CustomPropertyContentMapperTests
{
    private static readonly CustomPropertyCodec Codec = new();

    private static string Canon(CustomPropertyContentV1 content) => CanonicalJson.Serialize(Codec.Write(content));

    [TestMethod]
    public void EveryDefinedTypeIsSupported()
    {
        NewtonsoftDefaults.UnderEach(() =>
        {
            foreach (var type in Enum.GetValues<PropertyType>())
                Assert.IsTrue(CustomPropertyContentMapper.Supports(type), type.ToString());
            Assert.IsFalse(CustomPropertyContentMapper.Supports(0));
            Assert.IsFalse(CustomPropertyContentMapper.Supports((PropertyType) 99));
        });
    }

    [TestMethod]
    [DataRow(PropertyType.SingleLineText, """{"name":"P","type":"SingleLineText"}""")]
    [DataRow(PropertyType.MultilineText, """{"name":"P","type":"MultilineText"}""")]
    [DataRow(PropertyType.SingleChoice, """{"ignoreCase":false,"name":"P","type":"SingleChoice"}""")]
    [DataRow(PropertyType.MultipleChoice, """{"ignoreCase":false,"name":"P","type":"MultipleChoice"}""")]
    [DataRow(PropertyType.Number, """{"name":"P","settings":{"precision":0},"type":"Number"}""")]
    [DataRow(PropertyType.Percentage, """{"name":"P","settings":{"precision":0,"showProgressBar":false},"type":"Percentage"}""")]
    [DataRow(PropertyType.Rating, """{"name":"P","settings":{"maxValue":5},"type":"Rating"}""")]
    [DataRow(PropertyType.Boolean, """{"name":"P","type":"Boolean"}""")]
    [DataRow(PropertyType.Link, """{"name":"P","type":"Link"}""")]
    [DataRow(PropertyType.Attachment, """{"name":"P","settings":{"layout":"Tile"},"type":"Attachment"}""")]
    [DataRow(PropertyType.Date, """{"name":"P","type":"Date"}""")]
    [DataRow(PropertyType.DateTime, """{"name":"P","type":"DateTime"}""")]
    [DataRow(PropertyType.Time, """{"name":"P","type":"Time"}""")]
    [DataRow(PropertyType.Formula, """{"name":"P","type":"Formula"}""")]
    [DataRow(PropertyType.Multilevel, """{"ignoreCase":false,"name":"P","settings":{"valueIsSingleton":false},"type":"Multilevel"}""")]
    [DataRow(PropertyType.Tags, """{"ignoreCase":false,"name":"P","type":"Tags"}""")]
    public void NullOptionsReadAsTheTypesDefaults(PropertyType type, string expected)
    {
        NewtonsoftDefaults.UnderEach(() =>
        {
            foreach (var options in new[] { null, "", "null" })
            {
                var stored = CustomPropertyContentMapper.ReadRow("P", type, options);
                Assert.IsFalse(stored.Unreadable);
                Assert.AreEqual(expected, Canon(stored.Content), $"options {options ?? "<null>"}");
            }

            // The defaults are already in the validated form a reader produces.
            var read = Codec.Read(Codec.Write(CustomPropertyContentMapper.ReadRow("P", type, null).Content), DataSyncLimits.Default);
            Assert.AreEqual(expected, Canon((CustomPropertyContentV1) read.Content!));
            Assert.AreEqual(0, read.Warnings.Count);
        });
    }

    [TestMethod]
    public void EveryTypeRoundTripsThroughTheServicesSerializer()
    {
        NewtonsoftDefaults.UnderEach(() =>
        {
            foreach (var content in Contents())
            {
                var json = CustomPropertyContentMapper.ToOptionsJson(content);
                var descriptor = PropertySystem.Property.GetDescriptor(content.Type);
                if (descriptor.OptionsType is null)
                {
                    Assert.IsNull(json, content.Type.ToString());
                }
                else
                {
                    // The same stack as ToDbModel: Newtonsoft over the typed options.
                    Assert.AreEqual(JsonConvert.SerializeObject(CustomPropertyContentMapper.ToOptions(content)), json);
                    Assert.IsInstanceOfType(JsonConvert.DeserializeObject(json!, descriptor.OptionsType), descriptor.OptionsType);
                }

                var stored = CustomPropertyContentMapper.ReadRow(content.Name, content.Type, json);
                Assert.IsFalse(stored.Unreadable, content.Type.ToString());
                Assert.AreEqual(Canon(content), Canon(stored.Content), content.Type.ToString());
            }
        });
    }

    [TestMethod]
    public void LocalOddities_AreKept()
    {
        NewtonsoftDefaults.UnderEach(() =>
        {
            // B3 and §3.3: ids of any length, duplicate ids, empty and control-character labels, missing ids.
            var longUuid = new string('f', 200);
            var content = new CustomPropertyContentV1
            {
                Name = "", Type = PropertyType.MultipleChoice, IgnoreCase = true,
                Choices =
                [
                    new CustomPropertyChoiceV1(longUuid, "Long", null),
                    new CustomPropertyChoiceV1("dup", "A\u0000B", "#fff"),
                    new CustomPropertyChoiceV1("dup", "", null),
                    new CustomPropertyChoiceV1(null, "No id", null),
                    new CustomPropertyChoiceV1("x", "action", null),
                    new CustomPropertyChoiceV1("y", "Action", null),
                ],
                // A default naming no option is kept, with an empty label.
                DefaultValue = [OptionRef.Choice("x", "action"), OptionRef.Choice("gone", "")],
            };
            var json = CustomPropertyContentMapper.ToOptionsJson(content)!;
            var stored = CustomPropertyContentMapper.ReadRow(content.Name, content.Type, json);
            Assert.AreEqual(Canon(content), Canon(stored.Content));
            StringAssert.Contains(CanonicalJson.Serialize(Codec.Write(stored.Content)), "\"uuid\":\"\"");
        });
    }

    [TestMethod]
    public void NullLabelsAndIdsReadAsEmpty()
    {
        NewtonsoftDefaults.UnderEach(() =>
        {
            const string json = """
                {"IgnoreCase":false,"Tags":[{"Group":null,"Name":null,"Value":null,"Color":null},{"Group":"G","Name":"n","Value":"t"}]}
                """;
            var stored = CustomPropertyContentMapper.ReadRow(null, PropertyType.Tags, json);
            Assert.AreEqual("""{"ignoreCase":false,"name":"","tags":[{"name":"","uuid":""},{"group":"G","name":"n","uuid":"t"}],"type":"Tags"}""",
                Canon(stored.Content));
            Assert.IsNull(stored.Content.Tags[0].Uuid);
        });
    }

    [TestMethod]
    public void AChoiceStoredWithoutAnId_ReadsTheSameEveryTime()
    {
        NewtonsoftDefaults.UnderEach(() =>
        {
            // ChoiceOptions.Value defaults to a fresh guid: without care each read would see another id.
            const string json = """{"Choices":[{"Label":"A"},{"Label":"B","Value":"b"},{"Label":"C","Value":null}]}""";
            var first = CustomPropertyContentMapper.ReadRow("P", PropertyType.SingleChoice, json).Content;
            var second = CustomPropertyContentMapper.ReadRow("P", PropertyType.SingleChoice, json).Content;
            Assert.AreEqual(Canon(first), Canon(second));
            CollectionAssert.AreEqual(new[] { null, "b", null }, first.Choices.Select(c => c.Uuid).ToArray());
        });
    }

    [TestMethod]
    public void CamelCaseOptionsFromTheWebReadLikeTheServicesOwn()
    {
        NewtonsoftDefaults.UnderEach(() =>
        {
            const string camel = """{"ignoreCase":true,"choices":[{"value":"a","label":"A","color":"#fff"}],"defaultValue":["a"]}""";
            var pascal = JsonConvert.SerializeObject(new MultipleChoicePropertyOptions
            {
                IgnoreCase = true, Choices = [new() { Value = "a", Label = "A", Color = "#fff" }], DefaultValue = ["a"],
            });
            Assert.AreEqual(Canon(CustomPropertyContentMapper.ReadRow("P", PropertyType.MultipleChoice, pascal).Content),
                Canon(CustomPropertyContentMapper.ReadRow("P", PropertyType.MultipleChoice, camel).Content));
        });
    }

    [TestMethod]
    [DataRow(PropertyType.MultipleChoice, "{not json")]
    [DataRow(PropertyType.MultipleChoice, "[]")]
    [DataRow(PropertyType.MultipleChoice, "\"text\"")]
    [DataRow(PropertyType.MultipleChoice, """{"Choices":"x"}""")]
    [DataRow(PropertyType.Tags, """{"Tags":[1]}""")]
    [DataRow(PropertyType.Multilevel, """{"Data":{"Value":"x"}}""")]
    [DataRow(PropertyType.Number, """{"Precision":"many"}""")]
    [DataRow(PropertyType.Rating, "{} trailing")]
    // A null where an option belongs deserializes, but is no option.
    [DataRow(PropertyType.MultipleChoice, """{"Choices":[null]}""")]
    [DataRow(PropertyType.SingleChoice, """{"Choices":[{"Value":"a","Label":"A"},null]}""")]
    [DataRow(PropertyType.Tags, """{"Tags":[null]}""")]
    [DataRow(PropertyType.Multilevel, """{"Data":[null]}""")]
    [DataRow(PropertyType.Multilevel, """{"Data":[{"Value":"a","Label":"A","Children":[null]}]}""")]
    public void OptionsThatDoNotRead_MakeTheRowUnreadable(PropertyType type, string options)
    {
        NewtonsoftDefaults.UnderEach(() =>
        {
            var stored = CustomPropertyContentMapper.ReadRow("Broken", type, options);
            Assert.IsTrue(stored.Unreadable);
            Assert.AreEqual($$"""{"name":"Broken","type":"{{type}}"}""", Canon(stored.Content));
            // What Refresh and the merger do with it: the local parser takes it.
            Assert.AreEqual(Canon(stored.Content), Canon(Codec.ReadLocal(Codec.Write(stored.Content))));
        });
    }

    [TestMethod]
    public void OptionsOfATypeWithoutOptionsAreIgnored()
    {
        NewtonsoftDefaults.UnderEach(() =>
        {
            var stored = CustomPropertyContentMapper.ReadRow("T", PropertyType.SingleLineText, "{not json");
            Assert.IsFalse(stored.Unreadable);
            Assert.AreEqual("""{"name":"T","type":"SingleLineText"}""", Canon(stored.Content));
        });
    }

    [TestMethod]
    public void AnEmptyTagGroupIsStoredAsNone()
    {
        NewtonsoftDefaults.UnderEach(() =>
        {
            var content = new CustomPropertyContentV1
            {
                Name = "T", Type = PropertyType.Tags, IgnoreCase = false,
                Tags = [new CustomPropertyTagV1("a", "", "x", null), new CustomPropertyTagV1("b", null, "y", null)],
            };
            var options = (TagsPropertyOptions) CustomPropertyContentMapper.ToOptions(content)!;
            Assert.IsTrue(options.Tags!.All(t => t.Group is null), "TagValue stores \"\" as no group");
            var back = CustomPropertyContentMapper.ReadRow("T", PropertyType.Tags, CustomPropertyContentMapper.ToOptionsJson(content)).Content;
            Assert.IsNull(back.Tags[0].Group);
        });
    }

    [TestMethod]
    public void SettingsOfAnotherTypeAndChildrenLocalAreNotOptions()
    {
        NewtonsoftDefaults.UnderEach(() =>
        {
            var content = new CustomPropertyContentV1
            {
                Name = "S", Type = PropertyType.Number, Settings = new CustomPropertySettingsV1
                {
                    Precision = 3, MaxValue = 9, Layout = "Carousel", ShowProgressBar = true, ValueIsSingleton = true,
                },
            };
            var number = JObject.Parse(CustomPropertyContentMapper.ToOptionsJson(content)!);
            Assert.AreEqual(1, number.Count, number.ToString());
            Assert.AreEqual(3, number.GetValue("Precision", StringComparison.OrdinalIgnoreCase)!.Value<int>());

            var choice = new CustomPropertyContentV1
            {
                Name = "C", Type = PropertyType.SingleChoice, IgnoreCase = false, ChildrenLocal = true,
                Choices = [new CustomPropertyChoiceV1("a", "A", null)],
            };
            var back = CustomPropertyContentMapper.ReadRow("C", PropertyType.SingleChoice,
                CustomPropertyContentMapper.ToOptionsJson(choice)).Content;
            Assert.IsFalse(back.ChildrenLocal, "childrenLocal lives on the side row");
            Assert.AreEqual("a", back.Choices.Single().Uuid);
        });
    }

    [TestMethod]
    public void AnUndefinedAttachmentLayoutRoundTrips()
    {
        NewtonsoftDefaults.UnderEach(() =>
        {
            var json = JsonConvert.SerializeObject(new AttachmentPropertyOptions { Layout = (AttachmentLayout) 7 });
            var content = CustomPropertyContentMapper.ReadRow("G", PropertyType.Attachment, json).Content;
            Assert.AreEqual("7", content.Settings!.Layout);
            Assert.AreEqual(json, CustomPropertyContentMapper.ToOptionsJson(content));
            // A reader holds it: this build does not know that layout.
            Assert.IsNotNull(Codec.Read(Codec.Write(content), DataSyncLimits.Default).Held);
        });
    }

    [TestMethod]
    public void AnUnknownTypeIsRefused()
    {
        NewtonsoftDefaults.UnderEach(() =>
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                CustomPropertyContentMapper.ReadRow("X", (PropertyType) 99, null));
        });
    }

    /// <summary>One content per type, with settings, colours, defaults and nested nodes where the type has them.</summary>
    private static IEnumerable<CustomPropertyContentV1> Contents()
    {
        foreach (var type in Enum.GetValues<PropertyType>())
        {
            var content = new CustomPropertyContentV1 { Name = $"{type} property", Type = type };
            yield return type switch
            {
                PropertyType.SingleChoice => content with
                {
                    IgnoreCase = true,
                    Choices = [new("a", "A", "#e5484d"), new("b", "B", null)],
                    DefaultValue = [OptionRef.Choice("b", "B")],
                },
                PropertyType.MultipleChoice => content with
                {
                    IgnoreCase = false,
                    Choices = [new("a", "A", null), new("b", "B", "#fff"), new("a2", "A", null)],
                    DefaultValue = [OptionRef.Choice("b", "B"), OptionRef.Choice("a", "A")],
                },
                PropertyType.Tags => content with
                {
                    IgnoreCase = true,
                    Tags = [new("t1", "Studio", "Kyoto", "#3e63dd"), new("t2", null, "Isekai", null)],
                },
                PropertyType.Multilevel => content with
                {
                    IgnoreCase = false,
                    Settings = new CustomPropertySettingsV1 { ValueIsSingleton = true },
                    Nodes =
                    [
                        new("n1", "Asia", "#30a46c")
                        {
                            Children = [new("n2", "Japan", null) { Children = [new("n3", "Kyoto", null)] }],
                        },
                        new("n4", "Europe", null),
                    ],
                    DefaultValue = [OptionRef.Node("n3", ["Asia", "Japan", "Kyoto"]), OptionRef.Node("n4", ["Europe"])],
                },
                PropertyType.Number => content with { Settings = new CustomPropertySettingsV1 { Precision = 2 } },
                PropertyType.Percentage => content with
                {
                    Settings = new CustomPropertySettingsV1 { Precision = 1, ShowProgressBar = true },
                },
                PropertyType.Rating => content with { Settings = new CustomPropertySettingsV1 { MaxValue = 10 } },
                PropertyType.Attachment => content with
                {
                    Settings = new CustomPropertySettingsV1 { Layout = CustomPropertyAttachmentLayouts.Carousel },
                },
                _ => content,
            };
        }
    }
}
