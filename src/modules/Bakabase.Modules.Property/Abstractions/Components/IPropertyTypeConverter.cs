using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Abstractions.Components;

/// <summary>
/// Converts property values between different property types.
/// This is an abstraction over CustomPropertyService.ChangeType for general Property usage.
/// </summary>
public interface IPropertyTypeConverter
{
    /// <summary>
    /// Convert a single property value from one type to another.
    /// </summary>
    /// <param name="fromProperty">Source property definition</param>
    /// <param name="toProperty">Target property definition (will be modified if options change)</param>
    /// <param name="dbValue">The DB value to convert</param>
    /// <returns>Conversion result with new DB value and updated property</returns>
    Task<PropertyValueConversionResult> ConvertValueAsync(
        Bakabase.Abstractions.Models.Domain.Property fromProperty,
        Bakabase.Abstractions.Models.Domain.Property toProperty,
        object? dbValue);

    /// <summary>
    /// Preview conversion without modifying data.
    /// </summary>
    /// <param name="fromProperty">Source property definition</param>
    /// <param name="toType">Target property type</param>
    /// <param name="dbValues">The DB values to preview</param>
    /// <returns>Preview of changes that would occur</returns>
    Task<PropertyTypeConversionPreview> PreviewConversionAsync(
        Bakabase.Abstractions.Models.Domain.Property fromProperty,
        PropertyType toType,
        IEnumerable<object?> dbValues);

    /// <summary>
    /// Batch convert multiple values.
    /// </summary>
    /// <param name="fromProperty">Source property definition</param>
    /// <param name="toProperty">Target property definition</param>
    /// <param name="dbValues">The DB values to convert</param>
    /// <returns>List of conversion results</returns>
    Task<PropertyBatchConversionResult> ConvertValuesAsync(
        Bakabase.Abstractions.Models.Domain.Property fromProperty,
        Bakabase.Abstractions.Models.Domain.Property toProperty,
        IEnumerable<object?> dbValues);
}
