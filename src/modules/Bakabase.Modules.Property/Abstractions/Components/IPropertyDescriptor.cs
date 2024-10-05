using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Db;

namespace Bakabase.Modules.Property.Abstractions.Components
{
    public interface IPropertyDescriptor
    {
        StandardValueType DbValueType { get; }
        StandardValueType BizValueType { get; }

        PropertyType Type { get; }
        // Bakabase.Abstractions.Models.Domain.Property ToDomainModel(CustomPropertyDbModel customProperty);
        // CustomPropertyValue ToDomainModel(CustomPropertyValueDbModel value);
        object? GetBizValue(Bakabase.Abstractions.Models.Domain.Property property, object? dbValue);

        (object? DbValue, bool PropertyChanged) PrepareDbValue(Bakabase.Abstractions.Models.Domain.Property property,
            object? bizValue);

        object? InitializeOptions() => null;
        Type? OptionsType => null;
    }
}