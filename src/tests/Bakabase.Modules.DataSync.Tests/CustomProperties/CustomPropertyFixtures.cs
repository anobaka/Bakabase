using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Refs;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>Builders and assertions shared by the custom property codec tests.</summary>
internal static class Cp
{
    public static readonly CustomPropertyCodec Codec = new();
    public static IDataSyncKindCodec Untyped => Codec;

    public static CustomPropertyChoiceV1 C(string? uuid, string label, string? color = null) => new(uuid, label, color);

    public static CustomPropertyTagV1 T(string? uuid, string? group, string name, string? color = null) =>
        new(uuid, group, name, color);

    public static CustomPropertyNodeV1 N(string? uuid, string label, params CustomPropertyNodeV1[] children) =>
        new(uuid, label, null) { Children = children };

    public static CustomPropertyNodeV1 N(string? uuid, string label, string? color, params CustomPropertyNodeV1[] children) =>
        new(uuid, label, color) { Children = children };

    public static CustomPropertyContentV1 Choice(string name, bool ignoreCase, params CustomPropertyChoiceV1[] choices) =>
        new() { Name = name, Type = PropertyType.MultipleChoice, IgnoreCase = ignoreCase, Choices = choices };

    public static CustomPropertyContentV1 Single(string name, bool ignoreCase, params CustomPropertyChoiceV1[] choices) =>
        new() { Name = name, Type = PropertyType.SingleChoice, IgnoreCase = ignoreCase, Choices = choices };

    public static CustomPropertyContentV1 Tags(string name, bool ignoreCase, params CustomPropertyTagV1[] tags) =>
        new() { Name = name, Type = PropertyType.Tags, IgnoreCase = ignoreCase, Tags = tags };

    public static CustomPropertyContentV1 Tree(string name, bool ignoreCase, params CustomPropertyNodeV1[] nodes) =>
        new()
        {
            Name = name, Type = PropertyType.Multilevel, IgnoreCase = ignoreCase, Nodes = nodes,
            Settings = new CustomPropertySettingsV1 { ValueIsSingleton = false },
        };

    public static OptionRef Ref(string uuid, string label) => OptionRef.Choice(uuid, label);
    public static OptionRef NodeRef(string uuid, params string[] path) => OptionRef.Node(uuid, path);

    public static string Canon(JsonNode? node) => CanonicalJson.Serialize(node);
    public static string Canon(CustomPropertyContentV1 content) => Canon(Codec.Write(content));
    public static JsonObject Json(string text) => (JsonObject)JsonNode.Parse(text)!;

    public static string Form(CustomPropertyContentV1 content, string? orderKey = null, bool childrenLocal = false) =>
        Canon(Codec.ComparisonForm(content, orderKey, childrenLocal));

    /// <summary>Peer validation that must succeed.</summary>
    public static (CustomPropertyContentV1 Content, CodecReadResult Result) ReadValid(JsonObject json,
        DataSyncLimits? limits = null)
    {
        var result = Codec.Read(json, limits ?? DataSyncLimits.Default);
        Assert.IsNull(result.Held, $"held: {result.Held} {string.Join(",", result.Errors)}");
        return ((CustomPropertyContentV1)result.Content!, result);
    }

    public static (CustomPropertyContentV1 Content, CodecReadResult Result) ReadValid(string json) => ReadValid(Json(json));

    public static CodecReadResult ReadHeld(JsonObject json, DataSyncHeldReason expected, DataSyncLimits? limits = null)
    {
        var result = Codec.Read(json, limits ?? DataSyncLimits.Default);
        Assert.AreEqual(expected, result.Held, string.Join(",", result.Errors));
        Assert.IsNull(result.Content);
        return result;
    }

    public static IReadOnlyList<DataSyncPlanWarning> Warnings(IEnumerable<DataSyncPlanWarning> warnings,
        DataSyncWarningCode code) => warnings.Where(w => w.Code == code).ToArray();

    public static string Arg(DataSyncPlanWarning warning, string key) =>
        warning.Args is { } args && args.TryGetValue(key, out var value) ? value : throw new AssertFailedException(
            $"{warning.Code} has no arg {key}");

    /// <summary>Asserts that no JSON null appears anywhere (codecs never emit one).</summary>
    public static void AssertNoNulls(JsonNode? node)
    {
        switch (node)
        {
            case null:
                Assert.Fail("null in canonical content");
                break;
            case JsonObject obj:
                foreach (var (_, value) in obj) AssertNoNulls(value);
                break;
            case JsonArray array:
                foreach (var item in array) AssertNoNulls(item);
                break;
        }
    }
}
