using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.Search.Models.Db;
using Bakabase.Modules.StandardValue.Extensions;

namespace Bakabase.Modules.Search.Extensions;

public static class ResourceSearchExtensions
{
    /// <summary>
    /// Convert ResourceSearch domain model to ResourceSearchDbModel
    /// </summary>
    public static ResourceSearchDbModel ToDbModel(this ResourceSearch model)
    {
        return new ResourceSearchDbModel
        {
            Group = model.Group?.ToDbModel(),
            Keyword = null, // Keyword is handled separately during search
            Tags = model.Tags,
            Page = model.PageIndex,
            PageSize = model.PageSize,
            Orders = null // Orders are not stored
        };
    }

    /// <summary>
    /// Convert ResourceSearchDbModel to ResourceSearch domain model
    /// </summary>
    public static async Task<ResourceSearch> ToDomainModel(
        this ResourceSearchDbModel dbModel,
        IPropertyService propertyService)
    {
        Dictionary<PropertyPool, Dictionary<int, Abstractions.Models.Domain.Property>>? propertyMap = null;

        if (dbModel.Group != null)
        {
            var validFilters = dbModel.Group.ExtractFilters().Where(f => f.IsValid()).ToList();
            var propertyPools = validFilters.Aggregate<ResourceSearchFilterDbModel, PropertyPool>(
                default, (current, f) => current | f.PropertyPool!.Value);

            if (propertyPools != default)
            {
                propertyMap = (await propertyService.GetProperties(propertyPools))
                    .GroupBy(d => d.Pool)
                    .ToDictionary(d => d.Key, d => d.ToDictionary(a => a.Id, a => a));
            }
        }

        return dbModel.ToDomainModel(propertyMap);
    }

    /// <summary>
    /// Convert ResourceSearchDbModel to ResourceSearch domain model with pre-fetched property map
    /// </summary>
    public static ResourceSearch ToDomainModel(
        this ResourceSearchDbModel dbModel,
        Dictionary<PropertyPool, Dictionary<int, Abstractions.Models.Domain.Property>>? propertyMap)
    {
        ResourceSearchFilterGroup? group = null;

        if (dbModel.Group != null && propertyMap != null)
        {
            group = dbModel.Group.ToDomainModel(propertyMap);
        }

        return new ResourceSearch
        {
            Group = group,
            Tags = dbModel.Tags,
            PageIndex = dbModel.Page,
            PageSize = dbModel.PageSize
        };
    }

    public static ResourceSearchFilterDbModel ToDbModel(this ResourceSearchFilter model)
    {
        // Use the operation-aware property type for serialization.
        // e.g., SingleChoice with In operation needs MultipleChoice value type (List<string>),
        // not SingleChoice value type (string).
        var psh = PropertySystem.Property.TryGetSearchHandler(model.Property.Type);
        var asType = psh?.SearchOperations.GetValueOrDefault(model.Operation)?.AsType ?? model.Property.Type;

        return new ResourceSearchFilterDbModel
        {
            Value = model.DbValue?.SerializeAsStandardValue(asType.GetDbValueType()),
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

            // Use the operation-aware property type for deserialization.
            // e.g., SingleChoice with In operation stores MultipleChoice value type (List<string>).
            var psh = PropertySystem.Property.TryGetSearchHandler(property.Type);
            var asType = psh?.SearchOperations.GetValueOrDefault(f.Operation!.Value)?.AsType ?? property.Type;

            return new ResourceSearchFilter
            {
                DbValue = f.Value?.DeserializeAsStandardValue(asType.GetDbValueType()),
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