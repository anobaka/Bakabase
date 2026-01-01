using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Components.BuiltinProperty;
using Bakabase.Modules.Property.Components.Properties.Choice;

namespace Bakabase.Modules.Property.Components.Accessors;

/// <summary>
/// Specialized accessor for ParentResource property.
/// Provides int-based API since resource IDs are integers.
/// </summary>
public class ParentResourcePropertyAccessor
{
    public ResourceProperty ResourceProperty { get; }
    public Bakabase.Abstractions.Models.Domain.Property Definition => BuiltinProperties.Get(ResourceProperty);
    public PropertyType Type => Definition.Type;
    public int Id => Definition.Id;
    public PropertyPool Pool => Definition.Pool;

    internal ParentResourcePropertyAccessor(ResourceProperty prop)
    {
        ResourceProperty = prop;
    }

    /// <summary>
    /// Get BizValue from DbValue
    /// </summary>
    public string? GetBizValue(object? dbValue) =>
        PropertySystem.Property.ToBizValue<string>(Definition, dbValue);

    // === DbValue - Build from int ID ===

    /// <summary>
    /// Build DbValue from parent resource ID.
    /// </summary>
    /// <param name="resourceId">Parent resource ID</param>
    /// <returns>ID string for storage</returns>
    public string? BuildDbValue(int? resourceId) =>
        resourceId?.ToString();

    /// <summary>
    /// Build serialized DbValue from parent resource ID.
    /// </summary>
    public string? BuildDbValueSerialized(int? resourceId) =>
        PropertyValueFactory.SingleChoice.BuildDbValueSerialized(resourceId?.ToString());

    // === DbValue - Build from string ID (for compatibility) ===

    /// <summary>
    /// Build DbValue from string ID.
    /// Prefer using the int overload for type safety.
    /// </summary>
    public string? BuildDbValue(string? value) =>
        PropertyValueFactory.SingleChoice.BuildDbValue(value);

    /// <summary>
    /// Build serialized DbValue from string ID.
    /// </summary>
    public string? BuildDbValueSerialized(string? value) =>
        PropertyValueFactory.SingleChoice.BuildDbValueSerialized(value);

    // === BizValue ===

    /// <summary>
    /// Build BizValue (resource name) directly.
    /// </summary>
    public string? BuildBizValue(string? resourceName) =>
        PropertyValueFactory.SingleChoice.BuildBizValue(resourceName);

    /// <summary>
    /// Build serialized BizValue.
    /// </summary>
    public string? BuildBizValueSerialized(string? resourceName) =>
        PropertyValueFactory.SingleChoice.BuildBizValueSerialized(resourceName);

    // === Match (requires options) ===

    /// <summary>
    /// Match resource name to options and return DbValue (ID).
    /// </summary>
    public string? MatchDbValue(SingleChoicePropertyOptions? options, string? resourceName, bool addOnMiss = false) =>
        PropertyValueFactory.SingleChoice.MatchDbValue(options, resourceName, addOnMiss);

    /// <summary>
    /// Match resource name to options and return serialized DbValue.
    /// </summary>
    public string? MatchDbValueSerialized(SingleChoicePropertyOptions? options, string? resourceName, bool addOnMiss = false) =>
        PropertyValueFactory.SingleChoice.MatchDbValueSerialized(options, resourceName, addOnMiss);

    /// <summary>
    /// Match DbValue (ID) to options and return BizValue (resource name).
    /// </summary>
    public string? MatchBizValue(SingleChoicePropertyOptions? options, string? dbValue) =>
        PropertyValueFactory.SingleChoice.MatchBizValue(options, dbValue);

    /// <summary>
    /// Match DbValue to options and return serialized BizValue.
    /// </summary>
    public string? MatchBizValueSerialized(SingleChoicePropertyOptions? options, string? dbValue) =>
        PropertyValueFactory.SingleChoice.MatchBizValueSerialized(options, dbValue);

    // === Utility ===

    /// <summary>
    /// Parse DbValue string back to resource ID.
    /// </summary>
    public int? ParseResourceId(string? dbValue)
    {
        if (string.IsNullOrEmpty(dbValue)) return null;
        return int.TryParse(dbValue, out var id) ? id : null;
    }
}
