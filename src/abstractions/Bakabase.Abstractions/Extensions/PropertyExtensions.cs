using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Extensions;

public static class PropertyExtensions
{
    public static Property ToProperty(this CustomProperty property) => new(ResourcePropertyType.Custom, property.Id,
        property.DbValueType, property.BizValueType, property.Type, property.Name, property.Options);
}