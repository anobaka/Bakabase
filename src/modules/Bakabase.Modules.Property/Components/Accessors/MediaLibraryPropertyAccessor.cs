using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Components.BuiltinProperty;
using Bakabase.Modules.Property.Components.Properties.Choice;

namespace Bakabase.Modules.Property.Components.Accessors;

/// <summary>
/// Specialized accessor for MediaLibraryV2Multi property.
/// Provides int-based API since media library IDs are integers.
/// </summary>
public class MediaLibraryPropertyAccessor
{
    public ResourceProperty ResourceProperty { get; }
    public Bakabase.Abstractions.Models.Domain.Property Definition => BuiltinProperties.Get(ResourceProperty);
    public PropertyType Type => Definition.Type;
    public int Id => Definition.Id;
    public PropertyPool Pool => Definition.Pool;

    internal MediaLibraryPropertyAccessor(ResourceProperty prop)
    {
        ResourceProperty = prop;
    }

    /// <summary>
    /// Get BizValue from DbValue
    /// </summary>
    public List<string>? GetBizValue(object? dbValue) =>
        PropertySystem.Property.ToBizValue<List<string>>(Definition, dbValue);

    // === DbValue - Build from int IDs ===

    /// <summary>
    /// Build DbValue from media library IDs.
    /// </summary>
    /// <param name="libraryIds">Media library IDs</param>
    /// <returns>List of ID strings for storage</returns>
    public List<string>? BuildDbValue(IEnumerable<int>? libraryIds)
    {
        if (libraryIds == null) return null;
        var result = libraryIds.Select(id => id.ToString()).ToList();
        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Build DbValue from media library IDs.
    /// </summary>
    public List<string>? BuildDbValue(params int[] libraryIds) =>
        BuildDbValue(libraryIds.AsEnumerable());

    /// <summary>
    /// Build serialized DbValue from media library IDs.
    /// </summary>
    public string? BuildDbValueSerialized(IEnumerable<int>? libraryIds) =>
        PropertyValueFactory.MultipleChoice.BuildDbValueSerialized(
            libraryIds?.Select(id => id.ToString()));

    /// <summary>
    /// Build serialized DbValue from media library IDs.
    /// </summary>
    public string? BuildDbValueSerialized(params int[] libraryIds) =>
        BuildDbValueSerialized(libraryIds.AsEnumerable());

    // === DbValue - Build from string IDs (for compatibility) ===

    /// <summary>
    /// Build DbValue from string IDs.
    /// Prefer using the int overload for type safety.
    /// </summary>
    public List<string>? BuildDbValue(IEnumerable<string>? values) =>
        PropertyValueFactory.MultipleChoice.BuildDbValue(values);

    /// <summary>
    /// Build serialized DbValue from string IDs.
    /// </summary>
    public string? BuildDbValueSerialized(IEnumerable<string>? values) =>
        PropertyValueFactory.MultipleChoice.BuildDbValueSerialized(values);

    // === BizValue ===

    /// <summary>
    /// Build BizValue (library names) directly.
    /// Note: BizValue for this property is typically library names, not IDs.
    /// </summary>
    public List<string>? BuildBizValue(IEnumerable<string>? libraryNames) =>
        PropertyValueFactory.MultipleChoice.BuildBizValue(libraryNames);

    /// <summary>
    /// Build serialized BizValue.
    /// </summary>
    public string? BuildBizValueSerialized(IEnumerable<string>? libraryNames) =>
        PropertyValueFactory.MultipleChoice.BuildBizValueSerialized(libraryNames);

    // === Match (requires options) ===

    /// <summary>
    /// Match library names to options and return DbValue (list of IDs).
    /// </summary>
    public List<string>? MatchDbValue(MultipleChoicePropertyOptions? options, IEnumerable<string>? libraryNames, bool addOnMiss = false) =>
        PropertyValueFactory.MultipleChoice.MatchDbValue(options, libraryNames, addOnMiss);

    /// <summary>
    /// Match library names to options and return serialized DbValue.
    /// </summary>
    public string? MatchDbValueSerialized(MultipleChoicePropertyOptions? options, IEnumerable<string>? libraryNames, bool addOnMiss = false) =>
        PropertyValueFactory.MultipleChoice.MatchDbValueSerialized(options, libraryNames, addOnMiss);

    /// <summary>
    /// Match DbValues (IDs) to options and return BizValue (library names).
    /// </summary>
    public List<string>? MatchBizValue(MultipleChoicePropertyOptions? options, List<string>? dbValues) =>
        PropertyValueFactory.MultipleChoice.MatchBizValue(options, dbValues);

    /// <summary>
    /// Match DbValues to options and return serialized BizValue.
    /// </summary>
    public string? MatchBizValueSerialized(MultipleChoicePropertyOptions? options, List<string>? dbValues) =>
        PropertyValueFactory.MultipleChoice.MatchBizValueSerialized(options, dbValues);

    // === Search Filter Values ===

    /// <summary>
    /// Build filter value for SearchOperation.In.
    /// Returns the DbValue (List&lt;string&gt;) to use as filter value.
    /// </summary>
    /// <param name="libraryIds">Media library IDs to filter by</param>
    /// <returns>Filter value as List&lt;string&gt; (library IDs as strings)</returns>
    public List<string>? BuildInFilterValue(IEnumerable<int>? libraryIds) =>
        BuildDbValue(libraryIds);

    /// <summary>
    /// Build filter value for SearchOperation.In.
    /// Returns the DbValue (List&lt;string&gt;) to use as filter value.
    /// </summary>
    public List<string>? BuildInFilterValue(params int[] libraryIds) =>
        BuildDbValue(libraryIds);

    /// <summary>
    /// Build serialized filter value for SearchOperation.In.
    /// Returns the serialized string ready for use in ResourceSearchFilterDbModel.Value.
    /// </summary>
    /// <param name="libraryIds">Media library IDs to filter by</param>
    /// <returns>Serialized filter value</returns>
    public string? BuildInFilterValueSerialized(IEnumerable<int>? libraryIds) =>
        BuildDbValueSerialized(libraryIds);

    /// <summary>
    /// Build serialized filter value for SearchOperation.In.
    /// Returns the serialized string ready for use in ResourceSearchFilterDbModel.Value.
    /// </summary>
    public string? BuildInFilterValueSerialized(params int[] libraryIds) =>
        BuildDbValueSerialized(libraryIds);

    // === Utility ===

    /// <summary>
    /// Parse DbValue strings back to library IDs.
    /// </summary>
    public List<int>? ParseLibraryIds(List<string>? dbValues)
    {
        if (dbValues == null || dbValues.Count == 0) return null;
        var result = new List<int>();
        foreach (var v in dbValues)
        {
            if (int.TryParse(v, out var id))
            {
                result.Add(id);
            }
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Parse DbValue (object) to typed List&lt;string&gt;.
    /// Use this when you have a raw DbValue from filter or other sources.
    /// </summary>
    /// <param name="dbValue">The raw DbValue (object?)</param>
    /// <returns>Typed List&lt;string&gt; or null</returns>
    public List<string>? ParseDbValue(object? dbValue)
    {
        return dbValue switch
        {
            null => null,
            List<string> list => list,
            IEnumerable<string> enumerable => enumerable.ToList(),
            string s => string.IsNullOrEmpty(s) ? null : [s],
            _ => null
        };
    }

    /// <summary>
    /// Parse DbValue (object) directly to library IDs.
    /// Combines ParseDbValue and ParseLibraryIds for convenience.
    /// </summary>
    /// <param name="dbValue">The raw DbValue (object?)</param>
    /// <returns>List of library IDs or null</returns>
    public List<int>? ParseDbValueAsLibraryIds(object? dbValue)
    {
        var stringList = ParseDbValue(dbValue);
        return ParseLibraryIds(stringList);
    }
}
