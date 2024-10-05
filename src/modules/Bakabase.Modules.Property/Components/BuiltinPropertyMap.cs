using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Abstractions.Components;

namespace Bakabase.Modules.Property.Components;

public class BuiltinPropertyMap : Dictionary<ResourceProperty, Bakabase.Abstractions.Models.Domain.Property>
{
    public BuiltinPropertyMap(IPropertyLocalizer propertyLocalizer)
    {
        foreach (var (type, property) in PropertyInternals.BuiltinPropertyMap)
        {
            Add(type, property with {Name = propertyLocalizer.BuiltinPropertyName(type)});
        }
    }
}