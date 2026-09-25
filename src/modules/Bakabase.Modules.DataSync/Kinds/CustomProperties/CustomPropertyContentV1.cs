using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Refs;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

/// <summary>
/// Kind <c>customProperty</c>, schemaVersion 1 (v3.1 §3.3, §3.3 here): the portable part of one custom property.
/// <c>Order</c>, <c>Id</c>, <c>CreatedAt</c> and values are never content; order travels as the record's
/// <c>orderKey</c> (§3.7).
/// </summary>
/// <remarks>
/// The typed form is a faithful parse of canonical content, so <c>Write(ReadLocal(x)) == x</c> holds for any shape a
/// local row produces: a member is written exactly when it is present here (<see cref="IgnoreCase"/> and
/// <see cref="Settings"/> are null when absent; an empty list and a false <see cref="ChildrenLocal"/> are never
/// written). Validated content (<c>Read</c>) is normalized instead: IgnoreCase is set exactly for the four reference
/// types, and Settings carries exactly the type's settings with defaults filled in.
/// </remarks>
public sealed record CustomPropertyContentV1
{
    public required string Name { get; init; }

    /// <summary>Serialized by enum name; an unknown name holds the entity (UnknownEnumValue).</summary>
    public required PropertyType Type { get; init; }

    /// <summary>Choice, multiple choice, tags and multilevel only.</summary>
    public bool? IgnoreCase { get; init; }

    public CustomPropertySettingsV1? Settings { get; init; }

    /// <summary>Single and multiple choice, in local list order.</summary>
    public IReadOnlyList<CustomPropertyChoiceV1> Choices { get; init; } = [];

    /// <summary>Tags, in local list order.</summary>
    public IReadOnlyList<CustomPropertyTagV1> Tags { get; init; } = [];

    /// <summary>Multilevel roots, in local list order.</summary>
    public IReadOnlyList<CustomPropertyNodeV1> Nodes { get; init; } = [];

    /// <summary>Single choice (one element), multiple choice and multilevel. Tags have no default value.</summary>
    public IReadOnlyList<OptionRef> DefaultValue { get; init; } = [];

    /// <summary>
    /// "Sync the definition only" (§3.6): a shared field, true only on reference types. While true the published
    /// content carries no children and no default value.
    /// </summary>
    public bool ChildrenLocal { get; init; }

    public bool Equals(CustomPropertyContentV1? other) =>
        other is not null && Name == other.Name && Type == other.Type && IgnoreCase == other.IgnoreCase &&
        Equals(Settings, other.Settings) && Choices.SequenceEqual(other.Choices) && Tags.SequenceEqual(other.Tags) &&
        Nodes.SequenceEqual(other.Nodes) && DefaultValue.SequenceEqual(other.DefaultValue) &&
        ChildrenLocal == other.ChildrenLocal;

    public override int GetHashCode() => HashCode.Combine(Name, Type, IgnoreCase, Settings, Choices.Count, Tags.Count,
        Nodes.Count, DefaultValue.Count);
}

/// <summary>
/// Type settings (v3.1 §3.3). A member is present exactly for the types that use it: Number <c>precision</c>;
/// Percentage <c>precision</c>, <c>showProgressBar</c>; Rating <c>maxValue</c>; Attachment <c>layout</c>; Multilevel
/// <c>valueIsSingleton</c>.
/// </summary>
public sealed record CustomPropertySettingsV1
{
    public int? Precision { get; init; }
    public bool? ShowProgressBar { get; init; }
    public int? MaxValue { get; init; }

    /// <summary>The Property module's <c>AttachmentLayout</c> by name; see <see cref="CustomPropertyAttachmentLayouts"/>.</summary>
    public string? Layout { get; init; }

    public bool? ValueIsSingleton { get; init; }
}

/// <summary>
/// The names of the Property module's <c>AttachmentLayout</c> members, which this pure module cannot reference. An
/// unknown name holds the entity (UnknownEnumValue), like an unknown <see cref="PropertyType"/>.
/// </summary>
public static class CustomPropertyAttachmentLayouts
{
    public const string Tile = "Tile";
    public const string Carousel = "Carousel";
    public static IReadOnlyList<string> All { get; } = [Tile, Carousel];
}

/// <param name="Uuid">The option's id; null when the stored option has none (written as <c>""</c>, never published).</param>
/// <param name="Label">Never null; a stored null label is written as <c>""</c> (never published).</param>
/// <param name="Color">Omitted when null or empty.</param>
public sealed record CustomPropertyChoiceV1(string? Uuid, string Label, string? Color);

/// <param name="Group">Omitted only when null: <c>""</c> is written as <c>"group":""</c>, as the content carries it. The
/// service stores <c>""</c> as null (<see cref="OptionMatcher"/>), and comparison and merging treat both as "no group"
/// (§3.4).</param>
public sealed record CustomPropertyTagV1(string? Uuid, string? Group, string Name, string? Color);

public sealed record CustomPropertyNodeV1(string? Uuid, string Label, string? Color)
{
    /// <summary>Omitted when empty.</summary>
    public IReadOnlyList<CustomPropertyNodeV1> Children { get; init; } = [];

    public bool Equals(CustomPropertyNodeV1? other) =>
        other is not null && Uuid == other.Uuid && Label == other.Label && Color == other.Color &&
        Children.SequenceEqual(other.Children);

    public override int GetHashCode() => HashCode.Combine(Uuid, Label, Color, Children.Count);
}
