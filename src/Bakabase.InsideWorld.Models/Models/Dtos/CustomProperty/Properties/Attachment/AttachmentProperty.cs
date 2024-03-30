using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Attachment;

public record AttachmentProperty() : CustomPropertyDto;

public record AttachmentPropertyValue : CustomPropertyValueDto<string[]>
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