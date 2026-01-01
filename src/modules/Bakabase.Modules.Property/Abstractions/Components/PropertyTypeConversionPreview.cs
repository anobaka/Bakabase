using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Abstractions.Components;

/// <summary>
/// Preview of property type conversion.
/// </summary>
public record PropertyTypeConversionPreview(
    /// <summary>Total number of values</summary>
    int TotalCount,
    /// <summary>Source biz value type</summary>
    StandardValueType FromBizType,
    /// <summary>Target biz value type</summary>
    StandardValueType ToBizType,
    /// <summary>List of values that would change (from display, to display)</summary>
    List<PropertyValueChangePreview> Changes);
