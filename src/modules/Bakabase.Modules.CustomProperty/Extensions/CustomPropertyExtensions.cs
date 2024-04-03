using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Properties.Choice;

namespace Bakabase.Modules.CustomProperty.Extensions;

public static class CustomPropertyExtensions
{
    public static Dictionary<CustomPropertyType, ICustomPropertyDescriptor> Descriptors = new()
    {
        {
            CustomPropertyType.SingleChoice, new SingleChoicePropertyDescriptor()
        }
    };

    public static Abstractions.Models.Domain.CustomProperty? ToDomainModel(
        this Abstractions.Models.Db.CustomProperty? entity)
    {
        if (entity == null)
        {
            return null;
        }

        return Descriptors[entity.Type].BuildPropertyDto(entity);
    }
}