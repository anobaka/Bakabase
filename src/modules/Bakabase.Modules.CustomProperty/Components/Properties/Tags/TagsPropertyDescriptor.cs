using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Tags;

public class TagsPropertyDescriptor : AbstractCustomPropertyDescriptor<TagsProperty, TagsPropertyOptions,
    TagsPropertyValue, List<string>, List<TagValue>>
{
    public override CustomPropertyType EnumType => CustomPropertyType.Tags;

    public override SearchOperation[] SearchOperations =>
    [
        SearchOperation.Contains,
        SearchOperation.NotContains,
        SearchOperation.IsNull,
        SearchOperation.IsNotNull,
        SearchOperation.In,
        SearchOperation.NotIn
    ];

    protected override bool IsMatch(List<string>? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new NotImplementedException();
    }
}