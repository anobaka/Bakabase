using Bakabase.Abstractions.Components.Text;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.Abstractions.Components.CustomProperty
{
    public interface ICustomPropertyDescriptor
    {
        StandardValueType ValueType { get; }
        CustomPropertyType Type { get; }
        Models.Domain.CustomProperty? BuildDomainProperty(Models.Db.CustomProperty? customProperty);
        CustomPropertyValue? BuildDomainValue(Models.Db.CustomPropertyValue? value);
        bool IsMatch(CustomPropertyValue? value, ResourceSearchFilter filter);
        SearchOperation[] SearchOperations { get; }
    }
}