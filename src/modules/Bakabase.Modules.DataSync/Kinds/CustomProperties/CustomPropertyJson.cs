using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Refs;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

/// <summary>
/// The JSON shape of <see cref="CustomPropertyContentV1"/> (v3.1 §3.3): member names, the canonical writer and the
/// unvalidated local parser. Validation of peer content is the codec's (<c>CustomPropertyCodec.Read</c>).
/// </summary>
internal static class CustomPropertyJson
{
    public const string Name = "name";
    public const string Type = "type";
    public const string IgnoreCase = "ignoreCase";
    public const string Settings = "settings";
    public const string Choices = "choices";
    public const string Tags = "tags";
    public const string Nodes = "nodes";
    public const string DefaultValue = "defaultValue";
    public const string ChildrenLocal = "childrenLocal";

    public const string Precision = "precision";
    public const string ShowProgressBar = "showProgressBar";
    public const string MaxValue = "maxValue";
    public const string Layout = "layout";
    public const string ValueIsSingleton = "valueIsSingleton";

    public const string Uuid = "uuid";
    public const string Label = "label";
    public const string Group = "group";
    public const string TagName = "name";
    public const string Color = "color";
    public const string Children = "children";

    public static readonly IReadOnlyList<string> ContentMembers =
        [Name, Type, IgnoreCase, Settings, Choices, Tags, Nodes, DefaultValue, ChildrenLocal];

    public static readonly IReadOnlyList<string> SettingMembers =
        [Precision, ShowProgressBar, MaxValue, Layout, ValueIsSingleton];

    public static readonly IReadOnlySet<string> ChoiceMembers = new HashSet<string> { Uuid, Label, Color };
    public static readonly IReadOnlySet<string> TagMembers = new HashSet<string> { Uuid, Group, TagName, Color };
    public static readonly IReadOnlySet<string> NodeMembers = new HashSet<string> { Uuid, Label, Color, Children };

    // ---- Write ------------------------------------------------------------------------------

    /// <summary>
    /// Canonical content: a member is written exactly when it is present in <paramref name="content"/> (see
    /// <see cref="CustomPropertyContentV1"/>). No nulls, no floats. Throws for a type outside the enum (a codec bug).
    /// </summary>
    public static JsonObject Write(CustomPropertyContentV1 content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var type = CustomPropertyTypes.NameOf(content.Type)
                   ?? throw new InvalidOperationException($"customProperty: {(int)content.Type} is not a PropertyType.");
        var json = new JsonObject
        {
            [Name] = content.Name ?? throw new InvalidOperationException("customProperty: a name is required."),
            [Type] = type,
        };
        if (content.IgnoreCase is { } ignoreCase) json[IgnoreCase] = ignoreCase;
        if (content.Settings is { } settings) json[Settings] = WriteSettings(settings);
        if (content.Choices.Count > 0) json[Choices] = new JsonArray(content.Choices.Select(c => (JsonNode)WriteChoice(c)).ToArray());
        if (content.Tags.Count > 0) json[Tags] = new JsonArray(content.Tags.Select(t => (JsonNode)WriteTag(t)).ToArray());
        if (content.Nodes.Count > 0) json[Nodes] = WriteNodes(content.Nodes);
        if (content.DefaultValue.Count > 0)
            json[DefaultValue] = new JsonArray(content.DefaultValue.Select(r => (JsonNode)r.ToJson()).ToArray());
        if (content.ChildrenLocal) json[ChildrenLocal] = true;
        return json;
    }

    public static JsonObject WriteSettings(CustomPropertySettingsV1 settings)
    {
        var json = new JsonObject();
        if (settings.Precision is { } precision) json[Precision] = precision;
        if (settings.ShowProgressBar is { } showProgressBar) json[ShowProgressBar] = showProgressBar;
        if (settings.MaxValue is { } maxValue) json[MaxValue] = maxValue;
        if (settings.Layout is { } layout) json[Layout] = layout;
        if (settings.ValueIsSingleton is { } valueIsSingleton) json[ValueIsSingleton] = valueIsSingleton;
        return json;
    }

    private static JsonObject WriteChoice(CustomPropertyChoiceV1 choice)
    {
        var json = new JsonObject { [Uuid] = choice.Uuid ?? "", [Label] = choice.Label ?? "" };
        if (!string.IsNullOrEmpty(choice.Color)) json[Color] = choice.Color;
        return json;
    }

    private static JsonObject WriteTag(CustomPropertyTagV1 tag)
    {
        var json = new JsonObject { [Uuid] = tag.Uuid ?? "", [TagName] = tag.Name ?? "" };
        if (tag.Group is not null) json[Group] = tag.Group;
        if (!string.IsNullOrEmpty(tag.Color)) json[Color] = tag.Color;
        return json;
    }

    private static JsonArray WriteNodes(IReadOnlyList<CustomPropertyNodeV1> nodes)
    {
        var array = new JsonArray();
        foreach (var node in nodes)
        {
            var json = new JsonObject { [Uuid] = node.Uuid ?? "", [Label] = node.Label ?? "" };
            if (!string.IsNullOrEmpty(node.Color)) json[Color] = node.Color;
            if (node.Children.Count > 0) json[Children] = WriteNodes(node.Children);
            array.Add(json);
        }

        return array;
    }

    // ---- ReadLocal --------------------------------------------------------------------------

    /// <summary>
    /// Parses this device's own canonical content without validating, dropping or holding anything (v3.1 B3): any
    /// string, any number of options, any depth, duplicate uuids. <c>"uuid":""</c> reads back as a null uuid (§3.3).
    /// Throws <see cref="FormatException"/> for a shape <see cref="Write"/> never produces (a codec bug).
    /// </summary>
    public static CustomPropertyContentV1 ReadLocal(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);
        foreach (var (member, _) in json)
        {
            if (!ContentMembers.Contains(member)) throw Malformed($"unknown member \"{member}\"");
        }

        var typeName = RequiredString(json, Type);
        if (!CustomPropertyTypes.TryParse(typeName, out var type)) throw Malformed($"unknown type \"{typeName}\"");
        return new CustomPropertyContentV1
        {
            Name = RequiredString(json, Name),
            Type = type,
            IgnoreCase = OptionalBool(json, IgnoreCase),
            Settings = json[Settings] is null ? null : ReadLocalSettings(AsObject(json[Settings], Settings)),
            Choices = OptionalArray(json, Choices).Select(ReadLocalChoice).ToArray(),
            Tags = OptionalArray(json, Tags).Select(ReadLocalTag).ToArray(),
            Nodes = ReadLocalNodes(OptionalArray(json, Nodes)),
            DefaultValue = OptionalArray(json, DefaultValue).Select(n =>
                OptionRef.TryRead(n, out var r) ? r! : throw Malformed("unreadable defaultValue ref")).ToArray(),
            ChildrenLocal = OptionalBool(json, ChildrenLocal) ?? false,
        };
    }

    private static CustomPropertySettingsV1 ReadLocalSettings(JsonObject json)
    {
        foreach (var (member, _) in json)
        {
            if (!SettingMembers.Contains(member)) throw Malformed($"unknown setting \"{member}\"");
        }

        return new CustomPropertySettingsV1
        {
            Precision = OptionalInt(json, Precision),
            ShowProgressBar = OptionalBool(json, ShowProgressBar),
            MaxValue = OptionalInt(json, MaxValue),
            Layout = json[Layout] is null ? null : RequiredString(json, Layout),
            ValueIsSingleton = OptionalBool(json, ValueIsSingleton),
        };
    }

    private static CustomPropertyChoiceV1 ReadLocalChoice(JsonNode? node)
    {
        var json = AsObject(node, Choices);
        CheckMembers(json, ChoiceMembers, Choices);
        return new CustomPropertyChoiceV1(LocalUuid(json), RequiredString(json, Label), OptionalString(json, Color));
    }

    private static CustomPropertyTagV1 ReadLocalTag(JsonNode? node)
    {
        var json = AsObject(node, Tags);
        CheckMembers(json, TagMembers, Tags);
        return new CustomPropertyTagV1(LocalUuid(json), OptionalString(json, Group), RequiredString(json, TagName),
            OptionalString(json, Color));
    }

    private static CustomPropertyNodeV1[] ReadLocalNodes(IEnumerable<JsonNode?> nodes) =>
        nodes.Select(node =>
        {
            var json = AsObject(node, Nodes);
            CheckMembers(json, NodeMembers, Nodes);
            return new CustomPropertyNodeV1(LocalUuid(json), RequiredString(json, Label), OptionalString(json, Color))
            {
                Children = ReadLocalNodes(OptionalArray(json, Children)),
            };
        }).ToArray();

    private static string? LocalUuid(JsonObject json)
    {
        var uuid = RequiredString(json, Uuid);
        return uuid.Length == 0 ? null : uuid;
    }

    private static void CheckMembers(JsonObject json, IReadOnlySet<string> allowed, string where)
    {
        foreach (var (member, _) in json)
        {
            if (!allowed.Contains(member)) throw Malformed($"unknown member \"{member}\" in {where}");
        }
    }

    private static JsonObject AsObject(JsonNode? node, string where) =>
        node as JsonObject ?? throw Malformed($"{where}: an object expected");

    private static IEnumerable<JsonNode?> OptionalArray(JsonObject json, string member) => json[member] switch
    {
        null => [],
        JsonArray array => array,
        _ => throw Malformed($"{member}: an array expected"),
    };

    private static string RequiredString(JsonObject json, string member) =>
        TryString(json[member], out var value) ? value : throw Malformed($"{member}: a string expected");

    private static string? OptionalString(JsonObject json, string member) =>
        json[member] is null ? null : RequiredString(json, member);

    private static bool? OptionalBool(JsonObject json, string member) => json[member] switch
    {
        null => null,
        JsonValue v when v.GetValueKind() == JsonValueKind.True => true,
        JsonValue v when v.GetValueKind() == JsonValueKind.False => false,
        _ => throw Malformed($"{member}: a boolean expected"),
    };

    private static int? OptionalInt(JsonObject json, string member) => json[member] switch
    {
        null => null,
        JsonValue v when JsonNumbers.TryGetInt64(v, out var l) && l is >= int.MinValue and <= int.MaxValue => (int)l,
        _ => throw Malformed($"{member}: an integer expected"),
    };

    /// <summary>True for a JSON string (parsed or built from a CLR string).</summary>
    public static bool TryString(JsonNode? node, out string value)
    {
        value = null!;
        if (node is not JsonValue v || v.GetValueKind() != JsonValueKind.String || !v.TryGetValue(out string? s))
            return false;
        value = s;
        return true;
    }

    private static FormatException Malformed(string detail) => new($"customProperty: malformed local content: {detail}.");
}
