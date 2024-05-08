using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Attachment;

public record AttachmentProperty() : Models.CustomProperty;
public record AttachmentPropertyValue : CustomPropertyValue<List<string>>;

public class
    AttachmentPropertyDescriptor : AbstractCustomPropertyDescriptor<AttachmentProperty, AttachmentPropertyValue,
    List<string>>
{
    public override CustomPropertyType EnumType => CustomPropertyType.Attachment;

    public override SearchOperation[] SearchOperations { get; } = [SearchOperation.IsNotNull, SearchOperation.IsNull];

    protected override bool IsMatch(List<string>? value, CustomPropertyValueSearchRequestModel model)
    {
        return model.Operation switch
        {
            SearchOperation.IsNull => value?.Any() != true,
            SearchOperation.IsNotNull => value?.Any() == true,
            _ => true
        };
    }
}