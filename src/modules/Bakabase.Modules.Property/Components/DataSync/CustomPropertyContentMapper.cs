using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Refs;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Components.Properties.Attachment;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Components.Properties.Number;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Modules.Property.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Modules.Property.Components.DataSync;

/// <summary>A stored custom property as data sync reads it (§3.3).</summary>
/// <param name="Content">
/// The row's content; <c>{name, type}</c> only when <paramref name="Unreadable"/>.
/// </param>
/// <param name="Unreadable">
/// The stored <c>Options</c> do not deserialize into the type's options: the row is recorded, published held
/// (<c>LocalUnreadable</c>) and never written by data sync.
/// </param>
public sealed record CustomPropertyStoredContent(CustomPropertyContentV1 Content, bool Unreadable);

/// <summary>
/// Typed custom property options ↔ <c>customProperty</c> content (v3.1 §3.3, §3.3 here). Options are read and written
/// with Newtonsoft through the type's <c>OptionsType</c>, the same stack as <c>ToDbModel</c>/<c>ToDomainModel</c>.
/// </summary>
/// <remarks>
/// <para>
/// Reading keeps every option as stored, in list order: duplicates, empty labels and ids of any length (v3.1 B3). An
/// option without a uuid reads as a null uuid and a null label or name as <c>""</c> (§3.3), so such options are kept
/// here and never published. <c>defaultValue</c> keeps every stored default id as a ref, one that names no option
/// included (with an empty label, or the path <c>[""]</c>), so writing the content back keeps it; publishing drops it.
/// </para>
/// <para>
/// Null options read as the type's <c>InitializeOptions()</c> defaults. Options that do not deserialize make the row
/// unreadable (§3.3): v3.1's "treat as defaults" fallback is gone, because an update would then overwrite them.
/// </para>
/// <para>
/// Writing sets every option's id explicitly (F4) and writes tag groups as the content holds them; the service's own
/// <c>TagValue</c> stores <c>""</c> as no group. <c>childrenLocal</c> is not an option: it lives on data sync's side
/// row (§3.6) and is ignored here, as are settings a type does not use. Order is never content (§3.7).
/// </para>
/// </remarks>
public static class CustomPropertyContentMapper
{
    private const string ChoicesMember = "Choices";
    private const string ValueMember = "Value";

    /// <summary>
    /// Types data sync can represent: a defined <see cref="PropertyType"/> the Property module has a descriptor for.
    /// A row of any other type (a newer build's, after a downgrade) has no content and is never read by data sync.
    /// </summary>
    public static bool Supports(PropertyType type) =>
        CustomPropertyTypes.NameOf(type) is not null && PropertySystem.Property.TryGetDescriptor(type) is not null;

    /// <summary>Content of a stored row: its name, type and serialized options (§3.3).</summary>
    public static CustomPropertyStoredContent ReadRow(string? name, PropertyType type, string? serializedOptions)
    {
        if (!Supports(type)) throw new ArgumentOutOfRangeException(nameof(type), type, "Not a supported property type.");
        var descriptor = PropertySystem.Property.GetDescriptor(type);
        object? options = null;
        if (descriptor.OptionsType is { } optionsType && !string.IsNullOrEmpty(serializedOptions))
        {
            try
            {
                options = JsonConvert.DeserializeObject(serializedOptions, optionsType);
            }
            catch (Exception e) when (e is JsonException or ArgumentException or InvalidCastException
                                          or FormatException or OverflowException or InvalidOperationException)
            {
                return new CustomPropertyStoredContent(
                    new CustomPropertyContentV1 { Name = name ?? "", Type = type }, Unreadable: true);
            }

            if (options is not null) ClearGeneratedChoiceIds(options, serializedOptions);
        }

        return new CustomPropertyStoredContent(ToContent(name, type, options), Unreadable: false);
    }

    /// <summary>
    /// Content of a property with typed <paramref name="options"/> (null: the type's defaults). Reference types carry
    /// <c>ignoreCase</c>, and types with settings carry all of them, as validated content does.
    /// </summary>
    public static CustomPropertyContentV1 ToContent(string? name, PropertyType type, object? options)
    {
        if (!Supports(type)) throw new ArgumentOutOfRangeException(nameof(type), type, "Not a supported property type.");
        options ??= PropertySystem.Property.GetDescriptor(type).InitializeOptions();
        var content = new CustomPropertyContentV1 { Name = name ?? "", Type = type };
        switch (options)
        {
            case null:
                return content;
            case SingleChoicePropertyOptions single:
            {
                var choices = Choices(single.Choices);
                return content with
                {
                    IgnoreCase = single.IgnoreCase, Choices = choices,
                    DefaultValue = ChoiceRefs(choices, string.IsNullOrEmpty(single.DefaultValue) ? [] : [single.DefaultValue]),
                };
            }
            case MultipleChoicePropertyOptions multiple:
            {
                var choices = Choices(multiple.Choices);
                return content with
                {
                    IgnoreCase = multiple.IgnoreCase, Choices = choices,
                    DefaultValue = ChoiceRefs(choices, multiple.DefaultValue ?? []),
                };
            }
            case TagsPropertyOptions tags:
                return content with
                {
                    IgnoreCase = tags.IgnoreCase,
                    Tags = (tags.Tags ?? []).Select(t => new CustomPropertyTagV1(Uuid(t.Value), t.Group, t.Name ?? "",
                        t.Color)).ToArray(),
                };
            case MultilevelPropertyOptions multilevel:
            {
                var nodes = Nodes(multilevel.Data);
                return content with
                {
                    IgnoreCase = multilevel.IgnoreCase, Nodes = nodes,
                    Settings = new CustomPropertySettingsV1 { ValueIsSingleton = multilevel.ValueIsSingleton },
                    DefaultValue = NodeRefs(nodes, multilevel.DefaultValue ?? []),
                };
            }
            case NumberPropertyOptions number:
                return content with { Settings = new CustomPropertySettingsV1 { Precision = number.Precision } };
            case PercentagePropertyOptions percentage:
                return content with
                {
                    Settings = new CustomPropertySettingsV1
                        { Precision = percentage.Precision, ShowProgressBar = percentage.ShowProgressBar },
                };
            case RatingPropertyOptions rating:
                return content with { Settings = new CustomPropertySettingsV1 { MaxValue = rating.MaxValue } };
            case AttachmentPropertyOptions attachment:
                // An undefined value reads as its number ("5"), which ToOptions parses back to the same value.
                return content with { Settings = new CustomPropertySettingsV1 { Layout = attachment.Layout.ToString() } };
            default:
                throw new NotSupportedException(
                    $"{type}: data sync does not map options of type {options.GetType().Name}.");
        }
    }

    /// <summary>
    /// Typed options for <paramref name="content"/> (null for a type without options). Every option keeps its uuid, a
    /// null uuid included; lists stay in content order.
    /// </summary>
    public static object? ToOptions(CustomPropertyContentV1 content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!Supports(content.Type))
            throw new ArgumentOutOfRangeException(nameof(content), content.Type, "Not a supported property type.");
        var ignoreCase = content.IgnoreCase ?? false;
        var settings = content.Settings;
        switch (content.Type)
        {
            case PropertyType.SingleChoice:
                return new SingleChoicePropertyOptions
                {
                    IgnoreCase = ignoreCase, Choices = ToChoices(content.Choices),
                    DefaultValue = content.DefaultValue.Count == 0 ? null : content.DefaultValue[0].Uuid,
                };
            case PropertyType.MultipleChoice:
                return new MultipleChoicePropertyOptions
                {
                    IgnoreCase = ignoreCase, Choices = ToChoices(content.Choices),
                    DefaultValue = content.DefaultValue.Count == 0 ? null : content.DefaultValue.Select(r => r.Uuid).ToList(),
                };
            case PropertyType.Tags:
                return new TagsPropertyOptions
                {
                    IgnoreCase = ignoreCase,
                    Tags = content.Tags.Select(t =>
                        new TagsPropertyOptions.TagOptions(t.Group, t.Name) { Value = t.Uuid!, Color = t.Color }).ToList(),
                };
            case PropertyType.Multilevel:
                return new MultilevelPropertyOptions
                {
                    IgnoreCase = ignoreCase, Data = ToNodes(content.Nodes),
                    DefaultValue = content.DefaultValue.Count == 0 ? null : content.DefaultValue.Select(r => r.Uuid).ToList(),
                    ValueIsSingleton = settings?.ValueIsSingleton ?? false,
                };
            case PropertyType.Number:
                return new NumberPropertyOptions { Precision = settings?.Precision ?? 0 };
            case PropertyType.Percentage:
                return new PercentagePropertyOptions
                {
                    Precision = settings?.Precision ?? 0, ShowProgressBar = settings?.ShowProgressBar ?? false,
                };
            case PropertyType.Rating:
                return new RatingPropertyOptions { MaxValue = settings?.MaxValue ?? RatingPropertyOptions.DefaultMaxValue };
            case PropertyType.Attachment:
                return new AttachmentPropertyOptions { Layout = ParseLayout(settings?.Layout) };
            default:
                var descriptor = PropertySystem.Property.GetDescriptor(content.Type);
                return descriptor.OptionsType is null
                    ? null
                    : throw new NotSupportedException(
                        $"{content.Type}: data sync does not map options of type {descriptor.OptionsType.Name}.");
        }
    }

    /// <summary>
    /// <see cref="ToOptions"/> serialized the way the service stores options (Newtonsoft, as <c>ToDbModel</c>); a
    /// serializer failure throws instead of turning into null options.
    /// </summary>
    public static string? ToOptionsJson(CustomPropertyContentV1 content) =>
        ToOptions(content).SerializeAsCustomPropertyOptions(throwOnError: true);

    // ---- reading -----------------------------------------------------------------------------

    private static string? Uuid(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static CustomPropertyChoiceV1[] Choices(List<ChoiceOptions>? choices) =>
        (choices ?? []).Select(c => new CustomPropertyChoiceV1(Uuid(c.Value), c.Label ?? "", c.Color)).ToArray();

    private static CustomPropertyNodeV1[] Nodes(List<MultilevelDataOptions>? nodes) =>
        (nodes ?? []).Select(n => new CustomPropertyNodeV1(Uuid(n.Value), n.Label ?? "", n.Color)
        {
            Children = Nodes(n.Children),
        }).ToArray();

    private static OptionRef[] ChoiceRefs(IReadOnlyList<CustomPropertyChoiceV1> choices, IEnumerable<string?> uuids) =>
        uuids.Where(u => !string.IsNullOrEmpty(u))
            .Select(u => OptionRef.Choice(u!, choices.FirstOrDefault(c => c.Uuid == u)?.Label ?? ""))
            .ToArray();

    private static OptionRef[] NodeRefs(IReadOnlyList<CustomPropertyNodeV1> nodes, IEnumerable<string?> uuids) =>
        uuids.Where(u => !string.IsNullOrEmpty(u))
            .Select(u => OptionRef.Node(u!, PathTo(nodes, u!) ?? [""]))
            .ToArray();

    /// <summary>The labels from a root down to the first node (pre-order) with <paramref name="uuid"/>, or null.</summary>
    private static string[]? PathTo(IReadOnlyList<CustomPropertyNodeV1> nodes, string uuid)
    {
        foreach (var node in nodes)
        {
            if (node.Uuid == uuid) return [node.Label];
            if (PathTo(node.Children, uuid) is { } below) return [node.Label, ..below];
        }

        return null;
    }

    /// <summary>
    /// <see cref="ChoiceOptions.Value"/> defaults to a fresh random guid, so a stored choice without a <c>Value</c>
    /// would read with a different uuid every time — and so would one with <c>"Value":null</c>, because the app's
    /// Newtonsoft defaults ignore nulls (<c>AppService</c>), which is also why a null id is never written. Such a
    /// choice has no uuid; it is kept and never published (§3.3).
    /// </summary>
    private static void ClearGeneratedChoiceIds(object options, string serializedOptions)
    {
        var choices = options switch
        {
            SingleChoicePropertyOptions s => s.Choices,
            MultipleChoicePropertyOptions m => m.Choices,
            _ => null,
        };
        if (choices is not { Count: > 0 }) return;
        JToken parsed;
        try
        {
            parsed = JToken.Parse(serializedOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (parsed is not JObject json ||
            json.GetValue(ChoicesMember, StringComparison.OrdinalIgnoreCase) is not JArray stored ||
            stored.Count != choices.Count) return;
        for (var i = 0; i < choices.Count; i++)
        {
            if (stored[i] is JObject choice &&
                choice.GetValue(ValueMember, StringComparison.OrdinalIgnoreCase) is null or { Type: JTokenType.Null })
                choices[i] = choices[i] with { Value = null! };
        }
    }

    // ---- writing -----------------------------------------------------------------------------

    private static List<ChoiceOptions> ToChoices(IReadOnlyList<CustomPropertyChoiceV1> choices) =>
        choices.Select(c => new ChoiceOptions { Value = c.Uuid!, Label = c.Label, Color = c.Color }).ToList();

    private static List<MultilevelDataOptions> ToNodes(IReadOnlyList<CustomPropertyNodeV1> nodes) =>
        nodes.Select(n => new MultilevelDataOptions
        {
            Value = n.Uuid!, Label = n.Label, Color = n.Color!,
            Children = n.Children.Count == 0 ? null : ToNodes(n.Children),
        }).ToList();

    private static AttachmentLayout ParseLayout(string? layout)
    {
        if (layout is null) return AttachmentLayout.Tile;
        return Enum.TryParse<AttachmentLayout>(layout, ignoreCase: false, out var parsed)
            ? parsed
            : throw new ArgumentException($"'{layout}' is not an attachment layout.", nameof(layout));
    }
}
