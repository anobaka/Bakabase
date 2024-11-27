using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.Search.Models.Db;
using Bakabase.Modules.StandardValue.Extensions;

namespace Bakabase.Modules.Search.Extensions;

public static class ResourceSearchExtensions
{
    public static ResourceSearchFilterDbModel ToDbModel(this ResourceSearchFilter model)
    {
        return new ResourceSearchFilterDbModel
        {
            Value = model.DbValue?.SerializeAsStandardValue(model.Property.Type.GetDbValueType()),
            Operation = model.Operation,
            PropertyId = model.PropertyId,
            PropertyPool = model.PropertyPool,
            Disabled = model.Disabled
        };
    }

    public static ResourceSearchFilterGroupDbModel ToDbModel(this ResourceSearchFilterGroup model)
    {
        return new ResourceSearchFilterGroupDbModel
        {
            Filters = model.Filters?.Select(f => f.ToDbModel()).ToList(),
            Groups = model.Groups?.Select(g => g.ToDbModel()).ToList(),
            Combinator = model.Combinator,
            Disabled = model.Disabled
        };
    }

    public static bool IsValid(this ResourceSearchFilterDbModel filter)
    {
        return filter is {PropertyId: not null, PropertyPool: not null, Operation: not null};
    }

    public static ResourceSearchFilterGroup? ToDomainModel(this ResourceSearchFilterGroupDbModel group,
        Dictionary<PropertyPool, Dictionary<int, Abstractions.Models.Domain.Property>> propertyMap)
    {
        var filters = group.Filters?.Select(f =>
        {
            if (!f.IsValid())
            {
                return null;
            }

            var property = propertyMap.GetValueOrDefault(f.PropertyPool!.Value)
                ?.GetValueOrDefault(f.PropertyId!.Value);
            if (property == null)
            {
                return null;
            }

            return new ResourceSearchFilter
            {
                DbValue = f.Value?.DeserializeAsStandardValue(property.Type.GetDbValueType()),
                Operation = f.Operation!.Value,
                Property = property,
                PropertyId = f.PropertyId!.Value,
                PropertyPool = f.PropertyPool!.Value,
                Disabled = f.Disabled
            };
        }).OfType<ResourceSearchFilter>().ToList();

        var groups = group.Groups?.Select(g => g.ToDomainModel(propertyMap)).OfType<ResourceSearchFilterGroup>()
            .ToList();

        if (filters?.Any() == true || groups?.Any() == true)
        {
            return new ResourceSearchFilterGroup
            {
                Combinator = group.Combinator,
                Filters = filters,
                Groups = groups,
                Disabled = group.Disabled

            };
        }

        return null;
    }
}