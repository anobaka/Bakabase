using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

/// <summary>Which content members each of the 16 property types uses (v3.1 §3.3, F6, F7).</summary>
public static class CustomPropertyTypes
{
    /// <summary>The children list a type keeps, or null for a type without options.</summary>
    internal enum ChildList { Choices, Tags, Nodes }

    /// <summary>Choice, multiple choice, tags and multilevel: options with ids, IgnoreCase, "Sync the definition only".</summary>
    public static bool IsReference(PropertyType type) =>
        type is PropertyType.SingleChoice or PropertyType.MultipleChoice or PropertyType.Multilevel or PropertyType.Tags;

    internal static ChildList? ChildListOf(PropertyType type) => type switch
    {
        PropertyType.SingleChoice or PropertyType.MultipleChoice => ChildList.Choices,
        PropertyType.Tags => ChildList.Tags,
        PropertyType.Multilevel => ChildList.Nodes,
        _ => null,
    };

    /// <summary>Single choice (at most one ref), multiple choice and multilevel. Tags have no default value.</summary>
    public static bool HasDefaultValue(PropertyType type) =>
        type is PropertyType.SingleChoice or PropertyType.MultipleChoice or PropertyType.Multilevel;

    /// <summary>The settings a type uses with their defaults filled in, or null for a type without settings.</summary>
    public static CustomPropertySettingsV1? DefaultSettings(PropertyType type) => type switch
    {
        PropertyType.Number => new CustomPropertySettingsV1 { Precision = 0 },
        PropertyType.Percentage => new CustomPropertySettingsV1 { Precision = 0, ShowProgressBar = false },
        PropertyType.Rating => new CustomPropertySettingsV1 { MaxValue = 5 },
        PropertyType.Attachment => new CustomPropertySettingsV1 { Layout = CustomPropertyAttachmentLayouts.Tile },
        PropertyType.Multilevel => new CustomPropertySettingsV1 { ValueIsSingleton = false },
        _ => null,
    };

    /// <summary>The <c>settings.*</c> member names a type uses, in canonical (ordinal) order.</summary>
    public static IReadOnlyList<string> SettingNames(PropertyType type) => type switch
    {
        PropertyType.Number => [CustomPropertyJson.Precision],
        PropertyType.Percentage => [CustomPropertyJson.Precision, CustomPropertyJson.ShowProgressBar],
        PropertyType.Rating => [CustomPropertyJson.MaxValue],
        PropertyType.Attachment => [CustomPropertyJson.Layout],
        PropertyType.Multilevel => [CustomPropertyJson.ValueIsSingleton],
        _ => [],
    };

    /// <summary>
    /// The enum name <see cref="PropertyType"/> travels as, or null for a value outside the enum; numeric strings are
    /// never names.
    /// </summary>
    public static string? NameOf(PropertyType type) => Enum.IsDefined(type) ? type.ToString() : null;

    public static bool TryParse(string name, out PropertyType type)
    {
        type = default;
        if (name.Length == 0 || !char.IsAsciiLetter(name[0])) return false;
        if (!Enum.TryParse(name, ignoreCase: false, out PropertyType parsed) || !Enum.IsDefined(parsed) ||
            parsed.ToString() != name) return false;
        type = parsed;
        return true;
    }
}
