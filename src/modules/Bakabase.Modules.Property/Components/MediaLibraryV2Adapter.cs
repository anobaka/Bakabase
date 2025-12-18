using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Components.Properties.Choice;

namespace Bakabase.Modules.Property.Components;

/// <summary>
/// Adapter for MediaLibraryV2 → MediaLibraryV2Multi migration.
///
/// Strategy:
/// - MediaLibraryV2 definition remains SingleChoice (type consistency)
/// - All V2 operations internally redirect to V2Multi
/// - Read: Multi → Single (return first value only)
/// - Write: Single → Multi (wrap in list)
/// - V2Multi operates normally without facade
/// </summary>
public static class MediaLibraryV2Adapter
{
    #region Property Definitions

    /// <summary>
    /// Legacy property definition (SingleChoice, facade only)
    /// </summary>
    public static Bakabase.Abstractions.Models.Domain.Property LegacyProperty =>
        PropertyInternals.BuiltinPropertyMap[ResourceProperty.MediaLibraryV2];

    /// <summary>
    /// Current property definition (MultipleChoice, actual storage)
    /// </summary>
    public static Bakabase.Abstractions.Models.Domain.Property CurrentProperty =>
        PropertyInternals.BuiltinPropertyMap[ResourceProperty.MediaLibraryV2Multi];

    /// <summary>
    /// Legacy property ID
    /// </summary>
    public static int LegacyPropertyId => (int)ResourceProperty.MediaLibraryV2;

    /// <summary>
    /// Current property ID
    /// </summary>
    public static int CurrentPropertyId => (int)ResourceProperty.MediaLibraryV2Multi;

    #endregion

    #region Detection

    /// <summary>
    /// Check if the property is the legacy MediaLibraryV2
    /// </summary>
    public static bool IsLegacyProperty(PropertyPool pool, int id) =>
        pool == PropertyPool.Internal && id == LegacyPropertyId;

    /// <summary>
    /// Check if the property is MediaLibraryV2Multi
    /// </summary>
    public static bool IsCurrentProperty(PropertyPool pool, int id) =>
        pool == PropertyPool.Internal && id == CurrentPropertyId;

    /// <summary>
    /// Check if either V2 or V2Multi
    /// </summary>
    public static bool IsMediaLibraryProperty(PropertyPool pool, int id) =>
        IsLegacyProperty(pool, id) || IsCurrentProperty(pool, id);

    #endregion

    #region Write Operations: Single → Multi

    /// <summary>
    /// Convert SingleChoice DB value to MultipleChoice format for storage.
    /// Used when writing via legacy MediaLibraryV2 API.
    /// </summary>
    public static List<string>? ToMultiDbValue(string? singleDbValue) =>
        string.IsNullOrEmpty(singleDbValue) ? null : [singleDbValue];

    /// <summary>
    /// Convert SingleChoice Biz value to MultipleChoice format.
    /// </summary>
    public static List<string>? ToMultiBizValue(string? singleBizValue) =>
        string.IsNullOrEmpty(singleBizValue) ? null : [singleBizValue];

    /// <summary>
    /// Convert legacy options to multi options.
    /// </summary>
    public static MultipleChoicePropertyOptions? ToMultiOptions(SingleChoicePropertyOptions? singleOptions)
    {
        if (singleOptions == null) return null;
        return new MultipleChoicePropertyOptions
        {
            Choices = singleOptions.Choices,
            DefaultValue = string.IsNullOrEmpty(singleOptions.DefaultValue)
                ? null
                : [singleOptions.DefaultValue]
        };
    }

    #endregion

    #region Read Operations: Multi → Single

    /// <summary>
    /// Convert MultipleChoice DB value to SingleChoice format for backward compatibility.
    /// Returns only the first value.
    /// </summary>
    public static string? ToSingleDbValue(List<string>? multiDbValue) =>
        multiDbValue?.FirstOrDefault();

    /// <summary>
    /// Convert MultipleChoice DB value to SingleChoice format.
    /// Also handles legacy string format for migration.
    /// </summary>
    public static string? ToSingleDbValue(object? dbValue) =>
        dbValue switch
        {
            string single => single, // Already single (legacy data)
            List<string> multi => multi.FirstOrDefault(),
            _ => null
        };

    /// <summary>
    /// Convert MultipleChoice Biz value to SingleChoice format.
    /// Returns only the first value.
    /// </summary>
    public static string? ToSingleBizValue(List<string>? multiBizValue) =>
        multiBizValue?.FirstOrDefault();

    /// <summary>
    /// Convert multi options to single options.
    /// </summary>
    public static SingleChoicePropertyOptions? ToSingleOptions(MultipleChoicePropertyOptions? multiOptions)
    {
        if (multiOptions == null) return null;
        return new SingleChoicePropertyOptions
        {
            Choices = multiOptions.Choices,
            DefaultValue = multiOptions.DefaultValue?.FirstOrDefault()
        };
    }

    #endregion

    #region Unified Read (handles both formats)

    /// <summary>
    /// Read DB value in normalized format (always returns List&lt;string&gt; or null).
    /// Handles both legacy string and new list formats.
    /// </summary>
    public static List<string>? ReadAsMulti(object? dbValue) =>
        dbValue switch
        {
            string single when !string.IsNullOrEmpty(single) => [single],
            List<string> multi => multi.Count > 0 ? multi : null,
            _ => null
        };

    /// <summary>
    /// Read DB value as single (for legacy API compatibility).
    /// </summary>
    public static string? ReadAsSingle(object? dbValue) =>
        dbValue switch
        {
            string single => single,
            List<string> multi => multi.FirstOrDefault(),
            _ => null
        };

    #endregion

    #region Property Redirection

    /// <summary>
    /// Get the actual property to use for operations.
    /// Legacy property → Current property
    /// </summary>
    public static Bakabase.Abstractions.Models.Domain.Property GetActualProperty(PropertyPool pool, int id) =>
        IsLegacyProperty(pool, id) ? CurrentProperty : PropertyInternals.BuiltinPropertyMap[(ResourceProperty)id];

    /// <summary>
    /// Get the actual property ID to use for storage.
    /// </summary>
    public static int GetActualPropertyId(PropertyPool pool, int id) =>
        IsLegacyProperty(pool, id) ? CurrentPropertyId : id;

    #endregion
}
