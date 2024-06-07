using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.Modules.CustomProperty.Abstractions.Components
{
    public interface ICustomPropertyDescriptor
    {
        StandardValueType DbValueType { get; }
        StandardValueType BizValueType { get; }
        int Type { get; }

        Models.CustomProperty? ToDomainModel(Bakabase.Abstractions.Models.Db.CustomProperty? customProperty);

        CustomPropertyValue? ToDomainModel(Bakabase.Abstractions.Models.Db.CustomPropertyValue? value);

        object? ConvertDbValueToBizValue(Bakabase.Abstractions.Models.Domain.CustomProperty property, object? dbValue);

        (object? DbValue, bool PropertyChanged) PrepareDbValueFromBizValue(
            Bakabase.Abstractions.Models.Domain.CustomProperty property, object? bizValue);

        bool IsMatch(CustomPropertyValue? value, ResourceSearchFilter filter);
        SearchOperation[] SearchOperations { get; }
    }
}