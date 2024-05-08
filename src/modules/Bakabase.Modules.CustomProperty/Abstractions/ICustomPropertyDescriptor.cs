using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.Modules.CustomProperty.Abstractions
{
    public interface ICustomPropertyDescriptor
    {
        StandardValueType ValueType { get; }
        int Type { get; }
        Modules.CustomProperty.Models.CustomProperty? BuildDomainProperty(Bakabase.Abstractions.Models.Db.CustomProperty? customProperty);
        CustomPropertyValue? BuildDomainValue(Bakabase.Abstractions.Models.Db.CustomPropertyValue? value);
        bool IsMatch(CustomPropertyValue? value, ResourceSearchFilter filter);
        SearchOperation[] SearchOperations { get; }
        object? BuildValueForDisplay(Bakabase.Abstractions.Models.Domain.CustomProperty property, CustomPropertyValue value);
    }
}