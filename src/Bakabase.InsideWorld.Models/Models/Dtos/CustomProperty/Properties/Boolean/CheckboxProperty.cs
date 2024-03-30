using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Boolean;

public record BooleanProperty(): CustomPropertyDto;

public record BooleanPropertyValue : CustomPropertyValueDto<bool>
{
    protected override bool IsMatch(bool value, CustomPropertyValueSearchRequestModel model)
    {
        throw new System.NotImplementedException();
    }
}

public class BooleanPropertyDescriptor : AbstractCustomPropertyDescriptor<BooleanProperty, BooleanPropertyValue, bool>
{
    public override CustomPropertyType Type => CustomPropertyType.Boolean;

    protected override bool IsMatch(bool value, CustomPropertyValueSearchRequestModel model)
    {
        throw new System.NotImplementedException();
    }
}