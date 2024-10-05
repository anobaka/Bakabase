using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Service.Models.View;

namespace Bakabase.Service.Extensions
{
    public static class PropertyExtensions
    {
        public static PropertyViewModel ToViewModel(this Property property,
            IPropertyLocalizer? propertyLocalizer = null)
        {
            return new PropertyViewModel
            {
                Id = property.Id,
                Name = property.Name,
                Options = property.Options,
                Pool = property.Pool,
                PoolName = propertyLocalizer?.PropertyPoolName(property.Pool) ?? property.Pool.ToString(),
                Type = property.Type,
                TypeName = propertyLocalizer?.PropertyTypeName(property.Type) ?? property.Type.ToString()
            };
        }
    }
}