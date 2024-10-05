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
}