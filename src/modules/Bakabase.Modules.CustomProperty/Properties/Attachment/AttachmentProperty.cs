using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Attachment;

public record AttachmentProperty() : Abstractions.Models.Domain.CustomProperty;

public record AttachmentPropertyValue : TypedCustomPropertyValue<string[]>
{
    protected override bool IsMatch(string[]? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new System.NotImplementedException();
    }
}

public class
    AttachmentPropertyDescriptor : AbstractCustomPropertyDescriptor<AttachmentProperty, AttachmentPropertyValue, string
    []>
{
    public override CustomPropertyType Type => CustomPropertyType.Attachment;

    protected override bool IsMatch(string[]? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new System.NotImplementedException();
    }
}