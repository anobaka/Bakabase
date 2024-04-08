using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Formula;

public record FormulaProperty(): Abstractions.Models.Domain.CustomProperty;

public record FormulaPropertyValue(): TypedCustomPropertyValue<string>
{
    protected override bool IsMatch(string? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new System.NotImplementedException();
    }
}

public class FormulaPropertyDescriptor : AbstractCustomPropertyDescriptor<FormulaProperty, FormulaPropertyValue, string>
{
    public override CustomPropertyType Type => CustomPropertyType.Formula;

    protected override bool IsMatch(string? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new System.NotImplementedException();
    }
}