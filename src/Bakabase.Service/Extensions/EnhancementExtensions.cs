using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Service.Models.View;

namespace Bakabase.Service.Extensions;

public static class EnhancementExtensions
{
    public static EnhancementViewModel ToViewModel(this Enhancement enhancement)
    {
        return new EnhancementViewModel
        {
            Id = enhancement.Id,
            DynamicTarget = enhancement.DynamicTarget,
            EnhancerId = enhancement.EnhancerId,
            PropertyId = enhancement.PropertyId,
            PropertyPool = enhancement.PropertyPool,
            ResourceId = enhancement.ResourceId,
            Target = enhancement.Target,
            Value = enhancement.Value,
            ValueType = enhancement.ValueType,
            ReservedPropertyValue = enhancement.ReservedPropertyValue,
            CustomPropertyValue = enhancement.CustomPropertyValue,
            Property = enhancement.Property?.ToViewModel()
        };
    }
}