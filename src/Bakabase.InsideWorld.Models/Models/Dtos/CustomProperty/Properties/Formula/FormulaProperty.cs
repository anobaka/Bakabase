using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Formula;

public record FormulaProperty(): CustomPropertyDto;

public record FormulaPropertyValue(): CustomPropertyValueDto<string>
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