using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Service.Models.View;

namespace Bakabase.Service.Extensions;

public static class ResourceExtensions
{
    public static ResourceDiffViewModel ToViewModel(this ResourceDiff dbModel, Property property, IPropertyLocalizer? propertyLocalizer = null)
    {
        return new ResourceDiffViewModel
        {
            Property = property.ToViewModel(propertyLocalizer),
            Value1 = dbModel.SerializedValue1,
            Value2 = dbModel.SerializedValue1
        };
    }
}