using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Boolean;

public record BooleanProperty(): Abstractions.Models.Domain.CustomProperty;

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