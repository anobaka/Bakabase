using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.StandardValue.Abstractions.Components;

/// <summary>
/// This is a *very* abstract layer to define the abilities of a base value in global scope.
/// </summary>
public interface IStandardValueHandler
{
    StandardValueType Type { get; }
    Dictionary<StandardValueType, StandardValueConversionRule> ConversionRules { get; }
    object? Convert(object? currentValue, StandardValueType toType);
    object? Optimize(object? value);
    bool ValidateType(object? value);
    Type ExpectedType { get; }
    string? BuildDisplayValue(object? value);
    public List<string>? ExtractTextsForConvertingToDateTime(object optimizedValue) => null;
    bool Compare(object? a, object? b);

    /// <summary>
    /// Combines multiple values into a single value.
    /// This is NOT string concatenation - it determines how to resolve multiple property values into one.
    /// For collection types (ListString, ListTag, etc.): aggregates all values (union).
    /// For single-value types (String, Decimal, etc.): returns the first non-null value.
    /// </summary>
    /// <param name="values">Multiple values to combine, may contain nulls.</param>
    /// <returns>The combined value, or null if all inputs are null.</returns>
    object? Combine(IEnumerable<object?> values);
}