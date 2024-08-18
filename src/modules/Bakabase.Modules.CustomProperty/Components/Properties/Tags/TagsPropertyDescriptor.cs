using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Tags;

public class TagsPropertyDescriptor(IStandardValueHelper standardValueHelper)
    : AbstractCustomPropertyDescriptor<TagsProperty, TagsPropertyOptions,
        TagsPropertyValue, List<string>, List<TagValue>>(standardValueHelper)
{
    public override CustomPropertyType EnumType => CustomPropertyType.Tags;

    public override SearchOperation[] SearchOperations =>
    [
        SearchOperation.Contains,
        SearchOperation.NotContains,
        SearchOperation.IsNull,
        SearchOperation.IsNotNull,
        SearchOperation.In,
    ];

    protected override (object DbValue, SearchOperation Operation)? BuildSearchFilterByKeyword(TagsProperty property,
        string keyword)
    {
        var values = property.Options?.Tags?.Where(t => t.Group?.Contains(keyword) == true || t.Name.Contains(keyword))
            .Select(t => t.Value).ToHashSet();
        return values?.Any() == true ? (values, SearchOperation.In) : null;
    }

    protected override bool IsMatch(List<string>? value, SearchOperation operation, object? filterValue)
    {
        switch (operation)
        {
            case SearchOperation.Contains:
            case SearchOperation.NotContains:
            case SearchOperation.In:
            {
                var typedTarget = filterValue as List<string>;
                if (typedTarget?.Any() != true)
                {
                    return true;
                }

                if (value?.Any() != true)
                {
                    return false;
                }

                return operation switch
                {
                    SearchOperation.Contains => typedTarget.All(value.Contains),
                    SearchOperation.NotContains => !typedTarget.Any(value.Contains),
                    SearchOperation.In => typedTarget.Any(value.Contains),
                    _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
                };

                break;
            }
            case SearchOperation.IsNull:
                return value?.Any() != true;
            case SearchOperation.IsNotNull:
                return value?.Any() == true;
        }

        return true;
    }
}