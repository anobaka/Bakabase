using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Attachment;

public record AttachmentProperty() : Models.CustomProperty;
public record AttachmentPropertyValue : CustomPropertyValue<List<string>>;

public class
    AttachmentPropertyDescriptor(IStandardValueHelper standardValueHelper) : AbstractCustomPropertyDescriptor<AttachmentProperty, AttachmentPropertyValue,
    List<string>, List<string>>(standardValueHelper)
{
    public override CustomPropertyType EnumType => CustomPropertyType.Attachment;

    public override SearchOperation[] SearchOperations { get; } = [SearchOperation.IsNotNull, SearchOperation.IsNull];
    protected override bool IsMatch(List<string>? value, SearchOperation operation, object? filterValue)
    {
        return operation switch
        {
            SearchOperation.IsNull => value?.Any() != true,
            SearchOperation.IsNotNull => value?.Any() == true,
            _ => true
        };
    }
}