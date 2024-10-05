using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Microsoft.Extensions.Localization;

namespace Bakabase.Modules.Property.Components;

internal class PropertyLocalizer(IStringLocalizer<PropertyResource> localizer) : IPropertyLocalizer
{
    public string DescriptorNotDefined(PropertyType propertyType)
    {
        return localizer[nameof(DescriptorNotDefined), PropertyTypeName(propertyType)];
    }

    public string BuiltinPropertyName(ResourceProperty property)
    {
        return localizer[$"{nameof(BuiltinPropertyName)}_{property}"];
    }

    public string DescriptorIsNotFound(PropertyPool pool, int propertyId)
    {
        return localizer[nameof(DescriptorIsNotFound), PropertyPoolName(pool), propertyId];
    }

    public string PropertyTypeName(PropertyType type)
    {
        return localizer[$"{nameof(PropertyTypeName)}_{type}"];
    }

    public string PropertyPoolName(PropertyPool pool)
    {
        return localizer[$"{nameof(PropertyPoolName)}_{pool}"];
    }
}