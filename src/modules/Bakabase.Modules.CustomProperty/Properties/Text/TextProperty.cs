using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Text;

public record TextProperty : Abstractions.Models.Domain.CustomProperty;

public record TextPropertyValue: CustomPropertyValueDto<string>
{
    protected override bool IsMatch(string? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new NotImplementedException();
    }
}

public abstract class TextPropertyDescriptor : AbstractCustomPropertyDescriptor<TextProperty, TextPropertyValue, string>
{
    protected override bool IsMatch(string? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new NotImplementedException();
    }
}