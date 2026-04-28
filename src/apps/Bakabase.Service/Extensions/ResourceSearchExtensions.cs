using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.Search.Models.Db;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Extensions;

namespace Bakabase.Service.Extensions;

public static class ResourceSearchExtensions
{
    /// <summary>
    /// Simple filter data for ParentResource population
    /// </summary>
    public record FilterData(PropertyPool PropertyPool, int PropertyId, string? DbValue, SearchOperation? Operation);

    /// <summary>
    /// Builds a ParentResource property with choices based on resource IDs found in filters.
    /// Returns null if no ParentResource filters found or no valid resource IDs.
    /// </summary>
    public static async Task<Property?> BuildParentResourcePropertyWithChoices(
        this IEnumerable<FilterData> filters,
        Property parentResourceProperty,
        IResourceService resourceService)
    {
        var parentResourceFilters = filters
            .Where(f => f.PropertyPool == PropertyPool.Internal &&
                       f.PropertyId == (int)InternalProperty.ParentResource &&
                       !string.IsNullOrEmpty(f.DbValue) &&
                       f.Operation.HasValue)
            .ToList();

        if (!parentResourceFilters.Any())
        {
            return null;
        }

        var resourceIds = new HashSet<int>();
        foreach (var filter in parentResourceFilters)
        {
            // Get value property type dynamically based on operation
            var valueProperty = parentResourceProperty.ConvertPropertyIfNecessary(filter.Operation!.Value);
            var dbValueType = valueProperty.Type.GetDbValueType();

            // Deserialize based on the actual value type
            var dbValue = filter.DbValue.DeserializeAsStandardValue(dbValueType);

            if (dbValue is List<string> idList)
            {
                foreach (var id in idList)
                {
                    if (int.TryParse(id, out var resourceId))
                    {
                        resourceIds.Add(resourceId);
                    }
                }
            }
            else if (dbValue is string idStr)
            {
                if (int.TryParse(idStr, out var resourceId))
                {
                    resourceIds.Add(resourceId);
                }
            }
        }

        if (!resourceIds.Any())
        {
            return null;
        }

        var resources = await resourceService.GetByKeys(resourceIds.ToArray(), ResourceAdditionalItem.DisplayName);
        var choices = resources.Select(r => new ChoiceOptions
        {
            Value = r.Id.ToString(),
            Label = r.DisplayName ?? r.FileName ?? $"Resource {r.Id}"
        }).ToList();

        return parentResourceProperty with
        {
            Options = new SingleChoicePropertyOptions { Choices = choices }
        };
    }

    public static ResourceSearchFilterDbModel ToDbModel(this ResourceSearchFilterInputModel model)
    {
        return new ResourceSearchFilterDbModel
        {
            Value = model.DbValue,
            Operation = model.Operation,
            PropertyId = model.PropertyId,
            PropertyPool = model.PropertyPool,
            Disabled = model.Disabled
        };
    }

    public static ResourceSearchFilterGroupDbModel ToDbModel(this ResourceSearchFilterGroupInputModel model)
    {
        return new ResourceSearchFilterGroupDbModel
        {
            Filters = model.Filters?.Select(f => f.ToDbModel()).ToList(),
            Groups = model.Groups?.Select(g => g.ToDbModel()).ToList(),
            Combinator = model.Combinator,
            Disabled = model.Disabled
        };
    }

    public static ResourceSearchDbModel ToDbModel(this ResourceSearchInputModel model)
    {
        return new ResourceSearchDbModel
        {
            Group = model.Group?.ToDbModel(),
            Keyword = model.Keyword,
            Orders = model.Orders,
            Page = model.Page,
            PageSize = model.PageSize,
            Tags = model.Tags
        };
    }

    private static bool IsValid(this ResourceSearchFilterInputModel filter)
    {
        return filter is {PropertyId: not null, PropertyPool: not null, Operation: not null};
    }

    public static ResourceSearchFilterGroup? ToDomainModel(this ResourceSearchFilterGroupInputModel group,
        Dictionary<PropertyPool, Dictionary<int, Property>> propertyMap)
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

            // The value may use a different property type than the property itself
            var valueProperty = property;
            var ph = PropertySystem.Property.GetSearchHandler(property.Type);
            var conversion = ph.SearchOperations.GetValueOrDefault(f.Operation!.Value);
            if (conversion?.ConvertProperty != null)
            {
                valueProperty = conversion.ConvertProperty(property);
            }

            return new ResourceSearchFilter
            {
                DbValue = f.DbValue?.DeserializeAsStandardValue(valueProperty.Type.GetDbValueType()),
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

    public static async Task<ResourceSearch> ToDomainModel(this ResourceSearchInputModel model,
        IPropertyService propertyService)
    {
        var validFilters = model.Group?.ExtractFilters().Where(f => f.IsValid()) ?? [];
        var propertyPools =
            validFilters.Aggregate<ResourceSearchFilterInputModel, PropertyPool>(default,
                (current, f) => current | f.PropertyPool!.Value);
        if (model.Keyword.IsNotEmpty())
        {
            propertyPools |= PropertyPool.Custom | PropertyPool.Internal;
        }

        var propertyMap = (await propertyService.GetProperties(propertyPools)).GroupBy(d => d.Pool)
            .ToDictionary(d => d.Key, d => d.ToDictionary(a => a.Id, a => a));

        var domainModel = new ResourceSearch
        {
            Group = model.Group?.ToDomainModel(propertyMap),
            Orders = model.Orders,
            PageIndex = model.Page,
            PageSize = model.PageSize,
            Tags = model.Tags
        };

        if (!string.IsNullOrEmpty(model.Keyword))
        {
            var newGroup = new ResourceSearchFilterGroup
            {
                Combinator = SearchCombinator.Or,
                Filters =
                [
                    new ResourceSearchFilter
                    {
                        DbValue = model.Keyword.SerializeAsStandardValue(StandardValueType.String),
                        Operation = SearchOperation.Contains,
                        PropertyPool = PropertyPool.Internal,
                        PropertyId = (int) ResourceProperty.Filename,
                        Property = propertyMap[PropertyPool.Internal][(int) ResourceProperty.Filename]
                    }
                ]
            };

            foreach (var (pId, p) in propertyMap[PropertyPool.Custom])
            {
                var pd = PropertySystem.Property.TryGetSearchHandler(p.Type);
                if (pd != null)
                {
                    var filter = pd.BuildSearchFilterByKeyword(p, model.Keyword);
                    if (filter != null)
                    {
                        newGroup.Filters.Add(filter);
                    }
                }
            }

            if (domainModel.Group == null)
            {
                domainModel.Group = newGroup;
            }
            else
            {
                domainModel.Group = new ResourceSearchFilterGroup
                {
                    Combinator = SearchCombinator.And,
                    Groups = [domainModel.Group, newGroup]
                };
            }
        }

        return domainModel;
    }

    public static ResourceSearchFilterViewModel ToViewModel(this ResourceSearchFilterDbModel model, Property? property,
        IPropertyLocalizer propertyLocalizer)
    {
        var filter = new ResourceSearchFilterViewModel
        {
            PropertyId = model.PropertyId,
            PropertyPool = model.PropertyPool,
            Operation = model.Operation,
            DbValue = model.Value,
            Disabled = model.Disabled
        };

        if (property != null)
        {
            var psh = PropertySystem.Property.TryGetSearchHandler(property.Type);
            if (psh != null)
            {
                filter.AvailableOperations = psh.SearchOperations.Keys.ToList();
                filter.Property = property.ToViewModel(propertyLocalizer);
                if (filter.Operation.HasValue)
                {
                    var convertProperty =
                        psh.SearchOperations.GetValueOrDefault(filter.Operation.Value)?.ConvertProperty;
                    var valueProperty = property;
                    if (convertProperty != null)
                    {
                        valueProperty = convertProperty(valueProperty);
                    }

                    filter.ValueProperty = valueProperty.ToViewModel(propertyLocalizer);
                    var asType = psh.SearchOperations.GetValueOrDefault(filter.Operation.Value)?.AsType;
                    if (asType.HasValue)
                    {
                        var dbValue = model.Value?.DeserializeAsStandardValue(asType.Value.GetDbValueType());
                        var pd = PropertySystem.Property.TryGetDescriptor(valueProperty.Type);
                        filter.BizValue = pd?.GetBizValue(valueProperty, dbValue)
                            ?.SerializeAsStandardValue(asType.Value.GetBizValueType());
                    }
                }
            }
        }

        return filter;
    }

    private static ResourceSearchFilterGroupViewModel ToViewModel(this ResourceSearchFilterGroupDbModel model,
        Dictionary<PropertyPool, Dictionary<int, Property>> propertyMap, IPropertyLocalizer propertyLocalizer)
    {
        return new ResourceSearchFilterGroupViewModel
        {
            Groups = model.Groups?.Select(g => g.ToViewModel(propertyMap, propertyLocalizer)).ToList(),
            Filters = model.Filters?.Select(f =>
            {
                if (f is {PropertyPool: not null, PropertyId: not null})
                {
                    var property = propertyMap.GetValueOrDefault(f.PropertyPool.Value)
                        ?.GetValueOrDefault(f.PropertyId.Value);
                    return f.ToViewModel(property, propertyLocalizer);
                }

                return f.ToViewModel(null, propertyLocalizer);
            }).ToList(),
            Combinator = model.Combinator,
            Disabled = model.Disabled
        };
    }

    public static async Task<List<ResourceSearchViewModel>> ToViewModels(this IEnumerable<ResourceSearchDbModel> models,
        IPropertyService propertyService, IPropertyLocalizer propertyLocalizer, IResourceService? resourceService = null)
    {
        var modelsArray = models.ToArray();
        var validFilters = modelsArray.SelectMany(m => m.Group?.ExtractFilters() ?? []).ToList();
        var propertyPools =
            validFilters.Aggregate<ResourceSearchFilterDbModel, PropertyPool>(default,
                (current, f) => current | f.PropertyPool!.Value);

        var propertyMap = (await propertyService.GetProperties(propertyPools)).GroupBy(d => d.Pool)
            .ToDictionary(d => d.Key, d => d.ToDictionary(a => a.Id, a => a));

        // Build ParentResource property with choices if needed
        Property? parentResourcePropertyWithChoices = null;
        if (resourceService != null &&
            propertyMap.TryGetValue(PropertyPool.Internal, out var internalProps) &&
            internalProps.TryGetValue((int)InternalProperty.ParentResource, out var parentResourceProperty))
        {
            var filterData = validFilters
                .Where(f => f.PropertyPool.HasValue && f.PropertyId.HasValue)
                .Select(f => new FilterData(f.PropertyPool!.Value, f.PropertyId!.Value, f.Value, f.Operation));

            parentResourcePropertyWithChoices = await filterData.BuildParentResourcePropertyWithChoices(
                parentResourceProperty, resourceService);

            // Update the local property map copy (safe because propertyMap is a new dictionary)
            if (parentResourcePropertyWithChoices != null)
            {
                internalProps[(int)InternalProperty.ParentResource] = parentResourcePropertyWithChoices;
            }
        }

        var viewModels = new List<ResourceSearchViewModel>();
        foreach (var model in modelsArray)
        {
            var viewModel = new ResourceSearchViewModel
            {
                Keyword = model.Keyword,
                PageSize = model.PageSize,
                Page = model.Page,
                Group = model.Group?.ToViewModel(propertyMap, propertyLocalizer),
                Orders = model.Orders,
                Tags = model.Tags
            };
            viewModels.Add(viewModel);
        }

        return viewModels;
    }

    public static ResourceSearchFilterGroupViewModel ToViewModel(this ResourceSearchFilterGroup domainModel,
        IPropertyLocalizer propertyLocalizer)
    {
        return new ResourceSearchFilterGroupViewModel
        {
            Combinator = domainModel.Combinator,
            Disabled = domainModel.Disabled,
            Filters = domainModel.Filters?.Select(f => f.ToViewModel(propertyLocalizer)).ToList(),
            Groups = domainModel.Groups?.Select(g => g.ToViewModel(propertyLocalizer)).ToList()
        };
    }

    public static ResourceSearchFilterViewModel ToViewModel(this ResourceSearchFilter domainModel,
        IPropertyLocalizer propertyLocalizer)
    {
        var valueProperty = domainModel.Property.ConvertPropertyIfNecessary(domainModel.Operation);

        var filter = new ResourceSearchFilterViewModel
        {
            PropertyId = domainModel.PropertyId,
            PropertyPool = domainModel.PropertyPool,
            Operation = domainModel.Operation,
            Disabled = domainModel.Disabled,
            DbValue = domainModel.DbValue?.SerializeAsStandardValue(valueProperty.Type.GetDbValueType()),
            ValueProperty = valueProperty.ToViewModel(propertyLocalizer),
            Property = domainModel.Property.ToViewModel(propertyLocalizer),
            BizValue = PropertySystem.Property.TryGetDescriptor(valueProperty.Type)?.GetBizValue(valueProperty, domainModel.DbValue)
                ?.SerializeAsStandardValue(valueProperty.Type.GetBizValueType()),
            AvailableOperations = PropertySystem.Property.TryGetSearchHandler(domainModel.Property.Type)?.SearchOperations.Keys.ToList()
        };
        
        return filter;
    }
    
    public static Property ConvertPropertyIfNecessary(this Property property, SearchOperation operation)
    {
        var psh = PropertySystem.Property.TryGetSearchHandler(property.Type);
        if (psh != null && psh.SearchOperations.TryGetValue(operation, out var conversion) && conversion is
            {
                ConvertProperty: not null
            })
        {
            return conversion.ConvertProperty(property);
        }

        return property;
    }

    public static ResourceSearchViewModel ToViewModel(this ResourceSearch domainModel,
        IPropertyLocalizer propertyLocalizer)
    {
        return new ResourceSearchViewModel
        {
            Group = domainModel.Group?.ToViewModel(propertyLocalizer),
            Keyword = null,
            Orders = domainModel.Orders,
            Page = domainModel.PageIndex,
            PageSize = domainModel.PageSize,
            Tags = domainModel.Tags
        };
    }

    /// <summary>
    /// Async version that populates ParentResource property options with resource display names.
    /// </summary>
    public static async Task<ResourceSearchViewModel> ToViewModelAsync(this ResourceSearch domainModel,
        IPropertyService propertyService, IPropertyLocalizer propertyLocalizer, IResourceService resourceService)
    {
        // Extract all filters from the search group
        var validFilters = domainModel.Group?.ExtractFilters() ?? [];

        // Build property map
        var propertyPools = validFilters.Aggregate<ResourceSearchFilter, PropertyPool>(default,
            (current, f) => current | f.PropertyPool);

        if (propertyPools == default)
        {
            return domainModel.ToViewModel(propertyLocalizer);
        }

        var propertyMap = (await propertyService.GetProperties(propertyPools)).GroupBy(d => d.Pool)
            .ToDictionary(d => d.Key, d => d.ToDictionary(a => a.Id, a => a));

        // Build ParentResource property with choices if needed
        if (propertyMap.TryGetValue(PropertyPool.Internal, out var internalProps) &&
            internalProps.TryGetValue((int)InternalProperty.ParentResource, out var parentResourceProperty))
        {
            var filterData = validFilters
                .Where(f => f.PropertyPool == PropertyPool.Internal && f.PropertyId == (int)InternalProperty.ParentResource)
                .Select(f => new FilterData(f.PropertyPool, f.PropertyId,
                    f.DbValue?.SerializeAsStandardValue(f.Property.Type.GetDbValueType()), f.Operation));

            var parentResourcePropertyWithChoices = await filterData.BuildParentResourcePropertyWithChoices(
                parentResourceProperty, resourceService);

            if (parentResourcePropertyWithChoices != null)
            {
                internalProps[(int)InternalProperty.ParentResource] = parentResourcePropertyWithChoices;
            }
        }

        return new ResourceSearchViewModel
        {
            Group = domainModel.Group?.ToViewModel(propertyMap, propertyLocalizer),
            Keyword = null,
            Orders = domainModel.Orders,
            Page = domainModel.PageIndex,
            PageSize = domainModel.PageSize,
            Tags = domainModel.Tags
        };
    }

    private static ResourceSearchFilterGroupViewModel ToViewModel(this ResourceSearchFilterGroup domainModel,
        Dictionary<PropertyPool, Dictionary<int, Property>> propertyMap, IPropertyLocalizer propertyLocalizer)
    {
        return new ResourceSearchFilterGroupViewModel
        {
            Combinator = domainModel.Combinator,
            Disabled = domainModel.Disabled,
            Filters = domainModel.Filters?.Select(f =>
            {
                var property = propertyMap.GetValueOrDefault(f.PropertyPool)?.GetValueOrDefault(f.PropertyId);
                return f.ToViewModel(property ?? f.Property, propertyLocalizer);
            }).ToList(),
            Groups = domainModel.Groups?.Select(g => g.ToViewModel(propertyMap, propertyLocalizer)).ToList()
        };
    }

    private static ResourceSearchFilterViewModel ToViewModel(this ResourceSearchFilter domainModel,
        Property property, IPropertyLocalizer propertyLocalizer)
    {
        var valueProperty = property.ConvertPropertyIfNecessary(domainModel.Operation);

        var filter = new ResourceSearchFilterViewModel
        {
            PropertyId = domainModel.PropertyId,
            PropertyPool = domainModel.PropertyPool,
            Operation = domainModel.Operation,
            Disabled = domainModel.Disabled,
            DbValue = domainModel.DbValue?.SerializeAsStandardValue(valueProperty.Type.GetDbValueType()),
            ValueProperty = valueProperty.ToViewModel(propertyLocalizer),
            Property = property.ToViewModel(propertyLocalizer),
            BizValue = PropertySystem.Property.TryGetDescriptor(valueProperty.Type)?.GetBizValue(valueProperty, domainModel.DbValue)
                ?.SerializeAsStandardValue(valueProperty.Type.GetBizValueType()),
            AvailableOperations = PropertySystem.Property.TryGetSearchHandler(property.Type)?.SearchOperations.Keys.ToList()
        };

        return filter;
    }

    public static ResourceProfileViewModel ToViewModel(this ResourceProfile profile,
        IPropertyLocalizer propertyLocalizer)
    {
        return new ResourceProfileViewModel
        {
            Id = profile.Id,
            Name = profile.Name,
            Search = profile.Search.Group != null ? profile.Search.ToViewModel(propertyLocalizer) : null,
            NameTemplate = profile.NameTemplate,
            EnhancerOptions = profile.EnhancerOptions,
            PlayableFileOptions = profile.PlayableFileOptions,
            PlayerOptions = profile.PlayerOptions,
            PropertyOptions = profile.PropertyOptions,
            Priority = profile.Priority,
            CreatedAt = profile.CreatedAt,
            UpdatedAt = profile.UpdatedAt
        };
    }
}