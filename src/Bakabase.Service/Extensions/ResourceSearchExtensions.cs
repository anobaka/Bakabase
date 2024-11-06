using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Extensions;

namespace Bakabase.Service.Extensions;

public static class ResourceSearchExtensions
{
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
            PageSize = model.PageSize
        };
    }

    private static bool IsValid(this ResourceSearchFilterInputModel filter)
    {
        return filter is {PropertyId: not null, PropertyPool: not null, Operation: not null};
    }

    private static ResourceSearchFilterGroup? ToDomainModel(this ResourceSearchFilterGroupInputModel group,
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

            return new ResourceSearchFilter
            {
                DbValue = f.DbValue?.DeserializeAsStandardValue(property.Type.GetDbValueType()),
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
            PageSize = model.PageSize
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
                if (PropertyInternals.PropertySearchHandlerMap.TryGetValue(p.Type, out var pd))
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

    private static ResourceSearchFilterViewModel ToViewModel(this ResourceSearchFilterDbModel model, Property? property,
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
            var psh = PropertyInternals.PropertySearchHandlerMap.GetValueOrDefault(property.Type);
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
                        var pd = PropertyInternals.DescriptorMap.GetValueOrDefault(valueProperty.Type);
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
        IPropertyService propertyService, IPropertyLocalizer propertyLocalizer)
    {
        var modelsArray = models.ToArray();
        var validFilters = modelsArray.SelectMany(m => m.Group?.ExtractFilters() ?? []).ToList();
        var propertyPools =
            validFilters.Aggregate<ResourceSearchFilterDbModel, PropertyPool>(default,
                (current, f) => current | f.PropertyPool!.Value);

        var propertyMap = (await propertyService.GetProperties(propertyPools)).GroupBy(d => d.Pool)
            .ToDictionary(d => d.Key, d => d.ToDictionary(a => a.Id, a => a));
        var viewModels = new List<ResourceSearchViewModel>();
        foreach (var model in modelsArray)
        {
            var viewModel = new ResourceSearchViewModel
            {
                Keyword = model.Keyword,
                PageSize = model.PageSize,
                Page = model.Page,
                Group = model.Group?.ToViewModel(propertyMap, propertyLocalizer),
                Orders = model.Orders
            };
            viewModels.Add(viewModel);
        }

        return viewModels;
    }
}