using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.Property.Abstractions.Components;

public interface IPropertyLocalizer
{
    string DescriptorNotDefined(PropertyType propertyType);
    string BuiltinPropertyName(ResourceProperty property);
    string DescriptorIsNotFound(PropertyPool pool, int propertyId);
    string PropertyTypeName(PropertyType type);
    string PropertyPoolName(PropertyPool  pool);
}