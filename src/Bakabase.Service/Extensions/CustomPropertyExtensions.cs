using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Service.Models.View;

namespace Bakabase.Service.Extensions;

public static class CustomPropertyExtensions
{
    public static CustomPropertyViewModel ToViewModel(this CustomProperty property,
        IPropertyLocalizer? propertyLocalizer = null)
    {
        return new CustomPropertyViewModel
        {
            Categories = property.Categories,
            Id = property.Id,
            Name = property.Name,
            Options = property.Options,
            Pool = PropertyPool.Custom,
            PoolName = propertyLocalizer?.PropertyPoolName(PropertyPool.Custom) ?? PropertyPool.Custom.ToString(),
            Type = property.Type,
            TypeName = propertyLocalizer?.PropertyTypeName(property.Type) ?? property.Type.ToString(),
            ValueCount = property.ValueCount
        };
    }
}